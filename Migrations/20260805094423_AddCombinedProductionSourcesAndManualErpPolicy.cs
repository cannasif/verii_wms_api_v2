using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddCombinedProductionSourcesAndManualErpPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "RequireErpMasterDataForManualTransfer",
                table: "RII_PT_POLICIES",
                type: "bit",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RequireErpMasterDataForManualTransfer",
                table: "RII_PT_POLICIES");
        }
    }
}
