using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddProductionPickingStagingLocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "ProductionPickingStagingLocationId",
                table: "RII_WAREHOUSE",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RII_WAREHOUSE_PRODUCTION_PICKING_STAGING_LOCATION",
                table: "RII_WAREHOUSE",
                column: "ProductionPickingStagingLocationId");

            migrationBuilder.AddForeignKey(
                name: "FK_RII_WAREHOUSE_RII_LOCATION_ProductionPickingStagingLocationId",
                table: "RII_WAREHOUSE",
                column: "ProductionPickingStagingLocationId",
                principalTable: "RII_LOCATION",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RII_WAREHOUSE_RII_LOCATION_ProductionPickingStagingLocationId",
                table: "RII_WAREHOUSE");

            migrationBuilder.DropIndex(
                name: "IX_RII_WAREHOUSE_PRODUCTION_PICKING_STAGING_LOCATION",
                table: "RII_WAREHOUSE");

            migrationBuilder.DropColumn(
                name: "ProductionPickingStagingLocationId",
                table: "RII_WAREHOUSE");
        }
    }
}
