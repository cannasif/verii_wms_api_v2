using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddKkdPreparationTasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RII_KKD_PREPARATION_TASK",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CorrelationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestId = table.Column<long>(type: "bigint", nullable: false),
                    TaskNo = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    AssignedUserId = table.Column<long>(type: "bigint", nullable: false),
                    WarehouseId = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    PreviousTaskId = table.Column<long>(type: "bigint", nullable: true),
                    OriginUserId = table.Column<long>(type: "bigint", nullable: true),
                    DistributionId = table.Column<long>(type: "bigint", nullable: true),
                    AssignedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ClosedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ClosureReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
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
                    table.PrimaryKey("PK_RII_KKD_PREPARATION_TASK", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RII_KKD_PREPARATION_TASK_RII_KKD_DISTRIBUTION_DistributionId",
                        column: x => x.DistributionId,
                        principalTable: "RII_KKD_DISTRIBUTION",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_KKD_PREPARATION_TASK_RII_KKD_PREPARATION_TASK_PreviousTaskId",
                        column: x => x.PreviousTaskId,
                        principalTable: "RII_KKD_PREPARATION_TASK",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_KKD_PREPARATION_TASK_RII_KKD_REQUEST_RequestId",
                        column: x => x.RequestId,
                        principalTable: "RII_KKD_REQUEST",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_KKD_PREPARATION_TASK_RII_USERS_AssignedUserId",
                        column: x => x.AssignedUserId,
                        principalTable: "RII_USERS",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_KKD_PREPARATION_TASK_RII_WAREHOUSE_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "RII_WAREHOUSE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_KKD_PREPARATION_TASK_LINE",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TaskId = table.Column<long>(type: "bigint", nullable: false),
                    RequestLineId = table.Column<long>(type: "bigint", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: false),
                    PreparedQuantity = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: false),
                    DeliveredQuantity = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
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
                    table.PrimaryKey("PK_RII_KKD_PREPARATION_TASK_LINE", x => x.Id);
                    table.CheckConstraint("CK_RII_KKD_PREPARATION_TASK_LINE_QTY", "[Quantity] > 0 AND [PreparedQuantity] >= 0 AND [DeliveredQuantity] >= 0");
                    table.ForeignKey(
                        name: "FK_RII_KKD_PREPARATION_TASK_LINE_RII_KKD_PREPARATION_TASK_TaskId",
                        column: x => x.TaskId,
                        principalTable: "RII_KKD_PREPARATION_TASK",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_KKD_PREPARATION_TASK_LINE_RII_KKD_REQUEST_LINE_RequestLineId",
                        column: x => x.RequestLineId,
                        principalTable: "RII_KKD_REQUEST_LINE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RII_KKD_PREPARATION_TASK_AssignedUserId",
                table: "RII_KKD_PREPARATION_TASK",
                column: "AssignedUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_KKD_PREPARATION_TASK_BranchCode_AssignedUserId_Status",
                table: "RII_KKD_PREPARATION_TASK",
                columns: new[] { "BranchCode", "AssignedUserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_KKD_PREPARATION_TASK_BranchCode_TaskNo",
                table: "RII_KKD_PREPARATION_TASK",
                columns: new[] { "BranchCode", "TaskNo" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_KKD_PREPARATION_TASK_CorrelationId",
                table: "RII_KKD_PREPARATION_TASK",
                column: "CorrelationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RII_KKD_PREPARATION_TASK_DistributionId",
                table: "RII_KKD_PREPARATION_TASK",
                column: "DistributionId",
                filter: "[DistributionId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RII_KKD_PREPARATION_TASK_IsDeleted",
                table: "RII_KKD_PREPARATION_TASK",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_KKD_PREPARATION_TASK_PreviousTaskId",
                table: "RII_KKD_PREPARATION_TASK",
                column: "PreviousTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_KKD_PREPARATION_TASK_RequestId_Status",
                table: "RII_KKD_PREPARATION_TASK",
                columns: new[] { "RequestId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_KKD_PREPARATION_TASK_WarehouseId",
                table: "RII_KKD_PREPARATION_TASK",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_KKD_PREPARATION_TASK_LINE_IsDeleted",
                table: "RII_KKD_PREPARATION_TASK_LINE",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_KKD_PREPARATION_TASK_LINE_RequestLineId",
                table: "RII_KKD_PREPARATION_TASK_LINE",
                column: "RequestLineId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_KKD_PREPARATION_TASK_LINE_TaskId_RequestLineId",
                table: "RII_KKD_PREPARATION_TASK_LINE",
                columns: new[] { "TaskId", "RequestLineId" },
                unique: true,
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RII_KKD_PREPARATION_TASK_LINE");

            migrationBuilder.DropTable(
                name: "RII_KKD_PREPARATION_TASK");
        }
    }
}
