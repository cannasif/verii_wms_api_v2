using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using verii_wms_api_v2.Modules.Identity.Infrastructure;

#nullable disable

namespace verii_wms_api_v2.Migrations
{
    [DbContext(typeof(WmsDbContext))]
    [Migration("20260815220000_AddQualityInspectionPriorityAssignedAt")]
    public partial class AddQualityInspectionPriorityAssignedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PriorityAssignedAtUtc",
                table: "RII_QUALITY_INSPECTIONS",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RII_QUALITY_INSPECTIONS_BranchCode_IsPriority_PriorityAssignedAtUtc",
                table: "RII_QUALITY_INSPECTIONS",
                columns: new[] { "BranchCode", "IsPriority", "PriorityAssignedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RII_QUALITY_INSPECTIONS_BranchCode_IsPriority_PriorityAssignedAtUtc",
                table: "RII_QUALITY_INSPECTIONS");

            migrationBuilder.DropColumn(
                name: "PriorityAssignedAtUtc",
                table: "RII_QUALITY_INSPECTIONS");
        }
    }
}
