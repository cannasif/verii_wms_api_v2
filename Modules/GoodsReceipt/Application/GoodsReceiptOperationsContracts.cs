using verii_wms_api_v2.Modules.GoodsReceipt.Domain;
using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using verii_wms_api_v2.Shared;

namespace verii_wms_api_v2.Modules.GoodsReceipt.Application;

public sealed record ManualGoodsReceiptLineRequest(
    long StockId, long? YapCodeId, decimal Quantity, string? UnitCode,
    string? LotNo, string? SerialNo, DateOnly? ManufacturingDate, DateOnly? ExpirationDate,
    string? ScannedBarcode, long? GoodsReceiptLabelId, string? Description,
    long? TargetWarehouseId, long? ReceivingLocationId);

public sealed record CreateManualGoodsReceiptRequest(
    Guid IdempotencyKey, string BranchCode, long DocumentSeriesId, long SupplierId,
    long TargetWarehouseId, long ReceivingLocationId, DateOnly DocumentDate,
    string? WaybillNo, DateOnly? WaybillDate, string? ElectronicWaybillNo,
    string? ShipmentReferenceNo, string? CarrierCode, string? CarrierName,
    string? VehiclePlate, string? TrailerPlate, string? DriverName, string? SealNo,
    DateTimeOffset? PlannedArrivalAtUtc, DateTimeOffset? OccurredAtUtc,
    GoodsReceiptLabelStrategy LabelStrategy, GoodsReceiptExecutionMode ExecutionMode,
    byte Priority, string? DeviceId, string? Description,
    IReadOnlyList<long>? AssignedUserIds, IReadOnlyList<ManualGoodsReceiptLineRequest> Lines);

public sealed record ManualGoodsReceiptResult(
    long Id, string DocumentNo, GoodsReceiptInitiationMode InitiationMode,
    WarehouseOperationStatus Status, long? TaskId, string? TaskNo,
    long? ExecutionId, long? StockMovementOperationId, long? QualityInspectionId,
    int LineCount, decimal Quantity, bool Replayed, IReadOnlyList<long> GeneratedLabelIds);

public sealed record GoodsReceiptGridRow(
    long Id, string BranchCode, string DocumentNo, DateOnly DocumentDate,
    GoodsReceiptType ReceiptType, GoodsReceiptInitiationMode InitiationMode, GoodsReceiptProcessType ProcessType,
    WarehouseOperationStatus Status, OperationApprovalStatus ApprovalStatus,
    OperationQualityStatus QualityStatus, OperationPutawayStatus PutawayStatus,
    ErpIntegrationStatus ErpIntegrationStatus,
    long? SupplierId, string? SupplierCode, string? SupplierName,
    long TargetWarehouseId, int WarehouseCode, string WarehouseName,
    string? WaybillNo, DateOnly? WaybillDate, int LineCount,
    decimal ExpectedQuantity, decimal ReceivedQuantity, byte Priority,
    DateTimeOffset? PlannedArrivalAtUtc, DateTimeOffset? ReceivedAtUtc,
    long? CreatedBy, DateTime? CreatedDate, long? UpdatedBy, DateTime? UpdatedDate,
    byte[] RowVersion);

public sealed record GoodsReceiptDetailLine(
    long Id, int LineNo, long StockId, string StockCode, string? StockName,
    long? YapCodeId, string? YapCode, string UnitCode, decimal ExpectedQuantity,
    decimal ReceivedQuantity, decimal AcceptedQuantity, decimal RejectedQuantity,
    decimal QuarantineQuantity, decimal ShortClosedQuantity, decimal PutawayQuantity, GoodsReceiptLineStatus Status,
    bool RequireQualityControl, long TargetWarehouseId,
    long? DefaultReceivingLocationId, long? DefaultPutawayLocationId,
    decimal RoutedQuantity, decimal RoutableQuantity);

public sealed record GoodsReceiptPutawayCandidate(
    long LineId,
    int LineNo,
    long StockId,
    string StockCode,
    string? StockName,
    long? YapCodeId,
    string? YapCode,
    string UnitCode,
    decimal Quantity,
    long WarehouseId,
    long SourceLocationId,
    string? LotNo,
    string? SerialNo,
    string StockStatus,
    long? DefaultTargetLocationId);

public sealed record GoodsReceiptDetail(
    GoodsReceiptGridRow Header,
    IReadOnlyList<GoodsReceiptDetailLine> Lines,
    IReadOnlyList<GoodsReceiptPutawayCandidate> PutawayCandidates,
    IReadOnlyList<string> SourceDocuments,
    IReadOnlyList<string> TaskNumbers,
    int ExecutionCount);

public interface IGoodsReceiptOperationsService
{
    Task<ManualGoodsReceiptResult> CreateOrderlessTaskAsync(CreateManualGoodsReceiptRequest request, long actorUserId, CancellationToken cancellationToken = default);
    Task<ManualGoodsReceiptResult> CreateDirectReceiptAsync(CreateManualGoodsReceiptRequest request, long actorUserId, CancellationToken cancellationToken = default);
    Task<ManualGoodsReceiptResult> CreateDirectReceiptDeferredErpAsync(
        CreateManualGoodsReceiptRequest request, long actorUserId,
        bool qualityAlreadyApproved, CancellationToken cancellationToken = default);
    Task<PagedResponse<GoodsReceiptGridRow>> GetPagedAsync(PagedRequest request, CancellationToken cancellationToken = default);
    Task<GoodsReceiptDetail> GetDetailAsync(long id, CancellationToken cancellationToken = default);
}
