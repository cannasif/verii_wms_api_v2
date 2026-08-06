using verii_wms_api_v2.Modules.ProductionTransfer.Domain;

namespace verii_wms_api_v2.Modules.ProductionTransfer.Application;

public sealed record ProductionTransferScanPickRequest(
    Guid IdempotencyKey,
    long ExpectedLineId,
    string Barcode,
    long? SourceLocationId = null);

public sealed record CompleteProductionPickingRequest(
    Guid IdempotencyKey,
    bool ConfirmPartialPicking,
    string? Reason);

public sealed record ConfirmProductionHandoverRequest(
    Guid IdempotencyKey,
    bool ConfirmShortage,
    string? ShortageReason);

public sealed record ProductionTransferExecutionLineDto(
    long LineId,
    int LineNo,
    long StockId,
    string StockCode,
    string? StockName,
    string UnitCode,
    decimal RequestedQuantity,
    decimal PickedQuantity,
    decimal HandedOverQuantity,
    decimal RemainingToPickQuantity,
    decimal ShortageQuantity,
    string TrackingType,
    long? SuggestedSourceLocationId,
    string? SuggestedSourceLocationCode,
    string? SuggestedSourceLocationName);

public sealed record ProductionTransferExecutionDto(
    long TransferId,
    string DocumentNo,
    ProductionTransferWorkflowStatus WorkflowStatus,
    string TransferStatus,
    long SourceWarehouseId,
    int SourceWarehouseCode,
    string SourceWarehouseName,
    long TargetWarehouseId,
    int TargetWarehouseCode,
    string TargetWarehouseName,
    long? WaitingLocationId,
    string? WaitingLocationCode,
    string? WaitingLocationName,
    long? RequestedByUserId,
    string? RequestedByName,
    long? HandoverConfirmedBy,
    DateTimeOffset? HandoverConfirmedAtUtc,
    string? HandoverShortageReason,
    long? ParentTransferId,
    long? ResidualTransferId,
    string? ResidualDocumentNo,
    decimal RequestedQuantity,
    decimal PickedQuantity,
    decimal HandedOverQuantity,
    decimal ShortageQuantity,
    bool CanCompletePicking,
    bool CanConfirmHandover,
    IReadOnlyList<ProductionTransferExecutionLineDto> Lines);

public sealed record ProductionTransferScanPickResult(
    ProductionTransferExecutionDto Execution,
    long LineId,
    string StockCode,
    decimal AcceptedQuantity,
    string? SerialNo,
    string? LotNo);

public interface IProductionTransferExecutionService
{
    Task<ProductionTransferExecutionDto> GetAsync(long transferId, CancellationToken ct = default);
    Task<ProductionTransferScanPickResult> ScanPickAsync(long transferId, ProductionTransferScanPickRequest request, long actor, CancellationToken ct = default);
    Task<ProductionTransferExecutionDto> CompletePickingAsync(long transferId, CompleteProductionPickingRequest request, long actor, CancellationToken ct = default);
    Task<ProductionTransferExecutionDto> ConfirmHandoverAsync(long transferId, ConfirmProductionHandoverRequest request, long actor, bool canOverrideRequester, CancellationToken ct = default);
}
