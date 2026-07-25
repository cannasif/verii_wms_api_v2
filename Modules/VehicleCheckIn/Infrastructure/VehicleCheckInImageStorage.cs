using verii_wms_api_v2.Modules.VehicleCheckIn.Application;
using verii_wms_api_v2.Shared.Infrastructure.Files;

namespace verii_wms_api_v2.Modules.VehicleCheckIn.Infrastructure;

public sealed class VehicleCheckInImageStorage(IPrivateUploadStorage storage):IVehicleCheckInImageStorage
{
    private static readonly PrivateUploadPolicy Policy=new(
        8*1024*1024,
        new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase)
        {
            ["image/jpeg"]=".jpg",
            ["image/png"]=".png",
            ["image/webp"]=".webp"
        });

    public Task<string> SaveAsync(long headerId,VehicleImageUpload upload,CancellationToken ct=default)=>
        storage.SaveAsync(PrivateUploadArea.SacMalKabul,headerId,upload.Content,upload.ContentType,upload.Length,Policy,ct);

    public Task<Stream> OpenReadAsync(string storagePath,CancellationToken ct=default)=>
        storage.OpenReadAsync(PrivateUploadArea.SacMalKabul,storagePath,"vehicle-check-in",ct);

    public void Delete(string storagePath)=>
        storage.Delete(PrivateUploadArea.SacMalKabul,storagePath,"vehicle-check-in");
}
