using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class AlignWarehouseTransferDiscrepancyPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
ALTER TABLE dbo.RII_WT_HEADER ALTER COLUMN DiscrepancyPolicy nvarchar(30) NOT NULL;
UPDATE dbo.RII_WT_HEADER
SET DiscrepancyPolicy = CASE DiscrepancyPolicy
    WHEN '1' THEN 'Block'
    WHEN '2' THEN 'AllowWithReason'
    WHEN '3' THEN 'RequireApproval'
    ELSE DiscrepancyPolicy
END;
""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
UPDATE dbo.RII_WT_HEADER
SET DiscrepancyPolicy = CASE DiscrepancyPolicy
    WHEN 'Block' THEN '1'
    WHEN 'AllowWithReason' THEN '2'
    WHEN 'RequireApproval' THEN '3'
    ELSE DiscrepancyPolicy
END;
ALTER TABLE dbo.RII_WT_HEADER ALTER COLUMN DiscrepancyPolicy int NOT NULL;
""");
        }
    }
}
