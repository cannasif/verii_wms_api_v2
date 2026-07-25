using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class EnforceCentralStockTrackingOnOutbound : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "ExpirationDate",
                table: "RII_WO_TRACKING",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ManufacturingDate",
                table: "RII_WO_TRACKING",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ExpirationDate",
                table: "RII_SH_TRACKING",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ManufacturingDate",
                table: "RII_SH_TRACKING",
                type: "date",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExpirationDate",
                table: "RII_WO_TRACKING");

            migrationBuilder.DropColumn(
                name: "ManufacturingDate",
                table: "RII_WO_TRACKING");

            migrationBuilder.DropColumn(
                name: "ExpirationDate",
                table: "RII_SH_TRACKING");

            migrationBuilder.DropColumn(
                name: "ManufacturingDate",
                table: "RII_SH_TRACKING");
        }
    }
}
