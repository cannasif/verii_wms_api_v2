using System.Text.RegularExpressions;

namespace verii_wms_api_v2.Shared.Application.Validation;

public static class PurchaseWaybillNumberPolicy
{
    public const int RequiredLength = 15;

    private static readonly Regex AllowedFormat = new(
        $"^[!-~]{{{RequiredLength}}}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex NumericSuffix = new(
        "[0-9]+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var normalized = value.Trim().ToUpperInvariant();
        if (normalized.Length == 0 || normalized.Length >= RequiredLength)
            return normalized.Length == 0 ? null : normalized;

        var suffix = NumericSuffix.Match(normalized);
        if (!suffix.Success)
            return normalized;

        var prefix = normalized[..suffix.Index];
        return prefix + suffix.Value.PadLeft(RequiredLength - prefix.Length, '0');
    }

    public static bool IsValid(string? value) =>
        value is not null && AllowedFormat.IsMatch(value);
}
