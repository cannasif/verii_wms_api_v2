using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using verii_wms_api_v2.Modules.AccessControl.Application;
using verii_wms_api_v2.Modules.SerialNumberPolicy.Application;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.SerialNumberPolicy.Api;

[Authorize,ApiController,Route("api/serial-number-rules")]
public sealed class SerialNumberRulesController(ISerialNumberPolicyService service,IPermissionAuthorizationService permissions):ControllerBase
{
    [HttpPost("paged")] public async Task<IActionResult> Paged(PagedRequest r,CancellationToken ct){await Require("WMS.SERIAL_RULES.VIEW",ct);return Ok(ApiResponse<PagedResponse<SerialRuleRow>>.Ok(await service.GetPagedAsync(r,ct)));}
    [HttpPost] public async Task<IActionResult> Create(SerialRuleUpsertRequest r,CancellationToken ct){await Require("WMS.SERIAL_RULES.MANAGE",ct);return Ok(ApiResponse<object>.Ok(new{id=await service.CreateAsync(r,UserId(),ct)}));}
    [HttpPost("{id:long}/versions")] public async Task<IActionResult> Version(long id,SerialRuleUpsertRequest r,[FromQuery]string? concurrencyToken,CancellationToken ct){await Require("WMS.SERIAL_RULES.MANAGE",ct);return Ok(ApiResponse<object>.Ok(new{id=await service.CreateNextVersionAsync(id,r,UserId(),concurrencyToken,ct)}));}
    [HttpPost("{id:long}/delete")] public async Task<IActionResult> Delete(long id,CancellationToken ct){await Require("WMS.SERIAL_RULES.MANAGE",ct);await service.DeleteAsync(id,UserId(),ct);return Ok(ApiResponse<bool>.Ok(true));}
    [HttpPost("validate")] public async Task<IActionResult> Validate(ValidateSerialRequest r,CancellationToken ct){await Require("WMS.SERIAL_RULES.VIEW",ct);return Ok(ApiResponse<SerialValidationResult>.Ok(await service.ValidateAsync(r,ct)));}
    [HttpPost("generate")] public async Task<IActionResult> Generate(GenerateStockSerialsRequest r,CancellationToken ct){await RequireAny(["WMS.SERIAL_RULES.MANAGE","WMS.GOODS_RECEIPT.CREATE","WMS.WAREHOUSE_INBOUND.CREATE"],ct);return Ok(ApiResponse<GenerateStockSerialsResult>.Ok(await service.GenerateAsync(r,UserId(),ct)));}
    [HttpPost("void")] public async Task<IActionResult> Void(VoidGeneratedSerialsRequest r,CancellationToken ct){await RequireAny(["WMS.SERIAL_RULES.MANAGE","WMS.GOODS_RECEIPT.CREATE","WMS.WAREHOUSE_INBOUND.CREATE"],ct);return Ok(ApiResponse<VoidGeneratedSerialsResult>.Ok(await service.VoidAsync(r,UserId(),ct)));}
    private long UserId()=>long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier),out var id)?id:throw AppException.Unauthorized("Geçersiz kullanıcı oturumu.");
    private async Task Require(string code,CancellationToken ct){if(!await permissions.HasPermissionAsync(User,code,ct))throw AppException.Forbidden();}
    private async Task RequireAny(IReadOnlyCollection<string> codes,CancellationToken ct)
    {
        foreach(var code in codes)if(await permissions.HasPermissionAsync(User,code,ct))return;
        throw AppException.Forbidden();
    }
}
