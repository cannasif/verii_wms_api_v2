using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using verii_wms_api_v2.Modules.AccessControl.Application;
using verii_wms_api_v2.Modules.StockTracking.Application;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.StockTracking.Api;

[Authorize, ApiController, Route("api/stocks/{stockId:long}/tracking-settings")]
public sealed class StockTrackingSettingsController(
    IStockTrackingPolicyService service,
    IPermissionAuthorizationService permissions) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(
        long stockId,
        [FromQuery] string branchCode,
        CancellationToken ct)
    {
        await Require("WMS.SERIAL_RULES.VIEW", ct);
        var result = await service.GetStockSettingsAsync(branchCode, stockId, ct);
        return Ok(ApiResponse<StockTrackingSettings>.Ok(result));
    }

    [HttpPut]
    public async Task<IActionResult> Update(
        long stockId,
        UpdateStockTrackingSettingsRequest request,
        CancellationToken ct)
    {
        await Require("WMS.SERIAL_RULES.MANAGE", ct);
        var result = await service.UpdateStockSettingsAsync(stockId, request, UserId(), ct);
        return Ok(ApiResponse<StockTrackingSettings>.Ok(result));
    }

    private long UserId() => long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
        ? id
        : throw AppException.Unauthorized("Geçersiz kullanıcı oturumu.");

    private async Task Require(string code, CancellationToken ct)
    {
        if (!await permissions.HasPermissionAsync(User, code, ct))
            throw AppException.Forbidden();
    }
}
