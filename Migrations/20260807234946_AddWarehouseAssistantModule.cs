using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddWarehouseAssistantModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RII_WAREHOUSE_ASSISTANT_CONVERSATIONS",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    LastMessageAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsArchived = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
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
                    table.PrimaryKey("PK_RII_WAREHOUSE_ASSISTANT_CONVERSATIONS", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RII_WAREHOUSE_ASSISTANT_CONVERSATIONS_RII_USERS_UserId",
                        column: x => x.UserId,
                        principalTable: "RII_USERS",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_WAREHOUSE_ASSISTANT_MESSAGES",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ConversationId = table.Column<long>(type: "bigint", nullable: false),
                    Role = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Content = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    Intent = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Scope = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    ToolName = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    ResponseDataJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ContextJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CorrelationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
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
                    table.PrimaryKey("PK_RII_WAREHOUSE_ASSISTANT_MESSAGES", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RII_WAREHOUSE_ASSISTANT_MESSAGES_RII_WAREHOUSE_ASSISTANT_CONVERSATIONS_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "RII_WAREHOUSE_ASSISTANT_CONVERSATIONS",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "RII_PERMISSION_DEFINITIONS",
                columns: new[] { "Id", "AvailableOnMobile", "AvailableOnWeb", "BranchCode", "Code", "CreatedBy", "CreatedDate", "DeletedBy", "DeletedDate", "Description", "IsActive", "Name", "UpdatedBy", "UpdatedDate" },
                values: new object[] { 2700L, false, true, "0", "WMS.WAREHOUSE_ASSISTANT.QUERY_ALL_USERS", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, "Depo asistanında başka kullanıcıların ve tüm kullanıcıların denetim kayıtlarını sorgulamaya izin verir.", true, "Depo asistanında tüm kullanıcıların işlemlerini sorgula", null, null });

            migrationBuilder.InsertData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                columns: new[] { "Id", "BranchCode", "CreatedBy", "CreatedDate", "DeletedBy", "DeletedDate", "PermissionDefinitionId", "PermissionGroupId", "UpdatedBy", "UpdatedDate" },
                values: new object[] { 2700L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 2700L, 1001L, null, null });

            migrationBuilder.CreateIndex(
                name: "IX_RII_WAREHOUSE_ASSISTANT_CONVERSATIONS_IsDeleted",
                table: "RII_WAREHOUSE_ASSISTANT_CONVERSATIONS",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_WAREHOUSE_ASSISTANT_CONVERSATIONS_User_Branch_LastMessage",
                table: "RII_WAREHOUSE_ASSISTANT_CONVERSATIONS",
                columns: new[] { "UserId", "BranchCode", "IsArchived", "LastMessageAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_WAREHOUSE_ASSISTANT_MESSAGES_Conversation_CreatedDate",
                table: "RII_WAREHOUSE_ASSISTANT_MESSAGES",
                columns: new[] { "ConversationId", "CreatedDate" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_WAREHOUSE_ASSISTANT_MESSAGES_CorrelationId",
                table: "RII_WAREHOUSE_ASSISTANT_MESSAGES",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_WAREHOUSE_ASSISTANT_MESSAGES_IsDeleted",
                table: "RII_WAREHOUSE_ASSISTANT_MESSAGES",
                column: "IsDeleted");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RII_WAREHOUSE_ASSISTANT_MESSAGES");

            migrationBuilder.DropTable(
                name: "RII_WAREHOUSE_ASSISTANT_CONVERSATIONS");

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 2700L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2700L);
        }
    }
}
