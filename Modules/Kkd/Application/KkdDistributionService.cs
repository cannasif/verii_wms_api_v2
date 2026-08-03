using System.Data;
using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.ErpIntegration.Application;
using verii_wms_api_v2.Modules.Kkd.Domain;
using verii_wms_api_v2.Modules.NetsisRead.Application;
using verii_wms_api_v2.Modules.NetsisRead.Application.Dtos;
using verii_wms_api_v2.Modules.StockTracking.Application;
using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using verii_wms_api_v2.Modules.WarehouseOutbound.Application;
using verii_wms_api_v2.Modules.WarehouseOutbound.Domain;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;
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
    public async Task<KkdDistributionContext> GetContextAsync(long employeeId, CancellationToken ct = default)
    {
        var employee = await ActiveEmployeeAsync(employeeId, ct);
        var customer = await uow.Repository<verii_wms_api_v2.Modules.Customer.Domain.Customer>().Query()
            .SingleOrDefaultAsync(x => x.Id == employee.CustomerId, ct)
            ?? throw AppException.Conflict("KKD personelinin bağlı olduğu cari bulunamadı.");
        var headers = await netsis.GetShipmentOpenOrderHeadersAsync(customer.CustomerCode, employee.BranchCode, ct);
        var policy = await policies.GetAsync(employee.BranchCode, ct);
        return new(
            employee.Id,
            employee.EmployeeCode,
            $"{employee.FirstName} {employee.LastName}".Trim(),
            employee.BranchCode,
            customer.Id,
            customer.CustomerCode,
            customer.CustomerName,
            policy,
            headers
                .Where(x => (x.AvailableQuantity ?? x.RemainingQuantity ?? 0) > 0)
                .OrderBy(x => x.OrderDate).ThenBy(x => x.OrderNumber, StringComparer.OrdinalIgnoreCase)
                .Select(x => new KkdOpenOrderHeader(
                    x.OrderNumber,
                    x.OrderDate.HasValue ? DateOnly.FromDateTime(x.OrderDate.Value) : null,
                    x.ProjectCode,
                    x.AvailableQuantity ?? x.RemainingQuantity ?? 0))
                .ToArray());
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
        var rows = await netsis.GetShipmentOpenOrderLinesAsync(string.Join(',', numbers), employee.BranchCode, ct);
        if (rows.Any(x => !string.Equals(x.CustomerCode, customerCode, StringComparison.OrdinalIgnoreCase)))
            throw AppException.Conflict("Seçilen siparişlerden biri personelin bağlı olduğu cariye ait değil.");

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
            .Where(x => (x.AvailableQuantity ?? x.RemainingQuantity ?? 0) > 0)
            .OrderBy(x => x.OrderDate).ThenBy(x => x.OrderNumber, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.OrderLineSequence)
            .Select(x =>
            {
                var code = x.StockCode?.Trim() ?? string.Empty;
                var mapped = stocksByCode.TryGetValue(code, out var stock);
                return new KkdOpenOrderLine(
                    x.OrderNumber, x.OrderId, x.OrderLineSequence, stock?.Id, code,
                    stock?.StockName ?? x.StockName ?? code, x.UnitCode, x.YapCode, x.ProjectCode,
                    x.OrderDate.HasValue ? DateOnly.FromDateTime(x.OrderDate.Value) : null,
                    x.DeliveryDate.HasValue ? DateOnly.FromDateTime(x.DeliveryDate.Value) : null,
                    x.AvailableQuantity ?? x.RemainingQuantity ?? 0,
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

        var employee = await ActiveEmployeeAsync(request.EmployeeId, ct);
        var policy = await policies.GetAsync(employee.BranchCode, ct);
        ValidatePolicy(request, employee, policy);

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
        var openRows = orderBased
            ? await netsis.GetShipmentOpenOrderLinesAsync(string.Join(',', orderNumbers), employee.BranchCode, ct)
            : [];
        var openByKey = openRows.ToDictionary(
            x => OrderKey(x.OrderNumber, x.OrderId), StringComparer.OrdinalIgnoreCase);
        var customerCode = await CustomerCodeAsync(employee.CustomerId, ct);

        var prepared = new List<PreparedLine>(request.Lines.Count);
        foreach (var item in request.Lines)
        {
            var stock = stocks[item.StockId];
            ShipmentOpenOrderLineDto? order = null;
            if (orderBased)
            {
                var key = OrderKey(item.OrderNumber!, item.OrderLineId!.Value);
                if (!openByKey.TryGetValue(key, out order))
                    throw AppException.Conflict($"{item.OrderNumber}/{item.OrderLineId} Netsis açık siparişlerinde bulunamadı.");
                if (!string.Equals(order.CustomerCode, customerCode, StringComparison.OrdinalIgnoreCase))
                    throw AppException.Conflict($"{item.OrderNumber} personelin bağlı olduğu cariye ait değil.");
                if (!string.Equals(stock.ErpStockCode, order.StockCode, StringComparison.OrdinalIgnoreCase))
                    throw AppException.Conflict($"{item.OrderNumber}/{item.OrderLineId} stok eşleşmesi geçersiz.");
                var available = order.AvailableQuantity ?? order.RemainingQuantity ?? 0;
                if (item.Quantity > available)
                    throw AppException.Conflict($"{item.OrderNumber}/{item.OrderLineId} açık sipariş bakiyesi {available}; {item.Quantity} çıkış yapılamaz.");
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

        var outboundRequest = new CreateWarehouseOutboundDraftRequest(
            request.IdempotencyKey,
            employee.BranchCode,
            request.DocumentSeriesId,
            request.DocumentDate,
            orderBased ? WarehouseOutboundInitiationMode.OrderBasedDirect : WarehouseOutboundInitiationMode.StockBasedDirect,
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
            null);
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
                CreatedBy = actor,
                CreatedDate = now
            };
            var lineNo = 0;
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
            }
            await uow.Repository<KkdDistribution>().AddAsync(distribution, token);
            await uow.SaveChangesAsync(token);
            return CreateResult(distribution, false);
        }, ct, IsolationLevel.Serializable);
    }

    public async Task<IReadOnlyList<KkdDistributionRow>> GetRecentAsync(CancellationToken ct = default) =>
        await uow.Repository<KkdDistribution>().Query()
            .OrderByDescending(x => x.Id).Take(250)
            .Select(x => new KkdDistributionRow(
                x.Id, x.DocumentNo, x.Status.ToString(), x.EmployeeId, x.Employee.EmployeeCode,
                x.Employee.FirstName + " " + x.Employee.LastName, x.WarehouseId, x.WarehouseOutboundId,
                x.Lines.Sum(l => l.Quantity), x.Lines.Sum(l => l.EntitledQuantity),
                x.Lines.Sum(l => l.ExcessQuantity), x.CreatedDate, x.CompletedAtUtc))
            .ToListAsync(ct);

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
            item.Order.OrderId.ToString(),
            item.Order.OrderLineSequence,
            item.Order.StockCode ?? item.Stock.ErpStockCode,
            item.Order.YapCode,
            item.Order.OrderDate.HasValue ? DateOnly.FromDateTime(item.Order.OrderDate.Value) : null,
            item.Order.OrderedQuantity ?? 0,
            item.Order.DeliveredQuantity ?? 0,
            item.Order.RemainingQuantity ?? 0));

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
        x.Lines.Sum(l => l.Quantity), x.Lines.Sum(l => l.EntitledQuantity), x.Lines.Sum(l => l.ExcessQuantity), replayed);

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
    }

    internal static void ValidatePolicy(KkdDistributionCreateRequest request, KkdEmployee employee, KkdPolicyDto policy)
    {
        var orderRefs = request.Lines
            .Where(x => !string.IsNullOrWhiteSpace(x.OrderNumber) && x.OrderLineId.HasValue)
            .ToArray();
        if (policy.RequireOpenOrder && orderRefs.Length != request.Lines.Count)
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
        ShipmentOpenOrderLineDto? Order,
        EffectiveStockTrackingPolicy TrackingPolicy,
        string GroupCode,
        decimal Entitled,
        decimal Excess,
        IReadOnlyList<ReservedAllocation> Allocations);
}
