using verii_wms_api_v2.Modules.NetsisRead.Application.Dtos;

namespace verii_wms_api_v2.Modules.NetsisRead.Application;

public interface INetsisReadService
{
    Task<IReadOnlyList<BranchDto>> GetBranchesAsync(int? branchNo, CancellationToken cancellationToken);
    Task<IReadOnlyList<WarehouseDto>> GetWarehousesAsync(short? warehouseCode, int? branchCode, CancellationToken cancellationToken);
    Task<IReadOnlyList<StockDto>> GetStocksAsync(string? stockCode, int? branchCode, CancellationToken cancellationToken);
    Task<IReadOnlyList<NetsisStockTrackingDto>> GetStockTrackingRulesAsync(
        IReadOnlyCollection<string> stockCodes,
        int branchCode,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<NetsisStockBalanceDto>> GetStockBalancesAsync(short? warehouseCode, string? stockCode, CancellationToken cancellationToken);
    Task<IReadOnlyList<CustomerDto>> GetCustomersAsync(string? customerCode, int? branchCode, CancellationToken cancellationToken);
    Task<IReadOnlyList<ConfigurationCodeDto>> GetConfigurationCodesAsync(string? search, int? branchCode, CancellationToken cancellationToken);
    Task<IReadOnlyList<GoodsReceiptOpenOrderHeaderDto>> GetGoodsReceiptOpenOrderHeadersAsync(string customerCode, string? branchCode, CancellationToken cancellationToken);
    Task<IReadOnlyList<GoodsReceiptOpenOrderLineDto>> GetGoodsReceiptOpenOrderLinesAsync(
        string? orderNumbersCsv,
        string? customerCode,
        string? branchCode,
        bool includeUnavailable,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<WarehouseTransferOpenOrderHeaderDto>> GetWarehouseTransferOpenOrderHeadersAsync(string customerCode,string? branchCode,CancellationToken cancellationToken);
    Task<IReadOnlyList<WarehouseTransferOpenOrderLineDto>> GetWarehouseTransferOpenOrderLinesAsync(string orderNumbersCsv,string? branchCode,CancellationToken cancellationToken);
    Task<IReadOnlyList<ShipmentOpenOrderHeaderDto>> GetShipmentOpenOrderHeadersAsync(string customerCode,string? branchCode,CancellationToken cancellationToken);
    Task<IReadOnlyList<ShipmentOpenOrderLineDto>> GetShipmentOpenOrderLinesAsync(string orderNumbersCsv,string? branchCode,CancellationToken cancellationToken);
    Task<IReadOnlyList<KkdCustomerOpenOrderDto>> GetKkdCustomerOpenOrdersAsync(string customerCode, CancellationToken cancellationToken);
    Task<IReadOnlyList<ProductionWorkOrderDto>> GetProductionWorkOrdersAsync(
        string? workOrderNumber,
        int branchCode,
        bool includeClosed,
        int take,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<StockRecipeComponentDto>> GetStockRecipeAsync(
        string stockCode,
        int branchCode,
        string? configurationCode,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<ProductionWorkOrderRecipeComponentDto>> GetProductionWorkOrderRecipeAsync(
        string workOrderNumber,
        int branchCode,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<ProductionWorkOrderRecipeComponentDto>> GetProductionWorkOrderRecipesAsync(
        IReadOnlyCollection<string> workOrderNumbers,
        int branchCode,
        CancellationToken cancellationToken);
}
