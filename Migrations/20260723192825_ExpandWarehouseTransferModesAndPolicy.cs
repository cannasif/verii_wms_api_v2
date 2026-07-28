using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class ExpandWarehouseTransferModesAndPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AutoRelease",
                table: "RII_WT_HEADER",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "DirectPostingPolicy",
                table: "RII_WT_HEADER",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "TwoStepTransit");

            migrationBuilder.AddColumn<decimal>(
                name: "MinimumFulfillmentPercent",
                table: "RII_WT_HEADER",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: false,
                defaultValue: 100m);

            migrationBuilder.AddColumn<bool>(
                name: "RequireAssignee",
                table: "RII_WT_HEADER",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "RequireShipmentInformation",
                table: "RII_WT_HEADER",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "RequireSourceLocation",
                table: "RII_WT_HEADER",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "RequireTargetLocation",
                table: "RII_WT_HEADER",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "ReservationPolicy",
                table: "RII_WT_HEADER",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "OnRelease");

            migrationBuilder.CreateTable(
                name: "RII_WT_POLICIES",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PolicyKey = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    AllowOrderBasedTask = table.Column<bool>(type: "bit", nullable: false),
                    AllowStockBasedTask = table.Column<bool>(type: "bit", nullable: false),
                    AllowOrderBasedDirect = table.Column<bool>(type: "bit", nullable: false),
                    AllowStockBasedDirect = table.Column<bool>(type: "bit", nullable: false),
                    RequireApproval = table.Column<bool>(type: "bit", nullable: false),
                    RequireAssigneeForTask = table.Column<bool>(type: "bit", nullable: false),
                    AllowMultipleAssignees = table.Column<bool>(type: "bit", nullable: false),
                    AutoReleaseTaskBased = table.Column<bool>(type: "bit", nullable: false),
                    ReservationPolicy = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    MinimumFulfillmentPercent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    AllowPartialPicking = table.Column<bool>(type: "bit", nullable: false),
                    AllowPartialShipment = table.Column<bool>(type: "bit", nullable: false),
                    AllowPartialReceipt = table.Column<bool>(type: "bit", nullable: false),
                    RequireDestinationAcceptance = table.Column<bool>(type: "bit", nullable: false),
                    CreateTransitInventory = table.Column<bool>(type: "bit", nullable: false),
                    RequirePutaway = table.Column<bool>(type: "bit", nullable: false),
                    RequireSourceLocation = table.Column<bool>(type: "bit", nullable: false),
                    RequireTargetLocation = table.Column<bool>(type: "bit", nullable: false),
                    RequireShipmentInformation = table.Column<bool>(type: "bit", nullable: false),
                    DirectPostingPolicy = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    DiscrepancyPolicy = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
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
                    table.PrimaryKey("PK_RII_WT_POLICIES", x => x.Id);
                    table.CheckConstraint("CK_RII_WT_POLICY_FULFILLMENT", "[MinimumFulfillmentPercent] >= 0 AND [MinimumFulfillmentPercent] <= 100");
                });

            migrationBuilder.CreateIndex(
                name: "IX_RII_WT_POLICIES_BranchCode_PolicyKey",
                table: "RII_WT_POLICIES",
                columns: new[] { "BranchCode", "PolicyKey" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_WT_POLICIES_IsDeleted",
                table: "RII_WT_POLICIES",
                column: "IsDeleted");

            migrationBuilder.Sql("""
INSERT dbo.RII_PERMISSION_DEFINITIONS
    (AvailableOnMobile,AvailableOnWeb,BranchCode,Code,CreatedDate,IsActive,Name)
SELECT 0,1,'0',V.Code,SYSUTCDATETIME(),1,V.Name
FROM (VALUES
    ('WMS.WAREHOUSE_TRANSFER.VIEW',N'Depolar arası transferleri görüntüle'),
    ('WMS.WAREHOUSE_TRANSFER.CREATE',N'Depolar arası transfer oluştur'),
    ('WMS.WAREHOUSE_TRANSFER.OPERATE',N'Depolar arası transfer operasyonu yürüt'),
    ('WMS.WAREHOUSE_TRANSFER.APPROVE',N'Depolar arası transferi onayla'),
    ('WMS.WAREHOUSE_TRANSFER.SETTINGS.VIEW',N'Transfer süreç ayarlarını görüntüle'),
    ('WMS.WAREHOUSE_TRANSFER.SETTINGS.MANAGE',N'Transfer süreç ayarlarını yönet')
) V(Code,Name)
WHERE NOT EXISTS(SELECT 1 FROM dbo.RII_PERMISSION_DEFINITIONS P WHERE P.Code=V.Code);
INSERT dbo.RII_PERMISSION_GROUP_PERMISSIONS
    (BranchCode,CreatedDate,PermissionDefinitionId,PermissionGroupId)
SELECT '0',SYSUTCDATETIME(),P.Id,1001
FROM dbo.RII_PERMISSION_DEFINITIONS P
WHERE P.Code LIKE 'WMS.WAREHOUSE_TRANSFER.%'
  AND NOT EXISTS (SELECT 1 FROM dbo.RII_PERMISSION_GROUP_PERMISSIONS G WHERE G.PermissionDefinitionId=P.Id AND G.PermissionGroupId=1001);
""");

            migrationBuilder.Sql(SqlServerMigrationSql.CreateOrAlterFunction("dbo.RII_FN_WT_HEADER", """
CREATE OR ALTER FUNCTION dbo.RII_FN_WT_HEADER
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
    FROM dbo.RII_WT_LINE_SOURCE L
    JOIN dbo.RII_WT_SOURCE_DOCUMENT D ON D.Id=L.WtSourceDocumentId
    JOIN dbo.RII_WT_HEADER H ON H.Id=D.WtHeaderId
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

            migrationBuilder.Sql(SqlServerMigrationSql.CreateOrAlterFunction("dbo.RII_FN_WT_LINE", """
CREATE OR ALTER FUNCTION dbo.RII_FN_WT_LINE
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
    FROM Base GROUP BY FISNO,BranchCode,TargetWh,ProjectCode,OrderID,StockCode
),
Allocated AS
(
    SELECT D.ExternalDocumentNo,L.ExternalLineId,SUM(ISNULL(L.AllocatedQuantity,0)) PlannedQtyAllocated
    FROM dbo.RII_WT_LINE_SOURCE L
    JOIN dbo.RII_WT_SOURCE_DOCUMENT D ON D.Id=L.WtSourceDocumentId
    JOIN dbo.RII_WT_HEADER H ON H.Id=D.WtHeaderId
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
            migrationBuilder.Sql(SqlServerMigrationSql.DropFunction("dbo.RII_FN_WT_LINE"));
            migrationBuilder.Sql(SqlServerMigrationSql.DropFunction("dbo.RII_FN_WT_HEADER"));
            migrationBuilder.DropTable(
                name: "RII_WT_POLICIES");

            migrationBuilder.DropColumn(
                name: "AutoRelease",
                table: "RII_WT_HEADER");

            migrationBuilder.DropColumn(
                name: "DirectPostingPolicy",
                table: "RII_WT_HEADER");

            migrationBuilder.DropColumn(
                name: "MinimumFulfillmentPercent",
                table: "RII_WT_HEADER");

            migrationBuilder.DropColumn(
                name: "RequireAssignee",
                table: "RII_WT_HEADER");

            migrationBuilder.DropColumn(
                name: "RequireShipmentInformation",
                table: "RII_WT_HEADER");

            migrationBuilder.DropColumn(
                name: "RequireSourceLocation",
                table: "RII_WT_HEADER");

            migrationBuilder.DropColumn(
                name: "RequireTargetLocation",
                table: "RII_WT_HEADER");

            migrationBuilder.DropColumn(
                name: "ReservationPolicy",
                table: "RII_WT_HEADER");

            migrationBuilder.Sql("""
DELETE G FROM dbo.RII_PERMISSION_GROUP_PERMISSIONS G
JOIN dbo.RII_PERMISSION_DEFINITIONS P ON P.Id=G.PermissionDefinitionId
WHERE G.PermissionGroupId=1001 AND P.Code IN ('WMS.WAREHOUSE_TRANSFER.SETTINGS.VIEW','WMS.WAREHOUSE_TRANSFER.SETTINGS.MANAGE');
DELETE FROM dbo.RII_PERMISSION_DEFINITIONS WHERE Code IN ('WMS.WAREHOUSE_TRANSFER.SETTINGS.VIEW','WMS.WAREHOUSE_TRANSFER.SETTINGS.MANAGE');
""");
        }
    }
}
