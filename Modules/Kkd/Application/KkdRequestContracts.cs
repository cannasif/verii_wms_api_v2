using verii_wms_api_v2.Modules.BarcodeDesigner.Application;
using verii_wms_api_v2.Modules.Kkd.Domain;
using verii_wms_api_v2.Shared;

namespace verii_wms_api_v2.Modules.Kkd.Application;

public sealed record KkdRequestLineCreateRequest(
    string GroupCode,
    string? GroupName,
    long? StockId,
    decimal Quantity,
    string? ExternalOrderNo = null,
    string? ExternalOrderLineId = null);

public sealed record KkdRequestCreateRequest(
    Guid IdempotencyKey,
    long EmployeeId,
    long? WarehouseId,
    long? AssignedUserId,
    KkdRequestSourceType SourceType,
    string? ExternalRequestNo,
    KkdRequestPriority Priority,
    DateTimeOffset? NeededAtUtc,
    string? Description,
    IReadOnlyList<KkdRequestLineCreateRequest> Lines);

public sealed record KkdRequestResolveLineRequest(
    Guid IdempotencyKey,
    long StockId,
    string Reason,
    string? ExpectedRowVersion);

public sealed record KkdRequestAssignRequest(
    long? WarehouseId,
    long? AssignedUserId,
    string? ExpectedRowVersion);

public sealed record KkdRequestCancelRequest(
    Guid IdempotencyKey,
    string Reason,
    string? ExpectedRowVersion);

/// <summary>İptal edilmiş bir talebi tekrar beklemeye alır (Hazırlamada'dan gelen "beklemeye geri al" ile karıştırılmamalı).</summary>
public sealed record KkdRequestReactivateRequest(
    Guid IdempotencyKey,
    string? ExpectedRowVersion);

/// <summary>Üretim iş emirleri sayfasındaki sekme modelinin KKD karşılığı.</summary>
public enum KkdRequestBoardTab
{
    All = 0,
    Pending = 1,
    Preparing = 2,
    Completed = 3,
    Cancelled = 4,
    Mine = 5,
    /// <summary>Sadece kota onayı yetkisi olanlara gösterilir: en az bir kalemi QuotaDecision=Pending olan talepler.</summary>
    QuotaPending = 6
}

public sealed record KkdRequestTabCounts(
    int Pending,
    int Preparing,
    int Completed,
    int Cancelled,
    int Mine,
    int QuotaPending);

public sealed record KkdRequestGridRow(
    long Id,
    string RequestNo,
    string Status,
    string Priority,
    string SourceType,
    long EmployeeId,
    string EmployeeCode,
    string EmployeeName,
    string DepartmentName,
    string RoleName,
    long? WarehouseId,
    long? AssignedUserId,
    string? ExternalRequestNo,
    int TotalLineCount,
    int UnresolvedLineCount,
    decimal RequestedQuantity,
    decimal AllocatedQuantity,
    decimal DeliveredQuantity,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset? NeededAtUtc,
    long? CreatedBy,
    DateTime? CreatedDate,
    long? UpdatedBy,
    DateTime? UpdatedDate)
{
    public string? AssignedUserName { get; init; }
    public long? LinkedDistributionId { get; init; }
    /// <summary>Bağlı son dağıtımın durumu; Tamamlanan sekmesindeki ERP rozetini besler.</summary>
    public string? LinkedDistributionStatus { get; init; }
    public string? LinkedDistributionFailureReason { get; init; }
    public long? WarehouseOutboundId { get; init; }
    public string RowVersion { get; init; } = string.Empty;
    /// <summary>Aktif (Atandı/Hazırlanıyor) hazırlama görevi sayısı.</summary>
    public int ActiveTaskCount { get; init; }
    /// <summary>Aktif bir göreve bağlanmamış açık kalem sayısı.</summary>
    public int UnassignedLineCount { get; init; }
    /// <summary>İstek sahibinin bu talepteki aktif görevi (Benim İşlerim navigasyonu).</summary>
    public long? MyActiveTaskId { get; init; }
    /// <summary>MyActiveTaskId "Bu işi yapıyorum" ile başlatıldı mı (raf ataması + rezervasyon yapıldı) — "Toplama yap"/"İşe devam et" ayrımı için.</summary>
    public bool MyActiveTaskStarted { get; init; }
    /// <summary>MyActiveTaskId'nin canlı toplanan (PreparedQuantity) toplamı — henüz teslim edilmemiş.</summary>
    public decimal MyActiveTaskPreparedQuantity { get; init; }
    /// <summary>Aktif görev atanan kullanıcı adları (Hazırlamada kolonunda gösterim).</summary>
    public IReadOnlyList<string> ActiveAssigneeNames { get; init; } = Array.Empty<string>();
    /// <summary>Depo havuzuna bırakılmış (kişiye atanmamış) aktif bir görev var mı.</summary>
    public bool HasPoolTask { get; init; }
    /// <summary>Havuzdaki görevin id'si; "Havuzdan üzerime al" aksiyonu bunu kullanır.</summary>
    public long? PoolTaskId { get; init; }
    /// <summary>MyActiveTaskId'nin kalemlerinden kota kararı bekleyen (Pending/Rejected) sayısı — toplama bu yüzden başlayamıyor olabilir.</summary>
    public int MyActiveTaskQuotaPendingCount { get; init; }
    /// <summary>MyActiveTaskId'nin kalemlerinden müdürce onaylanmış (Approved) kota kararı sayısı.</summary>
    public int MyActiveTaskQuotaApprovedCount { get; init; }
}

