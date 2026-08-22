using System.Reflection;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using verii_wms_api_v2.Migrations;
using verii_wms_api_v2.Modules.ProductionTransfer.Application;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class ProductionWarehousePoolReleaseContractTests
{
    [Fact]
    public void Task_board_dto_exposes_pool_release_flag()
    {
        Assert.NotNull(typeof(ProductionTransferTaskDto).GetProperty("ReleasedToWarehousePool"));
    }

    [Fact]
    public void Migration_backfills_the_legacy_description_marker()
    {
        var migration = new AddWarehouseTaskPoolReleaseFlag();
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
        var up = typeof(AddWarehouseTaskPoolReleaseFlag).GetMethod(
            "Up",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(up);
        up!.Invoke(migration, [builder]);

        var addedColumn = Assert.Single(builder.Operations.OfType<AddColumnOperation>());
        Assert.Equal("ReleasedToWarehousePool", addedColumn.Name);
        Assert.Equal("RII_WT_TASK", addedColumn.Table);

        var backfill = Assert.Single(builder.Operations.OfType<SqlOperation>()).Sql;
        Assert.Contains("ReleasedToWarehousePool", backfill, StringComparison.Ordinal);
        Assert.Contains("depo havuzuna bırakıldı", backfill, StringComparison.Ordinal);
        Assert.Contains("UPDATE [RII_WT_TASK]", backfill, StringComparison.Ordinal);
    }
}
