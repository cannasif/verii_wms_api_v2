using System.Buffers;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Shared.Infrastructure.Files;

public sealed record ValidatedUploadDescriptor(string ContentType,string Extension);

public static class UploadContentGuard
{
    private const int SignatureLength=12;
    private const int BufferLength=81920;

    public static ValidatedUploadDescriptor ValidateDeclaration(
        string? contentType,
        long declaredLength,
        PrivateUploadPolicy policy)
    {
        if(declaredLength<=0||declaredLength>policy.MaximumLength)
            throw AppException.BadRequest($"Dosya boyutu 1 bayt ile {policy.MaximumLength/(1024*1024)} MB arasında olmalıdır.");

        var normalizedType=NormalizeContentType(contentType);
        if(!policy.AllowedContentTypes.TryGetValue(normalizedType,out var extension))
            throw AppException.BadRequest("Desteklenmeyen dosya türü.");

        return new(normalizedType,extension);
    }

    public static async Task CopyAndValidateAsync(
        Stream source,
        Stream destination,
        ValidatedUploadDescriptor descriptor,
        long declaredLength,
        long maximumLength,
        CancellationToken cancellationToken=default)
    {
        if(!source.CanRead)throw AppException.BadRequest("Dosya içeriği okunamıyor.");
        if(!destination.CanWrite)throw new InvalidOperationException("Hedef dosya akışı yazılabilir olmalıdır.");

        var buffer=ArrayPool<byte>.Shared.Rent(BufferLength);
        var signature=new byte[SignatureLength];
        var signatureBytes=0;
        long total=0;

        try
        {
            while(true)
            {
                var read=await source.ReadAsync(buffer.AsMemory(0,buffer.Length),cancellationToken);
                if(read==0)break;

                total+=read;
                if(total>maximumLength)
                    throw AppException.BadRequest($"Dosya boyutu en fazla {maximumLength/(1024*1024)} MB olabilir.");

                if(signatureBytes<SignatureLength)
                {
                    var copyLength=Math.Min(SignatureLength-signatureBytes,read);
                    buffer.AsSpan(0,copyLength).CopyTo(signature.AsSpan(signatureBytes));
                    signatureBytes+=copyLength;
                }

                await destination.WriteAsync(buffer.AsMemory(0,read),cancellationToken);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer,clearArray:true);
        }

        if(total!=declaredLength)
            throw AppException.BadRequest("Dosyanın bildirilen ve gerçek boyutu eşleşmiyor.");
        if(!MatchesSignature(descriptor.ContentType,signature.AsSpan(0,signatureBytes)))
            throw AppException.BadRequest("Dosya içeriği bildirilen dosya türüyle eşleşmiyor.");
    }

    private static string NormalizeContentType(string? contentType)=>
        (contentType??string.Empty).Split(';',2)[0].Trim().ToLowerInvariant();

    private static bool MatchesSignature(string contentType,ReadOnlySpan<byte> header)=>contentType switch
    {
        "image/jpeg"=>header.Length>=3&&header[0]==0xFF&&header[1]==0xD8&&header[2]==0xFF,
        "image/png"=>header.Length>=8&&header[..8].SequenceEqual(new byte[]{0x89,0x50,0x4E,0x47,0x0D,0x0A,0x1A,0x0A}),
        "image/webp"=>header.Length>=12&&header[..4].SequenceEqual("RIFF"u8)&&header.Slice(8,4).SequenceEqual("WEBP"u8),
        "application/pdf"=>header.Length>=5&&header[..5].SequenceEqual("%PDF-"u8),
        _=>false
    };
}
