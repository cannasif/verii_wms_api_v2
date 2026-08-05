using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using verii_wms_api_v2.Modules.AccessControl.Application;
using verii_wms_api_v2.Modules.Production.Application;
using verii_wms_api_v2.Modules.Production.Domain;
using verii_wms_api_v2.Modules.Identity.Infrastructure;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.Production.Api;

[Authorize,ApiController,Route("api/production")]
public sealed class ProductionController(
    IProductionService service,
    IPermissionAuthorizationService permissions) : ControllerBase
{
    [HttpPost("plans")]
    public async Task<IActionResult> Create(CreateProductionPlanRequest request,CancellationToken ct)
    {
        await Require("WMS.PRODUCTION.CREATE",ct);
        return Ok(ApiResponse<CreateProductionPlanResult>.Ok(
            await service.CreateAsync(request,UserId(),ct),"Üretim planı oluşturuldu."));
    }

    [HttpGet("netsis-work-orders/{workOrderNumber}/prepare")]
    public async Task<IActionResult> PrepareNetsisWorkOrder(string workOrderNumber,CancellationToken ct)
    {
        await Require("WMS.PRODUCTION.VIEW",ct);
        await Require("ERP.NETSIS_READ.VIEW",ct);
        return Ok(ApiResponse<PreparedNetsisProductionWorkOrder>.Ok(
            await service.PrepareNetsisWorkOrderAsync(workOrderNumber,BranchCode(),ct)));
    }

    [HttpGet("work-orders")]
    public async Task<IActionResult>SourceWorkOrders(
        [FromQuery]string? search,[FromQuery]int take=200,CancellationToken ct=default)
    {
        await Require("WMS.PRODUCTION.VIEW",ct);
        return Ok(ApiResponse<IReadOnlyList<ProductionSourceWorkOrderRow>>.Ok(
            await service.GetSourceWorkOrdersAsync(search,BranchCode(),take,ct)));
    }

    [HttpGet("work-orders/{workOrderNumber}/prepare")]
    public async Task<IActionResult>PrepareSourceWorkOrder(
        string workOrderNumber,[FromQuery]ProductionOrderSourceType? sourceType,
        [FromQuery]string? sourceSystemCode,CancellationToken ct)
    {
        await Require("WMS.PRODUCTION.VIEW",ct);
        return Ok(ApiResponse<PreparedNetsisProductionWorkOrder>.Ok(
            await service.PrepareSourceWorkOrderAsync(workOrderNumber,sourceType,sourceSystemCode,BranchCode(),ct)));
    }

    [HttpPost("plans/paged")]
    public async Task<IActionResult> Paged(PagedRequest request,CancellationToken ct)
    {
        await Require("WMS.PRODUCTION.VIEW",ct);
        return Ok(ApiResponse<PagedResponse<ProductionPlanGridRow>>.Ok(
            await service.GetPagedAsync(request,ct)));
    }

    [HttpGet("plans/{id:long}")]
    public async Task<IActionResult> Detail(long id,CancellationToken ct)
    {
        await Require("WMS.PRODUCTION.VIEW",ct);
        return Ok(ApiResponse<ProductionPlanDetail>.Ok(await service.GetDetailAsync(id,ct)));
    }

    [HttpPost("plans/{id:long}/release")]
    public async Task<IActionResult> Release(
        long id,
        ProductionTransitionRequest request,
        CancellationToken ct)
    {
        await Require("WMS.PRODUCTION.RELEASE",ct);
        return Ok(ApiResponse<ProductionPlanDetail>.Ok(
            await service.ReleaseAsync(id,request,UserId(),ct),"Üretim planı serbest bırakıldı."));
    }

    [HttpDelete("plans/{id:long}"),HttpPost("plans/{id:long}/delete")]
    public async Task<IActionResult> Delete(long id,CancellationToken ct)
    {
        await Require("WMS.PRODUCTION.DELETE",ct);
        await service.DeleteDraftAsync(id,UserId(),ct);
        return Ok(ApiResponse<bool>.Ok(true,"Üretim planı silindi."));
    }

    private long UserId()=>long.TryParse(
        User.FindFirstValue(ClaimTypes.NameIdentifier),out var id)
        ?id:throw AppException.Unauthorized("Kullanıcı kimliği bulunamadı.");

    private string BranchCode()=>User.FindFirstValue(JwtTokenIssuer.BranchCodeClaim)?.Trim()
        is {Length:>0} branch?branch:throw AppException.Unauthorized("Oturum şube bilgisi bulunamadı.");

    private async Task Require(string code,CancellationToken ct)
    {
        if(!await permissions.HasPermissionAsync(User,code,ct))
            throw AppException.Forbidden("Bu işlem için yetkiniz bulunmuyor.");
    }
}
