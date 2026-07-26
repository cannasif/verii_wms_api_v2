using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using verii_wms_api_v2.Modules.AccessControl.Application;
using verii_wms_api_v2.Modules.IncomingInvoice.Application;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.IncomingInvoice.Api;

[Authorize, ApiController, Route("api/incoming-invoices/connections")]
public sealed class ELogoConnectionsController(
    IELogoConnectionService service,
    IPermissionAuthorizationService permissions) : ControllerBase
{
    [HttpGet("selectable")]
    public async Task<IActionResult> GetSelectable(
        [FromQuery] string branchCode, CancellationToken ct)
    {
        await Require("WMS.INCOMING_INVOICE.VIEW", ct);
        return Ok(ApiResponse<IReadOnlyList<ELogoConnectionRow>>.Ok(
            await service.GetSelectableAsync(branchCode, ct)));
    }

    [HttpPost("paged")]
    public async Task<IActionResult> GetPaged(
        [FromQuery] string branchCode, PagedRequest request, CancellationToken ct)
    {
        await Require("WMS.INCOMING_INVOICE.CONNECTIONS.MANAGE", ct);
        return Ok(ApiResponse<PagedResponse<ELogoConnectionRow>>.Ok(
            await service.GetPagedAsync(branchCode, request, ct)));
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Get(
        long id, [FromQuery] string branchCode, CancellationToken ct)
    {
        await Require("WMS.INCOMING_INVOICE.CONNECTIONS.MANAGE", ct);
        return Ok(ApiResponse<ELogoConnectionRow>.Ok(
            await service.GetAsync(id, branchCode, ct)));
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        SaveELogoConnectionRequest request, CancellationToken ct)
    {
        await Require("WMS.INCOMING_INVOICE.CONNECTIONS.MANAGE", ct);
        return Ok(ApiResponse<ELogoConnectionRow>.Ok(
            await service.CreateAsync(request, ct), "eLogo bağlantısı oluşturuldu."));
    }

    [HttpPut("{id:long}")]
    [HttpPost("{id:long}/update")]
    public async Task<IActionResult> Update(
        long id, SaveELogoConnectionRequest request, CancellationToken ct)
    {
        await Require("WMS.INCOMING_INVOICE.CONNECTIONS.MANAGE", ct);
        return Ok(ApiResponse<ELogoConnectionRow>.Ok(
            await service.UpdateAsync(id, request, ct), "eLogo bağlantısı güncellendi."));
    }

    [HttpDelete("{id:long}")]
    [HttpPost("{id:long}/delete")]
    public async Task<IActionResult> Delete(
        long id, [FromQuery] string branchCode, CancellationToken ct)
    {
        await Require("WMS.INCOMING_INVOICE.CONNECTIONS.MANAGE", ct);
        await service.DeleteAsync(id, branchCode, ct);
        return Ok(ApiResponse<bool>.Ok(true, "eLogo bağlantısı silindi."));
    }

    private async Task Require(string code, CancellationToken ct)
    {
        if (!await permissions.HasPermissionAsync(User, code, ct))
            throw AppException.Forbidden();
    }
}
