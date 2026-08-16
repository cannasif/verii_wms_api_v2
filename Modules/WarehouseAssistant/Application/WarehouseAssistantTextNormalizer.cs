using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace verii_wms_api_v2.Modules.WarehouseAssistant.Application;

/// <summary>
/// Provides the canonical, culture-aware representation used by every local language layer.
/// Identifier separators are preserved so stock, location and document codes remain detectable.
/// </summary>
public static partial class WarehouseAssistantTextNormalizer
{
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var decomposed = value.Trim().ToLower(new CultureInfo("tr-TR")).Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark) continue;

            var normalizedCharacter = character == 'ı' ? 'i' : character;
            builder.Append(char.IsPunctuation(normalizedCharacter) && normalizedCharacter is not '-' and not '/' and not '.' and not '_'
                ? ' '
                : normalizedCharacter);
        }

        return WhitespaceRegex().Replace(builder.ToString().Normalize(NormalizationForm.FormC), " ").Trim();
    }

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();
}
