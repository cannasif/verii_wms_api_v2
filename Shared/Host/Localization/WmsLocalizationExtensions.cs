using System.Globalization;
using Microsoft.AspNetCore.Localization;

namespace verii_wms_api_v2.Shared.Host.Localization;

public static class WmsLocalizationExtensions
{
    private static readonly string[] SupportedLanguageCodes = ["tr", "en", "de", "fr", "ar", "es", "it"];
    private static readonly CultureInfo[] SupportedCultures = SupportedLanguageCodes.Select(code => new CultureInfo(code)).ToArray();

    public static IServiceCollection AddWmsLocalization(this IServiceCollection services)
    {
        services.AddLocalization();
        services.Configure<RequestLocalizationOptions>(options =>
        {
            options.DefaultRequestCulture = new RequestCulture("tr");
            options.SupportedCultures = SupportedCultures;
            options.SupportedUICultures = SupportedCultures;
            options.FallBackToParentCultures = true;
            options.FallBackToParentUICultures = true;
            options.RequestCultureProviders.Insert(0, new XLanguageRequestCultureProvider(SupportedLanguageCodes));
        });
        return services;
    }

    public static IApplicationBuilder UseWmsLocalization(this IApplicationBuilder app) => app.UseRequestLocalization();
}

internal sealed class XLanguageRequestCultureProvider(IEnumerable<string> supportedLanguages) : RequestCultureProvider
{
    private readonly HashSet<string> _supported = new(supportedLanguages, StringComparer.OrdinalIgnoreCase);

    public override Task<ProviderCultureResult?> DetermineProviderCultureResult(HttpContext httpContext)
    {
        var raw = httpContext.Request.Headers["X-Language"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(raw)) return NullProviderCultureResult;

        var normalized = raw.Trim().Replace('_', '-').Split('-', 2)[0].ToLowerInvariant();
        if (normalized == "sa") normalized = "ar";
        return _supported.Contains(normalized)
            ? Task.FromResult<ProviderCultureResult?>(new ProviderCultureResult(normalized))
            : NullProviderCultureResult;
    }
}
