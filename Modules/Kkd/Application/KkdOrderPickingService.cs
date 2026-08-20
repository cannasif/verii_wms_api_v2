using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Kkd.Domain;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;
using StockEntity = verii_wms_api_v2.Modules.Stock.Domain.Stock;

namespace verii_wms_api_v2.Modules.Kkd.Application;

/// <summary>
/// Tezgâh akışı: personel karşıda beklerken açık sipariş kalemlerinden talep üretip toplamayı başlatır.
/// Açık talepler tarafındaki atama/havuz adımları burada yoktur — kartı okutan depocu işi doğrudan üstlenir.
/// Talep ve hazırlama görevi üretilmesinin sebebi, toplama ve fiziksel teslim borusunun tamamının bu ikisi
/// üzerine kurulu olmasıdır; böylece iki kanal aynı kodu paylaşır.
/// </summary>
public sealed class KkdOrderPickingService(
    IUnitOfWork uow,
    IKkdDistributionService distributions,
    IKkdRequestService requests,
    IKkdPreparationTaskService tasks) : IKkdOrderPickingService
{
    public async Task<KkdOrderPickingStartResult> StartAsync(
        KkdOrderPickingStartRequest request,
        long actor,
        CancellationToken ct = default)
    {
        Validate(request);

        var replay = await uow.Repository<KkdRequest>().Query()
            .SingleOrDefaultAsync(x => x.CorrelationId == request.IdempotencyKey, ct);
        if (replay is not null) return await ReplayAsync(replay, actor, ct);

        var orderLines = await LoadOrderLinesAsync(request, ct);
        var stocks = await LoadStocksAsync(request, ct);

        var created = await requests.CreateAsync(new KkdRequestCreateRequest(
            request.IdempotencyKey,
            request.EmployeeId,
            request.WarehouseId,
            AssignedUserId: actor,
            KkdRequestSourceType.Netsis,
            ExternalRequestNo: DistinctOrderNumbers(request),
            KkdRequestPriority.Normal,
            NeededAtUtc: null,
            request.Description,
            request.Lines.Select(line =>
            {
                var stock = stocks[line.StockId];
                var order = orderLines[Key(line.OrderNumber, line.OrderLineId)];
                return new KkdRequestLineCreateRequest(
                    GroupCodeOf(stock),
                    stock.StockName,
                    line.StockId,
                    line.Quantity,
                    order.OrderNumber,
                    order.OrderLineId.ToString());
            }).ToArray()), actor, ct);

        var claimed = await tasks.ClaimAsync(
            created.Id,
            new KkdPreparationClaimRequest(Derive(request.IdempotencyKey, "claim"), request.WarehouseId, null),
            actor,
            ct);

        return await StartPickingAsync(created.Id, claimed, request.IdempotencyKey, actor, replayed: false, ct);
    }

    /// <summary>
    /// Kota aşan kalem varsa toplama başlatılmaz: rezervasyon yapılmadan önce müdür kararı beklenir.
    /// Karar verilince aynı görev üzerinden "işe devam et" ile toplamaya geçilir.
    /// </summary>
    private async Task<KkdOrderPickingStartResult> StartPickingAsync(
        long requestId,
        KkdPreparationTaskRow task,
        Guid idempotencyKey,
        long actor,
        bool replayed,
        CancellationToken ct)
    {
        var pending = await PendingQuotaCountAsync(requestId, ct);
        if (pending > 0)
            return new(requestId, task.RequestNo, task.Id, task.TaskNo, false, pending, replayed);

        if (task.StartedAtUtc is null)
        {
            task = await tasks.StartAsync(
                task.Id,
                new KkdPreparationStartRequest(Derive(idempotencyKey, "start"), null),
                actor,
                ct);
        }
        return new(requestId, task.RequestNo, task.Id, task.TaskNo, true, 0, replayed);
    }

    /// <summary>
    /// Aynı anahtarla tekrar gelindiğinde yeni talep açılmaz; mevcut görev döner. Kota kararı bu arada
    /// verilmiş olabileceği için toplama başlatma denemesi tekrarlanır.
    /// </summary>
    private async Task<KkdOrderPickingStartResult> ReplayAsync(KkdRequest entity, long actor, CancellationToken ct)
    {
        var rows = await tasks.GetByRequestAsync(entity.Id, actor, ct);
        var task = rows.FirstOrDefault(x => x.AssignedUserId == actor && x.CompletedAtUtc is null)
            ?? rows.FirstOrDefault(x => x.CompletedAtUtc is null)
            ?? rows.LastOrDefault()
            ?? throw AppException.Conflict("Bu talep için hazırlama görevi bulunamadı.");
        return await StartPickingAsync(entity.Id, task, entity.CorrelationId, actor, replayed: true, ct);
    }

    private async Task<int> PendingQuotaCountAsync(long requestId, CancellationToken ct) =>
        await uow.Repository<KkdRequestLine>().Query()
            .CountAsync(
                x => x.RequestId == requestId
                    && (x.QuotaDecision == KkdRequestLineQuotaDecision.Pending
                        || x.QuotaDecision == KkdRequestLineQuotaDecision.Rejected),
                ct);

    /// <summary>Sipariş kalemleri canlı okunur: ekranda seçildikten sonra kapanmış ya da azalmış olabilir.</summary>
    private async Task<Dictionary<string, KkdOpenOrderLine>> LoadOrderLinesAsync(
        KkdOrderPickingStartRequest request,
        CancellationToken ct)
    {
        var rows = await distributions.GetOpenOrderLinesAsync(
            request.EmployeeId, DistinctOrderNumbers(request), ct);
        var byKey = rows.ToDictionary(x => Key(x.OrderNumber, x.OrderLineId));
        foreach (var line in request.Lines)
        {
            if (!byKey.TryGetValue(Key(line.OrderNumber, line.OrderLineId), out var order))
                throw AppException.Conflict(
                    $"{line.OrderNumber} siparişindeki kalem artık açık değil; listeyi yenileyin.");
            if (line.Quantity > order.RemainingQuantity)
                throw AppException.Conflict(
                    $"{line.OrderNumber} siparişinde kalan miktar {order.RemainingQuantity}; daha fazlası toplanamaz.");
        }
        return byKey;
    }

    private async Task<Dictionary<long, StockEntity>> LoadStocksAsync(
        KkdOrderPickingStartRequest request,
        CancellationToken ct)
    {
        var ids = request.Lines.Select(x => x.StockId).Distinct().ToArray();
        var rows = await uow.Repository<StockEntity>().Query()
            .Where(x => ids.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);
        if (rows.Count != ids.Length)
            throw AppException.NotFound("Seçilen stoklardan biri bulunamadı.");
        return rows;
    }

    /// <summary>
    /// Talep kalemi bir KKD grubuna bağlanır. Sipariş kalemindeki stok hiçbir gruba ait değilse stoğun kendi
    /// kodu grup olarak kullanılır: yetkiyi zaten sipariş verdiği için talep açılabilmeli, ama hak matrisinde
    /// karşılığı olmadığından miktar hak dışı sayılıp müdür onayına düşer.
    /// </summary>
    internal static string GroupCodeOf(StockEntity stock) =>
        (string.IsNullOrWhiteSpace(stock.GroupCode) ? stock.ErpStockCode : stock.GroupCode)
            .Trim()
            .ToUpperInvariant();

    private static string DistinctOrderNumbers(KkdOrderPickingStartRequest request) =>
        string.Join(',', request.Lines.Select(x => x.OrderNumber.Trim())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase));

    private static string Key(string orderNumber, long orderLineId) =>
        $"{orderNumber.Trim().ToUpperInvariant()}|{orderLineId}";

    internal static void Validate(KkdOrderPickingStartRequest request)
    {
        if (request.IdempotencyKey == Guid.Empty || request.EmployeeId <= 0 || request.WarehouseId <= 0)
            throw AppException.BadRequest("Idempotency anahtarı, personel ve toplama deposu zorunludur.");
        if (request.Lines.Count == 0)
            throw AppException.BadRequest("En az bir sipariş kalemi seçilmelidir.");
        if (request.Lines.Any(x => x.StockId <= 0))
            throw AppException.BadRequest("Her kalemde verilecek stok (beden) seçilmelidir.");
        if (request.Lines.Any(x => x.Quantity <= 0))
            throw AppException.BadRequest("Toplanacak miktar sıfırdan büyük olmalıdır.");
        if (request.Lines.Select(x => Key(x.OrderNumber, x.OrderLineId)).Distinct().Count() != request.Lines.Count)
            throw AppException.BadRequest("Aynı sipariş kalemi birden fazla kez seçilemez.");
    }

    /// <summary>Tek tezgâh anahtarından adım bazlı, tekrar oynatmada aynı sonucu veren anahtarlar türetir.</summary>
    private static Guid Derive(Guid idempotencyKey, string step)
    {
        var bytes = System.Security.Cryptography.MD5.HashData(
            System.Text.Encoding.UTF8.GetBytes($"kkd-order-picking|{idempotencyKey:N}|{step}"));
        return new Guid(bytes);
    }
}
