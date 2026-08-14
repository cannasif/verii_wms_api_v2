using verii_wms_api_v2.Modules.Quality.Domain;
using verii_wms_api_v2.Modules.Quality.Application;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class QualitySamplingCalculatorTests
{
    [Theory]
    [InlineData(100, 10, 10)]
    [InlineData(13, 10, 2)]
    [InlineData(3, 50, 2)]
    [InlineData(5, 200, 5)]
    public void Percentage_sampling_is_rounded_up_and_capped_by_lot(
        decimal lotQuantity,
        decimal percentage,
        decimal expected)
    {
        var actual = QualitySamplingCalculator.Calculate(
            lotQuantity,
            QualitySamplingMode.Percentage,
            percentage);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(100, 12, 12)]
    [InlineData(8, 12, 8)]
    public void Fixed_sampling_never_exceeds_the_lot(
        decimal lotQuantity,
        decimal fixedQuantity,
        decimal expected)
    {
        var actual = QualitySamplingCalculator.Calculate(
            lotQuantity,
            QualitySamplingMode.FixedQuantity,
            fixedQuantity);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Full_sampling_requires_the_entire_lot()
    {
        Assert.Equal(
            37m,
            QualitySamplingCalculator.Calculate(37m, QualitySamplingMode.All, 100m));
    }

    [Fact]
    public void Handling_unit_sampling_does_not_under_sample_without_handling_unit_data()
    {
        Assert.Equal(
            37m,
            QualitySamplingCalculator.Calculate(
                37m,
                QualitySamplingMode.EveryNthHandlingUnit,
                5m));
    }

    [Fact]
    public void Decision_minimum_is_based_on_the_original_lot_not_current_disposition()
    {
        var line = new QualityInspectionLine
        {
            Quantity = 100m,
            SampleQuantity = 10m,
            QuarantineQuantity = 4m,
            Decision = QualityDecision.Quarantined
        };

        Assert.Equal(10m, QualityService.RequiredControlQuantityForDecision(line));
    }

    [Fact]
    public void Physical_control_capacity_is_independent_from_decision_quantity()
    {
        var line = new QualityInspectionLine
        {
            Id = 41,
            Quantity = 100m,
            SampleQuantity = 10m,
            InspectedQuantity = 4m
        };
        var decisions = new Dictionary<long, QualityInspectionQuantityDecisionRequest>
        {
            [line.Id] = new(line.Id, 100m, 0m, 0m)
        };

        var parts = QualityService.BuildDecisionParts([line], decisions, QualityDecision.Pending);

        Assert.Equal(6m, QualityService.RequiredControlQuantityForDecision(line));
        Assert.Equal(96m, QualityService.RemainingInspectableQuantity(line));
        var part = Assert.Single(parts);
        Assert.Equal(100m, part.Quantity);
        Assert.Equal(QualityDecision.Accepted, part.Decision);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(6, 4)]
    [InlineData(10, 0)]
    [InlineData(12, 0)]
    public void Decision_minimum_uses_previous_controls_cumulatively(
        decimal previouslyInspected,
        decimal expectedOutstandingMinimum)
    {
        var line = new QualityInspectionLine
        {
            Quantity = 100m,
            SampleQuantity = 10m,
            InspectedQuantity = previouslyInspected
        };

        Assert.Equal(
            expectedOutstandingMinimum,
            QualityService.RequiredControlQuantityForDecision(line));
    }
}
