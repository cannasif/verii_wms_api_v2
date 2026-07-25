using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using verii_wms_api_v2.Modules.AccessControl.Application;
using verii_wms_api_v2.Modules.Shipping.Application;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.Shipping.Api;

[Authorize, ApiController, Route("api/shipments")]
public sealed class ShipmentsController(
    IShippingService service,
    IShippingOperationService operations,
    IPermissionAuthorizationService permissions) : ControllerBase
{
    [HttpPost("drafts")]
    public async Task<IActionResult> Create(CreateShipmentDraftRequest request, CancellationToken ct)
    {
        await Require("WMS.SHIPPING.CREATE", ct);
        return Ok(ApiResponse<CreateShipmentDraftResult>.Ok(await service.CreateDraftAsync(request, UserId(), ct), "Sevk taslağı oluşturuldu."));
    }

    [HttpPost("paged")]
    public async Task<IActionResult> Paged(PagedRequest request, CancellationToken ct)
    {
        await Require("WMS.SHIPPING.VIEW", ct);
        return Ok(ApiResponse<PagedResponse<ShipmentGridRow>>.Ok(await service.GetPagedAsync(request, ct)));
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Detail(long id, CancellationToken ct)
    {
        await Require("WMS.SHIPPING.VIEW", ct);
        return Ok(ApiResponse<ShipmentDetail>.Ok(await service.GetDetailAsync(id, ct)));
    }

    [HttpPut("{id:long}"), HttpPost("{id:long}/update")]
    public async Task<IActionResult> Update(long id, UpdateShipmentDraftRequest request, CancellationToken ct)
    {
        await Require("WMS.SHIPPING.UPDATE", ct);
        return Ok(ApiResponse<ShipmentDetail>.Ok(await service.UpdateDraftAsync(id, request, UserId(), ct), "Sevk taslağı güncellendi."));
    }

    [HttpDelete("{id:long}"), HttpPost("{id:long}/delete")]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        await Require("WMS.SHIPPING.DELETE", ct);
        await service.DeleteDraftAsync(id, UserId(), ct);
        return Ok(ApiResponse<bool>.Ok(true, "Sevk taslağı silindi."));
    }

    [HttpPost("{id:long}/approve")]
    public async Task<IActionResult> Approve(long id, ShipmentTransitionRequest request, CancellationToken ct)
    {
        await Require("WMS.SHIPPING.APPROVE", ct);
        return Ok(ApiResponse<ShipmentOperationResult>.Ok(await operations.ApproveAsync(id, request, UserId(), ct), "Sevk onaylandı."));
    }

    [HttpPost("{id:long}/release")]
    public async Task<IActionResult> Release(long id, ShipmentTransitionRequest request, CancellationToken ct)
    {
        await Require("WMS.SHIPPING.OPERATE", ct);
        return Ok(ApiResponse<ShipmentOperationResult>.Ok(await operations.ReleaseAsync(id, request, UserId(), ct), "Sevk serbest bırakıldı."));
    }

    [HttpPost("{id:long}/pick")]
    public async Task<IActionResult> Pick(long id, ShipmentOperationRequest request, CancellationToken ct)
    {
        await Require("WMS.SHIPPING.OPERATE", ct);
        return Ok(ApiResponse<ShipmentOperationResult>.Ok(await operations.PickAsync(id, request, UserId(), ct), "Toplama işlendi."));
    }

    [HttpPost("{id:long}/pack")]
    public async Task<IActionResult> Pack(long id, ShipmentOperationRequest request, CancellationToken ct)
    {
        await Require("WMS.SHIPPING.OPERATE", ct);
        return Ok(ApiResponse<ShipmentOperationResult>.Ok(await operations.PackAsync(id, request, UserId(), ct), "Paketleme işlendi."));
    }

    [HttpPost("{id:long}/load")]
    public async Task<IActionResult> Load(long id, ShipmentOperationRequest request, CancellationToken ct)
    {
        await Require("WMS.SHIPPING.OPERATE", ct);
        return Ok(ApiResponse<ShipmentOperationResult>.Ok(await operations.LoadAsync(id, request, UserId(), ct), "Yükleme işlendi."));
    }

    [HttpPost("{id:long}/ship")]
    public async Task<IActionResult> Ship(long id, ShipmentOperationRequest request, CancellationToken ct)
    {
        await Require("WMS.SHIPPING.OPERATE", ct);
        return Ok(ApiResponse<ShipmentOperationResult>.Ok(await operations.ShipAsync(id, request, UserId(), ct), "Sevk kesinleştirildi."));
    }

    [HttpPost("{id:long}/cancel")]
    public async Task<IActionResult> Cancel(long id, ShipmentTransitionRequest request, CancellationToken ct)
    {
        await Require("WMS.SHIPPING.CANCEL", ct);
        return Ok(ApiResponse<ShipmentOperationResult>.Ok(
            await operations.CancelAsync(id, request, UserId(), ct), "Sevk iptal edildi ve stok hareketleri ters çevrildi."));
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

[Authorize, ApiController, Route("api/shipment-policy")]
public sealed class ShipmentPolicyController(IShipmentPolicyService service, IPermissionAuthorizationService permissions) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string branchCode = "0", CancellationToken ct = default)
    {
        await Require("WMS.SHIPPING.SETTINGS.VIEW", ct);
        return Ok(ApiResponse<ShipmentPolicyDto>.Ok(await service.GetAsync(branchCode, ct)));
    }

    [HttpPut]
    public async Task<IActionResult> Update(UpdateShipmentPolicyRequest request, CancellationToken ct)
    {
        await Require("WMS.SHIPPING.SETTINGS.MANAGE", ct);
        var actor = long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? id
            : throw AppException.Unauthorized("Kullanıcı kimliği bulunamadı.");
        return Ok(ApiResponse<ShipmentPolicyDto>.Ok(await service.UpdateAsync(request, actor, ct), "Sevk politikası güncellendi."));
    }

    private async Task Require(string code, CancellationToken ct)
    {
        if (!await permissions.HasPermissionAsync(User, code, ct)) throw AppException.Forbidden();
    }
}
