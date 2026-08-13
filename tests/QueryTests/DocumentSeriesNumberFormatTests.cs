using verii_wms_api_v2.Modules.DocumentSeries.Application;
using verii_wms_api_v2.Modules.DocumentSeries.Domain;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class DocumentSeriesNumberFormatTests
{
    [Theory]
    [InlineData(DocumentYearFormat.None, "MK", 8, 42, "MK0000000000042")]
    [InlineData(DocumentYearFormat.TwoDigit, "MK", 8, 42, "MK2600000000042")]
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
        Assert.Equal(DocumentNumberRules.TotalLength, result.Length);
    }

    [Theory]
    [InlineData(DocumentYearFormat.None, "MK", 13)]
    [InlineData(DocumentYearFormat.TwoDigit, "MK", 11)]
    [InlineData(DocumentYearFormat.FourDigit, "MKB", 8)]
    public void Counter_length_is_derived_from_prefix_and_year(
        DocumentYearFormat yearFormat,
        string prefix,
        int expected)
    {
        Assert.Equal(expected, DocumentNumberRules.GetRequiredCounterLength(prefix, yearFormat));
    }
}
