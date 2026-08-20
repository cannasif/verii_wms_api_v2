using System.Text.Json.Serialization;
using Hangfire;
using verii_wms_api_v2.Modules.ErpIntegration.Domain;
using verii_wms_api_v2.Modules.GoodsReceipt.Application;
using verii_wms_api_v2.Modules.GoodsReceipt.Domain;
using verii_wms_api_v2.Modules.Shipping.Application;
using verii_wms_api_v2.Modules.WarehouseInbound.Application;
using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using verii_wms_api_v2.Modules.WarehouseOutbound.Application;
using verii_wms_api_v2.Modules.WarehouseTransfer.Application;

namespace verii_wms_api_v2.Modules.ErpIntegration.Application;

public sealed record ErpPostRequest(Guid IdempotencyKey);
public sealed record CancelErpDocumentRequest(Guid IdempotencyKey, string Reason);
public sealed record ReconcileErpCancellationRequest(bool ErpDocumentExists, string Reason);
public sealed record ReconcileErpPostingRequest(
    bool ErpDocumentExists,
    string Reason,
    string? ErpDocumentNo = null,
    string? ErpWaybillNo = null,
    string? ErpRecordNo = null,
    string? ErpReferenceNo = null);

public enum OperationCancellationRoute
{
    LocalCompensation = 1,
    ErpCompensation = 2,
    ManualReconciliationRequired = 3,
    AlreadyCancelled = 4
}

public sealed record OperationCancellationResult(
    string SourceType,
    long SourceEntityId,
    string SourceDocumentNo,
    OperationCancellationRoute Route,
    string OperationStatus,
    string ErpStatus,
    bool ErpDeleted,
    bool WmsReversed,
    bool Replayed,
    string? ErrorCode = null,
    string? ErrorMessage = null);

public sealed record ErpPostingResult(
    long PostingRecordId,
    ErpPostingSourceType SourceType,
    long SourceEntityId,
    string SourceDocumentNo,
    ErpPostingStatus Status,
    int AttemptCount,
    string? ErpDocumentNo,
    string? ErpWaybillNo,
    string? ErpRecordNo,
    string? ErpReferenceNo,
    string? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset? CompletedAtUtc);

public sealed record ErpCancellationResult(
    long CancellationRecordId,
    long PostingRecordId,
    ErpPostingSourceType SourceType,
    long SourceEntityId,
    string SourceDocumentNo,
    long ErpRecordId,
    ErpCancellationStatus Status,
    int AttemptCount,
    string? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset? ErpDeletedAtUtc,
    DateTimeOffset? WmsReversedAtUtc,
    DateTimeOffset? CompletedAtUtc);

public interface IErpPostingService
{
    Task<ErpPostingResult> PostGoodsReceiptAsync(long id, Guid idempotencyKey, long userId, CancellationToken cancellationToken);
    Task<ErpPostingResult> PostWarehouseInboundAsync(long id, Guid idempotencyKey, long userId, CancellationToken cancellationToken);
    Task<ErpPostingResult> PostWarehouseTransferAsync(long id, Guid idempotencyKey, long userId, CancellationToken cancellationToken);
    Task<ErpPostingResult> PostWarehouseOutboundAsync(long id, Guid idempotencyKey, long userId, CancellationToken cancellationToken);
    Task<ErpPostingResult> PostShipmentAsync(long id, Guid idempotencyKey, long userId, CancellationToken cancellationToken);
    Task<ErpPostingResult> GetAsync(ErpPostingSourceType sourceType, long sourceEntityId, CancellationToken cancellationToken);
    Task<ErpPostingResult> ReconcileAsync(
        ErpPostingSourceType sourceType,
        long sourceEntityId,
        ReconcileErpPostingRequest request,
        long userId,
        CancellationToken cancellationToken);
}

public interface IGoodsReceiptErpPostingCoordinator
{
    Task<ErpPostingResult?> PostIfEligibleAsync(
        long goodsReceiptId,
        long actorUserId,
        CancellationToken cancellationToken);
}

