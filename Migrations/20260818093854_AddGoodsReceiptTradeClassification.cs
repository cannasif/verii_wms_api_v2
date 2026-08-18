using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddGoodsReceiptTradeClassification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ImportFileNumber",
                table: "RII_GR_HEADER",
                type: "varchar(20)",
                unicode: false,
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TradeType",
                table: "RII_GR_HEADER",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Domestic");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ImportFileNumber",
                table: "RII_GR_HEADER");

            migrationBuilder.DropColumn(
                name: "TradeType",
                table: "RII_GR_HEADER");
        }
    }
}
