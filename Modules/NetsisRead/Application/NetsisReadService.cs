using Microsoft.Data.SqlClient;
using verii_wms_api_v2.Modules.NetsisRead.Application.Dtos;
using verii_wms_api_v2.Modules.NetsisRead.Infrastructure;

namespace verii_wms_api_v2.Modules.NetsisRead.Application;

public sealed class NetsisReadService(INetsisQueryExecutor queryExecutor) : INetsisReadService
{
    private const int ProductionRecipeBatchSize = 500;
    private const int StockTrackingBatchSize = 500;

    public async Task<IReadOnlyList<BranchDto>> GetBranchesAsync(int? branchNo, CancellationToken ct) =>
        await queryExecutor.QueryAsync<BranchDto>("RII_FN_BRANCHES", "SELECT * FROM dbo.RII_FN_BRANCHES(@branchNo)", r => new BranchDto(Get<short>(r, "SUBE_KODU"), NullableString(r, "UNVAN")), ct, Parameter("@branchNo", branchNo));

    public async Task<IReadOnlyList<WarehouseDto>> GetWarehousesAsync(short? warehouseCode, int? branchCode, CancellationToken ct)
    {
        var rows = await queryExecutor.QueryAsync<WarehouseDto>("RII_FN_DEPO", "SELECT * FROM dbo.RII_FN_DEPO(@depoKodu, @branchCode)", r => new WarehouseDto(Get<short>(r, "DEPO_KODU"), String(r, "DEPO_ISMI"), Get<short>(r, "SUBE_KODU")), ct, Parameter("@depoKodu", warehouseCode), Parameter("@branchCode", branchCode));
        return rows;
    }

    public async Task<IReadOnlyList<StockDto>> GetStocksAsync(string? stockCode, int? branchCode, CancellationToken ct)
    {
        var rows = await queryExecutor.QueryAsync<StockDto>("RII_FN_STOK", "SELECT * FROM dbo.RII_FN_STOK(@stokKodu, @branchCode)", r => new StockDto(Get<short>(r,"SUBE_KODU"), Get<short>(r,"ISLETME_KODU"), String(r,"STOK_KODU"), String(r,"URETICI_KODU"), String(r,"STOK_ADI"), String(r,"GRUP_KODU"), String(r,"KOD_1"), String(r,"KOD_2"), String(r,"KOD_3"), String(r,"KOD_4"), String(r,"KOD_5"), NullableString(r,"OLCU_BR1")), ct, Parameter("@stokKodu", stockCode), Parameter("@branchCode", branchCode));
        return rows;
    }

    public async Task<IReadOnlyList<NetsisStockTrackingDto>> GetStockTrackingRulesAsync(
        IReadOnlyCollection<string> stockCodes,
        int branchCode,
        CancellationToken ct)
    {
        var codes = stockCodes
            .Select(Normalize)
            .Where(x => x is not null)
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (codes.Length == 0) return [];

        var result = new List<NetsisStockTrackingDto>(codes.Length);
        foreach (var batch in codes.Chunk(StockTrackingBatchSize))
        {
            var rows = await queryExecutor.QueryAsync<NetsisStockTrackingDto>(
                "RII_FN_STOK.TRACKING",
                """
                SELECT SUBE_KODU, STOK_KODU,
                       GIRIS_SERI, CIKIS_SERI, SERI_BAK, SERI_MIK,
                       SERI_GIR_OT, SERI_CIK_OT
                FROM dbo.RII_FN_STOK(@stockCodes, @branchCode)
                """,
                r => new NetsisStockTrackingDto(
                    Get<short>(r, "SUBE_KODU"),
                    String(r, "STOK_KODU"),
                    NullableString(r, "GIRIS_SERI") ?? "H",
                    NullableString(r, "CIKIS_SERI") ?? "H",
                    NullableString(r, "SERI_BAK") ?? "H",
                    NullableString(r, "SERI_MIK") ?? "H",
                    NullableString(r, "SERI_GIR_OT") ?? "H",
                    NullableString(r, "SERI_CIK_OT") ?? "H"),
                ct,
                Parameter("@stockCodes", string.Join(',', batch)),
                Parameter("@branchCode", branchCode));
            result.AddRange(rows);
        }

        return result;
    }

