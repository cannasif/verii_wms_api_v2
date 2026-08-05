using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using verii_wms_api_v2.Modules.AccessControl.Application;
using verii_wms_api_v2.Modules.VehicleCheckIn.Application;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.VehicleCheckIn.Api;

[Authorize,ApiController,Route("api/vehicle-check-ins")]
public sealed class VehicleCheckInController(IVehicleCheckInService service,IPermissionAuthorizationService permissions):ControllerBase
{
    [HttpGet("today-by-plate")]public async Task<IActionResult> Today([FromQuery]string branchCode,[FromQuery]string plateNo,CancellationToken ct)
    {await Require("WMS.STEEL_RECEIPT.VEHICLE.VIEW",ct);return Ok(ApiResponse<VehicleCheckInDetail?>.Ok(await service.FindTodayByPlateAsync(branchCode,plateNo,ct)));}
    [HttpPost]public async Task<IActionResult> Save(SaveVehicleCheckInRequest request,CancellationToken ct)
    {await Require("WMS.STEEL_RECEIPT.VEHICLE.MANAGE",ct);return Ok(ApiResponse<VehicleCheckInDetail>.Ok(await service.SaveAsync(request,UserId(),ct),"Araç giriş kaydı kaydedildi."));}
    [HttpGet("{id:long}")]public async Task<IActionResult> Get(long id,CancellationToken ct)
    {await Require("WMS.STEEL_RECEIPT.VEHICLE.VIEW",ct);return Ok(ApiResponse<VehicleCheckInDetail>.Ok(await service.GetAsync(id,ct)));}
    [HttpPost("paged")]public async Task<IActionResult> Paged(PagedRequest request,CancellationToken ct)
    {await Require("WMS.STEEL_RECEIPT.VEHICLE.VIEW",ct);return Ok(ApiResponse<PagedResponse<VehicleCheckInRow>>.Ok(await service.GetPagedAsync(request,ct)));}
    [HttpPost("{id:long}/images"),RequestSizeLimit(80_000_000)]public async Task<IActionResult> Images(long id,List<IFormFile> files,CancellationToken ct)
    {
        await Require("WMS.STEEL_RECEIPT.VEHICLE.MANAGE",ct);var uploads=new List<VehicleImageUpload>();
        foreach(var file in files)uploads.Add(new(file.OpenReadStream(),file.FileName,file.ContentType,file.Length));
        try{return Ok(ApiResponse<IReadOnlyList<VehicleCheckInImageRow>>.Ok(await service.AddImagesAsync(id,uploads,UserId(),ct),"Araç görselleri yüklendi."));}
        finally{foreach(var upload in uploads)await upload.Content.DisposeAsync();}
    }
    [HttpGet("images/{id:long}/file")]public async Task<IActionResult> Image(long id,CancellationToken ct)
    {await Require("WMS.STEEL_RECEIPT.VEHICLE.VIEW",ct);var file=await service.DownloadImageAsync(id,ct);return File(file.Content,file.ContentType,file.FileName);}
    [HttpDelete("images/{id:long}")]public async Task<IActionResult> DeleteImage(long id,CancellationToken ct)
    {await Require("WMS.STEEL_RECEIPT.VEHICLE.MANAGE",ct);await service.RemoveImageAsync(id,UserId(),ct);return Ok(ApiResponse<bool>.Ok(true,"Araç görseli silindi."));}
    private long UserId()=>long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier),out var id)?id:throw AppException.Unauthorized("Geçersiz kullanıcı oturumu.");
    private async Task Require(string code,CancellationToken ct){if(!await permissions.HasPermissionAsync(User,code,ct))throw AppException.Forbidden();}
}
