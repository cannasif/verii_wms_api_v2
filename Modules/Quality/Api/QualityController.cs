using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using verii_wms_api_v2.Modules.AccessControl.Application;
using verii_wms_api_v2.Modules.Quality.Application;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.Quality.Api;

[Authorize,ApiController,Route("api/quality")]
public sealed class QualityController(IQualityService service,IPermissionAuthorizationService permissions):ControllerBase
{
    [HttpGet("parameters")] public async Task<IActionResult> Parameters([FromQuery]string branchCode,CancellationToken ct){await Require("WMS.QUALITY.SETTINGS.VIEW",ct);return Ok(ApiResponse<QualityParameterDto>.Ok(await service.GetParametersAsync(branchCode,ct)));}
    [HttpPut("parameters")] public async Task<IActionResult> UpdateParameters(UpdateQualityParameterRequest request,CancellationToken ct){await Require("WMS.QUALITY.SETTINGS.MANAGE",ct);return Ok(ApiResponse<QualityParameterDto>.Ok(await service.UpdateParametersAsync(request,UserId(),ct),"Kalite parametreleri kaydedildi."));}
    [HttpPost("rules/paged")] public async Task<IActionResult> RulesPaged(PagedRequest request,CancellationToken ct){await Require("WMS.QUALITY.RULES.VIEW",ct);return Ok(ApiResponse<PagedResponse<QualityRuleGridRow>>.Ok(await service.GetRulesPagedAsync(request,ct)));}
    [HttpPost("rules")] public async Task<IActionResult> CreateRule(QualityRuleUpsertRequest request,CancellationToken ct){await Require("WMS.QUALITY.RULES.MANAGE",ct);return Ok(ApiResponse<object>.Ok(new{id=await service.CreateRuleAsync(request,UserId(),ct)},"Kalite kuralı oluşturuldu."));}
    [HttpPut("rules/{id:long}"),HttpPost("rules/{id:long}/update")] public async Task<IActionResult> UpdateRule(long id,QualityRuleUpsertRequest request,CancellationToken ct){await Require("WMS.QUALITY.RULES.MANAGE",ct);await service.UpdateRuleAsync(id,request,UserId(),ct);return Ok(ApiResponse<bool>.Ok(true,"Kalite kuralı güncellendi."));}
    [HttpDelete("rules/{id:long}"),HttpPost("rules/{id:long}/delete")] public async Task<IActionResult> DeleteRule(long id,CancellationToken ct){await Require("WMS.QUALITY.RULES.MANAGE",ct);await service.DeleteRuleAsync(id,UserId(),ct);return Ok(ApiResponse<bool>.Ok(true,"Kalite kuralı silindi."));}
    [HttpPost("inspections/paged")] public async Task<IActionResult> InspectionsPaged(PagedRequest request,CancellationToken ct){await Require("WMS.QUALITY.INSPECTIONS.VIEW",ct);return Ok(ApiResponse<PagedResponse<QualityInspectionGridRow>>.Ok(await service.GetInspectionsPagedAsync(request,ct)));}
    [HttpGet("inspections/{id:long}")] public async Task<IActionResult> Inspection(long id,CancellationToken ct){await Require("WMS.QUALITY.INSPECTIONS.VIEW",ct);return Ok(ApiResponse<QualityInspectionDetail>.Ok(await service.GetInspectionAsync(id,ct)));}
    [HttpPost("inspections/{id:long}/decision")] public async Task<IActionResult> Decide(long id,DecideQualityInspectionRequest request,CancellationToken ct){await Require("WMS.QUALITY.INSPECTIONS.DECIDE",ct);var canRelease=await permissions.HasPermissionAsync(User,"WMS.QUALITY.INSPECTIONS.RELEASE",ct);await service.DecideInspectionAsync(id,request,UserId(),canRelease,ct);return Ok(ApiResponse<bool>.Ok(true,"Kalite kararı kaydedildi."));}
    private long UserId()=>long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier),out var id)?id:throw AppException.Unauthorized("Geçersiz kullanıcı oturumu."); private async Task Require(string code,CancellationToken ct){if(!await permissions.HasPermissionAsync(User,code,ct))throw AppException.Forbidden();}
}
