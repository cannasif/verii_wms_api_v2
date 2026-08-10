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

    public async Task<PagedResponse<KkdRequestGridRow>> GetPagedAsync(PagedRequest request, long actor, KkdRequestBoardTab tab = KkdRequestBoardTab.All, CancellationToken ct = default)
    {
        var warehouseScope = await ActorWarehouseScopeAsync(actor, ct);
        var query = ApplyTabFilter(await AuthorizedRequestsAsync(actor, warehouseScope, ct), tab, actor, warehouseScope);
        var rows = ApplySearch(query, request).Select(x => new KkdRequestGridRow(
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

        var page = await rows
            .ApplyAdvancedFilters(request)
            .ApplySort(request, nameof(KkdRequestGridRow.RequestedAtUtc))
            .ToPagedResponseAsync(request, ct);
        return await EnrichPageAsync(page, actor, ct);
    }

    public async Task<KkdRequestTabCounts> GetTabCountsAsync(long actor, CancellationToken ct = default)
    {
        var warehouseScope = await ActorWarehouseScopeAsync(actor, ct);
        var query = await AuthorizedRequestsAsync(actor, warehouseScope, ct);
        var pending = await ApplyTabFilter(query, KkdRequestBoardTab.Pending, actor, warehouseScope).CountAsync(ct);
        var preparing = await ApplyTabFilter(query, KkdRequestBoardTab.Preparing, actor, warehouseScope).CountAsync(ct);
        var completed = await ApplyTabFilter(query, KkdRequestBoardTab.Completed, actor, warehouseScope).CountAsync(ct);
        var cancelled = await ApplyTabFilter(query, KkdRequestBoardTab.Cancelled, actor, warehouseScope).CountAsync(ct);
        var mine = await ApplyTabFilter(query, KkdRequestBoardTab.Mine, actor, warehouseScope).CountAsync(ct);
        return new KkdRequestTabCounts(pending, preparing, completed, cancelled, mine);
    }

    /// <summary>
    /// Beklemede = henüz görevlenmemiş açık kalemleri olan talepler,
    /// Hazırlamada = aktif görevi olan talepler,
    /// Benim İşlerim = aktöre atanmış görevler + (depo kısıtlı kullanıcıda) kendi depolarındaki havuz görevleri.
    /// </summary>
    private IQueryable<KkdRequest> ApplyTabFilter(
        IQueryable<KkdRequest> query,
        KkdRequestBoardTab tab,
        long actor,
        ActorWarehouseScope warehouseScope)
    {
        var tasks = uow.Repository<KkdPreparationTask>().Query();
        var restricted = warehouseScope.IsRestricted;
        var warehouseIds = warehouseScope.WarehouseIds;
        return tab switch
        {
            KkdRequestBoardTab.Pending => query.Where(x =>
                x.Status != KkdRequestStatus.Completed && x.Status != KkdRequestStatus.Cancelled
                && x.AssignedUserId == null
                && x.Lines.Any(line =>
                    line.Status != KkdRequestLineStatus.Cancelled && line.Status != KkdRequestLineStatus.Completed
                    && !tasks.Any(task =>
                        (task.Status == KkdPreparationTaskStatus.Assigned || task.Status == KkdPreparationTaskStatus.InPreparation)
                        && task.Lines.Any(taskLine => taskLine.RequestLineId == line.Id)))),
            KkdRequestBoardTab.Preparing => query.Where(x =>
                x.Status != KkdRequestStatus.Completed && x.Status != KkdRequestStatus.Cancelled
                && (x.AssignedUserId != null || tasks.Any(task => task.RequestId == x.Id
                    && (task.Status == KkdPreparationTaskStatus.Assigned || task.Status == KkdPreparationTaskStatus.InPreparation)))),
            KkdRequestBoardTab.Completed => query.Where(x => x.Status == KkdRequestStatus.Completed),
            KkdRequestBoardTab.Cancelled => query.Where(x => x.Status == KkdRequestStatus.Cancelled),
            // Depoya bırakılan havuz görevleri, o depoya yetkili kullanıcıların Benim İşlerim'inde görünür.
            KkdRequestBoardTab.Mine => query.Where(x =>
                x.Status != KkdRequestStatus.Completed && x.Status != KkdRequestStatus.Cancelled
                && (x.AssignedUserId == actor || tasks.Any(task => task.RequestId == x.Id
                    && (task.Status == KkdPreparationTaskStatus.Assigned || task.Status == KkdPreparationTaskStatus.InPreparation)
                    && (task.AssignedUserId == actor
                        || (task.AssignedUserId == null && restricted && warehouseIds.Contains(task.WarehouseId)))))),
            _ => query,
        };
    }

    /// <summary>Sayfadaki satırlara atanan kullanıcı adı, satır sürümü, bağlı dağıtım/ambar çıkışı ve görev bilgilerini ekler.</summary>
    private async Task<PagedResponse<KkdRequestGridRow>> EnrichPageAsync(PagedResponse<KkdRequestGridRow> page, long actor, CancellationToken ct)
    {
        if (page.Items.Count == 0) return page;

        var requestIds = page.Items.Select(x => x.Id).ToArray();

        var rowVersions = await Requests.Query().Where(x => requestIds.Contains(x.Id))
            .Select(x => new { x.Id, x.RowVersion })
            .ToDictionaryAsync(x => x.Id, x => x.RowVersion, ct);

        var distributions = await uow.Repository<KkdDistribution>().Query()
            .Where(x => x.KkdRequestId.HasValue && requestIds.Contains(x.KkdRequestId.Value))
            .OrderByDescending(x => x.Id)
            .Select(x => new { RequestId = x.KkdRequestId!.Value, x.Id, x.WarehouseOutboundId, x.Status, x.FailureReason })
            .ToListAsync(ct);
        var latestDistribution = distributions
            .GroupBy(x => x.RequestId)
            .ToDictionary(x => x.Key, x => x.First());

        var activeTasks = await uow.Repository<KkdPreparationTask>().Query()
            .Where(x => requestIds.Contains(x.RequestId)
                && (x.Status == KkdPreparationTaskStatus.Assigned || x.Status == KkdPreparationTaskStatus.InPreparation))
            .Select(x => new { x.RequestId, x.Id, x.AssignedUserId, LineIds = x.Lines.Select(l => l.RequestLineId) })
            .ToListAsync(ct);
        var tasksByRequest = activeTasks.ToLookup(x => x.RequestId);
        var coveredLineIds = activeTasks.SelectMany(x => x.LineIds).ToHashSet();

        var openLines = await uow.Repository<KkdRequestLine>().Query()
            .Where(x => requestIds.Contains(x.RequestId)
                && x.Status != KkdRequestLineStatus.Cancelled && x.Status != KkdRequestLineStatus.Completed)
            .Select(x => new { x.RequestId, x.Id })
            .ToListAsync(ct);
        var unassignedByRequest = openLines
            .Where(x => !coveredLineIds.Contains(x.Id))
            .GroupBy(x => x.RequestId)
            .ToDictionary(x => x.Key, x => x.Count());

        var userIds = page.Items.Where(x => x.AssignedUserId.HasValue).Select(x => x.AssignedUserId!.Value)
            .Concat(activeTasks.Where(x => x.AssignedUserId.HasValue).Select(x => x.AssignedUserId!.Value))
            .Distinct().ToArray();
        var usernames = userIds.Length == 0
            ? new Dictionary<long, string>()
            : await Users.Query().Where(x => userIds.Contains(x.Id))
                .Select(x => new { x.Id, DisplayName = x.Detail == null || (x.Detail.FirstName == "" && x.Detail.LastName == "")
                    ? x.Username : (x.Detail.FirstName + " " + x.Detail.LastName).Trim() })
                .ToDictionaryAsync(x => x.Id, x => x.DisplayName, ct);

        var items = page.Items.Select(item =>
        {
            var requestTasks = tasksByRequest[item.Id].ToArray();
            var assigneeNames = requestTasks
                .Where(x => x.AssignedUserId.HasValue)
                .Select(x => usernames.GetValueOrDefault(x.AssignedUserId!.Value, $"#{x.AssignedUserId}"))
                .Distinct().ToArray();
            var poolTask = requestTasks.FirstOrDefault(x => !x.AssignedUserId.HasValue);
            return item with
            {
                AssignedUserName = item.AssignedUserId.HasValue ? usernames.GetValueOrDefault(item.AssignedUserId.Value) : null,
                RowVersion = rowVersions.TryGetValue(item.Id, out var version) ? Convert.ToBase64String(version) : string.Empty,
                LinkedDistributionId = latestDistribution.TryGetValue(item.Id, out var distribution) ? distribution.Id : null,
                LinkedDistributionStatus = latestDistribution.TryGetValue(item.Id, out var status) ? status.Status.ToString() : null,
                LinkedDistributionFailureReason = latestDistribution.TryGetValue(item.Id, out var failure) ? failure.FailureReason : null,
                WarehouseOutboundId = latestDistribution.TryGetValue(item.Id, out var linked) ? linked.WarehouseOutboundId : null,
                ActiveTaskCount = requestTasks.Length,
                UnassignedLineCount = unassignedByRequest.GetValueOrDefault(item.Id),
                MyActiveTaskId = requestTasks.FirstOrDefault(x => x.AssignedUserId == actor)?.Id,
                ActiveAssigneeNames = assigneeNames,
                HasPoolTask = poolTask is not null,
                PoolTaskId = poolTask?.Id,
            };
        }).ToArray();

        return new PagedResponse<KkdRequestGridRow>
        {
            Items = items,
            TotalCount = page.TotalCount,
            PageNumber = page.PageNumber,
            PageSize = page.PageSize,
        };
    }

    public async Task<KkdRequestDetail> GetDetailAsync(long id, long actor, CancellationToken ct = default)
    {
        var entity = await DetailQuery(false).SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw AppException.NotFound(Message(KkdRequestMessageKeys.NotFound));
        if (entity.WarehouseId.HasValue)
            await EnsureWarehouseAccessAsync(actor, entity.WarehouseId.Value, ct);
        return MapDetail(entity);
    }

    public async Task<KkdRequestDetail> CreateAsync(KkdRequestCreateRequest request, long actor, CancellationToken ct = default)
    {
        ValidateCreate(request);
        return await uow.ExecuteInTransactionAsync(async token =>
        {
            var existing = await Requests.Query().SingleOrDefaultAsync(x => x.CorrelationId == request.IdempotencyKey, token);
            if (existing is not null) return await GetDetailAsync(existing.Id, actor, token);

            var employee = await Employees.Query().Include(x => x.Department).Include(x => x.Role)
                .SingleOrDefaultAsync(x => x.Id == request.EmployeeId, token)
                ?? throw AppException.NotFound(Message(KkdRequestMessageKeys.EmployeeNotFound));
            if (!employee.IsActive) throw AppException.Conflict(Message(KkdRequestMessageKeys.EmployeeInactive));

            await ValidateAssignmentAsync(request.WarehouseId, request.AssignedUserId, actor, token);
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
            return await GetDetailAsync(entity.Id, actor, token);
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
                return await GetDetailAsync(requestId, actor, token);
            }

            var entity = await Requests.Query(true).Include(x => x.Employee).Include(x => x.Lines)
                .SingleOrDefaultAsync(x => x.Id == requestId, token)
                ?? throw AppException.NotFound(Message(KkdRequestMessageKeys.NotFound));
            EnsureMutable(entity);
            if (entity.WarehouseId.HasValue)
                await EnsureWarehouseAccessAsync(actor, entity.WarehouseId.Value, token);
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
            return await GetDetailAsync(entity.Id, actor, token);
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
            // Aynı hedef durum tekrar istenirse (ör. ağ zaman aşımı sonrası yeniden deneme), RowVersion
            // uyuşmazlığına düşmeden başarı döndür — talep zaten istenen durumda.
            if (entity.WarehouseId == request.WarehouseId && entity.AssignedUserId == request.AssignedUserId)
                return await GetDetailAsync(entity.Id, actor, token);
            CheckVersion(entity.RowVersion, request.ExpectedRowVersion);
            await ValidateAssignmentAsync(request.WarehouseId, request.AssignedUserId, actor, token);
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
            return await GetDetailAsync(entity.Id, actor, token);
        }, ct, IsolationLevel.Serializable);
    }

    /// <summary>Üretimdeki iptal precheck karşılığı: ilerlemesi olan talep iptal edilemez, engel listesi döner.</summary>
    public async Task<KkdRequestCancelPrecheckResult> GetCancelPrecheckAsync(long id, CancellationToken ct = default)
    {
        var entity = await Requests.Query().Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw AppException.NotFound(Message(KkdRequestMessageKeys.NotFound));

        var blockers = new List<string>();
        long? activeDistributionId = null;
        long? activeOutboundId = null;

        if (entity.Status is KkdRequestStatus.Completed or KkdRequestStatus.Cancelled)
            blockers.Add(Message(KkdRequestMessageKeys.ClosedRequestCannotChange));

        if (entity.Lines.Any(x => x.AllocatedQuantity > 0 || x.DeliveredQuantity > 0))
        {
            blockers.Add(Message(KkdRequestMessageKeys.RequestHasProgress));
            var activeDistribution = await uow.Repository<KkdDistribution>().Query()
                .Where(x => x.KkdRequestId == id
                    && x.Status != KkdDistributionStatus.Cancelled && x.Status != KkdDistributionStatus.Failed)
                .OrderByDescending(x => x.Id)
                .Select(x => new { x.Id, x.WarehouseOutboundId })
                .FirstOrDefaultAsync(ct);
            activeDistributionId = activeDistribution?.Id;
            activeOutboundId = activeDistribution?.WarehouseOutboundId;
        }

        return new KkdRequestCancelPrecheckResult(blockers.Count == 0, blockers, activeDistributionId, activeOutboundId);
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
            if (entity.Status == KkdRequestStatus.Cancelled) return await GetDetailAsync(entity.Id, actor, token);
            EnsureMutable(entity);
            if (entity.WarehouseId.HasValue)
                await EnsureWarehouseAccessAsync(actor, entity.WarehouseId.Value, token);
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
            // Talep iptalinde açık hazırlama görevleri de kapanır.
            var openTasks = await uow.Repository<KkdPreparationTask>().Query(true)
                .Where(x => x.RequestId == entity.Id
                    && (x.Status == KkdPreparationTaskStatus.Assigned || x.Status == KkdPreparationTaskStatus.InPreparation))
                .ToListAsync(token);
            foreach (var task in openTasks)
            {
                task.Status = KkdPreparationTaskStatus.Cancelled;
                task.ClosedAtUtc = now;
                task.ClosureReason = request.Reason.Trim();
                task.UpdatedBy = actor;
                task.UpdatedDate = now.UtcDateTime;
            }
            await SaveAsync(token);
            await audit.WriteAsync(new AuditLogWriteEntry(
                "kkd.request.cancel", nameof(KkdRequest), entity.Id.ToString(), "Succeeded", "kkd-request",
                Reason: request.Reason.Trim(), OldValues: old, NewValues: Snapshot(entity),
                ChangedFields: ["Status", "CancelledAtUtc", "CancellationReason"]), token);
            return await GetDetailAsync(entity.Id, actor, token);
        }, ct, IsolationLevel.Serializable);
    }

    /// <summary>İptal edilmiş bir talebi tekrar beklemeye alır. Hazırlamada sekmesindeki müdür "beklemeye geri al" işleminden
    /// (aktif bir görevin iadesi) ayrıdır — burada talep zaten Cancelled durumundan başlar.</summary>
    public async Task<KkdRequestDetail> ReactivateAsync(long id, KkdRequestReactivateRequest request, long actor, CancellationToken ct = default)
    {
        return await uow.ExecuteInTransactionAsync(async token =>
        {
            var entity = await Requests.Query(true).Include(x => x.Lines)
                .SingleOrDefaultAsync(x => x.Id == id, token)
                ?? throw AppException.NotFound(Message(KkdRequestMessageKeys.NotFound));
            if (entity.Status != KkdRequestStatus.Cancelled)
                throw AppException.Conflict(Message(KkdRequestMessageKeys.NotCancelled));
            if (entity.WarehouseId.HasValue)
                await EnsureWarehouseAccessAsync(actor, entity.WarehouseId.Value, token);
            CheckVersion(entity.RowVersion, request.ExpectedRowVersion);
            var old = Snapshot(entity);
            var now = DateTimeOffset.UtcNow;
            entity.CancelledAtUtc = null;
            entity.CancellationReason = null;
            entity.UpdatedBy = actor;
            entity.UpdatedDate = now.UtcDateTime;
            foreach (var line in entity.Lines)
            {
                line.CancelledQuantity = 0;
                line.Status = line.StockId is null ? KkdRequestLineStatus.AwaitingStockSelection : KkdRequestLineStatus.ReadyToPrepare;
                line.UpdatedBy = actor;
                line.UpdatedDate = now.UtcDateTime;
            }
            KkdRequestStateMachine.Refresh(entity, now);
            await SaveAsync(token);
            await audit.WriteAsync(new AuditLogWriteEntry(
                "kkd.request.reactivate", nameof(KkdRequest), entity.Id.ToString(), "Succeeded", "kkd-request",
                OldValues: old, NewValues: Snapshot(entity),
                ChangedFields: ["Status", "CancelledAtUtc", "CancellationReason"]), token);
            return await GetDetailAsync(entity.Id, actor, token);
        }, ct, IsolationLevel.Serializable);
    }

    private sealed record ActorWarehouseScope(bool IsRestricted, long[] WarehouseIds);

    /// <summary>
    /// Depo ataması yoksa (veya süperadmin) kısıtsız = müdür görünümü.
    /// En az bir depoya bağlıysa sadece o depoların işlerini görür.
    /// </summary>
    private async Task<ActorWarehouseScope> ActorWarehouseScopeAsync(long actor, CancellationToken ct)
    {
        var role = await Users.Query().Where(x => x.Id == actor && x.IsActive).Select(x => x.Role).FirstOrDefaultAsync(ct);
        if (!string.IsNullOrWhiteSpace(role)
            && (role.Equals("Admin", StringComparison.OrdinalIgnoreCase)
                || role.Equals("superadmin", StringComparison.OrdinalIgnoreCase)))
            return new ActorWarehouseScope(false, []);

        var warehouseIds = await uow.Repository<UserWarehouseAssignment>().Query()
            .Where(x => x.UserId == actor)
            .Select(x => x.WarehouseId)
            .Distinct()
            .ToArrayAsync(ct);
        return warehouseIds.Length == 0
            ? new ActorWarehouseScope(false, [])
            : new ActorWarehouseScope(true, warehouseIds);
    }

    private Task<IQueryable<KkdRequest>> AuthorizedRequestsAsync(long actor, ActorWarehouseScope scope, CancellationToken ct)
    {
        _ = actor;
        _ = ct;
        var query = Requests.Query();
        // Depoya bağlanmamış (henüz triyaj edilmemiş) talepler herkese görünür kalır; depo atanmış
        // talepler ise yalnızca o depoya yetkili kullanıcılara gösterilir. Depo kısıtlaması olmayan
        // kullanıcılar (ör. müdür) için filtre uygulanmaz.
        if (!scope.IsRestricted) return Task.FromResult(query);
        var warehouseIds = scope.WarehouseIds;
        return Task.FromResult(query.Where(x => x.WarehouseId == null || warehouseIds.Contains(x.WarehouseId.Value)));
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

    private async Task ValidateAssignmentAsync(long? warehouseId, long? assignedUserId, long actor, CancellationToken ct)
    {
        if (warehouseId.HasValue && !await Warehouses.AnyAsync(x => x.Id == warehouseId.Value, ct))
            throw AppException.NotFound(Message(KkdRequestMessageKeys.NotFound));
        if (assignedUserId.HasValue && !await Users.AnyAsync(x => x.Id == assignedUserId.Value && x.IsActive, ct))
            throw AppException.NotFound(Message(KkdRequestMessageKeys.UserNotFound));
        if (warehouseId.HasValue)
        {
            await EnsureWarehouseAccessAsync(actor, warehouseId.Value, ct);
            if (assignedUserId.HasValue)
                await EnsureWarehouseAccessAsync(assignedUserId.Value, warehouseId.Value, ct);
        }
    }

    /// <summary>Depo kısıtı olan (müdür olmayan) kullanıcılar yalnızca kendi depolarına ait talepleri görüp yönetebilir.</summary>
    private async Task EnsureWarehouseAccessAsync(long userId, long warehouseId, CancellationToken ct)
    {
        var warehouseIds = await uow.Repository<UserWarehouseAssignment>().Query().Where(x => x.UserId == userId)
            .Select(x => x.WarehouseId).ToArrayAsync(ct);
        if (warehouseIds.Length > 0 && !warehouseIds.Contains(warehouseId))
            throw AppException.Forbidden(Message(KkdRequestMessageKeys.WarehouseAccessDenied));
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
