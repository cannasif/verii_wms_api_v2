using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddWarehouseOutboundOperationMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProjectCode",
                table: "RII_WO_LINE",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CostCenterCode",
                table: "RII_WO_HEADER",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExitLocationCode",
                table: "RII_WO_HEADER",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MovementTypeCode",
                table: "RII_WO_HEADER",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProjectCode",
                table: "RII_WO_HEADER",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProjectCode",
                table: "RII_WO_LINE");

            migrationBuilder.DropColumn(
                name: "CostCenterCode",
                table: "RII_WO_HEADER");

            migrationBuilder.DropColumn(
                name: "ExitLocationCode",
                table: "RII_WO_HEADER");

            migrationBuilder.DropColumn(
                name: "MovementTypeCode",
                table: "RII_WO_HEADER");

            migrationBuilder.DropColumn(
                name: "ProjectCode",
                table: "RII_WO_HEADER");
        }
    }
}
