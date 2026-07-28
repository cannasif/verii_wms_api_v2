using verii_wms_api_v2.Modules.Quality.Application;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class QualityWarehouseDispositionTests
{
    [Fact]
    public void Same_warehouse_disposition_remains_an_internal_location_movement()
    {
        Assert.False(QualityService.RequiresDat(10, 10));
    }

    [Fact]
    public void Different_warehouse_disposition_requires_a_DAT()
    {
        Assert.True(QualityService.RequiresDat(10, 20));
    }
}
