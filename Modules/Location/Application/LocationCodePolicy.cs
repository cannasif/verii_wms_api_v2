using System.Text.RegularExpressions;

namespace verii_wms_api_v2.Modules.Location.Application;

internal static partial class LocationCodePolicy
{
    internal const int MaxLength = 50;

    internal static string Normalize(string? value) => value?.Trim().ToUpperInvariant() ?? string.Empty;

    internal static bool IsValid(string? normalizedCode) =>
        !string.IsNullOrWhiteSpace(normalizedCode) && CodePattern().IsMatch(normalizedCode);

    [GeneratedRegex("^[A-Z0-9][A-Z0-9._/-]{0,49}$", RegexOptions.CultureInvariant)]
    private static partial Regex CodePattern();
}