public sealed record KkdRequestLineResolutionRow(
    long Id,
    long? PreviousStockId,
    long StockId,
    string StockCode,
    string? StockName,
    string Reason,
    long? ResolvedBy,
    DateTimeOffset ResolvedAtUtc);

public sealed record KkdRequestLineDetail(
    long Id,
    int LineNo,
    string GroupCode,
    string? GroupName,
    long? StockId,
    string? StockCode,
    string? StockName,
    string UnitCode,
    decimal RequestedQuantity,
    decimal AllocatedQuantity,
    decimal DeliveredQuantity,
    decimal RemainingQuantity,
    string Status,
    string? ExternalOrderNo,
    string? ExternalOrderLineId,
    long? ResolvedByUserId,
    DateTimeOffset? ResolvedAtUtc,
    string? ResolutionReason,
    string QuotaDecision,
    long? QuotaDecisionByUserId,
    DateTimeOffset? QuotaDecisionAtUtc,
    string RowVersion,
    IReadOnlyList<KkdRequestLineResolutionRow> Resolutions);

public sealed record KkdRequestDetail(
    long Id,
    Guid CorrelationId,
    string RequestNo,
    string Status,
    string Priority,
    string SourceType,
    long EmployeeId,
    string EmployeeCode,
    string EmployeeName,
    string DepartmentName,
    string RoleName,
    long CustomerId,
    long? WarehouseId,
    long? AssignedUserId,
    string? ExternalRequestNo,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset? NeededAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? ReadyAtUtc,
    DateTimeOffset? CompletedAtUtc,
    DateTimeOffset? CancelledAtUtc,
    string? CancellationReason,
    string? Description,
    string RowVersion,
    IReadOnlyList<KkdRequestLineDetail> Lines);

public sealed record KkdPreparationAssignGroup(long? AssignedUserId, IReadOnlyList<long> LineIds);

/// <summary>Üretimdeki "Atamayı yap" karşılığı: kalem grupları kişilere bölünür, kişi başına görev oluşur.
/// AssignedUserId null olan grup depo havuzuna bırakılır; o depodaki herkes üzerine alabilir.</summary>
public sealed record KkdPreparationAssignRequest(
    Guid IdempotencyKey,
    long WarehouseId,
    IReadOnlyList<KkdPreparationAssignGroup> Groups,
    string? ExpectedRowVersion);

public sealed record KkdPreparationClaimRequest(
    Guid IdempotencyKey,
    long WarehouseId,
    string? ExpectedRowVersion);

/// <summary>Depo havuzundaki (sahipsiz) bir görevi aktörün üzerine almasını sağlar.</summary>
public sealed record KkdPreparationClaimTaskRequest(
    Guid IdempotencyKey,
    string? ExpectedRowVersion);

public sealed record KkdPreparationHandoffRequest(
    Guid IdempotencyKey,
    long ToUserId,
    string Reason,
    string? ExpectedRowVersion);

public sealed record KkdPreparationReturnRequest(
    Guid IdempotencyKey,
    string Reason,
    string? ExpectedRowVersion);

/// <summary>"Bu işi yapıyorum": havuz görevinde önce üzerine alır, sonra stoğu bilinen satırlara
/// raf ataması + gerçek rezervasyon yapar. Stoğu henüz bilinmeyen satırlar atlanır.</summary>
public sealed record KkdPreparationStartRequest(
    Guid IdempotencyKey,
    string? ExpectedRowVersion);

public sealed record KkdRouteCandidateRow(
    long LocationId,
    string LocationCode,
    string LocationName,
    decimal AvailableQuantity,
    string? SerialNo,
    string? LotNo);

