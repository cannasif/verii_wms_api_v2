using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using verii_wms_api_v2.Modules.AccessControl.Application;
using verii_wms_api_v2.Modules.Identity.Infrastructure;
using verii_wms_api_v2.Modules.WarehouseAssistant.Application;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.WarehouseAssistant.Api;

[Authorize, ApiController, Route("api/warehouse-assistant"), EnableRateLimiting("warehouse-assistant")]
public sealed class WarehouseAssistantController(
    IWarehouseAssistantService service,
    IPermissionAuthorizationService permissions) : ControllerBase
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

    private async Task<WarehouseAssistantAccess> ResolveAccessAsync(CancellationToken ct) => new(
        await permissions.HasPermissionAsync(User, WarehouseAssistantPermissions.QueryAllUsers, ct),
        await permissions.HasPermissionAsync(User, "WMS.STOCK_BALANCES.VIEW", ct),
        await permissions.HasPermissionAsync(User, "WMS.STOCK_MOVEMENTS.VIEW", ct),
        await permissions.HasPermissionAsync(User, "WMS.GOODS_RECEIPT.VIEW", ct));

    private long CurrentUserId() =>
        long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) && id > 0
            ? id
            : throw AppException.Unauthorized("Geçersiz kullanıcı oturumu.");

    private string BranchCode()
    {
        var branchCode = User.FindFirstValue(JwtTokenIssuer.BranchCodeClaim)?.Trim();
        if (string.IsNullOrWhiteSpace(branchCode)) throw AppException.Unauthorized("Şube kapsamı bulunamadı.");
        return branchCode;
    }
}
