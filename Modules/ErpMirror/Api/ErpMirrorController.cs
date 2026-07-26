using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using verii_wms_api_v2.Modules.AccessControl.Application;
using verii_wms_api_v2.Modules.ErpMirror.Application;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.ErpMirror.Api;

[Authorize, ApiController]
[Route("api/erp-mirror")]
public sealed class ErpMirrorController(IErpMirrorService service, IPermissionAuthorizationService permissions) : ControllerBase
{
    [HttpPost("warehouses/paged")] public async Task<IActionResult> WarehousesPaged([FromBody] PagedRequest request, CancellationToken ct) { await Require("ERP.MIRROR.VIEW", ct); return Ok(ApiResponse<PagedResponse<WarehouseMirrorDto>>.Ok(await service.GetWarehousesPagedAsync(request, ct))); }
    [HttpPost("stocks/paged")] public async Task<IActionResult> StocksPaged([FromBody] PagedRequest request, CancellationToken ct) { await Require("ERP.MIRROR.VIEW", ct); return Ok(ApiResponse<PagedResponse<StockMirrorDto>>.Ok(await service.GetStocksPagedAsync(request, ct))); }
    [HttpPost("customers/paged")] public async Task<IActionResult> CustomersPaged([FromBody] PagedRequest request, CancellationToken ct) { await Require("ERP.MIRROR.VIEW", ct); return Ok(ApiResponse<PagedResponse<CustomerMirrorDto>>.Ok(await service.GetCustomersPagedAsync(request, ct))); }
    [HttpPost("configuration-codes/paged"), HttpPost("yap-codes/paged")]
    public async Task<IActionResult> ConfigurationCodesPaged([FromBody] PagedRequest request, CancellationToken ct) { await Require("ERP.MIRROR.VIEW", ct); return Ok(ApiResponse<PagedResponse<ConfigurationCodeMirrorDto>>.Ok(await service.GetConfigurationCodesPagedAsync(request, ct))); }
    [HttpPost("sync/all")] public async Task<IActionResult> SyncAll(CancellationToken ct) { await Require("ERP.MIRROR.SYNC", ct); return Ok(ApiResponse<IReadOnlyList<MirrorSyncResult>>.Ok(await service.SyncAllAsync(ct))); }
    [HttpPost("sync/warehouses")] public async Task<IActionResult> SyncWarehouses(CancellationToken ct) { await Require("ERP.MIRROR.SYNC", ct); return Ok(ApiResponse<MirrorSyncResult>.Ok(await service.SyncWarehousesAsync(ct))); }
    [HttpPost("sync/stocks")] public async Task<IActionResult> SyncStocks(CancellationToken ct) { await Require("ERP.MIRROR.SYNC", ct); return Ok(ApiResponse<MirrorSyncResult>.Ok(await service.SyncStocksAsync(ct))); }
    [HttpPost("sync/customers")] public async Task<IActionResult> SyncCustomers(CancellationToken ct) { await Require("ERP.MIRROR.SYNC", ct); return Ok(ApiResponse<MirrorSyncResult>.Ok(await service.SyncCustomersAsync(ct))); }
    [HttpPost("sync/configuration-codes"), HttpPost("sync/yap-codes")]
    public async Task<IActionResult> SyncConfigurationCodes(CancellationToken ct) { await Require("ERP.MIRROR.SYNC", ct); return Ok(ApiResponse<MirrorSyncResult>.Ok(await service.SyncConfigurationCodesAsync(ct))); }

    private async Task Require(string code, CancellationToken ct)
    {
        if (!await permissions.HasPermissionAsync(User, code, ct)) throw AppException.Forbidden();
    }
}
