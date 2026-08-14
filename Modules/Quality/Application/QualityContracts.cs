using verii_wms_api_v2.Modules.Quality.Domain;
using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using verii_wms_api_v2.Shared;

namespace verii_wms_api_v2.Modules.Quality.Application;

public sealed record QualityQuarantineDestinationDto(
    long Id,
    long LocationId,
    long WarehouseId,
    int WarehouseCode,
    string WarehouseName,
    string LocationCode,
    string LocationName,
    int Priority,
    bool IsDefault,
    bool IsActive);

public sealed record QualityQuarantineDestinationRequest(
    long LocationId,
    int Priority,
    bool IsActive = true);

public sealed record QualityDecisionDestinationDto(
    long LocationId,
    long WarehouseId,
    int WarehouseCode,
    string WarehouseName,
    string LocationCode,
    string LocationName);

public sealed record QualityDatDocumentSeriesDto(
    long Id,
    string Code,
    string Name,
    string PreviewDocumentNumber,
    bool IsDefault);

public sealed record QualityWarehouseRouteDto(
    long Id,
    long SourceWarehouseId,
    int SourceWarehouseCode,
    string SourceWarehouseName,
    long? QualityLocationId,
    long? AcceptedLocationId,
    long? QuarantineLocationId,
    long? RejectLocationId,
    QualityDecisionDestinationDto? QualityLocation,
    QualityDecisionDestinationDto? AcceptedLocation,
    QualityDecisionDestinationDto? QuarantineLocation,
    QualityDecisionDestinationDto? RejectLocation,
    bool IsActive);

public sealed record QualityWarehouseRouteRequest(
    long SourceWarehouseId,
    long? QualityLocationId,
    long? AcceptedLocationId,
    long? QuarantineLocationId,
    long? RejectLocationId,
    bool IsActive = true);

public sealed record QualityParameterDto(long Id, string BranchCode, bool AutoCreateInspectionOnReceipt,
    QualityInspectionMode DefaultInspectionMode, QualityFailAction DefaultFailAction, bool HoldInventoryUntilDecision,
    bool BlockPutawayUntilDecision, bool BlockErpPostingUntilDecision, bool RequireManagerApprovalForRelease,
    bool AllowPartialDecision, bool AllowDirectReceiptWhenNoRule, bool BlockReceiptWhenLotMissing,
    bool BlockReceiptWhenSerialMissing, bool BlockReceiptWhenExpiryMissing, long? DefaultQualityLocationId,
    long? DefaultAcceptedLocationId,
    long? DefaultQuarantineLocationId, long? DefaultRejectLocationId,
    IReadOnlyList<QualityQuarantineDestinationDto> QuarantineDestinations,
    IReadOnlyList<QualityWarehouseRouteDto> WarehouseRoutes,
    long? UpdatedBy, DateTime? UpdatedDate);

public sealed record UpdateQualityParameterRequest(string BranchCode, bool AutoCreateInspectionOnReceipt,
    QualityInspectionMode DefaultInspectionMode, QualityFailAction DefaultFailAction, bool HoldInventoryUntilDecision,
    bool BlockPutawayUntilDecision, bool BlockErpPostingUntilDecision, bool RequireManagerApprovalForRelease,
    bool AllowPartialDecision, bool AllowDirectReceiptWhenNoRule, bool BlockReceiptWhenLotMissing,
    bool BlockReceiptWhenSerialMissing, bool BlockReceiptWhenExpiryMissing, long? DefaultQualityLocationId,
    long? DefaultAcceptedLocationId,
    long? DefaultQuarantineLocationId, long? DefaultRejectLocationId,
    IReadOnlyList<QualityQuarantineDestinationRequest>? QuarantineDestinations = null,
    IReadOnlyList<QualityWarehouseRouteRequest>? WarehouseRoutes = null);

public sealed record QualityRuleUpsertRequest(string BranchCode, string ScopeType, long? StockId, string? StockGroupCode,
    QualityInspectionMode InspectionMode, QualitySamplingMode SamplingMode, decimal SamplingValue,
    QualityFailAction FailAction, bool AutoQuarantine, bool RequireLot, bool RequireSerial,
    bool RequireExpiryDate, int? MinimumRemainingShelfLifeDays, bool IsActive, string? Description);

public sealed record QualityStockGroupOption(string Code, int StockCount);

public sealed record QualityRuleImportRowResult(
    int RowNumber, string Status, string ScopeType, string? ScopeCode, string Message);

