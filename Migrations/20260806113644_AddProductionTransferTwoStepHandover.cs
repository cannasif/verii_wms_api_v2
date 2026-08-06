using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddProductionTransferTwoStepHandover : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "HandedOverQuantity",
                table: "RII_PT_LINE_LINK",
                type: "decimal(20,6)",
                precision: 20,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ShortClosedQuantity",
                table: "RII_PT_LINE_LINK",
                type: "decimal(20,6)",
                precision: 20,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "HandoverConfirmedAtUtc",
                table: "RII_PT_HEADER_LINK",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "HandoverConfirmedBy",
                table: "RII_PT_HEADER_LINK",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HandoverShortageReason",
                table: "RII_PT_HEADER_LINK",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LastHandoverIdempotencyKey",
                table: "RII_PT_HEADER_LINK",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LastPickingCompletionIdempotencyKey",
                table: "RII_PT_HEADER_LINK",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ParentWarehouseTransferHeaderId",
                table: "RII_PT_HEADER_LINK",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestedByNameSnapshot",
                table: "RII_PT_HEADER_LINK",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "RequestedByUserId",
                table: "RII_PT_HEADER_LINK",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ResidualWarehouseTransferHeaderId",
                table: "RII_PT_HEADER_LINK",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WorkflowStatus",
                table: "RII_PT_HEADER_LINK",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "Planned");

            migrationBuilder.CreateIndex(
                name: "IX_RII_PT_HEADER_LINK_ParentWarehouseTransferHeaderId",
                table: "RII_PT_HEADER_LINK",
                column: "ParentWarehouseTransferHeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_PT_HEADER_LINK_ResidualWarehouseTransferHeaderId",
                table: "RII_PT_HEADER_LINK",
                column: "ResidualWarehouseTransferHeaderId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RII_PT_HEADER_LINK_ParentWarehouseTransferHeaderId",
                table: "RII_PT_HEADER_LINK");

            migrationBuilder.DropIndex(
                name: "IX_RII_PT_HEADER_LINK_ResidualWarehouseTransferHeaderId",
                table: "RII_PT_HEADER_LINK");

            migrationBuilder.DropColumn(
                name: "HandedOverQuantity",
                table: "RII_PT_LINE_LINK");

            migrationBuilder.DropColumn(
                name: "ShortClosedQuantity",
                table: "RII_PT_LINE_LINK");

            migrationBuilder.DropColumn(
                name: "HandoverConfirmedAtUtc",
                table: "RII_PT_HEADER_LINK");

            migrationBuilder.DropColumn(
                name: "HandoverConfirmedBy",
                table: "RII_PT_HEADER_LINK");

            migrationBuilder.DropColumn(
                name: "HandoverShortageReason",
                table: "RII_PT_HEADER_LINK");

            migrationBuilder.DropColumn(
                name: "LastHandoverIdempotencyKey",
                table: "RII_PT_HEADER_LINK");

            migrationBuilder.DropColumn(
                name: "LastPickingCompletionIdempotencyKey",
                table: "RII_PT_HEADER_LINK");

            migrationBuilder.DropColumn(
                name: "ParentWarehouseTransferHeaderId",
                table: "RII_PT_HEADER_LINK");

            migrationBuilder.DropColumn(
                name: "RequestedByNameSnapshot",
                table: "RII_PT_HEADER_LINK");

            migrationBuilder.DropColumn(
                name: "RequestedByUserId",
                table: "RII_PT_HEADER_LINK");

            migrationBuilder.DropColumn(
                name: "ResidualWarehouseTransferHeaderId",
                table: "RII_PT_HEADER_LINK");

            migrationBuilder.DropColumn(
                name: "WorkflowStatus",
                table: "RII_PT_HEADER_LINK");
        }
    }
}
