using verii_wms_api_v2.Modules.SteelReceipt.Domain;
using verii_wms_api_v2.Shared;

namespace verii_wms_api_v2.Modules.SteelReceipt.Application;

public sealed record SteelImportLineRequest(int RowNumber,string? NetsisOrderNo,string? NetsisOrderLineNo,long? StockId,string StockCode,
    long? YapCodeId,string? YapCode,string SupplierSerialNo,string? SecondarySerialNo,decimal ExpectedQuantity,string UnitCode,string? CombinedSize,string? MaterialGrade,
    string? HeatNumber,string? CertificateNumber,long? TargetWarehouseId,long? ReceivingLocationId);
public sealed record PreviewSteelReceiptImportRequest(string BranchCode,string ImportReferenceNo,string SourceFileName,string? ExportReferenceNo,
    long? VehicleCheckInId,long SupplierId,long TargetWarehouseId,long? ReceivingLocationId,long DocumentSeriesId,string? WaybillNo,DateOnly? WaybillDate,
    DateTimeOffset? PlannedArrivalAtUtc,IReadOnlyList<SteelImportLineRequest> Lines);
public sealed record SteelImportPreviewLine(int RowNumber,string SupplierSerialNo,string? StockCode,string Action,string? ExistingDCode,IReadOnlyList<string> Errors);
public sealed record SteelImportPreview(int TotalRows,int NewRows,int ExistingRows,int ErrorRows,decimal TotalExpectedQuantity,IReadOnlyList<SteelImportPreviewLine> Lines);
public sealed record CommitSteelReceiptImportRequest(Guid IdempotencyKey,PreviewSteelReceiptImportRequest Import);

public sealed record SteelReceiptPlanGridRow(long Id,string BranchCode,string ImportReferenceNo,string SourceFileName,string? ExportReferenceNo,
    long? VehicleCheckInId,string? VehiclePlateNo,string? DriverName,long SupplierId,string SupplierCode,string SupplierName,long TargetWarehouseId,int WarehouseCode,string WarehouseName,
    SteelReceiptPlanStatus Status,int TotalLineCount,decimal TotalExpectedQuantity,DateTimeOffset ImportedAtUtc,
    long? CreatedBy,DateTime? CreatedDate,long? UpdatedBy,DateTime? UpdatedDate);
public sealed record SteelReceiptLineGridRow(long Id,long PlanId,string ImportReferenceNo,int LineNo,string DCode,string? NetsisOrderNo,
    string StockCode,string? StockName,string SupplierSerialNo,string? SecondarySerialNo,string? CombinedSize,string? MaterialGrade,
    string? HeatNumber,string? CertificateNumber,decimal ExpectedQuantity,decimal ArrivedQuantity,decimal ApprovedQuantity,
    decimal RejectedQuantity,string UnitCode,SteelArrivalStatus ArrivalStatus,SteelInspectionStatus InspectionStatus,
    SteelReceiptConversionStatus ConversionStatus,SteelPutawayStatus PutawayStatus,string? GoodsReceiptNo,long? GoodsReceiptId,
    string? ErpIntegrationStatus,long TargetWarehouseId,long ReceivingLocationId,long? GoodsReceiptLineId,long? CreatedBy,DateTime? CreatedDate,long? UpdatedBy,DateTime? UpdatedDate,string RowVersion);
public sealed record SteelReceiptSourceRow(long PlanId,string ImportReferenceNo,string SourceFileName,string? WaybillNo,DateOnly? WaybillDate,
    long SupplierId,string SupplierCode,string SupplierName,SteelReceiptPlanStatus Status,int TotalLineCount,decimal TotalExpectedQuantity,
    IReadOnlyList<SteelReceiptLineGridRow> Lines);
public sealed record InspectSteelReceiptLineRequest(bool IsArrived,decimal ArrivedQuantity,decimal ApprovedQuantity,decimal RejectedQuantity,
    string? RejectReason,string? Note,string RowVersion);
public sealed record ConvertSteelReceiptRequest(Guid IdempotencyKey,DateOnly DocumentDate,IReadOnlyList<long> LineIds,
    IReadOnlyList<long>? AssignedUserIds,bool AssignToAllActiveUsers,byte Priority,string? Description,
    SteelReceiptConversionMode Mode=0,string? WaybillNo=null,
    string? ElectronicWaybillNo=null,DateOnly? WaybillDate=null);
