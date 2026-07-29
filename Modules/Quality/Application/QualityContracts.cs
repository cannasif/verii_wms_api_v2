using verii_wms_api_v2.Modules.Quality.Domain;
using verii_wms_api_v2.Shared;

namespace verii_wms_api_v2.Modules.Quality.Application;

public sealed record QualityParameterDto(long Id, string BranchCode, bool AutoCreateInspectionOnReceipt,
    QualityInspectionMode DefaultInspectionMode, QualityFailAction DefaultFailAction, bool HoldInventoryUntilDecision,
    bool BlockPutawayUntilDecision, bool BlockErpPostingUntilDecision, bool RequireManagerApprovalForRelease,
    bool AllowPartialDecision, bool AllowDirectReceiptWhenNoRule, bool BlockReceiptWhenLotMissing,
    bool BlockReceiptWhenSerialMissing, bool BlockReceiptWhenExpiryMissing, long? DefaultQualityLocationId,
    long? DefaultQuarantineLocationId, long? DefaultRejectLocationId, long? UpdatedBy, DateTime? UpdatedDate);

public sealed record UpdateQualityParameterRequest(string BranchCode, bool AutoCreateInspectionOnReceipt,
    QualityInspectionMode DefaultInspectionMode, QualityFailAction DefaultFailAction, bool HoldInventoryUntilDecision,
    bool BlockPutawayUntilDecision, bool BlockErpPostingUntilDecision, bool RequireManagerApprovalForRelease,
    bool AllowPartialDecision, bool AllowDirectReceiptWhenNoRule, bool BlockReceiptWhenLotMissing,
    bool BlockReceiptWhenSerialMissing, bool BlockReceiptWhenExpiryMissing, long? DefaultQualityLocationId,
    long? DefaultQuarantineLocationId, long? DefaultRejectLocationId);

public sealed record QualityRuleUpsertRequest(string BranchCode, string ScopeType, long? StockId, string? StockGroupCode,
    QualityInspectionMode InspectionMode, QualitySamplingMode SamplingMode, decimal SamplingValue,
    QualityFailAction FailAction, bool AutoQuarantine, bool RequireLot, bool RequireSerial,
    bool RequireExpiryDate, int? MinimumRemainingShelfLifeDays, bool IsActive, string? Description);

public sealed record QualityStockGroupOption(string Code, int StockCount);

public sealed class QualityRuleGridRow
{
    public long Id { get; init; } public string BranchCode { get; init; } = "0"; public string ScopeType { get; init; } = string.Empty;
    public long? StockId { get; init; } public string? StockCode { get; init; } public string? StockName { get; init; } public string? StockGroupCode { get; init; }
    public string InspectionMode { get; init; } = string.Empty; public string SamplingMode { get; init; } = string.Empty; public decimal SamplingValue { get; init; }
    public string FailAction { get; init; } = string.Empty; public bool AutoQuarantine { get; init; } public bool RequireLot { get; init; }
    public bool RequireSerial { get; init; } public bool RequireExpiryDate { get; init; } public int? MinimumRemainingShelfLifeDays { get; init; }
    public bool IsActive { get; init; } public string? Description { get; init; } public long? CreatedBy { get; init; } public DateTime? CreatedDate { get; init; }
    public long? UpdatedBy { get; init; } public DateTime? UpdatedDate { get; init; }
}

public sealed class QualityInspectionGridRow
{
    public long Id { get; init; } public string BranchCode { get; init; } = "0"; public string InspectionNo { get; init; } = string.Empty;
    public string SourceDocumentType { get; init; } = string.Empty; public long SourceDocumentId { get; init; } public string SourceDocumentNo { get; init; } = string.Empty;
    public long WarehouseId { get; init; } public int? WarehouseCode { get; init; } public string? WarehouseName { get; init; } public long? SupplierId { get; init; }
    public string? SourceWaybillNo { get; init; } public string? CreatedByName { get; init; }
    public string Status { get; init; } = string.Empty; public int LineCount { get; init; } public decimal TotalQuantity { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; } public DateTimeOffset? QueuedAtUtc { get; init; } public DateTimeOffset? DecidedAtUtc { get; init; } public long? InspectorUserId { get; init; }
    public long? CreatedBy { get; init; } public DateTime? CreatedDate { get; init; } public long? UpdatedBy { get; init; } public DateTime? UpdatedDate { get; init; }
}

public sealed record ResolvedQualityPolicy(string Source, long? RuleId, QualityInspectionMode InspectionMode,
    QualitySamplingMode SamplingMode, decimal SamplingValue, QualityFailAction FailAction, bool AutoQuarantine,
    bool RequireLot, bool RequireSerial, bool RequireExpiryDate, int? MinimumRemainingShelfLifeDays,
    bool HoldInventoryUntilDecision, bool BlockPutawayUntilDecision, bool BlockErpPostingUntilDecision);

public sealed record DecideQualityInspectionRequest(Guid IdempotencyKey, QualityDecision Decision,
    string? ReasonCode, string? Note, IReadOnlyList<long>? LineIds, string? RowVersion);

public sealed record QualityInspectionLineDto(long Id, long? GoodsReceiptLineId, long StockId,
    string StockCode, string? StockName, string? YapCode, string? LotNo, string? SerialNo,
    DateOnly? ExpiryDate, decimal Quantity, decimal SampleQuantity, decimal AcceptedQuantity,
    decimal RejectedQuantity, decimal QuarantineQuantity, QualityDecision Decision,
    string? ReasonCode, string? ReasonNote, long? DecisionBy, DateTimeOffset? DecisionAtUtc);

public sealed record QualityInspectionDetail(QualityInspectionGridRow Header,
    IReadOnlyList<QualityInspectionLineDto> Lines, string? Note, byte[] RowVersion,
    bool AllowPartialDecision, bool RequireManagerApprovalForRelease);

public interface IQualityService
{
    Task<QualityParameterDto> GetParametersAsync(string branchCode, CancellationToken ct = default);
    Task<QualityParameterDto> UpdateParametersAsync(UpdateQualityParameterRequest request, long actor, CancellationToken ct = default);
    Task<PagedResponse<QualityRuleGridRow>> GetRulesPagedAsync(PagedRequest request, CancellationToken ct = default);
    Task<PagedResponse<QualityStockGroupOption>> GetStockGroupsPagedAsync(string branchCode, PagedRequest request, CancellationToken ct = default);
    Task<long> CreateRuleAsync(QualityRuleUpsertRequest request, long actor, CancellationToken ct = default);
    Task UpdateRuleAsync(long id, QualityRuleUpsertRequest request, long actor, CancellationToken ct = default);
    Task DeleteRuleAsync(long id, long actor, CancellationToken ct = default);
    Task<PagedResponse<QualityInspectionGridRow>> GetInspectionsPagedAsync(PagedRequest request, CancellationToken ct = default);
    Task<QualityInspectionDetail> GetInspectionAsync(long id, CancellationToken ct = default);
    Task DecideInspectionAsync(long id, DecideQualityInspectionRequest request, long actor,
        bool canReleaseQuarantine, CancellationToken ct = default);
}

public interface IQualityPolicyResolver
{
    Task<ResolvedQualityPolicy> ResolveAsync(string branchCode, long stockId, string? stockGroupCode, CancellationToken ct = default);
}
