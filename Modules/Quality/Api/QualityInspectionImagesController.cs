using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using verii_wms_api_v2.Modules.AccessControl.Application;
using verii_wms_api_v2.Modules.Identity.Infrastructure;
using verii_wms_api_v2.Modules.Quality.Application;
using verii_wms_api_v2.Modules.Quality.Localization;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.Quality.Api;

[Authorize,ApiController,Route("api/quality/inspections/{inspectionId:long}/lines/{lineId:long}/images")]
public sealed class QualityInspectionImagesController(
    IQualityInspectionImageService service,
    IPermissionAuthorizationService permissions,
    IStringLocalizer<QualityResource> localizer):ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(long inspectionId,long lineId,CancellationToken ct)
    {
        await Require("WMS.QUALITY.INSPECTIONS.IMAGES.VIEW",ct);
        return Ok(ApiResponse<IReadOnlyList<QualityInspectionImageDto>>.Ok(await service.ListAsync(inspectionId,lineId,Branch(),ct)));
    }

    [HttpPost,Consumes("multipart/form-data"),RequestSizeLimit(105_000_000),RequestFormLimits(MultipartBodyLengthLimit=105_000_000)]
    public async Task<IActionResult> Upload(long inspectionId,long lineId,List<IFormFile>? files,[FromForm]List<string>? captions,CancellationToken ct)
    {
        await Require("WMS.QUALITY.INSPECTIONS.IMAGES.UPLOAD",ct);
        if(files is null||files.Count==0)throw AppException.BadRequest(Message(QualityMessageKeys.InspectionImageRequired));
        var uploads=new List<QualityInspectionImageUpload>(files.Count);
        for(var index=0;index<files.Count;index++)
        {
            var file=files[index];
            uploads.Add(new(file.OpenReadStream(),file.FileName,file.ContentType,file.Length,captions is not null&&index<captions.Count?captions[index]:null));
        }
        try
        {
            var result=await service.UploadAsync(inspectionId,lineId,Branch(),UserId(),uploads,ct);
            return Ok(ApiResponse<IReadOnlyList<QualityInspectionImageDto>>.Ok(result,Message(QualityMessageKeys.InspectionImagesUploaded)));
        }
        finally
        {
            foreach(var upload in uploads)await upload.Content.DisposeAsync();
        }
    }

    [HttpGet("{imageId:long}/content")]
    public async Task<IActionResult> Content(long inspectionId,long lineId,long imageId,CancellationToken ct)
    {
        await Require("WMS.QUALITY.INSPECTIONS.IMAGES.VIEW",ct);
        var file=await service.OpenAsync(inspectionId,lineId,imageId,Branch(),ct);
        Response.Headers.CacheControl="private, no-store";
        return File(file.Content,file.ContentType,enableRangeProcessing:true);
    }

    [HttpDelete("{imageId:long}"),HttpPost("{imageId:long}/delete")]
    public async Task<IActionResult> Delete(long inspectionId,long lineId,long imageId,CancellationToken ct)
    {
        await Require("WMS.QUALITY.INSPECTIONS.IMAGES.DELETE",ct);
        await service.DeleteAsync(inspectionId,lineId,imageId,Branch(),UserId(),ct);
        return Ok(ApiResponse<object>.Ok(new{},Message(QualityMessageKeys.InspectionImageDeleted)));
    }

    private string Branch()=>User.FindFirstValue(JwtTokenIssuer.BranchCodeClaim)?.Trim()??throw AppException.Unauthorized("Şube bilgisi bulunamadı.");
    private long UserId()=>long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier),out var id)?id:throw AppException.Unauthorized("Kullanıcı kimliği bulunamadı.");
    private async Task Require(string code,CancellationToken ct){if(!await permissions.HasPermissionAsync(User,code,ct))throw AppException.Forbidden();}
    private string Message(string key,params object[] arguments)=>
        arguments.Length==0?localizer[key].Value:localizer[key,arguments].Value;
}
