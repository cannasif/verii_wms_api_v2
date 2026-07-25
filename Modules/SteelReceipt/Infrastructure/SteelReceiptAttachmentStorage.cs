using verii_wms_api_v2.Modules.SteelReceipt.Application;
using verii_wms_api_v2.Shared.Infrastructure.Files;

namespace verii_wms_api_v2.Modules.SteelReceipt.Infrastructure;

public sealed class SteelReceiptAttachmentStorage(IPrivateUploadStorage storage):ISteelReceiptAttachmentStorage
{
    private static readonly PrivateUploadPolicy Policy=new(
        10*1024*1024,
        new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase)
        {
            ["image/jpeg"]=".jpg",
            ["image/png"]=".png",
            ["image/webp"]=".webp",
            ["application/pdf"]=".pdf"
        });

    public Task<string> SaveAsync(long lineId,SteelReceiptAttachmentUpload upload,CancellationToken ct=default)=>
        storage.SaveAsync(PrivateUploadArea.SacPanel,lineId,upload.Content,upload.ContentType,upload.Length,Policy,ct);

    public Task<Stream> OpenReadAsync(string storagePath,CancellationToken ct=default)=>
        storage.OpenReadAsync(PrivateUploadArea.SacPanel,storagePath,"steel-receipts",ct);

    public void Delete(string storagePath)=>
        storage.Delete(PrivateUploadArea.SacPanel,storagePath,"steel-receipts");
}
