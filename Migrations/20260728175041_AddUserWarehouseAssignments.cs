using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddUserWarehouseAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RII_USER_WAREHOUSE",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    WarehouseId = table.Column<long>(type: "bigint", nullable: false),
                    BranchCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    DeletedBy = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RII_USER_WAREHOUSE", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RII_USER_WAREHOUSE_RII_USERS_UserId",
                        column: x => x.UserId,
                        principalTable: "RII_USERS",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RII_USER_WAREHOUSE_RII_WAREHOUSE_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "RII_WAREHOUSE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RII_USER_WAREHOUSE_UserId_WarehouseId",
                table: "RII_USER_WAREHOUSE",
                columns: new[] { "UserId", "WarehouseId" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_USER_WAREHOUSE_WarehouseId",
                table: "RII_USER_WAREHOUSE",
                column: "WarehouseId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RII_USER_WAREHOUSE");
        }
    }
}
