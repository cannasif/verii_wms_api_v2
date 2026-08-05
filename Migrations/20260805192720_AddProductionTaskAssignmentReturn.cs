using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddProductionTaskAssignmentReturn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "OriginTaskId",
                table: "RII_WT_TASK",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "OriginUserId",
                table: "RII_WT_TASK",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "PreviousTaskId",
                table: "RII_WT_TASK",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RII_WT_TASK_OriginTaskId",
                table: "RII_WT_TASK",
                column: "OriginTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_WT_TASK_PreviousTaskId",
                table: "RII_WT_TASK",
                column: "PreviousTaskId");

            migrationBuilder.AddForeignKey(
                name: "FK_RII_WT_TASK_RII_WT_TASK_OriginTaskId",
                table: "RII_WT_TASK",
                column: "OriginTaskId",
                principalTable: "RII_WT_TASK",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RII_WT_TASK_RII_WT_TASK_PreviousTaskId",
                table: "RII_WT_TASK",
                column: "PreviousTaskId",
                principalTable: "RII_WT_TASK",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RII_WT_TASK_RII_WT_TASK_OriginTaskId",
                table: "RII_WT_TASK");

            migrationBuilder.DropForeignKey(
                name: "FK_RII_WT_TASK_RII_WT_TASK_PreviousTaskId",
                table: "RII_WT_TASK");

            migrationBuilder.DropIndex(
                name: "IX_RII_WT_TASK_OriginTaskId",
                table: "RII_WT_TASK");

            migrationBuilder.DropIndex(
                name: "IX_RII_WT_TASK_PreviousTaskId",
                table: "RII_WT_TASK");

            migrationBuilder.DropColumn(
                name: "OriginTaskId",
                table: "RII_WT_TASK");

            migrationBuilder.DropColumn(
                name: "OriginUserId",
                table: "RII_WT_TASK");

            migrationBuilder.DropColumn(
                name: "PreviousTaskId",
                table: "RII_WT_TASK");
        }
    }
}
