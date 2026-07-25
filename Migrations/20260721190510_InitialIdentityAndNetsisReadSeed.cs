using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class InitialIdentityAndNetsisReadSeed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
-- RII_FN_BRANCHES
CREATE OR ALTER FUNCTION [dbo].[RII_FN_BRANCHES]  
(  
    @branchNo INT = NULL  
)  
RETURNS TABLE  
AS  
RETURN  
(  
    SELECT   
        SUBE_KODU,  
        UNVAN  
    FROM V3RIICO..TBLSUBELER WHERE SUBE_KODU NOT IN('-1','32767')  
    AND   
        -- Eğer @branchNo NULL ise tüm satırlar döner  
        (@branchNo IS NULL OR SUBE_KODU = @branchNo)  
        -- TBLSUBELER’de MERKEZMI = 'E' olan satırlarda UNVAN boş olabilir.  
        -- İstersen NULL yerine SUBE_KODU döndürebilirsin.  
);
""");
            migrationBuilder.Sql("""
-- RII_FN_CARI
CREATE OR ALTER FUNCTION RII_FN_CARI    
(    
    @CariKodu NVARCHAR(MAX) = NULL,    
    @SubeKodu NVARCHAR(MAX) = NULL    
)    
RETURNS TABLE    
AS    
RETURN    
(  
    WITH CARI_LIST AS   
    (  
        SELECT LTRIM(RTRIM(value)) AS CARI_KODU  
        FROM STRING_SPLIT(ISNULL(@CariKodu,''), ',')  
        WHERE LTRIM(RTRIM(value)) <> ''  
    ),  
    SUBE_LIST AS   
    (  
        SELECT LTRIM(RTRIM(value)) AS SUBE_KODU  
        FROM STRING_SPLIT(ISNULL(@SubeKodu,''), ',')  
        WHERE LTRIM(RTRIM(value)) <> ''  
    )  
    SELECT     
        CS.[SUBE_KODU],    
        CS.[ISLETME_KODU],    
        CS.[CARI_KOD],    
        CS.[CARI_TEL],    
        CS.[CARI_IL],    
        CS.[ULKE_KODU],    
        CS.[CARI_ISIM],    
        CS.[CARI_TIP],    
        CS.[GRUP_KODU],    
        CS.[RAPOR_KODU1],    
        CS.[RAPOR_KODU2],    
        CS.[RAPOR_KODU3],    
        CS.[RAPOR_KODU4],    
        CS.[RAPOR_KODU5],    
        CS.[CARI_ADRES],    
        CS.[CARI_ILCE],    
        CS.[VERGI_DAIRESI],    
        CS.[VERGI_NUMARASI],    
        CS.[FAX],    
        CS.[POSTAKODU],    
        CS.[DETAY_KODU],    
        CS.[NAKLIYE_KATSAYISI],    
        CS.[RISK_SINIRI],    
        CS.[TEMINATI],    
        CS.[CARISK],    
        CS.[CCRISK],    
        CS.[SARISK],    
        CS.[SCRISK],    
        CS.[CM_BORCT],    
        CS.[CM_ALACT],    
        CS.[CM_RAP_TARIH],    
        CS.[KOSULKODU],    
        CS.[ISKONTO_ORANI],    
        CS.[VADE_GUNU],    
        CS.[LISTE_FIATI],    
        CS.[ACIK1],    
        CS.[ACIK2],    
        CS.[ACIK3],    
        CS.[M_KOD],    
        CS.[DOVIZ_TIPI],    
        CS.[DOVIZ_TURU],    
        CS.[HESAPTUTMASEKLI],    
        CS.[DOVIZLIMI],    
        CS.[UPDATE_KODU],    
        CS.[PLASIYER_KODU],    
        CS.[LOKALDEPO],    
        CS.[EMAIL],    
        CS.[WEB],    
        CS.[KURFARKIBORC],    
        CS.[KURFARKIALAC],    
        CS.[S_YEDEK1],    
        CS.[S_YEDEK2],    
        CS.[F_YEDEK1],    
        CS.[F_YEDEK2],    
        CS.[C_YEDEK1],    
        CS.[C_YEDEK2],    
        CS.[B_YEDEK1],    
        CS.[I_YEDEK1],    
        CS.[L_YEDEK1],    
        CS.[FIYATGRUBU],    
        CS.[KAYITYAPANKUL],    
        CS.[KAYITTARIHI],    
        CS.[DUZELTMEYAPANKUL],    
        CS.[DUZELTMETARIHI],    
        CS.[ODEMETIPI],    
        CS.[ONAYTIPI],    
        CS.[ONAYNUM],    
        CS.[MUSTERIBAZIKDV],    
        CS.[AGIRLIK_ISK],    
        CS.[CARI_TEL2],    
        CS.[CARI_TEL3],    
        CS.[FAX2],    
        CS.[GSM1],    
        CS.[GSM2],    
        CS.[GEKAPHESAPLANMASIN],    
        CS.[ONCEKI_KOD],    
        CS.[SONRAKI_KOD],    
        CS.[SONCARIKODU],    
        CS.[TESLIMCARIBAGLIMI],    
        CS.[BAGLICARIKOD],    
        CS.[FABRIKA_KODU],    
        CS.[NAKLIYE_SURESI],    
        CS.[TESLIMAT_PERIYOD_TIPI],    
        CS.[TESLIMAT_GUNU],    
        CS.[TESLIMAT_EXTRAINFO],    
        CE.[TCKIMLIKNO]    
    FROM V3RIICO..TBLCASABIT CS    
    LEFT JOIN V3RIICO..TBLCASABITEK CE   
        ON CS.CARI_KOD = CE.CARI_KOD    
    WHERE    
        (NOT EXISTS (SELECT 1 FROM CARI_LIST) OR CS.CARI_KOD IN (SELECT CARI_KODU FROM CARI_LIST))    
        AND (NOT EXISTS (SELECT 1 FROM SUBE_LIST) OR CS.SUBE_KODU IN (SELECT SUBE_KODU FROM SUBE_LIST))  
);
""");
            migrationBuilder.Sql("""
