using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using verii_wms_api_v2.Modules.AccessControl.Application;
using verii_wms_api_v2.Modules.Procurement.Application;
using verii_wms_api_v2.Modules.Identity.Infrastructure;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.Procurement.Api;

[Authorize,ApiController,Route("api/procurement")]
public sealed class ProcurementController(IProcurementService service,IProcurementPolicyService policy,IPermissionAuthorizationService permissions):ControllerBase
{
    [HttpGet("policy")]
    public async Task<IActionResult> Policy(CancellationToken ct){await Require("WMS.PROCUREMENT.VIEW",ct);return Ok(ApiResponse<ProcurementPolicyDto>.Ok(await policy.GetAsync(BranchCode(),ct)));}

    [HttpPut("policy")]
    public async Task<IActionResult> UpdatePolicy(UpdateProcurementPolicyRequest request,CancellationToken ct){await Require("WMS.PROCUREMENT.APPROVE",ct);return Ok(ApiResponse<ProcurementPolicyDto>.Ok(await policy.UpdateAsync(BranchCode(),request,UserId(),ct),"Satınalma politikası kaydedildi."));}

    [HttpGet("summary")]
    public async Task<IActionResult> Summary(CancellationToken ct){await Require("WMS.PROCUREMENT.VIEW",ct);return Ok(ApiResponse<ProcurementWorkspaceSummary>.Ok(await service.GetSummaryAsync(ct)));}

    [HttpGet("next-document-no/{documentType}")]
    public async Task<IActionResult> NextDocumentNo(string documentType,CancellationToken ct){await Require("WMS.PROCUREMENT.VIEW",ct);return Ok(ApiResponse<ProcurementNextDocumentNo>.Ok(await service.PeekNextDocumentNoAsync(documentType,ct)));}

    [HttpPost("{documentType}/paged")]
    public async Task<IActionResult> Paged(string documentType,PagedRequest request,CancellationToken ct){await Require("WMS.PROCUREMENT.VIEW",ct);return Ok(ApiResponse<PagedResponse<ProcurementGridRow>>.Ok(await service.GetPagedAsync(documentType,request,ct)));}

    [HttpGet("{documentType}/{id:long}")]
    public async Task<IActionResult> Detail(string documentType,long id,CancellationToken ct){await Require("WMS.PROCUREMENT.VIEW",ct);return Ok(ApiResponse<ProcurementDocumentDetail>.Ok(await service.GetDetailAsync(documentType,id,ct)));}

    [HttpPost("requests")]
    public async Task<IActionResult> CreateRequest(CreateProcurementRequest request,CancellationToken ct){await Require("WMS.PROCUREMENT.REQUEST.MANAGE",ct);var id=await service.CreateRequestAsync(request,UserId(),ct);return Ok(ApiResponse<object>.Ok(new{id},"Satınalma talebi oluşturuldu."));}

    [HttpPost("requests/{id:long}/{transition}")]
    public async Task<IActionResult> RequestAction(long id,string transition,ProcurementTransitionRequest request,CancellationToken ct){await Require(transition.Equals("approve",StringComparison.OrdinalIgnoreCase)||transition.Equals("reject",StringComparison.OrdinalIgnoreCase)?"WMS.PROCUREMENT.APPROVE":"WMS.PROCUREMENT.REQUEST.MANAGE",ct);await service.TransitionRequestAsync(id,transition,request,UserId(),ct);return Ok(ApiResponse<bool>.Ok(true,"Talep durumu güncellendi."));}

    [HttpPost("requests/{id:long}/convert-to-rfq")]
    public async Task<IActionResult> ConvertRequest(long id,ConvertRequestToRfqRequest request,CancellationToken ct)
    {
        // Quote managers may prepare the hidden RFQ step when entering supplier quotes from a request.
        await RequireAny(["WMS.PROCUREMENT.RFQ.MANAGE","WMS.PROCUREMENT.QUOTE.MANAGE"],ct);
        var rfqId=await service.ConvertRequestToRfqAsync(id,request,UserId(),ct);
        return Ok(ApiResponse<object>.Ok(new{rfqId},"Teklif talebi oluşturuldu."));
    }