    public async Task<IReadOnlyList<NetsisStockBalanceDto>> GetStockBalancesAsync(
        short? warehouseCode,
        string? stockCode,
        CancellationToken ct)
    {
        stockCode = Normalize(stockCode);

        return await queryExecutor.QueryAsync<NetsisStockBalanceDto>(
            "RII_FN_STOCK_BALANCE",
            """
            SELECT DEPO_KODU, STOK_KODU, BAKIYE
            FROM dbo.RII_FN_STOCK_BALANCE(@warehouseCode, @stockCode)
            ORDER BY DEPO_KODU, STOK_KODU
            """,
            r => new NetsisStockBalanceDto(
                Nullable<short>(r, "DEPO_KODU"),
                String(r, "STOK_KODU"),
                Get<decimal>(r, "BAKIYE")),
            ct,
            new SqlParameter("@warehouseCode", System.Data.SqlDbType.Int)
            {
                Value = (object?)warehouseCode ?? DBNull.Value
            },
            new SqlParameter("@stockCode", System.Data.SqlDbType.VarChar, 50)
            {
                Value = (object?)stockCode ?? DBNull.Value
            });
    }

    public async Task<IReadOnlyList<CustomerDto>> GetCustomersAsync(string? customerCode, int? branchCode, CancellationToken ct)
    {
        var rows = await queryExecutor.QueryAsync<CustomerDto>(
            "RII_FN_CARI",
            "SELECT * FROM dbo.RII_FN_CARI(@cariKodu, @branchCode)",
            r => new CustomerDto(
                Get<short>(r, "SUBE_KODU"),
                Get<short>(r, "ISLETME_KODU"),
                String(r, "CARI_KOD"),
                NullableString(r, "CARI_ISIM"),
                NullableString(r, "CARI_TEL"),
                NullableString(r, "CARI_IL"),
                NullableString(r, "ULKE_KODU"),
                NullableString(r, "CARI_TIP"),
                NullableString(r, "CARI_ADRES"),
                NullableString(r, "CARI_ILCE"),
                NullableString(r, "VERGI_DAIRESI"),
                NullableString(r, "EMAIL"),
                NullableString(r, "WEB"),
                NullableString(r, "CARI_TEL2"),
                NullableString(r, "CARI_TEL3")),
            ct,
            Parameter("@cariKodu", customerCode),
            Parameter("@branchCode", branchCode));
        return rows;
    }

