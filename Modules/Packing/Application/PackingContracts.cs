using verii_wms_api_v2.Modules.Packing.Domain;
using verii_wms_api_v2.Shared;

namespace verii_wms_api_v2.Modules.Packing.Application;

public sealed record PackagingMaterialRequest(string BranchCode,string Code,string Name,PackagingMaterialType Type,decimal TareWeight,decimal? MaxNetWeight,decimal? MaxGrossWeight,decimal? InnerLength,decimal? InnerWidth,decimal? InnerHeight,decimal? MaxVolume,bool IsReturnable,bool IsActive,string? Description);
public sealed record PackagingMaterialRow(long Id,string BranchCode,string Code,string Name,PackagingMaterialType Type,decimal TareWeight,decimal? MaxNetWeight,decimal? MaxGrossWeight,decimal? InnerLength,decimal? InnerWidth,decimal? InnerHeight,decimal? MaxVolume,bool IsReturnable,bool IsActive,string? Description,long? CreatedBy,DateTime? CreatedDate,long? UpdatedBy,DateTime? UpdatedDate);
public sealed record PackingStationRequest(string BranchCode,long WarehouseId,long? LocationId,string Code,string Name,string? ScaleDeviceCode,long? PrinterDefinitionId,bool IsActive,string? Description);
public sealed record PackingStationRow(long Id,string BranchCode,long WarehouseId,long? LocationId,string Code,string Name,string? ScaleDeviceCode,long? PrinterDefinitionId,bool IsActive,string? Description,long? CreatedBy,DateTime? CreatedDate,long? UpdatedBy,DateTime? UpdatedDate);
public sealed record PackagingSpecificationRequest(string BranchCode,long? StockId,string? StockGroupCode,long? CustomerId,long PackagingMaterialId,decimal? UnitsPerHandlingUnit,decimal? MaxNetWeight,decimal? MaxVolume,int Priority,bool IsActive,string? Notes);
public sealed record PackagingSpecificationRow(long Id,string BranchCode,long? StockId,string? StockCode,string? StockName,string? StockGroupCode,long? CustomerId,string? CustomerCode,string? CustomerName,long PackagingMaterialId,string PackagingMaterialCode,string PackagingMaterialName,decimal? UnitsPerHandlingUnit,decimal? MaxNetWeight,decimal? MaxVolume,int Priority,bool IsActive,string? Notes,long? CreatedBy,DateTime? CreatedDate,long? UpdatedBy,DateTime? UpdatedDate);
public sealed record PackingPolicyDto(long Id,string BranchCode,bool RequirePacking,bool AllowPartialPacking,bool AllowMixedStock,bool AllowMixedLot,bool AllowMixedCustomer,bool RequireSerialLotScan,bool RequireWeight,decimal WeightTolerancePercent,bool RequireDimensions,bool RequireSscc,bool AutoGenerateSscc,bool AutoPrintLabelOnClose,bool AllowReopen,bool AllowRepack,PackingClosePolicy ClosePolicy,PackingReleasePolicy ReleasePolicy,string RowVersion);
public sealed record UpdatePackingPolicyRequest(string BranchCode,bool RequirePacking,bool AllowPartialPacking,bool AllowMixedStock,bool AllowMixedLot,bool AllowMixedCustomer,bool RequireSerialLotScan,bool RequireWeight,decimal WeightTolerancePercent,bool RequireDimensions,bool RequireSscc,bool AutoGenerateSscc,bool AutoPrintLabelOnClose,bool AllowReopen,bool AllowRepack,PackingClosePolicy ClosePolicy,PackingReleasePolicy ReleasePolicy,string? RowVersion);
public sealed record CreatePackingSessionRequest(Guid IdempotencyKey,string BranchCode,PackingSourceType SourceType,long? SourceHeaderId,long WarehouseId,long PackingStationId,string? Notes);
public sealed record CreateHandlingUnitRequest(Guid IdempotencyKey,long PackagingMaterialId,long? ParentHandlingUnitId,string? HandlingUnitNo,string? Sscc,decimal? Length,decimal? Width,decimal? Height);
public sealed record PackHandlingUnitLineRequest(Guid IdempotencyKey,long SourceLineId,decimal Quantity,string? LotNo,string? SerialNo);
public sealed record CloseHandlingUnitRequest(Guid IdempotencyKey,decimal? MeasuredGrossWeight,string? Reason);
public sealed record UnpackHandlingUnitLineRequest(Guid IdempotencyKey,long HandlingUnitLineId,decimal Quantity,string? Reason);
public sealed record MoveHandlingUnitLineRequest(Guid IdempotencyKey,long HandlingUnitLineId,long TargetHandlingUnitId,decimal Quantity,string? Reason);
public sealed record PrintHandlingUnitRequest(Guid IdempotencyKey,int Copies=1);
public sealed record ScaleReadingRequest(Guid IdempotencyKey);
public sealed record ScaleReadingDto(long Id,long PackingStationId,long? HandlingUnitId,string DeviceCode,decimal GrossWeight,bool IsStable,DateTimeOffset CapturedAtUtc);
public sealed record PackingPrintJobRow(long Id,long HandlingUnitId,long PackingStationId,long? PrinterDefinitionId,PackingPrintJobStatus Status,int Copies,int AttemptCount,DateTimeOffset RequestedAtUtc,DateTimeOffset? CompletedAtUtc,string? LastError);
public sealed record PackingSessionRow(long Id,string BranchCode,string PackingNo,PackingSourceType SourceType,long? SourceHeaderId,string? SourceDocumentNo,long WarehouseId,long PackingStationId,long? CustomerId,string? CustomerCode,PackingSessionStatus Status,int HandlingUnitCount,decimal TotalQuantity,decimal TotalGrossWeight,DateTimeOffset OpenedAtUtc,DateTimeOffset? ClosedAtUtc,DateTimeOffset? ReleasedAtUtc,long? CreatedBy,DateTime? CreatedDate,long? UpdatedBy,DateTime? UpdatedDate);
public sealed record HandlingUnitLineDto(long Id,long SourceLineId,long StockId,string StockCode,string? YapCode,string UnitCode,decimal Quantity,string? LotNo,string? SerialNo,DateTimeOffset PackedAtUtc,long PackedBy);
public sealed record HandlingUnitDto(long Id,long? ParentHandlingUnitId,long PackagingMaterialId,string HandlingUnitNo,string? Sscc,HandlingUnitStatus Status,decimal TareWeight,decimal NetWeight,decimal? MeasuredGrossWeight,decimal GrossWeight,decimal? Length,decimal? Width,decimal? Height,decimal? Volume,string RowVersion,IReadOnlyList<HandlingUnitLineDto> Lines);
public sealed record PackingSessionDetail(PackingSessionRow Header,string RowVersion,IReadOnlyList<HandlingUnitDto> HandlingUnits);
public sealed record PackingSourceLineOption(long Id,int LineNo,string StockCode,string? StockName,string? YapCode,string UnitCode,decimal PickedQuantity,decimal PackedQuantity,decimal RemainingQuantity,string TrackingType);

