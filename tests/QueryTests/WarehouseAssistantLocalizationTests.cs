using System.Globalization;
using System.Reflection;
using System.Resources;
using verii_wms_api_v2.Modules.WarehouseAssistant.Localization;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class WarehouseAssistantLocalizationTests
{
    [Fact]
    public void Every_supported_language_contains_every_assistant_message()
    {
        var resourceManager = new ResourceManager(typeof(WarehouseAssistantResource));
        var keys = typeof(WarehouseAssistantMessageKeys)
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
                Assert.False(string.IsNullOrWhiteSpace(value), $"Missing warehouse assistant resource '{key}' for '{language}'.");
            }
        }
    }
}
