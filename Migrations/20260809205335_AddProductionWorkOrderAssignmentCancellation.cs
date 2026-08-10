using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddProductionWorkOrderAssignmentCancellation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RII_PR_WO_ASSIGN_CANCEL",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorkOrderNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SourceType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    SourceSystemCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CorrelationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CancelledAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CancelledBy = table.Column<long>(type: "bigint", nullable: false),
                    RestoredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RestoredBy = table.Column<long>(type: "bigint", nullable: true),
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
                    table.PrimaryKey("PK_RII_PR_WO_ASSIGN_CANCEL", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RII_PR_WO_ASSIGN_CANCEL_LINE",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CancellationId = table.Column<long>(type: "bigint", nullable: false),
                    StockId = table.Column<long>(type: "bigint", nullable: true),
                    YapCodeId = table.Column<long>(type: "bigint", nullable: true),
                    OperationNumber = table.Column<int>(type: "int", nullable: false),
                    CancelledQuantity = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: false),
                    SourceTransferHeaderId = table.Column<long>(type: "bigint", nullable: true),
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
                    table.PrimaryKey("PK_RII_PR_WO_ASSIGN_CANCEL_LINE", x => x.Id);
                    table.CheckConstraint("CK_RII_PR_WO_ASSIGN_CANCEL_LINE_QTY", "[CancelledQuantity] > 0 AND [OperationNumber] >= 0");
                    table.ForeignKey(
                        name: "FK_RII_PR_WO_ASSIGN_CANCEL_LINE_RII_PR_WO_ASSIGN_CANCEL_CancellationId",
                        column: x => x.CancellationId,
                        principalTable: "RII_PR_WO_ASSIGN_CANCEL",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RII_PR_WO_ASSIGN_CANCEL_BranchCode_CancelledAtUtc",
                table: "RII_PR_WO_ASSIGN_CANCEL",
                columns: new[] { "BranchCode", "CancelledAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_PR_WO_ASSIGN_CANCEL_BranchCode_WorkOrderNumber_Status",
                table: "RII_PR_WO_ASSIGN_CANCEL",
                columns: new[] { "BranchCode", "WorkOrderNumber", "Status" },
                unique: true,
                filter: "[IsDeleted] = 0 AND [Status] = 'Active'");

            migrationBuilder.CreateIndex(
                name: "IX_RII_PR_WO_ASSIGN_CANCEL_IsDeleted",
                table: "RII_PR_WO_ASSIGN_CANCEL",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_PR_WO_ASSIGN_CANCEL_LINE_CancellationId_StockId_YapCodeId_OperationNumber",
                table: "RII_PR_WO_ASSIGN_CANCEL_LINE",
                columns: new[] { "CancellationId", "StockId", "YapCodeId", "OperationNumber" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_PR_WO_ASSIGN_CANCEL_LINE_IsDeleted",
                table: "RII_PR_WO_ASSIGN_CANCEL_LINE",
                column: "IsDeleted");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RII_PR_WO_ASSIGN_CANCEL_LINE");

            migrationBuilder.DropTable(
                name: "RII_PR_WO_ASSIGN_CANCEL");
        }
    }
}
