using verii_wms_api_v2.Modules.Production.Application;
using verii_wms_api_v2.Modules.Production.Domain;
using Xunit;

namespace verii_wms_api_v2.tests.QueryTests;

public sealed class ProductionSourceWorkOrderAssignmentFilterTests
{
    [Fact]
    public void ExcludeAssigned_removes_work_orders_with_active_production_transfer()
    {
        var rows = new[]
        {
            CreateRow("IE-100"),
            CreateRow("IE-200"),
            CreateRow("IE-300"),
        };
        var assigned = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "IE-200" };

        var filtered = ProductionSourceWorkOrderAssignmentFilter.ExcludeAssigned(rows, assigned);

        Assert.Equal(["IE-100", "IE-300"], filtered.Select(x => x.WorkOrderNumber).ToArray());
    }

    [Fact]
    public void IsAssigned_is_case_insensitive()
    {
        var assigned = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ie-500" };

        Assert.True(ProductionSourceWorkOrderAssignmentFilter.IsAssigned("IE-500", assigned));
    }

    private static ProductionSourceWorkOrderRow CreateRow(string workOrderNumber) =>
        new(
            ProductionOrderSourceType.NetsisErpFunctions,
            "NETSIS",
            1,
            workOrderNumber,
            1,
            "STK",
            "Stok",
            null,
            1,
            "ADET",
            1,
            null,
            null,
            null,
            1,
            1,
            false);
}
