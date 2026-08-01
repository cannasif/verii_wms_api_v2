using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddGoodsReceiptProcessType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProcessType",
                table: "RII_GR_HEADER",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "OrderBasedTask");

            // Geçmiş kayıtları teknik başlangıç biçiminden deterministik olarak iş senaryosuna taşır.
            migrationBuilder.Sql(SqlServerMigrationSql.Execute("""
                UPDATE [RII_GR_HEADER]
                SET [ProcessType] = CASE [InitiationMode]
                    WHEN N'UnplannedTask' THEN N'OrderlessTask'
                    WHEN N'DirectReceipt' THEN N'OrderlessDirectReceipt'
                    ELSE N'OrderBasedTask'
                END;
                """));

            migrationBuilder.Sql(SqlServerMigrationSql.Execute(
                "CREATE INDEX [IX_RII_GR_HEADER_PROCESS_REPORTING] ON [RII_GR_HEADER] ([BranchCode], [ProcessType], [Status], [DocumentDate]);"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RII_GR_HEADER_PROCESS_REPORTING",
                table: "RII_GR_HEADER");

            migrationBuilder.DropColumn(
                name: "ProcessType",
                table: "RII_GR_HEADER");
        }
    }
}
