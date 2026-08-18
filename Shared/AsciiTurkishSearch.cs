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
            // SQL Server bracket expressions follow the column collation. Under
            // SQL_Latin1_General_CP1_CI_AS, lowercase i does not case-fold to
            // Turkish uppercase İ inside a bracket class. Listing both cases
            // makes the contract deterministic without applying a column
            // function or COLLATE expression to every searched row.
            case 'a' or 'A' or 'â' or 'Â': pattern.Append("[aAâÂ]"); return;
            case 'c' or 'C' or 'ç' or 'Ç': pattern.Append("[cCçÇ]"); return;
            case 'g' or 'G' or 'ğ' or 'Ğ': pattern.Append("[gGğĞ]"); return;
            case 'i' or 'I' or 'İ' or 'ı' or 'î' or 'Î': pattern.Append("[iIİıîÎ]"); return;
            case 'o' or 'O' or 'ö' or 'Ö': pattern.Append("[oOöÖ]"); return;
            case 's' or 'S' or 'ş' or 'Ş': pattern.Append("[sSşŞ]"); return;
            case 'u' or 'U' or 'ü' or 'Ü' or 'û' or 'Û': pattern.Append("[uUüÜûÛ]"); return;
        }

        if (character is '%' or '_' or '[' or ']' or '^' or '\\')
            pattern.Append(LikeEscapeCharacter);
        pattern.Append(character);
    }
}
