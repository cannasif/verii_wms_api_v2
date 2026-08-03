using System.Data;
using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Audit.Application;
using verii_wms_api_v2.Modules.Identity.Domain;
using verii_wms_api_v2.Modules.Location.Domain;
using verii_wms_api_v2.Modules.StockBalance.Domain;
using verii_wms_api_v2.Modules.StockMovement.Application;
using verii_wms_api_v2.Modules.StockMovement.Domain;
using verii_wms_api_v2.Modules.WarehouseTransfer.Domain;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;
using WarehouseEntity = verii_wms_api_v2.Modules.Warehouse.Domain.Warehouse;

namespace verii_wms_api_v2.Modules.ProductionTransfer.Application;

public sealed class ProductionTransferTaskService(IUnitOfWork uow, IAuditLogWriter audit, IStockMovementService movements) : IProductionTransferTaskService
{
    private static readonly WarehouseTransferBusinessContext[] Contexts =
    [
        WarehouseTransferBusinessContext.ProductionMaterialSupply,
        WarehouseTransferBusinessContext.ProductionWipMove,
        WarehouseTransferBusinessContext.ProductionOutputMove
    ];

    public Task<ProductionTransferTaskBoardDto> GetBoardAsync(long transferId, CancellationToken ct = default) => MapAsync(transferId, ct);

    public async Task<IReadOnlyList<ProductionTransferTaskPoolRow>> GetPoolAsync(long actor, CancellationToken ct = default)
    {
        var warehouseIds = await uow.Repository<UserWarehouseAssignment>().Query()
            .Where(x => x.UserId == actor).Select(x => x.WarehouseId).Distinct().ToArrayAsync(ct);
        var query = uow.Repository<WarehouseTransferTask>().Query()
            .Where(x => Contexts.Contains(x.Header.BusinessContext));
        if (warehouseIds.Length > 0) query = query.Where(x => warehouseIds.Contains(x.WarehouseId));
        var rows = await query
            .Include(x => x.Header)
            .Include(x => x.Lines)
            .Include(x => x.Assignments)
            .Where(x => x.Status != WarehouseTransferTaskStatus.Cancelled)
            .OrderBy(x => x.Status == WarehouseTransferTaskStatus.Completed)
            .ThenByDescending(x => x.Id)
            .Take(500)
            .ToListAsync(ct);
        var userIds = rows.SelectMany(x => x.Assignments.Where(a => !a.IsDeleted)).Select(x => x.UserId).Distinct().ToArray();
        var users = await uow.Repository<User>().Query().Where(x => userIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Username, ct);
        return rows.Select(x => new ProductionTransferTaskPoolRow(
            x.WtHeaderId, x.Header.DocumentNo, x.Header.BusinessContext, x.Header.Status,
            x.Id, x.TaskNo, x.TaskType, x.WarehouseId, x.Status,
            x.Lines.Sum(line => line.PlannedQuantity), x.Lines.Sum(line => line.ProcessedQuantity),
            x.Lines.Sum(line => Math.Max(0, line.PlannedQuantity - line.ProcessedQuantity)),
            x.Assignments.Where(a => !a.IsDeleted)
                .OrderByDescending(a => a.IsPrimary)
                .Select(a => users.GetValueOrDefault(a.UserId, $"Kullanıcı #{a.UserId}"))
                .ToArray(),
            x.CreatedDate)).ToArray();
    }

