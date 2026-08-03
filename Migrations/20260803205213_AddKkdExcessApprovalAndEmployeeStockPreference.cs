using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddKkdExcessApprovalAndEmployeeStockPreference : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProjectCode",
                table: "RII_WT_HEADER",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RequireManagerApprovalForExcess",
                table: "RII_KKD_POLICY",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "ExcessApprovalReason",
                table: "RII_KKD_DISTRIBUTION",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExcessApprovalStatus",
                table: "RII_KKD_DISTRIBUTION",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "NotRequired");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ExcessApprovedAtUtc",
                table: "RII_KKD_DISTRIBUTION",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ExcessApprovedBy",
                table: "RII_KKD_DISTRIBUTION",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RII_KKD_EMPLOYEE_STOCK_PREFERENCE",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EmployeeId = table.Column<long>(type: "bigint", nullable: false),
                    GroupCode = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    StockId = table.Column<long>(type: "bigint", nullable: false),
                    LastSelectedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
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
                    table.PrimaryKey("PK_RII_KKD_EMPLOYEE_STOCK_PREFERENCE", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RII_KKD_EMPLOYEE_STOCK_PREFERENCE_RII_KKD_EMPLOYEE_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "RII_KKD_EMPLOYEE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RII_KKD_EMPLOYEE_STOCK_PREFERENCE_BranchCode_EmployeeId_GroupCode",
                table: "RII_KKD_EMPLOYEE_STOCK_PREFERENCE",
                columns: new[] { "BranchCode", "EmployeeId", "GroupCode" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_KKD_EMPLOYEE_STOCK_PREFERENCE_BranchCode_StockId",
                table: "RII_KKD_EMPLOYEE_STOCK_PREFERENCE",
                columns: new[] { "BranchCode", "StockId" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_KKD_EMPLOYEE_STOCK_PREFERENCE_EmployeeId",
                table: "RII_KKD_EMPLOYEE_STOCK_PREFERENCE",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_KKD_EMPLOYEE_STOCK_PREFERENCE_IsDeleted",
                table: "RII_KKD_EMPLOYEE_STOCK_PREFERENCE",
                column: "IsDeleted");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RII_KKD_EMPLOYEE_STOCK_PREFERENCE");

            migrationBuilder.DropColumn(
                name: "ProjectCode",
                table: "RII_WT_HEADER");

            migrationBuilder.DropColumn(
                name: "RequireManagerApprovalForExcess",
                table: "RII_KKD_POLICY");

            migrationBuilder.DropColumn(
                name: "ExcessApprovalReason",
                table: "RII_KKD_DISTRIBUTION");

            migrationBuilder.DropColumn(
                name: "ExcessApprovalStatus",
                table: "RII_KKD_DISTRIBUTION");

            migrationBuilder.DropColumn(
                name: "ExcessApprovedAtUtc",
                table: "RII_KKD_DISTRIBUTION");

            migrationBuilder.DropColumn(
                name: "ExcessApprovedBy",
                table: "RII_KKD_DISTRIBUTION");
        }
    }
}