    [HttpPost("rfqs/{id:long}/{transition}")]
    public async Task<IActionResult> RfqAction(long id,string transition,ProcurementTransitionRequest request,CancellationToken ct)
    {
        var permission=transition.Equals("send",StringComparison.OrdinalIgnoreCase)
            ? null
            : "WMS.PROCUREMENT.RFQ.MANAGE";
        if(permission is null)await RequireAny(["WMS.PROCUREMENT.RFQ.MANAGE","WMS.PROCUREMENT.QUOTE.MANAGE"],ct);
        else await Require(permission,ct);
        await service.TransitionRfqAsync(id,transition,request,UserId(),ct);
        return Ok(ApiResponse<bool>.Ok(true,"Teklif talebi durumu güncellendi."));
    }

    [HttpPost("rfqs/{rfqId:long}/quotes")]
    public async Task<IActionResult> CreateQuote(long rfqId,CreateSupplierQuoteRequest request,CancellationToken ct){await Require("WMS.PROCUREMENT.QUOTE.MANAGE",ct);var id=await service.CreateQuoteAsync(rfqId,request,UserId(),ct);return Ok(ApiResponse<object>.Ok(new{id},"Tedarikçi teklifi kaydedildi."));}

    [HttpPost("rfqs/{rfqId:long}/invitations")]
    public async Task<IActionResult> SendInvitation(long rfqId,SendProcurementInvitationRequest request,CancellationToken ct){await Require("WMS.PROCUREMENT.RFQ.MANAGE",ct);return Ok(ApiResponse<ProcurementInvitationResult>.Ok(await service.SendInvitationAsync(rfqId,request,UserId(),ct),"Tedarikçiye güvenli teklif bağlantısı gönderildi."));}

    [HttpPost("rfqs/{rfqId:long}/invitations/{supplierId:long}/revoke")]
    public async Task<IActionResult> RevokeInvitation(long rfqId,long supplierId,CancellationToken ct){await Require("WMS.PROCUREMENT.RFQ.MANAGE",ct);await service.RevokeInvitationAsync(rfqId,supplierId,UserId(),ct);return Ok(ApiResponse<bool>.Ok(true,"Teklif bağlantısı iptal edildi."));}

    [HttpPost("quotes/{id:long}/{transition}")]
    public async Task<IActionResult> QuoteAction(long id,string transition,ProcurementTransitionRequest request,CancellationToken ct){await Require(transition.Equals("approve",StringComparison.OrdinalIgnoreCase)||transition.Equals("reject",StringComparison.OrdinalIgnoreCase)?"WMS.PROCUREMENT.APPROVE":"WMS.PROCUREMENT.QUOTE.MANAGE",ct);await service.TransitionQuoteAsync(id,transition,request,UserId(),ct);return Ok(ApiResponse<bool>.Ok(true,"Teklif durumu güncellendi."));}

    [HttpPost("quotes/{id:long}/request-revision")]
    public async Task<IActionResult> RequestRevision(long id,ProcurementTransitionRequest request,CancellationToken ct){await Require("WMS.PROCUREMENT.QUOTE.MANAGE",ct);await service.RequestQuoteRevisionAsync(id,request.Note,UserId(),ct);return Ok(ApiResponse<bool>.Ok(true,"Tedarikçiden teklif revizyonu istendi."));}

    [HttpPost("quotes/{id:long}/convert-to-order")]
    public async Task<IActionResult> ConvertQuote(long id,[FromBody]ConvertQuoteToOrderRequest? request,CancellationToken ct){await Require("WMS.PROCUREMENT.ORDER.MANAGE",ct);var orderId=await service.ConvertQuoteToOrderAsync(id,request??new ConvertQuoteToOrderRequest(),UserId(),ct);return Ok(ApiResponse<object>.Ok(new{orderId},"Satınalma siparişi oluşturuldu."));}

    [HttpPost("orders")]
    public async Task<IActionResult> CreateOrder(CreatePurchaseOrderRequest request,CancellationToken ct){await Require("WMS.PROCUREMENT.ORDER.MANAGE",ct);var id=await service.CreateOrderAsync(request,UserId(),ct);return Ok(ApiResponse<object>.Ok(new{id},"Satınalma siparişi oluşturuldu."));}

