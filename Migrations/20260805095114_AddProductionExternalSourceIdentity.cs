using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddProductionExternalSourceIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExternalSourceSystemCode",
                table: "RII_PR_ORDER",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RII_PR_ORDER_BranchCode_ExternalSourceSystemCode_ExternalOrderNo",
                table: "RII_PR_ORDER",
                columns: new[] { "BranchCode", "ExternalSourceSystemCode", "ExternalOrderNo" },
                filter: "[IsDeleted] = 0 AND [ExternalOrderNo] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RII_PR_ORDER_BranchCode_ExternalSourceSystemCode_ExternalOrderNo",
                table: "RII_PR_ORDER");

            migrationBuilder.DropColumn(
                name: "ExternalSourceSystemCode",
                table: "RII_PR_ORDER");
        }
    }
}