    public Task<ProductionTransferTaskBoardDto> AssignAsync(long transferId, long taskId, AssignProductionTransferTaskRequest request, long actor, CancellationToken ct = default) =>
        uow.ExecuteInTransactionAsync(async token =>
        {
            var task = await LoadTaskAsync(transferId, taskId, token);
            if (task.Status is WarehouseTransferTaskStatus.Completed or WarehouseTransferTaskStatus.Cancelled)
                throw AppException.Conflict("Tamamlanmış veya iptal edilmiş göreve atama yapılamaz.");
            var user = await uow.Repository<User>().Query().SingleOrDefaultAsync(x => x.Id == request.UserId && x.IsActive, token)
                ?? throw AppException.BadRequest("Atanacak kullanıcı bulunamadı veya aktif değil.");
            var hasWarehouseAssignments = await uow.Repository<UserWarehouseAssignment>().Query().AnyAsync(x => x.UserId == user.Id, token);
            if (hasWarehouseAssignments && !await uow.Repository<UserWarehouseAssignment>().Query()
                    .AnyAsync(x => x.UserId == user.Id && x.WarehouseId == task.WarehouseId, token))
                throw AppException.BadRequest("Kullanıcı bu görevin deposuna atanmış bir depo çalışanı değildir.");
            if (task.Assignments.Any(x => !x.IsDeleted && x.UserId == user.Id)) return await MapAsync(transferId, token);
            if (request.IsPrimary) foreach (var assignment in task.Assignments.Where(x => !x.IsDeleted)) assignment.IsPrimary = false;
            var hasActiveAssignment = task.Assignments.Any(x => !x.IsDeleted);
            task.Assignments.Add(new WarehouseTransferTaskAssignment
            {
                BranchCode = task.BranchCode, Task = task, UserId = user.Id, IsPrimary = request.IsPrimary || !hasActiveAssignment,
                AssignedAtUtc = DateTimeOffset.UtcNow, AssignedBy = actor,
                CreatedBy = actor, CreatedDate = DateTime.UtcNow
            });
            if (task.Status == WarehouseTransferTaskStatus.Open) task.Status = WarehouseTransferTaskStatus.Assigned;
            task.UpdatedBy = actor; task.UpdatedDate = DateTime.UtcNow;
            await uow.SaveChangesAsync(token);
            await audit.WriteAsync(new("production-transfer.task.assign", nameof(WarehouseTransferTask), task.Id.ToString(), "Succeeded", "production-transfer",
                NewValues: new { TransferId = transferId, TaskId = task.Id, UserId = user.Id }, ChangedFields: ["Assignments", "Status"]), token);
            return await MapAsync(transferId, token);
        }, ct, IsolationLevel.Serializable);

    public Task<ProductionTransferTaskBoardDto> RemoveAssignmentAsync(long transferId, long taskId, long userId, long actor, CancellationToken ct = default) =>
        uow.ExecuteInTransactionAsync(async token =>
        {
            var task = await LoadTaskAsync(transferId, taskId, token);
            var assignment = task.Assignments.SingleOrDefault(x => !x.IsDeleted && x.UserId == userId)
                ?? throw AppException.NotFound("Görev ataması bulunamadı.");
            if (task.Lines.Any(x => x.ProcessedQuantity > 0))
                throw AppException.Conflict("Stok toplamış görevden atama kaldırılamaz. Önce toplanan stoklar yerine konmalıdır.");
            assignment.IsDeleted = true; assignment.DeletedBy = actor; assignment.DeletedDate = DateTime.UtcNow;
            var remainingAssignments = task.Assignments.Where(x => !x.IsDeleted && x.Id != assignment.Id).ToList();
            if (task.StartedBy == userId || task.AcceptedBy == userId)
            {
                task.StartedAtUtc = null; task.StartedBy = null;
                task.AcceptedAtUtc = null; task.AcceptedBy = null;
            }
            if (remainingAssignments.Count == 0) task.Status = WarehouseTransferTaskStatus.Open;
            else if (task.Status is WarehouseTransferTaskStatus.Accepted or WarehouseTransferTaskStatus.InProgress)
                task.Status = WarehouseTransferTaskStatus.Assigned;
            if (assignment.IsPrimary && remainingAssignments.Count > 0)
                remainingAssignments.OrderBy(x => x.AssignedAtUtc).First().IsPrimary = true;
            task.UpdatedBy = actor; task.UpdatedDate = DateTime.UtcNow;
            await uow.SaveChangesAsync(token);
            await audit.WriteAsync(new("production-transfer.task.unassign", nameof(WarehouseTransferTask), task.Id.ToString(), "Succeeded", "production-transfer",
                NewValues: new { TransferId = transferId, TaskId = task.Id, UserId = userId }, ChangedFields: ["Assignments", "Status"]), token);
            return await MapAsync(transferId, token);
        }, ct, IsolationLevel.Serializable);

