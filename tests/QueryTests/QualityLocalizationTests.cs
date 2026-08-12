using System.Globalization;
using System.Reflection;
using System.Resources;
using verii_wms_api_v2.Modules.Quality.Localization;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class QualityLocalizationTests
{
    [Fact]
    public void Every_supported_language_contains_every_quality_message()
    {
        var resourceManager = new ResourceManager(typeof(QualityResource));
        var keys = typeof(QualityMessageKeys)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && !field.IsInitOnly)
            .Select(field => Assert.IsType<string>(field.GetRawConstantValue()))
            .ToArray();

        foreach (var language in new[] { "tr", "en", "de", "fr", "es", "it", "ar" })
        {
            var culture = CultureInfo.GetCultureInfo(language);
            foreach (var key in keys)
            {
                var value = resourceManager.GetString(key, culture);
                Assert.False(string.IsNullOrWhiteSpace(value),
                    $"Missing quality resource '{key}' for '{language}'.");
            }
        }
    }
}
