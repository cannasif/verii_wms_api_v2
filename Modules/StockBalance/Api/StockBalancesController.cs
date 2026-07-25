using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using verii_wms_api_v2.Modules.AccessControl.Application;
using verii_wms_api_v2.Modules.Audit.Application;
using verii_wms_api_v2.Modules.StockBalance.Application;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.StockBalance.Api;

[Authorize, ApiController, Route("api/stock-balances")]
public sealed class StockBalancesController(IStockBalanceService service, IPermissionAuthorizationService permissions, IAuditLogWriter audit) : ControllerBase
{
    [HttpPost("locations/paged")]
    public async Task<IActionResult> Locations(PagedRequest request, CancellationToken ct)
    {
        await RequireAsync("WMS.STOCK_BALANCES.VIEW", ct);
        return Ok(ApiResponse<PagedResponse<LocationBalanceRow>>.Ok(await service.GetLocationBalancesAsync(request, ct)));
    }

    [HttpPost("warehouses/paged")]
    public async Task<IActionResult> Warehouses(PagedRequest request, CancellationToken ct)
    {
        await RequireAsync("WMS.STOCK_BALANCES.VIEW", ct);
        return Ok(ApiResponse<PagedResponse<WarehouseBalanceRow>>.Ok(await service.GetWarehouseBalancesAsync(request, ct)));
    }

    [HttpPost("serials/paged")]
    public async Task<IActionResult> Serials(PagedRequest request, CancellationToken ct)
    {
        await RequireAsync("WMS.STOCK_BALANCES.VIEW", ct);
        return Ok(ApiResponse<PagedResponse<SerialBalanceRow>>.Ok(await service.GetSerialBalancesAsync(request, ct)));
    }

    [HttpPost("serials/{id:long}/movements/paged")]
    public async Task<IActionResult> SerialMovements(long id, PagedRequest request, CancellationToken ct)
    {
        await RequireAsync("WMS.STOCK_BALANCES.VIEW", ct);
        return Ok(ApiResponse<PagedResponse<SerialMovementHistoryRow>>.Ok(await service.GetSerialMovementHistoryAsync(id, request, ct)));
    }

    [HttpGet("warehouses/{id:long}/drill-down")]
    public async Task<IActionResult> DrillDown(long id, CancellationToken ct)
    {
        await RequireAsync("WMS.STOCK_BALANCES.VIEW", ct);
        return Ok(ApiResponse<StockBalanceDrillDown>.Ok(await service.GetDrillDownAsync(id, ct)));
    }

    [HttpGet("reconciliation/summary")]
    public async Task<IActionResult> ReconciliationSummary(CancellationToken ct)
    {
        await RequireAsync("WMS.STOCK_BALANCES.RECONCILE", ct);
        return Ok(ApiResponse<ReconciliationSummary>.Ok(await service.GetReconciliationSummaryAsync(ct)));
    }

    [HttpPost("reconciliation/issues/paged")]
    public async Task<IActionResult> ReconciliationIssues(PagedRequest request, CancellationToken ct)
    {
        await RequireAsync("WMS.STOCK_BALANCES.RECONCILE", ct);
        return Ok(ApiResponse<PagedResponse<ReconciliationIssue>>.Ok(await service.GetReconciliationIssuesAsync(request, ct)));
    }

    [HttpPost("rebuild")]
    public async Task<IActionResult> Rebuild(CancellationToken ct)
    {
        await RequireAsync("WMS.STOCK_BALANCES.RECONCILE", ct);
        var result = await service.RebuildAsync(ct);
        await audit.WriteAsync(new AuditLogWriteEntry("stock-balance.rebuild", "StockBalanceProjection", "stock-balance-v1", "Succeeded", "stock-balance",
            NewValues: result, ChangedFields: ["LocationProjection", "WarehouseProjection"]), ct);
        return Ok(ApiResponse<ProjectionRebuildResult>.Ok(result, "Stok bakiyesi projection yeniden oluşturuldu."));
    }

    private async Task RequireAsync(string permission, CancellationToken ct)
    {
        if (!await permissions.HasPermissionAsync(User, permission, ct)) throw AppException.Forbidden();
    }
}
