using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class FilterSoftDeletedTaskLineUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RII_WT_TASK_LINE_WtTaskId_WtLineId",
                table: "RII_WT_TASK_LINE");

            migrationBuilder.CreateIndex(
                name: "IX_RII_WT_TASK_LINE_WtTaskId_WtLineId",
                table: "RII_WT_TASK_LINE",
                columns: new[] { "WtTaskId", "WtLineId" },
                unique: true,
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RII_WT_TASK_LINE_WtTaskId_WtLineId",
                table: "RII_WT_TASK_LINE");

            migrationBuilder.CreateIndex(
                name: "IX_RII_WT_TASK_LINE_WtTaskId_WtLineId",
                table: "RII_WT_TASK_LINE",
                columns: new[] { "WtTaskId", "WtLineId" },
                unique: true);
        }
    }
}
