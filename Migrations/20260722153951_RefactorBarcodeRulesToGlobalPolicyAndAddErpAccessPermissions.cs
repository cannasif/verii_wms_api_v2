using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class RefactorBarcodeRulesToGlobalPolicyAndAddErpAccessPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RII_GENERATED_BARCODE_RII_BARCODE_RULE_BarcodeRuleId",
                table: "RII_GENERATED_BARCODE");

            migrationBuilder.DropTable(
                name: "RII_BARCODE_RULE_SEGMENT");

            migrationBuilder.DropTable(
                name: "RII_BARCODE_RULE");

            migrationBuilder.DropIndex(
                name: "UX_RII_GENERATED_BARCODE_IDEMPOTENCY",
                table: "RII_GENERATED_BARCODE");

            migrationBuilder.RenameColumn(
                name: "BarcodeRuleId",
                table: "RII_GENERATED_BARCODE",
                newName: "BarcodePolicyProfileId");

            migrationBuilder.AddColumn<long>(
                name: "BarcodePolicyId",
                table: "RII_GENERATED_BARCODE",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "DocumentNo",
                table: "RII_GENERATED_BARCODE",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LocationCode",
                table: "RII_GENERATED_BARCODE",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PolicyVersion",
                table: "RII_GENERATED_BARCODE",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Scope",
                table: "RII_GENERATED_BARCODE",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "WarehouseCode",
                table: "RII_GENERATED_BARCODE",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RII_BARCODE_POLICY",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PolicyKey = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    CurrentVersion = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
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
                    table.PrimaryKey("PK_RII_BARCODE_POLICY", x => x.Id);
                    table.CheckConstraint("CK_RII_BARCODE_POLICY_VERSION", "[CurrentVersion] > 0");
                });

            migrationBuilder.CreateTable(
                name: "RII_BARCODE_POLICY_PROFILE",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BarcodePolicyId = table.Column<long>(type: "bigint", nullable: false),
                    Scope = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Prefix = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Separator = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false),
                    NextSequence = table.Column<long>(type: "bigint", nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
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
                    table.PrimaryKey("PK_RII_BARCODE_POLICY_PROFILE", x => x.Id);
                    table.CheckConstraint("CK_RII_BARCODE_POLICY_PROFILE_SEQUENCE", "[NextSequence] > 0");
                    table.ForeignKey(
                        name: "FK_RII_BARCODE_POLICY_PROFILE_RII_BARCODE_POLICY_BarcodePolicyId",
                        column: x => x.BarcodePolicyId,
                        principalTable: "RII_BARCODE_POLICY",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_BARCODE_POLICY_PROFILE_SEGMENT",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BarcodePolicyProfileId = table.Column<long>(type: "bigint", nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false),
                    SegmentType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SourceField = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    LiteralValue = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    IsRequired = table.Column<bool>(type: "bit", nullable: false),
                    Transform = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SequenceLength = table.Column<int>(type: "int", nullable: false),
                    DateFormat = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
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
                    table.PrimaryKey("PK_RII_BARCODE_POLICY_PROFILE_SEGMENT", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RII_BARCODE_POLICY_PROFILE_SEGMENT_RII_BARCODE_POLICY_PROFILE_BarcodePolicyProfileId",
                        column: x => x.BarcodePolicyProfileId,
                        principalTable: "RII_BARCODE_POLICY_PROFILE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "RII_BARCODE_POLICY",
                columns: new[] { "Id", "BranchCode", "CreatedBy", "CreatedDate", "CurrentVersion", "DeletedBy", "DeletedDate", "DisplayName", "IsActive", "PolicyKey", "UpdatedBy", "UpdatedDate" },
                values: new object[] { 1L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), 1, null, null, "Genel Barkod Politikası", true, "GLOBAL", null, null });

            migrationBuilder.UpdateData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 1030L,
                columns: new[] { "Code", "Name" },
                values: new object[] { "WMS.BARCODE_POLICY.VIEW", "Genel Barkod Politikasını Görüntüle" });

            migrationBuilder.UpdateData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 1031L,
                columns: new[] { "Code", "Name" },
                values: new object[] { "WMS.BARCODE_POLICY.MANAGE", "Genel Barkod Politikasını Yönet" });

            migrationBuilder.UpdateData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 1032L,
                columns: new[] { "Code", "Name" },
                values: new object[] { "WMS.BARCODE_POLICY.GENERATE", "Politikaya Göre Benzersiz Barkod Üret" });

            migrationBuilder.InsertData(
                table: "RII_PERMISSION_DEFINITIONS",
                columns: new[] { "Id", "AvailableOnMobile", "AvailableOnWeb", "BranchCode", "Code", "CreatedBy", "CreatedDate", "DeletedBy", "DeletedDate", "Description", "IsActive", "Name", "UpdatedBy", "UpdatedDate" },
                values: new object[,]
                {
                    { 1033L, false, true, "0", "ERP.MIRROR.VIEW", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "ERP Eşlenmiş Verilerini Görüntüle", null, null },
                    { 1034L, false, true, "0", "ERP.MIRROR.SYNC", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "ERP Eşleme İşlemlerini Tetikle", null, null },
                    { 1035L, false, true, "0", "ERP.NETSIS_READ.VIEW", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Netsis Okuma Servislerini Kullan", null, null }
                });

            migrationBuilder.InsertData(
                table: "RII_BARCODE_POLICY_PROFILE",
                columns: new[] { "Id", "BarcodePolicyId", "BranchCode", "CreatedBy", "CreatedDate", "DeletedBy", "DeletedDate", "DisplayName", "IsEnabled", "NextSequence", "Prefix", "Scope", "Separator", "UpdatedBy", "UpdatedDate" },
                values: new object[,]
                {
                    { 1L, 1L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, "Ürün / Seri", true, 1L, "WMS-S", "ProductSerial", "/", null, null },
                    { 2L, 1L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, "Ürün / Lot", true, 1L, "WMS-L", "ProductLot", "/", null, null },
                    { 3L, 1L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, "Raf / Konum", true, 1L, "WMS-R", "Location", "/", null, null },
                    { 4L, 1L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, "Palet / Koli / Lojistik", true, 1L, "WMS-P", "Logistics", "/", null, null },
                    { 5L, 1L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, "Belge / Operasyon", true, 1L, "WMS-B", "Document", "/", null, null }
                });

            migrationBuilder.InsertData(
                table: "RII_BARCODE_POLICY_PROFILE_SEGMENT",
                columns: new[] { "Id", "BarcodePolicyProfileId", "BranchCode", "CreatedBy", "CreatedDate", "DateFormat", "DeletedBy", "DeletedDate", "IsRequired", "LiteralValue", "Order", "SegmentType", "SequenceLength", "SourceField", "Transform", "UpdatedBy", "UpdatedDate" },
                values: new object[,]
                {
                    { 1L, 1L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), "yyyyMMdd", null, null, true, null, 1, "Field", 8, "StockCode", "Upper", null, null },
                    { 2L, 1L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), "yyyyMMdd", null, null, true, null, 2, "Field", 8, "SerialNo", "Upper", null, null },
                    { 3L, 1L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), "yyyyMMdd", null, null, false, null, 3, "Field", 8, "YapCode", "Upper", null, null },
                    { 4L, 1L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), "yyyyMMdd", null, null, true, null, 4, "Sequence", 8, null, "None", null, null },
                    { 5L, 2L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), "yyyyMMdd", null, null, true, null, 1, "Field", 8, "StockCode", "Upper", null, null },
                    { 6L, 2L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), "yyyyMMdd", null, null, true, null, 2, "Field", 8, "LotNo", "Upper", null, null },
                    { 7L, 2L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), "yyyyMMdd", null, null, false, null, 3, "Field", 8, "YapCode", "Upper", null, null },
                    { 8L, 2L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), "yyyyMMdd", null, null, true, null, 4, "Sequence", 8, null, "None", null, null },
                    { 9L, 3L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), "yyyyMMdd", null, null, true, null, 1, "Field", 8, "WarehouseCode", "Upper", null, null },
                    { 10L, 3L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), "yyyyMMdd", null, null, true, null, 2, "Field", 8, "LocationCode", "Upper", null, null },
                    { 11L, 3L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), "yyyyMMdd", null, null, true, null, 3, "Sequence", 8, null, "None", null, null },
                    { 12L, 4L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), "yyyyMMdd", null, null, true, null, 1, "Field", 8, "DocumentNo", "Upper", null, null },
                    { 13L, 4L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), "yyyyMMdd", null, null, true, null, 2, "Date", 8, null, "None", null, null },
                    { 14L, 4L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), "yyyyMMdd", null, null, true, null, 3, "Sequence", 8, null, "None", null, null },
                    { 15L, 5L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), "yyyyMMdd", null, null, true, null, 1, "Field", 8, "DocumentNo", "Upper", null, null },
                    { 16L, 5L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), "yyyyMMdd", null, null, true, null, 2, "Sequence", 8, null, "None", null, null }
                });

            // Preserve generated barcode history from the former single-rule model.
            // All legacy rows belonged to the stock/serial rule, which maps to profile 1.
            migrationBuilder.Sql("""
                UPDATE [RII_GENERATED_BARCODE]
                SET [BarcodePolicyId] = 1,
                    [BarcodePolicyProfileId] = 1,
                    [Scope] = N'ProductSerial',
                    [PolicyVersion] = 1
                WHERE [BarcodePolicyId] = 0;

                UPDATE [RII_BARCODE_POLICY_PROFILE]
                SET [NextSequence] =
                    CASE
                        WHEN (SELECT COUNT_BIG(1) + 1 FROM [RII_GENERATED_BARCODE] WHERE [BarcodePolicyProfileId] = 1) > [NextSequence]
                        THEN (SELECT COUNT_BIG(1) + 1 FROM [RII_GENERATED_BARCODE] WHERE [BarcodePolicyProfileId] = 1)
                        ELSE [NextSequence]
                    END
                WHERE [Id] = 1;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_RII_GENERATED_BARCODE_BarcodePolicyProfileId",
                table: "RII_GENERATED_BARCODE",
                column: "BarcodePolicyProfileId");

            migrationBuilder.CreateIndex(
                name: "UX_RII_GENERATED_BARCODE_IDEMPOTENCY",
                table: "RII_GENERATED_BARCODE",
                columns: new[] { "BarcodePolicyId", "Scope", "IdempotencyHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RII_BARCODE_POLICY_IsDeleted",
                table: "RII_BARCODE_POLICY",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "UX_RII_BARCODE_POLICY_BRANCH_KEY",
                table: "RII_BARCODE_POLICY",
                columns: new[] { "BranchCode", "PolicyKey" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_BARCODE_POLICY_PROFILE_IsDeleted",
                table: "RII_BARCODE_POLICY_PROFILE",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "UX_RII_BARCODE_POLICY_PROFILE_SCOPE",
                table: "RII_BARCODE_POLICY_PROFILE",
                columns: new[] { "BarcodePolicyId", "Scope" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_BARCODE_POLICY_PROFILE_SEGMENT_IsDeleted",
                table: "RII_BARCODE_POLICY_PROFILE_SEGMENT",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "UX_RII_BARCODE_POLICY_PROFILE_SEGMENT_ORDER",
                table: "RII_BARCODE_POLICY_PROFILE_SEGMENT",
                columns: new[] { "BarcodePolicyProfileId", "Order" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.AddForeignKey(
                name: "FK_RII_GENERATED_BARCODE_RII_BARCODE_POLICY_BarcodePolicyId",
                table: "RII_GENERATED_BARCODE",
                column: "BarcodePolicyId",
                principalTable: "RII_BARCODE_POLICY",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RII_GENERATED_BARCODE_RII_BARCODE_POLICY_PROFILE_BarcodePolicyProfileId",
                table: "RII_GENERATED_BARCODE",
                column: "BarcodePolicyProfileId",
                principalTable: "RII_BARCODE_POLICY_PROFILE",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RII_GENERATED_BARCODE_RII_BARCODE_POLICY_BarcodePolicyId",
                table: "RII_GENERATED_BARCODE");

            migrationBuilder.DropForeignKey(
                name: "FK_RII_GENERATED_BARCODE_RII_BARCODE_POLICY_PROFILE_BarcodePolicyProfileId",
                table: "RII_GENERATED_BARCODE");

            migrationBuilder.DropTable(
                name: "RII_BARCODE_POLICY_PROFILE_SEGMENT");

            migrationBuilder.DropTable(
                name: "RII_BARCODE_POLICY_PROFILE");

            migrationBuilder.DropTable(
                name: "RII_BARCODE_POLICY");

            migrationBuilder.DropIndex(
                name: "IX_RII_GENERATED_BARCODE_BarcodePolicyProfileId",
                table: "RII_GENERATED_BARCODE");

            migrationBuilder.DropIndex(
                name: "UX_RII_GENERATED_BARCODE_IDEMPOTENCY",
                table: "RII_GENERATED_BARCODE");

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 1033L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 1034L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 1035L);

            migrationBuilder.DropColumn(
                name: "BarcodePolicyId",
                table: "RII_GENERATED_BARCODE");

            migrationBuilder.DropColumn(
                name: "DocumentNo",
                table: "RII_GENERATED_BARCODE");

            migrationBuilder.DropColumn(
                name: "LocationCode",
                table: "RII_GENERATED_BARCODE");

            migrationBuilder.DropColumn(
                name: "PolicyVersion",
                table: "RII_GENERATED_BARCODE");

            migrationBuilder.DropColumn(
                name: "Scope",
                table: "RII_GENERATED_BARCODE");

            migrationBuilder.DropColumn(
                name: "WarehouseCode",
                table: "RII_GENERATED_BARCODE");

            migrationBuilder.RenameColumn(
                name: "BarcodePolicyProfileId",
                table: "RII_GENERATED_BARCODE",
                newName: "BarcodeRuleId");

            migrationBuilder.CreateTable(
                name: "RII_BARCODE_RULE",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BranchCode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false, defaultValue: "0"),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedBy = table.Column<long>(type: "bigint", nullable: true),
                    DeletedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DisplayName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    NextSequence = table.Column<long>(type: "bigint", nullable: false),
                    Prefix = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    RuleCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Separator = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false),
                    Target = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RII_BARCODE_RULE", x => x.Id);
                    table.CheckConstraint("CK_RII_BARCODE_RULE_SEQUENCE", "[NextSequence] > 0");
                });

            migrationBuilder.CreateTable(
                name: "RII_BARCODE_RULE_SEGMENT",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BarcodeRuleId = table.Column<long>(type: "bigint", nullable: false),
                    BranchCode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false, defaultValue: "0"),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DateFormat = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DeletedBy = table.Column<long>(type: "bigint", nullable: true),
                    DeletedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    IsRequired = table.Column<bool>(type: "bit", nullable: false),
                    LiteralValue = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Order = table.Column<int>(type: "int", nullable: false),
                    SegmentType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SequenceLength = table.Column<int>(type: "int", nullable: false),
                    SourceField = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Transform = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RII_BARCODE_RULE_SEGMENT", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RII_BARCODE_RULE_SEGMENT_RII_BARCODE_RULE_BarcodeRuleId",
                        column: x => x.BarcodeRuleId,
                        principalTable: "RII_BARCODE_RULE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "RII_BARCODE_RULE",
                columns: new[] { "Id", "BranchCode", "CreatedBy", "CreatedDate", "DeletedBy", "DeletedDate", "DisplayName", "IsActive", "NextSequence", "Prefix", "RuleCode", "Separator", "Target", "UpdatedBy", "UpdatedDate" },
                values: new object[] { 1L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, "Stok İzlenebilirlik Barkodu", true, 1L, "WMS", "STOCK_TRACE_UNIQUE", "/", "Serial", null, null });

            migrationBuilder.UpdateData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 1030L,
                columns: new[] { "Code", "Name" },
                values: new object[] { "WMS.BARCODE_RULES.VIEW", "Barkod Kurallarını Görüntüle" });

            migrationBuilder.UpdateData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 1031L,
                columns: new[] { "Code", "Name" },
                values: new object[] { "WMS.BARCODE_RULES.MANAGE", "Barkod Kurallarını Yönet" });

            migrationBuilder.UpdateData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 1032L,
                columns: new[] { "Code", "Name" },
                values: new object[] { "WMS.BARCODE_RULES.GENERATE", "Benzersiz Barkod Üret" });

            migrationBuilder.InsertData(
                table: "RII_BARCODE_RULE_SEGMENT",
                columns: new[] { "Id", "BarcodeRuleId", "BranchCode", "CreatedBy", "CreatedDate", "DateFormat", "DeletedBy", "DeletedDate", "IsRequired", "LiteralValue", "Order", "SegmentType", "SequenceLength", "SourceField", "Transform", "UpdatedBy", "UpdatedDate" },
                values: new object[,]
                {
                    { 1L, 1L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), "yyyyMMdd", null, null, true, null, 1, "Field", 8, "StockCode", "Upper", null, null },
                    { 2L, 1L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), "yyyyMMdd", null, null, false, null, 2, "Field", 8, "SerialNo", "Upper", null, null },
                    { 3L, 1L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), "yyyyMMdd", null, null, false, null, 3, "Field", 8, "YapCode", "Upper", null, null },
                    { 4L, 1L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), "yyyyMMdd", null, null, false, null, 4, "Field", 8, "LotNo", "Upper", null, null },
                    { 5L, 1L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), "yyyyMMdd", null, null, true, null, 5, "Sequence", 8, null, "None", null, null }
                });

            migrationBuilder.CreateIndex(
                name: "UX_RII_GENERATED_BARCODE_IDEMPOTENCY",
                table: "RII_GENERATED_BARCODE",
                columns: new[] { "BarcodeRuleId", "IdempotencyHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RII_BARCODE_RULE_IsDeleted",
                table: "RII_BARCODE_RULE",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "UX_RII_BARCODE_RULE_BRANCH_CODE",
                table: "RII_BARCODE_RULE",
                columns: new[] { "BranchCode", "RuleCode" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_BARCODE_RULE_SEGMENT_IsDeleted",
                table: "RII_BARCODE_RULE_SEGMENT",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "UX_RII_BARCODE_RULE_SEGMENT_ORDER",
                table: "RII_BARCODE_RULE_SEGMENT",
                columns: new[] { "BarcodeRuleId", "Order" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.AddForeignKey(
                name: "FK_RII_GENERATED_BARCODE_RII_BARCODE_RULE_BarcodeRuleId",
                table: "RII_GENERATED_BARCODE",
                column: "BarcodeRuleId",
                principalTable: "RII_BARCODE_RULE",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
