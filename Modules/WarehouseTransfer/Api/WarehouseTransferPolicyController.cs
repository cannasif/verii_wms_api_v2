using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using verii_wms_api_v2.Modules.AccessControl.Application;
using verii_wms_api_v2.Modules.WarehouseTransfer.Application;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.WarehouseTransfer.Api;

[Authorize,ApiController,Route("api/warehouse-transfer-policy")]
public sealed class WarehouseTransferPolicyController(IWarehouseTransferPolicyService service,IPermissionAuthorizationService permissions):ControllerBase
{
    [HttpGet]
    public async Task<IActionResult>Get([FromQuery]string branchCode,CancellationToken ct)
    {
        await Require("WMS.WAREHOUSE_TRANSFER.SETTINGS.VIEW",ct);
        return Ok(ApiResponse<WarehouseTransferPolicyDto>.Ok(await service.GetAsync(branchCode,ct)));
    }
    [HttpPut]
    public async Task<IActionResult>Update(UpdateWarehouseTransferPolicyRequest request,CancellationToken ct)
    {
        await Require("WMS.WAREHOUSE_TRANSFER.SETTINGS.MANAGE",ct);
        return Ok(ApiResponse<WarehouseTransferPolicyDto>.Ok(await service.UpdateAsync(request,UserId(),ct),"Transfer politikası kaydedildi."));
    }
    private long UserId()=>long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier),out var id)?id:throw AppException.Unauthorized("Kullanıcı kimliği bulunamadı.");
    private async Task Require(string code,CancellationToken ct){if(!await permissions.HasPermissionAsync(User,code,ct))throw AppException.Forbidden("Bu işlem için yetkiniz bulunmuyor.");}
}
