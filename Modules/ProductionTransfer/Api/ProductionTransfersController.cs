using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using verii_wms_api_v2.Modules.AccessControl.Application;
using verii_wms_api_v2.Modules.ProductionTransfer.Application;
using verii_wms_api_v2.Modules.WarehouseTransfer.Application;
using verii_wms_api_v2.Modules.WarehouseTransfer.Domain;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.ProductionTransfer.Api;

[Authorize,ApiController,Route("api/production-transfers")]
public sealed class ProductionTransfersController(
    IProductionTransferService service,
    IWarehouseTransferService transfers,
    IWarehouseTransferOperationService operations,
    IPermissionAuthorizationService permissions) : ControllerBase
{
    private static readonly WarehouseTransferBusinessContext[] Contexts=[
        WarehouseTransferBusinessContext.ProductionMaterialSupply,
        WarehouseTransferBusinessContext.ProductionWipMove,
        WarehouseTransferBusinessContext.ProductionOutputMove];

    [HttpPost("drafts")]
    public async Task<IActionResult>Create(CreateProductionTransferDraftRequest request,CancellationToken ct){
        await Require("WMS.PRODUCTION_TRANSFER.CREATE",ct);
        return Ok(ApiResponse<CreateWarehouseTransferDraftResult>.Ok(await service.CreateDraftAsync(request,UserId(),ct),"Üretim transfer taslağı oluşturuldu."));}
    [HttpPost("paged")]
    public async Task<IActionResult>Paged(PagedRequest request,CancellationToken ct){
        await Require("WMS.PRODUCTION_TRANSFER.VIEW",ct);return Ok(ApiResponse<PagedResponse<WarehouseTransferGridRow>>.Ok(await service.GetPagedAsync(request,ct)));}
    [HttpGet("{id:long}")]
    public async Task<IActionResult>Detail(long id,CancellationToken ct){
        await Require("WMS.PRODUCTION_TRANSFER.VIEW",ct);return Ok(ApiResponse<ProductionTransferDetail>.Ok(await service.GetDetailAsync(id,ct)));}
    [HttpPut("{id:long}"),HttpPost("{id:long}/update")]
    public async Task<IActionResult>Update(long id,UpdateWarehouseTransferDraftRequest request,CancellationToken ct){
        await Require("WMS.PRODUCTION_TRANSFER.UPDATE",ct);
        return Ok(ApiResponse<ProductionTransferDetail>.Ok(await service.UpdateDraftAsync(id,request,UserId(),ct),"Üretim transfer taslağı güncellendi."));}
    [HttpDelete("{id:long}"),HttpPost("{id:long}/delete")]
    public async Task<IActionResult>Delete(long id,CancellationToken ct){
        await Require("WMS.PRODUCTION_TRANSFER.DELETE",ct);await service.DeleteDraftAsync(id,UserId(),ct);
        return Ok(ApiResponse<bool>.Ok(true,"Üretim transfer taslağı silindi."));}
    [HttpPost("{id:long}/approve")]
    public async Task<IActionResult>Approve(long id,WarehouseTransferTransitionRequest request,CancellationToken ct){
        await Require("WMS.PRODUCTION_TRANSFER.APPROVE",ct);await Ensure(id,ct);
        return Ok(ApiResponse<WarehouseTransferOperationResult>.Ok(await operations.ApproveAsync(id,request,UserId(),ct)));}
    [HttpPost("{id:long}/release")]
    public async Task<IActionResult>Release(long id,WarehouseTransferTransitionRequest request,CancellationToken ct){
        await Require("WMS.PRODUCTION_TRANSFER.OPERATE",ct);await Ensure(id,ct);
        return Ok(ApiResponse<WarehouseTransferOperationResult>.Ok(await operations.ReleaseAsync(id,request,UserId(),ct)));}
    [HttpPost("{id:long}/{action:regex(^(pick|dispatch|receive|putaway)$)}")]
    public async Task<IActionResult>Operate(long id,string action,WarehouseTransferOperationRequest request,CancellationToken ct){
        await Require("WMS.PRODUCTION_TRANSFER.OPERATE",ct);await Ensure(id,ct);
        var result=action switch{
            "pick"=>await operations.PickAsync(id,request,UserId(),ct),
            "dispatch"=>await operations.DispatchAsync(id,request,UserId(),ct),
            "receive"=>await operations.ReceiveAsync(id,request,UserId(),ct),
            "putaway"=>await operations.PutawayAsync(id,request,UserId(),ct),
            _=>throw AppException.BadRequest("Geçersiz üretim transfer operasyonu.")};
        return Ok(ApiResponse<WarehouseTransferOperationResult>.Ok(result));}
    [HttpPost("{id:long}/cancel")]
    public async Task<IActionResult>Cancel(long id,WarehouseTransferTransitionRequest request,CancellationToken ct){
        await Require("WMS.PRODUCTION_TRANSFER.CANCEL",ct);await Ensure(id,ct);
        return Ok(ApiResponse<WarehouseTransferOperationResult>.Ok(await operations.CancelAsync(id,request,UserId(),ct)));}
    [HttpGet("policy")]
    public async Task<IActionResult>Policy([FromQuery]string branchCode,CancellationToken ct){
        await Require("WMS.PRODUCTION_TRANSFER.SETTINGS.VIEW",ct);return Ok(ApiResponse<ProductionTransferPolicyDto>.Ok(await service.GetPolicyAsync(branchCode,ct)));}
    [HttpPut("policy")]
    public async Task<IActionResult>UpdatePolicy(UpdateProductionTransferPolicyRequest request,CancellationToken ct){
        await Require("WMS.PRODUCTION_TRANSFER.SETTINGS.MANAGE",ct);
        return Ok(ApiResponse<ProductionTransferPolicyDto>.Ok(await service.UpdatePolicyAsync(request,UserId(),ct),"Üretim transfer politikası kaydedildi."));}

    private Task Ensure(long id,CancellationToken ct)=>transfers.EnsureContextAsync(id,Contexts,ct);
    private long UserId()=>long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier),out var id)?id:throw AppException.Unauthorized("Kullanıcı kimliği bulunamadı.");
    private async Task Require(string code,CancellationToken ct){if(!await permissions.HasPermissionAsync(User,code,ct))throw AppException.Forbidden("Bu işlem için yetkiniz bulunmuyor.");}
}
