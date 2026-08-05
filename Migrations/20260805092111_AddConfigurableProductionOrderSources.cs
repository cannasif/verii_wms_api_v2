using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddConfigurableProductionOrderSources : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProductionOrderSource",
                table: "RII_PT_POLICIES",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "NetsisErpFunctions");

            migrationBuilder.AddColumn<string>(
                name: "WmsSourceSystemCode",
                table: "RII_PT_POLICIES",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "WINDBOX");

            migrationBuilder.CreateTable(
                name: "RII_PR_SOURCE_ORDER",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SourceSystemCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ExternalKey = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    WorkOrderNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RevisionNumber = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ProductCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ProductName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ConfigurationCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PlannedQuantity = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: false),
                    UnitCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SourceWarehouseCode = table.Column<int>(type: "int", nullable: false),
                    TargetWarehouseCode = table.Column<int>(type: "int", nullable: false),
                    WorkOrderDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeliveryDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ProjectCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SourceUpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PayloadHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
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
                    table.PrimaryKey("PK_RII_PR_SOURCE_ORDER", x => x.Id);
                    table.CheckConstraint("CK_RII_PR_SOURCE_ORDER_QTY_REV", "[PlannedQuantity] > 0 AND [RevisionNumber] > 0");
                });

            migrationBuilder.CreateTable(
                name: "RII_PR_SOURCE_RECIPE",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductionSourceWorkOrderId = table.Column<long>(type: "bigint", nullable: false),
                    LineNumber = table.Column<int>(type: "int", nullable: false),
                    OperationNumber = table.Column<int>(type: "int", nullable: false),
                    ComponentStockCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ComponentStockName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ComponentConfigurationCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UnitCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RecipeQuantity = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: false),
                    VariableWasteQuantity = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: false),
                    FixedWasteQuantity = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: false),
                    TotalRequiredQuantity = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: false),
                    IsMandatory = table.Column<bool>(type: "bit", nullable: false),
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
                    table.PrimaryKey("PK_RII_PR_SOURCE_RECIPE", x => x.Id);
                    table.CheckConstraint("CK_RII_PR_SOURCE_RECIPE_QTY", "[LineNumber] > 0 AND [OperationNumber] >= 0 AND [RecipeQuantity] > 0 AND [TotalRequiredQuantity] > 0 AND [VariableWasteQuantity] >= 0 AND [FixedWasteQuantity] >= 0");
                    table.ForeignKey(
                        name: "FK_RII_PR_SOURCE_RECIPE_RII_PR_SOURCE_ORDER_ProductionSourceWorkOrderId",
                        column: x => x.ProductionSourceWorkOrderId,
                        principalTable: "RII_PR_SOURCE_ORDER",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RII_PR_SOURCE_ORDER_BranchCode_SourceSystemCode_ExternalKey",
                table: "RII_PR_SOURCE_ORDER",
                columns: new[] { "BranchCode", "SourceSystemCode", "ExternalKey" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_PR_SOURCE_ORDER_BranchCode_SourceSystemCode_Status_SourceUpdatedAtUtc",
                table: "RII_PR_SOURCE_ORDER",
                columns: new[] { "BranchCode", "SourceSystemCode", "Status", "SourceUpdatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_PR_SOURCE_ORDER_BranchCode_SourceSystemCode_WorkOrderNumber_RevisionNumber",
                table: "RII_PR_SOURCE_ORDER",
                columns: new[] { "BranchCode", "SourceSystemCode", "WorkOrderNumber", "RevisionNumber" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_PR_SOURCE_ORDER_IsDeleted",
                table: "RII_PR_SOURCE_ORDER",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_PR_SOURCE_RECIPE_ComponentStockCode_ComponentConfigurationCode",
                table: "RII_PR_SOURCE_RECIPE",
                columns: new[] { "ComponentStockCode", "ComponentConfigurationCode" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_PR_SOURCE_RECIPE_IsDeleted",
                table: "RII_PR_SOURCE_RECIPE",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_PR_SOURCE_RECIPE_ProductionSourceWorkOrderId_LineNumber",
                table: "RII_PR_SOURCE_RECIPE",
                columns: new[] { "ProductionSourceWorkOrderId", "LineNumber" },
                unique: true,
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RII_PR_SOURCE_RECIPE");

            migrationBuilder.DropTable(
                name: "RII_PR_SOURCE_ORDER");

            migrationBuilder.DropColumn(
                name: "ProductionOrderSource",
                table: "RII_PT_POLICIES");

            migrationBuilder.DropColumn(
                name: "WmsSourceSystemCode",
                table: "RII_PT_POLICIES");
        }
    }
}
