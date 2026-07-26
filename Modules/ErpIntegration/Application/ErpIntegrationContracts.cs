using System.Text.Json.Serialization;
using verii_wms_api_v2.Modules.ErpIntegration.Domain;

namespace verii_wms_api_v2.Modules.ErpIntegration.Application;

public sealed record ErpPostRequest(Guid IdempotencyKey);

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

public interface IErpPostingService
{
    Task<ErpPostingResult> PostGoodsReceiptAsync(long id, Guid idempotencyKey, long userId, CancellationToken cancellationToken);
    Task<ErpPostingResult> PostWarehouseTransferAsync(long id, Guid idempotencyKey, long userId, CancellationToken cancellationToken);
    Task<ErpPostingResult> PostShipmentAsync(long id, Guid idempotencyKey, long userId, CancellationToken cancellationToken);
    Task<ErpPostingResult> GetAsync(ErpPostingSourceType sourceType, long sourceEntityId, CancellationToken cancellationToken);
}

public interface INetsisTokenService
{
    Task<string> GetAccessTokenAsync(bool forceRefresh, CancellationToken cancellationToken);
}

public interface INetsisRestClient
{
    Task<NetsisCallResult<NetsisItemSlipResponse>> CreateItemSlipAsync(
        NetsisItemSlipRequest request,
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

public sealed class NetsisItemSlipHeader
{
    [JsonPropertyName("CariKod")]
    public string? CariKod { get; set; }

    [JsonPropertyName("FATIRS_NO")]
    public string? FisNo { get; set; }

    [JsonPropertyName("BELGE_NO")]
    public string? BelgeNo { get; set; }

    [JsonPropertyName("Tarih")]
    public DateTime Tarih { get; set; }

    [JsonPropertyName("FiiliTarih")]
    public DateTime FiiliTarih { get; set; }

    [JsonPropertyName("TIP")]
    public int Tip { get; set; }

    [JsonPropertyName("TIPI")]
    public int Tipi { get; set; } = 1;

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

    [JsonPropertyName("Gir_Depo_Kodu")]
    public int? GirisDepoKodu { get; set; }

    [JsonPropertyName("Cikis_Depo_Kodu")]
    public int? CikisDepoKodu { get; set; }

    [JsonPropertyName("YapKod")]
    public string? YapKod { get; set; }

    [JsonPropertyName("SeriNo")]
    public string? SeriNo { get; set; }

    [JsonPropertyName("Aciklama")]
    public string? Aciklama { get; set; }

    [JsonPropertyName("SiparisNo")]
    public string? SiparisNo { get; set; }
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
