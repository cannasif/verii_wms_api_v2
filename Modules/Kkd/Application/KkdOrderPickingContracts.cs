namespace verii_wms_api_v2.Modules.Kkd.Application;

/// <summary>
/// Tezgâh (kiosk) akışının tek kalemi. Beden/stok seçimi personel karşıdayken yapıldığı için stok
/// zorunludur; bu kanalda "stoğu belirsiz" kalem oluşmaz.
/// </summary>
public sealed record KkdOrderPickingLineRequest(
    string OrderNumber,
    long OrderLineId,
    long StockId,
    decimal Quantity);

/// <summary>
/// "Toplamaya başla": seçilen açık sipariş kalemlerinden bir KKD talebi üretir, hazırlama görevini açıp
/// okutan depocunun üzerine alır ve toplamayı başlatır. Kota aşımı varsa toplama başlatılmaz; kalem
/// müdür kararına düşer ve karar verilince aynı görevden devam edilir.
/// </summary>
public sealed record KkdOrderPickingStartRequest(
    Guid IdempotencyKey,
    long EmployeeId,
    long WarehouseId,
    string? Description,
    IReadOnlyList<KkdOrderPickingLineRequest> Lines);

public sealed record KkdOrderPickingStartResult(
    long RequestId,
    string RequestNo,
    long TaskId,
    string TaskNo,
    /// <summary>Rezervasyon yapıldı ve barkod okutmaya hazır. False ise kota kararı bekleniyor.</summary>
    bool PickingStarted,
    int QuotaPendingLineCount,
    bool Replayed);

public interface IKkdOrderPickingService
{
    Task<KkdOrderPickingStartResult> StartAsync(
        KkdOrderPickingStartRequest request,
        long actor,
        CancellationToken ct = default);
}
