using verii_wms_api_v2.Modules.SteelReceipt.Application;
using verii_wms_api_v2.Modules.SteelReceipt.Domain;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class SteelReceiptPlanStatusRulesTests
{
    [Fact]
    public void ResolveFromAggregates_matches_Resolve_for_representative_line_sets()
    {
        var scenarios = new[]
        {
            Array.Empty<SteelReceiptPlanStatusRules.LineState>(),
            [Line(SteelInspectionStatus.Pending, SteelReceiptConversionStatus.NotCreated)],
            [
                Line(SteelInspectionStatus.Approved, SteelReceiptConversionStatus.NotCreated),
                Line(SteelInspectionStatus.Pending, SteelReceiptConversionStatus.NotCreated)
            ],
            [Line(SteelInspectionStatus.Approved, SteelReceiptConversionStatus.NotCreated)],
            [Line(SteelInspectionStatus.Inspected, SteelReceiptConversionStatus.NotCreated)],
            [
                Line(SteelInspectionStatus.Approved, SteelReceiptConversionStatus.Created),
                Line(SteelInspectionStatus.Approved, SteelReceiptConversionStatus.NotCreated)
            ],
            [
                Line(SteelInspectionStatus.Approved, SteelReceiptConversionStatus.Created),
                Line(SteelInspectionStatus.Approved, SteelReceiptConversionStatus.Created)
            ],
        };

        foreach (var lines in scenarios)
        {
            var expected = SteelReceiptPlanStatusRules.Resolve(lines);
            var actual = SteelReceiptPlanStatusRules.ResolveFromAggregates(
                lines.Length > 0,
                lines.Length > 0 && lines.All(x => x.ConversionStatus == SteelReceiptConversionStatus.Created),
                lines.Any(x => x.ConversionStatus == SteelReceiptConversionStatus.Created),
                lines.Any(x => x.InspectionStatus is SteelInspectionStatus.Approved or SteelInspectionStatus.PartiallyApproved),
                lines.Any(x => x.InspectionStatus == SteelInspectionStatus.Pending),
                lines.Any(x => x.InspectionStatus != SteelInspectionStatus.Pending));

            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public void SelectGridStatus_preserves_cancelled_and_otherwise_resolves()
    {
        var lines = new[]
        {
            Line(SteelInspectionStatus.Approved, SteelReceiptConversionStatus.NotCreated)
        };

        Assert.Equal(
            SteelReceiptPlanStatus.Cancelled,
            SteelReceiptPlanStatusRules.SelectGridStatus(
                SteelReceiptPlanStatus.Cancelled,
                lines.Length > 0,
                false,
                false,
                true,
                false,
                true));

        Assert.Equal(
            SteelReceiptPlanStatus.ReadyForReceipt,
            SteelReceiptPlanStatusRules.SelectGridStatus(
                SteelReceiptPlanStatus.Imported,
                lines.Length > 0,
                false,
                false,
                true,
                false,
                true));
    }

    private static SteelReceiptPlanStatusRules.LineState Line(
        SteelInspectionStatus inspectionStatus,
        SteelReceiptConversionStatus conversionStatus) =>
        new(inspectionStatus, conversionStatus);
}
