using verii_wms_api_v2.Modules.Quality.Application;
using verii_wms_api_v2.Modules.Quality.Domain;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class QualityInspectionPriorityTests
{
    [Fact]
    public void Toggle_priority_changes_open_inspection_both_directions_and_records_actor()
    {
        var inspection = new QualityInspection { Status = QualityInspectionStatus.Pending };

        Assert.True(QualityService.TogglePriority(inspection, 41));
        Assert.True(inspection.IsPriority);
        Assert.Equal(41, inspection.UpdatedBy);
        Assert.NotNull(inspection.UpdatedDate);

        Assert.False(QualityService.TogglePriority(inspection, 42));
        Assert.False(inspection.IsPriority);
        Assert.Equal(42, inspection.UpdatedBy);
    }

    [Theory]
    [InlineData(QualityInspectionStatus.Pending, true)]
    [InlineData(QualityInspectionStatus.InProgress, true)]
    [InlineData(QualityInspectionStatus.PartiallyDecided, true)]
    [InlineData(QualityInspectionStatus.Quarantined, true)]
    [InlineData(QualityInspectionStatus.Passed, false)]
    [InlineData(QualityInspectionStatus.Failed, false)]
    [InlineData(QualityInspectionStatus.Released, false)]
    [InlineData(QualityInspectionStatus.Cancelled, false)]
    public void Only_open_inspections_can_be_prioritized(QualityInspectionStatus status, bool expected)
    {
        Assert.Equal(expected, QualityService.CanPrioritize(status));
    }
}
