using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddGeneratorQualityGate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RII_GP_QUALITY_GATE",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OperationId = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DecisionBy = table.Column<long>(type: "bigint", nullable: true),
                    DecisionAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DecisionNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
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
                    table.PrimaryKey("PK_RII_GP_QUALITY_GATE", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RII_GP_QUALITY_GATE_RII_GP_OPERATION_OperationId",
                        column: x => x.OperationId,
                        principalTable: "RII_GP_OPERATION",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RII_GP_QUALITY_GATE_BranchCode_Status_RequestedAtUtc",
                table: "RII_GP_QUALITY_GATE",
                columns: new[] { "BranchCode", "Status", "RequestedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_GP_QUALITY_GATE_IsDeleted",
                table: "RII_GP_QUALITY_GATE",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_GP_QUALITY_GATE_OperationId",
                table: "RII_GP_QUALITY_GATE",
                column: "OperationId",
                unique: true,
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RII_GP_QUALITY_GATE");
        }
    }
}
