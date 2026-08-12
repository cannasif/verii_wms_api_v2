using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddQualityWarehouseRouting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RII_QUALITY_WAREHOUSE_ROUTES",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    QualityParameterId = table.Column<long>(type: "bigint", nullable: false),
                    SourceWarehouseId = table.Column<long>(type: "bigint", nullable: false),
                    QualityLocationId = table.Column<long>(type: "bigint", nullable: true),
                    AcceptedLocationId = table.Column<long>(type: "bigint", nullable: true),
                    QuarantineLocationId = table.Column<long>(type: "bigint", nullable: true),
                    RejectLocationId = table.Column<long>(type: "bigint", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
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
                    table.PrimaryKey("PK_RII_QUALITY_WAREHOUSE_ROUTES", x => x.Id);
                    table.CheckConstraint("CK_RII_QUALITY_WAREHOUSE_ROUTE_TARGET", "[QualityLocationId] IS NOT NULL OR [AcceptedLocationId] IS NOT NULL OR [QuarantineLocationId] IS NOT NULL OR [RejectLocationId] IS NOT NULL");
                    table.ForeignKey(
                        name: "FK_RII_QUALITY_WAREHOUSE_ROUTES_RII_LOCATION_AcceptedLocationId",
                        column: x => x.AcceptedLocationId,
                        principalTable: "RII_LOCATION",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_QUALITY_WAREHOUSE_ROUTES_RII_LOCATION_QualityLocationId",
                        column: x => x.QualityLocationId,
                        principalTable: "RII_LOCATION",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_QUALITY_WAREHOUSE_ROUTES_RII_LOCATION_QuarantineLocationId",
                        column: x => x.QuarantineLocationId,
                        principalTable: "RII_LOCATION",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_QUALITY_WAREHOUSE_ROUTES_RII_LOCATION_RejectLocationId",
                        column: x => x.RejectLocationId,
                        principalTable: "RII_LOCATION",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_QUALITY_WAREHOUSE_ROUTES_RII_QUALITY_PARAMETERS_QualityParameterId",
                        column: x => x.QualityParameterId,
                        principalTable: "RII_QUALITY_PARAMETERS",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_QUALITY_WAREHOUSE_ROUTES_RII_WAREHOUSE_SourceWarehouseId",
                        column: x => x.SourceWarehouseId,
                        principalTable: "RII_WAREHOUSE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RII_QUALITY_WAREHOUSE_ROUTES_AcceptedLocationId",
                table: "RII_QUALITY_WAREHOUSE_ROUTES",
                column: "AcceptedLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_QUALITY_WAREHOUSE_ROUTES_BranchCode_SourceWarehouseId_IsActive",
                table: "RII_QUALITY_WAREHOUSE_ROUTES",
                columns: new[] { "BranchCode", "SourceWarehouseId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_QUALITY_WAREHOUSE_ROUTES_IsDeleted",
                table: "RII_QUALITY_WAREHOUSE_ROUTES",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_QUALITY_WAREHOUSE_ROUTES_QualityLocationId",
                table: "RII_QUALITY_WAREHOUSE_ROUTES",
                column: "QualityLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_QUALITY_WAREHOUSE_ROUTES_QualityParameterId_SourceWarehouseId",
                table: "RII_QUALITY_WAREHOUSE_ROUTES",
                columns: new[] { "QualityParameterId", "SourceWarehouseId" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_QUALITY_WAREHOUSE_ROUTES_QuarantineLocationId",
                table: "RII_QUALITY_WAREHOUSE_ROUTES",
                column: "QuarantineLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_QUALITY_WAREHOUSE_ROUTES_RejectLocationId",
                table: "RII_QUALITY_WAREHOUSE_ROUTES",
                column: "RejectLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_QUALITY_WAREHOUSE_ROUTES_SourceWarehouseId",
                table: "RII_QUALITY_WAREHOUSE_ROUTES",
                column: "SourceWarehouseId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RII_QUALITY_WAREHOUSE_ROUTES");
        }
    }
}
