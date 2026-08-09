using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class AllowRepeatedSameDayVehicleCheckIns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RII_VEHICLE_CHECKIN_HEADER_BranchCode_PlateNoNormalized_BusinessDate",
                table: "RII_VEHICLE_CHECKIN_HEADER");

            migrationBuilder.CreateIndex(
                name: "IX_RII_VEHICLE_CHECKIN_HEADER_PlateHistory",
                table: "RII_VEHICLE_CHECKIN_HEADER",
                columns: new[] { "BranchCode", "PlateNoNormalized", "BusinessDate", "CheckedInAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RII_VEHICLE_CHECKIN_HEADER_PlateHistory",
                table: "RII_VEHICLE_CHECKIN_HEADER");

            migrationBuilder.CreateIndex(
                name: "IX_RII_VEHICLE_CHECKIN_HEADER_BranchCode_PlateNoNormalized_BusinessDate",
                table: "RII_VEHICLE_CHECKIN_HEADER",
                columns: new[] { "BranchCode", "PlateNoNormalized", "BusinessDate" },
                unique: true,
                filter: "[IsDeleted] = 0");
        }
    }
}