    public Task<ProductionTransferTaskBoardDto> AcceptAndStartAsync(long transferId, long taskId, long actor, CancellationToken ct = default) =>
        uow.ExecuteInTransactionAsync(async token =>
        {
            var task = await LoadTaskAsync(transferId, taskId, token);
            if (task.Status is WarehouseTransferTaskStatus.Completed or WarehouseTransferTaskStatus.Cancelled)
                throw AppException.Conflict("Tamamlanmış veya iptal edilmiş görev başlatılamaz.");
            var assignment = task.Assignments.SingleOrDefault(x => !x.IsDeleted && x.UserId == actor)
                ?? throw AppException.Forbidden("Bu görev size atanmamış.");
            var now = DateTimeOffset.UtcNow;
            assignment.AcceptedAtUtc ??= now;
            task.AcceptedAtUtc ??= now; task.AcceptedBy ??= actor;
            task.StartedAtUtc ??= now; task.StartedBy ??= actor;
            task.Status = WarehouseTransferTaskStatus.InProgress;
            task.UpdatedBy = actor; task.UpdatedDate = DateTime.UtcNow;
            await uow.SaveChangesAsync(token);
            await audit.WriteAsync(new("production-transfer.task.start", nameof(WarehouseTransferTask), task.Id.ToString(), "Succeeded", "production-transfer",
                NewValues: new { TransferId = transferId, TaskId = task.Id, UserId = actor, StartedAtUtc = now }, ChangedFields: ["AcceptedAtUtc", "StartedAtUtc", "Status"]), token);
            return await MapAsync(transferId, token);
        }, ct, IsolationLevel.Serializable);

    public Task<ProductionTransferTaskBoardDto> CompleteCancellationReturnAsync(
        long transferId, long taskId, Guid idempotencyKey, long actor, CancellationToken ct = default) =>
        uow.ExecuteInTransactionAsync(async token =>
        {
            if (idempotencyKey == Guid.Empty) throw AppException.BadRequest("İdempotency anahtarı zorunludur.");
            var task = await LoadTaskAsync(transferId, taskId, token);
            if (task.TaskType != WarehouseTransferTaskType.CancellationReturn)
                throw AppException.BadRequest("Seçilen görev bir iptal iade görevi değildir.");
            if (task.Status == WarehouseTransferTaskStatus.Completed) return await MapAsync(transferId, token);
            if (task.Status != WarehouseTransferTaskStatus.InProgress || task.StartedBy != actor
                || !task.Assignments.Any(x => !x.IsDeleted && x.UserId == actor && x.AcceptedAtUtc.HasValue))
                throw AppException.Conflict("İade görevini tamamlamadan önce 'Bu işi yapıyorum' işlemini kullanın.");

            var movementLines = BuildReturnMovementLines(task);
            long? operationId = null;
            if (movementLines.Count > 0)
            {
                var movement = await movements.PostAsync(new(
                    $"WT:{transferId}:CANCEL-RETURN:{idempotencyKey:N}", StockMovementTypes.Transfer,
                    "WarehouseTransferCancellationReturn", task.Header.DocumentNo, transferId, DateTime.UtcNow,
                    "İptal edilen üretim transferinin fiziksel iadesi",
                    $"{task.Header.DocumentNo} iptal iade görevi tamamlandı", movementLines), token);
                operationId = movement.OperationId;
            }
            var now = DateTimeOffset.UtcNow;
            foreach (var line in task.Lines) line.ProcessedQuantity = line.PlannedQuantity;
            task.Status = WarehouseTransferTaskStatus.Completed;
            task.CompletedAtUtc = now; task.CompletedBy = actor;
            task.UpdatedBy = actor; task.UpdatedDate = DateTime.UtcNow;
            await uow.SaveChangesAsync(token);
            await audit.WriteAsync(new("production-transfer.task.cancellation-return.complete", nameof(WarehouseTransferTask), task.Id.ToString(), "Succeeded", "production-transfer",
                NewValues: new { TransferId = transferId, TaskId = task.Id, StockMovementOperationId = operationId }, ChangedFields: ["ProcessedQuantity", "Status"]), token);
            return await MapAsync(transferId, token);
        }, ct, IsolationLevel.Serializable);

