using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using verii_wms_api_v2.Modules.AccessControl.Application;
using verii_wms_api_v2.Modules.WarehouseInbound.Application;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.WarehouseInbound.Api;

[Authorize,ApiController,Route("api/warehouse-inbound-policy")]
public sealed class WarehouseInboundPolicyController(IWarehouseInboundPolicyService service,IPermissionAuthorizationService permissions):ControllerBase
{
    [HttpGet] public async Task<IActionResult> Get([FromQuery]string branchCode,CancellationToken ct){await Require("WMS.WAREHOUSE_INBOUND.SETTINGS.VIEW",ct);return Ok(ApiResponse<WarehouseInboundPolicyDto>.Ok(await service.GetAsync(branchCode,ct)));}
    [HttpPut] public async Task<IActionResult> Update(UpdateWarehouseInboundPolicyRequest request,CancellationToken ct){await Require("WMS.WAREHOUSE_INBOUND.SETTINGS.MANAGE",ct);return Ok(ApiResponse<WarehouseInboundPolicyDto>.Ok(await service.UpdateAsync(request,UserId(),ct),"Mal kabul politikası kaydedildi."));}
    private long UserId()=>long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier),out var id)?id:throw AppException.Unauthorized("Geçersiz kullanıcı oturumu.");
    private async Task Require(string code,CancellationToken ct){if(!await permissions.HasPermissionAsync(User,code,ct))throw AppException.Forbidden();}
}