    [HttpPost("orders/{id:long}/{transition}")]
    public async Task<IActionResult> OrderAction(long id,string transition,ProcurementTransitionRequest request,CancellationToken ct){await Require(transition.Equals("approve",StringComparison.OrdinalIgnoreCase)||transition.Equals("reject",StringComparison.OrdinalIgnoreCase)?"WMS.PROCUREMENT.APPROVE":"WMS.PROCUREMENT.ORDER.MANAGE",ct);await service.TransitionOrderAsync(id,transition,request,UserId(),ct);return Ok(ApiResponse<bool>.Ok(true,"Sipariş durumu güncellendi."));}

    [HttpGet("receipt-source/open-lines")]
    public async Task<IActionResult> OpenReceiptLines([FromQuery]long? purchaseOrderId,CancellationToken ct){await Require("WMS.PROCUREMENT.VIEW",ct);return Ok(ApiResponse<IReadOnlyList<ProcurementReceiptSourceLine>>.Ok(await service.GetOpenReceiptSourceLinesAsync(purchaseOrderId,ct)));}

    [HttpGet("attachments")]
    public async Task<IActionResult> ListAttachments([FromQuery]string ownerType,[FromQuery]long ownerId,CancellationToken ct)
    {
        await Require("WMS.PROCUREMENT.VIEW",ct);
        return Ok(ApiResponse<IReadOnlyList<ProcurementAttachmentRow>>.Ok(await service.ListAttachmentsAsync(ownerType,ownerId,ct)));
    }

    [HttpPost("attachments"),RequestSizeLimit(10_500_000)]
    public async Task<IActionResult> AddAttachment([FromQuery]string ownerType,[FromQuery]long ownerId,IFormFile file,[FromForm]string? caption,CancellationToken ct)
    {
        await RequireAttachmentManage(ownerType,ct);
        if(file is null||file.Length<=0)throw AppException.BadRequest("Yüklenecek dosya bulunamadı.");
        await using var stream=file.OpenReadStream();
        var result=await service.AddAttachmentAsync(ownerType,ownerId,new ProcurementAttachmentUpload(stream,file.FileName,file.ContentType,file.Length),caption,UserId(),ct);
        return Ok(ApiResponse<ProcurementAttachmentRow>.Ok(result,"Dosya eklendi."));
    }

    [HttpGet("attachments/{id:long}/file")]
    public async Task<IActionResult> DownloadAttachment(long id,CancellationToken ct)
    {
        await Require("WMS.PROCUREMENT.VIEW",ct);
        var file=await service.DownloadAttachmentAsync(id,ct);
        return File(file.Content,file.ContentType,file.FileName);
    }

    [HttpDelete("attachments/{id:long}")]
    public async Task<IActionResult> RemoveAttachment(long id,CancellationToken ct)
    {
        await RequireAny(["WMS.PROCUREMENT.REQUEST.MANAGE","WMS.PROCUREMENT.QUOTE.MANAGE"],ct);
        await service.RemoveAttachmentAsync(id,UserId(),ct);
        return Ok(ApiResponse<bool>.Ok(true,"Dosya silindi."));
    }

    private long UserId()=>long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier),out var id)?id:throw AppException.Unauthorized("Kullanıcı kimliği bulunamadı.");
    private string BranchCode()=>User.FindFirstValue(JwtTokenIssuer.BranchCodeClaim)?.Trim() is {Length:>0} branch?branch:"0";
    private async Task Require(string code,CancellationToken ct){if(!await permissions.HasPermissionAsync(User,code,ct))throw AppException.Forbidden("Bu işlem için yetkiniz bulunmuyor.");}
    private async Task RequireAny(IEnumerable<string> codes,CancellationToken ct)
    {
        foreach(var code in codes)
            if(await permissions.HasPermissionAsync(User,code,ct))return;
        throw AppException.Forbidden("Bu işlem için yetkiniz bulunmuyor.");
    }
    private Task RequireAttachmentManage(string ownerType,CancellationToken ct)=>
        ownerType.Trim().ToLowerInvariant() switch
        {
            "request" or "request-line" or "requestline"=>Require("WMS.PROCUREMENT.REQUEST.MANAGE",ct),
            "quote" or "quote-line" or "quoteline"=>Require("WMS.PROCUREMENT.QUOTE.MANAGE",ct),
            _=>throw AppException.BadRequest("Geçersiz ek dosya sahibi türü.")
        };
}
