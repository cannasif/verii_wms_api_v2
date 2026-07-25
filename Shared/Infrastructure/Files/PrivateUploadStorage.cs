using Microsoft.Extensions.DependencyInjection.Extensions;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Shared.Infrastructure.Files;

public enum PrivateUploadArea
{
    SacMalKabul,
    SacPanel
}

public sealed record PrivateUploadPolicy(
    long MaximumLength,
    IReadOnlyDictionary<string,string> AllowedContentTypes);

public interface IPrivateUploadStorage
{
    Task<string> SaveAsync(
        PrivateUploadArea area,
        long ownerId,
        Stream content,
        string? contentType,
        long length,
        PrivateUploadPolicy policy,
        CancellationToken cancellationToken=default);

    Task<Stream> OpenReadAsync(
        PrivateUploadArea area,
        string storagePath,
        string? legacyArea=null,
        CancellationToken cancellationToken=default);

    void Delete(PrivateUploadArea area,string storagePath,string? legacyArea=null);
}

public sealed class PrivateUploadStorage(IWebHostEnvironment environment):IPrivateUploadStorage
{
    private const string UploadRootName="Upload";

    public async Task<string> SaveAsync(
        PrivateUploadArea area,
        long ownerId,
        Stream content,
        string? contentType,
        long length,
        PrivateUploadPolicy policy,
        CancellationToken cancellationToken=default)
    {
        if(ownerId<=0)throw AppException.BadRequest("Dosya sahibi kimliği geçersiz.");
        var descriptor=UploadContentGuard.ValidateDeclaration(contentType,length,policy);

        var areaName=AreaName(area);
        var directory=Path.Combine(StorageRoot(),areaName,ownerId.ToString());
        Directory.CreateDirectory(directory);
        var fileName=$"{Guid.NewGuid():N}{descriptor.Extension}";
        var target=Path.Combine(directory,fileName);
        var temporary=Path.Combine(directory,$".{Guid.NewGuid():N}.uploading");

        try
        {
            await using(var destination=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None,81920,
                FileOptions.Asynchronous|FileOptions.SequentialScan))
            {
                await UploadContentGuard.CopyAndValidateAsync(content,destination,descriptor,length,policy.MaximumLength,cancellationToken);
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

        return $"{UploadRootName}/{areaName}/{ownerId}/{fileName}";
    }

    public Task<Stream> OpenReadAsync(
        PrivateUploadArea area,
        string storagePath,
        string? legacyArea=null,
        CancellationToken cancellationToken=default)
    {
        _=cancellationToken;
        var target=Resolve(area,storagePath,legacyArea);
        if(!File.Exists(target))throw AppException.NotFound("Yüklenen dosya bulunamadı.");
        return Task.FromResult<Stream>(new FileStream(target,FileMode.Open,FileAccess.Read,FileShare.Read,81920,
            FileOptions.Asynchronous|FileOptions.SequentialScan));
    }

    public void Delete(PrivateUploadArea area,string storagePath,string? legacyArea=null)
    {
        var target=Resolve(area,storagePath,legacyArea);
        if(File.Exists(target))File.Delete(target);
    }

    private string Resolve(PrivateUploadArea area,string storagePath,string? legacyArea)
    {
        if(string.IsNullOrWhiteSpace(storagePath))throw AppException.BadRequest("Dosya yolu geçersiz.");
        var clean=storagePath.Replace('\\','/').TrimStart('/');
        var currentPrefix=$"{UploadRootName}/{AreaName(area)}/";
        if(clean.StartsWith(currentPrefix,StringComparison.OrdinalIgnoreCase))
            return ResolveWithin(Path.Combine(StorageRoot(),AreaName(area)),clean[currentPrefix.Length..],"Dosya yolu geçersiz.");

        if(!string.IsNullOrWhiteSpace(legacyArea))
        {
            var legacyPrefix=$"{legacyArea.Trim('/')}/";
            if(clean.StartsWith(legacyPrefix,StringComparison.OrdinalIgnoreCase))
                return ResolveWithin(Path.Combine(environment.ContentRootPath,"App_Data",legacyArea),clean[legacyPrefix.Length..],"Eski dosya yolu geçersiz.");
        }

        throw AppException.BadRequest("Dosya alanı beklenen modülle eşleşmiyor.");
    }

    private static string ResolveWithin(string rootPath,string relativePath,string error)
    {
        var root=Path.GetFullPath(rootPath);
        var target=Path.GetFullPath(Path.Combine(root,relativePath.Replace('/',Path.DirectorySeparatorChar)));
        if(!target.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
            throw AppException.BadRequest(error);
        return target;
    }

    private string StorageRoot()=>Path.GetFullPath(Path.Combine(environment.ContentRootPath,UploadRootName));
    private static string AreaName(PrivateUploadArea area)=>area switch
    {
        PrivateUploadArea.SacMalKabul=>"SacMalKabul",
        PrivateUploadArea.SacPanel=>"SacPanel",
        _=>throw AppException.BadRequest("Dosya alanı geçersiz.")
    };

}

public static class PrivateUploadStorageRegistration
{
    public static IServiceCollection AddPrivateUploadStorage(this IServiceCollection services)
    {
        services.TryAddSingleton<IPrivateUploadStorage,PrivateUploadStorage>();
        return services;
    }
}

public static class PrivateUploadFileName
{
    public static string ForDisplay(string? untrustedName,int maximumLength=240)
    {
        var name=Path.GetFileName(untrustedName??string.Empty);
        name=new string(name.Where(character=>!char.IsControl(character)).ToArray()).Trim();
        if(string.IsNullOrWhiteSpace(name))name="dosya";
        if(name.Length<=maximumLength)return name;

        var extension=Path.GetExtension(name);
        if(extension.Length>=maximumLength)return name[..maximumLength];
        return $"{Path.GetFileNameWithoutExtension(name)[..(maximumLength-extension.Length)]}{extension}";
    }
}
