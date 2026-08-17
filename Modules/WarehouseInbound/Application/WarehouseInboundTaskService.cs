using System.Data;
using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Audit.Application;
using verii_wms_api_v2.Modules.WarehouseInbound.Domain;
using verii_wms_api_v2.Modules.Identity.Domain;
using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;
using WarehouseEntity = verii_wms_api_v2.Modules.Warehouse.Domain.Warehouse;

namespace verii_wms_api_v2.Modules.WarehouseInbound.Application;

public sealed class WarehouseInboundTaskService(IUnitOfWork unitOfWork, IAuditLogWriter audit) : IWarehouseInboundTaskService
{
    private static readonly HashSet<string> LineSummaryColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        nameof(WarehouseInboundTaskGridRow.LineCount),
        nameof(WarehouseInboundTaskGridRow.PlannedQuantity),
        nameof(WarehouseInboundTaskGridRow.ProcessedQuantity)
    };
    private static readonly HashSet<string> AssignmentSummaryColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        nameof(WarehouseInboundTaskGridRow.AssigneeCount),
        nameof(WarehouseInboundTaskGridRow.MyAssignmentStatus)
    };
    private IGenericRepository<WarehouseInboundTask> Tasks => unitOfWork.Repository<WarehouseInboundTask>();
    private IGenericRepository<WarehouseInboundTaskAssignment> Assignments => unitOfWork.Repository<WarehouseInboundTaskAssignment>();

    public async Task<PagedResponse<WarehouseInboundTaskGridRow>> GetPagedAsync(PagedRequest request, long? currentUserId, bool assignedOnly, CancellationToken cancellationToken = default)
    {
        if (assignedOnly && currentUserId is null) throw AppException.Unauthorized("Geçersiz kullanıcı oturumu.");
        var search = request.Search?.Trim();
        var tasks = Tasks.Query();
        if (assignedOnly)
        {
            var userId = currentUserId!.Value;
            tasks = tasks.Where(t => t.Assignments.Any(a => a.UserId == userId
                && a.Status != WarehouseInboundAssignmentStatus.Unassigned
                && a.Status != WarehouseInboundAssignmentStatus.Rejected
                && a.Status != WarehouseInboundAssignmentStatus.Completed));
        }

        var headers = unitOfWork.Repository<WarehouseInboundHeader>().Query();
        var warehouses = unitOfWork.Repository<WarehouseEntity>().Query();
        var lines = unitOfWork.Repository<WarehouseInboundTaskLine>().Query();
        var assignments = Assignments.Query();
        var includeLines = RequiresLineSummaryInMainQuery(request);
        var includeAssignments = RequiresAssignmentSummaryInMainQuery(request);
        var query = BuildPagedQuery(request, tasks, headers, warehouses, lines, assignments, currentUserId, search,
            includeLines, includeAssignments);
        var countQuery = BuildCountQuery(request, tasks, headers, warehouses, lines, assignments, currentUserId, search);
        var page = await query.ToPagedResponseAsync(countQuery, request, cancellationToken);
        if (page.Items.Count == 0 || includeLines && includeAssignments) return page;

        return new PagedResponse<WarehouseInboundTaskGridRow>
        {
            Items = await EnrichSummariesAsync(page.Items, lines, assignments, currentUserId,
                enrichLines: !includeLines, enrichAssignments: !includeAssignments, cancellationToken),
            TotalCount = page.TotalCount,
            PageNumber = page.PageNumber,
            PageSize = page.PageSize
        };
    }

    internal static IQueryable<WarehouseInboundTaskGridRow> BuildPagedQuery(
        PagedRequest request,
        IQueryable<WarehouseInboundTask> tasks,
        IQueryable<WarehouseInboundHeader> headers,
        IQueryable<WarehouseEntity> warehouses,
        IQueryable<WarehouseInboundTaskLine> lines,
        IQueryable<WarehouseInboundTaskAssignment> assignments,
        long? currentUserId,
        string? legacySearch = null,
        bool? includeLinesOverride = null,
        bool? includeAssignmentsOverride = null)
    {
        var query = BuildGridRows(tasks, headers, warehouses, lines, assignments, currentUserId, legacySearch,
            includeLinesOverride ?? RequiresLineSummaryInMainQuery(request),
            includeAssignmentsOverride ?? RequiresAssignmentSummaryInMainQuery(request));
        if (request.HasExplicitSearchFields) query = query.ApplySearch(request);
        return query.ApplyAdvancedFilters(request).ApplySort(request, nameof(WarehouseInboundTaskGridRow.CreatedDate));
    }

    internal static IQueryable<long> BuildCountQuery(
        PagedRequest request,
        IQueryable<WarehouseInboundTask> tasks,
        IQueryable<WarehouseInboundHeader> headers,
        IQueryable<WarehouseEntity> warehouses,
        IQueryable<WarehouseInboundTaskLine> lines,
        IQueryable<WarehouseInboundTaskAssignment> assignments,
        long? currentUserId,
        string? legacySearch = null)
    {
        var query = BuildGridRows(tasks, headers, warehouses, lines, assignments, currentUserId, legacySearch,
            RequiresLineSummaryForCount(request), RequiresAssignmentSummaryForCount(request));
        if (request.HasExplicitSearchFields) query = query.ApplySearch(request);
        return query.ApplyAdvancedFilters(request).Select(x => x.Id);
    }

    private static IQueryable<WarehouseInboundTaskGridRow> BuildGridRows(
        IQueryable<WarehouseInboundTask> tasks,
        IQueryable<WarehouseInboundHeader> headers,
        IQueryable<WarehouseEntity> warehouses,
        IQueryable<WarehouseInboundTaskLine> lines,
        IQueryable<WarehouseInboundTaskAssignment> assignments,
        long? currentUserId,
        string? search,
        bool includeLines,
        bool includeAssignments)
    {
        var joined = from task in tasks
                     join header in headers on task.GrHeaderId equals header.Id
                     join warehouse in warehouses on task.WarehouseId equals warehouse.Id
                     where string.IsNullOrWhiteSpace(search)
                         || task.TaskNo.Contains(search)
                         || header.DocumentNo.Contains(search)
                         || (header.SupplierCodeSnapshot != null && header.SupplierCodeSnapshot.Contains(search))
                         || (header.SupplierNameSnapshot != null && header.SupplierNameSnapshot.Contains(search))
                     select new { Task = task, Header = header, Warehouse = warehouse };
        return joined.Select(x => new WarehouseInboundTaskGridRow(
            x.Task.Id, x.Header.Id, x.Task.BranchCode, x.Task.TaskNo, x.Header.DocumentNo, x.Task.TaskType, x.Task.Status,
            x.Header.Status, x.Header.ProcessType, x.Task.Priority, x.Warehouse.Id, x.Warehouse.WarehouseCode,
            x.Warehouse.WarehouseName, x.Header.SupplierCodeSnapshot, x.Header.SupplierNameSnapshot,
            includeLines ? lines.Count(line => line.GrTaskId == x.Task.Id) : 0,
            includeLines ? lines.Where(line => line.GrTaskId == x.Task.Id).Sum(line => (decimal?)line.PlannedQuantity) ?? 0 : 0,
            includeLines ? lines.Where(line => line.GrTaskId == x.Task.Id).Sum(line => (decimal?)line.ProcessedQuantity) ?? 0 : 0,
            includeAssignments ? assignments.Count(assignment => assignment.GrTaskId == x.Task.Id
                && assignment.Status != WarehouseInboundAssignmentStatus.Unassigned
                && assignment.Status != WarehouseInboundAssignmentStatus.Rejected) : 0,
            includeAssignments && currentUserId.HasValue
                ? assignments.Where(assignment => assignment.GrTaskId == x.Task.Id
                        && assignment.UserId == currentUserId.Value
                        && assignment.Status != WarehouseInboundAssignmentStatus.Unassigned
                        && assignment.Status != WarehouseInboundAssignmentStatus.Rejected)
                    .Select(assignment => (WarehouseInboundAssignmentStatus?)assignment.Status).FirstOrDefault()
                : null,
            x.Task.PlannedStartAtUtc, x.Task.DueAtUtc, x.Task.StartedAtUtc, x.Task.CompletedAtUtc,
            x.Task.CreatedBy, x.Task.CreatedDate, x.Task.UpdatedBy, x.Task.UpdatedDate, x.Task.RowVersion));
    }

    private static bool RequiresLineSummaryForCount(PagedRequest request) =>
        (!string.IsNullOrWhiteSpace(request.EffectiveSearch)
         && request.SearchFields.Any(LineSummaryColumns.Contains))
        || request.Filters.Any(filter => LineSummaryColumns.Contains(filter.Column));

    private static bool RequiresLineSummaryInMainQuery(PagedRequest request) =>
        RequiresLineSummaryForCount(request) || LineSummaryColumns.Contains(request.SortBy ?? string.Empty);

    private static bool RequiresAssignmentSummaryForCount(PagedRequest request) =>
        (!string.IsNullOrWhiteSpace(request.EffectiveSearch)
         && request.SearchFields.Any(AssignmentSummaryColumns.Contains))
        || request.Filters.Any(filter => AssignmentSummaryColumns.Contains(filter.Column));

    private static bool RequiresAssignmentSummaryInMainQuery(PagedRequest request) =>
        RequiresAssignmentSummaryForCount(request) || AssignmentSummaryColumns.Contains(request.SortBy ?? string.Empty);

    private static async Task<IReadOnlyList<WarehouseInboundTaskGridRow>> EnrichSummariesAsync(
        IReadOnlyList<WarehouseInboundTaskGridRow> rows,
        IQueryable<WarehouseInboundTaskLine> lines,
        IQueryable<WarehouseInboundTaskAssignment> assignments,
        long? currentUserId,
        bool enrichLines,
        bool enrichAssignments,
        CancellationToken cancellationToken)
    {
        var taskIds = rows.Select(x => x.Id).ToArray();
        var lineTotals = enrichLines
            ? await lines.Where(x => taskIds.Contains(x.GrTaskId)).GroupBy(x => x.GrTaskId)
                .Select(groupRows => new
                {
                    TaskId = groupRows.Key,
                    LineCount = groupRows.Count(),
                    PlannedQuantity = groupRows.Sum(x => x.PlannedQuantity),
                    ProcessedQuantity = groupRows.Sum(x => x.ProcessedQuantity)
                }).ToDictionaryAsync(x => x.TaskId, cancellationToken)
            : [];
        var assignmentRows = enrichAssignments
            ? await assignments.Where(x => taskIds.Contains(x.GrTaskId)
                    && x.Status != WarehouseInboundAssignmentStatus.Unassigned
                    && x.Status != WarehouseInboundAssignmentStatus.Rejected)
                .Select(x => new { x.GrTaskId, x.UserId, x.Status }).ToListAsync(cancellationToken)
            : [];
        var assignmentTotals = assignmentRows.GroupBy(x => x.GrTaskId).ToDictionary(x => x.Key, x => new
        {
            Count = x.Count(),
            MyStatus = currentUserId.HasValue
                ? x.Where(y => y.UserId == currentUserId.Value).Select(y => (WarehouseInboundAssignmentStatus?)y.Status).FirstOrDefault()
                : null
        });

        return rows.Select(row =>
        {
            var result = row;
            if (enrichLines && lineTotals.TryGetValue(row.Id, out var lineTotal))
                result = result with
                {
                    LineCount = lineTotal.LineCount,
                    PlannedQuantity = lineTotal.PlannedQuantity,
                    ProcessedQuantity = lineTotal.ProcessedQuantity
                };
            if (enrichAssignments && assignmentTotals.TryGetValue(row.Id, out var assignmentTotal))
                result = result with
                {
                    AssigneeCount = assignmentTotal.Count,
                    MyAssignmentStatus = assignmentTotal.MyStatus
                };
            return result;
        }).ToArray();
    }

    public async Task<WarehouseInboundTaskDetail> GetDetailAsync(long id, long currentUserId, CancellationToken cancellationToken = default)
    {
        var page = await GetTaskRows(id, currentUserId, cancellationToken);
        var taskLines = await unitOfWork.Repository<WarehouseInboundTaskLine>().Query().Include(x => x.Line)
            .Where(x => x.GrTaskId == id).OrderBy(x => x.SequenceNo).ToListAsync(cancellationToken);
        var taskLineIds = taskLines.Select(x => x.Id).ToArray();
        var trackingRows = await unitOfWork.Repository<WarehouseInboundTaskLineTracking>().Query()
            .Where(x => taskLineIds.Contains(x.GrTaskLineId)).OrderBy(x => x.SequenceNo).ToListAsync(cancellationToken);
        var trackingByLine = trackingRows.GroupBy(x => x.GrTaskLineId).ToDictionary(x => x.Key, x => (IReadOnlyList<WarehouseInboundTaskLineTrackingDto>)x
            .Select(row => new WarehouseInboundTaskLineTrackingDto(row.Id, row.SequenceNo, row.PlannedQuantity, row.LotNo, row.SerialNo,
                row.ManufacturingDate, row.ExpirationDate, row.TargetWarehouseId, row.ToLocationId, row.Description)).ToList());
        var lines = taskLines.Select(x => new WarehouseInboundTaskLineDto(x.Id, x.SequenceNo, x.GrLineId, x.Line.StockId, x.Line.StockCodeSnapshot, x.Line.StockNameSnapshot,
            x.Line.YapCodeSnapshot, x.PlannedQuantity, x.ProcessedQuantity, x.UnitCode, x.Status, x.Line.TargetWarehouseId, x.ToLocationId,
            x.Line.TrackingType, trackingByLine.GetValueOrDefault(x.Id, []))).ToList();
        var assignmentRows = await Assignments.Query().Where(x => x.GrTaskId == id && x.Status != WarehouseInboundAssignmentStatus.Unassigned && x.Status != WarehouseInboundAssignmentStatus.Rejected)
            .OrderBy(x => x.AssignmentRole).ThenBy(x => x.AssignedAtUtc).ToListAsync(cancellationToken);
        var userIds = assignmentRows.Select(x => x.UserId).Distinct().ToArray();
        var users = await unitOfWork.Repository<User>().Query().Where(x => userIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Username, DisplayName = x.Detail == null ? x.Username : x.Detail.FirstName + " " + x.Detail.LastName })
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        var assignees = assignmentRows.Select(x => new WarehouseInboundTaskAssignmentDto(x.Id, x.UserId,
            users.TryGetValue(x.UserId, out var user) ? user.Username : x.UserId.ToString(),
            users.TryGetValue(x.UserId, out user) ? user.DisplayName.Trim() : x.UserId.ToString(),
            x.AssignmentRole, x.Status, x.AssignedAtUtc, x.AcceptedAtUtc, x.StartedAtUtc, x.CompletedAtUtc)).ToList();
        return new WarehouseInboundTaskDetail(page, lines, assignees);
    }

    public Task<WarehouseInboundTaskDetail> ReplaceAssignmentsAsync(long id, ReplaceWarehouseInboundTaskAssignmentsRequest request, long actorUserId, CancellationToken cancellationToken = default)
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
            foreach (var old in task.Assignments.Where(x => x.Status != WarehouseInboundAssignmentStatus.Unassigned && x.Status != WarehouseInboundAssignmentStatus.Rejected && !userIds.Contains(x.UserId)))
            {
                old.Status = WarehouseInboundAssignmentStatus.Unassigned;
                old.UnassignedAtUtc = now;
                old.UnassignedReason = "Assignment replaced";
                old.UpdatedBy = actorUserId;
                old.UpdatedDate = DateTime.UtcNow;
            }
            foreach (var userId in userIds.Where(userId => !task.Assignments.Any(x => x.UserId == userId && x.Status != WarehouseInboundAssignmentStatus.Unassigned && x.Status != WarehouseInboundAssignmentStatus.Rejected)))
            {
                task.Assignments.Add(new WarehouseInboundTaskAssignment
                {
                    BranchCode = task.BranchCode, UserId = userId,
                    AssignmentRole = userId == actorUserId ? WarehouseInboundAssignmentRole.Owner : WarehouseInboundAssignmentRole.Worker,
                    Status = WarehouseInboundAssignmentStatus.Assigned, AssignedAtUtc = now, AssignedBy = actorUserId,
                    CreatedBy = actorUserId, CreatedDate = DateTime.UtcNow
                });
            }
            task.Status = WarehouseInboundTaskStatus.Assigned;
            task.UpdatedBy = actorUserId;
            task.UpdatedDate = DateTime.UtcNow;
            await SaveConcurrency(ct);
            await audit.WriteAsync(new("warehouse-inbound.task.assign", nameof(WarehouseInboundTask), id.ToString(), "Succeeded", "warehouse-inbound",
                NewValues: new { UserIds = userIds }, ChangedFields: ["Assignments", "Status"]), ct);
            return await GetDetailAsync(id, actorUserId, ct);
        }, cancellationToken, IsolationLevel.Serializable);
    }

    public Task<WarehouseInboundTaskDetail> AcceptAsync(long id, long actorUserId, CancellationToken cancellationToken = default) =>
        ChangeOwnAssignment(id, actorUserId, start: false, cancellationToken);

    public Task<WarehouseInboundTaskDetail> StartAsync(long id, long actorUserId, CancellationToken cancellationToken = default) =>
        ChangeOwnAssignment(id, actorUserId, start: true, cancellationToken);

    private Task<WarehouseInboundTaskDetail> ChangeOwnAssignment(long id, long actor, bool start, CancellationToken ct) =>
        unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var task = await Tasks.Query(true).Include(x => x.Assignments).Include(x => x.Lines).Include(x => x.Header).FirstOrDefaultAsync(x => x.Id == id, token)
                ?? throw AppException.NotFound("Mal kabul emri bulunamadı.");
            if (task.Status is WarehouseInboundTaskStatus.Completed or WarehouseInboundTaskStatus.Cancelled) throw AppException.Conflict("Tamamlanmış veya iptal edilmiş emir değiştirilemez.");
            var assignment = task.Assignments.FirstOrDefault(x => x.UserId == actor && x.Status != WarehouseInboundAssignmentStatus.Unassigned && x.Status != WarehouseInboundAssignmentStatus.Rejected)
                ?? throw AppException.Forbidden("Bu mal kabul emri size atanmamış.");
            var now = DateTimeOffset.UtcNow;
            if (start)
            {
                if (assignment.Status is WarehouseInboundAssignmentStatus.Completed) throw AppException.Conflict("Tamamlanmış atama yeniden başlatılamaz.");
                assignment.Status = WarehouseInboundAssignmentStatus.InProgress;
                assignment.AcceptedAtUtc ??= now;
                assignment.StartedAtUtc ??= now;
                task.Status = WarehouseInboundTaskStatus.InProgress;
                task.StartedAtUtc ??= now;
                task.Header.Status = WarehouseOperationStatus.InProgress;
                foreach (var line in task.Lines.Where(x => x.Status is WarehouseInboundTaskStatus.Draft or WarehouseInboundTaskStatus.Assigned or WarehouseInboundTaskStatus.Released)) line.Status = WarehouseInboundTaskStatus.InProgress;
            }
            else
            {
                if (assignment.Status != WarehouseInboundAssignmentStatus.Assigned) throw AppException.Conflict("Yalnızca bekleyen atama kabul edilebilir.");
                assignment.Status = WarehouseInboundAssignmentStatus.Accepted;
                assignment.AcceptedAtUtc = now;
                if (task.Status is WarehouseInboundTaskStatus.Draft or WarehouseInboundTaskStatus.Released) task.Status = WarehouseInboundTaskStatus.Assigned;
            }
            assignment.UpdatedBy = actor; assignment.UpdatedDate = DateTime.UtcNow;
            task.UpdatedBy = actor; task.UpdatedDate = DateTime.UtcNow;
            await SaveConcurrency(token);
            await audit.WriteAsync(new(start ? "warehouse-inbound.task.start" : "warehouse-inbound.task.accept", nameof(WarehouseInboundTask), id.ToString(), "Succeeded", "warehouse-inbound",
                ChangedFields: ["Status", "AssignmentStatus"]), token);
            return await GetDetailAsync(id, actor, token);
        }, ct, IsolationLevel.Serializable);

    private async Task<WarehouseInboundTaskGridRow> GetTaskRows(long id, long currentUserId, CancellationToken ct)
    {
        var page = await GetPagedAsync(new PagedRequest { PageSize = 1, Filters = [new(nameof(WarehouseInboundTaskGridRow.Id), "eq", id.ToString())] }, currentUserId, false, ct);
        return page.Items.FirstOrDefault() ?? throw AppException.NotFound("Mal kabul emri bulunamadı.");
    }

    private static void EnsureAssignable(WarehouseInboundTask task)
    {
        if (task.Status is WarehouseInboundTaskStatus.InProgress or WarehouseInboundTaskStatus.PartiallyCompleted or WarehouseInboundTaskStatus.Completed or WarehouseInboundTaskStatus.Cancelled)
            throw AppException.Conflict("Başlamış, tamamlanmış veya iptal edilmiş emir yeniden atanamaz.");
    }

    private static void ApplyRowVersion(WarehouseInboundTask task, string rowVersion)
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
