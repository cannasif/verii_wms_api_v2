using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace verii_wms_api_v2.Migrations;

public partial class LinkGoodsReceiptSlipsToNetsisOrders : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(SqlServerMigrationSql.CreateOrAlterFunction(
            "dbo.RII_FN_GR_OPENORDERS_HEADER",
            HeaderFunction(includeOrderMetadata: true)));
        migrationBuilder.Sql(SqlServerMigrationSql.CreateOrAlterFunction(
            "dbo.RII_FN_GR_OPENORDERS_LINE",
            LineFunction(includeOrderMetadata: true)));
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(SqlServerMigrationSql.CreateOrAlterFunction(
            "dbo.RII_FN_GR_OPENORDERS_HEADER",
            HeaderFunction(includeOrderMetadata: false)));
        migrationBuilder.Sql(SqlServerMigrationSql.CreateOrAlterFunction(
            "dbo.RII_FN_GR_OPENORDERS_LINE",
            LineFunction(includeOrderMetadata: false)));
    }

    private static string HeaderFunction(bool includeOrderMetadata)
    {
        var projectColumns = includeOrderMetadata
            ? """
        CASE
            WHEN COUNT(DISTINCT NULLIF(LTRIM(RTRIM(S.PROJE_KODU)), '')) = 1
                THEN MAX(NULLIF(LTRIM(RTRIM(S.PROJE_KODU)), ''))
            ELSE '0'
        END AS ProjectCode,
        MIN(COALESCE(
            TRY_CONVERT(DATETIME, S.STHAR_TESTAR, 104),
            TRY_CONVERT(DATETIME, M.SIPARIS_TEST, 104))) AS DeliveryDate,
"""
            : """
        CAST(NULL AS VARCHAR(50)) AS ProjectCode,
        CAST(NULL AS DATETIME) AS DeliveryDate,
""";

        return $$"""
CREATE OR ALTER FUNCTION dbo.RII_FN_GR_OPENORDERS_HEADER
(
    @CustomerCode VARCHAR(30),
    @BranchCode VARCHAR(10) = NULL
)
RETURNS TABLE
AS
RETURN
WITH FilteredOrders AS
(
    SELECT DISTINCT S.FISNO
    FROM V3RIICO.dbo.TBLSIPATRA AS S WITH (NOLOCK)
    WHERE S.STHAR_ACIKLAMA = @CustomerCode
      AND (@BranchCode IS NULL OR @BranchCode = '' OR S.SUBE_KODU = @BranchCode)

    UNION

    SELECT DISTINCT M.FATIRS_NO
    FROM V3RIICO.dbo.TBLSIPAMAS AS M WITH (NOLOCK)
    WHERE M.CARI_KODU = @CustomerCode
      AND (@BranchCode IS NULL OR @BranchCode = '' OR M.SUBE_KODU = @BranchCode)
),
OrderTotals AS
(
    SELECT
        S.FISNO,
        MIN(S.SUBE_KODU) AS BranchCode,
        MIN(S.DEPO_KODU) AS TargetWh,
{{projectColumns}}
        MIN(S.STHAR_TARIH) AS OrderDate,
        SUM(CASE WHEN ISNULL(S.L_YEDEK9, 0) = -1 THEN S.STHAR_GCMIK2 ELSE S.STHAR_GCMIK END) AS OrderedQty,
        SUM(S.FIRMA_DOVTUT) AS DeliveredQty
    FROM V3RIICO.dbo.TBLSIPATRA AS S WITH (NOLOCK)
    INNER JOIN FilteredOrders AS F ON F.FISNO = S.FISNO
    LEFT JOIN V3RIICO.dbo.TBLSIPAMAS AS M WITH (NOLOCK)
      ON M.FATIRS_NO = S.FISNO
     AND M.FTIRSIP = S.STHAR_FTIRSIP
     AND M.SUBE_KODU = S.SUBE_KODU
     AND M.CARI_KODU = S.STHAR_ACIKLAMA
    WHERE S.STHAR_FTIRSIP = '7'
      AND S.STHAR_GCKOD = 'G'
      AND S.STHAR_HTUR <> 'K'
      AND ISNULL(S.L_YEDEK9, 0) <= 0
      AND NOT (S.REDNEDEN = 2 AND EXISTS
          (SELECT 1 FROM V3RIICO.dbo.TBLASORTIMAS AS A WITH (NOLOCK) WHERE A.ASORTIKOD = S.EKALAN1))
      AND (@BranchCode IS NULL OR @BranchCode = '' OR S.SUBE_KODU = @BranchCode)
    GROUP BY S.FISNO
    HAVING SUM(CASE WHEN ISNULL(S.L_YEDEK9, 0) = -1 THEN S.STHAR_GCMIK2 ELSE S.STHAR_GCMIK END)
         - SUM(S.FIRMA_DOVTUT) > 0
),
ActiveAllocations AS
(
    SELECT
        SD.ExternalDocumentNo,
        SUM(ISNULL(LS.AllocatedQuantity, 0)) AS PlannedQtyAllocated
    FROM dbo.RII_GR_LINE_SOURCE AS LS
    INNER JOIN dbo.RII_GR_SOURCE_DOCUMENT AS SD ON SD.Id = LS.GrSourceDocumentId
    INNER JOIN dbo.RII_GR_LINE AS L ON L.Id = LS.GrLineId
    INNER JOIN dbo.RII_GR_HEADER AS H ON H.Id = L.GrHeaderId
    WHERE LS.IsDeleted = 0 AND SD.IsDeleted = 0 AND L.IsDeleted = 0 AND H.IsDeleted = 0
      AND SD.SourceSystem = N'Netsis' AND SD.SourceDocumentType = N'PurchaseOrder'
      AND H.Status <> N'Cancelled' AND H.ErpIntegrationStatus <> N'Succeeded'
      AND (@BranchCode IS NULL OR @BranchCode = '' OR H.BranchCode = @BranchCode)
    GROUP BY SD.ExternalDocumentNo
)
SELECT
    'H' AS Mode,
    H.FISNO AS SiparisNo,
    CAST(NULL AS INT) AS OrderID,
    @CustomerCode AS CustomerCode,
    (SELECT TOP (1) C.CARI_ISIM FROM V3RIICO.dbo.TBLCASABIT AS C WITH (NOLOCK)
     WHERE C.CARI_KOD = @CustomerCode) AS CustomerName,
    H.BranchCode,
    H.TargetWh,
    H.ProjectCode,
    H.OrderDate,
    H.DeliveryDate,
    CAST(H.OrderedQty AS DECIMAL(18, 4)) AS OrderedQty,
    CAST(H.DeliveredQty AS DECIMAL(18, 4)) AS DeliveredQty,
    CAST(H.OrderedQty - H.DeliveredQty AS DECIMAL(18, 4)) AS RemainingHamax,
    CAST(ISNULL(A.PlannedQtyAllocated, 0) AS DECIMAL(18, 4)) AS PlannedQtyAllocated,
    CAST((H.OrderedQty - H.DeliveredQty) - ISNULL(A.PlannedQtyAllocated, 0) AS DECIMAL(18, 4)) AS RemainingForImport
FROM OrderTotals AS H
LEFT JOIN ActiveAllocations AS A ON A.ExternalDocumentNo = H.FISNO
WHERE (H.OrderedQty - H.DeliveredQty) - ISNULL(A.PlannedQtyAllocated, 0) > 0;
""";
    }

    private static string LineFunction(bool includeOrderMetadata)
    {
        var sourceColumns = includeOrderMetadata
            ? """
        NULLIF(LTRIM(RTRIM(S.PROJE_KODU)), '') AS ProjectCode,
        COALESCE(
            TRY_CONVERT(DATETIME, S.STHAR_TESTAR, 104),
            TRY_CONVERT(DATETIME, M.SIPARIS_TEST, 104)) AS DeliveryDate,
        CAST(ISNULL(S.STHAR_NF, 0) AS DECIMAL(28, 8)) AS NetUnitPrice,
        CAST(ISNULL(S.STHAR_BF, 0) AS DECIMAL(28, 8)) AS GrossUnitPrice,
"""
            : """
        CAST(NULL AS VARCHAR(50)) AS ProjectCode,
        CAST(NULL AS DATETIME) AS DeliveryDate,
        CAST(0 AS DECIMAL(28, 8)) AS NetUnitPrice,
        CAST(0 AS DECIMAL(28, 8)) AS GrossUnitPrice,
""";

        return $$"""
CREATE OR ALTER FUNCTION dbo.RII_FN_GR_OPENORDERS_LINE
(
    @SiparisNoCsv NVARCHAR(MAX) = NULL,
    @CustomerCode VARCHAR(30) = NULL,
    @BranchCode VARCHAR(10) = NULL
)
RETURNS TABLE
AS
RETURN
WITH OrderRows AS
(
    SELECT
        S.FISNO AS FisNo,
        S.SUBE_KODU AS BranchCode,
        S.DEPO_KODU AS TargetWh,
{{sourceColumns}}
        S.STHAR_TARIH AS OrderDate,
        S.INCKEYNO AS OrderID,
        S.SIRA AS OrderLineSequence,
        S.STOK_KODU AS StockCode,
        ST.STOK_ADI AS StockName,
        ST.OLCU_BR1 AS UnitCode,
        COALESCE(M.CARI_KODU, S.STHAR_ACIKLAMA) AS CustomerCode,
        C.CARI_ISIM AS CustomerName,
        CAST(CASE WHEN ISNULL(S.L_YEDEK9, 0) = -1 THEN S.STHAR_GCMIK2 ELSE S.STHAR_GCMIK END AS DECIMAL(18, 4)) AS OrderedQty,
        CAST(S.FIRMA_DOVTUT AS DECIMAL(18, 4)) AS DeliveredQty
    FROM V3RIICO.dbo.TBLSIPATRA AS S WITH (NOLOCK)
    LEFT JOIN V3RIICO.dbo.TBLSIPAMAS AS M WITH (NOLOCK)
      ON M.FATIRS_NO = S.FISNO
     AND M.FTIRSIP = S.STHAR_FTIRSIP
     AND M.SUBE_KODU = S.SUBE_KODU
     AND M.CARI_KODU = S.STHAR_ACIKLAMA
    LEFT JOIN V3RIICO.dbo.TBLCASABIT AS C WITH (NOLOCK)
      ON C.CARI_KOD = COALESCE(M.CARI_KODU, S.STHAR_ACIKLAMA)
    LEFT JOIN V3RIICO.dbo.TBLASORTIMAS AS A WITH (NOLOCK) ON A.ASORTIKOD = S.EKALAN1
    LEFT JOIN V3RIICO.dbo.TBLSTSABIT AS ST WITH (NOLOCK) ON ST.STOK_KODU = S.STOK_KODU
    WHERE S.STHAR_FTIRSIP = '7'
      AND S.STHAR_GCKOD = 'G'
      AND S.STHAR_HTUR <> 'K'
      AND ISNULL(S.L_YEDEK9, 0) <= 0
      AND (A.ASORTIKOD IS NULL OR S.REDNEDEN <> 2)
      AND (CASE WHEN ISNULL(S.L_YEDEK9, 0) = -1 THEN S.STHAR_GCMIK2 ELSE S.STHAR_GCMIK END) - S.FIRMA_DOVTUT > 0
      AND (@CustomerCode IS NULL OR @CustomerCode = '' OR COALESCE(M.CARI_KODU, S.STHAR_ACIKLAMA) = @CustomerCode)
      AND (@BranchCode IS NULL OR @BranchCode = '' OR S.SUBE_KODU = @BranchCode)
      AND (NULLIF(REPLACE(LTRIM(RTRIM(@SiparisNoCsv)), N' ', N''), N'') IS NULL
           OR CHARINDEX(
               N',' + LTRIM(RTRIM(CONVERT(NVARCHAR(100), S.FISNO))) + N',',
               N',' + REPLACE(@SiparisNoCsv, N' ', N'') + N',') > 0)
),
OrderLines AS
(
    SELECT
        FisNo, BranchCode, TargetWh, MAX(ProjectCode) AS ProjectCode,
        MIN(OrderDate) AS OrderDate, MIN(DeliveryDate) AS DeliveryDate,
        MAX(NetUnitPrice) AS NetUnitPrice, MAX(GrossUnitPrice) AS GrossUnitPrice,
        OrderID, MAX(OrderLineSequence) AS OrderLineSequence,
        StockCode, MAX(StockName) AS StockName, MAX(UnitCode) AS UnitCode,
        MAX(CustomerCode) AS CustomerCode, MAX(CustomerName) AS CustomerName,
        SUM(OrderedQty) AS OrderedQty, SUM(DeliveredQty) AS DeliveredQty,
        CAST(SUM(OrderedQty - DeliveredQty) AS DECIMAL(18, 4)) AS RemainingHamax
    FROM OrderRows
    GROUP BY FisNo, BranchCode, TargetWh, OrderID, StockCode
),
ActiveAllocations AS
(
    SELECT
        LS.ExternalLineId,
        SUM(ISNULL(LS.AllocatedQuantity, 0)) AS PlannedQtyAllocated
    FROM dbo.RII_GR_LINE_SOURCE AS LS
    INNER JOIN dbo.RII_GR_SOURCE_DOCUMENT AS SD ON SD.Id = LS.GrSourceDocumentId
    INNER JOIN dbo.RII_GR_LINE AS L ON L.Id = LS.GrLineId
    INNER JOIN dbo.RII_GR_HEADER AS H ON H.Id = L.GrHeaderId
    WHERE LS.IsDeleted = 0 AND SD.IsDeleted = 0 AND L.IsDeleted = 0 AND H.IsDeleted = 0
      AND SD.SourceSystem = N'Netsis' AND SD.SourceDocumentType = N'PurchaseOrder'
      AND H.Status <> N'Cancelled' AND H.ErpIntegrationStatus <> N'Succeeded'
      AND (@BranchCode IS NULL OR @BranchCode = '' OR H.BranchCode = @BranchCode)
    GROUP BY LS.ExternalLineId
)
SELECT
    'L' AS Mode,
    X.FisNo AS SiparisNo,
    X.OrderID,
    X.OrderLineSequence,
    X.StockCode,
    X.StockName,
    X.UnitCode,
    CAST('' AS VARCHAR(100)) AS YapKod,
    CAST('' AS VARCHAR(100)) AS YapAcik,
    X.CustomerCode,
    X.CustomerName,
    X.BranchCode,
    X.TargetWh,
    X.ProjectCode,
    X.OrderDate,
    X.DeliveryDate,
    X.NetUnitPrice,
    X.GrossUnitPrice,
    X.OrderedQty,
    X.DeliveredQty,
    X.RemainingHamax,
    CAST(ISNULL(A.PlannedQtyAllocated, 0) AS DECIMAL(18, 4)) AS PlannedQtyAllocated,
    CAST(X.RemainingHamax - ISNULL(A.PlannedQtyAllocated, 0) AS DECIMAL(18, 4)) AS RemainingForImport
FROM OrderLines AS X
LEFT JOIN ActiveAllocations AS A ON A.ExternalLineId = CONVERT(NVARCHAR(100), X.OrderID);
""";
    }
}
