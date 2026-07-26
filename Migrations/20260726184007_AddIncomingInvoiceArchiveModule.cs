using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddIncomingInvoiceArchiveModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RII_ELOGO_CONNECTION",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Key = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Vkn = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Username = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PasswordCipherText = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Source = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    EndpointUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ApplicationName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Version = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    TimeoutSeconds = table.Column<int>(type: "int", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
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
                    table.PrimaryKey("PK_RII_ELOGO_CONNECTION", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RII_INCOMING_INVOICE_HEADER",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ELogoConnectionId = table.Column<long>(type: "bigint", nullable: false),
                    OwnerVkn = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Uuid = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentKind = table.Column<int>(type: "int", nullable: false),
                    ProfileId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    InvoiceNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    InvoiceTypeCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    IssueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    IssueTime = table.Column<TimeOnly>(type: "time", nullable: true),
                    CurrencyCode = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    OrderReferenceNo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DespatchReferenceNo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SupplierVknOrTckn = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SupplierName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    SupplierTaxOffice = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SupplierCustomerId = table.Column<long>(type: "bigint", nullable: true),
                    CustomerVknOrTckn = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CustomerName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    LineExtensionAmount = table.Column<decimal>(type: "decimal(28,8)", precision: 28, scale: 8, nullable: false),
                    TaxExclusiveAmount = table.Column<decimal>(type: "decimal(28,8)", precision: 28, scale: 8, nullable: false),
                    TaxAmount = table.Column<decimal>(type: "decimal(28,8)", precision: 28, scale: 8, nullable: false),
                    TaxInclusiveAmount = table.Column<decimal>(type: "decimal(28,8)", precision: 28, scale: 8, nullable: false),
                    AllowanceTotalAmount = table.Column<decimal>(type: "decimal(28,8)", precision: 28, scale: 8, nullable: false),
                    PayableAmount = table.Column<decimal>(type: "decimal(28,8)", precision: 28, scale: 8, nullable: false),
                    ArchiveStatus = table.Column<int>(type: "int", nullable: false),
                    ValidationStatus = table.Column<int>(type: "int", nullable: false),
                    ValidationMessage = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    SourceHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ImportedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastSynchronizedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
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
                    table.PrimaryKey("PK_RII_INCOMING_INVOICE_HEADER", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RII_INCOMING_INVOICE_HEADER_RII_ELOGO_CONNECTION_ELogoConnectionId",
                        column: x => x.ELogoConnectionId,
                        principalTable: "RII_ELOGO_CONNECTION",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_INCOMING_INVOICE_DOCUMENT",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IncomingInvoiceId = table.Column<long>(type: "bigint", nullable: false),
                    Format = table.Column<int>(type: "int", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    StoragePath = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    StoredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
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
                    table.PrimaryKey("PK_RII_INCOMING_INVOICE_DOCUMENT", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RII_INCOMING_INVOICE_DOCUMENT_RII_INCOMING_INVOICE_HEADER_IncomingInvoiceId",
                        column: x => x.IncomingInvoiceId,
                        principalTable: "RII_INCOMING_INVOICE_HEADER",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RII_INCOMING_INVOICE_GR_LINK",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IncomingInvoiceId = table.Column<long>(type: "bigint", nullable: false),
                    GoodsReceiptId = table.Column<long>(type: "bigint", nullable: false),
                    IdempotencyKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    LinkedQuantity = table.Column<decimal>(type: "decimal(28,8)", precision: 28, scale: 8, nullable: false),
                    LinkedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LinkedBy = table.Column<long>(type: "bigint", nullable: false),
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
                    table.PrimaryKey("PK_RII_INCOMING_INVOICE_GR_LINK", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RII_INCOMING_INVOICE_GR_LINK_RII_GR_HEADER_GoodsReceiptId",
                        column: x => x.GoodsReceiptId,
                        principalTable: "RII_GR_HEADER",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_INCOMING_INVOICE_GR_LINK_RII_INCOMING_INVOICE_HEADER_IncomingInvoiceId",
                        column: x => x.IncomingInvoiceId,
                        principalTable: "RII_INCOMING_INVOICE_HEADER",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_INCOMING_INVOICE_LINE",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IncomingInvoiceId = table.Column<long>(type: "bigint", nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    ExternalLineId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    StockCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    BuyerStockCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    StockName = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(28,8)", precision: 28, scale: 8, nullable: false),
                    UnitCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(28,8)", precision: 28, scale: 8, nullable: false),
                    LineExtensionAmount = table.Column<decimal>(type: "decimal(28,8)", precision: 28, scale: 8, nullable: false),
                    TaxRate = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    TaxAmount = table.Column<decimal>(type: "decimal(28,8)", precision: 28, scale: 8, nullable: false),
                    StockId = table.Column<long>(type: "bigint", nullable: true),
                    YapCodeId = table.Column<long>(type: "bigint", nullable: true),
                    YapCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    MatchStatus = table.Column<int>(type: "int", nullable: false),
                    MatchMessage = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
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
                    table.PrimaryKey("PK_RII_INCOMING_INVOICE_LINE", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RII_INCOMING_INVOICE_LINE_RII_INCOMING_INVOICE_HEADER_IncomingInvoiceId",
                        column: x => x.IncomingInvoiceId,
                        principalTable: "RII_INCOMING_INVOICE_HEADER",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RII_INCOMING_INVOICE_LINE_RII_STOCK_StockId",
                        column: x => x.StockId,
                        principalTable: "RII_STOCK",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_INCOMING_INVOICE_LINE_RII_YAP_CODE_YapCodeId",
                        column: x => x.YapCodeId,
                        principalTable: "RII_YAP_CODE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_INCOMING_INVOICE_GR_LINE_LINK",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IncomingInvoiceGoodsReceiptLinkId = table.Column<long>(type: "bigint", nullable: false),
                    IncomingInvoiceLineId = table.Column<long>(type: "bigint", nullable: false),
                    GoodsReceiptLineId = table.Column<long>(type: "bigint", nullable: false),
                    LinkedQuantity = table.Column<decimal>(type: "decimal(28,8)", precision: 28, scale: 8, nullable: false),
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
                    table.PrimaryKey("PK_RII_INCOMING_INVOICE_GR_LINE_LINK", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RII_INCOMING_INVOICE_GR_LINE_LINK_RII_GR_LINE_GoodsReceiptLineId",
                        column: x => x.GoodsReceiptLineId,
                        principalTable: "RII_GR_LINE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_INCOMING_INVOICE_GR_LINE_LINK_RII_INCOMING_INVOICE_GR_LINK_IncomingInvoiceGoodsReceiptLinkId",
                        column: x => x.IncomingInvoiceGoodsReceiptLinkId,
                        principalTable: "RII_INCOMING_INVOICE_GR_LINK",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_INCOMING_INVOICE_GR_LINE_LINK_RII_INCOMING_INVOICE_LINE_IncomingInvoiceLineId",
                        column: x => x.IncomingInvoiceLineId,
                        principalTable: "RII_INCOMING_INVOICE_LINE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "RII_PERMISSION_DEFINITIONS",
                columns: new[] { "Id", "AvailableOnMobile", "AvailableOnWeb", "BranchCode", "Code", "CreatedBy", "CreatedDate", "DeletedBy", "DeletedDate", "Description", "IsActive", "Name", "UpdatedBy", "UpdatedDate" },
                values: new object[,]
                {
                    { 2300L, false, true, "0", "WMS.INCOMING_INVOICE.VIEW", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Gelen e-Fatura/e-Arşiv kayıtlarını görüntüle", null, null },
                    { 2301L, false, true, "0", "WMS.INCOMING_INVOICE.IMPORT", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Gelen e-Fatura/e-Arşiv belgesi arşivle", null, null },
                    { 2302L, false, true, "0", "WMS.INCOMING_INVOICE.CONNECTIONS.MANAGE", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "eLogo bağlantılarını yönet", null, null },
                    { 2303L, false, true, "0", "WMS.INCOMING_INVOICE.CREATE_GOODS_RECEIPT", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Faturadan mal kabul taslağı oluştur", null, null }
                });

            migrationBuilder.InsertData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                columns: new[] { "Id", "BranchCode", "CreatedBy", "CreatedDate", "DeletedBy", "DeletedDate", "PermissionDefinitionId", "PermissionGroupId", "UpdatedBy", "UpdatedDate" },
                values: new object[,]
                {
                    { 2300L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 2300L, 1001L, null, null },
                    { 2301L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 2301L, 1001L, null, null },
                    { 2302L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 2302L, 1001L, null, null },
                    { 2303L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 2303L, 1001L, null, null }
                });

            migrationBuilder.CreateIndex(
                name: "IX_RII_ELOGO_CONNECTION_BRANCH_ACTIVE_NAME",
                table: "RII_ELOGO_CONNECTION",
                columns: new[] { "BranchCode", "IsActive", "DisplayName" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_ELOGO_CONNECTION_IsDeleted",
                table: "RII_ELOGO_CONNECTION",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "UX_RII_ELOGO_CONNECTION_BRANCH_KEY",
                table: "RII_ELOGO_CONNECTION",
                columns: new[] { "BranchCode", "Key" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_INCOMING_INVOICE_DOCUMENT_IsDeleted",
                table: "RII_INCOMING_INVOICE_DOCUMENT",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "UX_RII_INCOMING_INVOICE_DOCUMENT_FORMAT",
                table: "RII_INCOMING_INVOICE_DOCUMENT",
                columns: new[] { "IncomingInvoiceId", "Format" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_INCOMING_INVOICE_GR_LINE_LINK_GoodsReceiptLineId",
                table: "RII_INCOMING_INVOICE_GR_LINE_LINK",
                column: "GoodsReceiptLineId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_INCOMING_INVOICE_GR_LINE_LINK_IncomingInvoiceGoodsReceiptLinkId",
                table: "RII_INCOMING_INVOICE_GR_LINE_LINK",
                column: "IncomingInvoiceGoodsReceiptLinkId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_INCOMING_INVOICE_GR_LINE_LINK_IsDeleted",
                table: "RII_INCOMING_INVOICE_GR_LINE_LINK",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_INCOMING_INVOICE_LINE_LINK_REMAINING",
                table: "RII_INCOMING_INVOICE_GR_LINE_LINK",
                columns: new[] { "IncomingInvoiceLineId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "UX_RII_INCOMING_INVOICE_GR_LINE",
                table: "RII_INCOMING_INVOICE_GR_LINE_LINK",
                columns: new[] { "IncomingInvoiceLineId", "GoodsReceiptLineId" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_INCOMING_INVOICE_GR_LINK_GoodsReceiptId",
                table: "RII_INCOMING_INVOICE_GR_LINK",
                column: "GoodsReceiptId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_INCOMING_INVOICE_GR_LINK_IsDeleted",
                table: "RII_INCOMING_INVOICE_GR_LINK",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "UX_RII_INCOMING_INVOICE_GR_IDEMPOTENCY",
                table: "RII_INCOMING_INVOICE_GR_LINK",
                columns: new[] { "IncomingInvoiceId", "IdempotencyKey" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_RII_INCOMING_INVOICE_GR_LINK",
                table: "RII_INCOMING_INVOICE_GR_LINK",
                columns: new[] { "IncomingInvoiceId", "GoodsReceiptId" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_INCOMING_INVOICE_BRANCH_DATE_NO",
                table: "RII_INCOMING_INVOICE_HEADER",
                columns: new[] { "BranchCode", "IssueDate", "InvoiceNo" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_INCOMING_INVOICE_HEADER_ELogoConnectionId",
                table: "RII_INCOMING_INVOICE_HEADER",
                column: "ELogoConnectionId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_INCOMING_INVOICE_HEADER_IsDeleted",
                table: "RII_INCOMING_INVOICE_HEADER",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_INCOMING_INVOICE_STATUS_IMPORTED",
                table: "RII_INCOMING_INVOICE_HEADER",
                columns: new[] { "BranchCode", "ArchiveStatus", "ImportedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_RII_INCOMING_INVOICE_OWNER_UUID",
                table: "RII_INCOMING_INVOICE_HEADER",
                columns: new[] { "BranchCode", "OwnerVkn", "Uuid" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_INCOMING_INVOICE_LINE_IsDeleted",
                table: "RII_INCOMING_INVOICE_LINE",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_INCOMING_INVOICE_LINE_STOCK",
                table: "RII_INCOMING_INVOICE_LINE",
                columns: new[] { "BranchCode", "StockCode" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_INCOMING_INVOICE_LINE_StockId",
                table: "RII_INCOMING_INVOICE_LINE",
                column: "StockId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_INCOMING_INVOICE_LINE_YapCodeId",
                table: "RII_INCOMING_INVOICE_LINE",
                column: "YapCodeId");

            migrationBuilder.CreateIndex(
                name: "UX_RII_INCOMING_INVOICE_LINE_NO",
                table: "RII_INCOMING_INVOICE_LINE",
                columns: new[] { "IncomingInvoiceId", "LineNo" },
                unique: true,
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RII_INCOMING_INVOICE_DOCUMENT");

            migrationBuilder.DropTable(
                name: "RII_INCOMING_INVOICE_GR_LINE_LINK");

            migrationBuilder.DropTable(
                name: "RII_INCOMING_INVOICE_GR_LINK");

            migrationBuilder.DropTable(
                name: "RII_INCOMING_INVOICE_LINE");

            migrationBuilder.DropTable(
                name: "RII_INCOMING_INVOICE_HEADER");

            migrationBuilder.DropTable(
                name: "RII_ELOGO_CONNECTION");

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 2300L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 2301L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 2302L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 2303L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2300L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2301L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2302L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2303L);
        }
    }
}
