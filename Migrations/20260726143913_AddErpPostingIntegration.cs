using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddErpPostingIntegration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "SendSerialsToErp",
                table: "RII_PROJECT_SETTINGS",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "RII_ERP_POSTING_RECORDS",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SourceType = table.Column<int>(type: "int", nullable: false),
                    SourceEntityId = table.Column<long>(type: "bigint", nullable: false),
                    SourceDocumentNo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IdempotencyKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastHttpStatusCode = table.Column<int>(type: "int", nullable: true),
                    ErpDocumentNo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ErpWaybillNo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ErpRecordNo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ErpReferenceNo = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    LastErrorCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    LastErrorMessage = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    TraceId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
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
                    table.PrimaryKey("PK_RII_ERP_POSTING_RECORDS", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RII_ERP_INTEGRATION_ATTEMPTS",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ErpPostingRecordId = table.Column<long>(type: "bigint", nullable: false),
                    AttemptNo = table.Column<int>(type: "int", nullable: false),
                    Operation = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    HttpMethod = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Endpoint = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    RequestHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    HttpStatusCode = table.Column<int>(type: "int", nullable: true),
                    IsSuccessful = table.Column<bool>(type: "bit", nullable: false),
                    CommitUncertain = table.Column<bool>(type: "bit", nullable: false),
                    DurationMs = table.Column<long>(type: "bigint", nullable: false),
                    ErrorCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    ProviderResponse = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: true),
                    TraceId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
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
                    table.PrimaryKey("PK_RII_ERP_INTEGRATION_ATTEMPTS", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RII_ERP_INTEGRATION_ATTEMPTS_RII_ERP_POSTING_RECORDS_ErpPostingRecordId",
                        column: x => x.ErpPostingRecordId,
                        principalTable: "RII_ERP_POSTING_RECORDS",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.UpdateData(
                table: "RII_PROJECT_SETTINGS",
                keyColumn: "Id",
                keyValue: 1L,
                column: "SendSerialsToErp",
                value: true);

            migrationBuilder.CreateIndex(
                name: "IX_RII_ERP_ATTEMPT_STARTED_STATUS",
                table: "RII_ERP_INTEGRATION_ATTEMPTS",
                columns: new[] { "StartedAtUtc", "IsSuccessful" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_ERP_INTEGRATION_ATTEMPTS_IsDeleted",
                table: "RII_ERP_INTEGRATION_ATTEMPTS",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "UX_RII_ERP_ATTEMPT_NO",
                table: "RII_ERP_INTEGRATION_ATTEMPTS",
                columns: new[] { "ErpPostingRecordId", "AttemptNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RII_ERP_POSTING_RECORDS_IsDeleted",
                table: "RII_ERP_POSTING_RECORDS",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_ERP_POSTING_STATUS_UPDATED",
                table: "RII_ERP_POSTING_RECORDS",
                columns: new[] { "Status", "UpdatedDate" });

            migrationBuilder.CreateIndex(
                name: "UX_RII_ERP_POSTING_SOURCE",
                table: "RII_ERP_POSTING_RECORDS",
                columns: new[] { "BranchCode", "SourceType", "SourceEntityId" },
                unique: true,
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RII_ERP_INTEGRATION_ATTEMPTS");

            migrationBuilder.DropTable(
                name: "RII_ERP_POSTING_RECORDS");

            migrationBuilder.DropColumn(
                name: "SendSerialsToErp",
                table: "RII_PROJECT_SETTINGS");
        }
    }
}
