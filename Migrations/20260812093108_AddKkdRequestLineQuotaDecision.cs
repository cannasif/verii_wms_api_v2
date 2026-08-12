using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddKkdRequestLineQuotaDecision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "QuotaDecision",
                table: "RII_KKD_REQUEST_LINE",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "QuotaDecisionAtUtc",
                table: "RII_KKD_REQUEST_LINE",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "QuotaDecisionByUserId",
                table: "RII_KKD_REQUEST_LINE",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "QuotaOverrideId",
                table: "RII_KKD_REQUEST_LINE",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RII_KKD_REQUEST_LINE_BranchCode_QuotaDecision",
                table: "RII_KKD_REQUEST_LINE",
                columns: new[] { "BranchCode", "QuotaDecision" },
                filter: "[QuotaDecision] = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "IX_RII_KKD_REQUEST_LINE_QuotaDecisionByUserId",
                table: "RII_KKD_REQUEST_LINE",
                column: "QuotaDecisionByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_RII_KKD_REQUEST_LINE_RII_USERS_QuotaDecisionByUserId",
                table: "RII_KKD_REQUEST_LINE",
                column: "QuotaDecisionByUserId",
                principalTable: "RII_USERS",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RII_KKD_REQUEST_LINE_RII_USERS_QuotaDecisionByUserId",
                table: "RII_KKD_REQUEST_LINE");

            migrationBuilder.DropIndex(
                name: "IX_RII_KKD_REQUEST_LINE_BranchCode_QuotaDecision",
                table: "RII_KKD_REQUEST_LINE");

            migrationBuilder.DropIndex(
                name: "IX_RII_KKD_REQUEST_LINE_QuotaDecisionByUserId",
                table: "RII_KKD_REQUEST_LINE");

            migrationBuilder.DropColumn(
                name: "QuotaDecision",
                table: "RII_KKD_REQUEST_LINE");

            migrationBuilder.DropColumn(
                name: "QuotaDecisionAtUtc",
                table: "RII_KKD_REQUEST_LINE");

            migrationBuilder.DropColumn(
                name: "QuotaDecisionByUserId",
                table: "RII_KKD_REQUEST_LINE");

            migrationBuilder.DropColumn(
                name: "QuotaOverrideId",
                table: "RII_KKD_REQUEST_LINE");
        }
    }
}
