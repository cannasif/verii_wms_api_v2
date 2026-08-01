using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class ShowFullyAllocatedGoodsReceiptOpenOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
DECLARE @definition nvarchar(max) = OBJECT_DEFINITION(OBJECT_ID(N'dbo.RII_FN_GR_OPENORDERS_LINE'));
DECLARE @availabilityFilter nvarchar(300) =
    N'WHERE X.RemainingHamax - ISNULL(A.PlannedQtyAllocated, 0) > 0;';

IF @definition IS NULL
BEGIN
    ;THROW 50001, 'RII_FN_GR_OPENORDERS_LINE not found.', 1;
END;
IF CHARINDEX(@availabilityFilter, @definition) = 0
BEGIN
    ;THROW 50002, 'RII_FN_GR_OPENORDERS_LINE availability filter not found.', 1;
END;

SET @definition =
    N'ALTER ' + SUBSTRING(@definition, CHARINDEX(N'FUNCTION', UPPER(@definition)), LEN(@definition));
SET @definition = REPLACE(@definition, @availabilityFilter, N';');
EXEC sys.sp_executesql @definition;
""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
DECLARE @definition nvarchar(max) = OBJECT_DEFINITION(OBJECT_ID(N'dbo.RII_FN_GR_OPENORDERS_LINE'));
DECLARE @unfilteredTail nvarchar(400) =
    N'LEFT JOIN ActiveAllocations AS A ON A.ExternalLineId = CONVERT(NVARCHAR(100), X.OrderID);';

IF @definition IS NULL
BEGIN
    ;THROW 50001, 'RII_FN_GR_OPENORDERS_LINE not found.', 1;
END;
IF CHARINDEX(@unfilteredTail, @definition) = 0
BEGIN
    ;THROW 50002, 'RII_FN_GR_OPENORDERS_LINE unfiltered tail not found.', 1;
END;

SET @definition =
    N'ALTER ' + SUBSTRING(@definition, CHARINDEX(N'FUNCTION', UPPER(@definition)), LEN(@definition));
SET @definition = REPLACE(
    @definition,
    @unfilteredTail,
    N'LEFT JOIN ActiveAllocations AS A ON A.ExternalLineId = CONVERT(NVARCHAR(100), X.OrderID);'
    + CHAR(13) + CHAR(10)
    + N'WHERE X.RemainingHamax - ISNULL(A.PlannedQtyAllocated, 0) > 0;');
EXEC sys.sp_executesql @definition;
""");
        }
    }
}