public static class GoodsReceiptErpPostingPolicyEvaluator
{
    public static bool IsEligible(
        WarehouseOperationStatus operationStatus,
        OperationApprovalStatus approvalStatus,
        OperationQualityStatus qualityStatus,
        GoodsReceiptErpPostingPolicy postingPolicy,
        GoodsReceiptErpQualityGatePolicy qualityGatePolicy = GoodsReceiptErpQualityGatePolicy.AnyQualityPlan,
        bool hasRuleBasedQualityPlan = false,
        bool hasManualQualityPlan = false,
        bool hasConclusiveQualityInspection = false)
    {
        if (operationStatus is not (WarehouseOperationStatus.Processed or WarehouseOperationStatus.Completed))
            return false;

        var qualityGateApplies = qualityGatePolicy switch
        {
            GoodsReceiptErpQualityGatePolicy.None => false,
            GoodsReceiptErpQualityGatePolicy.RuleBasedOnly => hasRuleBasedQualityPlan,
            GoodsReceiptErpQualityGatePolicy.AnyQualityPlan =>
                hasRuleBasedQualityPlan || hasManualQualityPlan,
            _ => true
        };
        var qualityDecisionCompleted = qualityStatus is
            OperationQualityStatus.Passed
            or OperationQualityStatus.Failed
            || qualityStatus == OperationQualityStatus.InProgress
                && hasConclusiveQualityInspection;
        if (qualityGateApplies && !qualityDecisionCompleted)
            return false;

        var receiptApprovalCompleted = approvalStatus is
            OperationApprovalStatus.NotRequired or OperationApprovalStatus.Approved;
        var qualityApprovalCompleted = qualityStatus is
            OperationQualityStatus.NotRequired
            or OperationQualityStatus.Passed
            or OperationQualityStatus.Failed
            || qualityStatus == OperationQualityStatus.InProgress
                && hasConclusiveQualityInspection;

        return postingPolicy switch
        {
            GoodsReceiptErpPostingPolicy.AfterReceipt => true,
            GoodsReceiptErpPostingPolicy.AfterReceiptApproval => receiptApprovalCompleted,
            GoodsReceiptErpPostingPolicy.AfterQualityApproval => qualityApprovalCompleted,
            GoodsReceiptErpPostingPolicy.AfterAllApprovals =>
                receiptApprovalCompleted && qualityApprovalCompleted,
            _ => false
        };
    }
}

public interface IErpCancellationService
{
    Task<ErpCancellationResult> CancelAsync(
        ErpPostingSourceType sourceType,
        long sourceEntityId,
        CancelErpDocumentRequest request,
        long userId,
        CancellationToken cancellationToken);

    Task<ErpCancellationResult> GetAsync(
        ErpPostingSourceType sourceType,
        long sourceEntityId,
        CancellationToken cancellationToken);

    Task<ErpCancellationResult> ReconcileAsync(
        ErpPostingSourceType sourceType,
        long sourceEntityId,
        ReconcileErpCancellationRequest request,
        long userId,
        CancellationToken cancellationToken);
}

public interface IOperationCancellationCoordinator
{
    Task<OperationCancellationResult> CancelGoodsReceiptAsync(
        long id,
        GoodsReceiptTransitionRequest request,
        long userId,
        CancellationToken cancellationToken);

    Task<OperationCancellationResult> CancelWarehouseInboundAsync(
        long id,
        WarehouseInboundTransitionRequest request,
        long userId,
        CancellationToken cancellationToken);

    Task<OperationCancellationResult> CancelWarehouseTransferAsync(
        long id,
        WarehouseTransferTransitionRequest request,
        long userId,
        CancellationToken cancellationToken);

    Task<OperationCancellationResult> CancelWarehouseOutboundAsync(
        long id,
        WarehouseOutboundTransitionRequest request,
        long userId,
        CancellationToken cancellationToken);

    Task<OperationCancellationResult> CancelShipmentAsync(
        long id,
        ShipmentTransitionRequest request,
        long userId,
        CancellationToken cancellationToken);
}

public static class OperationCancellationPolicy
{
    public static OperationCancellationRoute Decide(
        ErpIntegrationStatus erpStatus,
        bool operationAlreadyCancelled,
        bool erpCancellationSupported)
    {
        if (operationAlreadyCancelled)
            return OperationCancellationRoute.AlreadyCancelled;

        return erpStatus switch
        {
            ErpIntegrationStatus.Processing
                or ErpIntegrationStatus.CommitUncertain
                => OperationCancellationRoute.ManualReconciliationRequired,
            ErpIntegrationStatus.Succeeded
                or ErpIntegrationStatus.Cancelled
                => erpCancellationSupported
                    ? OperationCancellationRoute.ErpCompensation
                    : OperationCancellationRoute.ManualReconciliationRequired,
            _ => OperationCancellationRoute.LocalCompensation
        };
    }
}

public interface INetsisTokenService
{
    Task<string> GetAccessTokenAsync(
        string? branchCode,
        bool forceRefresh,
        CancellationToken cancellationToken);
}

