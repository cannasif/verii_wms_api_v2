using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using verii_wms_api_v2.Modules.AccessControl.Application;
using verii_wms_api_v2.Modules.ErpIntegration.Application;
using verii_wms_api_v2.Modules.WarehouseOutbound.Application;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.WarehouseOutbound.Api;

[Authorize, ApiController, Route("api/warehouse-outbounds")]
public sealed class WarehouseOutboundsController(
    IWarehouseOutboundService service,
    IWarehouseOutboundOperationService operations,
    IOperationCancellationCoordinator cancellationCoordinator,
    IPermissionAuthorizationService permissions) : ControllerBase
{
    [HttpPost("drafts")]
    public async Task<IActionResult> Create(CreateWarehouseOutboundDraftRequest request, CancellationToken ct)
    {
        await Require("WMS.WAREHOUSE_OUTBOUND.CREATE", ct);
        return Ok(ApiResponse<CreateWarehouseOutboundDraftResult>.Ok(await service.CreateDraftAsync(request, UserId(), ct), "Sevk taslağı oluşturuldu."));
    }

    [HttpPost("paged")]
    public async Task<IActionResult> Paged(PagedRequest request, CancellationToken ct)
    {
        await Require("WMS.WAREHOUSE_OUTBOUND.VIEW", ct);
        return Ok(ApiResponse<PagedResponse<WarehouseOutboundGridRow>>.Ok(await service.GetPagedAsync(request, ct)));
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Detail(long id, CancellationToken ct)
    {
        await Require("WMS.WAREHOUSE_OUTBOUND.VIEW", ct);
        return Ok(ApiResponse<WarehouseOutboundDetail>.Ok(await service.GetDetailAsync(id, ct)));
    }

    [HttpPut("{id:long}"), HttpPost("{id:long}/update")]
    public async Task<IActionResult> Update(long id, UpdateWarehouseOutboundDraftRequest request, CancellationToken ct)
    {
        await Require("WMS.WAREHOUSE_OUTBOUND.UPDATE", ct);
        return Ok(ApiResponse<WarehouseOutboundDetail>.Ok(await service.UpdateDraftAsync(id, request, UserId(), ct), "Sevk taslağı güncellendi."));
    }

    [HttpDelete("{id:long}"), HttpPost("{id:long}/delete")]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        await Require("WMS.WAREHOUSE_OUTBOUND.DELETE", ct);
        await service.DeleteDraftAsync(id, UserId(), ct);
        return Ok(ApiResponse<bool>.Ok(true, "Sevk taslağı silindi."));
    }

    [HttpPost("{id:long}/approve")]
    public async Task<IActionResult> Approve(long id, WarehouseOutboundTransitionRequest request, CancellationToken ct)
    {
        await Require("WMS.WAREHOUSE_OUTBOUND.APPROVE", ct);
        return Ok(ApiResponse<WarehouseOutboundOperationResult>.Ok(await operations.ApproveAsync(id, request, UserId(), ct), "Sevk onaylandı."));
    }

    [HttpPost("{id:long}/release")]
    public async Task<IActionResult> Release(long id, WarehouseOutboundTransitionRequest request, CancellationToken ct)
    {
        await Require("WMS.WAREHOUSE_OUTBOUND.OPERATE", ct);
        return Ok(ApiResponse<WarehouseOutboundOperationResult>.Ok(await operations.ReleaseAsync(id, request, UserId(), ct), "Sevk serbest bırakıldı."));
    }

    [HttpPost("{id:long}/pick")]
    public async Task<IActionResult> Pick(long id, WarehouseOutboundOperationRequest request, CancellationToken ct)
    {
        await Require("WMS.WAREHOUSE_OUTBOUND.OPERATE", ct);
        return Ok(ApiResponse<WarehouseOutboundOperationResult>.Ok(await operations.PickAsync(id, request, UserId(), ct), "Toplama işlendi."));
    }

    [HttpPost("{id:long}/pack")]
    public async Task<IActionResult> Pack(long id, WarehouseOutboundOperationRequest request, CancellationToken ct)
    {
        await Require("WMS.WAREHOUSE_OUTBOUND.OPERATE", ct);
        return Ok(ApiResponse<WarehouseOutboundOperationResult>.Ok(await operations.PackAsync(id, request, UserId(), ct), "Paketleme işlendi."));
    }

    [HttpPost("{id:long}/load")]
    public async Task<IActionResult> Load(long id, WarehouseOutboundOperationRequest request, CancellationToken ct)
    {
        await Require("WMS.WAREHOUSE_OUTBOUND.OPERATE", ct);
        return Ok(ApiResponse<WarehouseOutboundOperationResult>.Ok(await operations.LoadAsync(id, request, UserId(), ct), "Yükleme işlendi."));
    }

    [HttpPost("{id:long}/ship")]
    public async Task<IActionResult> Ship(long id, WarehouseOutboundOperationRequest request, CancellationToken ct)
    {
        await Require("WMS.WAREHOUSE_OUTBOUND.OPERATE", ct);
        return Ok(ApiResponse<WarehouseOutboundOperationResult>.Ok(await operations.ShipAsync(id, request, UserId(), ct), "Sevk kesinleştirildi."));
    }

    [HttpPost("{id:long}/cancel")]
    public async Task<IActionResult> Cancel(long id, WarehouseOutboundTransitionRequest request, CancellationToken ct)
    {
        await Require("WMS.WAREHOUSE_OUTBOUND.CANCEL", ct);
        return Ok(ApiResponse<OperationCancellationResult>.Ok(
            await cancellationCoordinator.CancelWarehouseOutboundAsync(
                id, request, UserId(), ct),
            "Ambar çıkış güvenli iptal sürecinde işlendi."));
    }

    private long UserId() =>
        long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? id
            : throw AppException.Unauthorized("Kullanıcı kimliği bulunamadı.");

    private async Task Require(string code, CancellationToken ct)
    {
        if (!await permissions.HasPermissionAsync(User, code, ct)) throw AppException.Forbidden();
    }
}

[Authorize, ApiController, Route("api/warehouse-outbound-policy")]
public sealed class WarehouseOutboundPolicyController(IWarehouseOutboundPolicyService service, IPermissionAuthorizationService permissions) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string branchCode = "0", CancellationToken ct = default)
    {
        await Require("WMS.WAREHOUSE_OUTBOUND.SETTINGS.VIEW", ct);
        return Ok(ApiResponse<WarehouseOutboundPolicyDto>.Ok(await service.GetAsync(branchCode, ct)));
    }

    [HttpPut]
    public async Task<IActionResult> Update(UpdateWarehouseOutboundPolicyRequest request, CancellationToken ct)
    {
        await Require("WMS.WAREHOUSE_OUTBOUND.SETTINGS.MANAGE", ct);
        var actor = long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? id
            : throw AppException.Unauthorized("Kullanıcı kimliği bulunamadı.");
        return Ok(ApiResponse<WarehouseOutboundPolicyDto>.Ok(await service.UpdateAsync(request, actor, ct), "Sevk politikası güncellendi."));
    }

    private async Task Require(string code, CancellationToken ct)
    {
        if (!await permissions.HasPermissionAsync(User, code, ct)) throw AppException.Forbidden();
    }
}
