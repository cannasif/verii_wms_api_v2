using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using verii_wms_api_v2.Modules.Audit.Application;
using verii_wms_api_v2.Modules.Identity.Domain;
using verii_wms_api_v2.Modules.Kkd.Domain;
using verii_wms_api_v2.Modules.Kkd.Localization;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;
using StockEntity = verii_wms_api_v2.Modules.Stock.Domain.Stock;
using WarehouseEntity = verii_wms_api_v2.Modules.Warehouse.Domain.Warehouse;

namespace verii_wms_api_v2.Modules.Kkd.Application;

public sealed class KkdRequestService(
    IUnitOfWork uow,
    IAuditLogWriter audit,
    IStringLocalizer<KkdRequestResource> localizer) : IKkdRequestService
{
    private static readonly HashSet<string> AllowedSearchFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "id", "requestNo", "employeeCode", "employeeName", "externalRequestNo",
        "groupCode", "groupName", "stockCode", "stockName", "createdBy", "updatedBy"
    };

    private static readonly string[] DefaultSearchFields =
        ["requestNo", "employeeCode", "employeeName", "externalRequestNo", "groupCode", "groupName", "stockCode", "stockName"];

    private IGenericRepository<KkdRequest> Requests => uow.Repository<KkdRequest>();
    private IGenericRepository<KkdRequestLineResolution> Resolutions => uow.Repository<KkdRequestLineResolution>();
    private IGenericRepository<KkdEmployee> Employees => uow.Repository<KkdEmployee>();
    private IGenericRepository<KkdEntitlementRule> Rules => uow.Repository<KkdEntitlementRule>();
    private IGenericRepository<KkdEmployeeStockPreference> Preferences => uow.Repository<KkdEmployeeStockPreference>();
    private IGenericRepository<StockEntity> Stocks => uow.Repository<StockEntity>();
    private IGenericRepository<WarehouseEntity> Warehouses => uow.Repository<WarehouseEntity>();
    private IGenericRepository<User> Users => uow.Repository<User>();

    public async Task<PagedResponse<KkdRequestGridRow>> GetPagedAsync(PagedRequest request, CancellationToken ct = default)
    {
        var rows = ApplySearch(Requests.Query(), request).Select(x => new KkdRequestGridRow(
            x.Id,
            x.RequestNo,
            x.Status.ToString(),
            x.Priority.ToString(),
            x.SourceType.ToString(),
            x.EmployeeId,
            x.Employee.EmployeeCode,
            (x.Employee.FirstName + " " + x.Employee.LastName).Trim(),
            x.Employee.Department.Name,
            x.Employee.Role.Name,
            x.WarehouseId,
            x.AssignedUserId,
            x.ExternalRequestNo,
            x.Lines.Count,
            x.Lines.Count(line => line.StockId == null && line.Status != KkdRequestLineStatus.Cancelled),
            x.Lines.Sum(line => line.RequestedQuantity),
            x.Lines.Sum(line => line.AllocatedQuantity),
            x.Lines.Sum(line => line.DeliveredQuantity),
            x.RequestedAtUtc,
            x.NeededAtUtc,
            x.CreatedBy,
            x.CreatedDate,
            x.UpdatedBy,
            x.UpdatedDate));

        return await rows
            .ApplyAdvancedFilters(request)
            .ApplySort(request, nameof(KkdRequestGridRow.RequestedAtUtc))
            .ToPagedResponseAsync(request, ct);
    }

    public async Task<KkdRequestDetail> GetDetailAsync(long id, CancellationToken ct = default)
    {
        var entity = await DetailQuery(false).SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw AppException.NotFound(Message(KkdRequestMessageKeys.NotFound));
        return MapDetail(entity);
    }

    public async Task<KkdRequestDetail> CreateAsync(KkdRequestCreateRequest request, long actor, CancellationToken ct = default)
    {
        ValidateCreate(request);
        return await uow.ExecuteInTransactionAsync(async token =>
        {
            var existing = await Requests.Query().SingleOrDefaultAsync(x => x.CorrelationId == request.IdempotencyKey, token);
            if (existing is not null) return await GetDetailAsync(existing.Id, token);

            var employee = await Employees.Query().Include(x => x.Department).Include(x => x.Role)
                .SingleOrDefaultAsync(x => x.Id == request.EmployeeId, token)
                ?? throw AppException.NotFound(Message(KkdRequestMessageKeys.EmployeeNotFound));
            if (!employee.IsActive) throw AppException.Conflict(Message(KkdRequestMessageKeys.EmployeeInactive));

            await ValidateAssignmentAsync(request.WarehouseId, request.AssignedUserId, token);
            var now = DateTimeOffset.UtcNow;
            var entity = new KkdRequest
            {
                BranchCode = employee.BranchCode,
                CorrelationId = request.IdempotencyKey,
                RequestNo = $"TMP-{request.IdempotencyKey:N}",
                EmployeeId = employee.Id,
                CustomerId = employee.CustomerId,
                WarehouseId = request.WarehouseId,
                AssignedUserId = request.AssignedUserId,
                SourceType = request.SourceType,
                ExternalRequestNo = Normalize(request.ExternalRequestNo, 100),
                Priority = request.Priority,
                RequestedAtUtc = now,
                NeededAtUtc = request.NeededAtUtc?.ToUniversalTime(),
                Description = Normalize(request.Description, 2000)
            };

            var lineNo = 0;
            foreach (var input in request.Lines)
            {
                var groupCode = NormalizeCode(input.GroupCode);
                await EnsureGroupEntitlementAsync(employee, groupCode, now, token);
                var stock = input.StockId.HasValue
                    ? await ValidateStockAsync(input.StockId.Value, groupCode, token)
                    : null;
                entity.Lines.Add(new KkdRequestLine
                {
                    BranchCode = employee.BranchCode,
                    CreatedBy = actor,
                    CreatedDate = now.UtcDateTime,
                    LineNo = ++lineNo,
                    GroupCode = groupCode,
                    GroupName = Normalize(input.GroupName, 200),
                    StockId = stock?.Id,
                    StockCodeSnapshot = stock?.ErpStockCode,
                    StockNameSnapshot = stock?.StockName,
                    UnitCode = stock?.BaseUnitCode ?? "ADET",
                    RequestedQuantity = input.Quantity,
                    Status = stock is null ? KkdRequestLineStatus.AwaitingStockSelection : KkdRequestLineStatus.ReadyToPrepare,
                    ExternalOrderNo = Normalize(input.ExternalOrderNo, 100),
                    ExternalOrderLineId = Normalize(input.ExternalOrderLineId, 100),
                    ResolvedByUserId = stock is null ? null : actor,
                    ResolvedAtUtc = stock is null ? null : now,
                    ResolutionReason = stock is null ? null : Message(KkdRequestMessageKeys.Created)
                });
            }

            KkdRequestStateMachine.Refresh(entity, now);
            await Requests.AddAsync(entity, token);
            await SaveAsync(token);
            entity.RequestNo = $"KKDR-{now:yyyy}-{entity.Id:000000}";
            await SaveAsync(token);
            await audit.WriteAsync(new AuditLogWriteEntry(
                "kkd.request.create", nameof(KkdRequest), entity.Id.ToString(), "Succeeded", "kkd-request",
                NewValues: Snapshot(entity), ChangedFields: RequestFields), token);
            return await GetDetailAsync(entity.Id, token);
        }, ct, IsolationLevel.Serializable);
    }

    public async Task<KkdRequestDetail> ResolveLineAsync(
        long requestId,
        long lineId,
        KkdRequestResolveLineRequest request,
        long actor,
        CancellationToken ct = default)
    {
        ValidateKey(request.IdempotencyKey);
        ValidateReason(request.Reason);
        return await uow.ExecuteInTransactionAsync(async token =>
        {
            var prior = await Resolutions.Query().Include(x => x.RequestLine)
                .SingleOrDefaultAsync(x => x.IdempotencyKey == request.IdempotencyKey, token);
            if (prior is not null)
            {
                if (prior.RequestLineId != lineId || prior.RequestLine.RequestId != requestId)
                    throw AppException.Conflict(Message(KkdRequestMessageKeys.InvalidIdempotencyKey));
                return await GetDetailAsync(requestId, token);
            }

            var entity = await Requests.Query(true).Include(x => x.Employee).Include(x => x.Lines)
                .SingleOrDefaultAsync(x => x.Id == requestId, token)
                ?? throw AppException.NotFound(Message(KkdRequestMessageKeys.NotFound));
            EnsureMutable(entity);
            var line = entity.Lines.SingleOrDefault(x => x.Id == lineId)
                ?? throw AppException.NotFound(Message(KkdRequestMessageKeys.LineNotFound));
            CheckVersion(line.RowVersion, request.ExpectedRowVersion);
            if (line.AllocatedQuantity > 0 || line.DeliveredQuantity > 0)
                throw AppException.Conflict(Message(KkdRequestMessageKeys.StockCannotChange));

            var stock = await ValidateStockAsync(request.StockId, line.GroupCode, token);
            await EnsureGroupEntitlementAsync(entity.Employee, line.GroupCode, DateTimeOffset.UtcNow, token);
            var old = new { line.StockId, line.StockCodeSnapshot, line.StockNameSnapshot, line.Status };
            var now = DateTimeOffset.UtcNow;
            await Resolutions.AddAsync(new KkdRequestLineResolution
            {
                IdempotencyKey = request.IdempotencyKey,
                RequestLineId = line.Id,
                PreviousStockId = line.StockId,
                StockId = stock.Id,
                StockCodeSnapshot = stock.ErpStockCode,
                StockNameSnapshot = stock.StockName,
                Reason = request.Reason.Trim(),
                ResolvedAtUtc = now
            }, token);

            line.StockId = stock.Id;
            line.StockCodeSnapshot = stock.ErpStockCode;
            line.StockNameSnapshot = stock.StockName;
            line.UnitCode = stock.BaseUnitCode;
            line.ResolvedByUserId = actor;
            line.ResolvedAtUtc = now;
            line.ResolutionReason = request.Reason.Trim();
            line.Status = KkdRequestLineStatus.ReadyToPrepare;
            line.UpdatedBy = actor;
            line.UpdatedDate = now.UtcDateTime;
            entity.UpdatedBy = actor;
            entity.UpdatedDate = now.UtcDateTime;
            await UpsertPreferenceAsync(entity.EmployeeId, line.GroupCode, stock.Id, now, token);
            KkdRequestStateMachine.Refresh(entity, now);
            await SaveAsync(token);
            await audit.WriteAsync(new AuditLogWriteEntry(
                "kkd.request-line.resolve", nameof(KkdRequestLine), line.Id.ToString(), "Succeeded", "kkd-request",
                Reason: request.Reason.Trim(), OldValues: old,
                NewValues: new { line.StockId, line.StockCodeSnapshot, line.StockNameSnapshot, line.Status },
                ChangedFields: ["StockId", "StockCodeSnapshot", "StockNameSnapshot", "UnitCode", "Status"]), token);
            return await GetDetailAsync(entity.Id, token);
        }, ct, IsolationLevel.Serializable);
    }

    public async Task<KkdRequestDetail> AssignAsync(long id, KkdRequestAssignRequest request, long actor, CancellationToken ct = default)
    {
        return await uow.ExecuteInTransactionAsync(async token =>
        {
            var entity = await Requests.Query(true).Include(x => x.Lines)
                .SingleOrDefaultAsync(x => x.Id == id, token)
                ?? throw AppException.NotFound(Message(KkdRequestMessageKeys.NotFound));
            EnsureMutable(entity);
            CheckVersion(entity.RowVersion, request.ExpectedRowVersion);
            await ValidateAssignmentAsync(request.WarehouseId, request.AssignedUserId, token);
            var old = new { entity.WarehouseId, entity.AssignedUserId };
            entity.WarehouseId = request.WarehouseId;
            entity.AssignedUserId = request.AssignedUserId;
            entity.StartedAtUtc ??= request.AssignedUserId.HasValue ? DateTimeOffset.UtcNow : null;
            entity.UpdatedBy = actor;
            entity.UpdatedDate = DateTime.UtcNow;
            await SaveAsync(token);
            await audit.WriteAsync(new AuditLogWriteEntry(
                "kkd.request.assign", nameof(KkdRequest), entity.Id.ToString(), "Succeeded", "kkd-request",
                OldValues: old, NewValues: new { entity.WarehouseId, entity.AssignedUserId },
                ChangedFields: ["WarehouseId", "AssignedUserId"]), token);
            return await GetDetailAsync(entity.Id, token);
        }, ct, IsolationLevel.Serializable);
    }

    public async Task<KkdRequestDetail> CancelAsync(long id, KkdRequestCancelRequest request, long actor, CancellationToken ct = default)
    {
        ValidateKey(request.IdempotencyKey);
        ValidateReason(request.Reason);
        return await uow.ExecuteInTransactionAsync(async token =>
        {
            var entity = await Requests.Query(true).Include(x => x.Lines)
                .SingleOrDefaultAsync(x => x.Id == id, token)
                ?? throw AppException.NotFound(Message(KkdRequestMessageKeys.NotFound));
            if (entity.Status == KkdRequestStatus.Cancelled) return await GetDetailAsync(entity.Id, token);
            EnsureMutable(entity);
            CheckVersion(entity.RowVersion, request.ExpectedRowVersion);
            if (entity.Lines.Any(x => x.AllocatedQuantity > 0 || x.DeliveredQuantity > 0))
                throw AppException.Conflict(Message(KkdRequestMessageKeys.RequestHasProgress));
            var old = Snapshot(entity);
            var now = DateTimeOffset.UtcNow;
            entity.Status = KkdRequestStatus.Cancelled;
            entity.CancelledAtUtc = now;
            entity.CancellationReason = request.Reason.Trim();
            entity.UpdatedBy = actor;
            entity.UpdatedDate = now.UtcDateTime;
            foreach (var line in entity.Lines)
            {
                line.CancelledQuantity = line.RequestedQuantity;
                line.Status = KkdRequestLineStatus.Cancelled;
                line.UpdatedBy = actor;
                line.UpdatedDate = now.UtcDateTime;
            }
            await SaveAsync(token);
            await audit.WriteAsync(new AuditLogWriteEntry(
                "kkd.request.cancel", nameof(KkdRequest), entity.Id.ToString(), "Succeeded", "kkd-request",
                Reason: request.Reason.Trim(), OldValues: old, NewValues: Snapshot(entity),
                ChangedFields: ["Status", "CancelledAtUtc", "CancellationReason"]), token);
            return await GetDetailAsync(entity.Id, token);
        }, ct, IsolationLevel.Serializable);
    }

    private IQueryable<KkdRequest> ApplySearch(IQueryable<KkdRequest> query, PagedRequest request)
    {
        var search = request.EffectiveSearch?.Trim();
        if (string.IsNullOrWhiteSpace(search)) return query;
        var fields = request.SearchFields.Count == 0 ? DefaultSearchFields : request.SearchFields;
        foreach (var field in fields)
            if (!AllowedSearchFields.Contains(field))
                throw AppException.BadRequest(Message(KkdRequestMessageKeys.InvalidSearchField, field));

        var selected = fields.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var term in search.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var value = term;
            var numeric = long.TryParse(value, out var id);
            query = query.Where(x =>
                (selected.Contains("id") && numeric && x.Id == id)
                || (selected.Contains("requestNo") && x.RequestNo.Contains(value))
                || (selected.Contains("employeeCode") && x.Employee.EmployeeCode.Contains(value))
                || (selected.Contains("employeeName") && (x.Employee.FirstName.Contains(value) || x.Employee.LastName.Contains(value)))
                || (selected.Contains("externalRequestNo") && x.ExternalRequestNo != null && x.ExternalRequestNo.Contains(value))
                || (selected.Contains("groupCode") && x.Lines.Any(line => line.GroupCode.Contains(value)))
                || (selected.Contains("groupName") && x.Lines.Any(line => line.GroupName != null && line.GroupName.Contains(value)))
                || (selected.Contains("stockCode") && x.Lines.Any(line => line.StockCodeSnapshot != null && line.StockCodeSnapshot.Contains(value)))
                || (selected.Contains("stockName") && x.Lines.Any(line => line.StockNameSnapshot != null && line.StockNameSnapshot.Contains(value)))
                || (selected.Contains("createdBy") && numeric && x.CreatedBy == id)
                || (selected.Contains("updatedBy") && numeric && x.UpdatedBy == id));
        }
        request.MarkSearchApplied();
        return query;
    }

    private IQueryable<KkdRequest> DetailQuery(bool tracking) => Requests.Query(tracking)
        .Include(x => x.Employee).ThenInclude(x => x.Department)
        .Include(x => x.Employee).ThenInclude(x => x.Role)
        .Include(x => x.Lines).ThenInclude(x => x.Resolutions);

    private async Task EnsureGroupEntitlementAsync(KkdEmployee employee, string groupCode, DateTimeOffset at, CancellationToken ct)
    {
        var date = DateOnly.FromDateTime(at.UtcDateTime);
        var allowed = await Rules.Query().AnyAsync(x => x.IsActive
            && x.GroupCode == groupCode
            && x.Matrix.IsActive
            && x.Matrix.CustomerId == employee.CustomerId
            && x.Matrix.DepartmentId == employee.DepartmentId
            && x.Matrix.RoleId == employee.RoleId
            && (!x.Matrix.EffectiveFrom.HasValue || x.Matrix.EffectiveFrom <= date)
            && (!x.Matrix.EffectiveTo.HasValue || x.Matrix.EffectiveTo >= date), ct);
        if (!allowed) throw AppException.Conflict(Message(KkdRequestMessageKeys.GroupEntitlementNotFound, groupCode));
    }

    private async Task<StockEntity> ValidateStockAsync(long stockId, string groupCode, CancellationToken ct)
    {
        var stock = await Stocks.FindByIdAsync(stockId, cancellationToken: ct)
            ?? throw AppException.NotFound(Message(KkdRequestMessageKeys.StockNotFound));
        if (!string.Equals(NormalizeCode(stock.GroupCode), groupCode, StringComparison.OrdinalIgnoreCase))
            throw AppException.Conflict(Message(KkdRequestMessageKeys.StockGroupMismatch, groupCode));
        return stock;
    }

    private async Task ValidateAssignmentAsync(long? warehouseId, long? assignedUserId, CancellationToken ct)
    {
        if (warehouseId.HasValue && !await Warehouses.AnyAsync(x => x.Id == warehouseId.Value, ct))
            throw AppException.NotFound(Message(KkdRequestMessageKeys.NotFound));
        if (assignedUserId.HasValue && !await Users.AnyAsync(x => x.Id == assignedUserId.Value && x.IsActive, ct))
            throw AppException.NotFound(Message(KkdRequestMessageKeys.UserNotFound));
    }

    private async Task UpsertPreferenceAsync(long employeeId, string groupCode, long stockId, DateTimeOffset at, CancellationToken ct)
    {
        var preference = await Preferences.Query(true)
            .SingleOrDefaultAsync(x => x.EmployeeId == employeeId && x.GroupCode == groupCode, ct);
        if (preference is null)
            await Preferences.AddAsync(new KkdEmployeeStockPreference
            {
                EmployeeId = employeeId,
                GroupCode = groupCode,
                StockId = stockId,
                LastSelectedAtUtc = at
            }, ct);
        else
        {
            preference.StockId = stockId;
            preference.LastSelectedAtUtc = at;
        }
    }

    private static KkdRequestDetail MapDetail(KkdRequest x) => new(
        x.Id, x.CorrelationId, x.RequestNo, x.Status.ToString(), x.Priority.ToString(), x.SourceType.ToString(),
        x.EmployeeId, x.Employee.EmployeeCode, $"{x.Employee.FirstName} {x.Employee.LastName}".Trim(),
        x.Employee.Department.Name, x.Employee.Role.Name, x.CustomerId, x.WarehouseId, x.AssignedUserId,
        x.ExternalRequestNo, x.RequestedAtUtc, x.NeededAtUtc, x.StartedAtUtc, x.ReadyAtUtc, x.CompletedAtUtc,
        x.CancelledAtUtc, x.CancellationReason, x.Description, Convert.ToBase64String(x.RowVersion),
        x.Lines.OrderBy(line => line.LineNo).Select(line => new KkdRequestLineDetail(
            line.Id, line.LineNo, line.GroupCode, line.GroupName, line.StockId, line.StockCodeSnapshot,
            line.StockNameSnapshot, line.UnitCode, line.RequestedQuantity, line.AllocatedQuantity,
            line.DeliveredQuantity, Math.Max(0, line.RequestedQuantity - line.AllocatedQuantity - line.DeliveredQuantity - line.CancelledQuantity),
            line.Status.ToString(), line.ExternalOrderNo, line.ExternalOrderLineId, line.ResolvedByUserId,
            line.ResolvedAtUtc, line.ResolutionReason, Convert.ToBase64String(line.RowVersion),
            line.Resolutions.OrderByDescending(item => item.ResolvedAtUtc).Select(item => new KkdRequestLineResolutionRow(
                item.Id, item.PreviousStockId, item.StockId, item.StockCodeSnapshot, item.StockNameSnapshot,
                item.Reason, item.CreatedBy, item.ResolvedAtUtc)).ToArray())).ToArray());

    private void ValidateCreate(KkdRequestCreateRequest request)
    {
        ValidateKey(request.IdempotencyKey);
        if (request.Lines is null || request.Lines.Count is < 1 or > 100)
            throw AppException.BadRequest(Message(KkdRequestMessageKeys.InvalidLines));
        if (request.Description?.Length > 2000)
            throw AppException.BadRequest(Message(KkdRequestMessageKeys.InvalidDescription));
        foreach (var line in request.Lines)
        {
            if (line.Quantity <= 0) throw AppException.BadRequest(Message(KkdRequestMessageKeys.InvalidQuantity));
            var group = NormalizeCode(line.GroupCode);
            if (group.Length is 0 or > 80) throw AppException.BadRequest(Message(KkdRequestMessageKeys.InvalidGroupCode));
        }
    }

    private void ValidateKey(Guid key)
    {
        if (key == Guid.Empty) throw AppException.BadRequest(Message(KkdRequestMessageKeys.InvalidIdempotencyKey));
    }

    private void ValidateReason(string? reason)
    {
        if (reason?.Trim().Length is not (>= 3 and <= 1000))
            throw AppException.BadRequest(Message(KkdRequestMessageKeys.InvalidReason));
    }

    private void EnsureMutable(KkdRequest entity)
    {
        if (entity.Status is KkdRequestStatus.Completed or KkdRequestStatus.Cancelled)
            throw AppException.Conflict(Message(KkdRequestMessageKeys.ClosedRequestCannotChange));
    }

    private void CheckVersion(byte[] current, string? expected)
    {
        if (string.IsNullOrWhiteSpace(expected)) return;
        byte[] decoded;
        try { decoded = Convert.FromBase64String(expected); }
        catch { throw AppException.Conflict(Message(KkdRequestMessageKeys.ConcurrencyConflict)); }
        if (!current.SequenceEqual(decoded))
            throw AppException.Conflict(Message(KkdRequestMessageKeys.ConcurrencyConflict));
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        try { await uow.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw AppException.Conflict(Message(KkdRequestMessageKeys.ConcurrencyConflict)); }
    }

    private string Message(string key, params object[] args) => localizer[key, args].Value;
    private static string NormalizeCode(string? value) => value?.Trim().ToUpperInvariant() ?? string.Empty;
    private static string? Normalize(string? value, int maxLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized)) return null;
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }

    private static object Snapshot(KkdRequest x) => new
    {
        x.Id, x.RequestNo, x.EmployeeId, x.CustomerId, x.WarehouseId, x.AssignedUserId,
        x.SourceType, x.ExternalRequestNo, x.Priority, x.Status, x.RequestedAtUtc, x.NeededAtUtc,
        x.StartedAtUtc, x.ReadyAtUtc, x.CompletedAtUtc, x.CancelledAtUtc, x.CancellationReason,
        Lines = x.Lines.Select(line => new { line.Id, line.LineNo, line.GroupCode, line.StockId, line.RequestedQuantity, line.AllocatedQuantity, line.DeliveredQuantity, line.Status })
    };

    private static readonly string[] RequestFields =
        ["EmployeeId", "CustomerId", "WarehouseId", "AssignedUserId", "SourceType", "ExternalRequestNo", "Priority", "Status", "NeededAtUtc", "Description", "Lines"];
}
