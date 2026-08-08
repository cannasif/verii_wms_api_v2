using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddKkdRequestWorkQueue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "KkdRequestLineId",
                table: "RII_KKD_DISTRIBUTION_LINE",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "KkdRequestId",
                table: "RII_KKD_DISTRIBUTION",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RII_KKD_REQUEST",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CorrelationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    EmployeeId = table.Column<long>(type: "bigint", nullable: false),
                    CustomerId = table.Column<long>(type: "bigint", nullable: false),
                    WarehouseId = table.Column<long>(type: "bigint", nullable: true),
                    AssignedUserId = table.Column<long>(type: "bigint", nullable: true),
                    SourceType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ExternalRequestNo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Priority = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    RequestedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    NeededAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReadyAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CancelledAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CancellationReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
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
                    table.PrimaryKey("PK_RII_KKD_REQUEST", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RII_KKD_REQUEST_RII_CUSTOMER_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "RII_CUSTOMER",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_KKD_REQUEST_RII_KKD_EMPLOYEE_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "RII_KKD_EMPLOYEE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_KKD_REQUEST_RII_USERS_AssignedUserId",
                        column: x => x.AssignedUserId,
                        principalTable: "RII_USERS",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_KKD_REQUEST_RII_WAREHOUSE_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "RII_WAREHOUSE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_KKD_REQUEST_LINE",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RequestId = table.Column<long>(type: "bigint", nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    GroupCode = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    GroupName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    StockId = table.Column<long>(type: "bigint", nullable: true),
                    StockCodeSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    StockNameSnapshot = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    UnitCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RequestedQuantity = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: false),
                    AllocatedQuantity = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: false),
                    DeliveredQuantity = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: false),
                    CancelledQuantity = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ExternalOrderNo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ExternalOrderLineId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ResolvedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    ResolvedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ResolutionReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
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
                    table.PrimaryKey("PK_RII_KKD_REQUEST_LINE", x => x.Id);
                    table.CheckConstraint("CK_RII_KKD_REQUEST_LINE_QTY", "[RequestedQuantity] > 0 AND [AllocatedQuantity] >= 0 AND [DeliveredQuantity] >= 0 AND [CancelledQuantity] >= 0 AND [AllocatedQuantity] + [DeliveredQuantity] + [CancelledQuantity] <= [RequestedQuantity]");
                    table.ForeignKey(
                        name: "FK_RII_KKD_REQUEST_LINE_RII_KKD_REQUEST_RequestId",
                        column: x => x.RequestId,
                        principalTable: "RII_KKD_REQUEST",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_KKD_REQUEST_LINE_RII_STOCK_StockId",
                        column: x => x.StockId,
                        principalTable: "RII_STOCK",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_KKD_REQUEST_LINE_RII_USERS_ResolvedByUserId",
                        column: x => x.ResolvedByUserId,
                        principalTable: "RII_USERS",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_KKD_REQUEST_LINE_RESOLUTION",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IdempotencyKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestLineId = table.Column<long>(type: "bigint", nullable: false),
                    PreviousStockId = table.Column<long>(type: "bigint", nullable: true),
                    StockId = table.Column<long>(type: "bigint", nullable: false),
                    StockCodeSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    StockNameSnapshot = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    ResolvedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
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
                    table.PrimaryKey("PK_RII_KKD_REQUEST_LINE_RESOLUTION", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RII_KKD_REQUEST_LINE_RESOLUTION_RII_KKD_REQUEST_LINE_RequestLineId",
                        column: x => x.RequestLineId,
                        principalTable: "RII_KKD_REQUEST_LINE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_KKD_REQUEST_LINE_RESOLUTION_RII_STOCK_PreviousStockId",
                        column: x => x.PreviousStockId,
                        principalTable: "RII_STOCK",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_KKD_REQUEST_LINE_RESOLUTION_RII_STOCK_StockId",
                        column: x => x.StockId,
                        principalTable: "RII_STOCK",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "RII_PERMISSION_DEFINITIONS",
                columns: new[] { "Id", "AvailableOnMobile", "AvailableOnWeb", "BranchCode", "Code", "CreatedBy", "CreatedDate", "DeletedBy", "DeletedDate", "Description", "IsActive", "Name", "UpdatedBy", "UpdatedDate" },
                values: new object[,]
                {
                    { 2512L, false, true, "0", "WMS.KKD.REQUESTS.VIEW", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Açık KKD taleplerini görüntüle", null, null },
                    { 2513L, false, true, "0", "WMS.KKD.REQUESTS.CREATE", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "KKD talebi oluştur", null, null },
                    { 2514L, false, true, "0", "WMS.KKD.REQUESTS.RESOLVE", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "KKD talebinde stok ve beden seçimini çözümle", null, null },
                    { 2515L, false, true, "0", "WMS.KKD.REQUESTS.CANCEL", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "KKD talebini iptal et", null, null }
                });

            migrationBuilder.InsertData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                columns: new[] { "Id", "BranchCode", "CreatedBy", "CreatedDate", "DeletedBy", "DeletedDate", "PermissionDefinitionId", "PermissionGroupId", "UpdatedBy", "UpdatedDate" },
                values: new object[,]
                {
                    { 2512L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 2512L, 1001L, null, null },
                    { 2513L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 2513L, 1001L, null, null },
                    { 2514L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 2514L, 1001L, null, null },
                    { 2515L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 2515L, 1001L, null, null }
                });

            migrationBuilder.Sql("""
                INSERT INTO RII_PERMISSION_GROUP_PERMISSIONS
                    (BranchCode, CreatedDate, IsDeleted, PermissionGroupId, PermissionDefinitionId)
                SELECT '0', SYSUTCDATETIME(), 0, groups.Id, permissions.Id
                FROM RII_PERMISSION_GROUPS groups
                CROSS JOIN RII_PERMISSION_DEFINITIONS permissions
                WHERE groups.IsDeleted = 0
                  AND permissions.IsDeleted = 0
                  AND permissions.IsActive = 1
                  AND (
                    (groups.TemplateKey = N'KKD_OPERATORLERI'
                        AND permissions.Code IN (N'WMS.KKD.REQUESTS.VIEW', N'WMS.KKD.REQUESTS.RESOLVE'))
                    OR
                    (groups.TemplateKey IN (N'KKD_YONETICILERI', N'DEPO_YONETICILERI')
                        AND permissions.Code LIKE N'WMS.KKD.REQUESTS.%')
                  )
                  AND NOT EXISTS (
                    SELECT 1
                    FROM RII_PERMISSION_GROUP_PERMISSIONS existing
                    WHERE existing.PermissionGroupId = groups.Id
                      AND existing.PermissionDefinitionId = permissions.Id
                      AND existing.IsDeleted = 0
                  );
                """);

            migrationBuilder.CreateIndex(
                name: "IX_RII_KKD_DISTRIBUTION_LINE_KkdRequestLineId",
                table: "RII_KKD_DISTRIBUTION_LINE",
                column: "KkdRequestLineId",
                filter: "[KkdRequestLineId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RII_KKD_DISTRIBUTION_KkdRequestId",
                table: "RII_KKD_DISTRIBUTION",
                column: "KkdRequestId",
                filter: "[KkdRequestId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RII_KKD_REQUEST_AssignedUserId",
                table: "RII_KKD_REQUEST",
                column: "AssignedUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_KKD_REQUEST_BranchCode_EmployeeId_Status",
                table: "RII_KKD_REQUEST",
                columns: new[] { "BranchCode", "EmployeeId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_KKD_REQUEST_BranchCode_RequestNo",
                table: "RII_KKD_REQUEST",
                columns: new[] { "BranchCode", "RequestNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RII_KKD_REQUEST_BranchCode_Status_Priority_NeededAtUtc",
                table: "RII_KKD_REQUEST",
                columns: new[] { "BranchCode", "Status", "Priority", "NeededAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_KKD_REQUEST_BranchCode_WarehouseId_Status",
                table: "RII_KKD_REQUEST",
                columns: new[] { "BranchCode", "WarehouseId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_KKD_REQUEST_CorrelationId",
                table: "RII_KKD_REQUEST",
                column: "CorrelationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RII_KKD_REQUEST_CustomerId",
                table: "RII_KKD_REQUEST",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_KKD_REQUEST_EmployeeId",
                table: "RII_KKD_REQUEST",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_KKD_REQUEST_IsDeleted",
                table: "RII_KKD_REQUEST",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_KKD_REQUEST_WarehouseId",
                table: "RII_KKD_REQUEST",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_KKD_REQUEST_LINE_BranchCode_ExternalOrderNo_ExternalOrderLineId",
                table: "RII_KKD_REQUEST_LINE",
                columns: new[] { "BranchCode", "ExternalOrderNo", "ExternalOrderLineId" },
                filter: "[ExternalOrderNo] IS NOT NULL AND [ExternalOrderLineId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RII_KKD_REQUEST_LINE_BranchCode_Status_GroupCode_StockId",
                table: "RII_KKD_REQUEST_LINE",
                columns: new[] { "BranchCode", "Status", "GroupCode", "StockId" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_KKD_REQUEST_LINE_IsDeleted",
                table: "RII_KKD_REQUEST_LINE",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_KKD_REQUEST_LINE_RequestId_LineNo",
                table: "RII_KKD_REQUEST_LINE",
                columns: new[] { "RequestId", "LineNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RII_KKD_REQUEST_LINE_ResolvedByUserId",
                table: "RII_KKD_REQUEST_LINE",
                column: "ResolvedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_KKD_REQUEST_LINE_StockId",
                table: "RII_KKD_REQUEST_LINE",
                column: "StockId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_KKD_REQUEST_LINE_RESOLUTION_BranchCode_RequestLineId_ResolvedAtUtc",
                table: "RII_KKD_REQUEST_LINE_RESOLUTION",
                columns: new[] { "BranchCode", "RequestLineId", "ResolvedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_KKD_REQUEST_LINE_RESOLUTION_IdempotencyKey",
                table: "RII_KKD_REQUEST_LINE_RESOLUTION",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RII_KKD_REQUEST_LINE_RESOLUTION_IsDeleted",
                table: "RII_KKD_REQUEST_LINE_RESOLUTION",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_KKD_REQUEST_LINE_RESOLUTION_PreviousStockId",
                table: "RII_KKD_REQUEST_LINE_RESOLUTION",
                column: "PreviousStockId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_KKD_REQUEST_LINE_RESOLUTION_RequestLineId",
                table: "RII_KKD_REQUEST_LINE_RESOLUTION",
                column: "RequestLineId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_KKD_REQUEST_LINE_RESOLUTION_StockId",
                table: "RII_KKD_REQUEST_LINE_RESOLUTION",
                column: "StockId");

            migrationBuilder.AddForeignKey(
                name: "FK_RII_KKD_DISTRIBUTION_RII_KKD_REQUEST_KkdRequestId",
                table: "RII_KKD_DISTRIBUTION",
                column: "KkdRequestId",
                principalTable: "RII_KKD_REQUEST",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RII_KKD_DISTRIBUTION_LINE_RII_KKD_REQUEST_LINE_KkdRequestLineId",
                table: "RII_KKD_DISTRIBUTION_LINE",
                column: "KkdRequestLineId",
                principalTable: "RII_KKD_REQUEST_LINE",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RII_KKD_DISTRIBUTION_RII_KKD_REQUEST_KkdRequestId",
                table: "RII_KKD_DISTRIBUTION");

            migrationBuilder.DropForeignKey(
                name: "FK_RII_KKD_DISTRIBUTION_LINE_RII_KKD_REQUEST_LINE_KkdRequestLineId",
                table: "RII_KKD_DISTRIBUTION_LINE");

            migrationBuilder.DropTable(
                name: "RII_KKD_REQUEST_LINE_RESOLUTION");

            migrationBuilder.DropTable(
                name: "RII_KKD_REQUEST_LINE");

            migrationBuilder.DropTable(
                name: "RII_KKD_REQUEST");

            migrationBuilder.DropIndex(
                name: "IX_RII_KKD_DISTRIBUTION_LINE_KkdRequestLineId",
                table: "RII_KKD_DISTRIBUTION_LINE");

            migrationBuilder.DropIndex(
                name: "IX_RII_KKD_DISTRIBUTION_KkdRequestId",
                table: "RII_KKD_DISTRIBUTION");

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 2512L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 2513L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 2514L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 2515L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2512L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2513L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2514L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2515L);

            migrationBuilder.DropColumn(
                name: "KkdRequestLineId",
                table: "RII_KKD_DISTRIBUTION_LINE");

            migrationBuilder.DropColumn(
                name: "KkdRequestId",
                table: "RII_KKD_DISTRIBUTION");
        }
    }
}