public sealed record QualityRuleImportResult(
    int TotalRows, int CreatedCount, int SkippedCount, int FailedCount,
    IReadOnlyList<QualityRuleImportRowResult> Rows);

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
    public bool IsPriority { get; init; }
    public string Status { get; init; } = string.Empty; public int LineCount { get; init; } public decimal TotalQuantity { get; init; }
    public decimal RequiredInspectionQuantity { get; init; } public decimal InspectedQuantity { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; } public DateTimeOffset? QueuedAtUtc { get; init; } public DateTimeOffset? DecidedAtUtc { get; init; } public long? InspectorUserId { get; init; }
    public string WorkState { get; init; } = QualityInspectionWorkState.NotStarted.ToString();
    public long RecordedWorkSeconds { get; init; }
    public int WorkSessionCount { get; init; }
    public int ParticipantCount { get; init; }
    public long? ActiveWorkerUserId { get; init; }
    public string? ActiveWorkerName { get; init; }
    public DateTimeOffset? ActiveWorkStartedAtUtc { get; init; }
    public long? CreatedBy { get; init; } public DateTime? CreatedDate { get; init; } public long? UpdatedBy { get; init; } public DateTime? UpdatedDate { get; init; }
}

public sealed record ResolvedQualityPolicy(string Source, long? RuleId, QualityInspectionMode InspectionMode,
    QualitySamplingMode SamplingMode, decimal SamplingValue, QualityFailAction FailAction, bool AutoQuarantine,
    bool RequireLot, bool RequireSerial, bool RequireExpiryDate, int? MinimumRemainingShelfLifeDays,
    bool HoldInventoryUntilDecision, bool BlockPutawayUntilDecision, bool BlockErpPostingUntilDecision);

public sealed record QualityInspectionQuantityDecisionRequest(
    long LineId,
    decimal AcceptedQuantity,
    decimal RejectedQuantity,
    decimal QuarantineQuantity,
    long? QuarantineLocationId = null);

public sealed record QualityInspectionDispositionRequest(
    long LineId,
    QualityDecision Decision,
    decimal Quantity,
    long? TargetLocationId = null,
    string? ReasonCode = null,
    string? Note = null);

public sealed record QualityInspectionControlQuantityRequest(
    long LineId,
    decimal InspectedQuantity);

public sealed record DecideQualityInspectionRequest(Guid IdempotencyKey, QualityDecision Decision,
    string? ReasonCode, string? Note, IReadOnlyList<long>? LineIds, string? RowVersion,
    IReadOnlyList<QualityInspectionQuantityDecisionRequest>? QuantityDecisions = null,
    long? QuarantineLocationId = null,
    IReadOnlyList<QualityInspectionDispositionRequest>? Dispositions = null,
    long? WarehouseTransferDocumentSeriesId = null,
    IReadOnlyList<QualityInspectionControlQuantityRequest>? ControlQuantities = null);

public sealed record QualityDecisionResult(
    long GoodsReceiptId,
    string GoodsReceiptDocumentNo,
    WarehouseOperationStatus GoodsReceiptStatus,
    OperationQualityStatus QualityStatus,
    OperationApprovalStatus ApprovalStatus,
    ErpIntegrationStatus ErpIntegrationStatus,
    bool ErpDocumentCreatedNow,
    string Message);

public sealed record QualityInspectionLineDto(long Id, long? GoodsReceiptLineId, long StockId,
    string StockCode, string? StockName, string? YapCode, string? LotNo, string? SerialNo,
    DateOnly? ExpiryDate, decimal Quantity, decimal SampleQuantity, decimal InspectedQuantity, decimal AcceptedQuantity,
    decimal RejectedQuantity, decimal QuarantineQuantity, long? QuarantineLocationId, QualityDecision Decision,
    string? ReasonCode, string? ReasonNote, long? DecisionBy, DateTimeOffset? DecisionAtUtc,
    QualityDecisionDestinationDto? DefaultAcceptedDestination);

public sealed record QualityInspectionControlDto(
    long Id,
    long LineId,
    Guid IdempotencyKey,
    decimal LotQuantity,
    decimal RequiredQuantity,
    decimal InspectedQuantity,
    string OutcomeSummary,
    string? Note,
    long InspectedBy,
    DateTimeOffset InspectedAtUtc);

public sealed record QualityInspectionDispositionDto(
    long Id,
    long LineId,
    Guid IdempotencyKey,
    int SequenceNo,
    QualityDecision Decision,
    decimal Quantity,
    long SourceWarehouseId,
    long SourceLocationId,
    long TargetWarehouseId,
    long TargetLocationId,
    string SourceWarehouseCode,
    string SourceLocationCode,
    string TargetWarehouseCode,
    string TargetLocationCode,
    string SourceStockStatus,
    string TargetStockStatus,
    long? StockMovementOperationId,
    long? WarehouseTransferId,
    string? ReasonCode,
    string? ReasonNote,
    long DecisionBy,
    DateTimeOffset DecisionAtUtc);

public sealed record StartQualityInspectionWorkRequest(Guid IdempotencyKey, string? RowVersion);

public sealed record PauseQualityInspectionWorkRequest(
    Guid IdempotencyKey,
    QualityInspectionWorkStopReason Reason,
    string? Note,
    string? RowVersion);

public sealed record QualityInspectionWorkSessionDto(
    long Id,
    int SequenceNo,
    long WorkerUserId,
    string WorkerName,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? EndedAtUtc,
    long DurationSeconds,
    QualityInspectionWorkStopReason? StopReason,
    string? StopNote,
    long? EndedByUserId);

