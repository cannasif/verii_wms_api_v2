using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using verii_wms_api_v2.Shared.Host.Localization;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class WmsApiMessageResolverTests
{
    [Theory]
    [InlineData(401, false, "Unauthorized")]
    [InlineData(403, false, "Forbidden")]
    [InlineData(404, false, "NotFound")]
    [InlineData(502, false, "BadGateway")]
    [InlineData(503, false, "ServiceUnavailable")]
    [InlineData(500, false, "InternalServerError")]
    public void Http_statuses_have_stable_message_codes(int statusCode, bool success, string expected)
    {
        Assert.Equal(expected, WmsApiMessageResolver.Classify(statusCode, null, success));
    }

    [Theory]
    [InlineData("Şube kodu zorunludur.", "Required")]
    [InlineData("Seçilen depo bulunamadı.", "ResourceNotFound")]
    [InlineData("Kayıt başka bir kullanıcı tarafından değiştirildi.", "Concurrency")]
    [InlineData("Aynı kod zaten kullanılıyor.", "Duplicate")]
    [InlineData("İstenen miktar mevcut stok miktarını aşıyor.", "Quantity")]
    [InlineData("Girilen değer geçersiz.", "Invalid")]
    public void Validation_and_conflict_messages_are_classified(string message, string expected)
    {
        var statusCode = expected is "Concurrency" or "Duplicate" or "Quantity" ? 409 : 400;
        Assert.Equal(expected, WmsApiMessageResolver.Classify(statusCode, message, false));
    }

    [Theory]
    [InlineData("Kayıt oluşturuldu.", "Created")]
    [InlineData("Kayıt güncellendi.", "Updated")]
    [InlineData("İşlem iptal edildi.", "Cancelled")]
    [InlineData("İşlem tamamlandı.", "Completed")]
    public void Success_messages_are_classified(string message, string expected)
    {
        Assert.Equal(expected, WmsApiMessageResolver.Classify(200, message, true));
    }

    [Fact]
    public void Shared_catalog_contains_the_forbidden_message_for_every_supported_language()
    {
        using var provider = new ServiceCollection()
            .AddLogging()
            .AddLocalization()
            .BuildServiceProvider();
        var localizer = provider.GetRequiredService<IStringLocalizer<WmsApiMessageResource>>();
        var originalCulture = CultureInfo.CurrentUICulture;

        try
        {
            foreach (var language in new[] { "tr", "en", "de", "fr", "ar", "es", "it" })
            {
                CultureInfo.CurrentUICulture = new CultureInfo(language);
                var value = localizer["Forbidden"].Value;

                Assert.False(string.IsNullOrWhiteSpace(value));
                Assert.NotEqual("Forbidden", value);
            }
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalCulture;
        }
    }
}