-- RII_FN_DEPO
CREATE OR ALTER FUNCTION RII_FN_DEPO  
(  
    @DepoKodu NVARCHAR(MAX) = NULL,   -- A11,A12,A13 gibi  
    @SubeKodu NVARCHAR(50) = NULL  
)  
RETURNS TABLE  
AS  
RETURN  
(  
    WITH DepoList AS  
    (  
        SELECT TRIM(value) AS DepoKodu  
        FROM STRING_SPLIT(@DepoKodu, ',')  
    )  
    SELECT   
          [DEPO_KODU]  
        , [DEPO_ISMI]  
        , [DEPO_KILITLE]  
        , [CARI_KODU]  
        , [EKSIBAKIYE]  
        , [FIAT_TIPI]  
        , [SUBE_KODU]  
        , [S_YEDEK1]  
        , [S_YEDEK2]  
        , [I_YEDEK1]  
        , [I_YEDEK2]  
        , [C_YEDEK1]  
        , [C_YEDEK2]  
        , [D_YEDEK1]  
        , [KAYITYAPANKUL]  
        , [KAYITTARIHI]  
        , [DUZELTMEYAPANKUL]  
        , [DUZELTMETARIHI]  
        , [EMANETDEPO]  
        , [KILIT_POLITIKASI]  
    FROM V3RIICO..TBLSTOKDP D  
    WHERE  
        (  
            @DepoKodu IS NULL OR @DepoKodu = ''   
            OR D.DEPO_KODU IN (SELECT DepoKodu FROM DepoList)  
        )  
        AND (@SubeKodu IS NULL OR @SubeKodu = '' OR D.SUBE_KODU = @SubeKodu)  
);
""");
            migrationBuilder.Sql("""
-- RII_FN_ESNYAPMAS
CREATE OR ALTER FUNCTION RII_FN_ESNYAPMAS ()    
RETURNS TABLE    
AS    
RETURN    
(    
    SELECT     
        YAPKOD,    
        YAPACIK,    
        SUBE_KODU,    
        YPLNDRSTOKKOD,
        CAST(NULL AS BIGINT) AS StockId
    FROM V3RIICO..TBLESNYAPMAS AS ESNYAPMAS
);
""");
            migrationBuilder.Sql("""
