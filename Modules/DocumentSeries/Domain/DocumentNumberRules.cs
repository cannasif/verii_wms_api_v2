using System.Globalization;

namespace verii_wms_api_v2.Modules.DocumentSeries.Domain;

public static class DocumentNumberRules
{
    public const int TotalLength = 15;
    public const int MinimumCounterLength = 3;

    public static int GetYearLength(DocumentYearFormat yearFormat) => yearFormat switch
    {
        DocumentYearFormat.TwoDigit => 2,
        DocumentYearFormat.FourDigit => 4,
        _ => 0
    };

    public static int GetRequiredCounterLength(string prefix, DocumentYearFormat yearFormat) =>
        TotalLength - (prefix?.Trim().Length ?? 0) - GetYearLength(yearFormat);

    public static string Format(string prefix, DocumentYearFormat yearFormat, long number, DateTime issuedAt)
    {
        var normalizedPrefix = prefix?.Trim().ToUpperInvariant() ?? string.Empty;
        var counterLength = GetRequiredCounterLength(normalizedPrefix, yearFormat);
        if (counterLength < MinimumCounterLength)
            throw new ArgumentOutOfRangeException(nameof(prefix));
        if (number < 0)
            throw new ArgumentOutOfRangeException(nameof(number));

        var numberText = number.ToString(CultureInfo.InvariantCulture);
        if (numberText.Length > counterLength)
            throw new ArgumentOutOfRangeException(nameof(number));

        var year = yearFormat switch
        {
            DocumentYearFormat.TwoDigit => issuedAt.ToString("yy", CultureInfo.InvariantCulture),
            DocumentYearFormat.FourDigit => issuedAt.ToString("yyyy", CultureInfo.InvariantCulture),
            _ => string.Empty
        };
        var result = $"{normalizedPrefix}{year}{numberText.PadLeft(counterLength, '0')}";
        if (result.Length != TotalLength)
            throw new InvalidOperationException();

        return result;
    }
}
