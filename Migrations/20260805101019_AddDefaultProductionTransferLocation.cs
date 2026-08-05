using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddDefaultProductionTransferLocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "DefaultProductionTransferLocationId",
                table: "RII_WAREHOUSE",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RII_WAREHOUSE_DEFAULT_PRODUCTION_TRANSFER_LOCATION",
                table: "RII_WAREHOUSE",
                column: "DefaultProductionTransferLocationId");

            migrationBuilder.AddForeignKey(
                name: "FK_RII_WAREHOUSE_RII_LOCATION_DefaultProductionTransferLocationId",
                table: "RII_WAREHOUSE",
                column: "DefaultProductionTransferLocationId",
                principalTable: "RII_LOCATION",
                principalColumn: "Id",
                onDelete: ReferentialAction.NoAction);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RII_WAREHOUSE_RII_LOCATION_DefaultProductionTransferLocationId",
                table: "RII_WAREHOUSE");

            migrationBuilder.DropIndex(
                name: "IX_RII_WAREHOUSE_DEFAULT_PRODUCTION_TRANSFER_LOCATION",
                table: "RII_WAREHOUSE");

            migrationBuilder.DropColumn(
                name: "DefaultProductionTransferLocationId",
                table: "RII_WAREHOUSE");
        }
    }
}
