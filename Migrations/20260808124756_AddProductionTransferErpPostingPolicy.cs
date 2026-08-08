using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddProductionTransferErpPostingPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ErpPostingPolicy",
                table: "RII_PT_POLICIES",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "AfterHandover");

            migrationBuilder.AddColumn<string>(
                name: "ErpPostingPolicy",
                table: "RII_PT_HEADER_LINK",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "AfterHandover");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ErpPostingPolicy",
                table: "RII_PT_POLICIES");

            migrationBuilder.DropColumn(
                name: "ErpPostingPolicy",
                table: "RII_PT_HEADER_LINK");
        }
    }
}
