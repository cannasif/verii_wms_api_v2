using System.Data;
using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Audit.Application;
using verii_wms_api_v2.Modules.Identity.Domain;
using verii_wms_api_v2.Modules.ProductionTransfer.Domain;
using verii_wms_api_v2.Modules.Location.Domain;
using verii_wms_api_v2.Modules.StockBalance.Domain;
using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using verii_wms_api_v2.Modules.StockMovement.Application;
using verii_wms_api_v2.Modules.StockMovement.Domain;
using verii_wms_api_v2.Modules.WarehouseTransfer.Application;
using verii_wms_api_v2.Modules.WarehouseTransfer.Domain;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;
using WarehouseEntity = verii_wms_api_v2.Modules.Warehouse.Domain.Warehouse;

namespace verii_wms_api_v2.Modules.ProductionTransfer.Application;

public sealed class ProductionTransferTaskService(
    IUnitOfWork uow,
    IAuditLogWriter audit,
    IStockMovementService movements,
    IWarehouseTransferReservationService reservations) : IProductionTransferTaskService
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

    public async Task<IReadOnlyList<ProductionWorkOrderTransferHeaderRowDto>> GetWorkOrderTransferGroupsAsync(
        ProductionWorkOrderTransferTab tab,
        string? search,
        CancellationToken ct = default)
    {
        var links = await uow.Repository<ProductionTransferHeaderLink>().Query()
            .Include(x => x.WarehouseTransferHeader).ThenInclude(h => h.Tasks).ThenInclude(t => t.Lines)
            .Include(x => x.WarehouseTransferHeader).ThenInclude(h => h.Tasks).ThenInclude(t => t.Assignments)
            .Include(x => x.WarehouseTransferHeader).ThenInclude(h => h.Lines)
            .Where(x => Contexts.Contains(x.WarehouseTransferHeader.BusinessContext))
            .OrderByDescending(x => x.WarehouseTransferHeader.CreatedDate)
            .Take(1000)
            .ToListAsync(ct);

        if (links.Count == 0) return [];

        var labelContext = ProductionWorkOrderTransferGrouping.BuildLabelContext(links);
        var residualIds = links
            .Where(x => x.ResidualWarehouseTransferHeaderId.HasValue)
            .Select(x => x.ResidualWarehouseTransferHeaderId!.Value)
            .Distinct()
            .ToArray();
        var residualDocs = residualIds.Length == 0
            ? new Dictionary<long, string>()
            : await uow.Repository<WarehouseTransferHeader>().Query()
                .Where(x => residualIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.DocumentNo, ct);

        var warehouseIds = links
            .SelectMany(x => new[] { x.WarehouseTransferHeader.SourceWarehouseId, x.WarehouseTransferHeader.TargetWarehouseId })
            .Distinct()
            .ToArray();
        var warehouses = await uow.Repository<WarehouseEntity>().Query(ignoreQueryFilters: true)
            .Where(x => warehouseIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => new { x.WarehouseCode, x.WarehouseName }, ct);

        var userIds = links
            .SelectMany(x => x.WarehouseTransferHeader.Tasks)
            .SelectMany(x => x.Assignments.Where(a => !a.IsDeleted))
            .Select(x => x.UserId)
            .Distinct()
            .ToArray();
        var users = userIds.Length == 0
            ? new Dictionary<long, string>()
            : await uow.Repository<User>().Query()
                .Where(x => userIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.Username, ct);

        var rows = new List<ProductionWorkOrderTransferHeaderRowDto>();
        foreach (var link in links)
        {
            var header = link.WarehouseTransferHeader;
            if (!ProductionWorkOrderTransferGrouping.MatchesTab(tab, header, link)) continue;
            if (!ProductionWorkOrderTransferGrouping.MatchesSearch(search, header, link)) continue;

            var source = warehouses.GetValueOrDefault(header.SourceWarehouseId);
            var target = warehouses.GetValueOrDefault(header.TargetWarehouseId);
            var activeTasks = header.Tasks.Where(x => !x.IsDeleted).OrderBy(x => x.Id).ToArray();
            var taskRows = activeTasks.Select(task =>
            {
                var planned = task.Lines.Where(x => !x.IsDeleted).Sum(x => x.PlannedQuantity);
                var processed = task.Lines.Where(x => !x.IsDeleted).Sum(x => x.ProcessedQuantity);
                var displaySuffix = ProductionWorkOrderTransferGrouping.GetDisplaySuffix(
                    task, link, labelContext, activeTasks);
                return new ProductionWorkOrderTransferTaskRowDto(
                    task.Id,
                    task.TaskNo,
                    ProductionWorkOrderTransferGrouping.BuildDisplayLabel(task.TaskNo, header.DocumentNo, displaySuffix),
                    displaySuffix,
                    task.TaskType,
                    task.Status,
                    task.WarehouseId,
                    planned,
                    processed,
                    Math.Max(0, planned - processed),
                    task.Assignments.Where(x => !x.IsDeleted)
                        .OrderByDescending(x => x.IsPrimary)
                        .Select(x => users.GetValueOrDefault(x.UserId, $"Kullanıcı #{x.UserId}"))
                        .ToArray(),
                    task.PreviousTaskId,
                    task.OriginTaskId,
                    task.OriginUserId,
                    task.CompletedAtUtc);
            }).ToArray();

            rows.Add(new ProductionWorkOrderTransferHeaderRowDto(
                header.Id,
                header.DocumentNo,
                header.ExternalReferenceNo,
                header.Status,
                link.WorkflowStatus,
                link.ProductionOrderId,
                link.ProductionOrderNo,
                link.ProductionHeaderId,
                link.ParentWarehouseTransferHeaderId,
                link.ResidualWarehouseTransferHeaderId,
                link.ResidualWarehouseTransferHeaderId is long residualId
                    ? residualDocs.GetValueOrDefault(residualId)
                    : null,
                link.ParentWarehouseTransferHeaderId.HasValue,
                header.SourceWarehouseId,
                source?.WarehouseCode ?? 0,
                source?.WarehouseName ?? string.Empty,
                header.TargetWarehouseId,
                target?.WarehouseCode ?? 0,
                target?.WarehouseName ?? string.Empty,
                header.Lines.Where(x => !x.IsDeleted).Sum(x => x.RequestedQuantity),
                header.Lines.Where(x => !x.IsDeleted).Sum(x => x.PickedQuantity),
                header.CreatedDate,
                taskRows));
        }

        return rows;
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
            var previousAssignment = task.Assignments.SingleOrDefault(x => x.UserId == user.Id);
            if (previousAssignment is not null)
            {
                previousAssignment.IsDeleted = false; previousAssignment.DeletedBy = null; previousAssignment.DeletedDate = null;
                previousAssignment.IsPrimary = request.IsPrimary || !hasActiveAssignment;
                previousAssignment.AssignedAtUtc = DateTimeOffset.UtcNow; previousAssignment.AssignedBy = actor;
                previousAssignment.AcceptedAtUtc = null; previousAssignment.UpdatedBy = actor; previousAssignment.UpdatedDate = DateTime.UtcNow;
            }
            else task.Assignments.Add(new WarehouseTransferTaskAssignment
            {
                BranchCode = task.BranchCode, Task = task, UserId = user.Id, IsPrimary = request.IsPrimary || !hasActiveAssignment,
                AssignedAtUtc = DateTimeOffset.UtcNow, AssignedBy = actor,
                CreatedBy = actor, CreatedDate = DateTime.UtcNow
            });
            if (task.Status == WarehouseTransferTaskStatus.Open) task.Status = WarehouseTransferTaskStatus.Assigned;
            task.UpdatedBy = actor; task.UpdatedDate = DateTime.UtcNow;
            try { await uow.SaveChangesAsync(token); }
            catch (DbUpdateException exception) when (
                exception.InnerException?.Message.Contains(
                    "IX_RII_WT_TASK_ASSIGNMENT_WtTaskId_UserId",
                    StringComparison.OrdinalIgnoreCase) == true)
            {
                throw AppException.Conflict(
                    $"{user.Username} bu göreve daha önce atanıp kaldırılmış; bu kaydın arşiv izi veritabanında tutulduğu için şu an yeniden atanamıyor. Lütfen sistem yöneticisine bildirin (Görev #{task.Id}, Kullanıcı #{user.Id}).");
            }
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
            var lineageLines = await GetLineageProcessedLinesAsync(task, token);
            if (lineageLines.Count > 0)
            {
                var completedReturn = await uow.Repository<WarehouseTransferTask>().Query()
                    .AnyAsync(x => x.OriginTaskId == task.Id && x.OriginUserId == userId
                        && x.Status == WarehouseTransferTaskStatus.Completed, token);
                if (!completedReturn)
                    throw AppException.Conflict(
                        "Bu iş emri için toplanmış stok var (devir öncesi dahil). Önce 'İade görevi oluştur' ile tüm stokları eski rafına koydurup onaylatın.");
            }
            RemoveAssignmentCore(task, assignment, actor);
            task.UpdatedBy = actor; task.UpdatedDate = DateTime.UtcNow;
            await uow.SaveChangesAsync(token);
            await audit.WriteAsync(new("production-transfer.task.unassign", nameof(WarehouseTransferTask), task.Id.ToString(), "Succeeded", "production-transfer",
                NewValues: new { TransferId = transferId, TaskId = task.Id, UserId = userId }, ChangedFields: ["Assignments", "Status"]), token);
            return await MapAsync(transferId, token);
        }, ct, IsolationLevel.Serializable);

    private static void RemoveAssignmentCore(WarehouseTransferTask task, WarehouseTransferTaskAssignment assignment, long actor)
    {
        var userId = assignment.UserId;
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
    }

    // Devret zincirini (PreviousTaskId üzerinden) geriye doğru izleyip, zincirdeki TÜM görevlerde
    // işlenmiş satırları toplar. İş emri devredildiğinde bitmiş sayılmadığından, önceki kullanıcının
    // (örn. A) topladığı stok da iadeye dahil edilmeli — sadece güncel görevin kendi satırları yeterli değil.
    private async Task<List<WarehouseTransferTaskLine>> GetLineageProcessedLinesAsync(WarehouseTransferTask task, CancellationToken ct)
    {
        var lines = new List<WarehouseTransferTaskLine>(task.Lines.Where(x => x.ProcessedQuantity > 0));
        var cursor = task;
        while (cursor.PreviousTaskId.HasValue)
        {
            var previous = await uow.Repository<WarehouseTransferTask>().Query()
                .Include(x => x.Lines).ThenInclude(x => x.Line).ThenInclude(x => x.Trackings)
                .SingleOrDefaultAsync(x => x.Id == cursor.PreviousTaskId.Value, ct);
            if (previous is null) break;
            lines.AddRange(previous.Lines.Where(x => x.ProcessedQuantity > 0));
            cursor = previous;
        }
        return lines;
    }

    public Task<ProductionTransferTaskBoardDto> RequestAssignmentReturnAsync(long transferId, long taskId, long userId, long actor, CancellationToken ct = default) =>
        uow.ExecuteInTransactionAsync(async token =>
        {
            var task = await LoadTaskAsync(transferId, taskId, token);
            if (!task.Assignments.Any(x => !x.IsDeleted && x.UserId == userId))
                throw AppException.NotFound("Görev ataması bulunamadı.");
            // Devir zincirindeki (bu görev + tüm önceki görevler) işlenmiş satırlar — en son atanan
            // kullanıcı (userId), kendisininki de dahil olmak üzere TÜM zincirin stoğunu iade eder;
            // önceki kullanıcı (ör. A) kendi topladığını ayrıca iade edemez/etmez.
            var processedLines = await GetLineageProcessedLinesAsync(task, token);
            if (processedLines.Count == 0)
                throw AppException.BadRequest("Bu görev için iade edilecek toplanmış stok bulunmuyor; atama doğrudan kaldırılabilir.");
            var existing = await uow.Repository<WarehouseTransferTask>().Query()
                .SingleOrDefaultAsync(x => x.OriginTaskId == task.Id && x.OriginUserId == userId
                    && x.Status != WarehouseTransferTaskStatus.Cancelled, token);
            if (existing is not null) return await MapAsync(transferId, token);

            var now = DateTime.UtcNow;
            var suffix = await NextRemainderSuffixAsync(task.Header.DocumentNo, token);
            var returnTask = new WarehouseTransferTask
            {
                BranchCode = task.BranchCode, CreatedBy = actor, CreatedDate = now, Header = task.Header,
                TaskNo = $"{task.Header.DocumentNo}-IADE{suffix}", TaskType = WarehouseTransferTaskType.AssignmentReturn,
                WarehouseId = task.WarehouseId, Status = WarehouseTransferTaskStatus.Assigned, Priority = task.Priority,
                OriginTaskId = task.Id, OriginUserId = userId,
                Description = $"{task.TaskNo} görevindeki atamanızın kaldırılabilmesi için topladığınız stokları eski rafına geri koyun."
            };
            returnTask.Assignments.Add(new WarehouseTransferTaskAssignment
            {
                BranchCode = task.BranchCode, Task = returnTask, UserId = userId, IsPrimary = true,
                AssignedAtUtc = DateTimeOffset.UtcNow, AssignedBy = actor, CreatedBy = actor, CreatedDate = now
            });
            // Aynı transfer satırı (WtLineId), devir zincirinde birden fazla görevde işlenmiş
            // olabilir (ör. A kısmen işledi, kalan B'ye devredildi, B de bir kısmını işledi) —
            // bunları tek bir iade satırında topla; konum için zincirdeki en güncel (en yeni
            // görevdeki) kaydı esas al.
            foreach (var group in processedLines.GroupBy(x => x.WtLineId))
            {
                var representative = group.OrderByDescending(x => x.WtTaskId).First();
                var line = representative.Line;
                var totalProcessed = group.Sum(x => x.ProcessedQuantity);
                var originalSources = line.Trackings.Where(x => x.PickedQuantity > 0 && x.SourceLocationId.HasValue)
                    .Select(x => x.SourceLocationId!.Value).Append(line.DefaultSourceLocationId ?? 0).Where(x => x > 0).Distinct().ToArray();
                returnTask.Lines.Add(new WarehouseTransferTaskLine
                {
                    BranchCode = task.BranchCode, CreatedBy = actor, CreatedDate = now, Task = returnTask, Line = line,
                    PlannedQuantity = totalProcessed, ProcessedQuantity = 0,
                    SourceLocationId = representative.TargetLocationId ?? representative.SourceLocationId,
                    TargetLocationId = originalSources.Length == 1 ? originalSources[0] : representative.SourceLocationId
                });
            }
            await uow.Repository<WarehouseTransferTask>().AddAsync(returnTask, token);
            await uow.SaveChangesAsync(token);
            await audit.WriteAsync(new("production-transfer.task.assignment-return.request", nameof(WarehouseTransferTask), task.Id.ToString(), "Succeeded", "production-transfer",
                NewValues: new { TransferId = transferId, OriginTaskId = task.Id, ReturnTaskId = returnTask.Id, returnTask.TaskNo, UserId = userId },
                ChangedFields: ["Lines", "Assignments"]), token);
            return await MapAsync(transferId, token);
        }, ct, IsolationLevel.Serializable);

    public Task<ProductionTransferTaskBoardDto> ProcessReturnTaskLineAsync(
        long transferId, long taskId, long taskLineId, Guid idempotencyKey, long actor, CancellationToken ct = default) =>
        uow.ExecuteInTransactionAsync(async token =>
        {
            if (idempotencyKey == Guid.Empty) throw AppException.BadRequest("İdempotency anahtarı zorunludur.");
            var task = await LoadTaskAsync(transferId, taskId, token);
            if (task.TaskType is not (WarehouseTransferTaskType.AssignmentReturn or WarehouseTransferTaskType.CancellationReturn))
                throw AppException.BadRequest("Seçilen görev bir iade görevi değildir.");
            if (task.Status != WarehouseTransferTaskStatus.InProgress || task.StartedBy != actor
                || !task.Assignments.Any(x => !x.IsDeleted && x.UserId == actor && x.AcceptedAtUtc.HasValue))
                throw AppException.Conflict("İade satırını onaylamadan önce 'Bu işi yapıyorum' işlemini kullanın.");
            var taskLine = task.Lines.SingleOrDefault(x => x.Id == taskLineId && !x.IsDeleted)
                ?? throw AppException.NotFound("İade görev satırı bulunamadı.");
            if (taskLine.ProcessedQuantity >= taskLine.PlannedQuantity)
                return await MapAsync(transferId, token);

            var movementLines = BuildReturnMovementLines(task, taskLine);
            if (movementLines.Count > 0)
            {
                var referenceType = task.TaskType == WarehouseTransferTaskType.AssignmentReturn
                    ? "WarehouseTransferAssignmentReturnLine"
                    : "WarehouseTransferCancellationReturnLine";
                await movements.PostAsync(new(
                    $"WT:{transferId}:RETURN-LINE:{taskLineId}:{idempotencyKey:N}",
                    StockMovementTypes.Transfer,
                    referenceType,
                    task.Header.DocumentNo,
                    transferId,
                    DateTime.UtcNow,
                    "İade görevi satır onayı",
                    $"{task.Header.DocumentNo} iade satırı: {taskLine.Line.StockCodeSnapshot}",
                    movementLines), token);
            }

            taskLine.ProcessedQuantity = taskLine.PlannedQuantity;
            taskLine.UpdatedBy = actor;
            taskLine.UpdatedDate = DateTime.UtcNow;
            task.UpdatedBy = actor;
            task.UpdatedDate = DateTime.UtcNow;
            await uow.SaveChangesAsync(token);
            await audit.WriteAsync(new("production-transfer.task.return-line.process", nameof(WarehouseTransferTaskLine), taskLine.Id.ToString(), "Succeeded", "production-transfer",
                NewValues: new { TransferId = transferId, TaskId = task.Id, TaskLineId = taskLine.Id, taskLine.PlannedQuantity },
                ChangedFields: ["ProcessedQuantity"]), token);
            return await MapAsync(transferId, token);
        }, ct, IsolationLevel.Serializable);

    public Task<ProductionTransferTaskBoardDto> CompleteAssignmentReturnAsync(
        long transferId, long taskId, Guid idempotencyKey, long actor, CancellationToken ct = default) =>
        uow.ExecuteInTransactionAsync(async token =>
        {
            if (idempotencyKey == Guid.Empty) throw AppException.BadRequest("İdempotency anahtarı zorunludur.");
            var task = await LoadTaskAsync(transferId, taskId, token);
            if (task.TaskType != WarehouseTransferTaskType.AssignmentReturn)
                throw AppException.BadRequest("Seçilen görev bir atama iade görevi değildir.");
            if (task.Status == WarehouseTransferTaskStatus.Completed) return await MapAsync(transferId, token);
            if (task.Status != WarehouseTransferTaskStatus.InProgress || task.StartedBy != actor
                || !task.Assignments.Any(x => !x.IsDeleted && x.UserId == actor && x.AcceptedAtUtc.HasValue))
                throw AppException.Conflict("İade görevini tamamlamadan önce 'Bu işi yapıyorum' işlemini kullanın.");
            if (task.Lines.Any(x => x.ProcessedQuantity < x.PlannedQuantity))
                throw AppException.Conflict("Tüm iade satırlarını rafa yerleştirmeden iadeyi tamamlayamazsınız.");

            var movementLines = BuildReturnMovementLines(task);
            long? operationId = null;
            if (movementLines.Count > 0)
            {
                var movement = await movements.PostAsync(new(
                    $"WT:{transferId}:ASSIGN-RETURN:{idempotencyKey:N}", StockMovementTypes.Transfer,
                    "WarehouseTransferAssignmentReturn", task.Header.DocumentNo, transferId, DateTime.UtcNow,
                    "Atama kaldırma öncesi fiziksel iade",
                    $"{task.Header.DocumentNo} atama iade görevi tamamlandı", movementLines), token);
                operationId = movement.OperationId;
            }
            var now = DateTimeOffset.UtcNow;
            var utcNow = DateTime.UtcNow;
            foreach (var line in task.Lines) line.ProcessedQuantity = line.PlannedQuantity;
            task.Status = WarehouseTransferTaskStatus.Completed;
            task.CompletedAtUtc = now; task.CompletedBy = actor;
            task.UpdatedBy = actor; task.UpdatedDate = utcNow;

            // İade edilen stok fiziksel olarak eski rafına döndü, ama iş emri hâlâ tüm miktarın
            // teslim edilmesini bekliyor — bu yüzden (a) satırın toplanan miktarı geri düşürülmeli
            // ki iş emrinin gerçek ilerlemesi doğru görünsün, (b) bu miktar tekrar birine atanabilsin
            // diye kaynak (origin) görevin kendisine yeni, işlenmemiş satırlar eklenmeli. Origin görev
            // zaten atama kaldırılınca "Open" durumuna dönüyor, böylece tek bir konsolide görev olarak
            // (mevcut hiç toplanmamış satırlarıyla birlikte) tekrar Görev Havuzu'nda görünür.
            foreach (var returnedLine in task.Lines)
            {
                var wtLine = returnedLine.Line;
                ApplyReturnedQuantityToTransferLine(wtLine, returnedLine.PlannedQuantity, actor);
            }

            if (task.OriginTaskId.HasValue && task.OriginUserId.HasValue)
            {
                var originTask = await LoadTaskAsync(transferId, task.OriginTaskId.Value, token);
                foreach (var returnedLine in task.Lines)
                {
                    // Devir olmadan (aynı görevde) iade edilmişse, o satır için görevde zaten bir
                    // kayıt var (WtTaskId+WtLineId üzerinde benzersizlik kısıtı var) — yeni satır
                    // eklemek yerine mevcut kaydı sıfırlamak gerekir. Devir zincirinden gelen
                    // satırlar için ise (o görevde hiç kaydı yoktur) yeni satır eklenir.
                    var existing = originTask.Lines.FirstOrDefault(x => !x.IsDeleted && x.WtLineId == returnedLine.WtLineId);
                    if (existing is not null)
                    {
                        existing.ProcessedQuantity = 0;
                        existing.UpdatedBy = actor; existing.UpdatedDate = utcNow;
                    }
                    else
                        originTask.Lines.Add(new WarehouseTransferTaskLine
                        {
                            BranchCode = originTask.BranchCode, CreatedBy = actor, CreatedDate = utcNow,
                            Task = originTask, Line = returnedLine.Line,
                            PlannedQuantity = returnedLine.PlannedQuantity, ProcessedQuantity = 0,
                            SourceLocationId = returnedLine.TargetLocationId,
                        });
                }
                var originAssignment = originTask.Assignments.SingleOrDefault(x => !x.IsDeleted && x.UserId == task.OriginUserId.Value);
                if (originAssignment is not null) RemoveAssignmentCore(originTask, originAssignment, actor);
                originTask.UpdatedBy = actor; originTask.UpdatedDate = utcNow;
            }

            // NOT: ReducePickTaskProcessedForLine buradan bilerek çağrılmıyor — origin görev
            // konsolidasyonu (yukarıda) zaten iade edilen satırı doğru şekilde tek bir (güncel)
            // göreve taşıyıp sıfırlıyor. ReducePickTaskProcessedForLine TÜM Pick görevlerini (geçmiş/
            // tamamlanmış olanlar dahil) tarayıp aynı satırı bulduğu her yerde ayrıca sıfırlayıp
            // gerekirse o eski görevi de yeniden "InProgress" açıyor — bu, yukarıdaki konsolidasyonla
            // çakışıp aynı işi iki farklı görevde (biri güncel, biri yanlışlıkla yeniden açılmış eski
            // görev) aktif gösteriyor ve tamamlanma yüzdesi/atanan-tamamlanan sayaçlarını bozuyordu.
            var header = await LoadTransferHeaderAsync(transferId, token);
            await RestoreOpenLineReservationsAsync(
                header,
                $"WT:{transferId}:RESERVE:ASSIGN-RETURN:{idempotencyKey:N}",
                actor,
                token);

            await uow.SaveChangesAsync(token);
            await audit.WriteAsync(new("production-transfer.task.assignment-return.complete", nameof(WarehouseTransferTask), task.Id.ToString(), "Succeeded", "production-transfer",
                NewValues: new { TransferId = transferId, TaskId = task.Id, StockMovementOperationId = operationId, OriginTaskId = task.OriginTaskId, OriginUserId = task.OriginUserId },
                ChangedFields: ["ProcessedQuantity", "Status", "OriginTask.Lines", "OriginTask.Assignments", "Line.PickedQuantity"]), token);
            return await MapAsync(transferId, token);
        }, ct, IsolationLevel.Serializable);

    public Task<ProductionTransferTaskBoardDto> HandoffAsync(
        long transferId, long taskId, HandoffProductionTransferTaskRequest request, long actor, CancellationToken ct = default) =>
        uow.ExecuteInTransactionAsync(async token =>
        {
            var task = await LoadTaskAsync(transferId, taskId, token);
            if (task.TaskType is WarehouseTransferTaskType.CancellationReturn or WarehouseTransferTaskType.AssignmentReturn)
                throw AppException.BadRequest("İade görevi başka kullanıcıya devredilemez.");
            if (task.Status is WarehouseTransferTaskStatus.Completed or WarehouseTransferTaskStatus.Cancelled)
                throw AppException.Conflict("Tamamlanmış veya iptal edilmiş görev devredilemez.");
            if (request.TargetUserId <= 0) throw AppException.BadRequest("Görevin devredileceği kullanıcı zorunludur.");
            if (task.Assignments.Any(x => !x.IsDeleted && x.UserId == request.TargetUserId))
                throw AppException.Conflict("Seçilen kullanıcı zaten bu göreve atanmış.");
            var target = await uow.Repository<User>().Query().SingleOrDefaultAsync(x => x.Id == request.TargetUserId && x.IsActive, token)
                ?? throw AppException.BadRequest("Görevin devredileceği kullanıcı bulunamadı veya aktif değil.");
            var hasWarehouseAssignments = await uow.Repository<UserWarehouseAssignment>().Query().AnyAsync(x => x.UserId == target.Id, token);
            if (hasWarehouseAssignments && !await uow.Repository<UserWarehouseAssignment>().Query()
                    .AnyAsync(x => x.UserId == target.Id && x.WarehouseId == task.WarehouseId, token))
                throw AppException.BadRequest("Seçilen kullanıcı bu görevin deposunda çalışmıyor.");

            var remaining = task.Lines
                .Select(x => new { Source = x, Quantity = Math.Max(0, x.PlannedQuantity - x.ProcessedQuantity) })
                .Where(x => x.Quantity > 0).ToArray();
            if (remaining.Length == 0) throw AppException.Conflict("Görevde devredilecek kalan miktar bulunmuyor.");

            if (task.Lines.All(x => x.ProcessedQuantity <= 0))
            {
                foreach (var assignment in task.Assignments.Where(x => !x.IsDeleted))
                {
                    assignment.IsDeleted = true; assignment.DeletedBy = actor; assignment.DeletedDate = DateTime.UtcNow;
                }
                var previousTarget = task.Assignments.SingleOrDefault(x => x.UserId == target.Id);
                if (previousTarget is not null)
                {
                    previousTarget.IsDeleted = false; previousTarget.DeletedBy = null; previousTarget.DeletedDate = null;
                    previousTarget.IsPrimary = true; previousTarget.AssignedAtUtc = DateTimeOffset.UtcNow;
                    previousTarget.AssignedBy = actor; previousTarget.AcceptedAtUtc = null;
                    previousTarget.UpdatedBy = actor; previousTarget.UpdatedDate = DateTime.UtcNow;
                }
                else task.Assignments.Add(new WarehouseTransferTaskAssignment
                {
                    BranchCode = task.BranchCode, UserId = target.Id, IsPrimary = true,
                    AssignedAtUtc = DateTimeOffset.UtcNow, AssignedBy = actor, CreatedBy = actor, CreatedDate = DateTime.UtcNow
                });
                task.Status = WarehouseTransferTaskStatus.Assigned;
                task.AcceptedAtUtc = null; task.AcceptedBy = null; task.StartedAtUtc = null; task.StartedBy = null;
                task.UpdatedBy = actor; task.UpdatedDate = DateTime.UtcNow;
                await uow.SaveChangesAsync(token);
                await audit.WriteAsync(new("production-transfer.task.handoff", nameof(WarehouseTransferTask), task.Id.ToString(), "Succeeded", "production-transfer",
                    NewValues: new { TransferId = transferId, TaskId = task.Id, TargetUserId = target.Id, RemainingQuantity = remaining.Sum(x => x.Quantity), request.Reason },
                    ChangedFields: ["Assignments", "Status"]), token);
                return await MapAsync(transferId, token);
            }

            var suffix = await NextRemainderSuffixAsync(task.Header.DocumentNo, token);
            var now = DateTimeOffset.UtcNow;
            var child = new WarehouseTransferTask
            {
                BranchCode = task.BranchCode, Header = task.Header, TaskNo = $"{task.Header.DocumentNo}-{suffix}",
                TaskType = task.TaskType, WarehouseId = task.WarehouseId, Status = WarehouseTransferTaskStatus.Assigned,
                Priority = task.Priority, PlannedAtUtc = task.PlannedAtUtc, PreviousTaskId = task.Id,
                Description = $"{task.TaskNo} kalan işi devredildi. {CleanReason(request.Reason)}".Trim(),
                CreatedBy = actor, CreatedDate = DateTime.UtcNow
            };
            foreach (var row in remaining)
            {
                child.Lines.Add(new WarehouseTransferTaskLine
                {
                    BranchCode = task.BranchCode, Line = row.Source.Line, PlannedQuantity = row.Quantity,
                    ProcessedQuantity = 0, SourceLocationId = row.Source.SourceLocationId,
                    TargetLocationId = row.Source.TargetLocationId, CreatedBy = actor, CreatedDate = DateTime.UtcNow
                });
                if (row.Source.ProcessedQuantity <= 0)
                {
                    row.Source.IsDeleted = true; row.Source.DeletedBy = actor; row.Source.DeletedDate = DateTime.UtcNow;
                }
                else
                {
                    row.Source.PlannedQuantity = row.Source.ProcessedQuantity;
                    row.Source.UpdatedBy = actor; row.Source.UpdatedDate = DateTime.UtcNow;
                }
            }
            child.Assignments.Add(new WarehouseTransferTaskAssignment
            {
                BranchCode = task.BranchCode, UserId = target.Id, IsPrimary = true,
                AssignedAtUtc = now, AssignedBy = actor, CreatedBy = actor, CreatedDate = DateTime.UtcNow
            });
            task.Status = WarehouseTransferTaskStatus.Completed;
            task.CompletedAtUtc = now; task.CompletedBy = task.StartedBy ?? actor;
            task.UpdatedBy = actor; task.UpdatedDate = DateTime.UtcNow;
            await uow.Repository<WarehouseTransferTask>().AddAsync(child, token);
            await uow.SaveChangesAsync(token);
            await audit.WriteAsync(new("production-transfer.task.handoff", nameof(WarehouseTransferTask), task.Id.ToString(), "Succeeded", "production-transfer",
                NewValues: new { TransferId = transferId, SourceTaskId = task.Id, ChildTaskId = child.Id, child.TaskNo, TargetUserId = target.Id, RemainingQuantity = remaining.Sum(x => x.Quantity), request.Reason },
                ChangedFields: ["Lines.PlannedQuantity", "Status", "CompletedAtUtc", "ChildTask"]), token);
            return await MapAsync(transferId, token);
        }, ct, IsolationLevel.Serializable);

    public Task<ProductionTransferTaskBoardDto> RefreshRouteAsync(
        long transferId, long taskId, long actor, CancellationToken ct = default) =>
        uow.ExecuteInTransactionAsync(async token =>
        {
            var task = await LoadTaskAsync(transferId, taskId, token);
            if (task.TaskType is WarehouseTransferTaskType.CancellationReturn or WarehouseTransferTaskType.AssignmentReturn)
                throw AppException.BadRequest("İade görevinin kaynak rotası değiştirilemez.");
            if (task.Status is WarehouseTransferTaskStatus.Completed or WarehouseTransferTaskStatus.Cancelled)
                throw AppException.Conflict("Tamamlanmış veya iptal edilmiş görevin rotası yenilenemez.");

            var movable = task.Lines.Where(x => !x.IsDeleted && x.PlannedQuantity - x.ProcessedQuantity > 0).ToArray();
            if (movable.Length == 0)
                throw AppException.Conflict("Rotası değiştirilebilecek açık kalem bulunamadı.");

            var stockIds = movable.Select(x => x.Line.StockId).Distinct().ToArray();
            var excludedLocationIds = await ProductionTransferSourceLocationExclusions.FromHeaderAsync(
                uow, task.Header, movable.Select(x => x.Line), token);
            var locations = await uow.Repository<WarehouseLocation>().Query()
                .Where(x => x.WarehouseId == task.WarehouseId && x.IsActive && x.IsPickable && !x.IsQuarantine)
                .ToDictionaryAsync(x => x.Id, token);
            var locationIds = locations.Keys.ToArray();
            var balances = (await uow.Repository<LocationStockBalance>().Query()
                .Where(x => x.WarehouseId == task.WarehouseId && stockIds.Contains(x.StockId)
                    && locationIds.Contains(x.LocationId) && x.StockStatus == "Available" && x.AvailableQuantity > 0)
                .ToListAsync(token))
                .Where(x => !excludedLocationIds.Contains(x.LocationId))
                .ToList();

            var changed = 0;
            foreach (var taskLine in movable)
            {
                var line = taskLine.Line;
                if (line.Trackings.Count == 0)
                {
                    var best = balances.Where(x => x.StockId == line.StockId && x.YapCodeId == line.YapCodeId
                            && string.Equals(x.UnitCode, line.UnitCode, StringComparison.OrdinalIgnoreCase))
                        .OrderByDescending(x => x.AvailableQuantity)
                        .ThenBy(x => locations[x.LocationId].Code, StringComparer.OrdinalIgnoreCase)
                        .FirstOrDefault();
                    if (best is null || taskLine.SourceLocationId == best.LocationId) continue;
                    taskLine.SourceLocationId = best.LocationId;
                    line.DefaultSourceLocationId = best.LocationId;
                    changed++;
                    continue;
                }

                var trackingLocations = new HashSet<long>();
                foreach (var tracking in line.Trackings.Where(x => x.PickedQuantity == 0 && x.ReservedQuantity == 0))
                {
                    var best = balances.Where(x => x.StockId == line.StockId && x.YapCodeId == line.YapCodeId
                            && string.Equals(x.UnitCode, line.UnitCode, StringComparison.OrdinalIgnoreCase)
                            && string.Equals(x.LotNo, tracking.LotNo ?? string.Empty, StringComparison.OrdinalIgnoreCase)
                            && string.Equals(x.SerialNo, tracking.SerialNo ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                        .OrderByDescending(x => x.AvailableQuantity)
                        .ThenBy(x => locations[x.LocationId].Code, StringComparer.OrdinalIgnoreCase)
                        .FirstOrDefault();
                    if (best is null) continue;
                    trackingLocations.Add(best.LocationId);
                    if (tracking.SourceLocationId == best.LocationId) continue;
                    tracking.SourceLocationId = best.LocationId;
                    changed++;
                }
                if (trackingLocations.Count == 1)
                {
                    taskLine.SourceLocationId = trackingLocations.Single();
                    line.DefaultSourceLocationId = taskLine.SourceLocationId;
                }
                else if (trackingLocations.Count > 1)
                {
                    taskLine.SourceLocationId = null;
                    line.DefaultSourceLocationId = null;
                }
            }
            if (changed == 0)
                throw AppException.Conflict("Güncel stok bakiyesinde daha uygun yeni bir kaynak raf bulunamadı.");

            task.UpdatedBy = actor; task.UpdatedDate = DateTime.UtcNow;
            await uow.SaveChangesAsync(token);

            var header = await LoadTransferHeaderAsync(transferId, token);
            await RestoreOpenLineReservationsAsync(
                header,
                $"WT:{transferId}:RESERVE:ROUTE-REFRESH:{taskId}",
                actor,
                token);
            await uow.SaveChangesAsync(token);

            await audit.WriteAsync(new("production-transfer.task.route.refresh", nameof(WarehouseTransferTask), task.Id.ToString(), "Succeeded", "production-transfer",
                NewValues: new { TransferId = transferId, TaskId = task.Id, ChangedRouteCount = changed },
                ChangedFields: ["SourceLocationId"]), token);
            return await MapAsync(transferId, token);
        }, ct, IsolationLevel.Serializable);

    public async Task<ProductionTaskStartCheckDto> CheckStartAsync(
        long transferId, long taskId, long actor, CancellationToken ct = default)
    {
        var task = await LoadTaskAsync(transferId, taskId, ct);
        if (!task.Assignments.Any(x => !x.IsDeleted && x.UserId == actor))
            throw AppException.Forbidden("Bu görev size atanmamış.");
        var shortages = await CheckStartInternalAsync(task, ct);
        return new(shortages.Count == 0, shortages);
    }

    public Task<ProductionTransferTaskBoardDto> AcceptAndStartAsync(
        long transferId, long taskId, long actor, bool allowPartialStart = false, CancellationToken ct = default) =>
        uow.ExecuteInTransactionAsync(async token =>
        {
            var task = await LoadTaskAsync(transferId, taskId, token);
            if (task.Status is WarehouseTransferTaskStatus.Completed or WarehouseTransferTaskStatus.Cancelled)
                throw AppException.Conflict("Tamamlanmış veya iptal edilmiş görev başlatılamaz.");
            var assignment = task.Assignments.SingleOrDefault(x => !x.IsDeleted && x.UserId == actor)
                ?? throw AppException.Forbidden("Bu görev size atanmamış.");

            var check = await CheckStartInternalAsync(task, token);
            if (check.Count > 0 && !allowPartialStart)
                throw AppException.Conflict("Görevde eksik stok var. Ön toplama onayı olmadan başlatılamaz.");

            await ApplyPermanentRouteSplitCoreAsync(task, actor, token);
            await uow.SaveChangesAsync(token);

            var now = DateTimeOffset.UtcNow;
            var header = await LoadTransferHeaderAsync(transferId, token);
            ProductionTransferPickingSupport.EnsureHeaderReleasedForPicking(header, actor, now);

            assignment.AcceptedAtUtc ??= now;
            task.AcceptedAtUtc ??= now; task.AcceptedBy ??= actor;
            task.StartedAtUtc ??= now; task.StartedBy ??= actor;
            task.Status = WarehouseTransferTaskStatus.InProgress;
            task.UpdatedBy = actor; task.UpdatedDate = DateTime.UtcNow;

            if (header.ReservationPolicy != WarehouseTransferReservationPolicy.None)
            {
                await reservations.ReserveAsync(
                    header,
                    $"WT:{transferId}:RESERVE:START:{taskId}:{actor}",
                    actor,
                    token);
            }

            await uow.SaveChangesAsync(token);
            await audit.WriteAsync(new("production-transfer.task.start", nameof(WarehouseTransferTask), task.Id.ToString(), "Succeeded", "production-transfer",
                NewValues: new { TransferId = transferId, TaskId = task.Id, UserId = actor, StartedAtUtc = now, allowPartialStart },
                ChangedFields: ["AcceptedAtUtc", "StartedAtUtc", "Status"]), token);
            return await MapAsync(transferId, token);
        }, ct, IsolationLevel.Serializable);

    public async Task ApplyPermanentRouteSplitAsync(long transferId, long taskId, long actor, CancellationToken ct = default)
    {
        await uow.ExecuteInTransactionAsync(async token =>
        {
            var task = await LoadTaskAsync(transferId, taskId, token);
            await ApplyPermanentRouteSplitCoreAsync(task, actor, token);
            await uow.SaveChangesAsync(token);
            return true;
        }, ct, IsolationLevel.Serializable);
    }

    public async Task<IReadOnlyList<WarehouseTransferPickedSourceLocationDto>> GetLinePickedSourcesAsync(
        long transferId, long lineId, CancellationToken ct = default)
    {
        var line = await uow.Repository<WarehouseTransferLine>().Query()
            .Include(x => x.Trackings)
            .SingleOrDefaultAsync(x => x.Id == lineId && x.WtHeaderId == transferId, ct)
            ?? throw AppException.NotFound("Transfer satırı bulunamadı.");

        var sources = new Dictionary<long, decimal>();

        foreach (var tracking in line.Trackings.Where(x => x.PickedQuantity > 0 && x.SourceLocationId.HasValue))
        {
            sources[tracking.SourceLocationId!.Value] =
                sources.GetValueOrDefault(tracking.SourceLocationId!.Value) + tracking.PickedQuantity;
        }

        if (line.TrackingType == StockTrackingType.None && line.PickedQuantity > 0)
        {
            var pickPrefix = $"WT:{transferId}:Pick:";
            var reversedIds = uow.Repository<StockMovementOperation>().Query()
                .Where(x => x.ReversalOfOperationId.HasValue)
                .Select(x => x.ReversalOfOperationId!.Value);
            var operationIds = await uow.Repository<StockMovementOperation>().Query()
                .Where(x => x.ReferenceType == "WarehouseTransfer"
                    && x.ReferenceId == transferId
                    && x.IdempotencyKey.StartsWith(pickPrefix)
                    && x.Status == StockMovementStatuses.Posted
                    && !reversedIds.Contains(x.Id))
                .Select(x => x.Id)
                .ToListAsync(ct);
            if (operationIds.Count > 0)
            {
                var movementSources = await uow.Repository<StockMovementEntry>().Query()
                    .Where(x => operationIds.Contains(x.OperationId)
                        && x.StockId == line.StockId
                        && x.YapCodeId == line.YapCodeId
                        && x.QuantityDelta < 0)
                    .GroupBy(x => x.LocationId)
                    .Select(g => new { LocationId = g.Key, Quantity = -g.Sum(x => x.QuantityDelta) })
                    .ToListAsync(ct);
                foreach (var row in movementSources)
                    sources[row.LocationId] = sources.GetValueOrDefault(row.LocationId) + row.Quantity;
            }
        }

        if (sources.Count == 0) return [];

        var locations = await uow.Repository<WarehouseLocation>().Query()
            .Where(x => sources.Keys.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => new { x.Code, x.Name }, ct);

        return sources
            .Where(x => locations.ContainsKey(x.Key))
            .OrderBy(x => locations[x.Key].Code, StringComparer.OrdinalIgnoreCase)
            .Select(x => new WarehouseTransferPickedSourceLocationDto(
                x.Key,
                locations[x.Key].Code,
                locations[x.Key].Name,
                x.Value))
            .ToArray();
    }

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
            if (task.Lines.Any(x => x.ProcessedQuantity < x.PlannedQuantity))
                throw AppException.Conflict("Tüm iade satırlarını rafa yerleştirmeden iadeyi tamamlayamazsınız.");

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
            foreach (var returnedLine in task.Lines)
            {
                ApplyReturnedQuantityToTransferLine(returnedLine.Line, returnedLine.PlannedQuantity, actor);
                returnedLine.ProcessedQuantity = returnedLine.PlannedQuantity;
            }
            task.Status = WarehouseTransferTaskStatus.Completed;
            task.CompletedAtUtc = now; task.CompletedBy = actor;
            task.UpdatedBy = actor; task.UpdatedDate = DateTime.UtcNow;

            var header = await LoadTransferHeaderAsync(transferId, token);
            foreach (var returnedLine in task.Lines)
                ReducePickTaskProcessedForLine(header, returnedLine.WtLineId, returnedLine.PlannedQuantity, actor);
            await RestoreOpenLineReservationsAsync(
                header,
                $"WT:{transferId}:RESERVE:CANCEL-RETURN:{idempotencyKey:N}",
                actor,
                token);

            await uow.SaveChangesAsync(token);
            await audit.WriteAsync(new("production-transfer.task.cancellation-return.complete", nameof(WarehouseTransferTask), task.Id.ToString(), "Succeeded", "production-transfer",
                NewValues: new { TransferId = transferId, TaskId = task.Id, StockMovementOperationId = operationId }, ChangedFields: ["ProcessedQuantity", "Status", "Line.PickedQuantity", "Line.ReservedQuantity"]), token);
            return await MapAsync(transferId, token);
        }, ct, IsolationLevel.Serializable);

    public async Task<WarehouseTransferReturnSettingDto> GetReturnSettingAsync(long warehouseId, CancellationToken ct = default)
    {
        var row = await uow.Repository<WarehouseEntity>().Query().Where(x => x.Id == warehouseId)
            .Select(x => new WarehouseTransferReturnSettingDto(
                x.Id, x.DefaultTransferReturnLocationId, x.DefaultProductionTransferLocationId))
            .SingleOrDefaultAsync(ct);
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
            if (request.DefaultProductionTransferLocationId.HasValue)
            {
                var valid = await uow.Repository<WarehouseLocation>().Query().AnyAsync(x =>
                    x.Id == request.DefaultProductionTransferLocationId
                    && x.WarehouseId == warehouse.Id
                    && x.IsActive
                    && x.IsPutaway, token);
                if (!valid)
                    throw AppException.BadRequest("Varsayılan üretim transfer rafı depoya ait, aktif ve yerleştirmeye uygun olmalıdır.");
            }
            warehouse.DefaultTransferReturnLocationId = request.DefaultTransferReturnLocationId;
            warehouse.DefaultProductionTransferLocationId = request.DefaultProductionTransferLocationId;
            warehouse.UpdatedBy = actor; warehouse.UpdatedDate = DateTime.UtcNow;
            await uow.SaveChangesAsync(token);
            await audit.WriteAsync(new("production-transfer.return-location.update", nameof(WarehouseEntity), warehouse.Id.ToString(), "Succeeded", "production-transfer",
                NewValues: new { warehouse.DefaultTransferReturnLocationId, warehouse.DefaultProductionTransferLocationId },
                ChangedFields: ["DefaultTransferReturnLocationId", "DefaultProductionTransferLocationId"]), token);
            return new WarehouseTransferReturnSettingDto(
                warehouse.Id, warehouse.DefaultTransferReturnLocationId, warehouse.DefaultProductionTransferLocationId);
        }, ct, IsolationLevel.Serializable);

    private async Task ApplyPermanentRouteSplitCoreAsync(
        WarehouseTransferTask task,
        long actor,
        CancellationToken ct)
    {
        if (task.TaskType is WarehouseTransferTaskType.CancellationReturn or WarehouseTransferTaskType.AssignmentReturn)
            return;
        if (task.Status is WarehouseTransferTaskStatus.Completed or WarehouseTransferTaskStatus.Cancelled)
            return;

        var header = task.Header;
        var link = await uow.Repository<ProductionTransferHeaderLink>().Query(true)
            .Include(x => x.Lines)
            .SingleAsync(x => x.WarehouseTransferHeaderId == header.Id, ct);
        var movable = task.Lines.Where(x => !x.IsDeleted && x.PlannedQuantity - x.ProcessedQuantity > 0).ToArray();
        if (movable.Length == 0) return;

        var context = await ProductionTransferPickingSupport.LoadBalanceContextAsync(
            uow, header, movable.Select(x => x.Line), ct);
        var utcNow = DateTime.UtcNow;
        var nextLineNo = header.Lines.Max(x => x.LineNo);

        foreach (var taskLine in movable)
        {
            var line = taskLine.Line;
            var remaining = taskLine.PlannedQuantity - taskLine.ProcessedQuantity;
            if (remaining <= 0) continue;

            if (line.Trackings.Count > 0)
            {
                ProductionTransferLineSplitHelper.RefreshSerialSources(taskLine, line, context, actor, utcNow);
                continue;
            }

            var chunks = ProductionTransferRouteAllocation.AllocateGreedyNonSerial(
                remaining, line.StockId, line.YapCodeId, line.UnitCode, context.Balances, context.Locations);
            if (chunks.Count == 0) continue;

            var sourceLineLink = link.Lines.Single(x => x.WarehouseTransferLineId == line.Id);
            if (chunks.All(x => !x.LocationId.HasValue))
            {
                line.DefaultSourceLocationId = null;
                taskLine.SourceLocationId = null;
                line.RequestedQuantity = remaining;
                taskLine.PlannedQuantity = remaining;
                sourceLineLink.RequiredQuantity = remaining;
                continue;
            }

            ProductionTransferLineSplitHelper.ApplyNonSerialRouteChunks(
                header, link, task, taskLine, line, sourceLineLink, chunks, ref nextLineNo, actor, utcNow,
                allowShortageWithoutLocation: true);
        }

        ProductionTransferLineSplitHelper.RemoveRedundantShortageSiblings(header, task, link);
    }

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
            .SelectMany(x =>
            {
                var ids = x.SourceLocationId.HasValue
                    ? new[] { x.SourceLocationId.Value }
                    : x.Line.Trackings.Where(t => t.SourceLocationId.HasValue).Select(t => t.SourceLocationId!.Value).ToArray();
                return x.TargetLocationId.HasValue ? ids.Append(x.TargetLocationId.Value) : ids;
            })
            .Distinct().ToArray();
        var locations = await uow.Repository<WarehouseLocation>().Query().Where(x => locationIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => new { x.Code, x.Name }, ct);
        var balances = await uow.Repository<LocationStockBalance>().Query()
            .Where(x => x.WarehouseId == header.SourceWarehouseId && locationIds.Contains(x.LocationId))
            .GroupBy(x => new { x.LocationId, x.StockId, x.YapCodeId })
            .Select(x => new { x.Key, Quantity = x.Sum(v => v.AvailableQuantity) }).ToListAsync(ct);

        var tasks = header.Tasks.OrderBy(x => x.Id).Select(task => new ProductionTransferTaskDto(
            task.Id, task.TaskNo, task.TaskType, task.WarehouseId, task.Status, task.AcceptedAtUtc, task.AcceptedBy, task.StartedAtUtc, task.StartedBy,
            task.CompletedAtUtc, task.CompletedBy, task.OriginTaskId, task.OriginUserId, task.PreviousTaskId,
            task.Assignments.Where(x => !x.IsDeleted).OrderByDescending(x => x.IsPrimary).Select(x => new ProductionTransferTaskAssignmentDto(
                x.UserId, users.GetValueOrDefault(x.UserId, $"Kullanıcı #{x.UserId}"), x.IsPrimary, x.AssignedAtUtc, x.AcceptedAtUtc)).ToList(),
            task.Lines.OrderBy(x => x.Id).Select(x =>
            {
                var available = x.SourceLocationId.HasValue
                    ? balances.Where(v => v.Key.LocationId == x.SourceLocationId && v.Key.StockId == x.Line.StockId && v.Key.YapCodeId == x.Line.YapCodeId).Sum(v => v.Quantity)
                    : 0m;
                var covered = task.TaskType is WarehouseTransferTaskType.CancellationReturn or WarehouseTransferTaskType.AssignmentReturn
                    ? x.PlannedQuantity
                    : Math.Min(x.PlannedQuantity, Math.Max(x.Line.ReservedQuantity, Math.Min(x.PlannedQuantity, available)));
                var lineLocationIds = x.SourceLocationId.HasValue
                    ? new[] { x.SourceLocationId.Value }
                    : x.Line.Trackings.Where(t => t.SourceLocationId.HasValue).Select(t => t.SourceLocationId!.Value).Distinct().ToArray();
                var lineLocations = lineLocationIds.Where(locations.ContainsKey).Select(id => locations[id]).ToArray();
                var targetLocation = x.TargetLocationId.HasValue && locations.TryGetValue(x.TargetLocationId.Value, out var targetLoc)
                    ? targetLoc
                    : default;
                var serialNos = x.Line.Trackings
                    .Where(t => t.PickedQuantity > 0 && !string.IsNullOrWhiteSpace(t.SerialNo))
                    .Select(t => t.SerialNo!.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                return new ProductionTransferTaskLineDto(x.Id, x.WtLineId, x.Line.StockCodeSnapshot, x.Line.StockNameSnapshot,
                    x.PlannedQuantity, covered, Math.Max(0, x.PlannedQuantity - covered), x.ProcessedQuantity,
                    x.SourceLocationId,
                    lineLocations.Length == 0 ? null : string.Join(", ", lineLocations.Select(v => v.Code).Distinct()),
                    lineLocations.Length == 0 ? null : string.Join(", ", lineLocations.Select(v => v.Name).Distinct()),
                    x.TargetLocationId,
                    targetLocation?.Code,
                    targetLocation?.Name,
                    serialNos.Length == 0 ? null : string.Join(", ", serialNos),
                    x.Line.RequestedQuantity);
            }).ToList())).ToList();
        var workloadRows = await uow.Repository<WarehouseTransferTask>().Query()
            .Where(x => Contexts.Contains(x.Header.BusinessContext) && x.BranchCode == header.BranchCode)
            .SelectMany(x => x.Assignments.Where(a => !a.IsDeleted).Select(a => new
            {
                a.UserId,
                TaskId = x.Id,
                x.Status,
                PlannedQuantity = x.Lines.Where(line => !line.IsDeleted).Sum(line => line.PlannedQuantity),
                ProcessedQuantity = x.Lines.Where(line => !line.IsDeleted).Sum(line => line.ProcessedQuantity)
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
            var plannedQuantity = group.Sum(x => x.PlannedQuantity);
            var processedQuantity = group.Sum(x => Math.Min(x.PlannedQuantity, x.ProcessedQuantity));
            return new ProductionTransferWorkloadDto(group.Key, workloadUsers.GetValueOrDefault(group.Key, $"Kullanıcı #{group.Key}"), assigned, completed,
                plannedQuantity, processedQuantity, plannedQuantity <= 0 ? 0 : decimal.Round(processedQuantity * 100m / plannedQuantity, 2));
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

    private async Task<int> NextRemainderSuffixAsync(string documentNo, CancellationToken ct)
    {
        var prefix = documentNo + "-";
        var taskNumbers = await uow.Repository<WarehouseTransferTask>().Query()
            .Where(x => x.TaskNo.StartsWith(prefix)).Select(x => x.TaskNo).ToListAsync(ct);
        var used = taskNumbers.Select(x => x[prefix.Length..])
            .Select(x => int.TryParse(x, out var value) ? value : 0).ToHashSet();
        var suffix = 1;
        while (used.Contains(suffix)) suffix++;
        return suffix;
    }

    private static string CleanReason(string? value) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    private static IReadOnlyList<StockMovementLineRequest> BuildReturnMovementLines(
        WarehouseTransferTask task,
        WarehouseTransferTaskLine? onlyTaskLine = null)
    {
        var rows = new List<StockMovementLineRequest>();
        foreach (var taskLine in task.Lines.Where(x => !x.IsDeleted))
        {
            if (onlyTaskLine is not null && taskLine.Id != onlyTaskLine.Id) continue;
            if (onlyTaskLine is null && taskLine.ProcessedQuantity >= taskLine.PlannedQuantity) continue;
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

    private async Task<WarehouseTransferHeader> LoadTransferHeaderAsync(long transferId, CancellationToken ct) =>
        await uow.Repository<WarehouseTransferHeader>().Query(true)
            .Include(x => x.Lines).ThenInclude(x => x.Trackings)
            .Include(x => x.Tasks).ThenInclude(x => x.Lines)
            .Include(x => x.Tasks).ThenInclude(x => x.Assignments)
            .SingleAsync(x => x.Id == transferId && Contexts.Contains(x.BusinessContext), ct);

    private static void ApplyReturnedQuantityToTransferLine(WarehouseTransferLine wtLine, decimal returnedQuantity, long actor)
    {
        if (returnedQuantity <= 0) return;
        var utcNow = DateTime.UtcNow;
        wtLine.PickedQuantity = Math.Max(0, wtLine.PickedQuantity - returnedQuantity);
        wtLine.Status = wtLine.PickedQuantity <= 0
            ? WarehouseTransferLineStatus.Open
            : WarehouseTransferLineStatus.PartiallyPicked;
        wtLine.UpdatedBy = actor;
        wtLine.UpdatedDate = utcNow;

        var remaining = returnedQuantity;
        foreach (var tracking in wtLine.Trackings.Where(x => x.PickedQuantity > 0).OrderByDescending(x => x.PickedQuantity))
        {
            if (remaining <= 0) break;
            var delta = Math.Min(tracking.PickedQuantity, remaining);
            tracking.PickedQuantity -= delta;
            remaining -= delta;
            tracking.UpdatedBy = actor;
            tracking.UpdatedDate = utcNow;
        }
    }

    private static void ReducePickTaskProcessedForLine(
        WarehouseTransferHeader header,
        long wtLineId,
        decimal returnedQuantity,
        long actor)
    {
        if (returnedQuantity <= 0) return;
        var remaining = returnedQuantity;
        foreach (var pickTask in header.Tasks
                     .Where(x => x.TaskType == WarehouseTransferTaskType.Pick && x.Status != WarehouseTransferTaskStatus.Cancelled)
                     .OrderByDescending(x => x.Id))
        {
            foreach (var taskLine in pickTask.Lines.Where(x => !x.IsDeleted && x.WtLineId == wtLineId && x.ProcessedQuantity > 0))
            {
                if (remaining <= 0) return;
                var delta = Math.Min(taskLine.ProcessedQuantity, remaining);
                taskLine.ProcessedQuantity -= delta;
                remaining -= delta;
                taskLine.UpdatedBy = actor;
                taskLine.UpdatedDate = DateTime.UtcNow;
            }
            if (pickTask.Lines.Where(x => !x.IsDeleted).All(x => x.ProcessedQuantity <= 0)
                && pickTask.Status is WarehouseTransferTaskStatus.PartiallyCompleted or WarehouseTransferTaskStatus.Completed)
            {
                pickTask.Status = WarehouseTransferTaskStatus.InProgress;
                pickTask.CompletedAtUtc = null;
                pickTask.CompletedBy = null;
            }
        }
    }

    private async Task RestoreOpenLineReservationsAsync(
        WarehouseTransferHeader header,
        string idempotencyKey,
        long actor,
        CancellationToken token)
    {
        if (header.ReservationPolicy == WarehouseTransferReservationPolicy.None) return;
        if (header.Status is WarehouseTransferStatus.Cancelled or WarehouseTransferStatus.Completed) return;
        await reservations.ReserveAsync(header, idempotencyKey, actor, token);
    }

    private async Task<List<ProductionTaskStockShortageDto>> CheckStartInternalAsync(
        WarehouseTransferTask task,
        CancellationToken ct)
    {
        if (task.TaskType is WarehouseTransferTaskType.CancellationReturn or WarehouseTransferTaskType.AssignmentReturn)
            return [];
        var stockIds = task.Lines.Where(x => !x.IsDeleted).Select(x => x.Line.StockId).Distinct().ToArray();
        var warehouseAvailable = await LoadWarehouseAvailableAsync(task.WarehouseId, stockIds, ct);
        var shortages = new List<ProductionTaskStockShortageDto>();
        foreach (var taskLine in task.Lines.Where(x => !x.IsDeleted))
        {
            var needed = taskLine.PlannedQuantity - taskLine.ProcessedQuantity;
            if (needed <= 0) continue;
            var line = taskLine.Line;
            var available = warehouseAvailable.GetValueOrDefault((line.StockId, line.YapCodeId, line.UnitCode));
            if (available + 0.000001m >= needed) continue;
            shortages.Add(new ProductionTaskStockShortageDto(
                taskLine.Id,
                line.Id,
                line.StockCodeSnapshot,
                line.StockNameSnapshot,
                needed,
                available,
                Math.Max(0, needed - available)));
        }
        return shortages;
    }

    private async Task<Dictionary<(long StockId, long? YapCodeId, string UnitCode), decimal>> LoadWarehouseAvailableAsync(
        long warehouseId,
        long[] stockIds,
        CancellationToken ct)
    {
        if (stockIds.Length == 0) return [];
        var locationIds = await uow.Repository<WarehouseLocation>().Query()
            .Where(x => x.WarehouseId == warehouseId && x.IsActive && x.IsPickable && !x.IsQuarantine)
            .Select(x => x.Id)
            .ToArrayAsync(ct);
        var balances = await uow.Repository<LocationStockBalance>().Query()
            .Where(x => x.WarehouseId == warehouseId
                && stockIds.Contains(x.StockId)
                && locationIds.Contains(x.LocationId)
                && x.StockStatus == "Available"
                && x.AvailableQuantity > 0)
            .ToListAsync(ct);
        return balances
            .GroupBy(x => (x.StockId, x.YapCodeId, x.UnitCode))
            .ToDictionary(g => g.Key, g => g.Sum(x => x.AvailableQuantity));
    }
}
