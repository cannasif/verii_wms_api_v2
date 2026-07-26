using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using verii_wms_api_v2.Modules.AccessControl.Application;
using verii_wms_api_v2.Modules.ErpIntegration.Application;
using verii_wms_api_v2.Modules.WarehouseTransfer.Application;
using verii_wms_api_v2.Modules.WarehouseTransfer.Domain;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.WarehouseTransfer.Api;

[Authorize, ApiController, Route("api/warehouse-transfers")]
public sealed class WarehouseTransfersController(
    IWarehouseTransferService service,
    IWarehouseTransferOperationService operations,
    IOperationCancellationCoordinator cancellationCoordinator,
    IPermissionAuthorizationService permissions) : ControllerBase
{
    [HttpPost("drafts")]
    public async Task<IActionResult> CreateDraft(CreateWarehouseTransferDraftRequest request, CancellationToken ct)
    {
        await Require("WMS.WAREHOUSE_TRANSFER.CREATE", ct);
        if (request.BusinessContext != WarehouseTransferBusinessContext.InterWarehouse)
            throw AppException.BadRequest("Uzmanlaşmış transferler kendi modül uçlarından oluşturulmalıdır.");
        return Ok(ApiResponse<CreateWarehouseTransferDraftResult>.Ok(
            await service.CreateDraftAsync(request, CurrentUserId(), ct), "Transfer taslağı oluşturuldu."));
    }

    [HttpPost("paged")]
    public async Task<IActionResult> GetPaged(PagedRequest request, CancellationToken ct)
    {
        await Require("WMS.WAREHOUSE_TRANSFER.VIEW", ct);
        return Ok(ApiResponse<PagedResponse<WarehouseTransferGridRow>>.Ok(await service.GetPagedAsync(request, ct)));
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> GetDetail(long id, CancellationToken ct)
    {
        await Require("WMS.WAREHOUSE_TRANSFER.VIEW", ct);
        return Ok(ApiResponse<WarehouseTransferDetail>.Ok(await service.GetDetailForContextAsync(
            id, [WarehouseTransferBusinessContext.InterWarehouse], ct)));
    }

    [HttpPut("{id:long}"), HttpPost("{id:long}/update")]
    public async Task<IActionResult> UpdateDraft(long id, UpdateWarehouseTransferDraftRequest request, CancellationToken ct)
    {
        await Require("WMS.WAREHOUSE_TRANSFER.UPDATE", ct);
        await EnsureInterWarehouse(id, ct);
        return Ok(ApiResponse<WarehouseTransferDetail>.Ok(
            await service.UpdateDraftAsync(id, request, CurrentUserId(), ct), "Transfer taslağı güncellendi."));
    }

    [HttpDelete("{id:long}"), HttpPost("{id:long}/delete")]
    public async Task<IActionResult> DeleteDraft(long id, CancellationToken ct)
    {
        await Require("WMS.WAREHOUSE_TRANSFER.DELETE", ct);
        await EnsureInterWarehouse(id, ct);
        await service.DeleteDraftAsync(id, CurrentUserId(), ct);
        return Ok(ApiResponse<bool>.Ok(true, "Transfer taslağı silindi."));
    }

    [HttpPost("{id:long}/approve")]
    public async Task<IActionResult> Approve(long id, WarehouseTransferTransitionRequest request, CancellationToken ct)
    {
        await Require("WMS.WAREHOUSE_TRANSFER.APPROVE", ct);
        await EnsureInterWarehouse(id, ct);
        return Ok(ApiResponse<WarehouseTransferOperationResult>.Ok(
            await operations.ApproveAsync(id, request, CurrentUserId(), ct), "Transfer onaylandı."));
    }

    [HttpPost("{id:long}/release")]
    public async Task<IActionResult> Release(long id, WarehouseTransferTransitionRequest request, CancellationToken ct)
    {
        await Require("WMS.WAREHOUSE_TRANSFER.OPERATE", ct);
        await EnsureInterWarehouse(id, ct);
        return Ok(ApiResponse<WarehouseTransferOperationResult>.Ok(
            await operations.ReleaseAsync(id, request, CurrentUserId(), ct), "Transfer serbest bırakıldı."));
    }

    [HttpPost("{id:long}/pick")]
    public async Task<IActionResult> Pick(long id, WarehouseTransferOperationRequest request, CancellationToken ct)
    {
        await Require("WMS.WAREHOUSE_TRANSFER.OPERATE", ct);
        await EnsureInterWarehouse(id, ct);
        return Ok(ApiResponse<WarehouseTransferOperationResult>.Ok(
            await operations.PickAsync(id, request, CurrentUserId(), ct), "Toplama işlendi."));
    }

    [HttpPost("{id:long}/dispatch")]
    public async Task<IActionResult> Dispatch(long id, WarehouseTransferOperationRequest request, CancellationToken ct)
    {
        await Require("WMS.WAREHOUSE_TRANSFER.OPERATE", ct);
        await EnsureInterWarehouse(id, ct);
        return Ok(ApiResponse<WarehouseTransferOperationResult>.Ok(
            await operations.DispatchAsync(id, request, CurrentUserId(), ct), "Transfer sevk edildi."));
    }

    [HttpPost("{id:long}/receive")]
    public async Task<IActionResult> Receive(long id, WarehouseTransferOperationRequest request, CancellationToken ct)
    {
        await Require("WMS.WAREHOUSE_TRANSFER.OPERATE", ct);
        await EnsureInterWarehouse(id, ct);
        return Ok(ApiResponse<WarehouseTransferOperationResult>.Ok(
            await operations.ReceiveAsync(id, request, CurrentUserId(), ct), "Transfer kabul edildi."));
    }

    [HttpPost("{id:long}/putaway")]
    public async Task<IActionResult> Putaway(long id, WarehouseTransferOperationRequest request, CancellationToken ct)
    {
        await Require("WMS.WAREHOUSE_TRANSFER.OPERATE", ct);
        await EnsureInterWarehouse(id, ct);
        return Ok(ApiResponse<WarehouseTransferOperationResult>.Ok(
            await operations.PutawayAsync(id, request, CurrentUserId(), ct), "Transfer rafa yerleştirildi."));
    }

    [HttpPost("{id:long}/cancel")]
    public async Task<IActionResult> Cancel(long id, WarehouseTransferTransitionRequest request, CancellationToken ct)
    {
        await Require("WMS.WAREHOUSE_TRANSFER.CANCEL", ct);
        await EnsureInterWarehouse(id, ct);
        return Ok(ApiResponse<OperationCancellationResult>.Ok(
            await cancellationCoordinator.CancelWarehouseTransferAsync(
                id, request, CurrentUserId(), ct),
            "Transfer güvenli iptal sürecinde işlendi."));
    }

    private long CurrentUserId() =>
        long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? id
            : throw AppException.Unauthorized("Kullanıcı kimliği bulunamadı.");

    private async Task Require(string code, CancellationToken ct)
    {
        if (!await permissions.HasPermissionAsync(User, code, ct))
            throw AppException.Forbidden("Bu işlem için yetkiniz bulunmuyor.");
    }

    private Task EnsureInterWarehouse(long id, CancellationToken ct) =>
        service.EnsureContextAsync(id, [WarehouseTransferBusinessContext.InterWarehouse], ct);
}
