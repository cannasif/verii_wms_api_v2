using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddErpStockBalanceAuthoritySync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RII_ERP_STOCK_BALANCE_SYNC_RUN",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RunKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Mode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    TriggerSource = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    WarehouseCode = table.Column<int>(type: "int", nullable: true),
                    StockCode = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    TriggerReference = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DurationMs = table.Column<long>(type: "bigint", nullable: true),
                    SourceCount = table.Column<int>(type: "int", nullable: false),
                    InsertedCount = table.Column<int>(type: "int", nullable: false),
                    UpdatedCount = table.Column<int>(type: "int", nullable: false),
                    UnchangedCount = table.Column<int>(type: "int", nullable: false),
                    MissingCount = table.Column<int>(type: "int", nullable: false),
                    DifferenceCount = table.Column<int>(type: "int", nullable: false),
                    UnmappedCount = table.Column<int>(type: "int", nullable: false),
                    ErrorType = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
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
                    table.PrimaryKey("PK_RII_ERP_STOCK_BALANCE_SYNC_RUN", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RII_ERP_STOCK_BALANCE_CHANGE_LOG",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SyncRunId = table.Column<long>(type: "bigint", nullable: false),
                    WarehouseCode = table.Column<int>(type: "int", nullable: false),
                    StockCode = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    WarehouseId = table.Column<long>(type: "bigint", nullable: true),
                    StockId = table.Column<long>(type: "bigint", nullable: true),
                    PreviousErpQuantity = table.Column<decimal>(type: "decimal(38,8)", precision: 38, scale: 8, nullable: true),
                    CurrentErpQuantity = table.Column<decimal>(type: "decimal(38,8)", precision: 38, scale: 8, nullable: false),
                    PreviousWmsQuantity = table.Column<decimal>(type: "decimal(38,8)", precision: 38, scale: 8, nullable: false),
                    CurrentWmsQuantity = table.Column<decimal>(type: "decimal(38,8)", precision: 38, scale: 8, nullable: false),
                    Difference = table.Column<decimal>(type: "decimal(38,8)", precision: 38, scale: 8, nullable: false),
                    ChangeType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ReasonCode = table.Column<string>(type: "varchar(60)", unicode: false, maxLength: 60, nullable: false),
                    ObservedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_RII_ERP_STOCK_BALANCE_CHANGE_LOG", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RII_ERP_STOCK_BALANCE_CHANGE_LOG_RII_ERP_STOCK_BALANCE_SYNC_RUN_SyncRunId",
                        column: x => x.SyncRunId,
                        principalTable: "RII_ERP_STOCK_BALANCE_SYNC_RUN",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_ERP_WAREHOUSE_STOCK_BALANCE",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WarehouseCode = table.Column<int>(type: "int", nullable: false),
                    StockCode = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    WarehouseId = table.Column<long>(type: "bigint", nullable: true),
                    StockId = table.Column<long>(type: "bigint", nullable: true),
                    UnitCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ErpQuantity = table.Column<decimal>(type: "decimal(38,8)", precision: 38, scale: 8, nullable: false),
                    WmsQuantityAtSync = table.Column<decimal>(type: "decimal(38,8)", precision: 38, scale: 8, nullable: false),
                    Difference = table.Column<decimal>(type: "decimal(38,8)", precision: 38, scale: 8, nullable: false),
                    MappingStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    IsMissingInErp = table.Column<bool>(type: "bit", nullable: false),
                    FirstObservedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastChangedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastSyncRunId = table.Column<long>(type: "bigint", nullable: false),
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
                    table.PrimaryKey("PK_RII_ERP_WAREHOUSE_STOCK_BALANCE", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RII_ERP_WAREHOUSE_STOCK_BALANCE_RII_ERP_STOCK_BALANCE_SYNC_RUN_LastSyncRunId",
                        column: x => x.LastSyncRunId,
                        principalTable: "RII_ERP_STOCK_BALANCE_SYNC_RUN",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_ERP_WAREHOUSE_STOCK_BALANCE_RII_STOCK_StockId",
                        column: x => x.StockId,
                        principalTable: "RII_STOCK",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_ERP_WAREHOUSE_STOCK_BALANCE_RII_WAREHOUSE_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "RII_WAREHOUSE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RII_ERP_BALANCE_CHANGE_RUN_TYPE",
                table: "RII_ERP_STOCK_BALANCE_CHANGE_LOG",
                columns: new[] { "SyncRunId", "ChangeType" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_ERP_BALANCE_CHANGE_SOURCE_DATE",
                table: "RII_ERP_STOCK_BALANCE_CHANGE_LOG",
                columns: new[] { "WarehouseCode", "StockCode", "ObservedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_ERP_STOCK_BALANCE_CHANGE_LOG_IsDeleted",
                table: "RII_ERP_STOCK_BALANCE_CHANGE_LOG",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_ERP_BALANCE_SYNC_RUN_STATUS_STARTED",
                table: "RII_ERP_STOCK_BALANCE_SYNC_RUN",
                columns: new[] { "Status", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_ERP_STOCK_BALANCE_SYNC_RUN_IsDeleted",
                table: "RII_ERP_STOCK_BALANCE_SYNC_RUN",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "UX_RII_ERP_BALANCE_SYNC_RUN_KEY",
                table: "RII_ERP_STOCK_BALANCE_SYNC_RUN",
                column: "RunKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RII_ERP_BALANCE_RECONCILIATION",
                table: "RII_ERP_WAREHOUSE_STOCK_BALANCE",
                columns: new[] { "IsMissingInErp", "MappingStatus", "Difference" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_ERP_BALANCE_WMS_DIMENSION",
                table: "RII_ERP_WAREHOUSE_STOCK_BALANCE",
                columns: new[] { "WarehouseId", "StockId" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_ERP_WAREHOUSE_STOCK_BALANCE_IsDeleted",
                table: "RII_ERP_WAREHOUSE_STOCK_BALANCE",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_ERP_WAREHOUSE_STOCK_BALANCE_LastSyncRunId",
                table: "RII_ERP_WAREHOUSE_STOCK_BALANCE",
                column: "LastSyncRunId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_ERP_WAREHOUSE_STOCK_BALANCE_StockId",
                table: "RII_ERP_WAREHOUSE_STOCK_BALANCE",
                column: "StockId");

            migrationBuilder.CreateIndex(
                name: "UX_RII_ERP_WAREHOUSE_STOCK_BALANCE_SOURCE",
                table: "RII_ERP_WAREHOUSE_STOCK_BALANCE",
                columns: new[] { "WarehouseCode", "StockCode" },
                unique: true,
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RII_ERP_STOCK_BALANCE_CHANGE_LOG");

            migrationBuilder.DropTable(
                name: "RII_ERP_WAREHOUSE_STOCK_BALANCE");

            migrationBuilder.DropTable(
                name: "RII_ERP_STOCK_BALANCE_SYNC_RUN");
        }
    }
}
