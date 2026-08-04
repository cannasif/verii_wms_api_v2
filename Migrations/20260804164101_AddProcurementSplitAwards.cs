using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddProcurementSplitAwards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_RII_PC_QUOTE_LINE_AMOUNTS",
                table: "RII_PC_QUOTE_LINE");

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "RII_PC_REQUEST_LINE",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<decimal>(
                name: "ConvertedQuantity",
                table: "RII_PC_QUOTE_LINE",
                type: "decimal(20,6)",
                precision: 20,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "RII_PC_QUOTE_LINE",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.CreateTable(
                name: "RII_PC_POLICY",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PolicyKey = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    AllowMultipleRfqsPerRequest = table.Column<bool>(type: "bit", nullable: false),
                    AllowPartialRfqLines = table.Column<bool>(type: "bit", nullable: false),
                    AllowMultipleQuotesPerSupplier = table.Column<bool>(type: "bit", nullable: false),
                    AllowMultipleOrdersPerQuote = table.Column<bool>(type: "bit", nullable: false),
                    AllowPartialOrderLines = table.Column<bool>(type: "bit", nullable: false),
                    AllowSplitAwardsAcrossSuppliers = table.Column<bool>(type: "bit", nullable: false),
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
                    table.PrimaryKey("PK_RII_PC_POLICY", x => x.Id);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_RII_PC_QUOTE_LINE_AMOUNTS",
                table: "RII_PC_QUOTE_LINE",
                sql: "[QuotedQuantity] > 0 AND [ConvertedQuantity] >= 0 AND [ConvertedQuantity] <= [QuotedQuantity] AND [UnitPrice] >= 0 AND [DiscountRate] >= 0 AND [DiscountRate] <= 100 AND [VatRate] >= 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_PC_POLICY_BranchCode_PolicyKey",
                table: "RII_PC_POLICY",
                columns: new[] { "BranchCode", "PolicyKey" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_PC_POLICY_IsDeleted",
                table: "RII_PC_POLICY",
                column: "IsDeleted");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RII_PC_POLICY");

            migrationBuilder.DropCheckConstraint(
                name: "CK_RII_PC_QUOTE_LINE_AMOUNTS",
                table: "RII_PC_QUOTE_LINE");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "RII_PC_REQUEST_LINE");

            migrationBuilder.DropColumn(
                name: "ConvertedQuantity",
                table: "RII_PC_QUOTE_LINE");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "RII_PC_QUOTE_LINE");

            migrationBuilder.AddCheckConstraint(
                name: "CK_RII_PC_QUOTE_LINE_AMOUNTS",
                table: "RII_PC_QUOTE_LINE",
                sql: "[QuotedQuantity] > 0 AND [UnitPrice] >= 0 AND [DiscountRate] >= 0 AND [DiscountRate] <= 100 AND [VatRate] >= 0");
        }
    }
}
