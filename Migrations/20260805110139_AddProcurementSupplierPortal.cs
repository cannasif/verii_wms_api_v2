using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddProcurementSupplierPortal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "PreviousQuoteId",
                table: "RII_PC_QUOTE",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RevisionNo",
                table: "RII_PC_QUOTE",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SubmittedAtUtc",
                table: "RII_PC_QUOTE",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RII_PC_QUOTE_INVITATION",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProcurementRfqId = table.Column<long>(type: "bigint", nullable: false),
                    ProcurementRfqSupplierId = table.Column<long>(type: "bigint", nullable: false),
                    SupplierId = table.Column<long>(type: "bigint", nullable: false),
                    RecipientEmail = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    TokenHash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    FirstOpenedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastOpenedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastSentAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SubmittedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RevokedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CurrentQuoteId = table.Column<long>(type: "bigint", nullable: true),
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
                    table.PrimaryKey("PK_RII_PC_QUOTE_INVITATION", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RII_PC_QUOTE_INVITATION_RII_PC_QUOTE_CurrentQuoteId",
                        column: x => x.CurrentQuoteId,
                        principalTable: "RII_PC_QUOTE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_PC_QUOTE_INVITATION_RII_PC_RFQ_ProcurementRfqId",
                        column: x => x.ProcurementRfqId,
                        principalTable: "RII_PC_RFQ",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_PC_QUOTE_INVITATION_RII_PC_RFQ_SUPPLIER_ProcurementRfqSupplierId",
                        column: x => x.ProcurementRfqSupplierId,
                        principalTable: "RII_PC_RFQ_SUPPLIER",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RII_PC_QUOTE_INVITATION_CurrentQuoteId",
                table: "RII_PC_QUOTE_INVITATION",
                column: "CurrentQuoteId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_PC_QUOTE_INVITATION_IsDeleted",
                table: "RII_PC_QUOTE_INVITATION",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_PC_QUOTE_INVITATION_ProcurementRfqId_SupplierId",
                table: "RII_PC_QUOTE_INVITATION",
                columns: new[] { "ProcurementRfqId", "SupplierId" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_PC_QUOTE_INVITATION_ProcurementRfqSupplierId",
                table: "RII_PC_QUOTE_INVITATION",
                column: "ProcurementRfqSupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_PC_QUOTE_INVITATION_Status_ExpiresAtUtc",
                table: "RII_PC_QUOTE_INVITATION",
                columns: new[] { "Status", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_PC_QUOTE_INVITATION_TokenHash",
                table: "RII_PC_QUOTE_INVITATION",
                column: "TokenHash",
                unique: true,
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RII_PC_QUOTE_INVITATION");

            migrationBuilder.DropColumn(
                name: "PreviousQuoteId",
                table: "RII_PC_QUOTE");

            migrationBuilder.DropColumn(
                name: "RevisionNo",
                table: "RII_PC_QUOTE");

            migrationBuilder.DropColumn(
                name: "SubmittedAtUtc",
                table: "RII_PC_QUOTE");
        }
    }
}
