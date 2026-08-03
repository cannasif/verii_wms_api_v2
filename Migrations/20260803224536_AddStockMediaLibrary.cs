using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddStockMediaLibrary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RII_STOCK_IMAGE",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StockId = table.Column<long>(type: "bigint", nullable: false),
                    FileUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    OriginalFileName = table.Column<string>(type: "nvarchar(240)", maxLength: 240, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    FileLength = table.Column<long>(type: "bigint", nullable: false),
                    AltText = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsPrimary = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
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
                    table.PrimaryKey("PK_RII_STOCK_IMAGE", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RII_STOCK_IMAGE_RII_STOCK_StockId",
                        column: x => x.StockId,
                        principalTable: "RII_STOCK",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RII_STOCK_IMAGE_IsDeleted",
                table: "RII_STOCK_IMAGE",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_STOCK_IMAGE_StockId",
                table: "RII_STOCK_IMAGE",
                column: "StockId");

            migrationBuilder.CreateIndex(
                name: "UX_StockImage_Branch_Stock_SortOrder",
                table: "RII_STOCK_IMAGE",
                columns: new[] { "BranchCode", "StockId", "SortOrder" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_StockImage_OnePrimary",
                table: "RII_STOCK_IMAGE",
                columns: new[] { "BranchCode", "StockId" },
                unique: true,
                filter: "[IsDeleted] = 0 AND [IsPrimary] = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RII_STOCK_IMAGE");
        }
    }
}
