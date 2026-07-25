using verii_wms_api_v2.Modules.WarehouseInbound.Domain;
using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using verii_wms_api_v2.Shared;

namespace verii_wms_api_v2.Modules.WarehouseInbound.Application;

public sealed record ManualWarehouseInboundLineRequest(
    long StockId, long? YapCodeId, decimal Quantity, string? UnitCode,
    string? LotNo, string? SerialNo, DateOnly? ManufacturingDate, DateOnly? ExpirationDate,
    string? ScannedBarcode, long? WarehouseInboundLabelId, string? Description,
    long? TargetWarehouseId, long? ReceivingLocationId);

public sealed record CreateManualWarehouseInboundRequest(
    Guid IdempotencyKey, string BranchCode, long DocumentSeriesId, long SupplierId,
    long TargetWarehouseId, long ReceivingLocationId, DateOnly DocumentDate,
    string? WaybillNo, DateOnly? WaybillDate, string? ElectronicWaybillNo,
    string? ShipmentReferenceNo, string? CarrierCode, string? CarrierName,
    string? VehiclePlate, string? TrailerPlate, string? DriverName, string? SealNo,
    DateTimeOffset? PlannedArrivalAtUtc, DateTimeOffset? OccurredAtUtc,
    WarehouseInboundLabelStrategy LabelStrategy, WarehouseInboundExecutionMode ExecutionMode,
    byte Priority, string? DeviceId, string? Description,
    IReadOnlyList<long>? AssignedUserIds, IReadOnlyList<ManualWarehouseInboundLineRequest> Lines);

public sealed record ManualWarehouseInboundResult(
    long Id, string DocumentNo, WarehouseInboundInitiationMode InitiationMode,
    WarehouseOperationStatus Status, long? TaskId, string? TaskNo,
    long? ExecutionId, long? StockMovementOperationId, long? QualityInspectionId,
    int LineCount, decimal Quantity, bool Replayed);

public sealed record WarehouseInboundGridRow(
    long Id, string BranchCode, string DocumentNo, DateOnly DocumentDate,
    WarehouseInboundType ReceiptType, WarehouseInboundInitiationMode InitiationMode, WarehouseInboundProcessType ProcessType,
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

public sealed record WarehouseInboundDetailLine(
    long Id, int LineNo, long StockId, string StockCode, string? StockName,
    long? YapCodeId, string? YapCode, string UnitCode, decimal ExpectedQuantity,
    decimal ReceivedQuantity, decimal AcceptedQuantity, decimal RejectedQuantity,
    decimal QuarantineQuantity, decimal ShortClosedQuantity, decimal PutawayQuantity, WarehouseInboundLineStatus Status,
    bool RequireQualityControl, long TargetWarehouseId,
    long? DefaultReceivingLocationId, long? DefaultPutawayLocationId);

public sealed record WarehouseInboundPutawayCandidate(
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

public sealed record WarehouseInboundDetail(
    WarehouseInboundGridRow Header,
    IReadOnlyList<WarehouseInboundDetailLine> Lines,
    IReadOnlyList<WarehouseInboundPutawayCandidate> PutawayCandidates,
    IReadOnlyList<string> SourceDocuments,
    IReadOnlyList<string> TaskNumbers,
    int ExecutionCount);

public interface IWarehouseInboundOperationsService
{
    Task<ManualWarehouseInboundResult> CreateOrderlessTaskAsync(CreateManualWarehouseInboundRequest request, long actorUserId, CancellationToken cancellationToken = default);
    Task<ManualWarehouseInboundResult> CreateDirectReceiptAsync(CreateManualWarehouseInboundRequest request, long actorUserId, CancellationToken cancellationToken = default);
    Task<PagedResponse<WarehouseInboundGridRow>> GetPagedAsync(PagedRequest request, CancellationToken cancellationToken = default);
    Task<WarehouseInboundDetail> GetDetailAsync(long id, CancellationToken cancellationToken = default);
}
