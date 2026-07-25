using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddGoodsReceiptLinePlanning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "TargetWarehouseId",
                table: "RII_GR_LINE",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            // Existing receipt lines inherit their header destination before the FK is created.
            migrationBuilder.Sql("""
                UPDATE [line]
                SET [line].[TargetWarehouseId] = [header].[TargetWarehouseId]
                FROM [RII_GR_LINE] AS [line]
                INNER JOIN [RII_GR_HEADER] AS [header] ON [header].[Id] = [line].[GrHeaderId]
                WHERE [line].[TargetWarehouseId] = 0;
                """);

            migrationBuilder.CreateTable(
                name: "RII_GR_TASK_LINE_TRACKING",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    GrTaskLineId = table.Column<long>(type: "bigint", nullable: false),
                    SequenceNo = table.Column<int>(type: "int", nullable: false),
                    StockId = table.Column<long>(type: "bigint", nullable: false),
                    PlannedQuantity = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    LotNo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SerialNo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ManufacturingDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ExpirationDate = table.Column<DateOnly>(type: "date", nullable: true),
                    TargetWarehouseId = table.Column<long>(type: "bigint", nullable: false),
                    ToLocationId = table.Column<long>(type: "bigint", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
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
                    table.PrimaryKey("PK_RII_GR_TASK_LINE_TRACKING", x => x.Id);
                    table.CheckConstraint("CK_RII_GR_TASK_LINE_TRACKING_QTY", "[PlannedQuantity] > 0");
                    table.CheckConstraint("CK_RII_GR_TASK_LINE_TRACKING_SEQUENCE", "[SequenceNo] > 0");
                    table.ForeignKey(
                        name: "FK_RII_GR_TASK_LINE_TRACKING_RII_GR_TASK_LINE_GrTaskLineId",
                        column: x => x.GrTaskLineId,
                        principalTable: "RII_GR_TASK_LINE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_GR_TASK_LINE_TRACKING_RII_LOCATION_ToLocationId",
                        column: x => x.ToLocationId,
                        principalTable: "RII_LOCATION",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_GR_TASK_LINE_TRACKING_RII_STOCK_StockId",
                        column: x => x.StockId,
                        principalTable: "RII_STOCK",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_GR_TASK_LINE_TRACKING_RII_WAREHOUSE_TargetWarehouseId",
                        column: x => x.TargetWarehouseId,
                        principalTable: "RII_WAREHOUSE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RII_GR_LINE_TARGET_WAREHOUSE_STATUS_STOCK",
                table: "RII_GR_LINE",
                columns: new[] { "TargetWarehouseId", "Status", "StockId" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_GR_TASK_LINE_TRACKING_IsDeleted",
                table: "RII_GR_TASK_LINE_TRACKING",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_GR_TASK_LINE_TRACKING_TargetWarehouseId",
                table: "RII_GR_TASK_LINE_TRACKING",
                column: "TargetWarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_GR_TASK_LINE_TRACKING_ToLocationId",
                table: "RII_GR_TASK_LINE_TRACKING",
                column: "ToLocationId");

            migrationBuilder.CreateIndex(
                name: "UX_RII_GR_TASK_LINE_TRACKING_SEQUENCE",
                table: "RII_GR_TASK_LINE_TRACKING",
                columns: new[] { "GrTaskLineId", "SequenceNo" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_RII_GR_TASK_LINE_TRACKING_SERIAL",
                table: "RII_GR_TASK_LINE_TRACKING",
                columns: new[] { "GrTaskLineId", "SerialNo" },
                unique: true,
                filter: "[IsDeleted] = 0 AND [SerialNo] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_RII_GR_TASK_LINE_TRACKING_STOCK_SERIAL",
                table: "RII_GR_TASK_LINE_TRACKING",
                columns: new[] { "StockId", "SerialNo" },
                unique: true,
                filter: "[IsDeleted] = 0 AND [SerialNo] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_RII_GR_LINE_RII_WAREHOUSE_TargetWarehouseId",
                table: "RII_GR_LINE",
                column: "TargetWarehouseId",
                principalTable: "RII_WAREHOUSE",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RII_GR_LINE_RII_WAREHOUSE_TargetWarehouseId",
                table: "RII_GR_LINE");

            migrationBuilder.DropTable(
                name: "RII_GR_TASK_LINE_TRACKING");

            migrationBuilder.DropIndex(
                name: "IX_RII_GR_LINE_TARGET_WAREHOUSE_STATUS_STOCK",
                table: "RII_GR_LINE");

            migrationBuilder.DropColumn(
                name: "TargetWarehouseId",
                table: "RII_GR_LINE");
        }
    }
}
