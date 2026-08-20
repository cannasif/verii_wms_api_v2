namespace verii_wms_api_v2.Modules.Kkd.Application;

/// <summary>
/// "Fiziksel Teslim Onayı": hazırlama görevinde okutulup KKD bekleme rafına alınmış tüm kalemleri
/// tek çağrıda dağıtım + ambar çıkışı + ERP gönderimine dönüştürür. Miktar sorulmaz; teslim edilen
/// miktar okutma journal'ının kendisidir.
/// </summary>
public sealed record KkdPhysicalDeliveryRequest(
    Guid IdempotencyKey,
    /// <summary>Boş bırakılırsa şubenin varsayılan ambar çıkış serisi kullanılır.</summary>
    long? DocumentSeriesId = null,
    string? Description = null,
    /// <summary>Tezgâh (anlık) teslimi: personel malı alıp gittiği için görev kapatılır ve okutulmayan
    /// kalan miktarın rezervasyonu serbest bırakılır. Açık talepler kanalında false'tur; orada yarım
    /// bırakılan iş açık kalır ve kalanı sonra toplanır.</summary>
    bool CloseTaskAfterDelivery = false);

public sealed record KkdPhysicalDeliveryLine(
    string StockCode,
    string StockName,
    decimal Quantity,
    string UnitCode,
    string? LotNo,
    string? SerialNo);

public sealed record KkdPhysicalDeliveryResult(
    long DistributionId,
    string DistributionDocumentNo,
    string DistributionStatus,
    long WarehouseOutboundId,
    string WarehouseOutboundDocumentNo,
    string WarehouseOutboundStatus,
    string ExcessApprovalStatus,
    string ErpStatus,
    string? ErpDocumentNo,
    string? ErpErrorMessage,
    /// <summary>Teslim fişi numarası; ayrı seri üretilmez, dağıtım belge numarası kullanılır.</summary>
    string ReceiptNo,
    DateTimeOffset? ReceiptDateUtc,
    string RecipientCode,
    string RecipientName,
    string DeliveredByName,
    IReadOnlyList<KkdPhysicalDeliveryLine> Lines,
    bool Replayed);

public interface IKkdPhysicalDeliveryService
{
    Task<KkdPhysicalDeliveryResult> DeliverAsync(
        long taskId,
        KkdPhysicalDeliveryRequest request,
        long actor,
        CancellationToken ct = default);
}
