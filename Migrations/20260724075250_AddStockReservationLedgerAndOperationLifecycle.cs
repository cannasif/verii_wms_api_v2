using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddStockReservationLedgerAndOperationLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_RII_SH_TRACKING_QTY",
                table: "RII_SH_TRACKING");

            migrationBuilder.AddColumn<decimal>(
                name: "ReservedQuantity",
                table: "RII_SH_TRACKING",
                type: "decimal(20,6)",
                precision: 20,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "RII_STOCK_RESERVATION_OPERATIONS",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IdempotencyKey = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    RequestHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    ReferenceType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ReferenceId = table.Column<long>(type: "bigint", nullable: false),
                    ReferenceNo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    OperationType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_RII_STOCK_RESERVATION_OPERATIONS", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RII_STOCK_RESERVATION_ENTRIES",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OperationId = table.Column<long>(type: "bigint", nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    ReferenceLineId = table.Column<long>(type: "bigint", nullable: false),
                    WarehouseId = table.Column<long>(type: "bigint", nullable: false),
                    LocationId = table.Column<long>(type: "bigint", nullable: false),
                    StockId = table.Column<long>(type: "bigint", nullable: false),
                    YapCodeId = table.Column<long>(type: "bigint", nullable: true),
                    UnitCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    LotNo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SerialNo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    StockStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    QuantityDelta = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_RII_STOCK_RESERVATION_ENTRIES", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RII_STOCK_RESERVATION_ENTRIES_RII_STOCK_RESERVATION_OPERATIONS_OperationId",
                        column: x => x.OperationId,
                        principalTable: "RII_STOCK_RESERVATION_OPERATIONS",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_RII_SH_TRACKING_QTY",
                table: "RII_SH_TRACKING",
                sql: "[PlannedQuantity] > 0 AND [ReservedQuantity] >= 0 AND [PickedQuantity] >= 0 AND [PackedQuantity] >= 0 AND [LoadedQuantity] >= 0 AND [ShippedQuantity] >= 0");

            migrationBuilder.Sql("""
                DECLARE @Marker nvarchar(200)=N'Migration:AddStockReservationLedgerAndOperationLifecycle';
                DECLARE @Permissions TABLE(Code nvarchar(150) NOT NULL, Name nvarchar(200) NOT NULL);
                INSERT INTO @Permissions(Code,Name) VALUES
                (N'WMS.WAREHOUSE_TRANSFER.UPDATE',N'Transfer taslağını güncelle'),
                (N'WMS.WAREHOUSE_TRANSFER.DELETE',N'Transfer taslağını sil'),
                (N'WMS.WAREHOUSE_TRANSFER.CANCEL',N'Transferi iptal et ve stok hareketlerini ters çevir'),
                (N'WMS.SHIPPING.UPDATE',N'Sevk taslağını güncelle'),
                (N'WMS.SHIPPING.DELETE',N'Sevk taslağını sil'),
                (N'WMS.SHIPPING.CANCEL',N'Sevki iptal et ve stok hareketlerini ters çevir');

                INSERT INTO dbo.RII_PERMISSION_DEFINITIONS
                    (BranchCode,Code,Name,Description,IsActive,AvailableOnWeb,AvailableOnMobile,IsDeleted,CreatedDate)
                SELECT N'0',p.Code,p.Name,@Marker,1,1,0,0,SYSUTCDATETIME()
                FROM @Permissions p
                WHERE NOT EXISTS(SELECT 1 FROM dbo.RII_PERMISSION_DEFINITIONS d WHERE d.Code=p.Code);

                IF EXISTS(SELECT 1 FROM dbo.RII_PERMISSION_GROUPS WHERE Id=1001 AND IsDeleted=0)
                BEGIN
                    INSERT INTO dbo.RII_PERMISSION_GROUP_PERMISSIONS
                        (BranchCode,PermissionGroupId,PermissionDefinitionId,IsDeleted,CreatedDate)
                    SELECT N'0',1001,d.Id,0,SYSUTCDATETIME()
                    FROM dbo.RII_PERMISSION_DEFINITIONS d
                    JOIN @Permissions p ON p.Code=d.Code
                    WHERE NOT EXISTS(
                        SELECT 1 FROM dbo.RII_PERMISSION_GROUP_PERMISSIONS gp
                        WHERE gp.PermissionGroupId=1001 AND gp.PermissionDefinitionId=d.Id AND gp.IsDeleted=0);
                END
                """);

            migrationBuilder.CreateIndex(
                name: "IX_RII_STOCK_RESERVATION_ENTRIES_DIMENSIONS",
                table: "RII_STOCK_RESERVATION_ENTRIES",
                columns: new[] { "WarehouseId", "LocationId", "StockId", "YapCodeId", "UnitCode", "StockStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_STOCK_RESERVATION_ENTRIES_IsDeleted",
                table: "RII_STOCK_RESERVATION_ENTRIES",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_STOCK_RESERVATION_ENTRIES_OperationId",
                table: "RII_STOCK_RESERVATION_ENTRIES",
                column: "OperationId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_STOCK_RESERVATION_ENTRIES_REFERENCE_LINE",
                table: "RII_STOCK_RESERVATION_ENTRIES",
                columns: new[] { "ReferenceLineId", "WarehouseId", "LocationId", "StockId" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_STOCK_RESERVATION_OPERATIONS_IsDeleted",
                table: "RII_STOCK_RESERVATION_OPERATIONS",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_STOCK_RESERVATION_OPERATIONS_REFERENCE",
                table: "RII_STOCK_RESERVATION_OPERATIONS",
                columns: new[] { "ReferenceType", "ReferenceId" });

            migrationBuilder.CreateIndex(
                name: "UX_RII_STOCK_RESERVATION_OPERATIONS_IDEMPOTENCY",
                table: "RII_STOCK_RESERVATION_OPERATIONS",
                column: "IdempotencyKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DECLARE @Marker nvarchar(200)=N'Migration:AddStockReservationLedgerAndOperationLifecycle';
                DELETE gp
                FROM dbo.RII_PERMISSION_GROUP_PERMISSIONS gp
                JOIN dbo.RII_PERMISSION_DEFINITIONS d ON d.Id=gp.PermissionDefinitionId
                WHERE d.Description=@Marker;
                DELETE FROM dbo.RII_PERMISSION_DEFINITIONS WHERE Description=@Marker;
                """);

            migrationBuilder.DropTable(
                name: "RII_STOCK_RESERVATION_ENTRIES");

            migrationBuilder.DropTable(
                name: "RII_STOCK_RESERVATION_OPERATIONS");

            migrationBuilder.DropCheckConstraint(
                name: "CK_RII_SH_TRACKING_QTY",
                table: "RII_SH_TRACKING");

            migrationBuilder.DropColumn(
                name: "ReservedQuantity",
                table: "RII_SH_TRACKING");

            migrationBuilder.AddCheckConstraint(
                name: "CK_RII_SH_TRACKING_QTY",
                table: "RII_SH_TRACKING",
                sql: "[PlannedQuantity] > 0 AND [PickedQuantity] >= 0 AND [PackedQuantity] >= 0 AND [LoadedQuantity] >= 0 AND [ShippedQuantity] >= 0");
        }
    }
}
