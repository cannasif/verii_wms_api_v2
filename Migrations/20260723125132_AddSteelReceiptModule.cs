using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddSteelReceiptModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SerialMaskSnapshot",
                table: "RII_GR_EXECUTION_LINE",
                type: "nvarchar(250)",
                maxLength: 250,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SerialNumberRuleCodeSnapshot",
                table: "RII_GR_EXECUTION_LINE",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SerialNumberRuleId",
                table: "RII_GR_EXECUTION_LINE",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SerialNumberRuleVersion",
                table: "RII_GR_EXECUTION_LINE",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RII_SERIAL_NUMBER_RULES",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RuleCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Scope = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    StockId = table.Column<long>(type: "bigint", nullable: true),
                    StockGroupCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Version = table.Column<int>(type: "int", nullable: false),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    MaskTemplate = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    CharacterSet = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    UniquenessScope = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    MinLength = table.Column<int>(type: "int", nullable: false),
                    MaxLength = table.Column<int>(type: "int", nullable: false),
                    TrimWhitespace = table.Column<bool>(type: "bit", nullable: false),
                    NormalizeToUpper = table.Column<bool>(type: "bit", nullable: false),
                    IsRequired = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    EffectiveFromUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EffectiveToUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
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
                    table.PrimaryKey("PK_RII_SERIAL_NUMBER_RULES", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RII_STEEL_RECEIPT_PLAN",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CorrelationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ImportReferenceNo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SourceFileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    ExportReferenceNo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SupplierId = table.Column<long>(type: "bigint", nullable: false),
                    SupplierCodeSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SupplierNameSnapshot = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    TargetWarehouseId = table.Column<long>(type: "bigint", nullable: false),
                    ReceivingLocationId = table.Column<long>(type: "bigint", nullable: false),
                    DocumentSeriesId = table.Column<long>(type: "bigint", nullable: false),
                    WaybillNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    WaybillDate = table.Column<DateOnly>(type: "date", nullable: true),
                    PlannedArrivalAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    TotalLineCount = table.Column<int>(type: "int", nullable: false),
                    TotalExpectedQuantity = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    ImportedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ImportedBy = table.Column<long>(type: "bigint", nullable: false),
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
                    table.PrimaryKey("PK_RII_STEEL_RECEIPT_PLAN", x => x.Id);
                    table.CheckConstraint("CK_RII_STEEL_PLAN_LINE_COUNT", "[TotalLineCount] >= 0");
                    table.CheckConstraint("CK_RII_STEEL_PLAN_QUANTITY", "[TotalExpectedQuantity] >= 0");
                    table.ForeignKey(
                        name: "FK_RII_STEEL_RECEIPT_PLAN_RII_CUSTOMER_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "RII_CUSTOMER",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_STEEL_RECEIPT_PLAN_RII_DOCUMENT_SERIES_DocumentSeriesId",
                        column: x => x.DocumentSeriesId,
                        principalTable: "RII_DOCUMENT_SERIES",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_STEEL_RECEIPT_PLAN_RII_LOCATION_ReceivingLocationId",
                        column: x => x.ReceivingLocationId,
                        principalTable: "RII_LOCATION",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_STEEL_RECEIPT_PLAN_RII_WAREHOUSE_TargetWarehouseId",
                        column: x => x.TargetWarehouseId,
                        principalTable: "RII_WAREHOUSE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_STEEL_RECEIPT_PLAN_LINE",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PlanId = table.Column<long>(type: "bigint", nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    DCode = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    ExternalLineKey = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    NetsisOrderNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    NetsisOrderLineNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    StockId = table.Column<long>(type: "bigint", nullable: false),
                    StockCodeSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    StockNameSnapshot = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    YapCodeId = table.Column<long>(type: "bigint", nullable: true),
                    YapCodeSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UnitCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SupplierSerialNo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SecondarySerialNo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CombinedSize = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    MaterialGrade = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    HeatNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CertificateNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ExpectedQuantity = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    ArrivedQuantity = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    ApprovedQuantity = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    RejectedQuantity = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    TargetWarehouseId = table.Column<long>(type: "bigint", nullable: false),
                    ReceivingLocationId = table.Column<long>(type: "bigint", nullable: false),
                    ArrivalStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    InspectionStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ConversionStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    PutawayStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    RejectReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    InspectionNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    InspectedBy = table.Column<long>(type: "bigint", nullable: true),
                    InspectedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    GoodsReceiptId = table.Column<long>(type: "bigint", nullable: true),
                    GoodsReceiptLineId = table.Column<long>(type: "bigint", nullable: true),
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
                    table.PrimaryKey("PK_RII_STEEL_RECEIPT_PLAN_LINE", x => x.Id);
                    table.CheckConstraint("CK_RII_STEEL_LINE_NO", "[LineNo] > 0");
                    table.CheckConstraint("CK_RII_STEEL_LINE_QTY", "[ExpectedQuantity] > 0 AND [ArrivedQuantity] >= 0 AND [ApprovedQuantity] >= 0 AND [RejectedQuantity] >= 0 AND [ApprovedQuantity] + [RejectedQuantity] <= [ArrivedQuantity] AND [ArrivedQuantity] <= [ExpectedQuantity]");
                    table.ForeignKey(
                        name: "FK_RII_STEEL_RECEIPT_PLAN_LINE_RII_GR_HEADER_GoodsReceiptId",
                        column: x => x.GoodsReceiptId,
                        principalTable: "RII_GR_HEADER",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_STEEL_RECEIPT_PLAN_LINE_RII_GR_LINE_GoodsReceiptLineId",
                        column: x => x.GoodsReceiptLineId,
                        principalTable: "RII_GR_LINE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_STEEL_RECEIPT_PLAN_LINE_RII_LOCATION_ReceivingLocationId",
                        column: x => x.ReceivingLocationId,
                        principalTable: "RII_LOCATION",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_STEEL_RECEIPT_PLAN_LINE_RII_STEEL_RECEIPT_PLAN_PlanId",
                        column: x => x.PlanId,
                        principalTable: "RII_STEEL_RECEIPT_PLAN",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_STEEL_RECEIPT_PLAN_LINE_RII_STOCK_StockId",
                        column: x => x.StockId,
                        principalTable: "RII_STOCK",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_STEEL_RECEIPT_PLAN_LINE_RII_WAREHOUSE_TargetWarehouseId",
                        column: x => x.TargetWarehouseId,
                        principalTable: "RII_WAREHOUSE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_STEEL_RECEIPT_PLAN_LINE_RII_YAP_CODE_YapCodeId",
                        column: x => x.YapCodeId,
                        principalTable: "RII_YAP_CODE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_STEEL_RECEIPT_ATTACHMENT",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PlanLineId = table.Column<long>(type: "bigint", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    StoragePath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Caption = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
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
                    table.PrimaryKey("PK_RII_STEEL_RECEIPT_ATTACHMENT", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RII_STEEL_RECEIPT_ATTACHMENT_RII_STEEL_RECEIPT_PLAN_LINE_PlanLineId",
                        column: x => x.PlanLineId,
                        principalTable: "RII_STEEL_RECEIPT_PLAN_LINE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_STEEL_RECEIPT_PLACEMENT",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PlanLineId = table.Column<long>(type: "bigint", nullable: false),
                    WarehouseId = table.Column<long>(type: "bigint", nullable: false),
                    LocationId = table.Column<long>(type: "bigint", nullable: false),
                    PlacementType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    RowNo = table.Column<int>(type: "int", nullable: true),
                    PositionNo = table.Column<int>(type: "int", nullable: true),
                    StackOrderNo = table.Column<int>(type: "int", nullable: true),
                    StockMovementOperationId = table.Column<long>(type: "bigint", nullable: false),
                    PlacedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PlacedBy = table.Column<long>(type: "bigint", nullable: false),
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
                    table.PrimaryKey("PK_RII_STEEL_RECEIPT_PLACEMENT", x => x.Id);
                    table.CheckConstraint("CK_RII_STEEL_PLACEMENT_COORDINATES", "[RowNo] > 0 AND [PositionNo] > 0");
                    table.CheckConstraint("CK_RII_STEEL_PLACEMENT_STACK", "[PlacementType] <> 'Stacked' OR [StackOrderNo] > 0");
                    table.ForeignKey(
                        name: "FK_RII_STEEL_RECEIPT_PLACEMENT_RII_LOCATION_LocationId",
                        column: x => x.LocationId,
                        principalTable: "RII_LOCATION",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_STEEL_RECEIPT_PLACEMENT_RII_STEEL_RECEIPT_PLAN_LINE_PlanLineId",
                        column: x => x.PlanLineId,
                        principalTable: "RII_STEEL_RECEIPT_PLAN_LINE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_STEEL_RECEIPT_PLACEMENT_RII_STOCK_MOVEMENT_OPERATION_StockMovementOperationId",
                        column: x => x.StockMovementOperationId,
                        principalTable: "RII_STOCK_MOVEMENT_OPERATION",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_STEEL_RECEIPT_PLACEMENT_RII_WAREHOUSE_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "RII_WAREHOUSE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "RII_PERMISSION_DEFINITIONS",
                columns: new[] { "Id", "AvailableOnMobile", "AvailableOnWeb", "BranchCode", "Code", "CreatedBy", "CreatedDate", "DeletedBy", "DeletedDate", "Description", "IsActive", "Name", "UpdatedBy", "UpdatedDate" },
                values: new object[,]
                {
                    { 1053L, false, true, "0", "WMS.SERIAL_RULES.VIEW", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Seri maske kurallarını görüntüle", null, null },
                    { 1054L, false, true, "0", "WMS.SERIAL_RULES.MANAGE", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Seri maske kurallarını yönet", null, null },
                    { 1055L, false, true, "0", "WMS.STEEL_RECEIPT.VIEW", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "SAC mal kabul planlarını görüntüle", null, null },
                    { 1056L, false, true, "0", "WMS.STEEL_RECEIPT.IMPORT", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "SAC beklenti aktarımı yap", null, null },
                    { 1057L, false, true, "0", "WMS.STEEL_RECEIPT.INSPECT", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "SAC varış kontrolü yap", null, null },
                    { 1058L, false, true, "0", "WMS.STEEL_RECEIPT.CONVERT", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "SAC levhalarını ortak mal kabule aktar", null, null },
                    { 1059L, false, true, "0", "WMS.STEEL_RECEIPT.PUTAWAY", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "SAC levhasını nihai rafa yerleştir", null, null }
                });

            migrationBuilder.InsertData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                columns: new[] { "Id", "BranchCode", "CreatedBy", "CreatedDate", "DeletedBy", "DeletedDate", "PermissionDefinitionId", "PermissionGroupId", "UpdatedBy", "UpdatedDate" },
                values: new object[,]
                {
                    { 1055L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 1055L, 1001L, null, null },
                    { 1056L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 1056L, 1001L, null, null },
                    { 1057L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 1057L, 1001L, null, null },
                    { 1058L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 1058L, 1001L, null, null },
                    { 1059L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 1059L, 1001L, null, null }
                });

            migrationBuilder.Sql(SqlServerMigrationSql.Execute(
                "CREATE INDEX [IX_RII_GR_EXECUTION_LINE_SerialNumberRuleId] ON [RII_GR_EXECUTION_LINE] ([SerialNumberRuleId]);"));

            migrationBuilder.CreateIndex(
                name: "IX_RII_SERIAL_NUMBER_RULES_IsDeleted",
                table: "RII_SERIAL_NUMBER_RULES",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_SERIAL_RULE_RESOLVE",
                table: "RII_SERIAL_NUMBER_RULES",
                columns: new[] { "BranchCode", "Scope", "StockId", "StockGroupCode", "IsActive", "EffectiveFromUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_RII_SERIAL_RULE_CODE_VERSION",
                table: "RII_SERIAL_NUMBER_RULES",
                columns: new[] { "BranchCode", "RuleCode", "Version" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_STEEL_RECEIPT_ATTACHMENT_IsDeleted",
                table: "RII_STEEL_RECEIPT_ATTACHMENT",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_STEEL_RECEIPT_ATTACHMENT_PlanLineId_CreatedDate",
                table: "RII_STEEL_RECEIPT_ATTACHMENT",
                columns: new[] { "PlanLineId", "CreatedDate" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_STEEL_RECEIPT_PLACEMENT_IsDeleted",
                table: "RII_STEEL_RECEIPT_PLACEMENT",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_STEEL_RECEIPT_PLACEMENT_LocationId",
                table: "RII_STEEL_RECEIPT_PLACEMENT",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_STEEL_RECEIPT_PLACEMENT_PlanLineId",
                table: "RII_STEEL_RECEIPT_PLACEMENT",
                column: "PlanLineId",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_STEEL_RECEIPT_PLACEMENT_StockMovementOperationId",
                table: "RII_STEEL_RECEIPT_PLACEMENT",
                column: "StockMovementOperationId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_STEEL_RECEIPT_PLACEMENT_WarehouseId_LocationId_RowNo_PositionNo_StackOrderNo",
                table: "RII_STEEL_RECEIPT_PLACEMENT",
                columns: new[] { "WarehouseId", "LocationId", "RowNo", "PositionNo", "StackOrderNo" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_STEEL_RECEIPT_PLAN_BranchCode_ImportReferenceNo",
                table: "RII_STEEL_RECEIPT_PLAN",
                columns: new[] { "BranchCode", "ImportReferenceNo" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_STEEL_RECEIPT_PLAN_CorrelationId",
                table: "RII_STEEL_RECEIPT_PLAN",
                column: "CorrelationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RII_STEEL_RECEIPT_PLAN_DocumentSeriesId",
                table: "RII_STEEL_RECEIPT_PLAN",
                column: "DocumentSeriesId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_STEEL_RECEIPT_PLAN_IsDeleted",
                table: "RII_STEEL_RECEIPT_PLAN",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_STEEL_RECEIPT_PLAN_ReceivingLocationId",
                table: "RII_STEEL_RECEIPT_PLAN",
                column: "ReceivingLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_STEEL_RECEIPT_PLAN_SupplierId_Status_PlannedArrivalAtUtc",
                table: "RII_STEEL_RECEIPT_PLAN",
                columns: new[] { "SupplierId", "Status", "PlannedArrivalAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_STEEL_RECEIPT_PLAN_TargetWarehouseId",
                table: "RII_STEEL_RECEIPT_PLAN",
                column: "TargetWarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_STEEL_RECEIPT_PLAN_LINE_DCode",
                table: "RII_STEEL_RECEIPT_PLAN_LINE",
                column: "DCode",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_STEEL_RECEIPT_PLAN_LINE_GoodsReceiptId",
                table: "RII_STEEL_RECEIPT_PLAN_LINE",
                column: "GoodsReceiptId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_STEEL_RECEIPT_PLAN_LINE_GoodsReceiptLineId",
                table: "RII_STEEL_RECEIPT_PLAN_LINE",
                column: "GoodsReceiptLineId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_STEEL_RECEIPT_PLAN_LINE_InspectionStatus_ConversionStatus",
                table: "RII_STEEL_RECEIPT_PLAN_LINE",
                columns: new[] { "InspectionStatus", "ConversionStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_STEEL_RECEIPT_PLAN_LINE_IsDeleted",
                table: "RII_STEEL_RECEIPT_PLAN_LINE",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_STEEL_RECEIPT_PLAN_LINE_PlanId_ExternalLineKey",
                table: "RII_STEEL_RECEIPT_PLAN_LINE",
                columns: new[] { "PlanId", "ExternalLineKey" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_STEEL_RECEIPT_PLAN_LINE_PlanId_LineNo",
                table: "RII_STEEL_RECEIPT_PLAN_LINE",
                columns: new[] { "PlanId", "LineNo" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_STEEL_RECEIPT_PLAN_LINE_ReceivingLocationId",
                table: "RII_STEEL_RECEIPT_PLAN_LINE",
                column: "ReceivingLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_STEEL_RECEIPT_PLAN_LINE_StockId_SupplierSerialNo",
                table: "RII_STEEL_RECEIPT_PLAN_LINE",
                columns: new[] { "StockId", "SupplierSerialNo" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_STEEL_RECEIPT_PLAN_LINE_TargetWarehouseId",
                table: "RII_STEEL_RECEIPT_PLAN_LINE",
                column: "TargetWarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_STEEL_RECEIPT_PLAN_LINE_YapCodeId",
                table: "RII_STEEL_RECEIPT_PLAN_LINE",
                column: "YapCodeId");

            migrationBuilder.AddForeignKey(
                name: "FK_RII_GR_EXECUTION_LINE_RII_SERIAL_NUMBER_RULES_SerialNumberRuleId",
                table: "RII_GR_EXECUTION_LINE",
                column: "SerialNumberRuleId",
                principalTable: "RII_SERIAL_NUMBER_RULES",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RII_GR_EXECUTION_LINE_RII_SERIAL_NUMBER_RULES_SerialNumberRuleId",
                table: "RII_GR_EXECUTION_LINE");

            migrationBuilder.DropTable(
                name: "RII_SERIAL_NUMBER_RULES");

            migrationBuilder.DropTable(
                name: "RII_STEEL_RECEIPT_ATTACHMENT");

            migrationBuilder.DropTable(
                name: "RII_STEEL_RECEIPT_PLACEMENT");

            migrationBuilder.DropTable(
                name: "RII_STEEL_RECEIPT_PLAN_LINE");

            migrationBuilder.DropTable(
                name: "RII_STEEL_RECEIPT_PLAN");

            migrationBuilder.DropIndex(
                name: "IX_RII_GR_EXECUTION_LINE_SerialNumberRuleId",
                table: "RII_GR_EXECUTION_LINE");

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 1053L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 1054L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 1055L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 1056L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 1057L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 1058L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 1059L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 1055L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 1056L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 1057L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 1058L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 1059L);

            migrationBuilder.DropColumn(
                name: "SerialMaskSnapshot",
                table: "RII_GR_EXECUTION_LINE");

            migrationBuilder.DropColumn(
                name: "SerialNumberRuleCodeSnapshot",
                table: "RII_GR_EXECUTION_LINE");

            migrationBuilder.DropColumn(
                name: "SerialNumberRuleId",
                table: "RII_GR_EXECUTION_LINE");

            migrationBuilder.DropColumn(
                name: "SerialNumberRuleVersion",
                table: "RII_GR_EXECUTION_LINE");
        }
    }
}
