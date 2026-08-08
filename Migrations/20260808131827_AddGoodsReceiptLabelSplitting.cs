using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddGoodsReceiptLabelSplitting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "ParentLabelId",
                table: "RII_GR_LABEL",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "RootLabelId",
                table: "RII_GR_LABEL",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SplitAtUtc",
                table: "RII_GR_LABEL",
                type: "datetimeoffset(7)",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SplitBy",
                table: "RII_GR_LABEL",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SplitCorrelationId",
                table: "RII_GR_LABEL",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SplitReason",
                table: "RII_GR_LABEL",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RII_GR_LABEL_PARENT_STATUS",
                table: "RII_GR_LABEL",
                columns: new[] { "ParentLabelId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_GR_LABEL_ROOT",
                table: "RII_GR_LABEL",
                column: "RootLabelId");

            migrationBuilder.CreateIndex(
                name: "UX_RII_GR_LABEL_SPLIT_CORRELATION",
                table: "RII_GR_LABEL",
                column: "SplitCorrelationId",
                unique: true,
                filter: "[SplitCorrelationId] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_RII_GR_LABEL_RII_GR_LABEL_ParentLabelId",
                table: "RII_GR_LABEL",
                column: "ParentLabelId",
                principalTable: "RII_GR_LABEL",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RII_GR_LABEL_RII_GR_LABEL_RootLabelId",
                table: "RII_GR_LABEL",
                column: "RootLabelId",
                principalTable: "RII_GR_LABEL",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RII_GR_LABEL_RII_GR_LABEL_ParentLabelId",
                table: "RII_GR_LABEL");

            migrationBuilder.DropForeignKey(
                name: "FK_RII_GR_LABEL_RII_GR_LABEL_RootLabelId",
                table: "RII_GR_LABEL");

            migrationBuilder.DropIndex(
                name: "IX_RII_GR_LABEL_PARENT_STATUS",
                table: "RII_GR_LABEL");

            migrationBuilder.DropIndex(
                name: "IX_RII_GR_LABEL_ROOT",
                table: "RII_GR_LABEL");

            migrationBuilder.DropIndex(
                name: "UX_RII_GR_LABEL_SPLIT_CORRELATION",
                table: "RII_GR_LABEL");

            migrationBuilder.DropColumn(
                name: "ParentLabelId",
                table: "RII_GR_LABEL");

            migrationBuilder.DropColumn(
                name: "RootLabelId",
                table: "RII_GR_LABEL");

            migrationBuilder.DropColumn(
                name: "SplitAtUtc",
                table: "RII_GR_LABEL");

            migrationBuilder.DropColumn(
                name: "SplitBy",
                table: "RII_GR_LABEL");

            migrationBuilder.DropColumn(
                name: "SplitCorrelationId",
                table: "RII_GR_LABEL");

            migrationBuilder.DropColumn(
                name: "SplitReason",
                table: "RII_GR_LABEL");
        }
    }
}
