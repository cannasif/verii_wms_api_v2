using verii_wms_api_v2.Shared.Application.Exceptions;
using verii_wms_api_v2.Shared.Infrastructure.Files;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class UploadContentGuardTests
{
    private static readonly PrivateUploadPolicy Policy=new(
        1024,
        new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase)
        {
            ["image/png"]=".png",
            ["application/pdf"]=".pdf"
        });

    [Fact]
    public async Task Valid_png_is_streamed_without_base64_conversion()
    {
        var bytes=new byte[]{0x89,0x50,0x4E,0x47,0x0D,0x0A,0x1A,0x0A,1,2,3,4};
        var descriptor=UploadContentGuard.ValidateDeclaration("image/png",bytes.Length,Policy);
        await using var source=new MemoryStream(bytes);
        await using var destination=new MemoryStream();

        await UploadContentGuard.CopyAndValidateAsync(source,destination,descriptor,bytes.Length,Policy.MaximumLength);

        Assert.Equal(bytes,destination.ToArray());
        Assert.Equal(".png",descriptor.Extension);
    }

    [Fact]
    public async Task Extension_or_content_type_spoofing_is_rejected_by_signature()
    {
        var bytes="not a png"u8.ToArray();
        var descriptor=UploadContentGuard.ValidateDeclaration("image/png",bytes.Length,Policy);
        await using var source=new MemoryStream(bytes);
        await using var destination=new MemoryStream();

        await Assert.ThrowsAsync<AppException>(()=>
            UploadContentGuard.CopyAndValidateAsync(source,destination,descriptor,bytes.Length,Policy.MaximumLength));
    }

    [Fact]
    public async Task Actual_size_cannot_differ_from_declared_size()
    {
        var bytes=new byte[]{0x89,0x50,0x4E,0x47,0x0D,0x0A,0x1A,0x0A,1,2,3,4};
        var descriptor=UploadContentGuard.ValidateDeclaration("image/png",8,Policy);
        await using var source=new MemoryStream(bytes);
        await using var destination=new MemoryStream();

        await Assert.ThrowsAsync<AppException>(()=>
            UploadContentGuard.CopyAndValidateAsync(source,destination,descriptor,8,Policy.MaximumLength));
    }

    [Fact]
    public void Application_profile_contract_does_not_accept_image_urls()
    {
        Assert.DoesNotContain(typeof(verii_wms_api_v2.Modules.Identity.Application.ProfileRequest).GetProperties(),
            property=>property.Name.Contains("Picture",StringComparison.OrdinalIgnoreCase)
                      ||property.Name.Contains("Image",StringComparison.OrdinalIgnoreCase)
                      ||property.Name.Contains("Base64",StringComparison.OrdinalIgnoreCase));
    }
}
