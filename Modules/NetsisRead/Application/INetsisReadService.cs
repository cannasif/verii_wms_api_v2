using verii_wms_api_v2.Modules.NetsisRead.Application.Dtos;

namespace verii_wms_api_v2.Modules.NetsisRead.Application;

public interface INetsisReadService
{
    Task<IReadOnlyList<BranchDto>> GetBranchesAsync(int? branchNo, CancellationToken cancellationToken);
    Task<IReadOnlyList<WarehouseDto>> GetWarehousesAsync(short? warehouseCode, int? branchCode, CancellationToken cancellationToken);
    Task<IReadOnlyList<StockDto>> GetStocksAsync(string? stockCode, int? branchCode, CancellationToken cancellationToken);
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
}
