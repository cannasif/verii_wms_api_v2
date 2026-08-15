using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using verii_wms_api_v2.Modules.Identity.Infrastructure;

#nullable disable

namespace verii_wms_api_v2.Migrations
{
    [DbContext(typeof(WmsDbContext))]
    [Migration("20260815020000_AddUserNavbarAppearance")]
    public partial class AddUserNavbarAppearance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NavbarCenterMode",
                table: "RII_USER_DETAILS",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "search");

            migrationBuilder.AddColumn<string>(
                name: "NavbarKpiKeys",
                table: "RII_USER_DETAILS",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "myTasks,qualityQueue,pendingApproval,erpIssues");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NavbarCenterMode",
                table: "RII_USER_DETAILS");

            migrationBuilder.DropColumn(
                name: "NavbarKpiKeys",
                table: "RII_USER_DETAILS");
        }
    }
}
