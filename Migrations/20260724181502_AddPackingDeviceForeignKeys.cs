using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddPackingDeviceForeignKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_RII_PACKING_PRINT_JOB_PackingStationId",
                table: "RII_PACKING_PRINT_JOB",
                column: "PackingStationId");

            migrationBuilder.AddForeignKey(
                name: "FK_RII_PACKING_PRINT_JOB_RII_HANDLING_UNIT_HandlingUnitId",
                table: "RII_PACKING_PRINT_JOB",
                column: "HandlingUnitId",
                principalTable: "RII_HANDLING_UNIT",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RII_PACKING_PRINT_JOB_RII_PACKING_STATION_PackingStationId",
                table: "RII_PACKING_PRINT_JOB",
                column: "PackingStationId",
                principalTable: "RII_PACKING_STATION",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RII_PACKING_SCALE_READING_RII_HANDLING_UNIT_HandlingUnitId",
                table: "RII_PACKING_SCALE_READING",
                column: "HandlingUnitId",
                principalTable: "RII_HANDLING_UNIT",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RII_PACKING_SCALE_READING_RII_PACKING_STATION_PackingStationId",
                table: "RII_PACKING_SCALE_READING",
                column: "PackingStationId",
                principalTable: "RII_PACKING_STATION",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RII_PACKING_PRINT_JOB_RII_HANDLING_UNIT_HandlingUnitId",
                table: "RII_PACKING_PRINT_JOB");

            migrationBuilder.DropForeignKey(
                name: "FK_RII_PACKING_PRINT_JOB_RII_PACKING_STATION_PackingStationId",
                table: "RII_PACKING_PRINT_JOB");

            migrationBuilder.DropForeignKey(
                name: "FK_RII_PACKING_SCALE_READING_RII_HANDLING_UNIT_HandlingUnitId",
                table: "RII_PACKING_SCALE_READING");

            migrationBuilder.DropForeignKey(
                name: "FK_RII_PACKING_SCALE_READING_RII_PACKING_STATION_PackingStationId",
                table: "RII_PACKING_SCALE_READING");

            migrationBuilder.DropIndex(
                name: "IX_RII_PACKING_PRINT_JOB_PackingStationId",
                table: "RII_PACKING_PRINT_JOB");
        }
    }
}
