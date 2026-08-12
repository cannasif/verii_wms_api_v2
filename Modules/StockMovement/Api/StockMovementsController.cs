using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using verii_wms_api_v2.Modules.AccessControl.Application;
using verii_wms_api_v2.Modules.StockMovement.Application;
using verii_wms_api_v2.Modules.StockMovement.Domain;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.StockMovement.Api;

[Authorize, ApiController, Route("api/stock-movements")]
public sealed class StockMovementsController(IStockMovementService service, IPermissionAuthorizationService permissions) : ControllerBase
{
    [HttpPost("paged")]
    public async Task<IActionResult> Paged(PagedRequest request, CancellationToken ct) { await Require("WMS.STOCK_MOVEMENTS.VIEW", ct); return Ok(ApiResponse<PagedResponse<StockMovementGridRow>>.Ok(await service.GetPagedAsync(request, ct))); }
    [HttpGet("{id:long}")]
    public async Task<IActionResult> Detail(long id, CancellationToken ct) { await Require("WMS.STOCK_MOVEMENTS.VIEW", ct); return Ok(ApiResponse<StockMovementDetail>.Ok(await service.GetByIdAsync(id, ct))); }
    [HttpPost]
    public async Task<IActionResult> Post(PostStockMovementRequest request, CancellationToken ct)
    {
        await Require("WMS.STOCK_MOVEMENTS.POST", ct);
        if (string.Equals(request.OperationType, StockMovementTypes.BalanceReconciliation, StringComparison.OrdinalIgnoreCase))
            throw AppException.BadRequest(
                "Kesin bakiye eşitlemesi yalnız depo açılışındaki ön doğrulama ve açık kullanıcı onayı akışından çalıştırılabilir.");
        return Ok(ApiResponse<StockMovementPostResult>.Ok(
            await service.PostAsync(request, ct), "Stok hareketi kaydedildi."));
    }
    [HttpPost("{id:long}/reverse")]
    public async Task<IActionResult> Reverse(long id, ReverseStockMovementRequest request, CancellationToken ct) { await Require("WMS.STOCK_MOVEMENTS.REVERSE", ct); return Ok(ApiResponse<StockMovementPostResult>.Ok(await service.ReverseAsync(id, request, ct), "Ters stok hareketi kaydedildi.")); }
    private async Task Require(string code, CancellationToken ct) { if (!await permissions.HasPermissionAsync(User, code, ct)) throw AppException.Forbidden(); }
}
