using verii_wms_api_v2.Modules.Identity.Application;
using verii_wms_api_v2.Shared.Infrastructure.Files;

namespace verii_wms_api_v2.Modules.Identity.Infrastructure;

public sealed class ProfileImageStorage(IWebHostEnvironment environment) : IProfileImageStorage
{
    private const long MaximumLength = 5 * 1024 * 1024;
    private static readonly PrivateUploadPolicy Policy=new(
        MaximumLength,
        new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase)
        {
            ["image/jpeg"]=".jpg",
            ["image/png"]=".png",
            ["image/webp"]=".webp"
        });
    private const string UrlPrefix = "/uploads/profiles/";

    public async Task<string> SaveAsync(long userId, ProfileImageUpload upload, CancellationToken cancellationToken = default)
    {
        var descriptor=UploadContentGuard.ValidateDeclaration(upload.ContentType,upload.Length,Policy);
        var directory = StorageDirectory(); Directory.CreateDirectory(directory);
        var fileName = $"{userId}-{Guid.NewGuid():N}{descriptor.Extension}";
        var target=Path.Combine(directory,fileName);
        var temporary=Path.Combine(directory,$".{Guid.NewGuid():N}.uploading");
        try
        {
            await using(var destination=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None,81920,
                FileOptions.Asynchronous|FileOptions.SequentialScan))
            {
                await UploadContentGuard.CopyAndValidateAsync(upload.Content,destination,descriptor,upload.Length,MaximumLength,cancellationToken);
                await destination.FlushAsync(cancellationToken);
            }
            File.Move(temporary,target);
        }
        catch
        {
            if(File.Exists(temporary))File.Delete(temporary);
            if(File.Exists(target))File.Delete(target);
            throw;
        }
        return $"{UrlPrefix}{fileName}";
    }

    public Task DeleteIfManagedAsync(string? relativeUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(relativeUrl) || !relativeUrl.StartsWith(UrlPrefix, StringComparison.OrdinalIgnoreCase)) return Task.CompletedTask;
        var fileName = Path.GetFileName(relativeUrl); if (string.IsNullOrWhiteSpace(fileName)) return Task.CompletedTask;
        var directory = Path.GetFullPath(StorageDirectory()); var path = Path.GetFullPath(Path.Combine(directory, fileName));
        if (!path.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return Task.CompletedTask;
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    private string StorageDirectory() => Path.Combine(environment.ContentRootPath, "wwwroot", "uploads", "profiles");
}
