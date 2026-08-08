using System.Data;
using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.ErpIntegration.Application;
using verii_wms_api_v2.Modules.Identity.Domain;
using verii_wms_api_v2.Modules.Kkd.Domain;
using verii_wms_api_v2.Modules.NetsisRead.Application;
using verii_wms_api_v2.Modules.NetsisRead.Application.Dtos;
using verii_wms_api_v2.Modules.StockTracking.Application;
using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using verii_wms_api_v2.Modules.WarehouseOutbound.Application;
using verii_wms_api_v2.Modules.WarehouseOutbound.Domain;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;
using verii_wms_api_v2.Shared.Domain;
using StockEntity = verii_wms_api_v2.Modules.Stock.Domain.Stock;

namespace verii_wms_api_v2.Modules.Kkd.Application;

public sealed class KkdDistributionService(
    IUnitOfWork uow,
    IKkdEntitlementService entitlements,
    IWarehouseOutboundService outbounds,
    IKkdDistributionCompletionService completion,
    IOperationCancellationCoordinator cancellations,
    INetsisReadService netsis,
    IStockTrackingPolicyResolver trackingPolicies,
    IKkdPolicyService policies) : IKkdDistributionService
{
    public async Task<KkdDistributionContext> GetContextAsync(
        long employeeId,
        bool includeOpenOrders = true,
        CancellationToken ct = default)
    {
        var employee = await ActiveEmployeeAsync(employeeId, ct);
        var customer = await uow.Repository<verii_wms_api_v2.Modules.Customer.Domain.Customer>().Query()
            .SingleOrDefaultAsync(x => x.Id == employee.CustomerId, ct)
            ?? throw AppException.Conflict("KKD personelinin bağlı olduğu cari bulunamadı.");
        IReadOnlyList<KkdCustomerOpenOrderDto> openOrderRows = includeOpenOrders
            ? await ReadKkdOpenOrdersAsync(customer.CustomerCode, ct)
            : [];
        var policy = await policies.GetAsync(employee.BranchCode, ct);
        var preferredStocks = await (
            from preference in uow.Repository<KkdEmployeeStockPreference>().Query()
            where preference.EmployeeId == employee.Id
            join stock in uow.Repository<StockEntity>().Query() on preference.StockId equals stock.Id
            orderby preference.GroupCode
            select new KkdPreferredStock(preference.GroupCode, stock.Id, stock.ErpStockCode, stock.StockName)
        ).ToListAsync(ct);
        return new(
            employee.Id,
            employee.EmployeeCode,
            $"{employee.FirstName} {employee.LastName}".Trim(),
            employee.BranchCode,
            customer.Id,
            customer.CustomerCode,
            customer.CustomerName,
            policy,
            openOrderRows
                .Where(x => x.RemainingQuantity > 0 && !string.IsNullOrWhiteSpace(x.OrderNumber))
                .GroupBy(x => x.OrderNumber, StringComparer.OrdinalIgnoreCase)
                .Select(group => new KkdOpenOrderHeader(
                    group.Key,
                    group.Min(x => x.OrderDate) is { } orderDate ? DateOnly.FromDateTime(orderDate) : null,
                    group.Select(x => x.ProjectCode).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)),
                    group.Sum(x => x.RemainingQuantity)))
                .OrderBy(x => x.OrderDate).ThenBy(x => x.OrderNumber, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            preferredStocks);
    }

    public async Task<IReadOnlyList<KkdOpenOrderLine>> GetOpenOrderLinesAsync(
        long employeeId,
        string orderNumbersCsv,
        CancellationToken ct = default)
    {
        var numbers = (orderNumbersCsv ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (numbers.Length == 0) throw AppException.BadRequest("En az bir açık sipariş seçilmelidir.");

        var employee = await ActiveEmployeeAsync(employeeId, ct);
        var customerCode = await CustomerCodeAsync(employee.CustomerId, ct);
        var rows = (await ReadKkdOpenOrdersAsync(customerCode, ct))
            .Where(x => numbers.Contains(x.OrderNumber, StringComparer.OrdinalIgnoreCase))
            .Where(x => x.RemainingQuantity > 0)
            .ToArray();
        if (rows.Length == 0)
            return [];

        var stockCodes = rows.Select(x => x.StockCode?.Trim()).Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var stocks = await uow.Repository<StockEntity>().Query()
            .Where(x => x.BranchCode == employee.BranchCode && stockCodes.Contains(x.ErpStockCode))
            .Select(x => new { x.Id, x.ErpStockCode, x.StockName })
            .ToListAsync(ct);
        var stocksByCode = stocks
            .GroupBy(x => x.ErpStockCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.OrderBy(y => y.Id).First(), StringComparer.OrdinalIgnoreCase);

        return rows
            .OrderBy(x => x.OrderDate).ThenBy(x => x.OrderNumber, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.OrderId)
            .Select((x, index) =>
            {
                var code = x.StockCode?.Trim() ?? string.Empty;
                var mapped = stocksByCode.TryGetValue(code, out var stock);
                return new KkdOpenOrderLine(
                    x.OrderNumber,
                    x.OrderId ?? index + 1,
                    index + 1,
                    stock?.Id,
                    code,
                    stock?.StockName ?? x.StockName ?? code,
                    x.UnitCode,
                    null,
                    x.ProjectCode,
                    x.OrderDate.HasValue ? DateOnly.FromDateTime(x.OrderDate.Value) : null,
                    null,
                    x.RemainingQuantity,
                    mapped,
                    mapped ? null : $"{code} stok kodu WMS stok aynasında bulunamadı; ERP stok senkronizasyonu gereklidir.");
            })
            .ToArray();
    }

    public async Task<KkdDistributionCreateResult> CreateAsync(
        KkdDistributionCreateRequest request,
        long actor,
        CancellationToken ct = default)
    {
        ValidateCreateEnvelope(request);
        var existing = await uow.Repository<KkdDistribution>().Query()
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.CorrelationId == request.IdempotencyKey, ct);
        if (existing is not null)
            return CreateResult(existing, true);

        var assignedWarehouseIds = await uow.Repository<UserWarehouseAssignment>().Query()
            .Where(x => x.UserId == actor)
            .Select(x => x.WarehouseId)
            .Distinct()
            .ToArrayAsync(ct);
        if (assignedWarehouseIds.Length > 0 && !assignedWarehouseIds.Contains(request.WarehouseId))
            throw AppException.Forbidden("Seçilen depo, oturum kullanıcısının yetkili olduğu depolar arasında değil.");

        var employee = await ActiveEmployeeAsync(request.EmployeeId, ct);
        var policy = await policies.GetAsync(employee.BranchCode, ct);
        ValidatePolicy(request, employee, policy);

        KkdRequest? linkedRequest = null;
        IReadOnlyDictionary<long, KkdRequestLine> linkedLines = new Dictionary<long, KkdRequestLine>();
        if (request.KkdRequestId.HasValue)
        {
            linkedRequest = await uow.Repository<KkdRequest>().Query().Include(x => x.Lines)
                .SingleOrDefaultAsync(x => x.Id == request.KkdRequestId.Value, ct)
                ?? throw AppException.NotFound("KKD talebi bulunamadı.");
            if (linkedRequest.EmployeeId != employee.Id)
                throw AppException.Conflict("KKD talebi seçilen personele ait değildir.");
            if (linkedRequest.Status is KkdRequestStatus.Completed or KkdRequestStatus.Cancelled)
                throw AppException.Conflict("Tamamlanmış veya iptal edilmiş KKD talebinden dağıtım oluşturulamaz.");
            if (linkedRequest.WarehouseId.HasValue && linkedRequest.WarehouseId.Value != request.WarehouseId)
                throw AppException.Conflict("Dağıtım deposu KKD talebine atanmış depoyla aynı olmalıdır.");
            linkedLines = linkedRequest.Lines.ToDictionary(x => x.Id);
            foreach (var item in request.Lines)
            {
                if (!item.KkdRequestLineId.HasValue || !linkedLines.TryGetValue(item.KkdRequestLineId.Value, out var requestLine))
                    throw AppException.Conflict("Her dağıtım kalemi seçilen KKD talebindeki bir kaleme bağlı olmalıdır.");
                if (!requestLine.StockId.HasValue)
                    throw AppException.Conflict($"{requestLine.GroupCode} grubu için stok/beden seçimi yapılmadan dağıtım başlatılamaz.");
                if (requestLine.StockId.Value != item.StockId)
                    throw AppException.Conflict($"{requestLine.GroupCode} talep kalemi için seçilen stok değiştirilemez.");
                var remaining = requestLine.RequestedQuantity - requestLine.AllocatedQuantity
                    - requestLine.DeliveredQuantity - requestLine.CancelledQuantity;
                if (item.Quantity > remaining)
                    throw AppException.Conflict($"{requestLine.GroupCode} talep kaleminde hazırlanabilir miktar {remaining:0.######}; {item.Quantity:0.######} ayrılamaz.");
            }
        }

        var stockIds = request.Lines.Select(x => x.StockId).Distinct().ToArray();
        var stocks = await uow.Repository<StockEntity>().Query()
            .Where(x => x.BranchCode == employee.BranchCode && stockIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);
        if (stocks.Count != stockIds.Length)
            throw AppException.BadRequest("Dağıtım stoklarından biri bulunamadı.");

        var orderNumbers = request.Lines.Select(x => x.OrderNumber?.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var orderBased = orderNumbers.Length > 0;
        var assignedUserIds = (request.AssignedUserIds ?? [])
            .Where(x => x > 0)
            .Distinct()
            .ToArray();
        if (!request.CreateWarehouseTask && assignedUserIds.Length > 0)
            throw AppException.BadRequest("Doğrudan KKD dağıtımına görev sorumlusu atanamaz.");
        var openRows = orderBased
            ? await ReadKkdOpenOrdersAsync(await CustomerCodeAsync(employee.CustomerId, ct), ct)
            : [];
        var openByKey = openRows
            .Where(x => x.OrderId.HasValue && x.OrderId.Value > 0)
            .GroupBy(x => OrderKey(x.OrderNumber, x.OrderId!.Value), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
        var customerCode = await CustomerCodeAsync(employee.CustomerId, ct);

        var prepared = new List<PreparedLine>(request.Lines.Count);
        foreach (var item in request.Lines)
        {
            var stock = stocks[item.StockId];
            KkdCustomerOpenOrderDto? order = null;
            if (orderBased)
            {
                var key = OrderKey(item.OrderNumber!, item.OrderLineId!.Value);
                if (!openByKey.TryGetValue(key, out order))
                    throw AppException.Conflict($"{item.OrderNumber}/{item.OrderLineId} Netsis açık siparişlerinde bulunamadı.");
                if (!string.Equals(order.CustomerCode, customerCode, StringComparison.OrdinalIgnoreCase))
                    throw AppException.Conflict($"{item.OrderNumber} personelin bağlı olduğu cariye ait değil.");
                if (!string.Equals(stock.ErpStockCode, order.StockCode, StringComparison.OrdinalIgnoreCase))
                    throw AppException.Conflict($"{item.OrderNumber}/{item.OrderLineId} stok eşleşmesi geçersiz.");
                if (item.Quantity > order.RemainingQuantity)
                    throw AppException.Conflict($"{item.OrderNumber}/{item.OrderLineId} açık sipariş bakiyesi {order.RemainingQuantity}; {item.Quantity} çıkış yapılamaz.");
            }

            var check = await entitlements.CheckAsync(new(employee.Id, stock.Id, item.Quantity, request.DocumentDate), ct);
            var entitled = Math.Min(item.Quantity, check.TotalRemainingQuantity);
            var excess = item.Quantity - entitled;
            if (excess > 0 && !orderBased)
                throw AppException.Conflict($"{stock.ErpStockCode} için yalnızca {entitled} hak mevcut; siparişsiz dağıtımda hak üstü teslim yapılamaz.");
            if (excess > 0 && !policy.AllowOpenOrderExcess)
                throw AppException.Conflict($"{stock.ErpStockCode} için yalnızca {entitled} hak mevcut; politika hak üstü dağıtıma izin vermiyor.");
            var allocations = Allocate(check.Allocations, entitled);
            var trackingPolicy = await trackingPolicies.ResolveAsync(employee.BranchCode, stock.Id, ct);
            prepared.Add(new(item, stock, order, trackingPolicy, check.GroupCode, entitled, excess, allocations));
        }

        var requiresExcessApproval = policy.RequireManagerApprovalForExcess
            && prepared.Any(x => x.Excess > 0);

        var outboundRequest = new CreateWarehouseOutboundDraftRequest(
            request.IdempotencyKey,
            employee.BranchCode,
            request.DocumentSeriesId,
            request.DocumentDate,
            request.CreateWarehouseTask
                ? orderBased ? WarehouseOutboundInitiationMode.OrderBasedTask : WarehouseOutboundInitiationMode.StockBasedTask
                : orderBased ? WarehouseOutboundInitiationMode.OrderBasedDirect : WarehouseOutboundInitiationMode.StockBasedDirect,
            employee.CustomerId,
            request.WarehouseId,
            request.StagingLocationId,
            request.LoadingLocationId,
            DateTimeOffset.UtcNow,
            1,
            $"KKD:{employee.EmployeeCode}",
            false,
            null, null, null, null, null, null,
            Clean(request.Description, 1000) ?? $"{employee.EmployeeCode} KKD teslimi",
            prepared.Select(ToOutboundLine).ToArray(),
            request.CreateWarehouseTask ? assignedUserIds : null);
        var outbound = await outbounds.CreateDraftAsync(outboundRequest, actor, ct);

        return await uow.ExecuteInTransactionAsync(async token =>
        {
            var replay = await uow.Repository<KkdDistribution>().Query()
                .Include(x => x.Lines)
                .SingleOrDefaultAsync(x => x.CorrelationId == request.IdempotencyKey, token);
            if (replay is not null) return CreateResult(replay, true);

            var now = DateTime.UtcNow;
            var distribution = new KkdDistribution
            {
                BranchCode = employee.BranchCode,
                CorrelationId = request.IdempotencyKey,
                EmployeeId = employee.Id,
                CustomerId = employee.CustomerId,
                WarehouseId = request.WarehouseId,
                DocumentSeriesId = request.DocumentSeriesId,
                DocumentNo = $"KKD-{outbound.DocumentNo}",
                Status = KkdDistributionStatus.OutboundCreated,
                WarehouseOutboundId = outbound.Id,
                KkdRequestId = request.KkdRequestId,
                ExcessApprovalStatus = requiresExcessApproval
                    ? KkdExcessApprovalStatus.Pending
                    : KkdExcessApprovalStatus.NotRequired,
                CreatedBy = actor,
                CreatedDate = now
            };
            var lineNo = 0;
            var preferenceGroups = prepared.Select(x => x.GroupCode).Distinct().ToArray();
            var preferences = await uow.Repository<KkdEmployeeStockPreference>().Query(true)
                .Where(x => x.EmployeeId == employee.Id && preferenceGroups.Contains(x.GroupCode))
                .ToDictionaryAsync(x => x.GroupCode, token);
            foreach (var item in prepared)
            {
                var line = new KkdDistributionLine
                {
                    BranchCode = employee.BranchCode,
                    Distribution = distribution,
                    LineNo = ++lineNo,
                    StockId = item.Stock.Id,
                    StockCodeSnapshot = item.Stock.ErpStockCode,
                    StockNameSnapshot = item.Stock.StockName,
                    GroupCode = item.GroupCode,
                    Quantity = item.Request.Quantity,
                    EntitledQuantity = item.Entitled,
                    ExcessQuantity = item.Excess,
                    SourceLocationId = item.Request.SourceLocationId,
                    LotNo = Single(item.Request.Trackings?.Select(x => x.LotNo)),
                    SerialNo = Single(item.Request.Trackings?.Select(x => x.SerialNo)),
                    OpenOrderNo = item.Order?.OrderNumber,
                    OpenOrderLineId = item.Order?.OrderId.ToString(),
                    KkdRequestLineId = item.Request.KkdRequestLineId,
                    CreatedBy = actor,
                    CreatedDate = now
                };
                foreach (var allocation in item.Allocations)
                    line.EntitlementAllocations.Add(new()
                    {
                        BranchCode = employee.BranchCode,
                        DistributionLine = line,
                        SourceType = allocation.SourceType,
                        SourceId = allocation.SourceId,
                        Quantity = allocation.Quantity,
                        PeriodStart = allocation.PeriodStart,
                        PeriodEnd = allocation.PeriodEnd,
                        CreatedBy = actor,
                        CreatedDate = now
                    });
                distribution.Lines.Add(line);

                if (!preferences.TryGetValue(item.GroupCode, out var preference))
                {
                    preference = new KkdEmployeeStockPreference
                    {
                        BranchCode = employee.BranchCode,
                        EmployeeId = employee.Id,
                        GroupCode = item.GroupCode,
                        StockId = item.Stock.Id,
                        LastSelectedAtUtc = DateTimeOffset.UtcNow,
                        CreatedBy = actor,
                        CreatedDate = now
                    };
                    await uow.Repository<KkdEmployeeStockPreference>().AddAsync(preference, token);
                    preferences[item.GroupCode] = preference;
                }
                else
                {
                    preference.StockId = item.Stock.Id;
                    preference.LastSelectedAtUtc = DateTimeOffset.UtcNow;
                    preference.UpdatedBy = actor;
                    preference.UpdatedDate = now;
                }
            }
            if (request.KkdRequestId.HasValue)
            {
                var trackedRequest = await uow.Repository<KkdRequest>().Query(true).Include(x => x.Lines)
                    .SingleOrDefaultAsync(x => x.Id == request.KkdRequestId.Value, token)
                    ?? throw AppException.NotFound("KKD talebi bulunamadı.");
                foreach (var item in request.Lines)
                {
                    var requestLine = trackedRequest.Lines.Single(x => x.Id == item.KkdRequestLineId!.Value);
                    var remaining = requestLine.RequestedQuantity - requestLine.AllocatedQuantity
                        - requestLine.DeliveredQuantity - requestLine.CancelledQuantity;
                    if (item.Quantity > remaining)
                        throw AppException.Conflict($"{requestLine.GroupCode} talep kalemi başka bir işlem tarafından ayrıldı. Ekranı yenileyin.");
                    requestLine.AllocatedQuantity += item.Quantity;
                    requestLine.UpdatedBy = actor;
                    requestLine.UpdatedDate = DateTime.UtcNow;
                }
                trackedRequest.WarehouseId ??= request.WarehouseId;
                trackedRequest.AssignedUserId ??= assignedUserIds.FirstOrDefault() is > 0 ? assignedUserIds[0] : null;
                trackedRequest.StartedAtUtc ??= DateTimeOffset.UtcNow;
                trackedRequest.UpdatedBy = actor;
                trackedRequest.UpdatedDate = DateTime.UtcNow;
                KkdRequestStateMachine.Refresh(trackedRequest, DateTimeOffset.UtcNow);
            }
            await uow.Repository<KkdDistribution>().AddAsync(distribution, token);
            if (requiresExcessApproval)
            {
                var outboundHeader = await uow.Repository<WarehouseOutboundHeader>().FindByIdAsync(
                    outbound.Id, tracking: true, cancellationToken: token)
                    ?? throw AppException.Conflict("KKD ambar çıkış taslağı bulunamadı.");
                outboundHeader.RequireApproval = true;
                outboundHeader.ApprovalStatus = OperationApprovalStatus.Pending;
                outboundHeader.UpdatedBy = actor;
                outboundHeader.UpdatedDate = now;
            }
            await uow.SaveChangesAsync(token);
            return CreateResult(distribution, false);
        }, ct, IsolationLevel.Serializable);
    }

    public async Task<IReadOnlyList<KkdDistributionRow>> GetRecentAsync(long actor, CancellationToken ct = default)
    {
        var warehouseIds = await uow.Repository<UserWarehouseAssignment>().Query()
            .Where(x => x.UserId == actor)
            .Select(x => x.WarehouseId)
            .Distinct()
            .ToArrayAsync(ct);
        var query = uow.Repository<KkdDistribution>().Query();
        if (warehouseIds.Length > 0) query = query.Where(x => warehouseIds.Contains(x.WarehouseId));
        return await query
            .OrderByDescending(x => x.Id).Take(250)
            .Select(x => new KkdDistributionRow(
                x.Id, x.DocumentNo, x.Status.ToString(), x.EmployeeId, x.Employee.EmployeeCode,
                x.Employee.FirstName + " " + x.Employee.LastName, x.WarehouseId, x.WarehouseOutboundId,
                x.Lines.Sum(l => l.Quantity), x.Lines.Sum(l => l.EntitledQuantity),
                x.Lines.Sum(l => l.ExcessQuantity), x.ExcessApprovalStatus.ToString(),
                x.ExcessApprovalReason, x.ExcessApprovedBy, x.ExcessApprovedAtUtc,
                x.CreatedDate, x.CompletedAtUtc))
            .ToListAsync(ct);
    }

    public async Task<PagedResponse<KkdDistributionRow>> GetPagedAsync(PagedRequest request, long actor, CancellationToken ct = default)
    {
        var query = await AuthorizedDistributionsAsync(actor, ct);
        var projected = query.Select(x => new KkdDistributionRow(
                x.Id, x.DocumentNo, x.Status.ToString(), x.EmployeeId, x.Employee.EmployeeCode,
                x.Employee.FirstName + " " + x.Employee.LastName, x.WarehouseId, x.WarehouseOutboundId,
                x.Lines.Sum(l => l.Quantity), x.Lines.Sum(l => l.EntitledQuantity),
                x.Lines.Sum(l => l.ExcessQuantity), x.ExcessApprovalStatus.ToString(),
                x.ExcessApprovalReason, x.ExcessApprovedBy, x.ExcessApprovedAtUtc,
                x.CreatedDate, x.CompletedAtUtc))
            .ApplySearch(request, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["documentNo"] = nameof(KkdDistributionRow.DocumentNo),
                ["employeeCode"] = nameof(KkdDistributionRow.EmployeeCode),
                ["employeeName"] = nameof(KkdDistributionRow.EmployeeName)
            }, ["documentNo", "employeeCode", "employeeName"])
            .ApplySort(request, nameof(KkdDistributionRow.Id));
        return await projected.ToPagedResponseAsync(request, ct);
    }

    public async Task<KkdDistributionDetail> GetDetailAsync(long id, long actor, CancellationToken ct = default)
    {
        var query = await AuthorizedDistributionsAsync(actor, ct);
        return await query.Where(x => x.Id == id)
            .Select(x => new KkdDistributionDetail(
                x.Id, x.CorrelationId, x.DocumentNo, x.Status.ToString(),
                x.EmployeeId, x.Employee.EmployeeCode, x.Employee.FirstName + " " + x.Employee.LastName,
                x.CustomerId, x.WarehouseId, x.WarehouseOutboundId, x.ExcessApprovalStatus.ToString(),
                x.ExcessApprovalReason, x.FailureReason, x.CreatedDate, x.CompletedAtUtc,
                x.Lines.OrderBy(l => l.LineNo).Select(l => new KkdDistributionLineDetail(
                    l.Id, l.LineNo, l.StockId, l.StockCodeSnapshot, l.StockNameSnapshot ?? string.Empty,
                    l.GroupCode, l.Quantity, l.EntitledQuantity, l.ExcessQuantity, l.SourceLocationId,
                    l.LotNo, l.SerialNo, l.OpenOrderNo, l.OpenOrderLineId)).ToArray()))
            .SingleOrDefaultAsync(ct)
            ?? throw AppException.NotFound("KKD dağıtım kaydı bulunamadı veya bu depoya erişiminiz yok.");
    }

    private async Task<IQueryable<KkdDistribution>> AuthorizedDistributionsAsync(long actor, CancellationToken ct)
    {
        var warehouseIds = await uow.Repository<UserWarehouseAssignment>().Query()
            .Where(x => x.UserId == actor)
            .Select(x => x.WarehouseId)
            .Distinct()
            .ToArrayAsync(ct);
        var query = uow.Repository<KkdDistribution>().Query();
        return warehouseIds.Length == 0 ? query : query.Where(x => warehouseIds.Contains(x.WarehouseId));
    }

    public async Task<KkdDistributionRow> DecideExcessApprovalAsync(
        long id,
        KkdExcessApprovalRequest request,
        long actor,
        CancellationToken ct = default)
    {
        if (id <= 0 || request.IdempotencyKey == Guid.Empty || string.IsNullOrWhiteSpace(request.Reason))
            throw AppException.BadRequest("Dağıtım, idempotency anahtarı ve karar açıklaması zorunludur.");
        if (request.Reason.Trim().Length < 5)
            throw AppException.BadRequest("Karar açıklaması en az 5 karakter olmalıdır.");

        await uow.ExecuteInTransactionAsync(async token =>
        {
            var entity = await uow.Repository<KkdDistribution>().Query(true)
                .Include(x => x.Lines)
                .SingleOrDefaultAsync(x => x.Id == id, token)
                ?? throw AppException.NotFound("KKD dağıtımı bulunamadı.");
            if (entity.Status is KkdDistributionStatus.Completed or KkdDistributionStatus.Cancelled)
                throw AppException.Conflict("Tamamlanmış veya iptal edilmiş KKD dağıtımında kota aşım kararı değiştirilemez.");
            if (entity.ExcessApprovalStatus == KkdExcessApprovalStatus.NotRequired)
                throw AppException.Conflict("Bu KKD dağıtımında yönetici kota aşım onayı gerekmiyor.");

            var desired = request.Approve ? KkdExcessApprovalStatus.Approved : KkdExcessApprovalStatus.Rejected;
            if (entity.ExcessApprovalStatus == desired) return true;

            var outbound = entity.WarehouseOutboundId.HasValue
                ? await uow.Repository<WarehouseOutboundHeader>().Query(true)
                    .Include(x => x.Lines).ThenInclude(x => x.Sources)
                    .Include(x => x.Lines).ThenInclude(x => x.Trackings)
                    .Include(x => x.Tasks).ThenInclude(x => x.Lines)
                    .SingleOrDefaultAsync(x => x.Id == entity.WarehouseOutboundId.Value, token)
                : null;
            if (outbound is null)
                throw AppException.Conflict("KKD dağıtımına bağlı ambar çıkışı bulunamadı.");
            if (outbound.Status != WarehouseOutboundStatus.Draft)
                throw AppException.Conflict("Ambar çıkışı serbest bırakıldıktan sonra kota aşım kararı değiştirilemez.");

            var now = DateTimeOffset.UtcNow;
            entity.ExcessApprovalStatus = desired;
            entity.ExcessApprovalReason = request.Reason.Trim();
            entity.ExcessApprovedBy = actor;
            entity.ExcessApprovedAtUtc = now;
            entity.UpdatedBy = actor;
            entity.UpdatedDate = now.UtcDateTime;
            outbound.UpdatedBy = actor;
            outbound.UpdatedDate = now.UtcDateTime;

            if (request.Approve)
            {
                // Onay: kota aşımı dahil tüm kalemler aynı tek ambar çıkışıyla devam eder.
                outbound.RequireApproval = true;
                outbound.ApprovalStatus = OperationApprovalStatus.Approved;
            }
            else
            {
                var excessLines = entity.Lines.Where(l => l.ExcessQuantity > 0).ToList();
                // Belgede hakkı hiç olmayan (tamamen aşım) kalem yoksa ya da en az bir kalemde hak
                // varsa: sadece aşım miktarını at, hakkı olan miktarla aynı tek çıkış devam etsin.
                var canPartiallySalvage = entity.Lines.Any(l => l.EntitledQuantity > 0);

                if (!canPartiallySalvage)
                {
                    // Hiçbir kalemde hak yok: kurtarılacak bir şey yok, eski davranış geçerli
                    // (belge ve bağlı çıkış tamamen reddedilmiş sayılır, tamamlanamaz).
                    outbound.RequireApproval = true;
                    outbound.ApprovalStatus = OperationApprovalStatus.Rejected;
                }
                else
                {
                    var discardedByRequestLineId = new Dictionary<long, decimal>();

                    void SoftDelete(BaseEntity soft)
                    {
                        soft.IsDeleted = true;
                        soft.DeletedBy = actor;
                        soft.DeletedDate = now.UtcDateTime;
                    }

                    foreach (var line in excessLines)
                    {
                        var discardedQuantity = line.ExcessQuantity;
                        if (line.KkdRequestLineId.HasValue)
                        {
                            discardedByRequestLineId.TryGetValue(line.KkdRequestLineId.Value, out var sum);
                            discardedByRequestLineId[line.KkdRequestLineId.Value] = sum + discardedQuantity;
                        }

                        var outboundLine = outbound.Lines.FirstOrDefault(x => x.LineNo == line.LineNo);
                        if (line.EntitledQuantity > 0)
                        {
                            // Kısmi aşım: kalem hak edilen miktara düşürülüp aynı çıkışta bırakılır.
                            line.Quantity = line.EntitledQuantity;
                            line.ExcessQuantity = 0;
                            line.UpdatedBy = actor;
                            line.UpdatedDate = now.UtcDateTime;
                            if (outboundLine is not null)
                            {
                                outboundLine.RequestedQuantity = line.EntitledQuantity;
                                outboundLine.UpdatedBy = actor;
                                outboundLine.UpdatedDate = now.UtcDateTime;
                                foreach (var taskLine in outbound.Tasks.SelectMany(t => t.Lines)
                                             .Where(tl => tl.WarehouseOutboundLineId == outboundLine.Id))
                                {
                                    taskLine.PlannedQuantity = line.EntitledQuantity;
                                    taskLine.UpdatedBy = actor;
                                    taskLine.UpdatedDate = now.UtcDateTime;
                                }
                            }
                        }
                        else
                        {
                            // Tamamı aşım: kalem tamamen düşer, aynı çıkışın geri kalanı etkilenmez.
                            SoftDelete(line);
                            if (outboundLine is not null)
                            {
                                foreach (var taskLine in outbound.Tasks.SelectMany(t => t.Lines)
                                             .Where(tl => tl.WarehouseOutboundLineId == outboundLine.Id)
                                             .ToList())
                                    SoftDelete(taskLine);
                                foreach (var source in outboundLine.Sources.ToList())
                                    SoftDelete(source);
                                foreach (var tracking in outboundLine.Trackings.ToList())
                                    SoftDelete(tracking);
                                SoftDelete(outboundLine);
                            }
                        }
                    }

                    if (entity.KkdRequestId.HasValue && discardedByRequestLineId.Count > 0)
                    {
                        var trackedRequest = await uow.Repository<KkdRequest>().Query(true)
                            .Include(x => x.Lines)
                            .SingleOrDefaultAsync(x => x.Id == entity.KkdRequestId.Value, token);
                        if (trackedRequest is not null)
                        {
                            foreach (var (requestLineId, discarded) in discardedByRequestLineId)
                            {
                                var requestLine = trackedRequest.Lines.SingleOrDefault(x => x.Id == requestLineId);
                                if (requestLine is null) continue;
                                requestLine.AllocatedQuantity = Math.Max(0, requestLine.AllocatedQuantity - discarded);
                                requestLine.UpdatedBy = actor;
                                requestLine.UpdatedDate = now.UtcDateTime;
                            }
                            trackedRequest.UpdatedBy = actor;
                            trackedRequest.UpdatedDate = now.UtcDateTime;
                            KkdRequestStateMachine.Refresh(trackedRequest, now);
                        }
                    }

                    // Kalan kalemler için aşım artık yok; bu tek çıkış normal akışla (release/pick/pack/ship) devam edebilir.
                    outbound.RequireApproval = true;
                    outbound.ApprovalStatus = OperationApprovalStatus.Approved;
                }
            }

            await uow.SaveChangesAsync(token);
            return true;
        }, ct, IsolationLevel.Serializable);

        return await GetRowAsync(id, ct);
    }

    public async Task<KkdDistributionCompleteResult> CompleteAsync(
        long id,
        KkdDistributionCompleteRequest request,
        long actor,
        CancellationToken ct = default)
        => await completion.CompleteByDistributionAsync(id, request.IdempotencyKey, actor, ct);

    public async Task CancelAsync(long id, Guid idempotencyKey, string reason, long actor, CancellationToken ct = default)
    {
        if (id <= 0 || idempotencyKey == Guid.Empty || string.IsNullOrWhiteSpace(reason))
            throw AppException.BadRequest("Dağıtım, idempotency anahtarı ve iptal nedeni zorunludur.");
        var entity = await uow.Repository<KkdDistribution>().Query()
            .SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw AppException.NotFound("KKD dağıtımı bulunamadı.");
        if (entity.Status == KkdDistributionStatus.Cancelled) return;
        if (entity.WarehouseOutboundId.HasValue)
        {
            var cancellation = await cancellations.CancelWarehouseOutboundAsync(entity.WarehouseOutboundId.Value,
                new(idempotencyKey, reason), actor, ct);
            if (!cancellation.WmsReversed)
                throw AppException.Conflict(cancellation.ErrorMessage
                    ?? "Bağlı ambar çıkışı ters kaydedilemediği için KKD hakkı serbest bırakılmadı.");
        }
        await uow.ExecuteInTransactionAsync(async token =>
        {
            entity = await uow.Repository<KkdDistribution>().Query(true)
                .Include(x => x.Lines).ThenInclude(x => x.Consumptions)
                .SingleOrDefaultAsync(x => x.Id == id, token)
                ?? throw AppException.NotFound("KKD dağıtımı bulunamadı.");
            var now = DateTimeOffset.UtcNow;
            var wasCompleted = entity.Status == KkdDistributionStatus.Completed;
            foreach (var consumption in entity.Lines.SelectMany(x => x.Consumptions).Where(x => !x.IsReversal))
            {
                if (await uow.Repository<KkdEntitlementConsumption>().AnyAsync(
                        x => x.ReversesConsumptionId == consumption.Id, token)) continue;
                await uow.Repository<KkdEntitlementConsumption>().AddAsync(new()
                {
                    BranchCode = entity.BranchCode,
                    EmployeeId = consumption.EmployeeId,
                    DistributionId = consumption.DistributionId,
                    DistributionLineId = consumption.DistributionLineId,
                    StockId = consumption.StockId,
                    GroupCode = consumption.GroupCode,
                    SourceType = consumption.SourceType,
                    MatrixId = consumption.MatrixId,
                    RuleId = consumption.RuleId,
                    PhaseId = consumption.PhaseId,
                    OverrideId = consumption.OverrideId,
                    Quantity = consumption.Quantity,
                    ConsumedAtUtc = now,
                    IsReversal = true,
                    ReversesConsumptionId = consumption.Id,
                    CreatedBy = actor,
                    CreatedDate = now.UtcDateTime
                }, token);
                if (consumption.OverrideId.HasValue)
                {
                    var item = await uow.Repository<KkdEmployeeEntitlementOverride>()
                        .FindByIdAsync(consumption.OverrideId.Value, true, token)
                        ?? throw AppException.Conflict("Ters kaydı alınacak personel ek hakkı bulunamadı.");
                    item.ConsumedQuantity = Math.Max(0, item.ConsumedQuantity - consumption.Quantity);
                }
            }
            if (entity.KkdRequestId.HasValue)
            {
                var request = await uow.Repository<KkdRequest>().Query(true).Include(x => x.Lines)
                    .SingleOrDefaultAsync(x => x.Id == entity.KkdRequestId.Value, token)
                    ?? throw AppException.Conflict("Bağlı KKD talebi bulunamadı.");
                var lines = request.Lines.ToDictionary(x => x.Id);
                foreach (var distributionLine in entity.Lines.Where(x => x.KkdRequestLineId.HasValue))
                {
                    if (!lines.TryGetValue(distributionLine.KkdRequestLineId!.Value, out var requestLine))
                        throw AppException.Conflict("Bağlı KKD talep kalemi bulunamadı.");
                    if (wasCompleted)
                        requestLine.DeliveredQuantity = Math.Max(0, requestLine.DeliveredQuantity - distributionLine.Quantity);
                    else
                        requestLine.AllocatedQuantity = Math.Max(0, requestLine.AllocatedQuantity - distributionLine.Quantity);
                    requestLine.UpdatedBy = actor;
                    requestLine.UpdatedDate = now.UtcDateTime;
                }
                request.UpdatedBy = actor;
                request.UpdatedDate = now.UtcDateTime;
                KkdRequestStateMachine.Refresh(request, now);
            }
            entity.Status = KkdDistributionStatus.Cancelled;
            entity.UpdatedBy = actor;
            entity.UpdatedDate = DateTime.UtcNow;
            await uow.SaveChangesAsync(token);
            return true;
        }, ct, IsolationLevel.Serializable);
    }

    public async Task<KkdDistributionCompleteResult?> CompleteByWarehouseOutboundAsync(
        long warehouseOutboundId,
        Guid idempotencyKey,
        long actor,
        CancellationToken ct = default)
    {
        if (warehouseOutboundId <= 0 || idempotencyKey == Guid.Empty)
            throw AppException.BadRequest("Ambar çıkışı ve idempotency anahtarı zorunludur.");
        return await completion.CompleteByWarehouseOutboundAsync(warehouseOutboundId, idempotencyKey, actor, ct);
    }

    private static WarehouseOutboundLineRequest ToOutboundLine(PreparedLine item) => new(
        item.Stock.Id,
        item.Request.YapCodeId,
        item.Request.Quantity,
        item.Request.UnitCode ?? item.Order?.UnitCode,
        item.TrackingPolicy.TrackingType,
        item.Request.RequireHandlingUnit,
        item.Request.SourceLocationId,
        item.Request.Description,
        item.Request.Trackings?.Select(x => new WarehouseOutboundTrackingRequest(
            x.Quantity, x.HandlingUnitNo, null, x.LotNo, x.SerialNo,
            x.ManufacturingDate, x.ExpirationDate, x.SourceLocationId ?? item.Request.SourceLocationId)).ToArray(),
        item.Order is null ? null : new WarehouseOutboundSourceRequest(
            item.Order.OrderNumber,
            (item.Order.OrderId ?? 0).ToString(),
            0,
            item.Order.StockCode ?? item.Stock.ErpStockCode,
            null,
            item.Order.OrderDate.HasValue ? DateOnly.FromDateTime(item.Order.OrderDate.Value) : null,
            item.Order.RemainingQuantity,
            0,
            item.Order.RemainingQuantity));

    private async Task<IReadOnlyList<KkdCustomerOpenOrderDto>> ReadKkdOpenOrdersAsync(
        string customerCode,
        CancellationToken ct)
    {
        try
        {
            return await netsis.GetKkdCustomerOpenOrdersAsync(customerCode, ct);
        }
        catch (AppException)
        {
            throw;
        }
        catch (Exception)
        {
            throw AppException.BadGateway(
                "Netsis açık KKD siparişleri okunamadı. V3RIICO erişimi veya personelin bağlı carisi kontrol edilmelidir.");
        }
    }

    private async Task<string> CustomerCodeAsync(long customerId, CancellationToken ct) =>
        await uow.Repository<verii_wms_api_v2.Modules.Customer.Domain.Customer>().Query()
            .Where(x => x.Id == customerId).Select(x => x.CustomerCode).SingleOrDefaultAsync(ct)
        ?? throw AppException.Conflict("KKD personelinin bağlı olduğu cari bulunamadı.");

    private async Task<KkdEmployee> ActiveEmployeeAsync(long employeeId, CancellationToken ct)
    {
        if (employeeId <= 0) throw AppException.BadRequest("KKD personeli zorunludur.");
        var employee = await uow.Repository<KkdEmployee>().Query()
            .SingleOrDefaultAsync(x => x.Id == employeeId, ct)
            ?? throw AppException.NotFound("KKD personeli bulunamadı.");
        if (!employee.IsActive) throw AppException.Conflict("KKD personeli aktif değil.");
        return employee;
    }

    private static IReadOnlyList<ReservedAllocation> Allocate(
        IReadOnlyList<KkdEntitlementAllocation> candidates,
        decimal quantity)
    {
        var remaining = quantity;
        var result = new List<ReservedAllocation>();
        foreach (var item in candidates)
        {
            if (remaining <= 0) break;
            var allocated = Math.Min(remaining, item.Quantity);
            if (allocated <= 0) continue;
            if (!Enum.TryParse<KkdEntitlementSourceType>(item.SourceType, true, out var type))
                throw AppException.Conflict($"Desteklenmeyen KKD hak kaynağı: {item.SourceType}");
            result.Add(new(type, item.SourceId, allocated, item.PeriodStart, item.PeriodEnd));
            remaining -= allocated;
        }
        if (remaining > 0)
            throw AppException.Conflict("KKD hak rezervasyonu hesaplanan hak miktarıyla eşleşmiyor.");
        return result;
    }

    private static KkdDistributionCreateResult CreateResult(KkdDistribution x, bool replayed) => new(
        x.Id, x.DocumentNo, x.Status.ToString(), x.WarehouseOutboundId ?? 0, x.DocumentNo.Replace("KKD-", string.Empty),
        x.Lines.Sum(l => l.Quantity), x.Lines.Sum(l => l.EntitledQuantity), x.Lines.Sum(l => l.ExcessQuantity),
        x.ExcessApprovalStatus.ToString(), replayed);

    private async Task<KkdDistributionRow> GetRowAsync(long id, CancellationToken ct) =>
        await uow.Repository<KkdDistribution>().Query()
            .Where(x => x.Id == id)
            .Select(x => new KkdDistributionRow(
                x.Id, x.DocumentNo, x.Status.ToString(), x.EmployeeId, x.Employee.EmployeeCode,
                x.Employee.FirstName + " " + x.Employee.LastName, x.WarehouseId, x.WarehouseOutboundId,
                x.Lines.Sum(l => l.Quantity), x.Lines.Sum(l => l.EntitledQuantity),
                x.Lines.Sum(l => l.ExcessQuantity), x.ExcessApprovalStatus.ToString(),
                x.ExcessApprovalReason, x.ExcessApprovedBy, x.ExcessApprovedAtUtc,
                x.CreatedDate, x.CompletedAtUtc))
            .SingleAsync(ct);

    internal static void ValidateCreateEnvelope(KkdDistributionCreateRequest request)
    {
        if (request.IdempotencyKey == Guid.Empty || request.EmployeeId <= 0 || request.WarehouseId <= 0 || request.DocumentSeriesId <= 0)
            throw AppException.BadRequest("Idempotency, personel, kaynak depo ve ambar çıkış belge serisi zorunludur.");
        if (request.Lines.Count == 0) throw AppException.BadRequest("En az bir KKD kalemi zorunludur.");
        if (request.Lines.Any(x => x.StockId <= 0 || x.Quantity <= 0 || x.SourceLocationId <= 0))
            throw AppException.BadRequest("Her KKD kaleminde stok, miktar ve kaynak raf zorunludur.");
        if (request.Lines.Any(x =>
            !string.IsNullOrWhiteSpace(x.OrderNumber)
            != (x.OrderLineId.HasValue && x.OrderLineId.Value > 0)))
            throw AppException.BadRequest("Sipariş numarası ve sipariş satırı birlikte gönderilmelidir.");
        if (request.KkdRequestId.HasValue)
        {
            if (request.KkdRequestId.Value <= 0 || request.Lines.Any(x => !x.KkdRequestLineId.HasValue || x.KkdRequestLineId <= 0))
                throw AppException.BadRequest("KKD talebi ve talep kalemi bağlantıları geçerli olmalıdır.");
            if (request.Lines.Select(x => x.KkdRequestLineId).Distinct().Count() != request.Lines.Count)
                throw AppException.BadRequest("Aynı KKD talep kalemi bir dağıtımda yalnızca bir kez kullanılabilir.");
            if (request.Lines.Any(x => !string.IsNullOrWhiteSpace(x.OrderNumber) || x.OrderLineId.HasValue))
                throw AppException.BadRequest("KKD talebi bağlantısı ile Netsis sipariş bağlantısı aynı dağıtımda kullanılamaz.");
        }
        else if (request.Lines.Any(x => x.KkdRequestLineId.HasValue))
            throw AppException.BadRequest("KKD talep kalemi gönderildiğinde üst talep kimliği de zorunludur.");
    }

    internal static void ValidatePolicy(KkdDistributionCreateRequest request, KkdEmployee employee, KkdPolicyDto policy)
    {
        var orderRefs = request.Lines
            .Where(x => !string.IsNullOrWhiteSpace(x.OrderNumber) && x.OrderLineId.HasValue)
            .ToArray();
        if (policy.RequireOpenOrder && !request.KkdRequestId.HasValue && orderRefs.Length != request.Lines.Count)
            throw AppException.Conflict("KKD politikası gereği her dağıtım kalemi açık Netsis siparişine bağlı olmalıdır.");
        if (orderRefs.Length > 0 && orderRefs.Length != request.Lines.Count)
            throw AppException.BadRequest("Sipariş bağlantılı ve siparişsiz kalemler aynı KKD dağıtımında kullanılamaz.");
        if (!policy.AllowMultipleOrdersPerDistribution
            && orderRefs.Select(x => x.OrderNumber!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Skip(1).Any())
            throw AppException.Conflict("KKD politikası tek dağıtımda yalnızca bir Netsis siparişine izin veriyor.");
        if (policy.RequireEmployeeUserLink && !employee.UserId.HasValue)
            throw AppException.Conflict("KKD politikası gereği personel aktif bir WMS kullanıcısına bağlanmalıdır.");
        if (!policy.AllowFutureDatedDistribution && request.DocumentDate > DateOnly.FromDateTime(DateTime.UtcNow))
            throw AppException.Conflict("KKD politikası ileri tarihli dağıtıma izin vermiyor.");
    }

    private static string OrderKey(string orderNumber, long orderLineId) =>
        $"{orderNumber.Trim()}|{orderLineId}";
    private static string? Clean(string? value, int max)
    {
        var text = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        return text?.Length > max ? text[..max] : text;
    }
    private static string? Single(IEnumerable<string?>? values) => values?
        .Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase).Take(2).ToArray() is { Length: 1 } one ? one[0] : null;

    private sealed record ReservedAllocation(
        KkdEntitlementSourceType SourceType, long SourceId, decimal Quantity, DateOnly PeriodStart, DateOnly? PeriodEnd);
    private sealed record PreparedLine(
        KkdDistributionLineCreateRequest Request,
        StockEntity Stock,
        KkdCustomerOpenOrderDto? Order,
        EffectiveStockTrackingPolicy TrackingPolicy,
        string GroupCode,
        decimal Entitled,
        decimal Excess,
        IReadOnlyList<ReservedAllocation> Allocations);
}