    public async Task<WarehouseTransferReturnSettingDto> GetReturnSettingAsync(long warehouseId, CancellationToken ct = default)
    {
        var row = await uow.Repository<WarehouseEntity>().Query().Where(x => x.Id == warehouseId)
            .Select(x => new WarehouseTransferReturnSettingDto(x.Id, x.DefaultTransferReturnLocationId)).SingleOrDefaultAsync(ct);
        return row ?? throw AppException.NotFound("Depo bulunamadı.");
    }

    public Task<WarehouseTransferReturnSettingDto> UpdateReturnSettingAsync(UpdateWarehouseTransferReturnSettingRequest request, long actor, CancellationToken ct = default) =>
        uow.ExecuteInTransactionAsync(async token =>
        {
            var warehouse = await uow.Repository<WarehouseEntity>().FindByIdAsync(request.WarehouseId, tracking: true, cancellationToken: token)
                ?? throw AppException.NotFound("Depo bulunamadı.");
            if (request.DefaultTransferReturnLocationId.HasValue)
            {
                var valid = await uow.Repository<WarehouseLocation>().Query().AnyAsync(x =>
                    x.Id == request.DefaultTransferReturnLocationId && x.WarehouseId == warehouse.Id && x.IsActive && x.IsPutaway, token);
                if (!valid) throw AppException.BadRequest("Varsayılan iade rafı depoya ait, aktif ve yerleştirmeye uygun olmalıdır.");
            }
            warehouse.DefaultTransferReturnLocationId = request.DefaultTransferReturnLocationId;
            warehouse.UpdatedBy = actor; warehouse.UpdatedDate = DateTime.UtcNow;
            await uow.SaveChangesAsync(token);
            await audit.WriteAsync(new("production-transfer.return-location.update", nameof(WarehouseEntity), warehouse.Id.ToString(), "Succeeded", "production-transfer",
                NewValues: new { warehouse.DefaultTransferReturnLocationId }, ChangedFields: ["DefaultTransferReturnLocationId"]), token);
            return new WarehouseTransferReturnSettingDto(warehouse.Id, warehouse.DefaultTransferReturnLocationId);
        }, ct, IsolationLevel.Serializable);

    private async Task<WarehouseTransferTask> LoadTaskAsync(long transferId, long taskId, CancellationToken ct) =>
        await uow.Repository<WarehouseTransferTask>().Query(true)
            .Include(x => x.Header).Include(x => x.Assignments)
            .Include(x => x.Lines).ThenInclude(x => x.Line).ThenInclude(x => x.Trackings)
            .SingleOrDefaultAsync(x => x.Id == taskId && x.WtHeaderId == transferId && Contexts.Contains(x.Header.BusinessContext), ct)
        ?? throw AppException.NotFound("Üretim transfer görevi bulunamadı.");