public sealed record KkdRouteCandidatesResult(
    bool IsSerial,
    IReadOnlyList<KkdRouteCandidateRow> Candidates);

public sealed record KkdRouteSplitSelection(long LocationId, decimal Quantity, string? SerialNo);

public sealed record KkdRouteSplitRequest(
    Guid IdempotencyKey,
    IReadOnlyList<KkdRouteSplitSelection> Selections,
    string? ExpectedTaskLineRowVersion);

/// <summary>Bir görev satırının bir rafa ayrılmış rezervasyon/toplama izi.</summary>
public sealed record KkdPreparationTaskLineLocationRow(
    long LocationId,
    string LocationCode,
    string LocationName,
    decimal ReservedQuantity,
    decimal PickedQuantity,
    string? SerialNo,
    string? LotNo);

public sealed record KkdPreparationTaskLineRow(
    long Id,
    long RequestLineId,
    int LineNo,
    string GroupCode,
    string? GroupName,
    long? StockId,
    string? StockCode,
    string? StockName,
    string UnitCode,
    decimal Quantity,
    decimal PreparedQuantity,
    decimal DeliveredQuantity,
    string LineStatus,
    string RequestLineRowVersion,
    string QuotaDecision,
    IReadOnlyList<KkdPreparationTaskLineLocationRow> Locations);

public sealed record KkdPreparationTaskRow(
    long Id,
    string TaskNo,
    long RequestId,
    string RequestNo,
    string Status,
    long? AssignedUserId,
    string? AssignedUserName,
    long WarehouseId,
    long? PreviousTaskId,
    string? PreviousTaskNo,
    long? OriginUserId,
    string? OriginUserName,
    long? DistributionId,
    long? WarehouseOutboundId,
    DateTimeOffset AssignedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? ClosureReason,
    string RowVersion,
    IReadOnlyList<KkdPreparationTaskLineRow> Lines)
{
    /// <summary>Kişiye değil depo havuzuna bırakılmış, henüz kimsenin üzerine almadığı görev.</summary>
    public bool IsPool => AssignedUserId is null;
}

public sealed record KkdRequestCancelPrecheckResult(
    bool CanCancel,
    IReadOnlyList<string> Blockers,
    long? ActiveDistributionId,
    long? ActiveWarehouseOutboundId);

public sealed record KkdPreparationResolveScanRequest(
    string Barcode,
    long? ExpectedTaskLineId = null);

public sealed record KkdPreparationResolveScanResult(
    long TaskLineId,
    long RequestLineId,
    bool NeedsGroupResolve,
    string GroupCode,
    long StockId,
    string StockCode,
    string StockName,
    string UnitCode,
    string? LotNo,
    string? SerialNo,
    long? SuggestedLocationId,
    bool RequireSerial,
    bool RequireLot,
    decimal RemainingQuantity,
    decimal DefaultQuantity,
    bool IsSerial,
    /// <summary>Üretim ile aynı: seri veya (depo eşiği > 0 ve default ≤ eşik).</summary>
    bool CanAutoPick,
    /// <summary>Kaynak deponun AutoPickWithoutConfirmMaxQuantity değeri.</summary>
    decimal? AutoPickWithoutConfirmMaxQuantity,
    string RawBarcode,
    string Source,
    /// <summary>Birden fazla raf/seri varsa kullanıcının seçebilmesi için tüm adaylar.</summary>
    IReadOnlyList<WarehouseBarcodeBalanceCandidate> BalanceCandidates);

public sealed record KkdPreparationScanPickRequest(
    Guid IdempotencyKey,
    string Barcode,
    long? ExpectedTaskLineId,
    decimal? Quantity,
    long? SourceLocationId,
    /// <summary>Grup→stok çözümünde talep kalemi concurrency kontrolü.</summary>
    string? ExpectedRequestLineRowVersion = null,
    /// <summary>Üretimdeki ConfirmAboveThreshold: eşik üstü miktarda kullanıcı onayı.</summary>
    bool ConfirmAboveThreshold = false);

public sealed record KkdPreparationScanPickTracking(
    decimal Quantity,
    string? LotNo,
    string? SerialNo,
    long? SourceLocationId);

public sealed record KkdPreparationScanPickResult(
    bool IsReplay,
    long TaskLineId,
    long RequestLineId,
    decimal AcceptedQuantity,
    decimal LinePreparedQuantity,
    decimal LineQuantity,
    long StockId,
    string StockCode,
    string StockName,
    string? LotNo,
    string? SerialNo,
    long? SourceLocationId,
    KkdPreparationTaskRow Task);

