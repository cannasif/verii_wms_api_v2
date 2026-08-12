using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddKkdPreparationTaskScanPick : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RII_KKD_PREPARATION_BARCODE_SCAN",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TaskId = table.Column<long>(type: "bigint", nullable: false),
                    TaskLineId = table.Column<long>(type: "bigint", nullable: false),
                    RequestLineId = table.Column<long>(type: "bigint", nullable: false),
                    IdempotencyKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BarcodeValue = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    NormalizedBarcode = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    BarcodeSource = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    StockId = table.Column<long>(type: "bigint", nullable: false),
                    UnitCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    LotNo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SerialNo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: false),
                    SourceLocationId = table.Column<long>(type: "bigint", nullable: true),
                    ScannedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
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
                    table.PrimaryKey("PK_RII_KKD_PREPARATION_BARCODE_SCAN", x => x.Id);
                    table.CheckConstraint("CK_RII_KKD_PREPARATION_BARCODE_SCAN_QTY", "[Quantity] > 0");
                    table.ForeignKey(
                        name: "FK_RII_KKD_PREPARATION_BARCODE_SCAN_RII_KKD_PREPARATION_TASK_LINE_TaskLineId",
                        column: x => x.TaskLineId,
                        principalTable: "RII_KKD_PREPARATION_TASK_LINE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_KKD_PREPARATION_BARCODE_SCAN_RII_KKD_PREPARATION_TASK_TaskId",
                        column: x => x.TaskId,
                        principalTable: "RII_KKD_PREPARATION_TASK",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RII_KKD_PREPARATION_BARCODE_SCAN_IdempotencyKey",
                table: "RII_KKD_PREPARATION_BARCODE_SCAN",
                column: "IdempotencyKey",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_KKD_PREPARATION_BARCODE_SCAN_IsDeleted",
                table: "RII_KKD_PREPARATION_BARCODE_SCAN",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_KKD_PREPARATION_BARCODE_SCAN_TaskId_NormalizedBarcode",
                table: "RII_KKD_PREPARATION_BARCODE_SCAN",
                columns: new[] { "TaskId", "NormalizedBarcode" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_KKD_PREPARATION_BARCODE_SCAN_TaskId_SerialNo",
                table: "RII_KKD_PREPARATION_BARCODE_SCAN",
                columns: new[] { "TaskId", "SerialNo" },
                filter: "[IsDeleted] = 0 AND [SerialNo] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RII_KKD_PREPARATION_BARCODE_SCAN_TaskId_TaskLineId_ScannedAtUtc",
                table: "RII_KKD_PREPARATION_BARCODE_SCAN",
                columns: new[] { "TaskId", "TaskLineId", "ScannedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_KKD_PREPARATION_BARCODE_SCAN_TaskLineId",
                table: "RII_KKD_PREPARATION_BARCODE_SCAN",
                column: "TaskLineId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RII_KKD_PREPARATION_BARCODE_SCAN");
        }
    }
}