public interface INetsisRestClient
{
    Task<NetsisCallResult<NetsisItemSlipResponse>> CreateItemSlipAsync(
        NetsisItemSlipRequest request,
        CancellationToken cancellationToken);

    Task<NetsisCallResult<NetsisDeleteItemSlipResponse>> DeleteItemSlipAsync(
        NetsisItemSlipDeleteRequest request,
        CancellationToken cancellationToken);
}

public sealed record NetsisCallResult<T>(
    bool TransportSucceeded,
    bool BusinessSucceeded,
    bool CommitUncertain,
    int? HttpStatusCode,
    long DurationMs,
    T? Data,
    string? RawResponse,
    string? ErrorCode,
    string? ErrorMessage);

public sealed class NetsisItemSlipRequest
{
    [JsonPropertyName("FaturaTip")]
    public int FaturaTip { get; set; }

    [JsonPropertyName("KayitliNumaraOtomatikGuncellensin")]
    public bool KayitliNumaraOtomatikGuncellensin { get; set; }

    [JsonPropertyName("Seri")]
    public string? Seri { get; set; }

    [JsonPropertyName("FatUst")]
    public NetsisItemSlipHeader FatUst { get; set; } = new();

    [JsonPropertyName("Kalems")]
    public List<NetsisItemSlipLine> Kalems { get; set; } = [];
}

public static class NetsisItemSlipDocumentTypes
{
    public const int InterBranchWarehouseTransfer = 4;
    public const int LocalWarehouseTransfer = 5;
}

public sealed record NetsisItemSlipDeleteRequest(
    int DocumentType,
    string DocumentNo,
    string? CustomerCode,
    string? BranchCode = null)
{
    public string ToProviderId()
    {
        var documentNo = DocumentNo?.Trim();
        if (string.IsNullOrWhiteSpace(documentNo))
            throw new ArgumentException("Netsis belge numarası zorunludur.", nameof(DocumentNo));

        var customerCode = CustomerCode?.Trim() ?? string.Empty;
        if (DocumentType is not (4 or 5 or 8 or 9) && string.IsNullOrWhiteSpace(customerCode))
            throw new ArgumentException("Netsis cari kodu zorunludur.", nameof(CustomerCode));

        var faturaTip = DocumentType switch
        {
            0 => "ftSFat",
            1 => "ftAFat",
            2 => "ftSIrs",
            3 => "ftAIrs",
            4 => "ftDepo",
            5 => "ftLokalDepo",
            6 => "ftASip",
            7 => "ftSSip",
            8 => "ftAmbarG",
            9 => "ftAmbarC",
            12 => "ftAlTalep",
            13 => "ftAlTeklif",
            14 => "ftSatTalep",
            15 => "ftSatTeklif",
            _ => throw new ArgumentOutOfRangeException(
                nameof(DocumentType),
                DocumentType,
                "Netsis silme işlemi için desteklenmeyen belge tipi.")
        };

        // NetOpenX ItemSlips DELETE kimliği:
        // FaturaTip;FATIRS_NO;CARI_KOD. Ayraçları koruyup değerleri ayrı ayrı kaçır.
        return string.Join(
            ';',
            Uri.EscapeDataString(faturaTip),
            Uri.EscapeDataString(documentNo),
            Uri.EscapeDataString(customerCode));
    }
}

public enum NetsisItemSlipInvoiceType
{
    Empty = 0,
    DomesticClosed = 1,
    DomesticOpen = 2,
    Foreign = 8
}

public enum NetsisWarehouseMovementType
{
    Opening = 0,
    Warehouses = 1,
    Production = 2,
    Miscellaneous = 3,
    Consignment = 4
}

/// <summary>
/// Runs durable WMS follow-up work only after the goods-receipt ERP posting is conclusive.
/// The implementation is idempotent; Hangfire retries and the recovery scan may safely overlap.
/// </summary>
public interface IGoodsReceiptErpSuccessJob
{
    [AutomaticRetry(Attempts = 5, DelaysInSeconds = [30, 60, 120, 300, 600])]
    [DisableConcurrentExecution(900)]
    Task ProcessGoodsReceiptAsync(
        long goodsReceiptId,
        long actorUserId,
        CancellationToken cancellationToken = default);

    [AutomaticRetry(Attempts = 0)]
    [DisableConcurrentExecution(900)]
    Task RetryPendingAsync(CancellationToken cancellationToken = default);
}

