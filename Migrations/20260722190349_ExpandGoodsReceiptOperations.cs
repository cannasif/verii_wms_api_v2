using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class ExpandGoodsReceiptOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RII_GR_EXECUTION",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    GrHeaderId = table.Column<long>(type: "bigint", nullable: false),
                    GrTaskId = table.Column<long>(type: "bigint", nullable: true),
                    IdempotencyKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    ExecutionNo = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Mode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    StockMovementOperationId = table.Column<long>(type: "bigint", nullable: true),
                    DeviceId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ReversalOfExecutionId = table.Column<long>(type: "bigint", nullable: true),
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
                    table.PrimaryKey("PK_RII_GR_EXECUTION", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RII_GR_EXECUTION_RII_GR_EXECUTION_ReversalOfExecutionId",
                        column: x => x.ReversalOfExecutionId,
                        principalTable: "RII_GR_EXECUTION",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_GR_EXECUTION_RII_GR_HEADER_GrHeaderId",
                        column: x => x.GrHeaderId,
                        principalTable: "RII_GR_HEADER",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_GR_EXECUTION_RII_GR_TASK_GrTaskId",
                        column: x => x.GrTaskId,
                        principalTable: "RII_GR_TASK",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_GR_EXECUTION_RII_STOCK_MOVEMENT_OPERATION_StockMovementOperationId",
                        column: x => x.StockMovementOperationId,
                        principalTable: "RII_STOCK_MOVEMENT_OPERATION",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_GR_EXECUTION_LINE",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    GrExecutionId = table.Column<long>(type: "bigint", nullable: false),
                    GrLineId = table.Column<long>(type: "bigint", nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    StockId = table.Column<long>(type: "bigint", nullable: false),
                    YapCodeId = table.Column<long>(type: "bigint", nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    UnitCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    LotNo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SerialNo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ManufacturingDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ExpirationDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ScannedBarcode = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    WarehouseId = table.Column<long>(type: "bigint", nullable: false),
                    LocationId = table.Column<long>(type: "bigint", nullable: false),
                    StockStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    GoodsReceiptLabelId = table.Column<long>(type: "bigint", nullable: true),
                    QualityInspectionLineId = table.Column<long>(type: "bigint", nullable: true),
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
                    table.PrimaryKey("PK_RII_GR_EXECUTION_LINE", x => x.Id);
                    table.CheckConstraint("CK_RII_GR_EXECUTION_LINE_NO", "[LineNo] > 0");
                    table.CheckConstraint("CK_RII_GR_EXECUTION_LINE_QTY", "[Quantity] > 0");
                    table.ForeignKey(
                        name: "FK_RII_GR_EXECUTION_LINE_RII_GR_EXECUTION_GrExecutionId",
                        column: x => x.GrExecutionId,
                        principalTable: "RII_GR_EXECUTION",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_GR_EXECUTION_LINE_RII_GR_LABEL_GoodsReceiptLabelId",
                        column: x => x.GoodsReceiptLabelId,
                        principalTable: "RII_GR_LABEL",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_GR_EXECUTION_LINE_RII_GR_LINE_GrLineId",
                        column: x => x.GrLineId,
                        principalTable: "RII_GR_LINE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_GR_EXECUTION_LINE_RII_LOCATION_LocationId",
                        column: x => x.LocationId,
                        principalTable: "RII_LOCATION",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_GR_EXECUTION_LINE_RII_QUALITY_INSPECTION_LINES_QualityInspectionLineId",
                        column: x => x.QualityInspectionLineId,
                        principalTable: "RII_QUALITY_INSPECTION_LINES",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_GR_EXECUTION_LINE_RII_STOCK_StockId",
                        column: x => x.StockId,
                        principalTable: "RII_STOCK",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_GR_EXECUTION_LINE_RII_WAREHOUSE_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "RII_WAREHOUSE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_GR_EXECUTION_LINE_RII_YAP_CODE_YapCodeId",
                        column: x => x.YapCodeId,
                        principalTable: "RII_YAP_CODE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RII_GR_EXECUTION_GrTaskId",
                table: "RII_GR_EXECUTION",
                column: "GrTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_GR_EXECUTION_HEADER_TIME",
                table: "RII_GR_EXECUTION",
                columns: new[] { "GrHeaderId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_GR_EXECUTION_IsDeleted",
                table: "RII_GR_EXECUTION",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_GR_EXECUTION_ReversalOfExecutionId",
                table: "RII_GR_EXECUTION",
                column: "ReversalOfExecutionId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_GR_EXECUTION_StockMovementOperationId",
                table: "RII_GR_EXECUTION",
                column: "StockMovementOperationId");

            migrationBuilder.CreateIndex(
                name: "UX_RII_GR_EXECUTION_BRANCH_NO",
                table: "RII_GR_EXECUTION",
                columns: new[] { "BranchCode", "ExecutionNo" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_RII_GR_EXECUTION_IDEMPOTENCY",
                table: "RII_GR_EXECUTION",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RII_GR_EXECUTION_LINE_GoodsReceiptLabelId",
                table: "RII_GR_EXECUTION_LINE",
                column: "GoodsReceiptLabelId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_GR_EXECUTION_LINE_GR_LINE",
                table: "RII_GR_EXECUTION_LINE",
                columns: new[] { "GrLineId", "GrExecutionId" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_GR_EXECUTION_LINE_IsDeleted",
                table: "RII_GR_EXECUTION_LINE",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_GR_EXECUTION_LINE_LocationId",
                table: "RII_GR_EXECUTION_LINE",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_GR_EXECUTION_LINE_QualityInspectionLineId",
                table: "RII_GR_EXECUTION_LINE",
                column: "QualityInspectionLineId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_GR_EXECUTION_LINE_TRACE",
                table: "RII_GR_EXECUTION_LINE",
                columns: new[] { "StockId", "LotNo", "SerialNo" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_GR_EXECUTION_LINE_WarehouseId",
                table: "RII_GR_EXECUTION_LINE",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_GR_EXECUTION_LINE_YapCodeId",
                table: "RII_GR_EXECUTION_LINE",
                column: "YapCodeId");

            migrationBuilder.CreateIndex(
                name: "UX_RII_GR_EXECUTION_LINE_SEQUENCE",
                table: "RII_GR_EXECUTION_LINE",
                columns: new[] { "GrExecutionId", "LineNo" },
                unique: true,
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RII_GR_EXECUTION_LINE");

            migrationBuilder.DropTable(
                name: "RII_GR_EXECUTION");
        }
    }
}
