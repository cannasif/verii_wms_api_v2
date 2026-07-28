using Microsoft.Data.SqlClient;
using verii_wms_api_v2.Modules.NetsisRead.Application.Dtos;
using verii_wms_api_v2.Modules.NetsisRead.Infrastructure;

namespace verii_wms_api_v2.Modules.NetsisRead.Application;

public sealed class NetsisReadService(INetsisQueryExecutor queryExecutor) : INetsisReadService
{
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

    public async Task<IReadOnlyList<CustomerDto>> GetCustomersAsync(string? customerCode, int? branchCode, CancellationToken ct)
    {
        var rows = await queryExecutor.QueryAsync<CustomerDto>("RII_FN_CARI", "SELECT * FROM dbo.RII_FN_CARI(@cariKodu, @branchCode)", r => new CustomerDto(Get<short>(r,"SUBE_KODU"), Get<short>(r,"ISLETME_KODU"), String(r,"CARI_KOD"), NullableString(r,"CARI_ISIM")), ct, Parameter("@cariKodu", customerCode), Parameter("@branchCode", branchCode));
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
                Nullable<DateTime>(r, "OrderDate"), Nullable<decimal>(r, "OrderedQty"),
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
                NullableString(r, "StockCode"), NullableString(r, "StockName"),
                NullableString(r, "UnitCode"),
                NullableString(r, "YapKod"), NullableString(r, "YapAcik"),
                NullableString(r, "CustomerCode"), NullableString(r, "CustomerName"),
                Nullable<int>(r, "BranchCode"), Nullable<int>(r, "TargetWh"), NullableString(r, "ProjectCode"),
                Nullable<DateTime>(r, "OrderDate"), Nullable<decimal>(r, "OrderedQty"),
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
                String(r,"Mode"),String(r,"SiparisNo"),Get<int>(r,"OrderID"),NullableString(r,"StockCode"),
                NullableString(r,"StockName"),NullableString(r,"YapKod"),NullableString(r,"YapAcik"),
                NullableString(r,"CustomerCode"),NullableString(r,"CustomerName"),Nullable<int>(r,"BranchCode"),
                Nullable<int>(r,"TargetWh"),NullableString(r,"ProjectCode"),Nullable<DateTime>(r,"OrderDate"),
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
            r=>new ShipmentOpenOrderLineDto(String(r,"Mode"),String(r,"SiparisNo"),Get<long>(r,"OrderID"),NullableString(r,"StockCode"),NullableString(r,"StockName"),NullableString(r,"YapKod"),NullableString(r,"YapAcik"),NullableString(r,"CustomerCode"),NullableString(r,"CustomerName"),Nullable<int>(r,"BranchCode"),Nullable<int>(r,"TargetWh"),NullableString(r,"ProjectCode"),Nullable<DateTime>(r,"OrderDate"),Nullable<decimal>(r,"OrderedQty"),Nullable<decimal>(r,"DeliveredQty"),Nullable<decimal>(r,"RemainingHamax"),Nullable<decimal>(r,"PlannedQtyAllocated"),Nullable<decimal>(r,"RemainingForImport")),
            ct,Parameter("@orderNumbersCsv",orderNumbersCsv),Parameter("@branchCode",Normalize(branchCode)));
    }

    private static SqlParameter Parameter(string name, object? value) => new(name, value ?? DBNull.Value);
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string String(SqlDataReader r, string name) => NullableString(r,name) ?? string.Empty;
    private static string? NullableString(SqlDataReader r, string name) { var i=r.GetOrdinal(name); return r.IsDBNull(i)?null:Convert.ToString(r.GetValue(i)); }
    private static T Get<T>(SqlDataReader r, string name) => (T)Convert.ChangeType(r.GetValue(r.GetOrdinal(name)), typeof(T));
    private static T? Nullable<T>(SqlDataReader r, string name) where T:struct { var i=r.GetOrdinal(name); return r.IsDBNull(i)?null:(T)Convert.ChangeType(r.GetValue(i),typeof(T)); }
}
