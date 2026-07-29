using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using verii_wms_api_v2.Modules.AccessControl.Application;
using verii_wms_api_v2.Modules.GoodsReceipt.Application;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.GoodsReceipt.Api;

[Authorize,ApiController,Route("api/goods-receipt-policy")]
public sealed class GoodsReceiptPolicyController(IGoodsReceiptPolicyService service,IPermissionAuthorizationService permissions):ControllerBase
{
    [HttpGet] public async Task<IActionResult> Get([FromQuery]string branchCode,CancellationToken ct)
    {
        if (!await permissions.HasPermissionAsync(User, "WMS.GOODS_RECEIPT.SETTINGS.VIEW", ct)
            && !await permissions.HasPermissionAsync(User, "WMS.GOODS_RECEIPT.CREATE", ct))
            throw AppException.Forbidden();
        return Ok(ApiResponse<GoodsReceiptPolicyDto>.Ok(await service.GetAsync(branchCode, ct)));
    }
    [HttpPut] public async Task<IActionResult> Update(UpdateGoodsReceiptPolicyRequest request,CancellationToken ct){await Require("WMS.GOODS_RECEIPT.SETTINGS.MANAGE",ct);return Ok(ApiResponse<GoodsReceiptPolicyDto>.Ok(await service.UpdateAsync(request,UserId(),ct),"Mal kabul politikası kaydedildi."));}
    private long UserId()=>long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier),out var id)?id:throw AppException.Unauthorized("Geçersiz kullanıcı oturumu.");
    private async Task Require(string code,CancellationToken ct){if(!await permissions.HasPermissionAsync(User,code,ct))throw AppException.Forbidden();}
}
