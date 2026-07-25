using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using verii_wms_api_v2.Modules.AccessControl.Application;
using verii_wms_api_v2.Modules.Packing.Application;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.Packing.Api;

[Authorize,ApiController,Route("api/packing")]
public sealed class PackingController(IPackingService service,IPermissionAuthorizationService permissions):ControllerBase
{
    [HttpPost("materials/paged")] public async Task<IActionResult> Materials(PagedRequest r,CancellationToken ct){await Require("WMS.PACKING.DEFINITIONS.VIEW",ct);return Ok(ApiResponse<PagedResponse<PackagingMaterialRow>>.Ok(await service.GetMaterialsAsync(r,ct)));}
    [HttpPost("materials")] public async Task<IActionResult> CreateMaterial(PackagingMaterialRequest r,CancellationToken ct){await Require("WMS.PACKING.DEFINITIONS.MANAGE",ct);return Ok(ApiResponse<object>.Ok(new{id=await service.CreateMaterialAsync(r,UserId(),ct)}));}
    [HttpPut("materials/{id:long}"),HttpPost("materials/{id:long}/update")] public async Task<IActionResult> UpdateMaterial(long id,PackagingMaterialRequest r,CancellationToken ct){await Require("WMS.PACKING.DEFINITIONS.MANAGE",ct);await service.UpdateMaterialAsync(id,r,UserId(),ct);return Ok(ApiResponse<bool>.Ok(true));}
    [HttpDelete("materials/{id:long}"),HttpPost("materials/{id:long}/delete")] public async Task<IActionResult> DeleteMaterial(long id,CancellationToken ct){await Require("WMS.PACKING.DEFINITIONS.MANAGE",ct);await service.DeleteMaterialAsync(id,UserId(),ct);return Ok(ApiResponse<bool>.Ok(true));}
    [HttpPost("stations/paged")] public async Task<IActionResult> Stations(PagedRequest r,CancellationToken ct){await Require("WMS.PACKING.DEFINITIONS.VIEW",ct);return Ok(ApiResponse<PagedResponse<PackingStationRow>>.Ok(await service.GetStationsAsync(r,ct)));}
    [HttpPost("stations")] public async Task<IActionResult> CreateStation(PackingStationRequest r,CancellationToken ct){await Require("WMS.PACKING.DEFINITIONS.MANAGE",ct);return Ok(ApiResponse<object>.Ok(new{id=await service.CreateStationAsync(r,UserId(),ct)}));}
    [HttpPut("stations/{id:long}"),HttpPost("stations/{id:long}/update")] public async Task<IActionResult> UpdateStation(long id,PackingStationRequest r,CancellationToken ct){await Require("WMS.PACKING.DEFINITIONS.MANAGE",ct);await service.UpdateStationAsync(id,r,UserId(),ct);return Ok(ApiResponse<bool>.Ok(true));}
    [HttpDelete("stations/{id:long}"),HttpPost("stations/{id:long}/delete")] public async Task<IActionResult> DeleteStation(long id,CancellationToken ct){await Require("WMS.PACKING.DEFINITIONS.MANAGE",ct);await service.DeleteStationAsync(id,UserId(),ct);return Ok(ApiResponse<bool>.Ok(true));}
    [HttpPost("specifications/paged")] public async Task<IActionResult> Specifications(PagedRequest r,CancellationToken ct){await Require("WMS.PACKING.DEFINITIONS.VIEW",ct);return Ok(ApiResponse<PagedResponse<PackagingSpecificationRow>>.Ok(await service.GetSpecificationsAsync(r,ct)));}
    [HttpPost("specifications")] public async Task<IActionResult> CreateSpecification(PackagingSpecificationRequest r,CancellationToken ct){await Require("WMS.PACKING.DEFINITIONS.MANAGE",ct);return Ok(ApiResponse<object>.Ok(new{id=await service.CreateSpecificationAsync(r,UserId(),ct)}));}
    [HttpPut("specifications/{id:long}"),HttpPost("specifications/{id:long}/update")] public async Task<IActionResult> UpdateSpecification(long id,PackagingSpecificationRequest r,CancellationToken ct){await Require("WMS.PACKING.DEFINITIONS.MANAGE",ct);await service.UpdateSpecificationAsync(id,r,UserId(),ct);return Ok(ApiResponse<bool>.Ok(true));}
    [HttpDelete("specifications/{id:long}"),HttpPost("specifications/{id:long}/delete")] public async Task<IActionResult> DeleteSpecification(long id,CancellationToken ct){await Require("WMS.PACKING.DEFINITIONS.MANAGE",ct);await service.DeleteSpecificationAsync(id,UserId(),ct);return Ok(ApiResponse<bool>.Ok(true));}
    [HttpGet("policy")] public async Task<IActionResult> Policy([FromQuery]string branchCode="0",CancellationToken ct=default){await Require("WMS.PACKING.SETTINGS.VIEW",ct);return Ok(ApiResponse<PackingPolicyDto>.Ok(await service.GetPolicyAsync(branchCode,ct)));}
    [HttpPut("policy"),HttpPost("policy/update")] public async Task<IActionResult> PolicyUpdate(UpdatePackingPolicyRequest r,CancellationToken ct){await Require("WMS.PACKING.SETTINGS.MANAGE",ct);return Ok(ApiResponse<PackingPolicyDto>.Ok(await service.UpdatePolicyAsync(r,UserId(),ct)));}
    [HttpPost("sessions/paged")] public async Task<IActionResult> Sessions(PagedRequest r,CancellationToken ct){await Require("WMS.PACKING.VIEW",ct);return Ok(ApiResponse<PagedResponse<PackingSessionRow>>.Ok(await service.GetSessionsAsync(r,ct)));}
    [HttpGet("sessions/{id:long}")] public async Task<IActionResult> Session(long id,CancellationToken ct){await Require("WMS.PACKING.VIEW",ct);return Ok(ApiResponse<PackingSessionDetail>.Ok(await service.GetSessionAsync(id,ct)));}
    [HttpGet("sessions/{id:long}/source-lines")] public async Task<IActionResult> SourceLines(long id,CancellationToken ct){await Require("WMS.PACKING.VIEW",ct);return Ok(ApiResponse<IReadOnlyList<PackingSourceLineOption>>.Ok(await service.GetSourceLinesAsync(id,ct)));}
    [HttpPost("sessions")] public async Task<IActionResult> CreateSession(CreatePackingSessionRequest r,CancellationToken ct){await Require("WMS.PACKING.OPERATE",ct);return Ok(ApiResponse<PackingSessionDetail>.Ok(await service.CreateSessionAsync(r,UserId(),ct)));}
    [HttpPost("sessions/{id:long}/handling-units")] public async Task<IActionResult> CreateUnit(long id,CreateHandlingUnitRequest r,CancellationToken ct){await Require("WMS.PACKING.OPERATE",ct);return Ok(ApiResponse<HandlingUnitDto>.Ok(await service.CreateHandlingUnitAsync(id,r,UserId(),ct)));}
    [HttpPost("handling-units/{id:long}/pack")] public async Task<IActionResult> Pack(long id,PackHandlingUnitLineRequest r,CancellationToken ct){await Require("WMS.PACKING.OPERATE",ct);return Ok(ApiResponse<HandlingUnitDto>.Ok(await service.PackAsync(id,r,UserId(),ct)));}
    [HttpPost("handling-units/{id:long}/unpack")] public async Task<IActionResult> Unpack(long id,UnpackHandlingUnitLineRequest r,CancellationToken ct){await Require("WMS.PACKING.OPERATE",ct);return Ok(ApiResponse<HandlingUnitDto>.Ok(await service.UnpackAsync(id,r,UserId(),ct)));}
    [HttpPost("handling-units/{id:long}/move")] public async Task<IActionResult> Move(long id,MoveHandlingUnitLineRequest r,CancellationToken ct){await Require("WMS.PACKING.OPERATE",ct);return Ok(ApiResponse<HandlingUnitDto>.Ok(await service.MoveAsync(id,r,UserId(),ct)));}
    [HttpPost("handling-units/{id:long}/close")] public async Task<IActionResult> Close(long id,CloseHandlingUnitRequest r,CancellationToken ct){await Require("WMS.PACKING.CLOSE",ct);return Ok(ApiResponse<HandlingUnitDto>.Ok(await service.CloseAsync(id,r,UserId(),ct)));}
    [HttpPost("handling-units/{id:long}/reopen")] public async Task<IActionResult> Reopen(long id,[FromBody]ReopenRequest r,CancellationToken ct){await Require("WMS.PACKING.REOPEN",ct);return Ok(ApiResponse<HandlingUnitDto>.Ok(await service.ReopenAsync(id,r.IdempotencyKey,r.Reason,UserId(),ct)));}
    [HttpDelete("handling-units/{id:long}"),HttpPost("handling-units/{id:long}/delete")] public async Task<IActionResult> DeleteUnit(long id,CancellationToken ct){await Require("WMS.PACKING.OPERATE",ct);await service.DeleteHandlingUnitAsync(id,UserId(),ct);return Ok(ApiResponse<bool>.Ok(true));}
    [HttpPost("handling-units/{id:long}/print")] public async Task<IActionResult> Print(long id,PrintHandlingUnitRequest r,CancellationToken ct){await Require("WMS.PACKING.OPERATE",ct);return Ok(ApiResponse<PackingPrintJobRow>.Ok(await service.EnqueuePrintAsync(id,r,UserId(),ct)));}
    [HttpPost("handling-units/{id:long}/read-scale")] public async Task<IActionResult> ReadScale(long id,ScaleReadingRequest r,CancellationToken ct){await Require("WMS.PACKING.OPERATE",ct);return Ok(ApiResponse<ScaleReadingDto>.Ok(await service.ReadScaleAsync(id,r,UserId(),ct)));}
    [HttpPost("print-jobs/paged")] public async Task<IActionResult> PrintJobs(PagedRequest r,CancellationToken ct){await Require("WMS.PACKING.VIEW",ct);return Ok(ApiResponse<PagedResponse<PackingPrintJobRow>>.Ok(await service.GetPrintJobsAsync(r,ct)));}
    private long UserId()=>long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier),out var id)?id:throw AppException.Unauthorized("Kullanıcı kimliği bulunamadı.");
    private async Task Require(string code,CancellationToken ct){if(!await permissions.HasPermissionAsync(User,code,ct))throw AppException.Forbidden();}
}
public sealed record ReopenRequest(Guid IdempotencyKey,string? Reason);
