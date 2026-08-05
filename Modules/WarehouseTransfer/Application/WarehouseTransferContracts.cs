using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using verii_wms_api_v2.Modules.WarehouseTransfer.Domain;
using verii_wms_api_v2.Shared;

namespace verii_wms_api_v2.Modules.WarehouseTransfer.Application;

public sealed record WarehouseTransferTrackingDraftRequest(
    decimal Quantity,
    string? HandlingUnitNo,
    string? LotNo,
    string? SerialNo,
    DateOnly? ManufacturingDate,
    DateOnly? ExpirationDate,
    long? SourceLocationId,
    long? TargetLocationId);

public sealed record WarehouseTransferLineSourceDraftRequest(
    string OrderNumber,
    string ExternalLineId,
    int? ExternalLineNo,
    string ExternalStockCode,
    string? ExternalYapCode,
    DateOnly? OrderDate,
    decimal OrderedQuantity,
    decimal PreviouslyTransferredQuantity,
    decimal AvailableQuantity,
    string? ExternalStatus);

public sealed record WarehouseTransferLineDraftRequest(
    long StockId,
    long? YapCodeId,
    decimal Quantity,
    string? UnitCode,
    StockTrackingType TrackingType,
    bool RequireHandlingUnit,
    long? DefaultSourceLocationId,
    long? DefaultTargetLocationId,
    string? Description,
    IReadOnlyList<WarehouseTransferTrackingDraftRequest>? Trackings,
    WarehouseTransferLineSourceDraftRequest? Source,
    string? SourceStockStatus = null,
    string? TargetStockStatus = null);

public sealed record CreateWarehouseTransferDraftRequest(
    Guid IdempotencyKey,
    string BranchCode,
    long DocumentSeriesId,
    DateOnly DocumentDate,
    WarehouseTransferInitiationMode InitiationMode,
    WarehouseTransferProcessType ProcessType,
    long SourceWarehouseId,
    long TargetWarehouseId,
    long? SourceStagingLocationId,
    long? TargetReceivingLocationId,
    long? TargetPutawayLocationId,
    DateTimeOffset? PlannedDispatchAtUtc,
    DateTimeOffset? PlannedArrivalAtUtc,
    byte Priority,
    string? ExternalReferenceNo,
    string? Description,
    IReadOnlyList<WarehouseTransferLineDraftRequest> Lines,
    IReadOnlyList<long>? AssignedUserIds,
    WarehouseTransferBusinessContext BusinessContext = WarehouseTransferBusinessContext.InterWarehouse,
    string? ProjectCode = null);

public sealed record CreateWarehouseTransferDraftResult(long Id,string DocumentNo,int LineCount,decimal RequestedQuantity,bool Replayed,long? TaskId,string? TaskNo);
public sealed record UpdateWarehouseTransferDraftRequest(string RowVersion,DateOnly DocumentDate,long? SourceStagingLocationId,
    long? TargetReceivingLocationId,long? TargetPutawayLocationId,DateTimeOffset? PlannedDispatchAtUtc,
    DateTimeOffset? PlannedArrivalAtUtc,byte Priority,string? ExternalReferenceNo,string? Description,string? ProjectCode = null);

public sealed record WarehouseTransferGridRow(
    long Id,string BranchCode,string DocumentNo,DateOnly DocumentDate,
    WarehouseTransferBusinessContext BusinessContext,
    WarehouseTransferInitiationMode InitiationMode,WarehouseTransferProcessType ProcessType,WarehouseTransferStatus Status,
    OperationApprovalStatus ApprovalStatus,ErpIntegrationStatus ErpIntegrationStatus,
    long SourceWarehouseId,int SourceWarehouseCode,string SourceWarehouseName,
    long TargetWarehouseId,int TargetWarehouseCode,string TargetWarehouseName,
    int LineCount,decimal RequestedQuantity,decimal PickedQuantity,decimal ShippedQuantity,decimal ReceivedQuantity,decimal PutawayQuantity,
    byte Priority,DateTimeOffset? PlannedDispatchAtUtc,DateTimeOffset? PlannedArrivalAtUtc,
    long? CreatedBy,DateTime? CreatedDate,long? UpdatedBy,DateTime? UpdatedDate);

public sealed record WarehouseTransferTrackingLineDto(
    long Id,string? HandlingUnitNo,string? LotNo,string? SerialNo,DateOnly? ManufacturingDate,DateOnly? ExpirationDate,
    decimal PlannedQuantity,decimal PickedQuantity,decimal ShippedQuantity,decimal ReceivedQuantity,decimal PutawayQuantity,
    WarehouseTransferTrackingStatus Status);

public sealed record WarehouseTransferDetailLine(
    long Id,int LineNo,long StockId,string StockCode,string? StockName,long? YapCodeId,string? YapCode,
    string UnitCode,decimal RequestedQuantity,decimal ReservedQuantity,decimal PickedQuantity,decimal ShippedQuantity,
    decimal ReceivedQuantity,decimal PutawayQuantity,decimal DamagedQuantity,decimal LostQuantity,
    StockTrackingType TrackingType,WarehouseTransferLineStatus Status,int TrackingCount,
    IReadOnlyList<WarehouseTransferTrackingLineDto> Trackings,
    long? DefaultSourceLocationId,string? DefaultSourceLocationCode,string? DefaultSourceLocationName,
    long? DefaultTargetLocationId,string? DefaultTargetLocationCode,string? DefaultTargetLocationName);

public sealed record WarehouseTransferDraftMetadata(long? SourceStagingLocationId,long? TargetReceivingLocationId,long? TargetPutawayLocationId,
    string? ExternalReferenceNo,string? Description,string? ProjectCode);
public sealed record WarehouseTransferDetail(WarehouseTransferGridRow Header,IReadOnlyList<WarehouseTransferDetailLine> Lines,string RowVersion,
    WarehouseTransferDraftMetadata Draft);

public interface IWarehouseTransferService
{
    Task<CreateWarehouseTransferDraftResult> CreateDraftAsync(CreateWarehouseTransferDraftRequest request,long actorUserId,CancellationToken cancellationToken=default);
    Task<PagedResponse<WarehouseTransferGridRow>> GetPagedAsync(PagedRequest request,CancellationToken cancellationToken=default);
    Task<PagedResponse<WarehouseTransferGridRow>> GetPagedByContextAsync(PagedRequest request,IReadOnlyCollection<WarehouseTransferBusinessContext> contexts,CancellationToken cancellationToken=default);
    Task<WarehouseTransferDetail> GetDetailAsync(long id,CancellationToken cancellationToken=default);
    Task<WarehouseTransferDetail> GetDetailForContextAsync(long id,IReadOnlyCollection<WarehouseTransferBusinessContext> contexts,CancellationToken cancellationToken=default);
    Task EnsureContextAsync(long id,IReadOnlyCollection<WarehouseTransferBusinessContext> contexts,CancellationToken cancellationToken=default);
    Task<WarehouseTransferDetail> UpdateDraftAsync(long id,UpdateWarehouseTransferDraftRequest request,long actorUserId,CancellationToken cancellationToken=default);
    Task DeleteDraftAsync(long id,long actorUserId,CancellationToken cancellationToken=default);
}
