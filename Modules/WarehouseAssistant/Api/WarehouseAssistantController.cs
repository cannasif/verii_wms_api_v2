using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Localization;
using verii_wms_api_v2.Modules.AccessControl.Application;
using verii_wms_api_v2.Modules.Identity.Infrastructure;
using verii_wms_api_v2.Modules.WarehouseAssistant.Application;
using verii_wms_api_v2.Modules.WarehouseAssistant.Localization;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.WarehouseAssistant.Api;

[Authorize, ApiController, Route("api/warehouse-assistant"), EnableRateLimiting("warehouse-assistant")]
public sealed class WarehouseAssistantController(
    IWarehouseAssistantService service,
    IPermissionAuthorizationService permissions,
    IStringLocalizer<WarehouseAssistantResource> localizer) : ControllerBase
{
    [HttpGet("capabilities")]
    public async Task<IActionResult> Capabilities(CancellationToken ct) =>
        Ok(ApiResponse<WarehouseAssistantCapabilities>.Ok(
            await service.GetCapabilitiesAsync(await ResolveAccessAsync(ct), ct)));

    [HttpGet("conversations")]
    public async Task<IActionResult> Conversations(CancellationToken ct) =>
        Ok(ApiResponse<IReadOnlyList<WarehouseAssistantConversationRow>>.Ok(
            await service.GetConversationsAsync(CurrentUserId(), BranchCode(), ct)));

    [HttpGet("conversations/{conversationId:long}/messages")]
    public async Task<IActionResult> Messages(long conversationId, CancellationToken ct) =>
        Ok(ApiResponse<IReadOnlyList<WarehouseAssistantMessageRow>>.Ok(
            await service.GetMessagesAsync(conversationId, CurrentUserId(), BranchCode(), ct)));

    [HttpPost("chat")]
    public async Task<IActionResult> Chat(AskWarehouseAssistantRequest request, CancellationToken ct) =>
        Ok(ApiResponse<WarehouseAssistantChatResponse>.Ok(
            await service.AskAsync(request, CurrentUserId(), BranchCode(), await ResolveAccessAsync(ct), ct)));

    [HttpPost("conversations/{conversationId:long}/archive")]
    public async Task<IActionResult> ArchiveConversation(long conversationId, CancellationToken ct)
    {
        await service.ArchiveConversationAsync(conversationId, CurrentUserId(), BranchCode(), ct);
        return Ok(ApiResponse<object>.Ok(new { conversationId }));
    }

    private async Task<WarehouseAssistantAccess> ResolveAccessAsync(CancellationToken ct) => new(
        await permissions.HasPermissionAsync(User, WarehouseAssistantPermissions.QueryAllUsers, ct),
        await permissions.HasPermissionAsync(User, "WMS.STOCK_BALANCES.VIEW", ct),
        await permissions.HasPermissionAsync(User, "WMS.STOCK_MOVEMENTS.VIEW", ct),
        await permissions.HasPermissionAsync(User, "WMS.GOODS_RECEIPT.VIEW", ct),
        await permissions.HasPermissionAsync(User, "WMS.WAREHOUSE_TRANSFER.VIEW", ct),
        await permissions.HasPermissionAsync(User, "WMS.SHIPPING.VIEW", ct),
        await permissions.HasPermissionAsync(User, "WMS.WAREHOUSE_INBOUND.VIEW", ct),
        await permissions.HasPermissionAsync(User, "WMS.WAREHOUSE_OUTBOUND.VIEW", ct),
        await permissions.HasPermissionAsync(User, "WMS.PRODUCTION_TRANSFER.VIEW", ct),
        await permissions.HasPermissionAsync(User, "WMS.STEEL_RECEIPT.VEHICLE.VIEW", ct));

    private long CurrentUserId() =>
        long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) && id > 0
            ? id
            : throw AppException.Unauthorized(localizer[WarehouseAssistantMessageKeys.InvalidSession].Value);

    private string BranchCode()
    {
        var branchCode = User.FindFirstValue(JwtTokenIssuer.BranchCodeClaim)?.Trim();
        if (string.IsNullOrWhiteSpace(branchCode)) throw AppException.Unauthorized(localizer[WarehouseAssistantMessageKeys.BranchScopeMissing].Value);
        return branchCode;
    }
}
