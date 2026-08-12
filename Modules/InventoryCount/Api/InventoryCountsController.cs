using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using verii_wms_api_v2.Modules.AccessControl.Application;
using verii_wms_api_v2.Modules.InventoryCount.Application;
using verii_wms_api_v2.Modules.InventoryCount.Localization;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.InventoryCount.Api;

[Authorize, ApiController, Route("api/inventory-counts")]
public sealed class InventoryCountsController(
    IInventoryCountService service,
    IPermissionAuthorizationService permissions,
    IStringLocalizer<InventoryCountResource> localizer) : ControllerBase
{
    [HttpPost("paged")]
    public async Task<IActionResult> GetPaged(PagedRequest request, CancellationToken ct)
    {
        await Require("WMS.INVENTORY_COUNT.VIEW", ct);
        return Ok(ApiResponse<PagedResponse<InventoryCountGridRow>>.Ok(await service.GetPagedAsync(request, ct)));
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> GetDetail(long id, CancellationToken ct)
    {
        await Require("WMS.INVENTORY_COUNT.VIEW", ct);
        var reveal = await permissions.HasPermissionAsync(User, "WMS.INVENTORY_COUNT.REVIEW", ct);
        return Ok(ApiResponse<InventoryCountDetail>.Ok(await service.GetDetailAsync(id, reveal, ct)));
    }

    [HttpPost("drafts")]
    public async Task<IActionResult> CreateDraft(CreateInventoryCountDraftRequest request, CancellationToken ct)
    {
        await Require("WMS.INVENTORY_COUNT.CREATE", ct);
        var id = await service.CreateDraftAsync(request, UserId(), ct);
        return Ok(ApiResponse<long>.Ok(id, localizer[InventoryCountMessageKeys.DraftCreated]));
    }

    [HttpPut("{id:long}"), HttpPost("{id:long}/update")]
    public async Task<IActionResult> UpdateDraft(long id, UpdateInventoryCountDraftRequest request, CancellationToken ct)
    {
        await Require("WMS.INVENTORY_COUNT.UPDATE", ct);
        await service.UpdateDraftAsync(id, request, UserId(), ct);
        return Ok(ApiResponse<bool>.Ok(true, localizer[InventoryCountMessageKeys.DraftUpdated]));
    }

    [HttpDelete("{id:long}"), HttpPost("{id:long}/delete")]
    public async Task<IActionResult> DeleteDraft(long id, CancellationToken ct)
    {
        await Require("WMS.INVENTORY_COUNT.UPDATE", ct);
        await service.DeleteDraftAsync(id, UserId(), ct);
        return Ok(ApiResponse<bool>.Ok(true, localizer[InventoryCountMessageKeys.DraftDeleted]));
    }

    [HttpGet("{id:long}/preview")]
    public async Task<IActionResult> Preview(long id, CancellationToken ct)
    {
        await Require("WMS.INVENTORY_COUNT.CREATE", ct);
        return Ok(ApiResponse<InventoryCountPreviewResult>.Ok(await service.PreviewAsync(id, ct)));
    }

    [HttpPost("{id:long}/release")]
    public async Task<IActionResult> Release(long id, ReleaseInventoryCountRequest request, CancellationToken ct)
    {
        await Require("WMS.INVENTORY_COUNT.RELEASE", ct);
        return Ok(ApiResponse<ReleaseInventoryCountResult>.Ok(
            await service.ReleaseAsync(id, request, UserId(), ct), localizer[InventoryCountMessageKeys.Released]));
    }

    [HttpGet("policy")]
    public async Task<IActionResult> GetPolicy([FromQuery] string branchCode, [FromQuery] long? warehouseId, CancellationToken ct)
    {
        await Require("WMS.INVENTORY_COUNT.POLICY.VIEW", ct);
        return Ok(ApiResponse<InventoryCountPolicyResponse>.Ok(await service.GetPolicyAsync(branchCode, warehouseId, ct)));
    }

    [HttpPut("policy"), HttpPost("policy")]
    public async Task<IActionResult> UpsertPolicy(UpsertInventoryCountPolicyRequest request, CancellationToken ct)
    {
        await Require("WMS.INVENTORY_COUNT.POLICY.MANAGE", ct);
        return Ok(ApiResponse<InventoryCountPolicyResponse>.Ok(
            await service.UpsertPolicyAsync(request, UserId(), ct), localizer[InventoryCountMessageKeys.PolicySaved]));
    }

    private long UserId() => long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
        ? id
        : throw AppException.Unauthorized(localizer[InventoryCountMessageKeys.PermissionDenied]);

    private async Task Require(string code, CancellationToken ct)
    {
        if (!await permissions.HasPermissionAsync(User, code, ct))
            throw AppException.Forbidden(localizer[InventoryCountMessageKeys.PermissionDenied]);
    }
}
