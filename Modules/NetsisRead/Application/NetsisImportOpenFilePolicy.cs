using verii_wms_api_v2.Modules.NetsisRead.Application.Dtos;

namespace verii_wms_api_v2.Modules.NetsisRead.Application;

public static class NetsisImportOpenFilePolicy
{
    public const int MaxFileNumberLength = 20;

    public static string NormalizeFileNumber(string? fileNumber)
    {
        var normalized = fileNumber?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            throw new ArgumentException("İthalat dosya numarası zorunludur.", nameof(fileNumber));
        if (normalized.Length > MaxFileNumberLength)
            throw new ArgumentException(
                $"İthalat dosya numarası en fazla {MaxFileNumberLength} karakter olabilir.",
                nameof(fileNumber));
        return normalized;
    }

    public static NetsisImportOpenFileDto? FindOpenFile(
        string? fileNumber,
        IEnumerable<NetsisImportOpenFileDto> openFiles)
    {
        ArgumentNullException.ThrowIfNull(openFiles);
        var normalized = NormalizeFileNumber(fileNumber);
        return openFiles.FirstOrDefault(file => string.Equals(
            file.FileNumber.Trim(),
            normalized,
            StringComparison.OrdinalIgnoreCase));
    }
}
