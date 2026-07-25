using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using verii_wms_api_v2.Modules.AccessControl.Application;
using verii_wms_api_v2.Modules.StockTracking.Application;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.StockTracking.Api;

[Authorize, ApiController, Route("api/stock-tracking-policies")]
public sealed class StockTrackingPoliciesController(
    IStockTrackingPolicyService service,
    IStockTrackingPolicyResolver resolver,
    IPermissionAuthorizationService permissions) : ControllerBase
{
    [HttpPost("paged")]
    public async Task<IActionResult> Paged(PagedRequest request, CancellationToken ct)
    {
        await Require("WMS.SERIAL_RULES.VIEW", ct);
        return Ok(ApiResponse<PagedResponse<StockTrackingPolicyRow>>.Ok(await service.GetPagedAsync(request, ct)));
    }

    [HttpGet("resolve")]
    public async Task<IActionResult> Resolve([FromQuery] string branchCode, [FromQuery] long stockId, CancellationToken ct)
    {
        // Etkin politika operasyon taslaklarının zorunlu girdisidir. Yönetim ekranı
        // yetkisi olmayan operasyon kullanıcıları da yalnızca çözülmüş sonucu okuyabilir.
        return Ok(ApiResponse<EffectiveStockTrackingPolicy>.Ok(await resolver.ResolveAsync(branchCode, stockId, ct)));
    }

    [HttpPost]
    public async Task<IActionResult> Create(StockTrackingPolicyUpsertRequest request, CancellationToken ct)
    {
        await Require("WMS.SERIAL_RULES.MANAGE", ct);
        return Ok(ApiResponse<object>.Ok(new { id = await service.CreateAsync(request, UserId(), ct) }));
    }

    [HttpPost("{id:long}/versions")]
    public async Task<IActionResult> Version(long id, StockTrackingPolicyUpsertRequest request, [FromQuery] string? concurrencyToken, CancellationToken ct)
    {
        await Require("WMS.SERIAL_RULES.MANAGE", ct);
        return Ok(ApiResponse<object>.Ok(new
        {
            id = await service.CreateNextVersionAsync(id, request, UserId(), concurrencyToken, ct)
        }));
    }

    [HttpPost("{id:long}/delete")]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        await Require("WMS.SERIAL_RULES.MANAGE", ct);
        await service.DeleteAsync(id, UserId(), ct);
        return Ok(ApiResponse<bool>.Ok(true));
    }

    private long UserId() => long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
        ? id
        : throw AppException.Unauthorized("Geçersiz kullanıcı oturumu.");

    private async Task Require(string code, CancellationToken ct)
    {
        if (!await permissions.HasPermissionAsync(User, code, ct)) throw AppException.Forbidden();
    }
}
