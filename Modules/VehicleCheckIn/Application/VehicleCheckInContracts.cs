using verii_wms_api_v2.Shared;

namespace verii_wms_api_v2.Modules.VehicleCheckIn.Application;

public sealed record SaveVehicleCheckInRequest(long? Id,string? RowVersion,string BranchCode,string PlateNo,string? TrailerPlateNo,string? DriverFirstName,
    string? DriverLastName,string? DriverPhone,string? CarrierName,int SteelSheetCount,long? CustomerId,string? Note);
public sealed record VehicleCheckInImageRow(long Id,long HeaderId,string FileName,string ContentType,long FileSize,int SortOrder,DateTime? CreatedDate);
public sealed record VehicleCheckInRow(long Id,string BranchCode,string PlateNo,string? TrailerPlateNo,string? DriverFirstName,string? DriverLastName,
    string? DriverPhone,string? CarrierName,int SteelSheetCount,long? CustomerId,string? CustomerCode,string? CustomerName,DateTimeOffset CheckedInAtUtc,
    DateOnly BusinessDate,string Status,string? Note,int ImageCount,long? CreatedBy,DateTime? CreatedDate,long? UpdatedBy,DateTime? UpdatedDate,string RowVersion);
public sealed record VehicleCheckInDetail(VehicleCheckInRow Header,IReadOnlyList<VehicleCheckInImageRow> Images);
public sealed record VehicleImageUpload(Stream Content,string FileName,string ContentType,long Length);
public sealed record VehicleImageDownload(Stream Content,string FileName,string ContentType);

public interface IVehicleCheckInService
{
    Task<VehicleCheckInDetail?> FindTodayByPlateAsync(string branchCode,string plateNo,CancellationToken ct=default);
    Task<VehicleCheckInDetail> SaveAsync(SaveVehicleCheckInRequest request,long actor,CancellationToken ct=default);
    Task<VehicleCheckInDetail> GetAsync(long id,CancellationToken ct=default);
    Task<PagedResponse<VehicleCheckInRow>> GetPagedAsync(PagedRequest request,CancellationToken ct=default);
    Task<IReadOnlyList<VehicleCheckInImageRow>> AddImagesAsync(long id,IReadOnlyList<VehicleImageUpload> files,long actor,CancellationToken ct=default);
    Task<VehicleImageDownload> DownloadImageAsync(long imageId,CancellationToken ct=default);
    Task RemoveImageAsync(long imageId,long actor,CancellationToken ct=default);
}

public interface IVehicleCheckInImageStorage
{
    Task<string> SaveAsync(long headerId,VehicleImageUpload upload,CancellationToken ct=default);
    Task<Stream> OpenReadAsync(string storagePath,CancellationToken ct=default);
    void Delete(string storagePath);
}
