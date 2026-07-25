using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddStockTrackingPolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RII_STOCK_TRACKING_POLICIES",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PolicyCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Scope = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    StockId = table.Column<long>(type: "bigint", nullable: true),
                    StockGroupCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Version = table.Column<int>(type: "int", nullable: false),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    TrackingType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    RequireSerial = table.Column<bool>(type: "bit", nullable: false),
                    SerialQuantityRule = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    RequireLot = table.Column<bool>(type: "bit", nullable: false),
                    RequireManufacturingDate = table.Column<bool>(type: "bit", nullable: false),
                    RequireExpirationDate = table.Column<bool>(type: "bit", nullable: false),
                    MinimumRemainingShelfLifeDays = table.Column<int>(type: "int", nullable: true),
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
                    table.PrimaryKey("PK_RII_STOCK_TRACKING_POLICIES", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RII_STOCK_TRACKING_POLICIES_IsDeleted",
                table: "RII_STOCK_TRACKING_POLICIES",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_STOCK_TRACKING_POLICY_RESOLVE",
                table: "RII_STOCK_TRACKING_POLICIES",
                columns: new[] { "BranchCode", "Scope", "StockId", "StockGroupCode", "IsActive", "EffectiveFromUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_RII_STOCK_TRACKING_POLICY_VERSION",
                table: "RII_STOCK_TRACKING_POLICIES",
                columns: new[] { "BranchCode", "PolicyCode", "Version" },
                unique: true,
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RII_STOCK_TRACKING_POLICIES");
        }
    }
}
