BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260730153748_LinkTransferShipmentAndWarehouseSlipsToNetsisOrders'
)
BEGIN
    IF OBJECT_ID(N'dbo.RII_FN_WT_LINE') IS NULL
    BEGIN
        EXEC sys.sp_executesql N'CREATE FUNCTION dbo.RII_FN_WT_LINE
    (
        @SiparisNoCsv NVARCHAR(MAX),
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
            NULLIF(LTRIM(RTRIM(S.PROJE_KODU)), '''') AS ProjectCode,
            COALESCE(
                TRY_CONVERT(DATETIME, S.STHAR_TESTAR, 104),
                TRY_CONVERT(DATETIME, M.SIPARIS_TEST, 104)) AS DeliveryDate,
            CAST(ISNULL(S.STHAR_NF, 0) AS DECIMAL(28, 8)) AS NetUnitPrice,
            CAST(ISNULL(S.STHAR_BF, 0) AS DECIMAL(28, 8)) AS GrossUnitPrice,
            S.STHAR_TARIH AS OrderDate,
            S.INCKEYNO AS OrderID,
            S.SIRA AS OrderLineSequence,
            S.STOK_KODU AS StockCode,
            ST.STOK_ADI AS StockName,
            ST.OLCU_BR1 AS UnitCode,
            COALESCE(M.CARI_KODU, S.STHAR_ACIKLAMA) AS CustomerCode,
            C.CARI_ISIM AS CustomerName,
            CAST(CASE WHEN ISNULL(S.L_YEDEK9, 0) = -1 THEN S.STHAR_GCMIK2 ELSE S.STHAR_GCMIK END AS DECIMAL(18, 4)) AS OrderedQty,
            CAST(ISNULL(S.FIRMA_DOVTUT, 0) AS DECIMAL(18, 4)) AS DeliveredQty
        FROM V3RIICO.dbo.TBLSIPATRA AS S WITH (NOLOCK)
        LEFT JOIN V3RIICO.dbo.TBLSIPAMAS AS M WITH (NOLOCK)
          ON M.FATIRS_NO = S.FISNO
         AND M.FTIRSIP = S.STHAR_FTIRSIP
         AND M.SUBE_KODU = S.SUBE_KODU
        LEFT JOIN V3RIICO.dbo.TBLCASABIT AS C WITH (NOLOCK)
          ON C.CARI_KOD = COALESCE(M.CARI_KODU, S.STHAR_ACIKLAMA)
        LEFT JOIN V3RIICO.dbo.TBLASORTIMAS AS A WITH (NOLOCK) ON A.ASORTIKOD = S.EKALAN1
        LEFT JOIN V3RIICO.dbo.TBLSTSABIT AS ST WITH (NOLOCK) ON ST.STOK_KODU = S.STOK_KODU
        WHERE S.STHAR_FTIRSIP = ''6''
          AND S.STHAR_GCKOD = ''C''
          AND S.STHAR_HTUR <> ''K''
          AND ISNULL(S.L_YEDEK9, 0) <= 0
          AND (A.ASORTIKOD IS NULL OR S.REDNEDEN <> 2)
          AND (CASE WHEN ISNULL(S.L_YEDEK9, 0) = -1 THEN S.STHAR_GCMIK2 ELSE S.STHAR_GCMIK END)
              - ISNULL(S.FIRMA_DOVTUT, 0) > 0
          AND (@BranchCode IS NULL OR @BranchCode = '''' OR CONVERT(VARCHAR(10), S.SUBE_KODU) = @BranchCode)
          AND (NULLIF(REPLACE(LTRIM(RTRIM(@SiparisNoCsv)), N'' '', N''''), N'''') IS NULL
               OR CHARINDEX(
                   N'','' + LTRIM(RTRIM(CONVERT(NVARCHAR(100), S.FISNO))) + N'','',
                   N'','' + REPLACE(@SiparisNoCsv, N'' '', N'''') + N'','') > 0)
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
        FROM dbo.RII_WT_LINE_SOURCE AS LS
        INNER JOIN dbo.RII_WT_SOURCE_DOCUMENT AS SD ON SD.Id = LS.WtSourceDocumentId
        INNER JOIN dbo.RII_WT_HEADER AS H ON H.Id = SD.WtHeaderId
        WHERE LS.IsDeleted = 0 AND SD.IsDeleted = 0 AND H.IsDeleted = 0
          AND H.Status <> 11 AND H.ErpIntegrationStatus <> 4
          AND (@BranchCode IS NULL OR @BranchCode = '''' OR H.BranchCode = @BranchCode)
        GROUP BY LS.ExternalLineId
    )
    SELECT
        ''L'' AS Mode,
        X.FisNo AS SiparisNo,
        X.OrderID,
        X.OrderLineSequence,
        X.StockCode,
        X.StockName,
        X.UnitCode,
        CAST('''' AS VARCHAR(100)) AS YapKod,
        CAST('''' AS VARCHAR(100)) AS YapAcik,
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
    LEFT JOIN ActiveAllocations AS A ON A.ExternalLineId = CONVERT(NVARCHAR(100), X.OrderID)
    WHERE X.RemainingHamax > 0;';
    END
    ELSE
    BEGIN
        EXEC sys.sp_executesql N'ALTER FUNCTION dbo.RII_FN_WT_LINE
    (
        @SiparisNoCsv NVARCHAR(MAX),
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
            NULLIF(LTRIM(RTRIM(S.PROJE_KODU)), '''') AS ProjectCode,
            COALESCE(
                TRY_CONVERT(DATETIME, S.STHAR_TESTAR, 104),
                TRY_CONVERT(DATETIME, M.SIPARIS_TEST, 104)) AS DeliveryDate,
            CAST(ISNULL(S.STHAR_NF, 0) AS DECIMAL(28, 8)) AS NetUnitPrice,
            CAST(ISNULL(S.STHAR_BF, 0) AS DECIMAL(28, 8)) AS GrossUnitPrice,
            S.STHAR_TARIH AS OrderDate,
            S.INCKEYNO AS OrderID,
            S.SIRA AS OrderLineSequence,
            S.STOK_KODU AS StockCode,
            ST.STOK_ADI AS StockName,
            ST.OLCU_BR1 AS UnitCode,
            COALESCE(M.CARI_KODU, S.STHAR_ACIKLAMA) AS CustomerCode,
            C.CARI_ISIM AS CustomerName,
            CAST(CASE WHEN ISNULL(S.L_YEDEK9, 0) = -1 THEN S.STHAR_GCMIK2 ELSE S.STHAR_GCMIK END AS DECIMAL(18, 4)) AS OrderedQty,
            CAST(ISNULL(S.FIRMA_DOVTUT, 0) AS DECIMAL(18, 4)) AS DeliveredQty
        FROM V3RIICO.dbo.TBLSIPATRA AS S WITH (NOLOCK)
        LEFT JOIN V3RIICO.dbo.TBLSIPAMAS AS M WITH (NOLOCK)
          ON M.FATIRS_NO = S.FISNO
         AND M.FTIRSIP = S.STHAR_FTIRSIP
         AND M.SUBE_KODU = S.SUBE_KODU
        LEFT JOIN V3RIICO.dbo.TBLCASABIT AS C WITH (NOLOCK)
          ON C.CARI_KOD = COALESCE(M.CARI_KODU, S.STHAR_ACIKLAMA)
        LEFT JOIN V3RIICO.dbo.TBLASORTIMAS AS A WITH (NOLOCK) ON A.ASORTIKOD = S.EKALAN1
        LEFT JOIN V3RIICO.dbo.TBLSTSABIT AS ST WITH (NOLOCK) ON ST.STOK_KODU = S.STOK_KODU
        WHERE S.STHAR_FTIRSIP = ''6''
          AND S.STHAR_GCKOD = ''C''
          AND S.STHAR_HTUR <> ''K''
          AND ISNULL(S.L_YEDEK9, 0) <= 0
          AND (A.ASORTIKOD IS NULL OR S.REDNEDEN <> 2)
          AND (CASE WHEN ISNULL(S.L_YEDEK9, 0) = -1 THEN S.STHAR_GCMIK2 ELSE S.STHAR_GCMIK END)
              - ISNULL(S.FIRMA_DOVTUT, 0) > 0
          AND (@BranchCode IS NULL OR @BranchCode = '''' OR CONVERT(VARCHAR(10), S.SUBE_KODU) = @BranchCode)
          AND (NULLIF(REPLACE(LTRIM(RTRIM(@SiparisNoCsv)), N'' '', N''''), N'''') IS NULL
               OR CHARINDEX(
                   N'','' + LTRIM(RTRIM(CONVERT(NVARCHAR(100), S.FISNO))) + N'','',
                   N'','' + REPLACE(@SiparisNoCsv, N'' '', N'''') + N'','') > 0)
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
        FROM dbo.RII_WT_LINE_SOURCE AS LS
        INNER JOIN dbo.RII_WT_SOURCE_DOCUMENT AS SD ON SD.Id = LS.WtSourceDocumentId
        INNER JOIN dbo.RII_WT_HEADER AS H ON H.Id = SD.WtHeaderId
        WHERE LS.IsDeleted = 0 AND SD.IsDeleted = 0 AND H.IsDeleted = 0
          AND H.Status <> 11 AND H.ErpIntegrationStatus <> 4
          AND (@BranchCode IS NULL OR @BranchCode = '''' OR H.BranchCode = @BranchCode)
        GROUP BY LS.ExternalLineId
    )
    SELECT
        ''L'' AS Mode,
        X.FisNo AS SiparisNo,
        X.OrderID,
        X.OrderLineSequence,
        X.StockCode,
        X.StockName,
        X.UnitCode,
        CAST('''' AS VARCHAR(100)) AS YapKod,
        CAST('''' AS VARCHAR(100)) AS YapAcik,
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
    LEFT JOIN ActiveAllocations AS A ON A.ExternalLineId = CONVERT(NVARCHAR(100), X.OrderID)
    WHERE X.RemainingHamax > 0;';
    END
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260730153748_LinkTransferShipmentAndWarehouseSlipsToNetsisOrders'
)
BEGIN
    IF OBJECT_ID(N'dbo.RII_FN_SH_LINE') IS NULL
    BEGIN
        EXEC sys.sp_executesql N'CREATE FUNCTION dbo.RII_FN_SH_LINE
    (
        @SiparisNoCsv NVARCHAR(MAX),
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
            NULLIF(LTRIM(RTRIM(S.PROJE_KODU)), '''') AS ProjectCode,
            COALESCE(
                TRY_CONVERT(DATETIME, S.STHAR_TESTAR, 104),
                TRY_CONVERT(DATETIME, M.SIPARIS_TEST, 104)) AS DeliveryDate,
            CAST(ISNULL(S.STHAR_NF, 0) AS DECIMAL(28, 8)) AS NetUnitPrice,
            CAST(ISNULL(S.STHAR_BF, 0) AS DECIMAL(28, 8)) AS GrossUnitPrice,
            S.STHAR_TARIH AS OrderDate,
            S.INCKEYNO AS OrderID,
            S.SIRA AS OrderLineSequence,
            S.STOK_KODU AS StockCode,
            ST.STOK_ADI AS StockName,
            ST.OLCU_BR1 AS UnitCode,
            COALESCE(M.CARI_KODU, S.STHAR_ACIKLAMA) AS CustomerCode,
            C.CARI_ISIM AS CustomerName,
            CAST(CASE WHEN ISNULL(S.L_YEDEK9, 0) = -1 THEN S.STHAR_GCMIK2 ELSE S.STHAR_GCMIK END AS DECIMAL(18, 4)) AS OrderedQty,
            CAST(ISNULL(S.FIRMA_DOVTUT, 0) AS DECIMAL(18, 4)) AS DeliveredQty
        FROM V3RIICO.dbo.TBLSIPATRA AS S WITH (NOLOCK)
        LEFT JOIN V3RIICO.dbo.TBLSIPAMAS AS M WITH (NOLOCK)
          ON M.FATIRS_NO = S.FISNO
         AND M.FTIRSIP = S.STHAR_FTIRSIP
         AND M.SUBE_KODU = S.SUBE_KODU
        LEFT JOIN V3RIICO.dbo.TBLCASABIT AS C WITH (NOLOCK)
          ON C.CARI_KOD = COALESCE(M.CARI_KODU, S.STHAR_ACIKLAMA)
        LEFT JOIN V3RIICO.dbo.TBLASORTIMAS AS A WITH (NOLOCK) ON A.ASORTIKOD = S.EKALAN1
        LEFT JOIN V3RIICO.dbo.TBLSTSABIT AS ST WITH (NOLOCK) ON ST.STOK_KODU = S.STOK_KODU
        WHERE S.STHAR_FTIRSIP = ''6''
          AND S.STHAR_GCKOD = ''C''
          AND S.STHAR_HTUR <> ''K''
          AND ISNULL(S.L_YEDEK9, 0) <= 0
          AND (A.ASORTIKOD IS NULL OR S.REDNEDEN <> 2)
          AND (CASE WHEN ISNULL(S.L_YEDEK9, 0) = -1 THEN S.STHAR_GCMIK2 ELSE S.STHAR_GCMIK END)
              - ISNULL(S.FIRMA_DOVTUT, 0) > 0
          AND (@BranchCode IS NULL OR @BranchCode = '''' OR CONVERT(VARCHAR(10), S.SUBE_KODU) = @BranchCode)
          AND (NULLIF(REPLACE(LTRIM(RTRIM(@SiparisNoCsv)), N'' '', N''''), N'''') IS NULL
               OR CHARINDEX(
                   N'','' + LTRIM(RTRIM(CONVERT(NVARCHAR(100), S.FISNO))) + N'','',
                   N'','' + REPLACE(@SiparisNoCsv, N'' '', N'''') + N'','') > 0)
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
        FROM dbo.RII_SH_LINE_SOURCE AS LS
        INNER JOIN dbo.RII_SH_SOURCE_DOCUMENT AS SD ON SD.Id = LS.ShipmentSourceDocumentId
        INNER JOIN dbo.RII_SH_HEADER AS H ON H.Id = SD.ShipmentHeaderId
        WHERE LS.IsDeleted = 0 AND SD.IsDeleted = 0 AND H.IsDeleted = 0
          AND H.Status <> 11 AND H.ErpIntegrationStatus <> 4
          AND (@BranchCode IS NULL OR @BranchCode = '''' OR H.BranchCode = @BranchCode)
        GROUP BY LS.ExternalLineId
    )
    SELECT
        ''L'' AS Mode,
        X.FisNo AS SiparisNo,
        X.OrderID,
        X.OrderLineSequence,
        X.StockCode,
        X.StockName,
        X.UnitCode,
        CAST('''' AS VARCHAR(100)) AS YapKod,
        CAST('''' AS VARCHAR(100)) AS YapAcik,
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
    LEFT JOIN ActiveAllocations AS A ON A.ExternalLineId = CONVERT(NVARCHAR(100), X.OrderID)
    WHERE X.RemainingHamax > 0;';
    END
    ELSE
    BEGIN
        EXEC sys.sp_executesql N'ALTER FUNCTION dbo.RII_FN_SH_LINE
    (
        @SiparisNoCsv NVARCHAR(MAX),
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
            NULLIF(LTRIM(RTRIM(S.PROJE_KODU)), '''') AS ProjectCode,
            COALESCE(
                TRY_CONVERT(DATETIME, S.STHAR_TESTAR, 104),
                TRY_CONVERT(DATETIME, M.SIPARIS_TEST, 104)) AS DeliveryDate,
            CAST(ISNULL(S.STHAR_NF, 0) AS DECIMAL(28, 8)) AS NetUnitPrice,
            CAST(ISNULL(S.STHAR_BF, 0) AS DECIMAL(28, 8)) AS GrossUnitPrice,
            S.STHAR_TARIH AS OrderDate,
            S.INCKEYNO AS OrderID,
            S.SIRA AS OrderLineSequence,
            S.STOK_KODU AS StockCode,
            ST.STOK_ADI AS StockName,
            ST.OLCU_BR1 AS UnitCode,
            COALESCE(M.CARI_KODU, S.STHAR_ACIKLAMA) AS CustomerCode,
            C.CARI_ISIM AS CustomerName,
            CAST(CASE WHEN ISNULL(S.L_YEDEK9, 0) = -1 THEN S.STHAR_GCMIK2 ELSE S.STHAR_GCMIK END AS DECIMAL(18, 4)) AS OrderedQty,
            CAST(ISNULL(S.FIRMA_DOVTUT, 0) AS DECIMAL(18, 4)) AS DeliveredQty
        FROM V3RIICO.dbo.TBLSIPATRA AS S WITH (NOLOCK)
        LEFT JOIN V3RIICO.dbo.TBLSIPAMAS AS M WITH (NOLOCK)
          ON M.FATIRS_NO = S.FISNO
         AND M.FTIRSIP = S.STHAR_FTIRSIP
         AND M.SUBE_KODU = S.SUBE_KODU
        LEFT JOIN V3RIICO.dbo.TBLCASABIT AS C WITH (NOLOCK)
          ON C.CARI_KOD = COALESCE(M.CARI_KODU, S.STHAR_ACIKLAMA)
        LEFT JOIN V3RIICO.dbo.TBLASORTIMAS AS A WITH (NOLOCK) ON A.ASORTIKOD = S.EKALAN1
        LEFT JOIN V3RIICO.dbo.TBLSTSABIT AS ST WITH (NOLOCK) ON ST.STOK_KODU = S.STOK_KODU
        WHERE S.STHAR_FTIRSIP = ''6''
          AND S.STHAR_GCKOD = ''C''
          AND S.STHAR_HTUR <> ''K''
          AND ISNULL(S.L_YEDEK9, 0) <= 0
          AND (A.ASORTIKOD IS NULL OR S.REDNEDEN <> 2)
          AND (CASE WHEN ISNULL(S.L_YEDEK9, 0) = -1 THEN S.STHAR_GCMIK2 ELSE S.STHAR_GCMIK END)
              - ISNULL(S.FIRMA_DOVTUT, 0) > 0
          AND (@BranchCode IS NULL OR @BranchCode = '''' OR CONVERT(VARCHAR(10), S.SUBE_KODU) = @BranchCode)
          AND (NULLIF(REPLACE(LTRIM(RTRIM(@SiparisNoCsv)), N'' '', N''''), N'''') IS NULL
               OR CHARINDEX(
                   N'','' + LTRIM(RTRIM(CONVERT(NVARCHAR(100), S.FISNO))) + N'','',
                   N'','' + REPLACE(@SiparisNoCsv, N'' '', N'''') + N'','') > 0)
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
        FROM dbo.RII_SH_LINE_SOURCE AS LS
        INNER JOIN dbo.RII_SH_SOURCE_DOCUMENT AS SD ON SD.Id = LS.ShipmentSourceDocumentId
        INNER JOIN dbo.RII_SH_HEADER AS H ON H.Id = SD.ShipmentHeaderId
        WHERE LS.IsDeleted = 0 AND SD.IsDeleted = 0 AND H.IsDeleted = 0
          AND H.Status <> 11 AND H.ErpIntegrationStatus <> 4
          AND (@BranchCode IS NULL OR @BranchCode = '''' OR H.BranchCode = @BranchCode)
        GROUP BY LS.ExternalLineId
    )
    SELECT
        ''L'' AS Mode,
        X.FisNo AS SiparisNo,
        X.OrderID,
        X.OrderLineSequence,
        X.StockCode,
        X.StockName,
        X.UnitCode,
        CAST('''' AS VARCHAR(100)) AS YapKod,
        CAST('''' AS VARCHAR(100)) AS YapAcik,
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
    LEFT JOIN ActiveAllocations AS A ON A.ExternalLineId = CONVERT(NVARCHAR(100), X.OrderID)
    WHERE X.RemainingHamax > 0;';
    END
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260730153748_LinkTransferShipmentAndWarehouseSlipsToNetsisOrders'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260730153748_LinkTransferShipmentAndWarehouseSlipsToNetsisOrders', N'10.0.10');
END;

COMMIT;
GO

