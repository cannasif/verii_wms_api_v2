using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddProductionPlanningFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RII_PR_HEADER",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DocumentSeriesId = table.Column<long>(type: "bigint", nullable: false),
                    DocumentNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    DocumentDate = table.Column<DateOnly>(type: "date", nullable: false),
                    CorrelationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ExecutionMode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Priority = table.Column<byte>(type: "tinyint", nullable: false),
                    CustomerId = table.Column<long>(type: "bigint", nullable: true),
                    CustomerCodeSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CustomerNameSnapshot = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    PlannedStartAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PlannedEndAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ActualStartAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ActualEndAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReleasedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReleasedBy = table.Column<long>(type: "bigint", nullable: true),
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
                    table.PrimaryKey("PK_RII_PR_HEADER", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RII_PR_ORDER",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductionHeaderId = table.Column<long>(type: "bigint", nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    OrderNo = table.Column<string>(type: "nvarchar(70)", maxLength: 70, nullable: false),
                    ExternalOrderNo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    SequenceNo = table.Column<int>(type: "int", nullable: false),
                    ParallelGroupNo = table.Column<int>(type: "int", nullable: true),
                    BomReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    RoutingReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    WorkCenterCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ProducedStockId = table.Column<long>(type: "bigint", nullable: false),
                    ProducedStockCodeSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ProducedStockNameSnapshot = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ProducedYapCodeId = table.Column<long>(type: "bigint", nullable: true),
                    ProducedYapCodeSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UnitCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PlannedQuantity = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: false),
                    CompletedQuantity = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: false),
                    ScrapQuantity = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: false),
                    SourceWarehouseId = table.Column<long>(type: "bigint", nullable: false),
                    TargetWarehouseId = table.Column<long>(type: "bigint", nullable: false),
                    RequireMaterialTransferBeforeStart = table.Column<bool>(type: "bit", nullable: false),
                    PlannedStartAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PlannedEndAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ActualStartAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ActualEndAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    BlockedReason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
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
                    table.PrimaryKey("PK_RII_PR_ORDER", x => x.Id);
                    table.CheckConstraint("CK_RII_PR_ORDER_QTY", "[PlannedQuantity] > 0 AND [CompletedQuantity] >= 0 AND [ScrapQuantity] >= 0");
                    table.ForeignKey(
                        name: "FK_RII_PR_ORDER_RII_PR_HEADER_ProductionHeaderId",
                        column: x => x.ProductionHeaderId,
                        principalTable: "RII_PR_HEADER",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_PR_ASSIGNMENT",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductionOrderId = table.Column<long>(type: "bigint", nullable: false),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    IsPrimary = table.Column<bool>(type: "bit", nullable: false),
                    AssignedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AssignedBy = table.Column<long>(type: "bigint", nullable: false),
                    AcceptedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
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
                    table.PrimaryKey("PK_RII_PR_ASSIGNMENT", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RII_PR_ASSIGNMENT_RII_PR_ORDER_ProductionOrderId",
                        column: x => x.ProductionOrderId,
                        principalTable: "RII_PR_ORDER",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_PR_DEPENDENCY",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductionHeaderId = table.Column<long>(type: "bigint", nullable: false),
                    PredecessorOrderId = table.Column<long>(type: "bigint", nullable: false),
                    SuccessorOrderId = table.Column<long>(type: "bigint", nullable: false),
                    DependencyType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    LagMinutes = table.Column<int>(type: "int", nullable: false),
                    RequireOutputAvailable = table.Column<bool>(type: "bit", nullable: false),
                    RequireTransferCompleted = table.Column<bool>(type: "bit", nullable: false),
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
                    table.PrimaryKey("PK_RII_PR_DEPENDENCY", x => x.Id);
                    table.CheckConstraint("CK_RII_PR_DEPENDENCY_SELF", "[PredecessorOrderId] <> [SuccessorOrderId]");
                    table.ForeignKey(
                        name: "FK_RII_PR_DEPENDENCY_RII_PR_HEADER_ProductionHeaderId",
                        column: x => x.ProductionHeaderId,
                        principalTable: "RII_PR_HEADER",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_PR_DEPENDENCY_RII_PR_ORDER_PredecessorOrderId",
                        column: x => x.PredecessorOrderId,
                        principalTable: "RII_PR_ORDER",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_PR_DEPENDENCY_RII_PR_ORDER_SuccessorOrderId",
                        column: x => x.SuccessorOrderId,
                        principalTable: "RII_PR_ORDER",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_PR_MATERIAL",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductionOrderId = table.Column<long>(type: "bigint", nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    StockId = table.Column<long>(type: "bigint", nullable: false),
                    StockCodeSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    StockNameSnapshot = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    YapCodeId = table.Column<long>(type: "bigint", nullable: true),
                    YapCodeSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UnitCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RequiredQuantity = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: false),
                    IssuedQuantity = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: false),
                    ConsumedQuantity = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: false),
                    IssueMode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    IsMandatory = table.Column<bool>(type: "bit", nullable: false),
                    SourceWarehouseId = table.Column<long>(type: "bigint", nullable: false),
                    PreferredSourceLocationId = table.Column<long>(type: "bigint", nullable: true),
                    TrackingType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
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
                    table.PrimaryKey("PK_RII_PR_MATERIAL", x => x.Id);
                    table.CheckConstraint("CK_RII_PR_MATERIAL_QTY", "[RequiredQuantity] > 0 AND [IssuedQuantity] >= 0 AND [ConsumedQuantity] >= 0");
                    table.ForeignKey(
                        name: "FK_RII_PR_MATERIAL_RII_PR_ORDER_ProductionOrderId",
                        column: x => x.ProductionOrderId,
                        principalTable: "RII_PR_ORDER",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_PR_OUTPUT",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductionOrderId = table.Column<long>(type: "bigint", nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    StockId = table.Column<long>(type: "bigint", nullable: false),
                    StockCodeSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    StockNameSnapshot = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    YapCodeId = table.Column<long>(type: "bigint", nullable: true),
                    YapCodeSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UnitCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PlannedQuantity = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: false),
                    ProducedQuantity = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: false),
                    ScrapQuantity = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: false),
                    TargetWarehouseId = table.Column<long>(type: "bigint", nullable: false),
                    PreferredTargetLocationId = table.Column<long>(type: "bigint", nullable: true),
                    TrackingType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    IsPrimary = table.Column<bool>(type: "bit", nullable: false),
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
                    table.PrimaryKey("PK_RII_PR_OUTPUT", x => x.Id);
                    table.CheckConstraint("CK_RII_PR_OUTPUT_QTY", "[PlannedQuantity] > 0 AND [ProducedQuantity] >= 0 AND [ScrapQuantity] >= 0");
                    table.ForeignKey(
                        name: "FK_RII_PR_OUTPUT_RII_PR_ORDER_ProductionOrderId",
                        column: x => x.ProductionOrderId,
                        principalTable: "RII_PR_ORDER",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "RII_PERMISSION_DEFINITIONS",
                columns: new[] { "Id", "AvailableOnMobile", "AvailableOnWeb", "BranchCode", "Code", "CreatedBy", "CreatedDate", "DeletedBy", "DeletedDate", "Description", "IsActive", "Name", "UpdatedBy", "UpdatedDate" },
                values: new object[,]
                {
                    { 2420L, false, true, "0", "WMS.PRODUCTION.VIEW", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Üretim plan ve emirlerini görüntüle", null, null },
                    { 2421L, false, true, "0", "WMS.PRODUCTION.CREATE", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Üretim planı ve emri oluştur", null, null },
                    { 2422L, false, true, "0", "WMS.PRODUCTION.RELEASE", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Üretim planını serbest bırak", null, null },
                    { 2423L, false, true, "0", "WMS.PRODUCTION.DELETE", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Taslak üretim planını sil", null, null },
                    { 2424L, false, true, "0", "WMS.PRODUCTION.OPERATE", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Üretim operasyonunu yürüt", null, null }
                });

            migrationBuilder.InsertData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                columns: new[] { "Id", "BranchCode", "CreatedBy", "CreatedDate", "DeletedBy", "DeletedDate", "PermissionDefinitionId", "PermissionGroupId", "UpdatedBy", "UpdatedDate" },
                values: new object[,]
                {
                    { 2420L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 2420L, 1001L, null, null },
                    { 2421L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 2421L, 1001L, null, null },
                    { 2422L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 2422L, 1001L, null, null },
                    { 2423L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 2423L, 1001L, null, null },
                    { 2424L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 2424L, 1001L, null, null }
                });

            migrationBuilder.CreateIndex(
                name: "IX_RII_PT_LINE_LINK_ProductionConsumptionId",
                table: "RII_PT_LINE_LINK",
                column: "ProductionConsumptionId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_PT_LINE_LINK_ProductionOutputId",
                table: "RII_PT_LINE_LINK",
                column: "ProductionOutputId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_PT_HEADER_LINK_ProductionHeaderId",
                table: "RII_PT_HEADER_LINK",
                column: "ProductionHeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_PT_HEADER_LINK_ProductionOrderId",
                table: "RII_PT_HEADER_LINK",
                column: "ProductionOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_PR_ASSIGNMENT_IsDeleted",
                table: "RII_PR_ASSIGNMENT",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_PR_ASSIGNMENT_ProductionOrderId_UserId",
                table: "RII_PR_ASSIGNMENT",
                columns: new[] { "ProductionOrderId", "UserId" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_PR_ASSIGNMENT_UserId_AcceptedAtUtc_CompletedAtUtc",
                table: "RII_PR_ASSIGNMENT",
                columns: new[] { "UserId", "AcceptedAtUtc", "CompletedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_PR_DEPENDENCY_IsDeleted",
                table: "RII_PR_DEPENDENCY",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_PR_DEPENDENCY_PredecessorOrderId_SuccessorOrderId",
                table: "RII_PR_DEPENDENCY",
                columns: new[] { "PredecessorOrderId", "SuccessorOrderId" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_PR_DEPENDENCY_ProductionHeaderId",
                table: "RII_PR_DEPENDENCY",
                column: "ProductionHeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_PR_DEPENDENCY_SuccessorOrderId",
                table: "RII_PR_DEPENDENCY",
                column: "SuccessorOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_PR_HEADER_BranchCode_DocumentNo",
                table: "RII_PR_HEADER",
                columns: new[] { "BranchCode", "DocumentNo" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_PR_HEADER_BranchCode_Status_PlannedStartAtUtc",
                table: "RII_PR_HEADER",
                columns: new[] { "BranchCode", "Status", "PlannedStartAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_PR_HEADER_CorrelationId",
                table: "RII_PR_HEADER",
                column: "CorrelationId",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_PR_HEADER_IsDeleted",
                table: "RII_PR_HEADER",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_PR_MATERIAL_IsDeleted",
                table: "RII_PR_MATERIAL",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_PR_MATERIAL_ProductionOrderId_LineNo",
                table: "RII_PR_MATERIAL",
                columns: new[] { "ProductionOrderId", "LineNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RII_PR_MATERIAL_StockId_SourceWarehouseId",
                table: "RII_PR_MATERIAL",
                columns: new[] { "StockId", "SourceWarehouseId" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_PR_ORDER_BranchCode_OrderNo",
                table: "RII_PR_ORDER",
                columns: new[] { "BranchCode", "OrderNo" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_PR_ORDER_IsDeleted",
                table: "RII_PR_ORDER",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_PR_ORDER_ProducedStockId_Status",
                table: "RII_PR_ORDER",
                columns: new[] { "ProducedStockId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_PR_ORDER_ProductionHeaderId_LineNo",
                table: "RII_PR_ORDER",
                columns: new[] { "ProductionHeaderId", "LineNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RII_PR_OUTPUT_IsDeleted",
                table: "RII_PR_OUTPUT",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_PR_OUTPUT_ProductionOrderId_LineNo",
                table: "RII_PR_OUTPUT",
                columns: new[] { "ProductionOrderId", "LineNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RII_PR_OUTPUT_StockId_TargetWarehouseId",
                table: "RII_PR_OUTPUT",
                columns: new[] { "StockId", "TargetWarehouseId" });

            migrationBuilder.AddForeignKey(
                name: "FK_RII_PT_HEADER_LINK_RII_PR_HEADER_ProductionHeaderId",
                table: "RII_PT_HEADER_LINK",
                column: "ProductionHeaderId",
                principalTable: "RII_PR_HEADER",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RII_PT_HEADER_LINK_RII_PR_ORDER_ProductionOrderId",
                table: "RII_PT_HEADER_LINK",
                column: "ProductionOrderId",
                principalTable: "RII_PR_ORDER",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RII_PT_LINE_LINK_RII_PR_MATERIAL_ProductionConsumptionId",
                table: "RII_PT_LINE_LINK",
                column: "ProductionConsumptionId",
                principalTable: "RII_PR_MATERIAL",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RII_PT_LINE_LINK_RII_PR_OUTPUT_ProductionOutputId",
                table: "RII_PT_LINE_LINK",
                column: "ProductionOutputId",
                principalTable: "RII_PR_OUTPUT",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RII_PT_HEADER_LINK_RII_PR_HEADER_ProductionHeaderId",
                table: "RII_PT_HEADER_LINK");

            migrationBuilder.DropForeignKey(
                name: "FK_RII_PT_HEADER_LINK_RII_PR_ORDER_ProductionOrderId",
                table: "RII_PT_HEADER_LINK");

            migrationBuilder.DropForeignKey(
                name: "FK_RII_PT_LINE_LINK_RII_PR_MATERIAL_ProductionConsumptionId",
                table: "RII_PT_LINE_LINK");

            migrationBuilder.DropForeignKey(
                name: "FK_RII_PT_LINE_LINK_RII_PR_OUTPUT_ProductionOutputId",
                table: "RII_PT_LINE_LINK");

            migrationBuilder.DropTable(
                name: "RII_PR_ASSIGNMENT");

            migrationBuilder.DropTable(
                name: "RII_PR_DEPENDENCY");

            migrationBuilder.DropTable(
                name: "RII_PR_MATERIAL");

            migrationBuilder.DropTable(
                name: "RII_PR_OUTPUT");

            migrationBuilder.DropTable(
                name: "RII_PR_ORDER");

            migrationBuilder.DropTable(
                name: "RII_PR_HEADER");

            migrationBuilder.DropIndex(
                name: "IX_RII_PT_LINE_LINK_ProductionConsumptionId",
                table: "RII_PT_LINE_LINK");

            migrationBuilder.DropIndex(
                name: "IX_RII_PT_LINE_LINK_ProductionOutputId",
                table: "RII_PT_LINE_LINK");

            migrationBuilder.DropIndex(
                name: "IX_RII_PT_HEADER_LINK_ProductionHeaderId",
                table: "RII_PT_HEADER_LINK");

            migrationBuilder.DropIndex(
                name: "IX_RII_PT_HEADER_LINK_ProductionOrderId",
                table: "RII_PT_HEADER_LINK");

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 2420L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 2421L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 2422L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 2423L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 2424L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2420L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2421L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2422L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2423L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2424L);
        }
    }
}
