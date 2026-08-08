using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Host.Localization;
using verii_wms_api_v2.Shared.Host.Middleware;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class WmsApiMessageResolverTests
{
    [Theory]
    [InlineData(401, false, "Unauthorized")]
    [InlineData(403, false, "Forbidden")]
    [InlineData(404, false, "NotFound")]
    [InlineData(405, false, "MethodNotAllowed")]
    [InlineData(408, false, "RequestTimeout")]
    [InlineData(413, false, "PayloadTooLarge")]
    [InlineData(415, false, "UnsupportedMediaType")]
    [InlineData(422, false, "ValidationFailed")]
    [InlineData(429, false, "TooManyRequests")]
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
    public void Shared_catalog_contains_every_contract_message_for_every_supported_language()
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
                foreach (var code in WmsApiMessageResolver.CatalogCodes)
                {
                    var value = localizer[code];
                    Assert.False(value.ResourceNotFound, $"{language}:{code} resource is missing.");
                    Assert.False(string.IsNullOrWhiteSpace(value.Value));
                    Assert.NotEqual(code, value.Value);
                }
            }
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalCulture;
        }
    }

    [Fact]
    public async Task Response_middleware_localizes_root_and_nested_display_messages()
    {
        using var provider = new ServiceCollection()
            .AddLogging()
            .AddLocalization()
            .BuildServiceProvider();
        var resolver = new WmsApiMessageResolver(provider.GetRequiredService<IStringLocalizer<WmsApiMessageResource>>());
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo("en");
            var body = new MemoryStream();
            var context = new DefaultHttpContext { Response = { Body = body } };
            var middleware = new ApiResponseLocalizationMiddleware(async httpContext =>
            {
                httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                httpContext.Response.ContentType = "application/json; charset=utf-8";
                await httpContext.Response.WriteAsync(
                    """{"success":false,"data":{"status":"Failed","validationMessage":"Stok kodu zorunludur.","matchMessage":"Seçilen stok bulunamadı."},"message":"İstek tamamlanamadı."}""");
            }, resolver);

            await middleware.InvokeAsync(context);
            body.Position = 0;
            using var json = await JsonDocument.ParseAsync(body);

            Assert.Equal("The request information is invalid.", json.RootElement.GetProperty("message").GetString());
            Assert.Equal("BadRequest", json.RootElement.GetProperty("messageCode").GetString());
            var data = json.RootElement.GetProperty("data");
            Assert.Equal("Complete the required information before continuing.", data.GetProperty("validationMessage").GetString());
            Assert.Equal("The selected record was not found or is no longer available.", data.GetProperty("matchMessage").GetString());
            Assert.False(data.TryGetProperty("validationMessageCode", out _));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [Fact]
    public void Model_validation_factory_never_returns_framework_hardtext_to_another_language()
    {
        using var provider = new ServiceCollection()
            .AddLogging()
            .AddLocalization()
            .AddSingleton<WmsApiMessageResolver>()
            .BuildServiceProvider();
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo("de");
            var httpContext = new DefaultHttpContext { RequestServices = provider };
            var modelState = new ModelStateDictionary();
            modelState.AddModelError("stockCode", "Stok kodu zorunludur.");
            var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor(), modelState);

            var result = Assert.IsType<BadRequestObjectResult>(WmsApiValidationResponseFactory.Create(actionContext));
            var response = Assert.IsType<ApiResponse<IReadOnlyDictionary<string, string[]>>>(result.Value);

            Assert.Equal("ValidationFailed", response.MessageCode);
            Assert.Equal("Die übermittelten Angaben konnten nicht validiert werden. Prüfen Sie die markierten Felder.", response.Message);
            Assert.Equal("Füllen Sie die erforderlichen Informationen aus.", response.Data!["stockCode"][0]);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }
}
