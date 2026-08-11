using Hangfire;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using verii_wms_api_v2.Modules.AccessControl.Application;
using verii_wms_api_v2.Modules.ErpBalanceSync.Application;
using verii_wms_api_v2.Modules.ErpBalanceSync.Domain;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.ErpBalanceSync.Api;

[Authorize, ApiController, Route("api/erp-balance-sync")]
public sealed class ErpBalanceSyncController(
    IErpStockBalanceQueryService queries,
    IBackgroundJobClient jobs,
    IPermissionAuthorizationService permissions) : ControllerBase
{
    [HttpPost("balances/paged")]
    public async Task<IActionResult> Balances(PagedRequest request, CancellationToken cancellationToken)
    {
        await RequireAsync("WMS.STOCK_BALANCES.VIEW", cancellationToken);
        return Ok(ApiResponse<PagedResponse<ErpWarehouseStockBalanceRow>>.Ok(
            await queries.GetBalancesAsync(request, cancellationToken)));
    }

    [HttpPost("changes/paged")]
    public async Task<IActionResult> Changes(PagedRequest request, CancellationToken cancellationToken)
    {
        await RequireAsync("WMS.STOCK_BALANCES.VIEW", cancellationToken);
        return Ok(ApiResponse<PagedResponse<ErpStockBalanceChangeRow>>.Ok(
            await queries.GetChangesAsync(request, cancellationToken)));
    }

    [HttpPost("runs/paged")]
    public async Task<IActionResult> Runs(PagedRequest request, CancellationToken cancellationToken)
    {
        await RequireAsync("ERP.MIRROR.VIEW", cancellationToken);
        return Ok(ApiResponse<PagedResponse<ErpStockBalanceSyncRunRow>>.Ok(
            await queries.GetRunsAsync(request, cancellationToken)));
    }

    [HttpPost("sync/full")]
    public async Task<IActionResult> EnqueueFull(CancellationToken cancellationToken)
    {
        await RequireAsync("ERP.MIRROR.SYNC", cancellationToken);
        var jobId = jobs.Enqueue<IErpStockBalanceSyncJobRunner>(runner =>
            runner.RunAsync(new ErpStockBalanceSyncJobRequest(
                ErpStockBalanceSyncModes.Full,
                ErpStockBalanceSyncTriggerSources.Manual,
                Array.Empty<ErpStockBalanceTarget>()), CancellationToken.None));
        return Accepted(new { jobId });
    }

    [HttpPost("sync/targeted")]
    public async Task<IActionResult> EnqueueTargeted(
        IReadOnlyList<ErpStockBalanceTarget> targets,
        CancellationToken cancellationToken)
    {
        await RequireAsync("ERP.MIRROR.SYNC", cancellationToken);
        if (targets.Count == 0)
            throw AppException.BadRequest("En az bir depo ve stok hedefi zorunludur.");
        var jobId = jobs.Enqueue<IErpStockBalanceSyncJobRunner>(runner =>
            runner.RunAsync(new ErpStockBalanceSyncJobRequest(
                ErpStockBalanceSyncModes.Targeted,
                ErpStockBalanceSyncTriggerSources.Manual,
                targets), CancellationToken.None));
        return Accepted(new { jobId });
    }

    private async Task RequireAsync(string permission, CancellationToken cancellationToken)
    {
        if (!await permissions.HasPermissionAsync(User, permission, cancellationToken))
            throw AppException.Forbidden();
    }
}
