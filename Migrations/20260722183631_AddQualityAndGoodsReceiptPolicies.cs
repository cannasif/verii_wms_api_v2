using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddQualityAndGoodsReceiptPolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "BlockPutawayUntilQualityDecision",
                table: "RII_GR_HEADER",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ErpPostingPolicy",
                table: "RII_GR_HEADER",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "AfterAllApprovals");

            migrationBuilder.AddColumn<bool>(
                name: "HoldInventoryUntilQualityDecision",
                table: "RII_GR_HEADER",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "InventoryAvailabilityPolicy",
                table: "RII_GR_HEADER",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "AfterQualityApproval");

            migrationBuilder.AddColumn<string>(
                name: "OverReceiptPolicy",
                table: "RII_GR_HEADER",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "NotAllowed");

            migrationBuilder.AddColumn<bool>(
                name: "RequireErpApproval",
                table: "RII_GR_HEADER",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "RequireQualityApproval",
                table: "RII_GR_HEADER",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "RequireReceiptApproval",
                table: "RII_GR_HEADER",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "RII_GR_POLICIES",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PolicyKey = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    OverReceiptPolicy = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    OverReceiptTolerancePercent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    AllowUnderReceipt = table.Column<bool>(type: "bit", nullable: false),
                    RequireShortCloseApproval = table.Column<bool>(type: "bit", nullable: false),
                    RequireReceiptApproval = table.Column<bool>(type: "bit", nullable: false),
                    RequireQualityApproval = table.Column<bool>(type: "bit", nullable: false),
                    RequireErpApproval = table.Column<bool>(type: "bit", nullable: false),
                    HoldInventoryUntilQualityDecision = table.Column<bool>(type: "bit", nullable: false),
                    BlockPutawayUntilQualityDecision = table.Column<bool>(type: "bit", nullable: false),
                    InventoryAvailabilityPolicy = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ErpPostingPolicy = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    AllowOrderlessReceipt = table.Column<bool>(type: "bit", nullable: false),
                    AllowUnplannedReceipt = table.Column<bool>(type: "bit", nullable: false),
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
                    table.PrimaryKey("PK_RII_GR_POLICIES", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RII_QUALITY_INSPECTIONS",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CorrelationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InspectionNo = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    SourceDocumentType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SourceDocumentId = table.Column<long>(type: "bigint", nullable: false),
                    SourceDocumentNo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    WarehouseId = table.Column<long>(type: "bigint", nullable: false),
                    SupplierId = table.Column<long>(type: "bigint", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DecidedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    InspectorUserId = table.Column<long>(type: "bigint", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
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
                    table.PrimaryKey("PK_RII_QUALITY_INSPECTIONS", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RII_QUALITY_PARAMETERS",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ParameterKey = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    AutoCreateInspectionOnReceipt = table.Column<bool>(type: "bit", nullable: false),
                    DefaultInspectionMode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    DefaultFailAction = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    HoldInventoryUntilDecision = table.Column<bool>(type: "bit", nullable: false),
                    BlockPutawayUntilDecision = table.Column<bool>(type: "bit", nullable: false),
                    BlockErpPostingUntilDecision = table.Column<bool>(type: "bit", nullable: false),
                    RequireManagerApprovalForRelease = table.Column<bool>(type: "bit", nullable: false),
                    AllowPartialDecision = table.Column<bool>(type: "bit", nullable: false),
                    AllowDirectReceiptWhenNoRule = table.Column<bool>(type: "bit", nullable: false),
                    BlockReceiptWhenLotMissing = table.Column<bool>(type: "bit", nullable: false),
                    BlockReceiptWhenSerialMissing = table.Column<bool>(type: "bit", nullable: false),
                    BlockReceiptWhenExpiryMissing = table.Column<bool>(type: "bit", nullable: false),
                    DefaultQualityLocationId = table.Column<long>(type: "bigint", nullable: true),
                    DefaultQuarantineLocationId = table.Column<long>(type: "bigint", nullable: true),
                    DefaultRejectLocationId = table.Column<long>(type: "bigint", nullable: true),
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
                    table.PrimaryKey("PK_RII_QUALITY_PARAMETERS", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RII_QUALITY_RULES",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ScopeType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    StockId = table.Column<long>(type: "bigint", nullable: true),
                    StockGroupCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    InspectionMode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    SamplingMode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    SamplingValue = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    FailAction = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    AutoQuarantine = table.Column<bool>(type: "bit", nullable: false),
                    RequireLot = table.Column<bool>(type: "bit", nullable: false),
                    RequireSerial = table.Column<bool>(type: "bit", nullable: false),
                    RequireExpiryDate = table.Column<bool>(type: "bit", nullable: false),
                    MinimumRemainingShelfLifeDays = table.Column<int>(type: "int", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
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
                    table.PrimaryKey("PK_RII_QUALITY_RULES", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RII_QUALITY_INSPECTION_LINES",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    QualityInspectionId = table.Column<long>(type: "bigint", nullable: false),
                    GoodsReceiptLineId = table.Column<long>(type: "bigint", nullable: true),
                    StockId = table.Column<long>(type: "bigint", nullable: false),
                    StockCodeSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    StockNameSnapshot = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    YapCodeId = table.Column<long>(type: "bigint", nullable: true),
                    YapCodeSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    LotNo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SerialNo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ExpiryDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    SampleQuantity = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    AcceptedQuantity = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    RejectedQuantity = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    QuarantineQuantity = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    Decision = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ReasonCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ReasonNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    DecisionBy = table.Column<long>(type: "bigint", nullable: true),
                    DecisionAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
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
                    table.PrimaryKey("PK_RII_QUALITY_INSPECTION_LINES", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RII_QUALITY_INSPECTION_LINES_RII_QUALITY_INSPECTIONS_QualityInspectionId",
                        column: x => x.QualityInspectionId,
                        principalTable: "RII_QUALITY_INSPECTIONS",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "RII_PERMISSION_DEFINITIONS",
                columns: new[] { "Id", "AvailableOnMobile", "AvailableOnWeb", "BranchCode", "Code", "CreatedBy", "CreatedDate", "DeletedBy", "DeletedDate", "Description", "IsActive", "Name", "UpdatedBy", "UpdatedDate" },
                values: new object[,]
                {
                    { 1044L, false, true, "0", "WMS.GOODS_RECEIPT.SETTINGS.VIEW", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Mal kabul ayarlarını görüntüle", null, null },
                    { 1045L, false, true, "0", "WMS.GOODS_RECEIPT.SETTINGS.MANAGE", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Mal kabul ayarlarını yönet", null, null },
                    { 1046L, false, true, "0", "WMS.QUALITY.SETTINGS.VIEW", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Kalite ayarlarını görüntüle", null, null },
                    { 1047L, false, true, "0", "WMS.QUALITY.SETTINGS.MANAGE", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Kalite ayarlarını yönet", null, null },
                    { 1048L, false, true, "0", "WMS.QUALITY.RULES.VIEW", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Kalite kurallarını görüntüle", null, null },
                    { 1049L, false, true, "0", "WMS.QUALITY.RULES.MANAGE", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Kalite kurallarını yönet", null, null },
                    { 1050L, false, true, "0", "WMS.QUALITY.INSPECTIONS.VIEW", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Kalite kontrollerini görüntüle", null, null },
                    { 1051L, false, true, "0", "WMS.QUALITY.INSPECTIONS.DECIDE", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Kalite kararı ver", null, null }
                });

            migrationBuilder.InsertData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                columns: new[] { "Id", "BranchCode", "CreatedBy", "CreatedDate", "DeletedBy", "DeletedDate", "PermissionDefinitionId", "PermissionGroupId", "UpdatedBy", "UpdatedDate" },
                values: new object[,]
                {
                    { 1044L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 1044L, 1001L, null, null },
                    { 1045L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 1045L, 1001L, null, null },
                    { 1046L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 1046L, 1001L, null, null },
                    { 1047L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 1047L, 1001L, null, null },
                    { 1048L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 1048L, 1001L, null, null },
                    { 1049L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 1049L, 1001L, null, null },
                    { 1050L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 1050L, 1001L, null, null },
                    { 1051L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 1051L, 1001L, null, null }
                });

            migrationBuilder.CreateIndex(
                name: "IX_RII_GR_POLICIES_BranchCode_PolicyKey",
                table: "RII_GR_POLICIES",
                columns: new[] { "BranchCode", "PolicyKey" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_GR_POLICIES_IsDeleted",
                table: "RII_GR_POLICIES",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_QUALITY_INSPECTION_LINES_GoodsReceiptLineId",
                table: "RII_QUALITY_INSPECTION_LINES",
                column: "GoodsReceiptLineId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_QUALITY_INSPECTION_LINES_IsDeleted",
                table: "RII_QUALITY_INSPECTION_LINES",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_QUALITY_INSPECTION_LINES_QualityInspectionId",
                table: "RII_QUALITY_INSPECTION_LINES",
                column: "QualityInspectionId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_QUALITY_INSPECTION_LINES_StockId_LotNo_SerialNo",
                table: "RII_QUALITY_INSPECTION_LINES",
                columns: new[] { "StockId", "LotNo", "SerialNo" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_QUALITY_INSPECTIONS_BranchCode_Status_CreatedAtUtc",
                table: "RII_QUALITY_INSPECTIONS",
                columns: new[] { "BranchCode", "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_QUALITY_INSPECTIONS_CorrelationId",
                table: "RII_QUALITY_INSPECTIONS",
                column: "CorrelationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RII_QUALITY_INSPECTIONS_IsDeleted",
                table: "RII_QUALITY_INSPECTIONS",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_QUALITY_PARAMETERS_BranchCode_ParameterKey",
                table: "RII_QUALITY_PARAMETERS",
                columns: new[] { "BranchCode", "ParameterKey" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_QUALITY_PARAMETERS_IsDeleted",
                table: "RII_QUALITY_PARAMETERS",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_QUALITY_RULES_BranchCode_ScopeType_StockId_StockGroupCode_IsActive",
                table: "RII_QUALITY_RULES",
                columns: new[] { "BranchCode", "ScopeType", "StockId", "StockGroupCode", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_QUALITY_RULES_IsDeleted",
                table: "RII_QUALITY_RULES",
                column: "IsDeleted");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RII_GR_POLICIES");

            migrationBuilder.DropTable(
                name: "RII_QUALITY_INSPECTION_LINES");

            migrationBuilder.DropTable(
                name: "RII_QUALITY_PARAMETERS");

            migrationBuilder.DropTable(
                name: "RII_QUALITY_RULES");

            migrationBuilder.DropTable(
                name: "RII_QUALITY_INSPECTIONS");

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 1044L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 1045L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 1046L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 1047L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 1048L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 1049L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 1050L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 1051L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 1044L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 1045L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 1046L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 1047L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 1048L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 1049L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 1050L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 1051L);

            migrationBuilder.DropColumn(
                name: "BlockPutawayUntilQualityDecision",
                table: "RII_GR_HEADER");

            migrationBuilder.DropColumn(
                name: "ErpPostingPolicy",
                table: "RII_GR_HEADER");

            migrationBuilder.DropColumn(
                name: "HoldInventoryUntilQualityDecision",
                table: "RII_GR_HEADER");

            migrationBuilder.DropColumn(
                name: "InventoryAvailabilityPolicy",
                table: "RII_GR_HEADER");

            migrationBuilder.DropColumn(
                name: "OverReceiptPolicy",
                table: "RII_GR_HEADER");

            migrationBuilder.DropColumn(
                name: "RequireErpApproval",
                table: "RII_GR_HEADER");

            migrationBuilder.DropColumn(
                name: "RequireQualityApproval",
                table: "RII_GR_HEADER");

            migrationBuilder.DropColumn(
                name: "RequireReceiptApproval",
                table: "RII_GR_HEADER");
        }
    }
}
