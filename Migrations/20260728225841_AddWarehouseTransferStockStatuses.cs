using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddWarehouseTransferStockStatuses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SourceStockStatus",
                table: "RII_WT_LINE",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "Available");

            migrationBuilder.AddColumn<string>(
                name: "TargetStockStatus",
                table: "RII_WT_LINE",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "Available");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SourceStockStatus",
                table: "RII_WT_LINE");

            migrationBuilder.DropColumn(
                name: "TargetStockStatus",
                table: "RII_WT_LINE");
        }
    }
}
