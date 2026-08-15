using verii_wms_api_v2.Modules.Identity.Application;
using verii_wms_api_v2.Shared.Application.Exceptions;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class NavbarAppearanceTests
{
    [Fact]
    public void NormalizeKeys_keeps_order_and_rejects_unknown()
    {
        var stored = NavbarAppearance.NormalizeKeys(["openOperations", "myTasks", "myTasks"], NavbarAppearance.DefaultKpiKeys);
        Assert.Equal("openOperations,myTasks", stored);

        var ex = Assert.Throws<AppException>(() => NavbarAppearance.NormalizeKeys(["weather"], NavbarAppearance.DefaultKpiKeys));
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public void SplitKeys_falls_back_to_default()
    {
        Assert.Equal(NavbarAppearance.DefaultKpiKeys.Split(','), NavbarAppearance.SplitKeys(null));
        Assert.Equal(["myTasks"], NavbarAppearance.SplitKeys("myTasks,unknown"));
    }
}
