using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddKkdPreparationTaskLineLocationAndScanReversal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "KkdPickingStagingLocationId",
                table: "RII_WAREHOUSE",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsReversed",
                table: "RII_KKD_PREPARATION_BARCODE_SCAN",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "StockMovementOperationId",
                table: "RII_KKD_PREPARATION_BARCODE_SCAN",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RII_KKD_PREPARATION_TASK_LINE_LOCATION",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TaskLineId = table.Column<long>(type: "bigint", nullable: false),
                    LocationId = table.Column<long>(type: "bigint", nullable: false),
                    ReservedQuantity = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: false),
                    PickedQuantity = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: false),
                    SerialNo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    LotNo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
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
                    table.PrimaryKey("PK_RII_KKD_PREPARATION_TASK_LINE_LOCATION", x => x.Id);
                    table.CheckConstraint("CK_RII_KKD_PREP_TASK_LINE_LOCATION_QTY", "[ReservedQuantity] >= 0 AND [PickedQuantity] >= 0");
                    table.ForeignKey(
                        name: "FK_RII_KKD_PREPARATION_TASK_LINE_LOCATION_RII_KKD_PREPARATION_TASK_LINE_TaskLineId",
                        column: x => x.TaskLineId,
                        principalTable: "RII_KKD_PREPARATION_TASK_LINE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_KKD_PREPARATION_TASK_LINE_LOCATION_RII_LOCATION_LocationId",
                        column: x => x.LocationId,
                        principalTable: "RII_LOCATION",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RII_WAREHOUSE_KKD_PICKING_STAGING_LOCATION",
                table: "RII_WAREHOUSE",
                column: "KkdPickingStagingLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_KKD_PREPARATION_TASK_LINE_LOCATION_IsDeleted",
                table: "RII_KKD_PREPARATION_TASK_LINE_LOCATION",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_KKD_PREPARATION_TASK_LINE_LOCATION_LocationId",
                table: "RII_KKD_PREPARATION_TASK_LINE_LOCATION",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_KKD_PREPARATION_TASK_LINE_LOCATION_TaskLineId_LocationId_SerialNo",
                table: "RII_KKD_PREPARATION_TASK_LINE_LOCATION",
                columns: new[] { "TaskLineId", "LocationId", "SerialNo" },
                filter: "[IsDeleted] = 0");

            migrationBuilder.AddForeignKey(
                name: "FK_RII_WAREHOUSE_RII_LOCATION_KkdPickingStagingLocationId",
                table: "RII_WAREHOUSE",
                column: "KkdPickingStagingLocationId",
                principalTable: "RII_LOCATION",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RII_WAREHOUSE_RII_LOCATION_KkdPickingStagingLocationId",
                table: "RII_WAREHOUSE");

            migrationBuilder.DropTable(
                name: "RII_KKD_PREPARATION_TASK_LINE_LOCATION");

            migrationBuilder.DropIndex(
                name: "IX_RII_WAREHOUSE_KKD_PICKING_STAGING_LOCATION",
                table: "RII_WAREHOUSE");

            migrationBuilder.DropColumn(
                name: "KkdPickingStagingLocationId",
                table: "RII_WAREHOUSE");

            migrationBuilder.DropColumn(
                name: "IsReversed",
                table: "RII_KKD_PREPARATION_BARCODE_SCAN");

            migrationBuilder.DropColumn(
                name: "StockMovementOperationId",
                table: "RII_KKD_PREPARATION_BARCODE_SCAN");
        }
    }
}
