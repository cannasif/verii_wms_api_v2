using verii_wms_api_v2.Shared.Infrastructure.Files;

namespace verii_wms_api_v2.Modules.Procurement.Infrastructure;

public interface IProcurementAttachmentStorage
{
    Task<string> SaveAsync(long ownerId, Stream content, string? contentType, string? fileName, long length, CancellationToken ct = default);
    Task<Stream> OpenReadAsync(string storagePath, CancellationToken ct = default);
    void Delete(string storagePath);
}

public sealed class ProcurementAttachmentStorage(IPrivateUploadStorage storage) : IProcurementAttachmentStorage
{
    private static readonly PrivateUploadPolicy Policy = new(
        10 * 1024 * 1024,
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["image/jpeg"] = ".jpg",
            ["image/png"] = ".png",
            ["image/webp"] = ".webp",
            ["application/pdf"] = ".pdf",
        });

    public Task<string> SaveAsync(long ownerId, Stream content, string? contentType, string? fileName, long length, CancellationToken ct = default)
    {
        _ = fileName;
        return storage.SaveAsync(PrivateUploadArea.Procurement, ownerId, content, contentType, length, Policy, ct);
    }

    public Task<Stream> OpenReadAsync(string storagePath, CancellationToken ct = default) =>
        storage.OpenReadAsync(PrivateUploadArea.Procurement, storagePath, cancellationToken: ct);

    public void Delete(string storagePath) =>
        storage.Delete(PrivateUploadArea.Procurement, storagePath);
}
