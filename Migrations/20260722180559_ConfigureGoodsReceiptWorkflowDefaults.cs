using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class ConfigureGoodsReceiptWorkflowDefaults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
INSERT INTO dbo.RII_LOCATION
    (WarehouseId, ParentLocationId, Code, Name, LocationType, BarcodeEntryMode, Barcode, ZoneCode,
     AllowMixedStock, AllowMixedLot, AllowMixedStatus, AllowCycleCount, IsPickable, IsPutaway, IsQuarantine,
     IsActive, Description, BranchCode, CreatedDate, IsDeleted)
SELECT W.Id, NULL, N'KABUL', N'Mal Kabul Alanı', N'Receiving', N'Auto', NULL, N'RECEIVING',
       1, 1, 1, 0, 0, 0, 0, 1, N'SYSTEM:GOODS_RECEIPT_DEFAULT', W.BranchCode, SYSUTCDATETIME(), 0
FROM dbo.RII_WAREHOUSE AS W
WHERE W.IsDeleted = 0
  AND NOT EXISTS (SELECT 1 FROM dbo.RII_LOCATION AS L WHERE L.WarehouseId = W.Id AND L.Code = N'KABUL' AND L.IsDeleted = 0);

IF COL_LENGTH(N'dbo.RII_DOCUMENT_SERIES', N'WarehouseId') IS NOT NULL
   AND COL_LENGTH(N'dbo.RII_DOCUMENT_SERIES', N'Separator') IS NOT NULL
BEGIN
    EXEC sys.sp_executesql N'
        INSERT INTO dbo.RII_DOCUMENT_SERIES
            (WarehouseId, Code, Name, DocumentType, Prefix, Separator, YearFormat, NumberLength, StartNumber, NextNumber,
             IncrementBy, IsDefault, IsActive, HasIssuedNumbers, Description, BranchCode, CreatedDate, IsDeleted)
        SELECT W.Id,
               LEFT(CONCAT(N''GR-'', W.WarehouseCode), 20),
               CONCAT(N''Mal Kabul '', W.WarehouseName),
               N''GoodsReceipt'',
               LEFT(CONCAT(N''GR'', W.WarehouseCode), 10),
               N''-'', N''FourDigit'', 8, 1, 1, 1, 1, 1, 0,
               N''SYSTEM:GOODS_RECEIPT_DEFAULT'', W.BranchCode, SYSUTCDATETIME(), 0
        FROM dbo.RII_WAREHOUSE AS W
        WHERE W.IsDeleted = 0
          AND NOT EXISTS
          (
              SELECT 1
              FROM dbo.RII_DOCUMENT_SERIES AS S
              WHERE S.WarehouseId = W.Id
                AND S.DocumentType = N''GoodsReceipt''
                AND S.IsDeleted = 0
          );';
END
ELSE
BEGIN
    EXEC sys.sp_executesql N'
        INSERT INTO dbo.RII_DOCUMENT_SERIES
            (Code, Name, DocumentType, Prefix, YearFormat, NumberLength, StartNumber, NextNumber,
             IncrementBy, IsDefault, IsActive, HasIssuedNumbers, Description, BranchCode, CreatedDate, IsDeleted)
        SELECT LEFT(CONCAT(N''GR-'', W.WarehouseCode), 20),
               CONCAT(N''Mal Kabul '', W.WarehouseName),
               N''GoodsReceipt'',
               LEFT(CONCAT(N''GR'', W.WarehouseCode), 10),
               N''FourDigit'', 8, 1, 1, 1, 1, 1, 0,
               N''SYSTEM:GOODS_RECEIPT_DEFAULT'', W.BranchCode, SYSUTCDATETIME(), 0
        FROM dbo.RII_WAREHOUSE AS W
        WHERE W.IsDeleted = 0
          AND W.Id =
          (
              SELECT MIN(candidate.Id)
              FROM dbo.RII_WAREHOUSE AS candidate
              WHERE candidate.BranchCode = W.BranchCode
                AND candidate.IsDeleted = 0
          )
          AND NOT EXISTS
          (
              SELECT 1
              FROM dbo.RII_DOCUMENT_SERIES AS S
              WHERE S.BranchCode = W.BranchCode
                AND S.DocumentType = N''GoodsReceipt''
                AND S.IsDeleted = 0
          );';
END;
""");

            migrationBuilder.Sql("""
DECLARE @definition nvarchar(max) = OBJECT_DEFINITION(OBJECT_ID(N'dbo.RII_FN_GR_OPENORDERS_LINE'));
IF @definition IS NULL
BEGIN
    ;THROW 50001, 'RII_FN_GR_OPENORDERS_LINE not found.', 1;
END;
IF @definition NOT LIKE N'%AS UnitCode%'
BEGIN
    SET @definition = N'ALTER ' + SUBSTRING(@definition, CHARINDEX(N'FUNCTION', UPPER(@definition)), LEN(@definition));
    SET @definition = REPLACE(@definition, N'ST.STOK_ADI AS StockName,', N'ST.STOK_ADI AS StockName,' + CHAR(13) + CHAR(10) + N'        ST.OLCU_BR1 AS UnitCode,');
    SET @definition = REPLACE(@definition, N'MAX(StockName) AS StockName,', N'MAX(StockName) AS StockName, MAX(UnitCode) AS UnitCode,');
    SET @definition = REPLACE(@definition, N'    X.StockName,', N'    X.StockName,' + CHAR(13) + CHAR(10) + N'    X.UnitCode,');
    EXEC sys.sp_executesql @definition;
END;
""");

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
DELETE FROM dbo.RII_DOCUMENT_SERIES WHERE Description = N'SYSTEM:GOODS_RECEIPT_DEFAULT' AND HasIssuedNumbers = 0;
DELETE FROM dbo.RII_LOCATION WHERE Description = N'SYSTEM:GOODS_RECEIPT_DEFAULT';
""");

        }
    }
}