-- RII_FN_STOK
CREATE OR ALTER FUNCTION RII_FN_STOK    
(    
    @StokKodu NVARCHAR(MAX) = NULL,   -- Artık birden fazla değer içerebilir  
    @SubeKodu NVARCHAR(MAX) = NULL    
)    
RETURNS TABLE    
AS    
RETURN    
(  
    WITH STOK_LIST AS   
    (  
        SELECT LTRIM(RTRIM(value)) AS STOK_KODU  
        FROM STRING_SPLIT(ISNULL(@StokKodu,''), ',')  
        WHERE LTRIM(RTRIM(value)) <> ''  
    ),  
    SUBE_LIST AS   
    (  
        SELECT LTRIM(RTRIM(value)) AS SUBE_KODU  
        FROM STRING_SPLIT(ISNULL(@SubeKodu,''), ',')  
        WHERE LTRIM(RTRIM(value)) <> ''  
    )  
    SELECT       
        X.SUBE_KODU,    
        X.ISLETME_KODU,    
        X.STOK_KODU,    
        X.URETICI_KODU,    
        X.STOK_ADI,    
        X.GRUP_KODU,    
        X.KOD_1,    
        X.KOD_2,    
        X.KOD_3,    
        X.KOD_4,    
        X.KOD_5,    
        X.SATICI_KODU,    
        X.OLCU_BR1,    
        X.OLCU_BR2,    
        X.PAY_1,    
        X.PAYDA_1,    
        X.OLCU_BR3,    
        X.PAY2,    
        X.PAYDA2,    
        X.FIAT_BIRIMI,    
        X.AZAMI_STOK,    
        X.ASGARI_STOK,    
        X.TEMIN_SURESI,    
        X.KUL_MIK,    
        X.RISK_SURESI,    
        X.ZAMAN_BIRIMI,    
        X.SATIS_FIAT1,    
        X.SATIS_FIAT2,    
        X.SATIS_FIAT3,    
        X.SATIS_FIAT4,    
        X.SAT_DOV_TIP,    
        X.DOV_ALIS_FIAT,    
        X.DOV_MAL_FIAT,    
        X.DOV_SATIS_FIAT,    
        X.MUH_DETAYKODU,    
        X.BIRIM_AGIRLIK,    
        X.NAKLIYET_TUT,    
        X.KDV_ORANI,    
        X.ALIS_DOV_TIP,    
        X.DEPO_KODU,    
        X.DOV_TUR,    
        X.URET_OLCU_BR,    
        X.BILESENMI,    
        X.MAMULMU,    
        X.FORMUL_TOPLAMI,    
        X.UPDATE_KODU,    
        X.MAX_ISKONTO,    
        X.ECZACI_KARI,    
        X.MIKTAR,    
        X.MAL_FAZLASI,    
        X.KDV_TENZIL_ORAN,    
        X.KILIT,    
        X.ONCEKI_KOD,    
        X.SONRAKI_KOD,    
        X.BARKOD1,    
        X.BARKOD2,    
        X.BARKOD3,    
        X.ALIS_KDV_KODU,    
        X.ALIS_FIAT1,    
        X.ALIS_FIAT2,    
        X.ALIS_FIAT3,    
        X.ALIS_FIAT4,    
        X.LOT_SIZE,    
        X.MIN_SIP_MIKTAR,    
        X.SABIT_SIP_ARALIK,    
        X.SIP_POLITIKASI,    
        X.OZELLIK_KODU1,    
        X.OZELLIK_KODU2,    
        X.OZELLIK_KODU3,    
        X.OZELLIK_KODU4,    
        X.OZELLIK_KODU5,    
        X.OPSIYON_KODU1,    
        X.OPSIYON_KODU2,    
        X.OPSIYON_KODU3,    
        X.OPSIYON_KODU4,    
        X.OPSIYON_KODU5,    
        X.BILESEN_OP_KODU,    
        X.SIP_VER_MAL,    
        X.ELDE_BUL_MAL,    
        X.YIL_TAH_KUL_MIK,    
        X.EKON_SIP_MIKTAR,    
        X.ESKI_RECETE,    
        X.OTOMATIK_URETIM,    
        X.ALFKOD,    
        X.SAFKOD,    
        X.KODTURU,    
        X.S_YEDEK1,    
        X.S_YEDEK2,    
        X.F_YEDEK3,    
        X.F_YEDEK4,    
        X.C_YEDEK5,    
        X.C_YEDEK6,    
        X.B_YEDEK7,    
        X.I_YEDEK8,    
        X.L_YEDEK9,    
        X.D_YEDEK10,    
  
        ISNULL(X.GIRIS_SERI, 'H') AS GIRIS_SERI,    
        ISNULL(X.CIKIS_SERI, 'H') AS CIKIS_SERI,    
        ISNULL(X.SERI_BAK, 'H') AS SERI_BAK,    
        ISNULL(X.SERI_MIK, 'H') AS SERI_MIK,    
        ISNULL(X.SERI_GIR_OT, 'H') AS SERI_GIR_OT,    
        ISNULL(X.SERI_CIK_OT, 'H') AS SERI_CIK_OT,    
  
        X.SERI_BASLANGIC,    
        X.FIYATKODU,    
        X.FIYATSIRASI,    
        X.PLANLANACAK,    
        X.LOT_SIZECUSTOMER,    
        X.MIN_SIP_MIKTARCUSTOMER,    
        X.GUMRUKTARIFEKODU,    
        X.ABCKODU,    
        X.PERFORMANSKODU,    
        X.SATICISIPKILIT,    
        X.MUSTERISIPKILIT,    
        X.SATINALMAKILIT,    
        X.SATISKILIT,    
        X.EN,    
        X.BOY,    
        X.GENISLIK,    
        X.SIPLIMITVAR,    
        X.SONSTOKKODU,    
        X.ONAYTIPI,    
        X.ONAYNUM,    
        X.FIKTIF_MAM,    
        X.YAPILANDIR,    
        X.SBOMVARMI,    
        X.BAGLISTOKKOD,    
        X.YAPKOD,    
        X.ALISTALTEKKILIT,    
        X.SATISTALTEKKILIT,    
   X.S_YEDEK3,    
        X.STOKMEVZUAT,    
        X.OTVTEVKIFAT,    
        X.SERIBARKOD,    
        X.ATIK_URUN,    
        Y.TUR,    
        Y.KAYITTARIHI,    
        Y.INGISIM    
    FROM v3riico..TBLSTSABIT X    
    LEFT JOIN V3RIICO..TBLSTSABITEK Y WITH (NOLOCK)     
        ON X.STOK_KODU = Y.STOK_KODU    
    WHERE    
        (NOT EXISTS (SELECT 1 FROM STOK_LIST) OR X.STOK_KODU IN (SELECT STOK_KODU FROM STOK_LIST))    
        AND (NOT EXISTS (SELECT 1 FROM SUBE_LIST) OR X.SUBE_KODU IN (SELECT SUBE_KODU FROM SUBE_LIST))  
);
""");
            migrationBuilder.CreateTable(
                name: "RII_USERS",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Username = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Role = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    LastLoginAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RefreshToken = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RefreshTokenExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RII_USERS", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RII_USER_DETAILS",
                columns: table => new
                {
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    FirstName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    LastName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RII_USER_DETAILS", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_RII_USER_DETAILS_RII_USERS_UserId",
                        column: x => x.UserId,
                        principalTable: "RII_USERS",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "RII_USERS",
                columns: new[] { "Id", "Email", "IsActive", "LastLoginAt", "PasswordHash", "RefreshToken", "RefreshTokenExpiresAt", "Role", "Username" },
                values: new object[] { 1L, "admin@v3rii.com", true, null, "$2a$11$/miyTaLTVkU0keOJabjkQ.bKF4Rb6a2jhuLWDz67I4LLxjwWQ6IJW", null, null, "superadmin", "admin" });

            migrationBuilder.InsertData(
                table: "RII_USER_DETAILS",
                columns: new[] { "UserId", "FirstName", "LastName", "Phone" },
                values: new object[] { 1L, "System", "Administrator", null });

            migrationBuilder.CreateIndex(
                name: "IX_RII_USERS_Email",
                table: "RII_USERS",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RII_USERS_Username",
                table: "RII_USERS",
                column: "Username",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
DROP FUNCTION IF EXISTS dbo.RII_FN_STOK;
DROP FUNCTION IF EXISTS dbo.RII_FN_ESNYAPMAS;
DROP FUNCTION IF EXISTS dbo.RII_FN_DEPO;
DROP FUNCTION IF EXISTS dbo.RII_FN_CARI;
DROP FUNCTION IF EXISTS dbo.RII_FN_BRANCHES;
""");
            migrationBuilder.DropTable(
                name: "RII_USER_DETAILS");

            migrationBuilder.DropTable(
                name: "RII_USERS");
        }
    }
}
