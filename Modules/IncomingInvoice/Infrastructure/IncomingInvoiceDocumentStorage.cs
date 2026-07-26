using verii_wms_api_v2.Modules.IncomingInvoice.Application;
using verii_wms_api_v2.Shared.Infrastructure.Files;

namespace verii_wms_api_v2.Modules.IncomingInvoice.Infrastructure;

public sealed class IncomingInvoiceDocumentStorage(IPrivateUploadStorage storage)
    : IIncomingInvoiceDocumentStorage
{
    private static readonly PrivateUploadPolicy Policy = new(
        50 * 1024 * 1024,
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["application/pdf"] = ".pdf",
            ["application/xml"] = ".xml",
            ["text/xml"] = ".xml"
        });

    public async Task<string> SaveAsync(
        long invoiceId, byte[] content, string contentType, CancellationToken ct = default)
    {
        await using var stream = new MemoryStream(content, writable: false);
        return await storage.SaveAsync(
            PrivateUploadArea.IncomingInvoice, invoiceId, stream, contentType, content.LongLength, Policy, ct);
    }

    public Task<Stream> OpenReadAsync(string storagePath, CancellationToken ct = default) =>
        storage.OpenReadAsync(PrivateUploadArea.IncomingInvoice, storagePath, cancellationToken: ct);

    public void Delete(string storagePath) =>
        storage.Delete(PrivateUploadArea.IncomingInvoice, storagePath);
}
