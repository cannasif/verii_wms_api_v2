using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddVehicleCheckInAndSteelFlow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "VehicleCheckInId",
                table: "RII_STEEL_RECEIPT_PLAN",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RII_VEHICLE_CHECKIN_HEADER",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PlateNo = table.Column<string>(type: "nvarchar(25)", maxLength: 25, nullable: false),
                    PlateNoNormalized = table.Column<string>(type: "nvarchar(25)", maxLength: 25, nullable: false),
                    TrailerPlateNo = table.Column<string>(type: "nvarchar(25)", maxLength: 25, nullable: true),
                    TrailerPlateNoNormalized = table.Column<string>(type: "nvarchar(25)", maxLength: 25, nullable: true),
                    DriverFirstName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DriverLastName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DriverPhone = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    CarrierName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CustomerId = table.Column<long>(type: "bigint", nullable: true),
                    CustomerCodeSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CustomerNameSnapshot = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CheckedInAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    BusinessDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
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
                    table.PrimaryKey("PK_RII_VEHICLE_CHECKIN_HEADER", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RII_VEHICLE_CHECKIN_HEADER_RII_CUSTOMER_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "RII_CUSTOMER",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_VEHICLE_CHECKIN_IMAGE",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    HeaderId = table.Column<long>(type: "bigint", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    StoragePath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
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
                    table.PrimaryKey("PK_RII_VEHICLE_CHECKIN_IMAGE", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RII_VEHICLE_CHECKIN_IMAGE_RII_VEHICLE_CHECKIN_HEADER_HeaderId",
                        column: x => x.HeaderId,
                        principalTable: "RII_VEHICLE_CHECKIN_HEADER",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "RII_PERMISSION_DEFINITIONS",
                columns: new[] { "Id", "AvailableOnMobile", "AvailableOnWeb", "BranchCode", "Code", "CreatedBy", "CreatedDate", "DeletedBy", "DeletedDate", "Description", "IsActive", "Name", "UpdatedBy", "UpdatedDate" },
                values: new object[,]
                {
                    { 1060L, false, true, "0", "WMS.STEEL_RECEIPT.VEHICLE.VIEW", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "SAC araç girişlerini görüntüle", null, null },
                    { 1061L, false, true, "0", "WMS.STEEL_RECEIPT.VEHICLE.MANAGE", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "SAC araç girişlerini yönet", null, null }
                });

            migrationBuilder.InsertData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                columns: new[] { "Id", "BranchCode", "CreatedBy", "CreatedDate", "DeletedBy", "DeletedDate", "PermissionDefinitionId", "PermissionGroupId", "UpdatedBy", "UpdatedDate" },
                values: new object[,]
                {
                    { 1060L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 1060L, 1001L, null, null },
                    { 1061L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 1061L, 1001L, null, null }
                });

            migrationBuilder.CreateIndex(
                name: "IX_RII_STEEL_RECEIPT_PLAN_VehicleCheckInId",
                table: "RII_STEEL_RECEIPT_PLAN",
                column: "VehicleCheckInId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_VEHICLE_CHECKIN_HEADER_BranchCode_CheckedInAtUtc",
                table: "RII_VEHICLE_CHECKIN_HEADER",
                columns: new[] { "BranchCode", "CheckedInAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_VEHICLE_CHECKIN_HEADER_BranchCode_PlateNoNormalized_BusinessDate",
                table: "RII_VEHICLE_CHECKIN_HEADER",
                columns: new[] { "BranchCode", "PlateNoNormalized", "BusinessDate" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_VEHICLE_CHECKIN_HEADER_CustomerId",
                table: "RII_VEHICLE_CHECKIN_HEADER",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_VEHICLE_CHECKIN_HEADER_IsDeleted",
                table: "RII_VEHICLE_CHECKIN_HEADER",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_VEHICLE_CHECKIN_IMAGE_HeaderId_SortOrder",
                table: "RII_VEHICLE_CHECKIN_IMAGE",
                columns: new[] { "HeaderId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_VEHICLE_CHECKIN_IMAGE_IsDeleted",
                table: "RII_VEHICLE_CHECKIN_IMAGE",
                column: "IsDeleted");

            migrationBuilder.AddForeignKey(
                name: "FK_RII_STEEL_RECEIPT_PLAN_RII_VEHICLE_CHECKIN_HEADER_VehicleCheckInId",
                table: "RII_STEEL_RECEIPT_PLAN",
                column: "VehicleCheckInId",
                principalTable: "RII_VEHICLE_CHECKIN_HEADER",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RII_STEEL_RECEIPT_PLAN_RII_VEHICLE_CHECKIN_HEADER_VehicleCheckInId",
                table: "RII_STEEL_RECEIPT_PLAN");

            migrationBuilder.DropTable(
                name: "RII_VEHICLE_CHECKIN_IMAGE");

            migrationBuilder.DropTable(
                name: "RII_VEHICLE_CHECKIN_HEADER");

            migrationBuilder.DropIndex(
                name: "IX_RII_STEEL_RECEIPT_PLAN_VehicleCheckInId",
                table: "RII_STEEL_RECEIPT_PLAN");

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 1060L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 1061L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 1060L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 1061L);

            migrationBuilder.DropColumn(
                name: "VehicleCheckInId",
                table: "RII_STEEL_RECEIPT_PLAN");
        }
    }
}