public interface IKkdPreparationTaskService
{
    Task<IReadOnlyList<KkdPreparationTaskRow>> GetByRequestAsync(long requestId, long actor, CancellationToken ct = default);
    Task<IReadOnlyList<KkdPreparationTaskRow>> AssignAsync(long requestId, KkdPreparationAssignRequest request, long actor, CancellationToken ct = default);
    Task<KkdPreparationTaskRow> ClaimAsync(long requestId, KkdPreparationClaimRequest request, long actor, CancellationToken ct = default);
    Task<KkdPreparationTaskRow> ClaimTaskAsync(long taskId, KkdPreparationClaimTaskRequest request, long actor, CancellationToken ct = default);
    Task<KkdPreparationTaskRow> HandoffAsync(long taskId, KkdPreparationHandoffRequest request, long actor, CancellationToken ct = default);
    Task ReturnAsync(long taskId, KkdPreparationReturnRequest request, long actor, CancellationToken ct = default);
    Task<KkdPreparationTaskRow> StartAsync(long taskId, KkdPreparationStartRequest request, long actor, CancellationToken ct = default);
    Task<KkdRouteCandidatesResult> GetRouteCandidatesAsync(long taskLineId, long actor, CancellationToken ct = default);
    Task<KkdPreparationTaskRow> ApplyRouteSplitAsync(long taskLineId, KkdRouteSplitRequest request, long actor, CancellationToken ct = default);
}

/// <summary>Yanlış okutulan bir taramayı geri alır: gerçek stok hareketini tersine çevirir.</summary>
public sealed record KkdPreparationUnpickRequest(Guid IdempotencyKey);

public sealed record KkdPreparationUnpickResult(long ScanId, long TaskLineId, decimal RevertedQuantity, KkdPreparationTaskRow Task);

/// <summary>"Son okutmalar" listesi — geri alma (Unpick) butonunun hedef aldığı satırlar.</summary>
public sealed record KkdPreparationScanRow(
    long Id,
    long TaskLineId,
    long StockId,
    string StockCode,
    string StockName,
    decimal Quantity,
    string UnitCode,
    string? LotNo,
    string? SerialNo,
    long? SourceLocationId,
    DateTimeOffset ScannedAtUtc,
    bool IsReversed,
    bool CanUnpick);

public interface IKkdPreparationScanPickService
{
    Task<KkdPreparationResolveScanResult> ResolveScanAsync(long taskId, KkdPreparationResolveScanRequest request, long actor, CancellationToken ct = default);
    Task<KkdPreparationScanPickResult> ScanPickAsync(long taskId, KkdPreparationScanPickRequest request, long actor, CancellationToken ct = default);
    Task<IReadOnlyList<KkdPreparationScanPickTracking>> GetStagedTrackingsAsync(long taskId, long requestLineId, CancellationToken ct = default);
    Task<KkdPreparationUnpickResult> UnpickAsync(long taskId, long scanId, KkdPreparationUnpickRequest request, long actor, CancellationToken ct = default);
    Task<IReadOnlyList<KkdPreparationScanRow>> GetRecentScansAsync(long taskId, long actor, CancellationToken ct = default);
}

public interface IKkdRequestService
{
    Task<PagedResponse<KkdRequestGridRow>> GetPagedAsync(PagedRequest request, long actor, KkdRequestBoardTab tab = KkdRequestBoardTab.All, CancellationToken ct = default);
    Task<KkdRequestTabCounts> GetTabCountsAsync(long actor, CancellationToken ct = default);
    Task<KkdRequestDetail> GetDetailAsync(long id, long actor, CancellationToken ct = default);
    Task<KkdRequestDetail> CreateAsync(KkdRequestCreateRequest request, long actor, CancellationToken ct = default);
    Task<KkdRequestDetail> ResolveLineAsync(long requestId, long lineId, KkdRequestResolveLineRequest request, long actor, CancellationToken ct = default);
    Task<KkdRequestDetail> AssignAsync(long id, KkdRequestAssignRequest request, long actor, CancellationToken ct = default);
    Task<KkdRequestCancelPrecheckResult> GetCancelPrecheckAsync(long id, CancellationToken ct = default);
    Task<KkdRequestDetail> CancelAsync(long id, KkdRequestCancelRequest request, long actor, CancellationToken ct = default);
    Task<KkdRequestDetail> ReactivateAsync(long id, KkdRequestReactivateRequest request, long actor, CancellationToken ct = default);
    Task<KkdQuotaDecisionResult> DecideQuotaAsync(long lineId, KkdQuotaDecisionRequest request, long actor, CancellationToken ct = default);
}