public sealed record ConvertSteelReceiptResult(long GoodsReceiptId,string DocumentNo,long? TaskId,string? TaskNo,
    long? ExecutionId,long? StockMovementOperationId,IReadOnlyList<long> GeneratedLabelIds,
    int ConvertedLineCount,decimal ConvertedQuantity,SteelReceiptConversionMode Mode,bool Replayed);
public sealed record PlaceSteelReceiptLineRequest(Guid IdempotencyKey,long LocationId,string RowVersion);
public sealed record PlaceSteelReceiptLineResult(long PlacementId,long StockMovementOperationId,bool Replayed,
    long LocationId,SteelPlacementType PlacementType,int RowNo,int PositionNo,int StackOrderNo);
public sealed record SteelReceiptAttachmentUpload(Stream Content,string FileName,string ContentType,long Length);
public sealed record SteelReceiptAttachmentRow(long Id,long PlanLineId,string FileName,string ContentType,string Url,string? Caption,long FileSize,
    long? CreatedBy,DateTime? CreatedDate);
public sealed record SteelReceiptAttachmentDownload(Stream Content,string FileName,string ContentType);
public sealed record SteelPlacementOccupancyRow(long PlacementId,long PlanLineId,string DCode,string StockCode,string SupplierSerialNo,
    string? CombinedSize,string? MaterialGrade,decimal Quantity,string UnitCode,long WarehouseId,long LocationId,SteelPlacementType PlacementType,
    int RowNo,int PositionNo,int? StackOrderNo,DateTimeOffset PlacedAtUtc);

public interface ISteelReceiptService
{
    Task<SteelImportPreview> PreviewAsync(PreviewSteelReceiptImportRequest request,CancellationToken ct=default);
    Task<long> CommitAsync(CommitSteelReceiptImportRequest request,long actor,CancellationToken ct=default);
    Task<PagedResponse<SteelReceiptPlanGridRow>> GetPlansPagedAsync(PagedRequest request,CancellationToken ct=default);
    Task<PagedResponse<SteelReceiptLineGridRow>> GetLinesPagedAsync(PagedRequest request,CancellationToken ct=default);
    Task<PagedResponse<SteelReceiptLineGridRow>> GetReceiptCandidatesPagedAsync(PagedRequest request,CancellationToken ct=default);
    Task<SteelReceiptSourceRow> GetReceiptSourceAsync(string reference,CancellationToken ct=default);
    Task<PagedResponse<SteelReceiptLineGridRow>> GetPlacementCandidatesPagedAsync(PagedRequest request,CancellationToken ct=default);
    Task<SteelReceiptLineGridRow> GetLineAsync(long lineId,CancellationToken ct=default);
    Task<IReadOnlyList<SteelPlacementOccupancyRow>> GetOccupancyAsync(long locationId,CancellationToken ct=default);
    Task<SteelReceiptLineGridRow> InspectAsync(long lineId,InspectSteelReceiptLineRequest request,long actor,CancellationToken ct=default);
    Task<ConvertSteelReceiptResult> ConvertAsync(long planId,ConvertSteelReceiptRequest request,long actor,CancellationToken ct=default);
    Task<PlaceSteelReceiptLineResult> PlaceAsync(long lineId,PlaceSteelReceiptLineRequest request,long actor,CancellationToken ct=default);
    Task<IReadOnlyList<SteelReceiptAttachmentRow>> GetAttachmentsAsync(long lineId,CancellationToken ct=default);
    Task<SteelReceiptAttachmentRow> AddAttachmentAsync(long lineId,SteelReceiptAttachmentUpload upload,string? caption,long actor,CancellationToken ct=default);
    Task<SteelReceiptAttachmentDownload> DownloadAttachmentAsync(long attachmentId,CancellationToken ct=default);
    Task RemoveAttachmentAsync(long attachmentId,long actor,CancellationToken ct=default);
}

public interface ISteelReceiptAttachmentStorage
{
    Task<string> SaveAsync(long lineId,SteelReceiptAttachmentUpload upload,CancellationToken ct=default);
    Task<Stream> OpenReadAsync(string storagePath,CancellationToken ct=default);
    void Delete(string storagePath);
}
