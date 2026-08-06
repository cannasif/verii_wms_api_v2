using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using verii_wms_api_v2.Modules.AccessControl.Application;
using verii_wms_api_v2.Modules.ErpIntegration.Application;
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
    IProductionTransferTaskService tasks,
    IProductionTransferExecutionService execution,
    IOperationCancellationCoordinator cancellationCoordinator,
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
    [HttpPost("{id:long}/{operation:regex(^(pick|dispatch|receive|putaway)$)}")]
    public async Task<IActionResult>Operate(long id,string operation,WarehouseTransferOperationRequest request,CancellationToken ct){
        await Require("WMS.PRODUCTION_TRANSFER.OPERATE",ct);await Ensure(id,ct);
        var result=operation switch{
            "pick"=>await operations.PickAsync(id,request,UserId(),ct),
            "dispatch"=>await operations.DispatchAsync(id,request,UserId(),ct),
            "receive"=>await operations.ReceiveAsync(id,request,UserId(),ct),
            "putaway"=>await operations.PutawayAsync(id,request,UserId(),ct),
            _=>throw AppException.BadRequest("Geçersiz üretim transfer operasyonu.")};
        return Ok(ApiResponse<WarehouseTransferOperationResult>.Ok(result));}
    [HttpPost("{id:long}/cancel")]
    public async Task<IActionResult>Cancel(long id,WarehouseTransferTransitionRequest request,CancellationToken ct){
        await Require("WMS.PRODUCTION_TRANSFER.CANCEL",ct);await Ensure(id,ct);
        return Ok(ApiResponse<OperationCancellationResult>.Ok(await cancellationCoordinator.CancelWarehouseTransferAsync(id,request,UserId(),ct)));}
    [HttpGet("{id:long}/tasks")]
    public async Task<IActionResult>Tasks(long id,CancellationToken ct){
        await Require("WMS.PRODUCTION_TRANSFER.VIEW",ct);await Ensure(id,ct);
        return Ok(ApiResponse<ProductionTransferTaskBoardDto>.Ok(await tasks.GetBoardAsync(id,ct)));}
    [HttpGet("{id:long}/execution")]
    public async Task<IActionResult>Execution(long id,CancellationToken ct){
        await Require("WMS.PRODUCTION_TRANSFER.VIEW",ct);await Ensure(id,ct);
        return Ok(ApiResponse<ProductionTransferExecutionDto>.Ok(await execution.GetAsync(id,ct)));}
    [HttpPost("{id:long}/scan-pick")]
    public async Task<IActionResult>ScanPick(long id,ProductionTransferScanPickRequest request,CancellationToken ct){
        await Require("WMS.PRODUCTION_TRANSFER.OPERATE",ct);await Ensure(id,ct);
        return Ok(ApiResponse<ProductionTransferScanPickResult>.Ok(await execution.ScanPickAsync(id,request,UserId(),ct),"Barkod doğrulandı ve stok bekleme rafına alındı."));}
    [HttpPost("{id:long}/complete-picking")]
    public async Task<IActionResult>CompletePicking(long id,CompleteProductionPickingRequest request,CancellationToken ct){
        await Require("WMS.PRODUCTION_TRANSFER.OPERATE",ct);await Ensure(id,ct);
        return Ok(ApiResponse<ProductionTransferExecutionDto>.Ok(await execution.CompletePickingAsync(id,request,UserId(),ct),"Toplama tamamlandı; transfer teslim onayı bekliyor."));}
    [HttpPost("{id:long}/confirm-handover")]
    public async Task<IActionResult>ConfirmHandover(long id,ConfirmProductionHandoverRequest request,CancellationToken ct){
        await Require("WMS.PRODUCTION_TRANSFER.OPERATE",ct);await Ensure(id,ct);
        var canOverrideRequester=await permissions.HasPermissionAsync(User,"WMS.PRODUCTION_TRANSFER.APPROVE",ct);
        return Ok(ApiResponse<ProductionTransferExecutionDto>.Ok(await execution.ConfirmHandoverAsync(id,request,UserId(),canOverrideRequester,ct),"Üretim transferi teslim onayı tamamlandı."));}
    [HttpGet("task-pool")]
    public async Task<IActionResult>TaskPool(CancellationToken ct){
        await Require("WMS.PRODUCTION_TRANSFER.ASSIGN",ct);
        return Ok(ApiResponse<IReadOnlyList<ProductionTransferTaskPoolRow>>.Ok(await tasks.GetPoolAsync(UserId(),ct)));}
    [HttpPost("{id:long}/tasks/{taskId:long}/assign")]
    public async Task<IActionResult>Assign(long id,long taskId,AssignProductionTransferTaskRequest request,CancellationToken ct){
        await Require("WMS.PRODUCTION_TRANSFER.ASSIGN",ct);await Ensure(id,ct);
        return Ok(ApiResponse<ProductionTransferTaskBoardDto>.Ok(await tasks.AssignAsync(id,taskId,request,UserId(),ct),"Görev atandı."));}
    [HttpDelete("{id:long}/tasks/{taskId:long}/assignments/{userId:long}"),HttpPost("{id:long}/tasks/{taskId:long}/assignments/{userId:long}/remove")]
    public async Task<IActionResult>RemoveAssignment(long id,long taskId,long userId,CancellationToken ct){
        await Require("WMS.PRODUCTION_TRANSFER.ASSIGN",ct);await Ensure(id,ct);
        return Ok(ApiResponse<ProductionTransferTaskBoardDto>.Ok(await tasks.RemoveAssignmentAsync(id,taskId,userId,UserId(),ct),"Görev ataması kaldırıldı."));}
    [HttpPost("{id:long}/tasks/{taskId:long}/assignments/{userId:long}/request-return")]
    public async Task<IActionResult>RequestAssignmentReturn(long id,long taskId,long userId,CancellationToken ct){
        await Require("WMS.PRODUCTION_TRANSFER.ASSIGN",ct);await Ensure(id,ct);
        return Ok(ApiResponse<ProductionTransferTaskBoardDto>.Ok(await tasks.RequestAssignmentReturnAsync(id,taskId,userId,UserId(),ct),"İade görevi oluşturuldu."));}
    [HttpPost("{id:long}/tasks/{taskId:long}/complete-assignment-return")]
    public async Task<IActionResult>CompleteAssignmentReturn(long id,long taskId,StartProductionTransferTaskRequest request,CancellationToken ct){
        await Require("WMS.PRODUCTION_TRANSFER.OPERATE",ct);await Ensure(id,ct);
        return Ok(ApiResponse<ProductionTransferTaskBoardDto>.Ok(await tasks.CompleteAssignmentReturnAsync(id,taskId,request.IdempotencyKey,UserId(),ct),"İade tamamlandı, atama kaldırıldı."));}
    [HttpPost("{id:long}/tasks/{taskId:long}/handoff")]
    public async Task<IActionResult>Handoff(long id,long taskId,HandoffProductionTransferTaskRequest request,CancellationToken ct){
        await Require("WMS.PRODUCTION_TRANSFER.ASSIGN",ct);await Ensure(id,ct);
        return Ok(ApiResponse<ProductionTransferTaskBoardDto>.Ok(await tasks.HandoffAsync(id,taskId,request,UserId(),ct),"Görevin kalan miktarı devredildi."));}
    [HttpPost("{id:long}/tasks/{taskId:long}/refresh-route")]
    public async Task<IActionResult>RefreshRoute(long id,long taskId,CancellationToken ct){
        await Require("WMS.PRODUCTION_TRANSFER.OPERATE",ct);await Ensure(id,ct);
        return Ok(ApiResponse<ProductionTransferTaskBoardDto>.Ok(await tasks.RefreshRouteAsync(id,taskId,UserId(),ct),"Toplanmamış kalemlerin rotası güncel stok bakiyesine göre yenilendi."));}
    [HttpGet("{id:long}/tasks/{taskId:long}/start-check")]
    public async Task<IActionResult>CheckTaskStart(long id,long taskId,CancellationToken ct){
        await Require("WMS.PRODUCTION_TRANSFER.OPERATE",ct);await Ensure(id,ct);
        return Ok(ApiResponse<ProductionTaskStartCheckDto>.Ok(await tasks.CheckStartAsync(id,taskId,UserId(),ct)));}
    [HttpPost("{id:long}/tasks/{taskId:long}/start")]
    public async Task<IActionResult>StartTask(long id,long taskId,[FromBody]StartProductionTransferTaskRequest? request,CancellationToken ct){
        await Require("WMS.PRODUCTION_TRANSFER.OPERATE",ct);await Ensure(id,ct);
        return Ok(ApiResponse<ProductionTransferTaskBoardDto>.Ok(await tasks.AcceptAndStartAsync(id,taskId,UserId(),request?.AllowPartialStart??false,ct),"Görev başlatıldı."));}
    [HttpGet("{id:long}/lines/{lineId:long}/picked-sources")]
    public async Task<IActionResult>LinePickedSources(long id,long lineId,CancellationToken ct){
        await Require("WMS.PRODUCTION_TRANSFER.VIEW",ct);await Ensure(id,ct);
        return Ok(ApiResponse<IReadOnlyList<WarehouseTransferPickedSourceLocationDto>>.Ok(await tasks.GetLinePickedSourcesAsync(id,lineId,ct)));}
    [HttpPost("{id:long}/tasks/{taskId:long}/complete-cancellation-return")]
    public async Task<IActionResult>CompleteCancellationReturn(long id,long taskId,StartProductionTransferTaskRequest request,CancellationToken ct){
        await Require("WMS.PRODUCTION_TRANSFER.OPERATE",ct);await Ensure(id,ct);
        return Ok(ApiResponse<ProductionTransferTaskBoardDto>.Ok(await tasks.CompleteCancellationReturnAsync(id,taskId,request.IdempotencyKey,UserId(),ct),"İptal iadesi tamamlandı."));}
    [HttpGet("warehouse-return-setting")]
    public async Task<IActionResult>ReturnSetting([FromQuery]long warehouseId,CancellationToken ct){
        await Require("WMS.PRODUCTION_TRANSFER.SETTINGS.VIEW",ct);
        return Ok(ApiResponse<WarehouseTransferReturnSettingDto>.Ok(await tasks.GetReturnSettingAsync(warehouseId,ct)));}
    [HttpPut("warehouse-return-setting")]
    public async Task<IActionResult>UpdateReturnSetting(UpdateWarehouseTransferReturnSettingRequest request,CancellationToken ct){
        await Require("WMS.PRODUCTION_TRANSFER.SETTINGS.MANAGE",ct);
        return Ok(ApiResponse<WarehouseTransferReturnSettingDto>.Ok(await tasks.UpdateReturnSettingAsync(request,UserId(),ct),"Depo üretim transfer ve iade rafı ayarları kaydedildi."));}
    [HttpGet("warehouses/{warehouseId:long}/default-target-location")]
    public async Task<IActionResult>DefaultTargetLocation(long warehouseId,[FromQuery]string branchCode,CancellationToken ct){
        await Require("WMS.PRODUCTION_TRANSFER.CREATE",ct);
        return Ok(ApiResponse<DefaultProductionTargetLocationDto>.Ok(await service.GetDefaultTargetLocationAsync(warehouseId,branchCode,ct)));}
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