    public async Task<IReadOnlyList<ConfigurationCodeDto>> GetConfigurationCodesAsync(string? search, int? branchCode, CancellationToken ct)
    {
        var rows = await queryExecutor.QueryAsync<ConfigurationCodeDto>(
            "RII_FN_ESNYAPMAS",
            "SELECT * FROM dbo.RII_FN_ESNYAPMAS()",
            r => new ConfigurationCodeDto(
                String(r, "YAPKOD"),
                String(r, "YAPACIK"),
                Nullable<short>(r, "SUBE_KODU"),
                NullableString(r, "YPLNDRSTOKKOD"),
                Nullable<long>(r, "StockId")),
            ct);

        return rows.Where(x =>
            (!branchCode.HasValue || !x.BranchCode.HasValue || x.BranchCode == branchCode)
            && (string.IsNullOrWhiteSpace(search)
                || $"{x.ConfigurationCode} {x.Description}".Contains(search, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    public async Task<IReadOnlyList<GoodsReceiptOpenOrderHeaderDto>> GetGoodsReceiptOpenOrderHeadersAsync(string customerCode, string? branchCode, CancellationToken ct)
    {
        customerCode = Normalize(customerCode) ?? throw new ArgumentException("Müşteri kodu zorunludur.", nameof(customerCode));

        return await queryExecutor.QueryAsync<GoodsReceiptOpenOrderHeaderDto>(
            "RII_FN_GR_OPENORDERS_HEADER",
            "SELECT * FROM dbo.RII_FN_GR_OPENORDERS_HEADER(@customerCode, @branchCode)",
            r => new GoodsReceiptOpenOrderHeaderDto(
                String(r, "Mode"), String(r, "SiparisNo"), Nullable<int>(r, "OrderID"),
                NullableString(r, "CustomerCode"), NullableString(r, "CustomerName"),
                Nullable<int>(r, "BranchCode"), Nullable<int>(r, "TargetWh"), NullableString(r, "ProjectCode"),
                Nullable<DateTime>(r, "OrderDate"), Nullable<DateTime>(r, "DeliveryDate"),
                Nullable<decimal>(r, "OrderedQty"),
                Nullable<decimal>(r, "DeliveredQty"), Nullable<decimal>(r, "RemainingHamax"),
                Nullable<decimal>(r, "PlannedQtyAllocated"), Nullable<decimal>(r, "RemainingForImport")),
            ct, Parameter("@customerCode", customerCode), Parameter("@branchCode", Normalize(branchCode)));
    }

    public async Task<IReadOnlyList<GoodsReceiptOpenOrderLineDto>> GetGoodsReceiptOpenOrderLinesAsync(
        string? orderNumbersCsv,
        string? customerCode,
        string? branchCode,
        bool includeUnavailable,
        CancellationToken ct)
    {
        orderNumbersCsv = Normalize(orderNumbersCsv);
        customerCode = Normalize(customerCode);
        if (orderNumbersCsv is null && customerCode is null)
            throw new ArgumentException("Sipariş numarası veya müşteri kodu zorunludur.");

        var rows = await queryExecutor.QueryAsync<GoodsReceiptOpenOrderLineDto>(
            "RII_FN_GR_OPENORDERS_LINE",
            "SELECT * FROM dbo.RII_FN_GR_OPENORDERS_LINE(@orderNumbersCsv, @customerCode, @branchCode)",
            r => new GoodsReceiptOpenOrderLineDto(
                String(r, "Mode"), String(r, "SiparisNo"), Get<int>(r, "OrderID"),
                Get<int>(r, "OrderLineSequence"),
                NullableString(r, "StockCode"), NullableString(r, "StockName"),
                NullableString(r, "UnitCode"),
                NullableString(r, "YapKod"), NullableString(r, "YapAcik"),
                NullableString(r, "CustomerCode"), NullableString(r, "CustomerName"),
                Nullable<int>(r, "BranchCode"), Nullable<int>(r, "TargetWh"), NullableString(r, "ProjectCode"),
                Nullable<DateTime>(r, "OrderDate"), Nullable<DateTime>(r, "DeliveryDate"),
                Nullable<decimal>(r, "NetUnitPrice"), Nullable<decimal>(r, "GrossUnitPrice"),
                Nullable<decimal>(r, "OrderedQty"),
                Nullable<decimal>(r, "DeliveredQty"), Nullable<decimal>(r, "RemainingHamax"),
                Nullable<decimal>(r, "PlannedQtyAllocated"), Nullable<decimal>(r, "RemainingForImport")),
            ct, Parameter("@orderNumbersCsv", orderNumbersCsv), Parameter("@customerCode", customerCode),
            Parameter("@branchCode", Normalize(branchCode)));
        return includeUnavailable
            ? rows
            : rows.Where(x => x.AvailableQuantity > 0).ToList();
    }

    public async Task<IReadOnlyList<WarehouseTransferOpenOrderHeaderDto>> GetWarehouseTransferOpenOrderHeadersAsync(string customerCode,string? branchCode,CancellationToken ct)
    {
        customerCode=Normalize(customerCode)??throw new ArgumentException("Müşteri kodu zorunludur.",nameof(customerCode));
        return await queryExecutor.QueryAsync<WarehouseTransferOpenOrderHeaderDto>(
            "RII_FN_WT_HEADER","SELECT * FROM dbo.RII_FN_WT_HEADER(@customerCode,@branchCode)",
            r=>new WarehouseTransferOpenOrderHeaderDto(
                String(r,"Mode"),String(r,"SiparisNo"),Nullable<int>(r,"OrderID"),
                NullableString(r,"CustomerCode"),NullableString(r,"CustomerName"),Nullable<int>(r,"BranchCode"),
                Nullable<int>(r,"TargetWh"),NullableString(r,"ProjectCode"),Nullable<DateTime>(r,"OrderDate"),
                Nullable<decimal>(r,"OrderedQty"),Nullable<decimal>(r,"DeliveredQty"),Nullable<decimal>(r,"RemainingHamax"),
                Nullable<decimal>(r,"PlannedQtyAllocated"),Nullable<decimal>(r,"RemainingForImport")),
            ct,Parameter("@customerCode",customerCode),Parameter("@branchCode",Normalize(branchCode)));
    }

    public async Task<IReadOnlyList<WarehouseTransferOpenOrderLineDto>> GetWarehouseTransferOpenOrderLinesAsync(string orderNumbersCsv,string? branchCode,CancellationToken ct)
    {
        orderNumbersCsv=Normalize(orderNumbersCsv)??throw new ArgumentException("Sipariş numarası zorunludur.",nameof(orderNumbersCsv));
        return await queryExecutor.QueryAsync<WarehouseTransferOpenOrderLineDto>(
            "RII_FN_WT_LINE","SELECT * FROM dbo.RII_FN_WT_LINE(@orderNumbersCsv,@branchCode)",
            r=>new WarehouseTransferOpenOrderLineDto(
                String(r,"Mode"),String(r,"SiparisNo"),Get<int>(r,"OrderID"),Get<int>(r,"OrderLineSequence"),NullableString(r,"StockCode"),
                NullableString(r,"StockName"),NullableString(r,"UnitCode"),NullableString(r,"YapKod"),NullableString(r,"YapAcik"),
                NullableString(r,"CustomerCode"),NullableString(r,"CustomerName"),Nullable<int>(r,"BranchCode"),
                Nullable<int>(r,"TargetWh"),NullableString(r,"ProjectCode"),Nullable<DateTime>(r,"OrderDate"),Nullable<DateTime>(r,"DeliveryDate"),
                Nullable<decimal>(r,"NetUnitPrice"),Nullable<decimal>(r,"GrossUnitPrice"),
                Nullable<decimal>(r,"OrderedQty"),Nullable<decimal>(r,"DeliveredQty"),Nullable<decimal>(r,"RemainingHamax"),
                Nullable<decimal>(r,"PlannedQtyAllocated"),Nullable<decimal>(r,"RemainingForImport")),
            ct,Parameter("@orderNumbersCsv",orderNumbersCsv),Parameter("@branchCode",Normalize(branchCode)));
    }

    public async Task<IReadOnlyList<ShipmentOpenOrderHeaderDto>> GetShipmentOpenOrderHeadersAsync(string customerCode,string? branchCode,CancellationToken ct)
    {
        customerCode=Normalize(customerCode)??throw new ArgumentException("Müşteri kodu zorunludur.",nameof(customerCode));
        return await queryExecutor.QueryAsync<ShipmentOpenOrderHeaderDto>("RII_FN_SH_HEADER","SELECT * FROM dbo.RII_FN_SH_HEADER(@customerCode,@branchCode)",
            r=>new ShipmentOpenOrderHeaderDto(String(r,"Mode"),String(r,"SiparisNo"),Nullable<long>(r,"OrderID"),NullableString(r,"CustomerCode"),NullableString(r,"CustomerName"),Nullable<int>(r,"BranchCode"),Nullable<int>(r,"TargetWh"),NullableString(r,"ProjectCode"),Nullable<DateTime>(r,"OrderDate"),Nullable<decimal>(r,"OrderedQty"),Nullable<decimal>(r,"DeliveredQty"),Nullable<decimal>(r,"RemainingHamax"),Nullable<decimal>(r,"PlannedQtyAllocated"),Nullable<decimal>(r,"RemainingForImport")),
            ct,Parameter("@customerCode",customerCode),Parameter("@branchCode",Normalize(branchCode)));
    }

    public async Task<IReadOnlyList<ShipmentOpenOrderLineDto>> GetShipmentOpenOrderLinesAsync(string orderNumbersCsv,string? branchCode,CancellationToken ct)
    {
        orderNumbersCsv=Normalize(orderNumbersCsv)??throw new ArgumentException("Sipariş numarası zorunludur.",nameof(orderNumbersCsv));
        return await queryExecutor.QueryAsync<ShipmentOpenOrderLineDto>("RII_FN_SH_LINE","SELECT * FROM dbo.RII_FN_SH_LINE(@orderNumbersCsv,@branchCode)",
            r=>new ShipmentOpenOrderLineDto(String(r,"Mode"),String(r,"SiparisNo"),Get<long>(r,"OrderID"),Get<int>(r,"OrderLineSequence"),NullableString(r,"StockCode"),NullableString(r,"StockName"),NullableString(r,"UnitCode"),NullableString(r,"YapKod"),NullableString(r,"YapAcik"),NullableString(r,"CustomerCode"),NullableString(r,"CustomerName"),Nullable<int>(r,"BranchCode"),Nullable<int>(r,"TargetWh"),NullableString(r,"ProjectCode"),Nullable<DateTime>(r,"OrderDate"),Nullable<DateTime>(r,"DeliveryDate"),Nullable<decimal>(r,"NetUnitPrice"),Nullable<decimal>(r,"GrossUnitPrice"),Nullable<decimal>(r,"OrderedQty"),Nullable<decimal>(r,"DeliveredQty"),Nullable<decimal>(r,"RemainingHamax"),Nullable<decimal>(r,"PlannedQtyAllocated"),Nullable<decimal>(r,"RemainingForImport")),
            ct,Parameter("@orderNumbersCsv",orderNumbersCsv),Parameter("@branchCode",Normalize(branchCode)));
    }

    public async Task<IReadOnlyList<KkdCustomerOpenOrderDto>> GetKkdCustomerOpenOrdersAsync(string customerCode, CancellationToken ct)
    {
        customerCode = Normalize(customerCode) ?? throw new ArgumentException("Müşteri kodu zorunludur.", nameof(customerCode));
        // V1 ile aynı kaynak: sevk RII_FN_SH_HEADER değil, KKD HTUR='H' açık siparişleri.
        return await queryExecutor.QueryAsync<KkdCustomerOpenOrderDto>(
            "RII_FN_KKD_CARIACIKSIPARISGETIR",
            """
            SELECT
                A.STOK_KODU AS StockCode,
                B.GRUP_KODU AS GroupCode,
                A.FISNO AS OrderNumber,
                A.STHAR_TARIH AS OrderDate,
                CAST((A.STHAR_GCMIK - ISNULL(A.FIRMA_DOVTUT, 0)) AS decimal(18,4)) AS RemainingQuantity,
                A.STHAR_ACIKLAMA AS CustomerCode,
                CONVERT(int, A.DEPO_KODU) AS WarehouseCode,
                CONVERT(bigint, A.INCKEYNO) AS OrderId,
                ISNULL(B.STOK_ADI, A.STOK_KODU) AS StockName,
                NULLIF(LTRIM(RTRIM(B.OLCU_BR1)), '') AS UnitCode,
                NULLIF(LTRIM(RTRIM(A.PROJE_KODU)), '') AS ProjectCode
            FROM V3RIICO.dbo.TBLSIPATRA AS A WITH (NOLOCK)
            LEFT JOIN V3RIICO.dbo.TBLSTSABIT AS B WITH (NOLOCK)
                ON A.STOK_KODU = B.STOK_KODU
            WHERE A.STHAR_FTIRSIP = '6'
              AND A.STHAR_HTUR = 'H'
              AND (A.STHAR_GCMIK - ISNULL(A.FIRMA_DOVTUT, 0)) > 0
              AND A.STHAR_ACIKLAMA = @customerCode
            """,
            r => new KkdCustomerOpenOrderDto(
                String(r, "StockCode"),
                NullableString(r, "GroupCode"),
                String(r, "OrderNumber"),
                Nullable<DateTime>(r, "OrderDate"),
                Nullable<decimal>(r, "RemainingQuantity") ?? 0m,
                NullableString(r, "CustomerCode"),
                Nullable<int>(r, "WarehouseCode"),
                Nullable<long>(r, "OrderId"),
                NullableString(r, "StockName"),
                NullableString(r, "UnitCode"),
                NullableString(r, "ProjectCode")),
            ct,
            Parameter("@customerCode", customerCode));
    }

    public async Task<IReadOnlyList<ProductionWorkOrderDto>> GetProductionWorkOrdersAsync(
        string? workOrderNumber,
        int branchCode,
        bool includeClosed,
        int take,
        CancellationToken ct)
    {
        workOrderNumber = Normalize(workOrderNumber);
        take = Math.Clamp(take, 1, 1_000);

        return await queryExecutor.QueryAsync<ProductionWorkOrderDto>(
            "RII_FN_ISEMRI",
            """
            SELECT TOP (@take) *
            FROM dbo.RII_FN_ISEMRI(@workOrderNumber, @branchCode, @includeClosed)
            ORDER BY Tarih DESC, IsEmriNo
            """,
            r => new ProductionWorkOrderDto(
                String(r, "IsEmriNo"), Nullable<int>(r, "SubeKodu"), String(r, "StokKodu"), String(r, "StokAdi"),
                NullableString(r, "YapilandirmaKodu"), Get<decimal>(r, "IsEmriMiktari"), Get<int>(r, "BirimSirasi"),
                NullableString(r, "BirimKodu"), Get<decimal>(r, "ReceteToplami"), Nullable<DateTime>(r, "Tarih"),
                Nullable<DateTime>(r, "TeslimTarihi"), NullableString(r, "SiparisNo"), Get<int>(r, "SiparisSatirNo"),
                NullableString(r, "ProjeKodu"), Get<int>(r, "DepoKodu"), Get<int>(r, "CikisDepoKodu"), Get<bool>(r, "Kapali"),
                NullableString(r, "Aciklama")),
            ct,
            Parameter("@take", take),
            Parameter("@workOrderNumber", workOrderNumber),
            Parameter("@branchCode", branchCode),
            Parameter("@includeClosed", includeClosed));
    }

    public async Task<IReadOnlyList<StockRecipeComponentDto>> GetStockRecipeAsync(
        string stockCode,
        int branchCode,
        string? configurationCode,
        CancellationToken ct)
    {
        stockCode = Normalize(stockCode) ?? throw new ArgumentException("Stok kodu zorunludur.", nameof(stockCode));

        return await queryExecutor.QueryAsync<StockRecipeComponentDto>(
            "RII_FN_STOK_RECETE",
            "SELECT * FROM dbo.RII_FN_STOK_RECETE(@stockCode, @branchCode, @configurationCode) ORDER BY OperasyonNo, BilesenStokKodu",
            r => new StockRecipeComponentDto(
                Get<int>(r, "SubeKodu"), String(r, "MamulKodu"), String(r, "MamulAdi"), NullableString(r, "MamulBirimKodu"),
                NullableString(r, "MamulYapilandirmaKodu"), String(r, "BilesenStokKodu"), NullableString(r, "BilesenStokAdi"),
                NullableString(r, "BilesenBirimKodu"), NullableString(r, "BilesenYapilandirmaKodu"), Get<int>(r, "OperasyonNo"),
                Get<decimal>(r, "ReceteToplami"), Get<decimal>(r, "ReceteMiktari"), Get<decimal>(r, "BirMamulIcinMiktar"),
                Get<decimal>(r, "FireDegeri"), Get<decimal>(r, "SabitFireMiktari"), Get<bool>(r, "MiktarSabit")),
            ct,
            Parameter("@stockCode", stockCode),
            Parameter("@branchCode", branchCode),
            Parameter("@configurationCode", Normalize(configurationCode)));
    }

    public async Task<IReadOnlyList<ProductionWorkOrderRecipeComponentDto>> GetProductionWorkOrderRecipeAsync(
        string workOrderNumber,
        int branchCode,
        CancellationToken ct)
    {
        workOrderNumber = Normalize(workOrderNumber)
            ?? throw new ArgumentException("İş emri numarası zorunludur.", nameof(workOrderNumber));

        return await queryExecutor.QueryAsync<ProductionWorkOrderRecipeComponentDto>(
            "RII_FN_ISEMRI_RECETE",
            "SELECT * FROM dbo.RII_FN_ISEMRI_RECETE(@workOrderNumber, @branchCode) ORDER BY OperasyonNo, BilesenStokKodu",
            MapProductionWorkOrderRecipe,
            ct,
            Parameter("@workOrderNumber", workOrderNumber),
            Parameter("@branchCode", branchCode));
    }

    public async Task<IReadOnlyList<ProductionWorkOrderRecipeComponentDto>> GetProductionWorkOrderRecipesAsync(
        IReadOnlyCollection<string> workOrderNumbers,
        int branchCode,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(workOrderNumbers);

        var normalized = workOrderNumbers
            .Select(Normalize)
            .Where(workOrderNumber => workOrderNumber is not null)
            .Select(workOrderNumber => workOrderNumber!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (normalized.Length == 0)
            return [];

        var result = new List<ProductionWorkOrderRecipeComponentDto>();
        foreach (var batch in normalized.Chunk(ProductionRecipeBatchSize))
        {
            var workOrderParameters = batch
                .Select((workOrderNumber, index) =>
                    new SqlParameter($"@workOrderNumber{index}", System.Data.SqlDbType.NVarChar, 100)
                    {
                        Value = workOrderNumber
                    })
                .ToArray();
            var values = string.Join(", ", workOrderParameters.Select(parameter => $"({parameter.ParameterName})"));
            var sql = $"""
                WITH RequestedWorkOrders (WorkOrderNumber) AS
                (
                    SELECT WorkOrderNumber
                    FROM (VALUES {values}) AS Requested(WorkOrderNumber)
                )
                SELECT Recipe.*
                FROM RequestedWorkOrders AS Requested
                CROSS APPLY dbo.RII_FN_ISEMRI_RECETE(Requested.WorkOrderNumber, @branchCode) AS Recipe
                ORDER BY Recipe.IsEmriNo, Recipe.OperasyonNo, Recipe.BilesenStokKodu
                """;
            var parameters = workOrderParameters
                .Cast<SqlParameter>()
                .Append(Parameter("@branchCode", branchCode))
                .ToArray();

            var rows = await queryExecutor.QueryAsync<ProductionWorkOrderRecipeComponentDto>(
                "RII_FN_ISEMRI_RECETE_BATCH",
                sql,
                MapProductionWorkOrderRecipe,
                ct,
                parameters);
            result.AddRange(rows);
        }

        return result;
    }

    private static ProductionWorkOrderRecipeComponentDto MapProductionWorkOrderRecipe(SqlDataReader r) =>
        new(
            String(r, "IsEmriNo"), Nullable<int>(r, "SubeKodu"), String(r, "MamulKodu"), String(r, "MamulAdi"),
            NullableString(r, "YapilandirmaKodu"), Get<decimal>(r, "IsEmriMiktari"), NullableString(r, "MamulBirimKodu"),
            Get<decimal>(r, "ReceteToplami"), String(r, "BilesenStokKodu"), NullableString(r, "BilesenStokAdi"),
            NullableString(r, "BilesenBirimKodu"), NullableString(r, "BilesenYapilandirmaKodu"), Get<int>(r, "OperasyonNo"),
            Get<decimal>(r, "ReceteMiktari"), Get<decimal>(r, "BirMamulIcinMiktar"), Get<decimal>(r, "FireDegeri"),
            Get<decimal>(r, "SabitFireMiktari"), Get<bool>(r, "MiktarSabit"), Get<decimal>(r, "BazIhtiyacMiktari"),
            Get<decimal>(r, "DegiskenFireMiktari"), Get<decimal>(r, "ToplamIhtiyacMiktari"));

    private static SqlParameter Parameter(string name, object? value) => new(name, value ?? DBNull.Value);
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string String(SqlDataReader r, string name) => NullableString(r,name) ?? string.Empty;
    private static string? NullableString(SqlDataReader r, string name) { var i=r.GetOrdinal(name); return r.IsDBNull(i)?null:Convert.ToString(r.GetValue(i)); }
    private static T Get<T>(SqlDataReader r, string name) => (T)Convert.ChangeType(r.GetValue(r.GetOrdinal(name)), typeof(T));
    private static T? Nullable<T>(SqlDataReader r, string name) where T:struct { var i=r.GetOrdinal(name); return r.IsDBNull(i)?null:(T)Convert.ChangeType(r.GetValue(i),typeof(T)); }
}
