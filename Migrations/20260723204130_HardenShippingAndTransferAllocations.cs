using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class HardenShippingAndTransferAllocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RII_WT_LINE_SOURCE_WtLineId",
                table: "RII_WT_LINE_SOURCE");

            migrationBuilder.DropIndex(
                name: "IX_RII_WT_LINE_SOURCE_WtSourceDocumentId_ExternalLineId",
                table: "RII_WT_LINE_SOURCE");

            migrationBuilder.DropIndex(
                name: "IX_RII_SH_LINE_SOURCE_ShipmentLineId",
                table: "RII_SH_LINE_SOURCE");

            migrationBuilder.DropIndex(
                name: "IX_RII_SH_LINE_SOURCE_ShipmentSourceDocumentId_ExternalLineId",
                table: "RII_SH_LINE_SOURCE");

            migrationBuilder.CreateIndex(
                name: "IX_RII_WT_LINE_SOURCE_WtLineId_WtSourceDocumentId_ExternalLineId",
                table: "RII_WT_LINE_SOURCE",
                columns: new[] { "WtLineId", "WtSourceDocumentId", "ExternalLineId" },
                unique: true);

            migrationBuilder.Sql(SqlServerMigrationSql.Execute(
                "CREATE INDEX [IX_RII_WT_LINE_SOURCE_WtSourceDocumentId_ExternalLineId] ON [RII_WT_LINE_SOURCE] ([WtSourceDocumentId], [ExternalLineId]);"));

            migrationBuilder.CreateIndex(
                name: "IX_RII_SH_LINE_SOURCE_ShipmentLineId_ShipmentSourceDocumentId_ExternalLineId",
                table: "RII_SH_LINE_SOURCE",
                columns: new[] { "ShipmentLineId", "ShipmentSourceDocumentId", "ExternalLineId" },
                unique: true);

            migrationBuilder.Sql(SqlServerMigrationSql.Execute(
                "CREATE INDEX [IX_RII_SH_LINE_SOURCE_ShipmentSourceDocumentId_ExternalLineId] ON [RII_SH_LINE_SOURCE] ([ShipmentSourceDocumentId], [ExternalLineId]);"));

            migrationBuilder.Sql(SqlServerMigrationSql.CreateOrAlterFunction("dbo.RII_FN_SH_HEADER", """
CREATE OR ALTER FUNCTION dbo.RII_FN_SH_HEADER
(
    @CustomerCode varchar(30),
    @BranchCode varchar(10)=NULL
)
RETURNS TABLE
AS
RETURN
WITH OrderNumbers AS
(
    SELECT DISTINCT S.FISNO
    FROM V3RIICO.dbo.TBLSIPATRA S WITH (NOLOCK)
    WHERE S.STHAR_ACIKLAMA=@CustomerCode
      AND (@BranchCode IS NULL OR @BranchCode='' OR CONVERT(varchar(10),S.SUBE_KODU)=@BranchCode)
    UNION
    SELECT DISTINCT M.FATIRS_NO
    FROM V3RIICO.dbo.TBLSIPAMAS M WITH (NOLOCK)
    WHERE M.CARI_KODU=@CustomerCode
      AND (@BranchCode IS NULL OR @BranchCode='' OR CONVERT(varchar(10),M.SUBE_KODU)=@BranchCode)
),
Orders AS
(
    SELECT S.FISNO,MIN(S.SUBE_KODU) BranchCode,MIN(S.DEPO_KODU) TargetWh,MIN(S.STHAR_TARIH) OrderDate,
           SUM(CASE WHEN ISNULL(S.L_YEDEK9,0)=-1 THEN ISNULL(S.STHAR_GCMIK2,0) ELSE ISNULL(S.STHAR_GCMIK,0) END) OrderedQty,
           SUM(ISNULL(S.FIRMA_DOVTUT,0)) DeliveredQty
    FROM V3RIICO.dbo.TBLSIPATRA S WITH (NOLOCK)
    JOIN OrderNumbers N ON N.FISNO=S.FISNO
    WHERE S.STHAR_FTIRSIP='6' AND S.STHAR_GCKOD='C' AND S.STHAR_HTUR<>'K' AND ISNULL(S.L_YEDEK9,0)<=0
      AND NOT(S.REDNEDEN=2 AND EXISTS(SELECT 1 FROM V3RIICO.dbo.TBLASORTIMAS A WITH(NOLOCK) WHERE A.ASORTIKOD=S.EKALAN1))
      AND (@BranchCode IS NULL OR @BranchCode='' OR CONVERT(varchar(10),S.SUBE_KODU)=@BranchCode)
    GROUP BY S.FISNO
),
Allocated AS
(
    SELECT D.ExternalDocumentNo,SUM(ISNULL(L.AllocatedQuantity,0)) PlannedQtyAllocated
    FROM dbo.RII_SH_LINE_SOURCE L
    JOIN dbo.RII_SH_SOURCE_DOCUMENT D ON D.Id=L.ShipmentSourceDocumentId
    JOIN dbo.RII_SH_HEADER H ON H.Id=D.ShipmentHeaderId
    WHERE L.IsDeleted=0 AND D.IsDeleted=0 AND H.IsDeleted=0 AND H.Status<>11
    GROUP BY D.ExternalDocumentNo
)
SELECT 'H' Mode,O.FISNO SiparisNo,CAST(NULL AS int) OrderID,@CustomerCode CustomerCode,
       (SELECT TOP(1) C.CARI_ISIM FROM V3RIICO.dbo.TBLCASABIT C WITH(NOLOCK) WHERE C.CARI_KOD=@CustomerCode) CustomerName,
       O.BranchCode,O.TargetWh,CAST(NULL AS varchar(50)) ProjectCode,O.OrderDate,
       CAST(O.OrderedQty AS decimal(18,4)) OrderedQty,CAST(O.DeliveredQty AS decimal(18,4)) DeliveredQty,
       CAST(O.OrderedQty-O.DeliveredQty AS decimal(18,4)) RemainingHamax,
       CAST(ISNULL(A.PlannedQtyAllocated,0) AS decimal(18,4)) PlannedQtyAllocated,
       CAST((O.OrderedQty-O.DeliveredQty)-ISNULL(A.PlannedQtyAllocated,0) AS decimal(18,4)) RemainingForImport
FROM Orders O
LEFT JOIN Allocated A ON A.ExternalDocumentNo=O.FISNO
WHERE (O.OrderedQty-O.DeliveredQty)-ISNULL(A.PlannedQtyAllocated,0)>0;
"""));

            migrationBuilder.Sql(SqlServerMigrationSql.CreateOrAlterFunction("dbo.RII_FN_SH_LINE", """
CREATE OR ALTER FUNCTION dbo.RII_FN_SH_LINE
(
    @SiparisNoCsv nvarchar(max),
    @BranchCode varchar(10)=NULL
)
RETURNS TABLE
AS
RETURN
WITH Base AS
(
    SELECT S.FISNO,S.SUBE_KODU BranchCode,S.DEPO_KODU TargetWh,S.PROJE_KODU ProjectCode,S.STHAR_TARIH OrderDate,
           S.INCKEYNO OrderID,S.STOK_KODU StockCode,ST.STOK_ADI StockName,
           COALESCE(M.CARI_KODU,S.STHAR_ACIKLAMA) CustomerCode,C.CARI_ISIM CustomerName,
           CASE WHEN ISNULL(S.L_YEDEK9,0)=-1 THEN ISNULL(S.STHAR_GCMIK2,0) ELSE ISNULL(S.STHAR_GCMIK,0) END OrderedQty,
           ISNULL(S.FIRMA_DOVTUT,0) DeliveredQty,CAST('' AS varchar(50)) YapKod,CAST('' AS varchar(200)) YapAcik
    FROM V3RIICO.dbo.TBLSIPATRA S WITH(NOLOCK)
    LEFT JOIN V3RIICO.dbo.TBLSIPAMAS M WITH(NOLOCK) ON M.FATIRS_NO=S.FISNO AND M.FTIRSIP=S.STHAR_FTIRSIP
    LEFT JOIN V3RIICO.dbo.TBLCASABIT C WITH(NOLOCK) ON C.CARI_KOD=COALESCE(M.CARI_KODU,S.STHAR_ACIKLAMA)
    LEFT JOIN V3RIICO.dbo.TBLSTSABIT ST WITH(NOLOCK) ON ST.STOK_KODU=S.STOK_KODU
    WHERE S.STHAR_FTIRSIP='6' AND S.STHAR_GCKOD='C' AND S.STHAR_HTUR<>'K' AND ISNULL(S.L_YEDEK9,0)<=0
      AND NOT(S.REDNEDEN=2 AND EXISTS(SELECT 1 FROM V3RIICO.dbo.TBLASORTIMAS A WITH(NOLOCK) WHERE A.ASORTIKOD=S.EKALAN1))
      AND (@BranchCode IS NULL OR @BranchCode='' OR CONVERT(varchar(10),S.SUBE_KODU)=@BranchCode)
      AND (NULLIF(REPLACE(LTRIM(RTRIM(@SiparisNoCsv)),N' ',N''),N'') IS NULL
           OR CHARINDEX(
               N','+LTRIM(RTRIM(CONVERT(nvarchar(100),S.FISNO)))+N',',
               N','+REPLACE(@SiparisNoCsv,N' ',N'')+N',')>0)
),
Orders AS
(
    SELECT FISNO,BranchCode,TargetWh,ProjectCode,MIN(OrderDate) OrderDate,OrderID,StockCode,MAX(StockName) StockName,
           MAX(CustomerCode) CustomerCode,MAX(CustomerName) CustomerName,SUM(OrderedQty) OrderedQty,
           SUM(DeliveredQty) DeliveredQty,SUM(OrderedQty-DeliveredQty) RemainingHamax,MAX(YapKod) YapKod,MAX(YapAcik) YapAcik
    FROM Base
    GROUP BY FISNO,BranchCode,TargetWh,ProjectCode,OrderID,StockCode
),
Allocated AS
(
    SELECT D.ExternalDocumentNo,L.ExternalLineId,SUM(ISNULL(L.AllocatedQuantity,0)) PlannedQtyAllocated
    FROM dbo.RII_SH_LINE_SOURCE L
    JOIN dbo.RII_SH_SOURCE_DOCUMENT D ON D.Id=L.ShipmentSourceDocumentId
    JOIN dbo.RII_SH_HEADER H ON H.Id=D.ShipmentHeaderId
    WHERE L.IsDeleted=0 AND D.IsDeleted=0 AND H.IsDeleted=0 AND H.Status<>11
    GROUP BY D.ExternalDocumentNo,L.ExternalLineId
)
SELECT 'L' Mode,O.FISNO SiparisNo,O.OrderID,O.StockCode,O.StockName,O.CustomerCode,O.CustomerName,
       O.BranchCode,O.TargetWh,O.ProjectCode,O.OrderDate,
       CAST(O.OrderedQty AS decimal(18,4)) OrderedQty,CAST(O.DeliveredQty AS decimal(18,4)) DeliveredQty,
       CAST(O.RemainingHamax AS decimal(18,4)) RemainingHamax,
       CAST(ISNULL(A.PlannedQtyAllocated,0) AS decimal(18,4)) PlannedQtyAllocated,
       CAST(O.RemainingHamax-ISNULL(A.PlannedQtyAllocated,0) AS decimal(18,4)) RemainingForImport,
       O.YapKod,O.YapAcik
FROM Orders O
LEFT JOIN Allocated A ON A.ExternalDocumentNo=O.FISNO AND A.ExternalLineId=CONVERT(varchar(100),O.OrderID)
WHERE O.RemainingHamax-ISNULL(A.PlannedQtyAllocated,0)>0;
"""));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(SqlServerMigrationSql.DropFunction("dbo.RII_FN_SH_LINE"));
            migrationBuilder.Sql(SqlServerMigrationSql.DropFunction("dbo.RII_FN_SH_HEADER"));

            migrationBuilder.DropIndex(
                name: "IX_RII_WT_LINE_SOURCE_WtLineId_WtSourceDocumentId_ExternalLineId",
                table: "RII_WT_LINE_SOURCE");

            migrationBuilder.DropIndex(
                name: "IX_RII_WT_LINE_SOURCE_WtSourceDocumentId_ExternalLineId",
                table: "RII_WT_LINE_SOURCE");

            migrationBuilder.DropIndex(
                name: "IX_RII_SH_LINE_SOURCE_ShipmentLineId_ShipmentSourceDocumentId_ExternalLineId",
                table: "RII_SH_LINE_SOURCE");

            migrationBuilder.DropIndex(
                name: "IX_RII_SH_LINE_SOURCE_ShipmentSourceDocumentId_ExternalLineId",
                table: "RII_SH_LINE_SOURCE");

            migrationBuilder.CreateIndex(
                name: "IX_RII_WT_LINE_SOURCE_WtLineId",
                table: "RII_WT_LINE_SOURCE",
                column: "WtLineId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_WT_LINE_SOURCE_WtSourceDocumentId_ExternalLineId",
                table: "RII_WT_LINE_SOURCE",
                columns: new[] { "WtSourceDocumentId", "ExternalLineId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RII_SH_LINE_SOURCE_ShipmentLineId",
                table: "RII_SH_LINE_SOURCE",
                column: "ShipmentLineId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_SH_LINE_SOURCE_ShipmentSourceDocumentId_ExternalLineId",
                table: "RII_SH_LINE_SOURCE",
                columns: new[] { "ShipmentSourceDocumentId", "ExternalLineId" },
                unique: true);
        }
    }
}
