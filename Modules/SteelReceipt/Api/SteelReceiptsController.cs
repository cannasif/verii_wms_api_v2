using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using verii_wms_api_v2.Modules.AccessControl.Application;
using verii_wms_api_v2.Modules.SteelReceipt.Application;
using verii_wms_api_v2.Modules.SteelReceipt.Domain;
using verii_wms_api_v2.Modules.VehicleCheckIn.Application;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.SteelReceipt.Api;

[Authorize,ApiController,Route("api/steel-receipts")]
public sealed class SteelReceiptsController(
    ISteelReceiptService service,
    ISteelVehicleAcceptanceService vehicleAcceptance,
    IPermissionAuthorizationService permissions):ControllerBase
{
    [HttpPost("import/preview")] public async Task<IActionResult> Preview(PreviewSteelReceiptImportRequest request,CancellationToken ct)
    {await Require("WMS.STEEL_RECEIPT.IMPORT",ct);return Ok(ApiResponse<SteelImportPreview>.Ok(await service.PreviewAsync(request,ct)));}
    [HttpPost("import/commit")] public async Task<IActionResult> Commit(CommitSteelReceiptImportRequest request,CancellationToken ct)
    {await Require("WMS.STEEL_RECEIPT.IMPORT",ct);return Ok(ApiResponse<long>.Ok(await service.CommitAsync(request,UserId(),ct),"SAC beklenti planı oluşturuldu."));}
    [HttpPost("paged")] public async Task<IActionResult> Plans(PagedRequest request,CancellationToken ct)
    {await Require("WMS.STEEL_RECEIPT.VIEW",ct);return Ok(ApiResponse<PagedResponse<SteelReceiptPlanGridRow>>.Ok(await service.GetPlansPagedAsync(request,ct)));}
    [HttpPost("lines/paged")] public async Task<IActionResult> Lines(PagedRequest request,CancellationToken ct)
    {await Require("WMS.STEEL_RECEIPT.VIEW",ct);return Ok(ApiResponse<PagedResponse<SteelReceiptLineGridRow>>.Ok(await service.GetLinesPagedAsync(request,ct)));}
    [HttpPost("vehicle-acceptance/candidates/paged")] public async Task<IActionResult> VehicleAcceptanceCandidates(
        [FromQuery]string branchCode,
        PagedRequest request,
        CancellationToken ct)
    {
        await Require("WMS.STEEL_RECEIPT.VIEW",ct);
        await Require("WMS.STEEL_RECEIPT.VEHICLE.VIEW",ct);
        return Ok(ApiResponse<PagedResponse<SteelVehicleAcceptanceCandidateRow>>.Ok(
            await vehicleAcceptance.GetCandidatesPagedAsync(branchCode,request,ct)));
    }
    [HttpGet("vehicle-acceptance/by-vehicle/{vehicleCheckInId:long}")] public async Task<IActionResult> VehicleAcceptanceByVehicle(
        long vehicleCheckInId,
        CancellationToken ct)
    {
        await Require("WMS.STEEL_RECEIPT.VIEW",ct);
        await Require("WMS.STEEL_RECEIPT.VEHICLE.VIEW",ct);
        return Ok(ApiResponse<CompleteSteelVehicleAcceptanceResult?>.Ok(
            await vehicleAcceptance.GetLatestByVehicleAsync(vehicleCheckInId,ct)));
    }
    [HttpPost("vehicle-acceptance/complete"),RequestSizeLimit(130_000_000)] public async Task<IActionResult> CompleteVehicleAcceptance(
        [FromForm]SteelVehicleAcceptanceForm form,
        CancellationToken ct)
    {
        await Require("WMS.STEEL_RECEIPT.VEHICLE.MANAGE",ct);
        await Require("WMS.STEEL_RECEIPT.INSPECT",ct);
        if(string.IsNullOrWhiteSpace(form.RequestJson))throw AppException.BadRequest("Araç kabul isteği eksik.");
        CompleteSteelVehicleAcceptanceRequest request;
        try
        {
            request=JsonSerializer.Deserialize<CompleteSteelVehicleAcceptanceRequest>(
                form.RequestJson,
                new JsonSerializerOptions(JsonSerializerDefaults.Web))
                ??throw new JsonException();
        }
        catch(JsonException)
        {
            throw AppException.BadRequest("Araç kabul isteği geçersiz.");
        }
        if(form.PlateImages.Count!=form.PlateImageLineIds.Count)
            throw AppException.BadRequest("Levha görsel eşleştirmesi geçersiz.");

        var vehicleUploads=form.VehicleImages.Select(file=>
            new VehicleImageUpload(file.OpenReadStream(),file.FileName,file.ContentType,file.Length)).ToList();
        var plateUploads=form.PlateImages.Select((file,index)=>
            new SteelPlateImageUpload(
                form.PlateImageLineIds[index],file.OpenReadStream(),file.FileName,file.ContentType,file.Length)).ToList();
        try
        {
            var result=await vehicleAcceptance.CompleteAsync(request,vehicleUploads,plateUploads,UserId(),ct);
            return Ok(ApiResponse<CompleteSteelVehicleAcceptanceResult>.Ok(
                result,
                result.Replayed?"Araç ve SAC kabulü daha önce tamamlandı.":"Araç giriş ve SAC kabulü tamamlandı."));
        }
        finally
        {
            foreach(var upload in vehicleUploads)await upload.Content.DisposeAsync();
            foreach(var upload in plateUploads)await upload.Content.DisposeAsync();
        }
    }
    [HttpPost("receipt/candidates/paged")] public async Task<IActionResult> ReceiptCandidates(PagedRequest request,CancellationToken ct)
    {await Require("WMS.STEEL_RECEIPT.CONVERT",ct);return Ok(ApiResponse<PagedResponse<SteelReceiptLineGridRow>>.Ok(await service.GetReceiptCandidatesPagedAsync(request,ct)));}
    [HttpPost("receipt/sources/paged")] public async Task<IActionResult> PendingReceiptSources(PagedRequest request,CancellationToken ct)
    {await Require("WMS.STEEL_RECEIPT.CONVERT",ct);return Ok(ApiResponse<PagedResponse<SteelReceiptPendingSourceRow>>.Ok(await service.GetPendingReceiptSourcesPagedAsync(request,ct)));}
    [HttpGet("receipt/source")] public async Task<IActionResult> ReceiptSource([FromQuery]string reference,CancellationToken ct)
    {await Require("WMS.STEEL_RECEIPT.CONVERT",ct);return Ok(ApiResponse<SteelReceiptSourceRow>.Ok(await service.GetReceiptSourceAsync(reference,ct)));}
    [HttpPost("placement/candidates/paged")] public async Task<IActionResult> PlacementCandidates(PagedRequest request,CancellationToken ct)
    {await Require("WMS.STEEL_RECEIPT.PUTAWAY",ct);return Ok(ApiResponse<PagedResponse<SteelReceiptLineGridRow>>.Ok(await service.GetPlacementCandidatesPagedAsync(request,ct)));}
    [HttpGet("lines/{id:long}")] public async Task<IActionResult> Line(long id,CancellationToken ct)
    {await Require("WMS.STEEL_RECEIPT.VIEW",ct);return Ok(ApiResponse<SteelReceiptLineGridRow>.Ok(await service.GetLineAsync(id,ct)));}
    [HttpGet("placement/occupancy")] public async Task<IActionResult> Occupancy([FromQuery]long locationId,CancellationToken ct)
    {await Require("WMS.STEEL_RECEIPT.VIEW",ct);return Ok(ApiResponse<IReadOnlyList<SteelPlacementOccupancyRow>>.Ok(await service.GetOccupancyAsync(locationId,ct)));}
    [HttpPut("lines/{id:long}/inspection")] public async Task<IActionResult> Inspect(long id,InspectSteelReceiptLineRequest request,CancellationToken ct)
    {await Require("WMS.STEEL_RECEIPT.INSPECT",ct);return Ok(ApiResponse<SteelReceiptLineGridRow>.Ok(await service.InspectAsync(id,request,UserId(),ct),"SAC kontrol kararı kaydedildi."));}
    [HttpPost("{id:long}/convert")] public async Task<IActionResult> Convert(long id,ConvertSteelReceiptRequest request,CancellationToken ct)
    {
        await Require("WMS.STEEL_RECEIPT.CONVERT",ct);
        if(request.Mode==SteelReceiptConversionMode.Direct)await Require("WMS.GOODS_RECEIPT.RECEIVE",ct);
        var result=await service.ConvertAsync(id,request,UserId(),ct);
        var message=result.Mode==SteelReceiptConversionMode.Direct
            ?"Seçilen levhaların mal kabulü tamamlandı."
            :"Levhalar ortak mal kabul emrine aktarıldı.";
        return Ok(ApiResponse<ConvertSteelReceiptResult>.Ok(result,message));
    }
    [HttpPost("lines/{id:long}/place")] public async Task<IActionResult> Place(long id,PlaceSteelReceiptLineRequest request,CancellationToken ct)
    {await Require("WMS.STEEL_RECEIPT.PUTAWAY",ct);return Ok(ApiResponse<PlaceSteelReceiptLineResult>.Ok(await service.PlaceAsync(id,request,UserId(),ct),"SAC levhası nihai rafa yerleştirildi."));}
    [HttpGet("lines/{id:long}/attachments")] public async Task<IActionResult> Attachments(long id,CancellationToken ct)
    {await Require("WMS.STEEL_RECEIPT.VIEW",ct);return Ok(ApiResponse<IReadOnlyList<SteelReceiptAttachmentRow>>.Ok(await service.GetAttachmentsAsync(id,ct)));}
    [HttpPost("lines/{id:long}/attachments"),RequestSizeLimit(10_500_000)] public async Task<IActionResult> AddAttachment(long id,IFormFile file,[FromForm]string? caption,CancellationToken ct)
    {
        await Require("WMS.STEEL_RECEIPT.INSPECT",ct);
        await using var stream=file.OpenReadStream();
        var result=await service.AddAttachmentAsync(id,new SteelReceiptAttachmentUpload(stream,file.FileName,file.ContentType,file.Length),caption,UserId(),ct);
        return Ok(ApiResponse<SteelReceiptAttachmentRow>.Ok(result,"Kontrol kanıtı yüklendi."));
    }
    [HttpDelete("attachments/{id:long}")] public async Task<IActionResult> RemoveAttachment(long id,CancellationToken ct)
    {await Require("WMS.STEEL_RECEIPT.INSPECT",ct);await service.RemoveAttachmentAsync(id,UserId(),ct);return Ok(ApiResponse<bool>.Ok(true,"Kontrol kanıtı silindi."));}
    [HttpGet("attachments/{id:long}/file")] public async Task<IActionResult> DownloadAttachment(long id,CancellationToken ct)
    {await Require("WMS.STEEL_RECEIPT.VIEW",ct);var file=await service.DownloadAttachmentAsync(id,ct);return File(file.Content,file.ContentType,file.FileName);}
    private long UserId()=>long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier),out var id)?id:throw AppException.Unauthorized("Geçersiz kullanıcı oturumu.");
    private async Task Require(string code,CancellationToken ct){if(!await permissions.HasPermissionAsync(User,code,ct))throw AppException.Forbidden();}
}

public sealed class SteelVehicleAcceptanceForm
{
    public string RequestJson { get; set; } = string.Empty;
    public List<IFormFile> VehicleImages { get; set; } = [];
    public List<IFormFile> PlateImages { get; set; } = [];
    public List<long> PlateImageLineIds { get; set; } = [];
}