public sealed class NetsisItemSlipHeader
{
    [JsonPropertyName("CariKod")]
    public string? CariKod { get; set; }

    [JsonPropertyName("CARI_KOD2")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SecondaryCustomerCode { get; set; }

    [JsonPropertyName("FATIRS_NO")]
    public string? FisNo { get; set; }

    [JsonPropertyName("BELGE_NO")]
    public string? BelgeNo { get; set; }

    [JsonPropertyName("Tarih")]
    public DateTime Tarih { get; set; }

    [JsonPropertyName("FIYATTARIHI")]
    public string? FiyatTarihi { get; set; }

    [JsonPropertyName("SIPARIS_TEST")]
    public string? SiparisTeslimTarihi { get; set; }

    [JsonPropertyName("FiiliTarih")]
    public DateTime FiiliTarih { get; set; }

    [JsonPropertyName("Proje_Kodu")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ProjeKodu { get; set; }

    [JsonPropertyName("TIP")]
    public int Tip { get; set; }

    [JsonPropertyName("TIPI")]
    public NetsisItemSlipInvoiceType Tipi { get; set; } = NetsisItemSlipInvoiceType.DomesticClosed;

    [JsonPropertyName("AMBHARTUR")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public NetsisWarehouseMovementType? WarehouseMovementType { get; set; }

    [JsonPropertyName("GCKOD_CIKIS")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? SourceBranchCode { get; set; }

    [JsonPropertyName("GCKOD_GIRIS")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? TargetBranchCode { get; set; }

    [JsonPropertyName("EXPORTTYPE")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ExportType { get; set; }

    [JsonPropertyName("EXPORTREFNO")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ExportReferenceNumber { get; set; }

    [JsonPropertyName("SUBE_KODU")]
    public int SubeKodu { get; set; }

    [JsonPropertyName("Aciklama")]
    public string? Aciklama { get; set; }

    [JsonPropertyName("DEPO_KODU")]
    public int? DepoKodu { get; set; }

    [JsonPropertyName("Seri")]
    public string? Seri { get; set; }

    [JsonPropertyName("KDV_DAHILMI")]
    public bool KdvDahilMi { get; set; }
}

public sealed class NetsisItemSlipLine
{
    [JsonPropertyName("StokKodu")]
    public string StokKodu { get; set; } = string.Empty;

    [JsonPropertyName("STra_GCMIK")]
    public decimal Miktar { get; set; }

    [JsonPropertyName("DEPO_KODU")]
    public int? DepoKodu { get; set; }

    // NetOpenX ItemSlips/FatKalem uses DEPO_KODU for the source warehouse
    // and Gir_Depo_Kodu for the destination warehouse.
    [JsonPropertyName("Gir_Depo_Kodu")]
    public int? GirisDepoKodu { get; set; }

    // Internal source-code mirror used by mapping validation and consolidation.
    // FatKalem has no CikisDepoKodu wire member; DEPO_KODU above is authoritative.
    [JsonIgnore]
    public int? CikisDepoKodu { get; set; }

    [JsonPropertyName("YapKod")]
    public string? ConfigurationCode { get; set; }

    [JsonPropertyName("SeriNo")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SeriNo { get; set; }

    [JsonPropertyName("Aciklama")]
    public string? Aciklama { get; set; }

    [JsonPropertyName("STra_NF")]
    public decimal NetFiyat { get; set; }

    [JsonPropertyName("STra_BF")]
    public decimal BrutFiyat { get; set; }

    [JsonPropertyName("STra_SIPNUM")]
    public string SiparisNumarasi { get; set; } = string.Empty;

    [JsonPropertyName("STra_SIPKONT")]
    public int SiparisKontrol { get; set; }

    [JsonPropertyName("ProjeKodu")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ProjeKodu { get; set; }
}

public sealed class NetsisItemSlipResponse
{
    public bool IsSuccessful { get; set; }
    public bool? IsSuccessStatusCode { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorDesc { get; set; }
    public string? ErrorDescription { get; set; }
    public NetsisItemSlipResponseData? Data { get; set; }
}

public sealed class NetsisItemSlipResponseData
{
    public string? FisNo { get; set; }
    public string? BelgeNo { get; set; }
    public string? KayitNo { get; set; }
    public string? ReferenceNumber { get; set; }
}

public sealed class NetsisDeleteItemSlipResponse
{
    public bool? IsSuccessful { get; set; }
    public bool? IsSuccessStatusCode { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorDesc { get; set; }
    public string? ErrorDescription { get; set; }
}
