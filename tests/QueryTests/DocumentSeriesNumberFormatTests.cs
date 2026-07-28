using verii_wms_api_v2.Modules.DocumentSeries.Application;
using verii_wms_api_v2.Modules.DocumentSeries.Domain;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class DocumentSeriesNumberFormatTests
{
    [Theory]
    [InlineData(DocumentYearFormat.None, "MK", 8, 42, "MK00000042")]
    [InlineData(DocumentYearFormat.TwoDigit, "MK", 8, 42, "MK2600000042")]
    [InlineData(DocumentYearFormat.FourDigit, "MKB", 8, 42, "MKB202600000042")]
    public void FormatNumber_generates_separator_free_netsis_compatible_value(
        DocumentYearFormat yearFormat,
        string prefix,
        int numberLength,
        long number,
        string expected)
    {
        var result = DocumentSeriesService.FormatNumber(
            prefix,
            yearFormat,
            numberLength,
            number,
            new DateTime(2026, 7, 28, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(expected, result);
        Assert.DoesNotContain('-', result);
        Assert.InRange(result.Length, 1, 15);
    }
}
