using System.Globalization;
using System.Text;

namespace verii_wms_api_v2.Shared;

internal static class AsciiTurkishSearch
{
    public const string LikeEscapeCharacter = "\\";

    public static string BuildContainsPattern(string term)
    {
        var normalized = (term ?? string.Empty).Trim().Normalize(NormalizationForm.FormC);
        var pattern = new StringBuilder(normalized.Length * 3 + 2).Append('%');
        foreach (var character in normalized)
            AppendPatternCharacter(pattern, character);
        return pattern.Append('%').ToString();
    }

    public static bool Contains(string? candidate, string term)
    {
        if (candidate is null) return false;
        var needle = Fold(term);
        return needle.Length == 0 || Fold(candidate).Contains(needle, StringComparison.Ordinal);
    }

    internal static string Fold(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;

        var folded = new StringBuilder(value.Length);
        foreach (var character in value.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
                continue;

            folded.Append(character switch
            {
                'I' or 'İ' or 'ı' or 'i' => 'i',
                _ => char.ToLowerInvariant(character)
            });
        }

        return folded.ToString();
    }

    private static void AppendPatternCharacter(StringBuilder pattern, char character)
    {
        switch (character)
        {
            case 'c' or 'C' or 'ç' or 'Ç': pattern.Append("[cç]"); return;
            case 'g' or 'G' or 'ğ' or 'Ğ': pattern.Append("[gğ]"); return;
            case 'i' or 'I' or 'İ' or 'ı': pattern.Append("[iı]"); return;
            case 'o' or 'O' or 'ö' or 'Ö': pattern.Append("[oö]"); return;
            case 's' or 'S' or 'ş' or 'Ş': pattern.Append("[sş]"); return;
            case 'u' or 'U' or 'ü' or 'Ü': pattern.Append("[uü]"); return;
        }

        if (character is '%' or '_' or '[' or ']' or '^' or '\\')
            pattern.Append(LikeEscapeCharacter);
        pattern.Append(character);
    }
}
