using verii_wms_api_v2.Modules.Stock.Application;
using verii_wms_api_v2.Shared.Application.Exceptions;
using verii_wms_api_v2.Shared.Infrastructure.Files;

namespace verii_wms_api_v2.Modules.Stock.Infrastructure;

public sealed class StockImageStorage(
    IWebHostEnvironment environment,
    ILogger<StockImageStorage> logger):IStockImageStorage
{
    public const long MaximumFileLength=10*1024*1024;
    private const string UrlPrefix="/uploads/stock-images/";
    private static readonly PrivateUploadPolicy Policy=new(MaximumFileLength,new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"]=".jpg",["image/png"]=".png",["image/webp"]=".webp"
    });

    public async Task<StoredStockImage> SaveAsync(string branchCode,long stockId,StockImageUpload upload,CancellationToken ct=default)
    {
        var descriptor=UploadContentGuard.ValidateDeclaration(upload.ContentType,upload.Length,Policy);
        var safeBranch=new string(branchCode.Where(char.IsLetterOrDigit).ToArray());
        if(string.IsNullOrWhiteSpace(safeBranch))safeBranch="0";
        var directory=Path.Combine(StorageRoot(),safeBranch,stockId.ToString());
        var fileName=$"{Guid.NewGuid():N}{descriptor.Extension}";
        var target=Path.Combine(directory,fileName);
        var temporary=Path.Combine(directory,$".{Guid.NewGuid():N}.uploading");
        try
        {
            Directory.CreateDirectory(directory);
            await using(var destination=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None,81920,
                FileOptions.Asynchronous|FileOptions.SequentialScan))
            {
                await UploadContentGuard.CopyAndValidateAsync(upload.Content,destination,descriptor,upload.Length,MaximumFileLength,ct);
                await destination.FlushAsync(ct);
            }
            File.Move(temporary,target);
        }
        catch(Exception exception)
        {
            TryDelete(temporary);
            TryDelete(target);
            if(exception is UnauthorizedAccessException or IOException)
            {
                logger.LogError(
                    exception,
                    "Stock image storage is not writable. Root={StorageRoot}, StockId={StockId}, Branch={BranchCode}",
                    StorageRoot(),stockId,safeBranch);
                throw AppException.ServiceUnavailable(
                    "Stok görseli depolama alanına yazılamıyor. Sunucu klasör yetkisini kontrol edin.");
            }
            throw;
        }
        return new($"{UrlPrefix}{safeBranch}/{stockId}/{fileName}",PrivateUploadFileName.ForDisplay(upload.FileName),descriptor.ContentType,upload.Length);
    }

    public Task DeleteIfManagedAsync(string? relativeUrl,CancellationToken ct=default)
    {
        _=ct;
        if(string.IsNullOrWhiteSpace(relativeUrl)||!relativeUrl.StartsWith(UrlPrefix,StringComparison.OrdinalIgnoreCase))return Task.CompletedTask;
        var relative=relativeUrl[UrlPrefix.Length..].Replace('/',Path.DirectorySeparatorChar);
        var root=Path.GetFullPath(StorageRoot());
        var target=Path.GetFullPath(Path.Combine(root,relative));
        if(target.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)&&File.Exists(target))File.Delete(target);
        return Task.CompletedTask;
    }
    private static void TryDelete(string path)
    {
        try{if(File.Exists(path))File.Delete(path);}
        catch(IOException){/* Asıl yükleme hatasını gölgelememelidir. */}
        catch(UnauthorizedAccessException){/* Asıl yükleme hatasını gölgelememelidir. */}
    }
    private string StorageRoot()=>Path.Combine(environment.ContentRootPath,"wwwroot","uploads","stock-images");
}
