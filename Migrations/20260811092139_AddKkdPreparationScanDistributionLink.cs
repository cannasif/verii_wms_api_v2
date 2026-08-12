using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddKkdPreparationScanDistributionLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RII_KKD_PREPARATION_BARCODE_SCAN_TaskLineId",
                table: "RII_KKD_PREPARATION_BARCODE_SCAN");

            migrationBuilder.AddColumn<long>(
                name: "DistributionId",
                table: "RII_KKD_PREPARATION_BARCODE_SCAN",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RII_KKD_PREPARATION_BARCODE_SCAN_DistributionId",
                table: "RII_KKD_PREPARATION_BARCODE_SCAN",
                column: "DistributionId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_KKD_PREPARATION_BARCODE_SCAN_TaskLineId_DistributionId",
                table: "RII_KKD_PREPARATION_BARCODE_SCAN",
                columns: new[] { "TaskLineId", "DistributionId" },
                filter: "[IsDeleted] = 0 AND [DistributionId] IS NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_RII_KKD_PREPARATION_BARCODE_SCAN_RII_KKD_DISTRIBUTION_DistributionId",
                table: "RII_KKD_PREPARATION_BARCODE_SCAN",
                column: "DistributionId",
                principalTable: "RII_KKD_DISTRIBUTION",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RII_KKD_PREPARATION_BARCODE_SCAN_RII_KKD_DISTRIBUTION_DistributionId",
                table: "RII_KKD_PREPARATION_BARCODE_SCAN");

            migrationBuilder.DropIndex(
                name: "IX_RII_KKD_PREPARATION_BARCODE_SCAN_DistributionId",
                table: "RII_KKD_PREPARATION_BARCODE_SCAN");

            migrationBuilder.DropIndex(
                name: "IX_RII_KKD_PREPARATION_BARCODE_SCAN_TaskLineId_DistributionId",
                table: "RII_KKD_PREPARATION_BARCODE_SCAN");

            migrationBuilder.DropColumn(
                name: "DistributionId",
                table: "RII_KKD_PREPARATION_BARCODE_SCAN");

            migrationBuilder.CreateIndex(
                name: "IX_RII_KKD_PREPARATION_BARCODE_SCAN_TaskLineId",
                table: "RII_KKD_PREPARATION_BARCODE_SCAN",
                column: "TaskLineId");
        }
    }
}