public sealed record QualityInspectionWorkSummaryDto(
    QualityInspectionWorkState State,
    DateTimeOffset ServerNowUtc,
    long TotalWorkedSeconds,
    long CurrentUserWorkedSeconds,
    int SessionCount,
    int ParticipantCount,
    long? ActiveWorkerUserId,
    string? ActiveWorkerName,
    DateTimeOffset? ActiveStartedAtUtc,
    bool CanStart,
    bool CanPause,
    bool CanApplyDecision);

public sealed record QualityInspectionDetail(QualityInspectionGridRow Header,
    IReadOnlyList<QualityInspectionLineDto> Lines, string? Note, byte[] RowVersion,
    bool AllowPartialDecision, bool RequireManagerApprovalForRelease,
    WarehouseOperationStatus? SourceOperationStatus,
    bool CanDecideInventoryDisposition,
    IReadOnlyList<QualityQuarantineDestinationDto> QuarantineDestinations,
    QualityDecisionDestinationDto? DefaultAcceptedDestination,
    QualityDecisionDestinationDto? DefaultRejectedDestination,
    IReadOnlyList<QualityDatDocumentSeriesDto> WarehouseTransferDocumentSeries,
    IReadOnlyList<QualityInspectionDispositionDto> Dispositions,
    IReadOnlyList<QualityInspectionControlDto> Controls,
    QualityInspectionWorkSummaryDto Work,
    IReadOnlyList<QualityInspectionWorkSessionDto> WorkSessions);

public sealed record QualityInspectionPriorityResult(long InspectionId, bool IsPriority);

public sealed record QualityInspectionStatusOptionDto(
    string Value,
    bool IsDefault,
    bool IsTerminal,
    bool CanPrioritize);

public sealed record QualityInspectionStatusCatalogDto(
    string DefaultValue,
    IReadOnlyList<QualityInspectionStatusOptionDto> Items);

public interface IQualityService
{
    Task<QualityParameterDto> GetParametersAsync(string branchCode, CancellationToken ct = default);
    Task<QualityParameterDto> UpdateParametersAsync(UpdateQualityParameterRequest request, long actor, CancellationToken ct = default);
    Task<PagedResponse<QualityRuleGridRow>> GetRulesPagedAsync(PagedRequest request, CancellationToken ct = default);
    Task<PagedResponse<QualityStockGroupOption>> GetStockGroupsPagedAsync(string branchCode, PagedRequest request, CancellationToken ct = default);
    Task<long> CreateRuleAsync(QualityRuleUpsertRequest request, long actor, CancellationToken ct = default);
    Task UpdateRuleAsync(long id, QualityRuleUpsertRequest request, long actor, CancellationToken ct = default);
    Task DeleteRuleAsync(long id, long actor, CancellationToken ct = default);
    QualityInspectionStatusCatalogDto GetInspectionStatusCatalog();
    Task<PagedResponse<QualityInspectionGridRow>> GetInspectionsPagedAsync(PagedRequest request, CancellationToken ct = default);
    Task<QualityInspectionDetail> GetInspectionAsync(long id, long actor, bool canExecute, bool canSupervise,
        bool canDecide, CancellationToken ct = default);
    Task<QualityInspectionWorkSummaryDto> StartInspectionWorkAsync(long id, StartQualityInspectionWorkRequest request,
        long actor, bool canExecute, bool canSupervise, bool canDecide, CancellationToken ct = default);
    Task<QualityInspectionWorkSummaryDto> PauseInspectionWorkAsync(long id, PauseQualityInspectionWorkRequest request,
        long actor, bool canExecute, bool canSupervise, bool canDecide, CancellationToken ct = default);
    Task<QualityInspectionPriorityResult> ToggleInspectionPriorityAsync(long id, long actor, CancellationToken ct = default);
    Task<QualityDecisionResult> DecideInspectionAsync(long id, DecideQualityInspectionRequest request, long actor,
        bool canReleaseQuarantine, CancellationToken ct = default);
}

public interface IQualityRuleImportService
{
    Task<byte[]> CreateTemplateAsync(string branchCode, CancellationToken ct = default);
    Task<QualityRuleImportResult> ImportAsync(Stream workbookStream, string branchCode, long actor, CancellationToken ct = default);
}

public interface IQualityPolicyResolver
{
    Task<ResolvedQualityPolicy> ResolveAsync(string branchCode, long stockId, string? stockGroupCode, CancellationToken ct = default);
}

public sealed record ResolvedQualityWarehouseRoute(
    long? QualityLocationId,
    long? AcceptedLocationId,
    long? QuarantineLocationId,
    long? RejectLocationId);

public interface IQualityWarehouseRoutingResolver
{
    Task<ResolvedQualityWarehouseRoute> ResolveWarehouseRouteAsync(
        string branchCode,
        long sourceWarehouseId,
        CancellationToken ct = default);
}
