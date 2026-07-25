using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddHangfireExecutionLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RII_HANGFIRE_EXECUTION_LOGS",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    JobKey = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    HangfireJobId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    TriggerSource = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DurationMs = table.Column<long>(type: "bigint", nullable: true),
                    SourceCount = table.Column<int>(type: "int", nullable: true),
                    InsertedCount = table.Column<int>(type: "int", nullable: true),
                    UpdatedCount = table.Column<int>(type: "int", nullable: true),
                    DeactivatedCount = table.Column<int>(type: "int", nullable: true),
                    ResultSummary = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ErrorType = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    StackTrace = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    BranchCode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false, defaultValue: "0"),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    DeletedBy = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RII_HANGFIRE_EXECUTION_LOGS", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RII_HANGFIRE_EXECUTION_LOGS_IsDeleted",
                table: "RII_HANGFIRE_EXECUTION_LOGS",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_HANGFIRE_EXECUTION_LOGS_JobKey_Status",
                table: "RII_HANGFIRE_EXECUTION_LOGS",
                columns: new[] { "JobKey", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_HANGFIRE_EXECUTION_LOGS_StartedAt",
                table: "RII_HANGFIRE_EXECUTION_LOGS",
                column: "StartedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RII_HANGFIRE_EXECUTION_LOGS");
        }
    }
}
