using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class FilterSoftDeletedTaskAssignmentUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RII_WT_TASK_ASSIGNMENT_WtTaskId_UserId",
                table: "RII_WT_TASK_ASSIGNMENT");

            migrationBuilder.CreateIndex(
                name: "IX_RII_WT_TASK_ASSIGNMENT_WtTaskId_UserId",
                table: "RII_WT_TASK_ASSIGNMENT",
                columns: new[] { "WtTaskId", "UserId" },
                unique: true,
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RII_WT_TASK_ASSIGNMENT_WtTaskId_UserId",
                table: "RII_WT_TASK_ASSIGNMENT");

            migrationBuilder.CreateIndex(
                name: "IX_RII_WT_TASK_ASSIGNMENT_WtTaskId_UserId",
                table: "RII_WT_TASK_ASSIGNMENT",
                columns: new[] { "WtTaskId", "UserId" },
                unique: true);
        }
    }
}
