using System.Data;
using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Audit.Application;
using verii_wms_api_v2.Modules.GoodsReceipt.Domain;
using verii_wms_api_v2.Modules.Identity.Domain;
using verii_wms_api_v2.Modules.Quality.Application;
using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;
using WarehouseEntity = verii_wms_api_v2.Modules.Warehouse.Domain.Warehouse;

namespace verii_wms_api_v2.Modules.GoodsReceipt.Application;

public sealed class GoodsReceiptTaskService(
    IUnitOfWork unitOfWork,
    IAuditLogWriter audit,
    IQualityPolicyResolver qualityPolicyResolver) : IGoodsReceiptTaskService
{
    private IGenericRepository<GoodsReceiptTask> Tasks => unitOfWork.Repository<GoodsReceiptTask>();
    private IGenericRepository<GoodsReceiptTaskAssignment> Assignments => unitOfWork.Repository<GoodsReceiptTaskAssignment>();

    public async Task<PagedResponse<GoodsReceiptTaskGridRow>> GetPagedAsync(PagedRequest request, long? currentUserId, bool assignedOnly, CancellationToken cancellationToken = default)
    {
        if (assignedOnly && currentUserId is null) throw AppException.Unauthorized("Geçersiz kullanıcı oturumu.");
        var search = request.Search?.Trim();
        var tasks = Tasks.Query();
        if (assignedOnly)
        {
            var userId = currentUserId!.Value;
            var taskLines = unitOfWork.Repository<GoodsReceiptTaskLine>().Query();
            var labels = unitOfWork.Repository<GoodsReceiptLabel>().Query();
            tasks = tasks.Where(t => t.Assignments.Any(a => a.UserId == userId
                && a.Status != GoodsReceiptAssignmentStatus.Unassigned
                && a.Status != GoodsReceiptAssignmentStatus.Rejected
                && (a.Status != GoodsReceiptAssignmentStatus.Completed
                    || labels.Any(label => label.GrTaskLineId.HasValue
                        && taskLines.Any(taskLine => taskLine.Id == label.GrTaskLineId.Value
                            && taskLine.GrTaskId == t.Id)
                        && label.Status == GoodsReceiptLabelStatus.Generated
                        && label.PrintCount == 0))));
        }

        var headers = unitOfWork.Repository<GoodsReceiptHeader>().Query();
        var warehouses = unitOfWork.Repository<WarehouseEntity>().Query();
        var lines = unitOfWork.Repository<GoodsReceiptTaskLine>().Query();
        var assignments = Assignments.Query();
        var joined = from task in tasks
                     join header in headers on task.GrHeaderId equals header.Id
                     join warehouse in warehouses on task.WarehouseId equals warehouse.Id
                     where string.IsNullOrWhiteSpace(search)
                         || task.TaskNo.Contains(search)
                         || header.DocumentNo.Contains(search)
                         || (header.SupplierCodeSnapshot != null && header.SupplierCodeSnapshot.Contains(search))
                         || (header.SupplierNameSnapshot != null && header.SupplierNameSnapshot.Contains(search))
                     select new { Task = task, Header = header, Warehouse = warehouse };
        var query = joined.Select(x => new GoodsReceiptTaskGridRow(
            x.Task.Id, x.Header.Id, x.Task.BranchCode, x.Task.TaskNo, x.Header.DocumentNo, x.Task.TaskType, x.Task.Status,
            x.Header.Status, x.Header.ProcessType, x.Header.LabelStrategy, x.Task.Priority, x.Warehouse.Id, x.Warehouse.WarehouseCode,
            x.Warehouse.WarehouseName, x.Header.SupplierCodeSnapshot, x.Header.SupplierNameSnapshot,
            lines.Count(line => line.GrTaskId == x.Task.Id),
            lines.Where(line => line.GrTaskId == x.Task.Id).Sum(line => (decimal?)line.PlannedQuantity) ?? 0,
            lines.Where(line => line.GrTaskId == x.Task.Id).Sum(line => (decimal?)line.ProcessedQuantity) ?? 0,
            assignments.Count(assignment => assignment.GrTaskId == x.Task.Id
                && assignment.Status != GoodsReceiptAssignmentStatus.Unassigned
                && assignment.Status != GoodsReceiptAssignmentStatus.Rejected),
            currentUserId.HasValue
                ? assignments.Where(assignment => assignment.GrTaskId == x.Task.Id
                        && assignment.UserId == currentUserId.Value
                        && assignment.Status != GoodsReceiptAssignmentStatus.Unassigned
                        && assignment.Status != GoodsReceiptAssignmentStatus.Rejected)
                    .Select(assignment => (GoodsReceiptAssignmentStatus?)assignment.Status).FirstOrDefault()
                : null,
            x.Task.PlannedStartAtUtc, x.Task.DueAtUtc, x.Task.StartedAtUtc, x.Task.CompletedAtUtc,
            x.Task.CreatedBy, x.Task.CreatedDate, x.Task.UpdatedBy, x.Task.UpdatedDate, x.Task.RowVersion));

        return await query.ApplyAdvancedFilters(request).ApplySort(request, nameof(GoodsReceiptTaskGridRow.CreatedDate))
            .ToPagedResponseAsync(request, cancellationToken);
    }

    public async Task<GoodsReceiptTaskDetail> GetDetailAsync(long id, long currentUserId, CancellationToken cancellationToken = default)
    {
        var page = await GetTaskRows(id, currentUserId, cancellationToken);
        var taskLines = await unitOfWork.Repository<GoodsReceiptTaskLine>().Query().Include(x => x.Line)
            .Where(x => x.GrTaskId == id).OrderBy(x => x.SequenceNo).ToListAsync(cancellationToken);
        var taskLineIds = taskLines.Select(x => x.Id).ToArray();
        var trackingRows = await unitOfWork.Repository<GoodsReceiptTaskLineTracking>().Query()
            .Where(x => taskLineIds.Contains(x.GrTaskLineId)).OrderBy(x => x.SequenceNo).ToListAsync(cancellationToken);
        var trackingByLine = trackingRows.GroupBy(x => x.GrTaskLineId).ToDictionary(x => x.Key, x => (IReadOnlyList<GoodsReceiptTaskLineTrackingDto>)x
            .Select(row => new GoodsReceiptTaskLineTrackingDto(row.Id, row.SequenceNo, row.PlannedQuantity, row.LotNo, row.SerialNo,
                row.ManufacturingDate, row.ExpirationDate, row.TargetWarehouseId, row.ToLocationId, row.Description)).ToList());
        var stockIds = taskLines.Select(x => x.Line.StockId).Distinct().ToArray();
        var stockGroups = await unitOfWork.Repository<Modules.Stock.Domain.Stock>().Query()
            .Where(x => stockIds.Contains(x.Id) && x.BranchCode == page.BranchCode)
            .Select(x => new { x.Id, x.GroupCode })
            .ToDictionaryAsync(x => x.Id, x => x.GroupCode, cancellationToken);
        var qualityByStockId = new Dictionary<long, bool>();
        foreach (var stockId in stockIds)
        {
            var policy = await qualityPolicyResolver.ResolveAsync(
                page.BranchCode,
                stockId,
                stockGroups.GetValueOrDefault(stockId),
                cancellationToken);
            qualityByStockId[stockId] =
                GoodsReceiptOperationsService.RequiresQualityForLine(false, policy);
        }
        var lines = taskLines.Select(x => new GoodsReceiptTaskLineDto(x.Id, x.SequenceNo, x.GrLineId, x.Line.StockId, x.Line.StockCodeSnapshot, x.Line.StockNameSnapshot,
            x.Line.YapCodeSnapshot, x.PlannedQuantity, x.ProcessedQuantity, x.UnitCode, x.Status, x.Line.TargetWarehouseId, x.ToLocationId,
            x.Line.TrackingType, qualityByStockId.GetValueOrDefault(x.Line.StockId), trackingByLine.GetValueOrDefault(x.Id, []))).ToList();
        var assignmentRows = await Assignments.Query().Where(x => x.GrTaskId == id && x.Status != GoodsReceiptAssignmentStatus.Unassigned && x.Status != GoodsReceiptAssignmentStatus.Rejected)
            .OrderBy(x => x.AssignmentRole).ThenBy(x => x.AssignedAtUtc).ToListAsync(cancellationToken);
        var userIds = assignmentRows.Select(x => x.UserId).Distinct().ToArray();
        var users = await unitOfWork.Repository<User>().Query().Where(x => userIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Username, DisplayName = x.Detail == null ? x.Username : x.Detail.FirstName + " " + x.Detail.LastName })
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        var assignees = assignmentRows.Select(x => new GoodsReceiptTaskAssignmentDto(x.Id, x.UserId,
            users.TryGetValue(x.UserId, out var user) ? user.Username : x.UserId.ToString(),
            users.TryGetValue(x.UserId, out user) ? user.DisplayName.Trim() : x.UserId.ToString(),
            x.AssignmentRole, x.Status, x.AssignedAtUtc, x.AcceptedAtUtc, x.StartedAtUtc, x.CompletedAtUtc)).ToList();
        return new GoodsReceiptTaskDetail(page, lines, assignees);
    }

    public Task<GoodsReceiptTaskDetail> ReplaceAssignmentsAsync(long id, ReplaceGoodsReceiptTaskAssignmentsRequest request, long actorUserId, CancellationToken cancellationToken = default)
    {
        if (request.UserIds is not { Count: > 0 } || request.UserIds.Count > 25) throw AppException.BadRequest("En az bir, en fazla 25 kullanıcı atanmalıdır.");
        return unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var task = await Tasks.Query(true).Include(x => x.Assignments).FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw AppException.NotFound("Mal kabul emri bulunamadı.");
            EnsureAssignable(task);
            ApplyRowVersion(task, request.RowVersion);
            var userIds = request.UserIds.Distinct().ToArray();
            var validUsers = await unitOfWork.Repository<User>().Query().CountAsync(x => userIds.Contains(x.Id) && x.IsActive, ct);
            if (validUsers != userIds.Length) throw AppException.BadRequest("Atanacak kullanıcılardan biri bulunamadı veya pasif.");

            var now = DateTimeOffset.UtcNow;
            foreach (var old in task.Assignments.Where(x => x.Status != GoodsReceiptAssignmentStatus.Unassigned && x.Status != GoodsReceiptAssignmentStatus.Rejected && !userIds.Contains(x.UserId)))
            {
                old.Status = GoodsReceiptAssignmentStatus.Unassigned;
                old.UnassignedAtUtc = now;
                old.UnassignedReason = "Assignment replaced";
                old.UpdatedBy = actorUserId;
                old.UpdatedDate = DateTime.UtcNow;
            }
            foreach (var userId in userIds.Where(userId => !task.Assignments.Any(x => x.UserId == userId && x.Status != GoodsReceiptAssignmentStatus.Unassigned && x.Status != GoodsReceiptAssignmentStatus.Rejected)))
            {
                task.Assignments.Add(new GoodsReceiptTaskAssignment
                {
                    BranchCode = task.BranchCode, UserId = userId,
                    AssignmentRole = userId == actorUserId ? GoodsReceiptAssignmentRole.Owner : GoodsReceiptAssignmentRole.Worker,
                    Status = GoodsReceiptAssignmentStatus.Assigned, AssignedAtUtc = now, AssignedBy = actorUserId,
                    CreatedBy = actorUserId, CreatedDate = DateTime.UtcNow
                });
            }
            task.Status = GoodsReceiptTaskStatus.Assigned;
            task.UpdatedBy = actorUserId;
            task.UpdatedDate = DateTime.UtcNow;
            await SaveConcurrency(ct);
            await audit.WriteAsync(new("goods-receipt.task.assign", nameof(GoodsReceiptTask), id.ToString(), "Succeeded", "goods-receipt",
                NewValues: new { UserIds = userIds }, ChangedFields: ["Assignments", "Status"]), ct);
            return await GetDetailAsync(id, actorUserId, ct);
        }, cancellationToken, IsolationLevel.Serializable);
    }

    public Task<GoodsReceiptTaskDetail> AcceptAsync(long id, long actorUserId, CancellationToken cancellationToken = default) =>
        ChangeOwnAssignment(id, actorUserId, start: false, cancellationToken);

    public Task<GoodsReceiptTaskDetail> StartAsync(long id, long actorUserId, CancellationToken cancellationToken = default) =>
        ChangeOwnAssignment(id, actorUserId, start: true, cancellationToken);

    private Task<GoodsReceiptTaskDetail> ChangeOwnAssignment(long id, long actor, bool start, CancellationToken ct) =>
        unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var task = await Tasks.Query(true).Include(x => x.Assignments).Include(x => x.Lines).Include(x => x.Header).FirstOrDefaultAsync(x => x.Id == id, token)
                ?? throw AppException.NotFound("Mal kabul emri bulunamadı.");
            if (task.Status is GoodsReceiptTaskStatus.Completed or GoodsReceiptTaskStatus.Cancelled) throw AppException.Conflict("Tamamlanmış veya iptal edilmiş emir değiştirilemez.");
            var assignment = task.Assignments.FirstOrDefault(x => x.UserId == actor && x.Status != GoodsReceiptAssignmentStatus.Unassigned && x.Status != GoodsReceiptAssignmentStatus.Rejected)
                ?? throw AppException.Forbidden("Bu mal kabul emri size atanmamış.");
            var now = DateTimeOffset.UtcNow;
            if (start)
            {
                if (assignment.Status is GoodsReceiptAssignmentStatus.Completed) throw AppException.Conflict("Tamamlanmış atama yeniden başlatılamaz.");
                assignment.Status = GoodsReceiptAssignmentStatus.InProgress;
                assignment.AcceptedAtUtc ??= now;
                assignment.StartedAtUtc ??= now;
                task.Status = GoodsReceiptTaskStatus.InProgress;
                task.StartedAtUtc ??= now;
                task.Header.Status = WarehouseOperationStatus.InProgress;
                foreach (var line in task.Lines.Where(x => x.Status is GoodsReceiptTaskStatus.Draft or GoodsReceiptTaskStatus.Assigned or GoodsReceiptTaskStatus.Released)) line.Status = GoodsReceiptTaskStatus.InProgress;
            }
            else
            {
                if (assignment.Status != GoodsReceiptAssignmentStatus.Assigned) throw AppException.Conflict("Yalnızca bekleyen atama kabul edilebilir.");
                assignment.Status = GoodsReceiptAssignmentStatus.Accepted;
                assignment.AcceptedAtUtc = now;
                if (task.Status is GoodsReceiptTaskStatus.Draft or GoodsReceiptTaskStatus.Released) task.Status = GoodsReceiptTaskStatus.Assigned;
            }
            assignment.UpdatedBy = actor; assignment.UpdatedDate = DateTime.UtcNow;
            task.UpdatedBy = actor; task.UpdatedDate = DateTime.UtcNow;
            await SaveConcurrency(token);
            await audit.WriteAsync(new(start ? "goods-receipt.task.start" : "goods-receipt.task.accept", nameof(GoodsReceiptTask), id.ToString(), "Succeeded", "goods-receipt",
                ChangedFields: ["Status", "AssignmentStatus"]), token);
            return await GetDetailAsync(id, actor, token);
        }, ct, IsolationLevel.Serializable);

    private async Task<GoodsReceiptTaskGridRow> GetTaskRows(long id, long currentUserId, CancellationToken ct)
    {
        var page = await GetPagedAsync(new PagedRequest { PageSize = 1, Filters = [new(nameof(GoodsReceiptTaskGridRow.Id), "eq", id.ToString())] }, currentUserId, false, ct);
        return page.Items.FirstOrDefault() ?? throw AppException.NotFound("Mal kabul emri bulunamadı.");
    }

    private static void EnsureAssignable(GoodsReceiptTask task)
    {
        if (task.Status is GoodsReceiptTaskStatus.InProgress or GoodsReceiptTaskStatus.PartiallyCompleted or GoodsReceiptTaskStatus.Completed or GoodsReceiptTaskStatus.Cancelled)
            throw AppException.Conflict("Başlamış, tamamlanmış veya iptal edilmiş emir yeniden atanamaz.");
    }

    private static void ApplyRowVersion(GoodsReceiptTask task, string rowVersion)
    {
        if (string.IsNullOrWhiteSpace(rowVersion)) throw AppException.BadRequest("Güncellik bilgisi zorunludur.");
        try { task.RowVersion = Convert.FromBase64String(rowVersion); }
        catch (FormatException) { throw AppException.BadRequest("Güncellik bilgisi geçersiz."); }
    }

    private async Task SaveConcurrency(CancellationToken ct)
    {
        try { await unitOfWork.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw AppException.Conflict("Emir başka bir kullanıcı tarafından güncellendi. Listeyi yenileyip tekrar deneyin."); }
    }
}