public interface IPackingService
{
    Task<PagedResponse<PackagingMaterialRow>> GetMaterialsAsync(PagedRequest request,CancellationToken ct=default);
    Task<long> CreateMaterialAsync(PackagingMaterialRequest request,long actor,CancellationToken ct=default);
    Task UpdateMaterialAsync(long id,PackagingMaterialRequest request,long actor,CancellationToken ct=default);
    Task DeleteMaterialAsync(long id,long actor,CancellationToken ct=default);
    Task<PagedResponse<PackingStationRow>> GetStationsAsync(PagedRequest request,CancellationToken ct=default);
    Task<long> CreateStationAsync(PackingStationRequest request,long actor,CancellationToken ct=default);
    Task UpdateStationAsync(long id,PackingStationRequest request,long actor,CancellationToken ct=default);
    Task DeleteStationAsync(long id,long actor,CancellationToken ct=default);
    Task<PagedResponse<PackagingSpecificationRow>> GetSpecificationsAsync(PagedRequest request,CancellationToken ct=default);
    Task<long> CreateSpecificationAsync(PackagingSpecificationRequest request,long actor,CancellationToken ct=default);
    Task UpdateSpecificationAsync(long id,PackagingSpecificationRequest request,long actor,CancellationToken ct=default);
    Task DeleteSpecificationAsync(long id,long actor,CancellationToken ct=default);
    Task<PackingPolicyDto> GetPolicyAsync(string branchCode,CancellationToken ct=default);
    Task<PackingPolicyDto> UpdatePolicyAsync(UpdatePackingPolicyRequest request,long actor,CancellationToken ct=default);
    Task<PagedResponse<PackingSessionRow>> GetSessionsAsync(PagedRequest request,CancellationToken ct=default);
    Task<PackingSessionDetail> GetSessionAsync(long id,CancellationToken ct=default);
    Task<IReadOnlyList<PackingSourceLineOption>> GetSourceLinesAsync(long id,CancellationToken ct=default);
    Task<PackingSessionDetail> CreateSessionAsync(CreatePackingSessionRequest request,long actor,CancellationToken ct=default);
    Task<HandlingUnitDto> CreateHandlingUnitAsync(long sessionId,CreateHandlingUnitRequest request,long actor,CancellationToken ct=default);
    Task<HandlingUnitDto> PackAsync(long handlingUnitId,PackHandlingUnitLineRequest request,long actor,CancellationToken ct=default);
    Task<HandlingUnitDto> UnpackAsync(long handlingUnitId,UnpackHandlingUnitLineRequest request,long actor,CancellationToken ct=default);
    Task<HandlingUnitDto> MoveAsync(long handlingUnitId,MoveHandlingUnitLineRequest request,long actor,CancellationToken ct=default);
    Task<HandlingUnitDto> CloseAsync(long handlingUnitId,CloseHandlingUnitRequest request,long actor,CancellationToken ct=default);
    Task<HandlingUnitDto> ReopenAsync(long handlingUnitId,Guid idempotencyKey,string? reason,long actor,CancellationToken ct=default);
    Task DeleteHandlingUnitAsync(long handlingUnitId,long actor,CancellationToken ct=default);
    Task<PackingPrintJobRow> EnqueuePrintAsync(long handlingUnitId,PrintHandlingUnitRequest request,long actor,CancellationToken ct=default);
    Task<PagedResponse<PackingPrintJobRow>> GetPrintJobsAsync(PagedRequest request,CancellationToken ct=default);
    Task<ScaleReadingDto> ReadScaleAsync(long handlingUnitId,ScaleReadingRequest request,long actor,CancellationToken ct=default);
}
