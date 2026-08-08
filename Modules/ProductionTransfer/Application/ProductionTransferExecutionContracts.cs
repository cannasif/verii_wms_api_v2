using verii_wms_api_v2.Modules.ErpIntegration.Domain;
using verii_wms_api_v2.Modules.ProductionTransfer.Domain;
using verii_wms_api_v2.Modules.WarehouseOperations.Domain;

namespace verii_wms_api_v2.Modules.ProductionTransfer.Application;

public sealed record ProductionTransferScanPickRequest(
    Guid IdempotencyKey,
    long ExpectedTaskLineId,
    string Barcode,
    decimal? Quantity = null,
    long? SourceLocationId = null);

public sealed record ResolveProductionTransferBarcodeRequest(string Barcode);

public sealed record ResolveProductionTransferBarcodeResult(
    long TaskLineId,
    long WtLineId,
    long? SourceLocationId,
    string? SourceLocationCode,
    long StockId,
    string StockCode,
    string? StockName,
    string? SerialNo,
    string? LotNo,
    decimal RemainingQuantity,
    decimal DefaultQuantity,
    bool IsSerial,
    bool CanPick);

public sealed record ProductionTransferPickingRowDto(
    long TaskLineId,
    long WtLineId,
    int LineNo,
    long? SourceLocationId,
    string? SourceLocationCode,
    long StockId,
    string StockCode,
    string? StockName,
    string? SerialNo,
    decimal RequestedQuantity,
    decimal RemainingQuantity,
    decimal ProcessedQuantity,
    bool CanPick);

public sealed record ProductionTransferPickingTableDto(
    long TransferId,
    string DocumentNo,
    string? ExternalReferenceNo,
    ProductionTransferWorkflowStatus WorkflowStatus,
    long PickTaskId,
    string PickTaskNo,
    bool IsLocked,
    bool CanCompletePicking,
    decimal RequestedQuantity,
    decimal PickedQuantity,
    decimal ShortageQuantity,
    IReadOnlyList<ProductionTransferPickingRowDto> Rows);

public sealed record ProductionTransferRouteRefreshCandidateDto(
    long LocationId,
    string LocationCode,
    decimal AvailableQuantity,
    decimal SuggestedQuantity,
    string? SerialNo = null);

public sealed record ProductionTransferRouteRefreshCandidatesDto(
    long TaskLineId,
    decimal RemainingQuantity,
    bool IsSerial,
    string? CurrentSerialNo,
    IReadOnlyList<ProductionTransferRouteRefreshCandidateDto> Candidates);

public sealed record ProductionTransferRouteRefreshSplitLineRequest(long LocationId, decimal Quantity, string? SerialNo = null);

public sealed record ApplyProductionTransferRouteRefreshSplitRequest(
    Guid IdempotencyKey,
    string? CurrentSerialNo,
    IReadOnlyList<ProductionTransferRouteRefreshSplitLineRequest> Splits);

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
    ProductionTransferErpPostingPolicy ErpPostingPolicy,
    ErpIntegrationStatus ErpIntegrationStatus,
    ErpPostingStatus? ErpPostingStatus,
    string? ErpDocumentNo,
    string? ErpErrorCode,
    string? ErpErrorMessage,
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
    IReadOnlyList<long> ExcludedSourceLocationIds,
    IReadOnlyList<ProductionTransferExecutionLineDto> Lines);

public sealed record ProductionTransferScanPickResult(
    ProductionTransferExecutionDto Execution,
    long LineId,
    string StockCode,
    decimal AcceptedQuantity,
    string? SerialNo,
    string? LotNo,
    string BarcodeSource,
    long SourceLocationId,
    string SourceLocationCode,
    string SourceLocationName,
    decimal? RemainingBarcodeQuantity);

public interface IProductionTransferExecutionService
{
    Task<ProductionTransferExecutionDto> GetAsync(long transferId, CancellationToken ct = default);
    Task<ProductionTransferPickingTableDto> GetPickingTableAsync(long transferId, long actor, CancellationToken ct = default);
    Task<ResolveProductionTransferBarcodeResult> ResolveBarcodeAsync(long transferId, ResolveProductionTransferBarcodeRequest request, long actor, CancellationToken ct = default);
    Task<ProductionTransferScanPickResult> ScanPickAsync(long transferId, ProductionTransferScanPickRequest request, long actor, CancellationToken ct = default);
    Task<ProductionTransferRouteRefreshCandidatesDto> GetRouteRefreshCandidatesAsync(
        long transferId,
        long taskLineId,
        string? currentSerialNo,
        long actor,
        CancellationToken ct = default);
    Task<ProductionTransferPickingTableDto> ApplyRouteRefreshSplitAsync(
        long transferId,
        long taskLineId,
        ApplyProductionTransferRouteRefreshSplitRequest request,
        long actor,
        CancellationToken ct = default);
    Task<ProductionTransferExecutionDto> CompletePickingAsync(long transferId, CompleteProductionPickingRequest request, long actor, CancellationToken ct = default);
    Task<ProductionTransferExecutionDto> ConfirmHandoverAsync(long transferId, ConfirmProductionHandoverRequest request, long actor, bool canOverrideRequester, CancellationToken ct = default);
}
