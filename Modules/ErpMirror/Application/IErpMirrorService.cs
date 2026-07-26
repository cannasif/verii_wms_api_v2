using verii_wms_api_v2.Shared;

namespace verii_wms_api_v2.Modules.ErpMirror.Application;

public interface IErpMirrorService
{
    Task<MirrorSyncResult> SyncWarehousesAsync(CancellationToken cancellationToken = default);
    Task<MirrorSyncResult> SyncStocksAsync(CancellationToken cancellationToken = default);
    Task<MirrorSyncResult> SyncCustomersAsync(CancellationToken cancellationToken = default);
    Task<MirrorSyncResult> SyncConfigurationCodesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MirrorSyncResult>> SyncAllAsync(CancellationToken cancellationToken = default);
    Task<PagedResponse<WarehouseMirrorDto>> GetWarehousesPagedAsync(PagedRequest request, CancellationToken cancellationToken = default);
    Task<PagedResponse<StockMirrorDto>> GetStocksPagedAsync(PagedRequest request, CancellationToken cancellationToken = default);
    Task<PagedResponse<CustomerMirrorDto>> GetCustomersPagedAsync(PagedRequest request, CancellationToken cancellationToken = default);
    Task<PagedResponse<ConfigurationCodeMirrorDto>> GetConfigurationCodesPagedAsync(PagedRequest request, CancellationToken cancellationToken = default);
}
