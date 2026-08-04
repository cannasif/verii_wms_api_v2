using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using verii_wms_api_v2.Modules.Identity.Infrastructure;

#nullable disable

namespace verii_wms_api_v2.Migrations;

/// <summary>
/// SQL Server does not allow both warehouse default-location foreign keys to
/// use SET NULL against RII_LOCATION. This bridge runs before the production
/// transfer migration without modifying an already published migration.
/// </summary>
[DbContext(typeof(WmsDbContext))]
[Migration("20260803150000_PrepareWarehouseTransferReturnLocation")]
public sealed class PrepareWarehouseTransferReturnLocation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_RII_WAREHOUSE_RII_LOCATION_DefaultGoodsReceiptLocationId",
            table: "RII_WAREHOUSE");

        migrationBuilder.AddForeignKey(
            name: "FK_RII_WAREHOUSE_RII_LOCATION_DefaultGoodsReceiptLocationId",
            table: "RII_WAREHOUSE",
            column: "DefaultGoodsReceiptLocationId",
            principalTable: "RII_LOCATION",
            principalColumn: "Id",
            onDelete: ReferentialAction.NoAction);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_RII_WAREHOUSE_RII_LOCATION_DefaultGoodsReceiptLocationId",
            table: "RII_WAREHOUSE");

        migrationBuilder.AddForeignKey(
            name: "FK_RII_WAREHOUSE_RII_LOCATION_DefaultGoodsReceiptLocationId",
            table: "RII_WAREHOUSE",
            column: "DefaultGoodsReceiptLocationId",
            principalTable: "RII_LOCATION",
            principalColumn: "Id",
            onDelete: ReferentialAction.SetNull);
    }
}
