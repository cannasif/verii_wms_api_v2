using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddIdentitySecuritySessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RefreshToken",
                table: "RII_USERS");

            migrationBuilder.DropColumn(
                name: "RefreshTokenExpiresAt",
                table: "RII_USERS");

            migrationBuilder.AddColumn<int>(
                name: "TokenVersion",
                table: "RII_USERS",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "RII_PASSWORD_RESET_TOKENS",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    TokenHash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ConsumedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RequestedByIp = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
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
                    table.PrimaryKey("PK_RII_PASSWORD_RESET_TOKENS", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RII_PASSWORD_RESET_TOKENS_RII_USERS_UserId",
                        column: x => x.UserId,
                        principalTable: "RII_USERS",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RII_REFRESH_TOKEN_SESSIONS",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    FamilyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TokenHash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RevokedReason = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ReplacedByTokenHash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: true),
                    CreatedByIp = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    RevokedByIp = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    UserAgent = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
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
                    table.PrimaryKey("PK_RII_REFRESH_TOKEN_SESSIONS", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RII_REFRESH_TOKEN_SESSIONS_RII_USERS_UserId",
                        column: x => x.UserId,
                        principalTable: "RII_USERS",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                table: "RII_USERS",
                keyColumn: "Id",
                keyValue: 1L,
                column: "TokenVersion",
                value: 1);

            migrationBuilder.CreateIndex(
                name: "IX_RII_PASSWORD_RESET_TOKENS_IsDeleted",
                table: "RII_PASSWORD_RESET_TOKENS",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_PASSWORD_RESET_TOKENS_TokenHash",
                table: "RII_PASSWORD_RESET_TOKENS",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RII_PASSWORD_RESET_TOKENS_UserId_ExpiresAt",
                table: "RII_PASSWORD_RESET_TOKENS",
                columns: new[] { "UserId", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_REFRESH_TOKEN_SESSIONS_ExpiresAt",
                table: "RII_REFRESH_TOKEN_SESSIONS",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_RII_REFRESH_TOKEN_SESSIONS_IsDeleted",
                table: "RII_REFRESH_TOKEN_SESSIONS",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_REFRESH_TOKEN_SESSIONS_TokenHash",
                table: "RII_REFRESH_TOKEN_SESSIONS",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RII_REFRESH_TOKEN_SESSIONS_UserId_FamilyId",
                table: "RII_REFRESH_TOKEN_SESSIONS",
                columns: new[] { "UserId", "FamilyId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RII_PASSWORD_RESET_TOKENS");

            migrationBuilder.DropTable(
                name: "RII_REFRESH_TOKEN_SESSIONS");

            migrationBuilder.DropColumn(
                name: "TokenVersion",
                table: "RII_USERS");

            migrationBuilder.AddColumn<string>(
                name: "RefreshToken",
                table: "RII_USERS",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RefreshTokenExpiresAt",
                table: "RII_USERS",
                type: "datetime2",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "RII_USERS",
                keyColumn: "Id",
                keyValue: 1L,
                columns: new[] { "RefreshToken", "RefreshTokenExpiresAt" },
                values: new object[] { null, null });
        }
    }
}
