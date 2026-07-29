using System.Text.RegularExpressions;

namespace verii_wms_api_v2.Shared.Application.Validation;

public static class PurchaseWaybillNumberPolicy
{
    public const int RequiredLength = 15;

    private static readonly Regex AllowedFormat = new(
        $"^[A-Z0-9]{{{RequiredLength}}}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim().ToUpperInvariant();

    public static bool IsValid(string? value) =>
        value is not null && AllowedFormat.IsMatch(value);
}