    private async Task<ProductionTransferTaskBoardDto> MapAsync(long transferId, CancellationToken ct)
    {
        var header = await uow.Repository<WarehouseTransferHeader>().Query()
            .Include(x => x.Tasks).ThenInclude(x => x.Assignments)
            .Include(x => x.Tasks).ThenInclude(x => x.Lines).ThenInclude(x => x.Line).ThenInclude(x => x.Trackings)
            .SingleOrDefaultAsync(x => x.Id == transferId && Contexts.Contains(x.BusinessContext), ct)
            ?? throw AppException.NotFound("Üretim transferi bulunamadı.");
        var userIds = header.Tasks.SelectMany(x => x.Assignments.Where(a => !a.IsDeleted)).Select(x => x.UserId).Distinct().ToArray();
        var users = await uow.Repository<User>().Query().Where(x => userIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Username, ct);
        var locationIds = header.Tasks.SelectMany(x => x.Lines)
            .SelectMany(x => x.SourceLocationId.HasValue
                ? new[] { x.SourceLocationId.Value }
                : x.Line.Trackings.Where(t => t.SourceLocationId.HasValue).Select(t => t.SourceLocationId!.Value))
            .Distinct().ToArray();
        var locations = await uow.Repository<WarehouseLocation>().Query().Where(x => locationIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => new { x.Code, x.Name }, ct);
        var balances = await uow.Repository<LocationStockBalance>().Query()
            .Where(x => x.WarehouseId == header.SourceWarehouseId && locationIds.Contains(x.LocationId))
            .GroupBy(x => new { x.LocationId, x.StockId, x.YapCodeId })
            .Select(x => new { x.Key, Quantity = x.Sum(v => v.AvailableQuantity) }).ToListAsync(ct);

        var tasks = header.Tasks.OrderBy(x => x.Id).Select(task => new ProductionTransferTaskDto(
            task.Id, task.TaskNo, task.TaskType, task.WarehouseId, task.Status, task.AcceptedAtUtc, task.AcceptedBy, task.StartedAtUtc, task.StartedBy,
            task.Assignments.Where(x => !x.IsDeleted).OrderByDescending(x => x.IsPrimary).Select(x => new ProductionTransferTaskAssignmentDto(
                x.UserId, users.GetValueOrDefault(x.UserId, $"Kullanıcı #{x.UserId}"), x.IsPrimary, x.AssignedAtUtc, x.AcceptedAtUtc)).ToList(),
            task.Lines.OrderBy(x => x.Id).Select(x =>
            {
                var available = x.SourceLocationId.HasValue
                    ? balances.Where(v => v.Key.LocationId == x.SourceLocationId && v.Key.StockId == x.Line.StockId && v.Key.YapCodeId == x.Line.YapCodeId).Sum(v => v.Quantity)
                    : 0m;
                var covered = task.TaskType == WarehouseTransferTaskType.CancellationReturn
                    ? x.PlannedQuantity
                    : Math.Min(x.PlannedQuantity, Math.Max(x.Line.ReservedQuantity, Math.Min(x.PlannedQuantity, available)));
                var lineLocationIds = x.SourceLocationId.HasValue
                    ? new[] { x.SourceLocationId.Value }
                    : x.Line.Trackings.Where(t => t.SourceLocationId.HasValue).Select(t => t.SourceLocationId!.Value).Distinct().ToArray();
                var lineLocations = lineLocationIds.Where(locations.ContainsKey).Select(id => locations[id]).ToArray();
                return new ProductionTransferTaskLineDto(x.Id, x.WtLineId, x.Line.StockCodeSnapshot, x.Line.StockNameSnapshot,
                    x.PlannedQuantity, covered, Math.Max(0, x.PlannedQuantity - covered), x.ProcessedQuantity,
                    x.SourceLocationId,
                    lineLocations.Length == 0 ? null : string.Join(", ", lineLocations.Select(v => v.Code).Distinct()),
                    lineLocations.Length == 0 ? null : string.Join(", ", lineLocations.Select(v => v.Name).Distinct()));
            }).ToList())).ToList();
        var workloadRows = await uow.Repository<WarehouseTransferTask>().Query()
            .Where(x => Contexts.Contains(x.Header.BusinessContext) && x.BranchCode == header.BranchCode)
            .SelectMany(x => x.Assignments.Where(a => !a.IsDeleted).Select(a => new
            {
                a.UserId,
                TaskId = x.Id,
                x.Status
            }))
            .ToListAsync(ct);
        var workloadUserIds = workloadRows.Select(x => x.UserId).Distinct().ToArray();
        var workloadUsers = await uow.Repository<User>().Query()
            .Where(x => workloadUserIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Username, ct);
        var workloads = workloadRows.GroupBy(x => x.UserId).Select(group =>
        {
            var assigned = group.Select(x => x.TaskId).Distinct().Count();
            var completed = group.Where(x => x.Status == WarehouseTransferTaskStatus.Completed).Select(x => x.TaskId).Distinct().Count();
            return new ProductionTransferWorkloadDto(group.Key, workloadUsers.GetValueOrDefault(group.Key, $"Kullanıcı #{group.Key}"), assigned, completed,
                assigned == 0 ? 0 : decimal.Round(completed * 100m / assigned, 2));
        }).OrderBy(x => x.Username).ToList();
        var activeUsers = await uow.Repository<User>().Query().Where(user => user.IsActive)
            .OrderBy(user => user.Username).Select(user => new { user.Id, user.Username }).ToListAsync(ct);
        var activeUserIds = activeUsers.Select(x => x.Id).ToArray();
        var warehouseAssignments = await uow.Repository<UserWarehouseAssignment>().Query()
            .Where(x => activeUserIds.Contains(x.UserId)).Select(x => new { x.UserId, x.WarehouseId }).ToListAsync(ct);
        var eligibleAssignees = activeUsers.Select(user => new ProductionTransferAssigneeOptionDto(
            user.Id, user.Username, warehouseAssignments.Where(x => x.UserId == user.Id).Select(x => x.WarehouseId).Distinct().ToArray())).ToList();
        return new(header.Id, header.DocumentNo, header.Status, header.SourceWarehouseId, tasks, workloads, eligibleAssignees);
    }

