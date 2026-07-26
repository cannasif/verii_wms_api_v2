using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddAtomicSteelVehicleAcceptance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "VehicleAcceptanceId",
                table: "RII_STEEL_RECEIPT_PLAN_LINE",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RII_STEEL_VEHICLE_ACCEPTANCE",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IdempotencyKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VehicleCheckInId = table.Column<long>(type: "bigint", nullable: false),
                    PlateCount = table.Column<int>(type: "int", nullable: false),
                    TotalAcceptedQuantity = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    AcceptedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AcceptedBy = table.Column<long>(type: "bigint", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
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
                    table.PrimaryKey("PK_RII_STEEL_VEHICLE_ACCEPTANCE", x => x.Id);
                    table.CheckConstraint("CK_RII_STEEL_VEHICLE_ACCEPTANCE_COUNT", "[PlateCount] > 0");
                    table.CheckConstraint("CK_RII_STEEL_VEHICLE_ACCEPTANCE_QTY", "[TotalAcceptedQuantity] > 0");
                    table.ForeignKey(
                        name: "FK_RII_STEEL_VEHICLE_ACCEPTANCE_RII_VEHICLE_CHECKIN_HEADER_VehicleCheckInId",
                        column: x => x.VehicleCheckInId,
                        principalTable: "RII_VEHICLE_CHECKIN_HEADER",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RII_STEEL_RECEIPT_PLAN_LINE_VehicleAcceptanceId",
                table: "RII_STEEL_RECEIPT_PLAN_LINE",
                column: "VehicleAcceptanceId",
                filter: "[VehicleAcceptanceId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RII_STEEL_VEHICLE_ACCEPTANCE_IdempotencyKey",
                table: "RII_STEEL_VEHICLE_ACCEPTANCE",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RII_STEEL_VEHICLE_ACCEPTANCE_IsDeleted",
                table: "RII_STEEL_VEHICLE_ACCEPTANCE",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_STEEL_VEHICLE_ACCEPTANCE_VehicleCheckInId_AcceptedAtUtc",
                table: "RII_STEEL_VEHICLE_ACCEPTANCE",
                columns: new[] { "VehicleCheckInId", "AcceptedAtUtc" });

            migrationBuilder.AddForeignKey(
                name: "FK_RII_STEEL_RECEIPT_PLAN_LINE_RII_STEEL_VEHICLE_ACCEPTANCE_VehicleAcceptanceId",
                table: "RII_STEEL_RECEIPT_PLAN_LINE",
                column: "VehicleAcceptanceId",
                principalTable: "RII_STEEL_VEHICLE_ACCEPTANCE",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RII_STEEL_RECEIPT_PLAN_LINE_RII_STEEL_VEHICLE_ACCEPTANCE_VehicleAcceptanceId",
                table: "RII_STEEL_RECEIPT_PLAN_LINE");

            migrationBuilder.DropTable(
                name: "RII_STEEL_VEHICLE_ACCEPTANCE");

            migrationBuilder.DropIndex(
                name: "IX_RII_STEEL_RECEIPT_PLAN_LINE_VehicleAcceptanceId",
                table: "RII_STEEL_RECEIPT_PLAN_LINE");

            migrationBuilder.DropColumn(
                name: "VehicleAcceptanceId",
                table: "RII_STEEL_RECEIPT_PLAN_LINE");
        }
    }
}
