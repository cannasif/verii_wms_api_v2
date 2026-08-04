using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddProcurementModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RII_PC_ORDER",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    OrderDate = table.Column<DateOnly>(type: "date", nullable: false),
                    DeliveryDate = table.Column<DateOnly>(type: "date", nullable: true),
                    SupplierId = table.Column<long>(type: "bigint", nullable: false),
                    SupplierCodeSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SupplierNameSnapshot = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    SourceQuoteId = table.Column<long>(type: "bigint", nullable: true),
                    CurrencyCode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    ExchangeRate = table.Column<decimal>(type: "decimal(20,8)", precision: 20, scale: 8, nullable: false),
                    ProjectCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ApprovedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ApprovedBy = table.Column<long>(type: "bigint", nullable: true),
                    ErpOrderNo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ErpPostedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
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
                    table.PrimaryKey("PK_RII_PC_ORDER", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RII_PC_REQUEST",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RequestNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    RequestDate = table.Column<DateOnly>(type: "date", nullable: false),
                    RequiredDate = table.Column<DateOnly>(type: "date", nullable: true),
                    DepartmentCode = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    ProjectCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Subject = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    SubmittedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DecidedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DecidedBy = table.Column<long>(type: "bigint", nullable: true),
                    DecisionNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
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
                    table.PrimaryKey("PK_RII_PC_REQUEST", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RII_PC_STATUS_HISTORY",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DocumentType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    DocumentId = table.Column<long>(type: "bigint", nullable: false),
                    FromStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ToStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ActorUserId = table.Column<long>(type: "bigint", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ChangedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
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
                    table.PrimaryKey("PK_RII_PC_STATUS_HISTORY", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RII_PC_ORDER_LINE",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProcurementPurchaseOrderId = table.Column<long>(type: "bigint", nullable: false),
                    SourceQuoteLineId = table.Column<long>(type: "bigint", nullable: true),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    StockId = table.Column<long>(type: "bigint", nullable: true),
                    StockCodeSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    StockNameSnapshot = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    UnitCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    OrderedQuantity = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: false),
                    ReceivedQuantity = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: false),
                    CancelledQuantity = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: false),
                    DiscountRate = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    VatRate = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    DeliveryDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ProjectCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
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
                    table.PrimaryKey("PK_RII_PC_ORDER_LINE", x => x.Id);
                    table.CheckConstraint("CK_RII_PC_ORDER_LINE_AMOUNTS", "[OrderedQuantity] > 0 AND [ReceivedQuantity] >= 0 AND [CancelledQuantity] >= 0 AND [ReceivedQuantity] + [CancelledQuantity] <= [OrderedQuantity] AND [UnitPrice] >= 0");
                    table.ForeignKey(
                        name: "FK_RII_PC_ORDER_LINE_RII_PC_ORDER_ProcurementPurchaseOrderId",
                        column: x => x.ProcurementPurchaseOrderId,
                        principalTable: "RII_PC_ORDER",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_PC_REQUEST_LINE",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProcurementRequestId = table.Column<long>(type: "bigint", nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    StockId = table.Column<long>(type: "bigint", nullable: true),
                    StockCodeSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    StockNameSnapshot = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    UnitCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RequestedQuantity = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: false),
                    ConvertedQuantity = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: false),
                    RequiredDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ProjectCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
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
                    table.PrimaryKey("PK_RII_PC_REQUEST_LINE", x => x.Id);
                    table.CheckConstraint("CK_RII_PC_REQUEST_LINE_QTY", "[RequestedQuantity] > 0 AND [ConvertedQuantity] >= 0 AND [ConvertedQuantity] <= [RequestedQuantity]");
                    table.ForeignKey(
                        name: "FK_RII_PC_REQUEST_LINE_RII_PC_REQUEST_ProcurementRequestId",
                        column: x => x.ProcurementRequestId,
                        principalTable: "RII_PC_REQUEST",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_PC_RFQ",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RfqNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    RfqDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ResponseDueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ProcurementRequestId = table.Column<long>(type: "bigint", nullable: true),
                    Subject = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    BuyerMessage = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    SentAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
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
                    table.PrimaryKey("PK_RII_PC_RFQ", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RII_PC_RFQ_RII_PC_REQUEST_ProcurementRequestId",
                        column: x => x.ProcurementRequestId,
                        principalTable: "RII_PC_REQUEST",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_PC_QUOTE",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProcurementRfqId = table.Column<long>(type: "bigint", nullable: false),
                    SupplierId = table.Column<long>(type: "bigint", nullable: false),
                    SupplierCodeSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SupplierNameSnapshot = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    QuoteNo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    QuoteDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ValidUntil = table.Column<DateOnly>(type: "date", nullable: true),
                    CurrencyCode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    ExchangeRate = table.Column<decimal>(type: "decimal(20,8)", precision: 20, scale: 8, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
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
                    table.PrimaryKey("PK_RII_PC_QUOTE", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RII_PC_QUOTE_RII_PC_RFQ_ProcurementRfqId",
                        column: x => x.ProcurementRfqId,
                        principalTable: "RII_PC_RFQ",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_PC_RFQ_LINE",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProcurementRfqId = table.Column<long>(type: "bigint", nullable: false),
                    ProcurementRequestLineId = table.Column<long>(type: "bigint", nullable: true),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    StockId = table.Column<long>(type: "bigint", nullable: true),
                    StockCodeSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    StockNameSnapshot = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    UnitCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RequestedQuantity = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: false),
                    RequiredDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ProjectCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
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
                    table.PrimaryKey("PK_RII_PC_RFQ_LINE", x => x.Id);
                    table.CheckConstraint("CK_RII_PC_RFQ_LINE_QTY", "[RequestedQuantity] > 0");
                    table.ForeignKey(
                        name: "FK_RII_PC_RFQ_LINE_RII_PC_RFQ_ProcurementRfqId",
                        column: x => x.ProcurementRfqId,
                        principalTable: "RII_PC_RFQ",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_PC_RFQ_SUPPLIER",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProcurementRfqId = table.Column<long>(type: "bigint", nullable: false),
                    SupplierId = table.Column<long>(type: "bigint", nullable: false),
                    SupplierCodeSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SupplierNameSnapshot = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
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
                    table.PrimaryKey("PK_RII_PC_RFQ_SUPPLIER", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RII_PC_RFQ_SUPPLIER_RII_PC_RFQ_ProcurementRfqId",
                        column: x => x.ProcurementRfqId,
                        principalTable: "RII_PC_RFQ",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_PC_QUOTE_LINE",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProcurementSupplierQuoteId = table.Column<long>(type: "bigint", nullable: false),
                    ProcurementRfqLineId = table.Column<long>(type: "bigint", nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    QuotedQuantity = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: false),
                    DiscountRate = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    VatRate = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    DeliveryDate = table.Column<DateOnly>(type: "date", nullable: true),
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
                    table.PrimaryKey("PK_RII_PC_QUOTE_LINE", x => x.Id);
                    table.CheckConstraint("CK_RII_PC_QUOTE_LINE_AMOUNTS", "[QuotedQuantity] > 0 AND [UnitPrice] >= 0 AND [DiscountRate] >= 0 AND [DiscountRate] <= 100 AND [VatRate] >= 0");
                    table.ForeignKey(
                        name: "FK_RII_PC_QUOTE_LINE_RII_PC_QUOTE_ProcurementSupplierQuoteId",
                        column: x => x.ProcurementSupplierQuoteId,
                        principalTable: "RII_PC_QUOTE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "RII_PERMISSION_DEFINITIONS",
                columns: new[] { "Id", "AvailableOnMobile", "AvailableOnWeb", "BranchCode", "Code", "CreatedBy", "CreatedDate", "DeletedBy", "DeletedDate", "Description", "IsActive", "Name", "UpdatedBy", "UpdatedDate" },
                values: new object[,]
                {
                    { 2600L, false, true, "0", "WMS.PROCUREMENT.VIEW", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Satınalma belgelerini görüntüle", null, null },
                    { 2601L, false, true, "0", "WMS.PROCUREMENT.REQUEST.MANAGE", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Satınalma taleplerini yönet", null, null },
                    { 2602L, false, true, "0", "WMS.PROCUREMENT.RFQ.MANAGE", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Teklif taleplerini yönet", null, null },
                    { 2603L, false, true, "0", "WMS.PROCUREMENT.QUOTE.MANAGE", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Tedarikçi tekliflerini yönet", null, null },
                    { 2604L, false, true, "0", "WMS.PROCUREMENT.ORDER.MANAGE", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Satınalma siparişlerini yönet", null, null },
                    { 2605L, false, true, "0", "WMS.PROCUREMENT.APPROVE", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Satınalma belgelerini onayla", null, null }
                });

            migrationBuilder.InsertData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                columns: new[] { "Id", "BranchCode", "CreatedBy", "CreatedDate", "DeletedBy", "DeletedDate", "PermissionDefinitionId", "PermissionGroupId", "UpdatedBy", "UpdatedDate" },
                values: new object[,]
                {
                    { 2600L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 2600L, 1001L, null, null },
                    { 2601L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 2601L, 1001L, null, null },
                    { 2602L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 2602L, 1001L, null, null },
                    { 2603L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 2603L, 1001L, null, null },
                    { 2604L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 2604L, 1001L, null, null },
                    { 2605L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 2605L, 1001L, null, null }
                });

            migrationBuilder.CreateIndex(
                name: "IX_RII_PC_ORDER_BranchCode_OrderNo",
                table: "RII_PC_ORDER",
                columns: new[] { "BranchCode", "OrderNo" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_PC_ORDER_BranchCode_Status_SupplierId",
                table: "RII_PC_ORDER",
                columns: new[] { "BranchCode", "Status", "SupplierId" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_PC_ORDER_IsDeleted",
                table: "RII_PC_ORDER",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_PC_ORDER_LINE_IsDeleted",
                table: "RII_PC_ORDER_LINE",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_PC_ORDER_LINE_ProcurementPurchaseOrderId_LineNo",
                table: "RII_PC_ORDER_LINE",
                columns: new[] { "ProcurementPurchaseOrderId", "LineNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RII_PC_ORDER_LINE_StockId_DeliveryDate",
                table: "RII_PC_ORDER_LINE",
                columns: new[] { "StockId", "DeliveryDate" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_PC_QUOTE_IsDeleted",
                table: "RII_PC_QUOTE",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_PC_QUOTE_ProcurementRfqId_SupplierId_QuoteNo",
                table: "RII_PC_QUOTE",
                columns: new[] { "ProcurementRfqId", "SupplierId", "QuoteNo" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_PC_QUOTE_LINE_IsDeleted",
                table: "RII_PC_QUOTE_LINE",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_PC_QUOTE_LINE_ProcurementSupplierQuoteId_LineNo",
                table: "RII_PC_QUOTE_LINE",
                columns: new[] { "ProcurementSupplierQuoteId", "LineNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RII_PC_REQUEST_BranchCode_RequestNo",
                table: "RII_PC_REQUEST",
                columns: new[] { "BranchCode", "RequestNo" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_PC_REQUEST_BranchCode_Status_RequestDate",
                table: "RII_PC_REQUEST",
                columns: new[] { "BranchCode", "Status", "RequestDate" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_PC_REQUEST_IsDeleted",
                table: "RII_PC_REQUEST",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_PC_REQUEST_LINE_IsDeleted",
                table: "RII_PC_REQUEST_LINE",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_PC_REQUEST_LINE_ProcurementRequestId_LineNo",
                table: "RII_PC_REQUEST_LINE",
                columns: new[] { "ProcurementRequestId", "LineNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RII_PC_RFQ_BranchCode_RfqNo",
                table: "RII_PC_RFQ",
                columns: new[] { "BranchCode", "RfqNo" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_PC_RFQ_IsDeleted",
                table: "RII_PC_RFQ",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_PC_RFQ_ProcurementRequestId",
                table: "RII_PC_RFQ",
                column: "ProcurementRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_PC_RFQ_LINE_IsDeleted",
                table: "RII_PC_RFQ_LINE",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_PC_RFQ_LINE_ProcurementRfqId_LineNo",
                table: "RII_PC_RFQ_LINE",
                columns: new[] { "ProcurementRfqId", "LineNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RII_PC_RFQ_SUPPLIER_IsDeleted",
                table: "RII_PC_RFQ_SUPPLIER",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_PC_RFQ_SUPPLIER_ProcurementRfqId_SupplierId",
                table: "RII_PC_RFQ_SUPPLIER",
                columns: new[] { "ProcurementRfqId", "SupplierId" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_PC_STATUS_HISTORY_DocumentType_DocumentId_ChangedAtUtc",
                table: "RII_PC_STATUS_HISTORY",
                columns: new[] { "DocumentType", "DocumentId", "ChangedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_PC_STATUS_HISTORY_IsDeleted",
                table: "RII_PC_STATUS_HISTORY",
                column: "IsDeleted");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RII_PC_ORDER_LINE");

            migrationBuilder.DropTable(
                name: "RII_PC_QUOTE_LINE");

            migrationBuilder.DropTable(
                name: "RII_PC_REQUEST_LINE");

            migrationBuilder.DropTable(
                name: "RII_PC_RFQ_LINE");

            migrationBuilder.DropTable(
                name: "RII_PC_RFQ_SUPPLIER");

            migrationBuilder.DropTable(
                name: "RII_PC_STATUS_HISTORY");

            migrationBuilder.DropTable(
                name: "RII_PC_ORDER");

            migrationBuilder.DropTable(
                name: "RII_PC_QUOTE");

            migrationBuilder.DropTable(
                name: "RII_PC_RFQ");

            migrationBuilder.DropTable(
                name: "RII_PC_REQUEST");

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 2600L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 2601L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 2602L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 2603L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 2604L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 2605L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2600L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2601L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2602L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2603L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2604L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2605L);
        }
    }
}
