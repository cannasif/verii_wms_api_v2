using verii_wms_api_v2.Modules.ProductionTransfer.Application;
using verii_wms_api_v2.Modules.ProductionTransfer.Domain;
using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using verii_wms_api_v2.Modules.WarehouseTransfer.Domain;
using Xunit;

namespace verii_wms_api_v2.tests.QueryTests;

public sealed class ProductionTransferOverIssueSupportTests
{
    [Fact]
    public void GetMaxPickQuantity_applies_tolerance_when_over_issue_enabled()
    {
        var line = new WarehouseTransferLine { RequestedQuantity = 100 };
        var policy = new ProductionTransferPolicy
        {
            AllowOverIssue = true,
            OverIssueTolerancePercent = 10,
        };

        Assert.Equal(110, ProductionTransferOverIssueSupport.GetMaxPickQuantity(line, policy));
    }

    [Fact]
    public void GetMaxPickQuantity_uses_requested_quantity_when_over_issue_disabled()
    {
        var line = new WarehouseTransferLine { RequestedQuantity = 100 };
        var policy = new ProductionTransferPolicy
        {
            AllowOverIssue = false,
            OverIssueTolerancePercent = 10,
        };

        Assert.Equal(100, ProductionTransferOverIssueSupport.GetMaxPickQuantity(line, policy));
    }

    [Fact]
    public void BuildOverIssueLines_returns_only_lines_above_requested()
    {
        var lines = new[]
        {
            new WarehouseTransferLine
            {
                Id = 1,
                LineNo = 1,
                StockCodeSnapshot = "A",
                UnitCode = "AD",
                RequestedQuantity = 10,
                PickedQuantity = 12,
            },
            new WarehouseTransferLine
            {
                Id = 2,
                LineNo = 2,
                StockCodeSnapshot = "B",
                UnitCode = "AD",
                RequestedQuantity = 5,
                PickedQuantity = 4,
            },
        };

        var overIssueLines = ProductionTransferOverIssueSupport.BuildOverIssueLines(lines);

        Assert.Single(overIssueLines);
        Assert.Equal("A", overIssueLines[0].StockCode);
        Assert.Equal(2, overIssueLines[0].OverIssueQuantity);
    }
}
