using System.Data;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using verii_wms_api_v2.Modules.Audit.Application;
using verii_wms_api_v2.Modules.Identity.Domain;
using verii_wms_api_v2.Modules.Kkd.Domain;
using verii_wms_api_v2.Modules.Kkd.Localization;
using verii_wms_api_v2.Modules.StockBalance.Application;
using verii_wms_api_v2.Modules.StockBalance.Domain;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;
using StockEntity = verii_wms_api_v2.Modules.Stock.Domain.Stock;
using WarehouseEntity = verii_wms_api_v2.Modules.Warehouse.Domain.Warehouse;

namespace verii_wms_api_v2.Modules.Kkd.Application;

public sealed class KkdRequestService(
    IUnitOfWork uow,
    IAuditLogWriter audit,
    IStockBalanceService balances,
    IKkdDefinitionService definitions,
    IKkdEntitlementService entitlements,
    IStringLocalizer<KkdRequestResource> localizer) : IKkdRequestService
{
    private static readonly HashSet<string> AllowedSearchFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "id", "requestNo", "employeeCode", "employeeName", "externalRequestNo",
        "groupCode", "groupName", "stockCode", "stockName", "createdBy", "updatedBy",
        "totalLineCount", "unresolvedLineCount", "requestedQuantity", "allocatedQuantity", "deliveredQuantity"
    };

    private static readonly string[] DefaultSearchFields =
        ["requestNo", "employeeCode", "employeeName", "externalRequestNo", "groupCode", "groupName", "stockCode", "stockName"];
    private static readonly IReadOnlySet<string> LineSummaryColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "totalLineCount", "unresolvedLineCount", "requestedQuantity", "allocatedQuantity", "deliveredQuantity"
    };

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
        var searched = ApplySearch(query, request);
        var lines = uow.Repository<KkdRequestLine>().Query();
        var rows = BuildPagedQuery(request, searched, lines);
        var countQuery = BuildCountQuery(request, searched, lines);
        var page = await rows.ToPagedResponseAsync(countQuery, request, ct);
        if (page.Items.Count > 0 && !RequiresLineSummaryInMainQuery(request))
            page = new PagedResponse<KkdRequestGridRow>
            {
                Items = await EnrichLineSummariesAsync(page.Items, lines, ct),
                TotalCount = page.TotalCount,
                PageNumber = page.PageNumber,
                PageSize = page.PageSize
            };
        return await EnrichPageAsync(page, actor, ct);
    }

    internal static IQueryable<KkdRequestGridRow> BuildPagedQuery(
        PagedRequest request,
        IQueryable<KkdRequest> requests,
        IQueryable<KkdRequestLine> lines)
    {
        var rows = BuildGridRows(requests, lines, RequiresLineSummaryInMainQuery(request))
            .ApplyAdvancedFilters(request)
            .ApplySort(request, nameof(KkdRequestGridRow.RequestedAtUtc));
        return rows.Select(x => new KkdRequestGridRow(
            x.Id, x.RequestNo, x.Status, x.Priority, x.SourceType, x.EmployeeId, x.EmployeeCode, x.EmployeeName,
            x.DepartmentName, x.RoleName, x.WarehouseId, x.AssignedUserId, x.ExternalRequestNo,
            x.TotalLineCount, x.UnresolvedLineCount, x.RequestedQuantity, x.AllocatedQuantity, x.DeliveredQuantity,
            x.RequestedAtUtc, x.NeededAtUtc, x.CreatedBy, x.CreatedDate, x.UpdatedBy, x.UpdatedDate));
    }

    internal static IQueryable<long> BuildCountQuery(
        PagedRequest request,
        IQueryable<KkdRequest> requests,
        IQueryable<KkdRequestLine> lines) =>
        BuildGridRows(requests, lines, RequiresLineSummaryForCount(request))
            .ApplyAdvancedFilters(request)
            .Select(x => x.Id);

    private static IQueryable<KkdRequestGridProjection> BuildGridRows(
        IQueryable<KkdRequest> requests,
        IQueryable<KkdRequestLine> lines,
        bool includeLineSummary)
    {
        if (!includeLineSummary)
            return requests.Select(x => new KkdRequestGridProjection
            {
                Id = x.Id, RequestNo = x.RequestNo, Status = x.Status.ToString(), Priority = x.Priority.ToString(), SourceType = x.SourceType.ToString(),
                EmployeeId = x.EmployeeId, EmployeeCode = x.Employee.EmployeeCode,
                EmployeeName = (x.Employee.FirstName + " " + x.Employee.LastName).Trim(),
                DepartmentName = x.Employee.Department.Name, RoleName = x.Employee.Role.Name, WarehouseId = x.WarehouseId,
                AssignedUserId = x.AssignedUserId, ExternalRequestNo = x.ExternalRequestNo, RequestedAtUtc = x.RequestedAtUtc,
                NeededAtUtc = x.NeededAtUtc, CreatedBy = x.CreatedBy, CreatedDate = x.CreatedDate,
                UpdatedBy = x.UpdatedBy, UpdatedDate = x.UpdatedDate
            });

        var totals = lines.GroupBy(x => x.RequestId).Select(groupRows => new
        {
            RequestId = groupRows.Key,
            TotalLineCount = groupRows.Count(),
            UnresolvedLineCount = groupRows.Count(x => x.StockId == null && x.Status != KkdRequestLineStatus.Cancelled),
            RequestedQuantity = groupRows.Sum(x => x.RequestedQuantity),
            AllocatedQuantity = groupRows.Sum(x => x.AllocatedQuantity),
            DeliveredQuantity = groupRows.Sum(x => x.DeliveredQuantity)
        });
        return from request in requests
               join total in totals on request.Id equals total.RequestId into totalRows
               from total in totalRows.DefaultIfEmpty()
               select new KkdRequestGridProjection
               {
                   Id = request.Id, RequestNo = request.RequestNo, Status = request.Status.ToString(), Priority = request.Priority.ToString(),
                   SourceType = request.SourceType.ToString(), EmployeeId = request.EmployeeId, EmployeeCode = request.Employee.EmployeeCode,
                   EmployeeName = (request.Employee.FirstName + " " + request.Employee.LastName).Trim(),
                   DepartmentName = request.Employee.Department.Name, RoleName = request.Employee.Role.Name, WarehouseId = request.WarehouseId,
                   AssignedUserId = request.AssignedUserId, ExternalRequestNo = request.ExternalRequestNo,
                   TotalLineCount = (int?)total.TotalLineCount ?? 0, UnresolvedLineCount = (int?)total.UnresolvedLineCount ?? 0,
                   RequestedQuantity = (decimal?)total.RequestedQuantity ?? 0, AllocatedQuantity = (decimal?)total.AllocatedQuantity ?? 0,
                   DeliveredQuantity = (decimal?)total.DeliveredQuantity ?? 0, RequestedAtUtc = request.RequestedAtUtc,
                   NeededAtUtc = request.NeededAtUtc, CreatedBy = request.CreatedBy, CreatedDate = request.CreatedDate,
                   UpdatedBy = request.UpdatedBy, UpdatedDate = request.UpdatedDate
               };
    }

    private static bool RequiresLineSummaryForCount(PagedRequest request) =>
        request.Filters.Any(filter => LineSummaryColumns.Contains(filter.Column));

    private static bool RequiresLineSummaryInMainQuery(PagedRequest request) =>
        RequiresLineSummaryForCount(request) || LineSummaryColumns.Contains(request.SortBy ?? string.Empty);

    private static async Task<IReadOnlyList<KkdRequestGridRow>> EnrichLineSummariesAsync(
        IReadOnlyList<KkdRequestGridRow> rows,
        IQueryable<KkdRequestLine> lines,
        CancellationToken ct)
    {
        var requestIds = rows.Select(x => x.Id).ToArray();
        var totals = await lines.Where(x => requestIds.Contains(x.RequestId)).GroupBy(x => x.RequestId)
            .Select(groupRows => new
            {
                RequestId = groupRows.Key,
                TotalLineCount = groupRows.Count(),
                UnresolvedLineCount = groupRows.Count(x => x.StockId == null && x.Status != KkdRequestLineStatus.Cancelled),
                RequestedQuantity = groupRows.Sum(x => x.RequestedQuantity),
                AllocatedQuantity = groupRows.Sum(x => x.AllocatedQuantity),
                DeliveredQuantity = groupRows.Sum(x => x.DeliveredQuantity)
            }).ToDictionaryAsync(x => x.RequestId, ct);
        return rows.Select(row => totals.TryGetValue(row.Id, out var total)
            ? row with
            {
                TotalLineCount = total.TotalLineCount,
                UnresolvedLineCount = total.UnresolvedLineCount,
                RequestedQuantity = total.RequestedQuantity,
                AllocatedQuantity = total.AllocatedQuantity,
                DeliveredQuantity = total.DeliveredQuantity
            }
            : row).ToArray();
    }

    private sealed class KkdRequestGridProjection
    {
        public long Id { get; init; }
        public string RequestNo { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public string Priority { get; init; } = string.Empty;
        public string SourceType { get; init; } = string.Empty;
        public long EmployeeId { get; init; }
        public string EmployeeCode { get; init; } = string.Empty;
        public string EmployeeName { get; init; } = string.Empty;
        public string DepartmentName { get; init; } = string.Empty;
        public string RoleName { get; init; } = string.Empty;
        public long? WarehouseId { get; init; }
        public long? AssignedUserId { get; init; }
        public string? ExternalRequestNo { get; init; }
        public int TotalLineCount { get; init; }
        public int UnresolvedLineCount { get; init; }
        public decimal RequestedQuantity { get; init; }
        public decimal AllocatedQuantity { get; init; }
        public decimal DeliveredQuantity { get; init; }
        public DateTimeOffset RequestedAtUtc { get; init; }
        public DateTimeOffset? NeededAtUtc { get; init; }
        public long? CreatedBy { get; init; }
        public DateTime? CreatedDate { get; init; }
        public long? UpdatedBy { get; init; }
        public DateTime? UpdatedDate { get; init; }
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
        var quotaPending = await ApplyTabFilter(query, KkdRequestBoardTab.QuotaPending, actor, warehouseScope).CountAsync(ct);
        return new KkdRequestTabCounts(pending, preparing, completed, cancelled, mine, quotaPending);
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
            // Sadece kota onay yetkisi olanlara gösterilir (kontrol controller/frontend'de) — henüz karar
            // verilmemiş (Pending) kalemi olan talepler; Rejected zaten kararlanmış, kuyrukta beklemez.
            KkdRequestBoardTab.QuotaPending => query.Where(x => x.Lines.Any(line =>
                line.QuotaDecision == KkdRequestLineQuotaDecision.Pending)),
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
            .Select(x => new
            {
                x.RequestId, x.Id, x.AssignedUserId, x.StartedAtUtc,
                LineIds = x.Lines.Select(l => l.RequestLineId),
                PreparedQuantity = x.Lines.Sum(l => l.PreparedQuantity),
                QuotaPendingCount = x.Lines.Count(l => l.RequestLine.QuotaDecision == KkdRequestLineQuotaDecision.Pending
                    || l.RequestLine.QuotaDecision == KkdRequestLineQuotaDecision.Rejected),
                QuotaApprovedCount = x.Lines.Count(l => l.RequestLine.QuotaDecision == KkdRequestLineQuotaDecision.Approved),
            })
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
            var myTask = requestTasks.FirstOrDefault(x => x.AssignedUserId == actor);
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
                MyActiveTaskId = myTask?.Id,
                // "Bu işi yapıyorum" ile başlatıldı mı (raf ataması + rezervasyon yapıldı mı) — board'da
                // "Toplama yap" ile "İşe devam et" ayrımı ve taslak/devam eden göstergesi için.
                MyActiveTaskStarted = myTask?.StartedAtUtc is not null,
                MyActiveTaskPreparedQuantity = myTask?.PreparedQuantity ?? 0,
                MyActiveTaskQuotaPendingCount = myTask?.QuotaPendingCount ?? 0,
                MyActiveTaskQuotaApprovedCount = myTask?.QuotaApprovedCount ?? 0,
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

            // Barkod okutulamadığında/yanlış stoğa bağlandığında "Stok listesi" ile her zaman yeniden
            // bağlanabilir — ama gerçek toplama (PreparedQuantity>0, canlı stok hareketi zaten postalanmış)
            // başladıysa artık değiştirilemez. Henüz sadece rezerve edilmiş (raf ayrılmış ama toplanmamış)
            // raflar varsa, eski stoktan serbest bırakılır ki raf kilitli kalmasın.
            var taskLines = await uow.Repository<KkdPreparationTaskLine>().Query(true)
                .Include(x => x.Task)
                .Include(x => x.Locations)
                .Where(x => x.RequestLineId == line.Id)
                .ToListAsync(token);
            if (taskLines.Any(x => x.PreparedQuantity > 0))
                throw AppException.Conflict(Message(KkdRequestMessageKeys.StockCannotChange));

            var stock = await ValidateStockAsync(request.StockId, line.GroupCode, token);
            await EnsureGroupEntitlementAsync(entity.Employee, line.GroupCode, DateTimeOffset.UtcNow, token);
            var old = new { line.StockId, line.StockCodeSnapshot, line.StockNameSnapshot, line.Status };
            var now = DateTimeOffset.UtcNow;
            foreach (var taskLine in taskLines)
            {
                var existingLocations = taskLine.Locations.Where(l => l.ReservedQuantity > 0).ToArray();
                if (existingLocations.Length == 0) continue;
                await balances.PostReservationAsync(new(
                    $"{request.IdempotencyKey}:release-{taskLine.Id}", "KkdPreparationTaskLine", taskLine.Id, taskLine.Task.TaskNo,
                    StockReservationOperationTypes.Release, "KKD stok listesinden yeniden bağlama",
                    existingLocations.Select(l => new StockReservationLineRequest(
                        taskLine.Id, taskLine.Task.WarehouseId, l.LocationId, line.StockId!.Value, null,
                        line.UnitCode, l.LotNo, l.SerialNo, "Available", -l.ReservedQuantity)).ToList()), token);
                foreach (var loc in existingLocations)
                {
                    loc.ReservedQuantity = 0;
                    loc.UpdatedBy = actor;
                    loc.UpdatedDate = now.UtcDateTime;
                }
            }
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
            // Stoğu bilinmeyen (StockId=null) kalemler ClaimAsync/AssignAsync'teki kota kontrolünden hiç
            // geçmemişti; stok burada ilk kez bağlandığında da aynı kontrol yapılmalı, yoksa aşım sessizce kaçar.
            if (line.QuotaDecision == KkdRequestLineQuotaDecision.None)
            {
                var remaining = Math.Max(0, line.RequestedQuantity - line.CancelledQuantity);
                var quotaCheck = await entitlements.CheckAsync(
                    new(entity.EmployeeId, stock.Id, remaining, DateOnly.FromDateTime(now.UtcDateTime)), token);
                if (!quotaCheck.IsAllowed)
                    line.QuotaDecision = KkdRequestLineQuotaDecision.Pending;
            }
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

    /// <summary>Kota aşımı kararı — talebe özel. Onay, bu talebe/kaleme özgü (tek günlük) bir
    /// <see cref="KkdEmployeeEntitlementOverride"/> yaratır (<see cref="IKkdDefinitionService.CreateOverrideAsync"/>
    /// zaten var olan mekanizma, burada yeniden kullanılıyor). Talep iptal olursa bu override <see cref="CancelAsync"/>
    /// içinde geçersizleştirilir. Karar <see cref="KkdRequestLine"/> üzerinde tutulur — bu sayede iş devri
    /// (<see cref="KkdPreparationTaskService.HandoffAsync"/>) veya havuza iade sırasında kaybolmaz.</summary>
    public async Task<KkdQuotaDecisionResult> DecideQuotaAsync(long lineId, KkdQuotaDecisionRequest request, long actor, CancellationToken ct = default)
    {
        ValidateReason(request.Reason);
        return await uow.ExecuteInTransactionAsync(async token =>
        {
            var line = await uow.Repository<KkdRequestLine>().Query(true).Include(x => x.Request)
                .SingleOrDefaultAsync(x => x.Id == lineId, token)
                ?? throw AppException.NotFound(Message(KkdRequestMessageKeys.LineNotFound));
            if (line.Request.WarehouseId.HasValue)
                await EnsureWarehouseAccessAsync(actor, line.Request.WarehouseId.Value, token);

            var wantedDecision = request.Approve ? KkdRequestLineQuotaDecision.Approved : KkdRequestLineQuotaDecision.Rejected;
            if (line.QuotaDecision == wantedDecision)
                return new KkdQuotaDecisionResult(line.Id, line.QuotaDecision.ToString(), line.QuotaOverrideId);
            if (line.QuotaDecision is KkdRequestLineQuotaDecision.Approved or KkdRequestLineQuotaDecision.Rejected)
                throw AppException.Conflict(Message(KkdRequestMessageKeys.QuotaDecisionAlreadyMade));
            if (line.QuotaDecision == KkdRequestLineQuotaDecision.None)
            {
                // Bu satır "üzerime al" ile hiç Pending işaretlenmemiş olabilir (ör. Ata ekranından geliyor) —
                // ClaimAsync'in aksine burada Pending'e güvenemeyiz, kotayı canlı kontrol etmemiz gerekiyor.
                var remaining = Math.Max(0, line.RequestedQuantity - line.CancelledQuantity);
                var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);
                var isOverQuota = line.StockId.HasValue
                    && !(await entitlements.CheckAsync(new(line.Request.EmployeeId, line.StockId.Value, remaining, today), token)).IsAllowed;
                if (!isOverQuota)
                    throw AppException.Conflict(Message(KkdRequestMessageKeys.QuotaDecisionNotNeeded));
            }

            var now = DateTimeOffset.UtcNow;
            long? overrideId = null;
            if (request.Approve)
            {
                var remaining = Math.Max(0, line.RequestedQuantity - line.CancelledQuantity);
                var today = DateOnly.FromDateTime(now.UtcDateTime);
                overrideId = await definitions.CreateOverrideAsync(new KkdOverrideCreateRequest(
                    line.Request.EmployeeId, null, line.GroupCode, remaining, today, today,
                    $"{line.Request.RequestNo} kota onayı: {request.Reason.Trim()}"), actor, token);
            }
            line.QuotaDecision = wantedDecision;
            line.QuotaDecisionByUserId = actor;
            line.QuotaDecisionAtUtc = now;
            line.QuotaOverrideId = overrideId;
            line.UpdatedBy = actor;
            line.UpdatedDate = now.UtcDateTime;
            await SaveAsync(token);
            await audit.WriteAsync(new AuditLogWriteEntry(
                "kkd.request-line.quota-decision", nameof(KkdRequestLine), line.Id.ToString(), "Succeeded", "kkd-request",
                Reason: request.Reason.Trim(), NewValues: new { line.QuotaDecision, line.QuotaOverrideId },
                ChangedFields: ["QuotaDecision", "QuotaOverrideId"]), token);
            return new KkdQuotaDecisionResult(line.Id, line.QuotaDecision.ToString(), line.QuotaOverrideId);
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
            // Talep iptalinde açık hazırlama görevleri de kapanır.
            var openTasks = await uow.Repository<KkdPreparationTask>().Query(true)
                .Include(x => x.Lines).ThenInclude(x => x.RequestLine)
                .Include(x => x.Lines).ThenInclude(x => x.Locations)
                .Where(x => x.RequestId == entity.Id
                    && (x.Status == KkdPreparationTaskStatus.Assigned || x.Status == KkdPreparationTaskStatus.InPreparation))
                .ToListAsync(token);
            // Canlı toplama (gerçek stok hareketi) zaten yapılmış bir görev varsa iptal engellenir —
            // önce geri alma (Unpick) gerekir.
            if (openTasks.Any(x => x.Lines.Any(l => l.PreparedQuantity > 0)))
                throw AppException.Conflict(Message(KkdRequestMessageKeys.TaskHasProgress));

            var old = Snapshot(entity);
            var now = DateTimeOffset.UtcNow;
            entity.Status = KkdRequestStatus.Cancelled;
            entity.CancelledAtUtc = now;
            entity.CancellationReason = request.Reason.Trim();
            entity.UpdatedBy = actor;
            entity.UpdatedDate = now.UtcDateTime;
            // Onaylanmış kota istisnası bu talebe özeldi — talep iptal olunca o istisna da geçersiz olur,
            // personel bunu başka bir talepte kullanamaz.
            var overrideIds = entity.Lines.Where(x => x.QuotaOverrideId.HasValue).Select(x => x.QuotaOverrideId!.Value).ToArray();
            if (overrideIds.Length > 0)
            {
                var overrides = await uow.Repository<KkdEmployeeEntitlementOverride>().Query(true)
                    .Where(x => overrideIds.Contains(x.Id)).ToListAsync(token);
                foreach (var item in overrides)
                {
                    item.IsActive = false;
                    item.UpdatedBy = actor;
                    item.UpdatedDate = now.UtcDateTime;
                }
            }
            foreach (var line in entity.Lines)
            {
                line.CancelledQuantity = line.RequestedQuantity;
                line.Status = KkdRequestLineStatus.Cancelled;
                line.UpdatedBy = actor;
                line.UpdatedDate = now.UtcDateTime;
            }
            foreach (var task in openTasks)
            {
                // Henüz toplanmamış ama "Bu işi yapıyorum" ile rezerve edilmiş raflar varsa serbest bırak.
                foreach (var line in task.Lines.Where(x => x.RequestLine.StockId.HasValue
                    && x.Locations.Any(l => l.ReservedQuantity > 0)))
                {
                    var existingLocations = line.Locations.Where(l => l.ReservedQuantity > 0).ToArray();
                    await balances.PostReservationAsync(new(
                        $"{request.IdempotencyKey}:release-{line.Id}", "KkdPreparationTaskLine", line.Id, task.TaskNo,
                        StockReservationOperationTypes.Release, "KKD talebi iptal edildi",
                        existingLocations.Select(l => new StockReservationLineRequest(
                            line.Id, task.WarehouseId, l.LocationId, line.RequestLine.StockId!.Value, null,
                            line.RequestLine.UnitCode, l.LotNo, l.SerialNo, "Available", -l.ReservedQuantity)).ToList()), token);
                    foreach (var loc in existingLocations)
                    {
                        loc.ReservedQuantity = 0;
                        loc.UpdatedBy = actor;
                        loc.UpdatedDate = now.UtcDateTime;
                    }
                }
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
        if (string.IsNullOrWhiteSpace(search))
        {
            request.MarkSearchApplied();
            return query;
        }
        var fields = request.SearchFields.Count == 0 ? DefaultSearchFields : request.SearchFields;
        foreach (var field in fields)
            if (!AllowedSearchFields.Contains(field))
                throw AppException.BadRequest(Message(KkdRequestMessageKeys.InvalidSearchField, field));

        var selected = fields.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var term in search.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var value = term;
            var numeric = long.TryParse(value, out var id);
            var quantity = decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var decimalValue);
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
                || (selected.Contains("updatedBy") && numeric && x.UpdatedBy == id)
                || (selected.Contains("totalLineCount") && numeric && x.Lines.Count == id)
                || (selected.Contains("unresolvedLineCount") && numeric
                    && x.Lines.Count(line => line.StockId == null && line.Status != KkdRequestLineStatus.Cancelled) == id)
                || (selected.Contains("requestedQuantity") && quantity && x.Lines.Sum(line => line.RequestedQuantity) == decimalValue)
                || (selected.Contains("allocatedQuantity") && quantity && x.Lines.Sum(line => line.AllocatedQuantity) == decimalValue)
                || (selected.Contains("deliveredQuantity") && quantity && x.Lines.Sum(line => line.DeliveredQuantity) == decimalValue));
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
            line.ResolvedAtUtc, line.ResolutionReason, line.QuotaDecision.ToString(), line.QuotaDecisionByUserId,
            line.QuotaDecisionAtUtc, Convert.ToBase64String(line.RowVersion),
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
