using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using verii_wms_api_v2.Modules.AccessControl.Application;
using verii_wms_api_v2.Modules.Identity.Infrastructure;
using verii_wms_api_v2.Modules.Stock.Application;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.Stock.Api;

[Authorize,ApiController,Route("api/stocks/{stockId:long}/images")]
public sealed class StockImagesController(IStockImageService service,IPermissionAuthorizationService permissions):ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(long stockId,CancellationToken ct){await Require("ERP.MIRROR.VIEW",ct);return Ok(ApiResponse<IReadOnlyList<StockImageDto>>.Ok(await service.ListAsync(stockId,Branch(),ct)));}

    [HttpPost,Consumes("multipart/form-data"),RequestSizeLimit(105_000_000),RequestFormLimits(MultipartBodyLengthLimit=105_000_000)]
    public async Task<IActionResult> Upload(long stockId,[FromForm]List<IFormFile>? files,[FromForm]List<string>? altTexts,CancellationToken ct)
    {
        await Require("ERP.MIRROR.SYNC",ct);
        if(files is null||files.Count==0)throw AppException.BadRequest("En az bir görsel seçilmelidir.");
        var uploads=new List<StockImageUpload>(files.Count);
        for(var i=0;i<files.Count;i++)
        {
            var file=files[i]; var stream=file.OpenReadStream();
            uploads.Add(new(stream,file.FileName,file.ContentType,file.Length,altTexts is not null&&i<altTexts.Count?altTexts[i]:null));
        }
        try{return Ok(ApiResponse<IReadOnlyList<StockImageDto>>.Ok(await service.UploadAsync(stockId,Branch(),UserId(),uploads,ct),"Görseller yüklendi."));}
        finally{foreach(var upload in uploads)await upload.Content.DisposeAsync();}
    }

    [HttpPatch("{imageId:long}")]
    public async Task<IActionResult> Update(long stockId,long imageId,[FromBody]UpdateStockImageRequest request,CancellationToken ct){await Require("ERP.MIRROR.SYNC",ct);return Ok(ApiResponse<StockImageDto>.Ok(await service.UpdateAsync(stockId,imageId,Branch(),UserId(),request.AltText,ct)));}
    [HttpPut("{imageId:long}/primary")]
    public async Task<IActionResult> Primary(long stockId,long imageId,CancellationToken ct){await Require("ERP.MIRROR.SYNC",ct);return Ok(ApiResponse<StockImageDto>.Ok(await service.SetPrimaryAsync(stockId,imageId,Branch(),UserId(),ct),"Kapak görseli güncellendi."));}
    [HttpPut("order")]
    public async Task<IActionResult> Order(long stockId,[FromBody]ReorderStockImagesRequest request,CancellationToken ct){await Require("ERP.MIRROR.SYNC",ct);return Ok(ApiResponse<IReadOnlyList<StockImageDto>>.Ok(await service.ReorderAsync(stockId,Branch(),UserId(),request.ImageIds,ct)));}
    [HttpDelete("{imageId:long}")]
    public async Task<IActionResult> Delete(long stockId,long imageId,CancellationToken ct){await Require("ERP.MIRROR.SYNC",ct);await service.DeleteAsync(stockId,imageId,Branch(),UserId(),ct);return Ok(ApiResponse<object>.Ok(new{},"Görsel silindi."));}

    private string Branch()=>User.FindFirstValue(JwtTokenIssuer.BranchCodeClaim)?.Trim()??throw AppException.Unauthorized("Şube bilgisi bulunamadı.");
    private long UserId()=>long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier),out var id)?id:throw AppException.Unauthorized("Kullanıcı kimliği bulunamadı.");
    private async Task Require(string code,CancellationToken ct){if(!await permissions.HasPermissionAsync(User,code,ct))throw AppException.Forbidden();}
}