    private static IReadOnlyList<StockMovementLineRequest> BuildReturnMovementLines(WarehouseTransferTask task)
    {
        var rows = new List<StockMovementLineRequest>();
        foreach (var taskLine in task.Lines)
        {
            var line = taskLine.Line;
            var tracked = line.Trackings.Where(x => x.PickedQuantity > 0).ToList();
            if (tracked.Count == 0)
            {
                var source = line.DefaultSourceLocationId
                    ?? throw AppException.Conflict($"{line.StockCodeSnapshot} için özgün kaynak raf bulunamadı.");
                var target = taskLine.TargetLocationId ?? source;
                if (source != target) rows.Add(new(line.StockId, line.YapCodeId, taskLine.PlannedQuantity,
                    task.Header.SourceWarehouseId, source, task.Header.SourceWarehouseId, target,
                    line.UnitCode, null, null, null, line.SourceStockStatus, line.SourceStockStatus));
                continue;
            }
            foreach (var tracking in tracked)
            {
                var source = tracking.SourceLocationId ?? line.DefaultSourceLocationId
                    ?? throw AppException.Conflict($"{line.StockCodeSnapshot} seri/lot satırı için özgün kaynak raf bulunamadı.");
                var target = taskLine.TargetLocationId ?? source;
                if (source != target) rows.Add(new(line.StockId, line.YapCodeId, tracking.PickedQuantity,
                    task.Header.SourceWarehouseId, source, task.Header.SourceWarehouseId, target,
                    line.UnitCode, tracking.LotNo, tracking.SerialNo, null, line.SourceStockStatus, line.SourceStockStatus));
            }
        }
        return rows;
    }
}
