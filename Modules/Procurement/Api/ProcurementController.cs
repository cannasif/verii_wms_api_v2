using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using verii_wms_api_v2.Modules.AccessControl.Application;
using verii_wms_api_v2.Modules.Procurement.Application;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.Procurement.Api;

[Authorize,ApiController,Route("api/procurement")]
public sealed class ProcurementController(IProcurementService service,IPermissionAuthorizationService permissions):ControllerBase
{
    [HttpGet("summary")]
    public async Task<IActionResult> Summary(CancellationToken ct){await Require("WMS.PROCUREMENT.VIEW",ct);return Ok(ApiResponse<ProcurementWorkspaceSummary>.Ok(await service.GetSummaryAsync(ct)));}

    [HttpPost("{documentType}/paged")]
    public async Task<IActionResult> Paged(string documentType,PagedRequest request,CancellationToken ct){await Require("WMS.PROCUREMENT.VIEW",ct);return Ok(ApiResponse<PagedResponse<ProcurementGridRow>>.Ok(await service.GetPagedAsync(documentType,request,ct)));}

    [HttpGet("{documentType}/{id:long}")]
    public async Task<IActionResult> Detail(string documentType,long id,CancellationToken ct){await Require("WMS.PROCUREMENT.VIEW",ct);return Ok(ApiResponse<ProcurementDocumentDetail>.Ok(await service.GetDetailAsync(documentType,id,ct)));}

    [HttpPost("requests")]
    public async Task<IActionResult> CreateRequest(CreateProcurementRequest request,CancellationToken ct){await Require("WMS.PROCUREMENT.REQUEST.MANAGE",ct);var id=await service.CreateRequestAsync(request,UserId(),ct);return Ok(ApiResponse<object>.Ok(new{id},"Satınalma talebi oluşturuldu."));}

    [HttpPost("requests/{id:long}/{action}")]
    public async Task<IActionResult> RequestAction(long id,string action,ProcurementTransitionRequest request,CancellationToken ct){await Require(action.Equals("approve",StringComparison.OrdinalIgnoreCase)||action.Equals("reject",StringComparison.OrdinalIgnoreCase)?"WMS.PROCUREMENT.APPROVE":"WMS.PROCUREMENT.REQUEST.MANAGE",ct);await service.TransitionRequestAsync(id,action,request,UserId(),ct);return Ok(ApiResponse<bool>.Ok(true,"Talep durumu güncellendi."));}

    [HttpPost("requests/{id:long}/convert-to-rfq")]
    public async Task<IActionResult> ConvertRequest(long id,ConvertRequestToRfqRequest request,CancellationToken ct){await Require("WMS.PROCUREMENT.RFQ.MANAGE",ct);var rfqId=await service.ConvertRequestToRfqAsync(id,request,UserId(),ct);return Ok(ApiResponse<object>.Ok(new{rfqId},"Teklif talebi oluşturuldu."));}

    [HttpPost("rfqs/{id:long}/{action}")]
    public async Task<IActionResult> RfqAction(long id,string action,ProcurementTransitionRequest request,CancellationToken ct){await Require("WMS.PROCUREMENT.RFQ.MANAGE",ct);await service.TransitionRfqAsync(id,action,request,UserId(),ct);return Ok(ApiResponse<bool>.Ok(true,"Teklif talebi durumu güncellendi."));}

    [HttpPost("rfqs/{rfqId:long}/quotes")]
    public async Task<IActionResult> CreateQuote(long rfqId,CreateSupplierQuoteRequest request,CancellationToken ct){await Require("WMS.PROCUREMENT.QUOTE.MANAGE",ct);var id=await service.CreateQuoteAsync(rfqId,request,UserId(),ct);return Ok(ApiResponse<object>.Ok(new{id},"Tedarikçi teklifi kaydedildi."));}

    [HttpPost("quotes/{id:long}/{action}")]
    public async Task<IActionResult> QuoteAction(long id,string action,ProcurementTransitionRequest request,CancellationToken ct){await Require(action.Equals("approve",StringComparison.OrdinalIgnoreCase)||action.Equals("reject",StringComparison.OrdinalIgnoreCase)?"WMS.PROCUREMENT.APPROVE":"WMS.PROCUREMENT.QUOTE.MANAGE",ct);await service.TransitionQuoteAsync(id,action,request,UserId(),ct);return Ok(ApiResponse<bool>.Ok(true,"Teklif durumu güncellendi."));}

    [HttpPost("quotes/{id:long}/convert-to-order")]
    public async Task<IActionResult> ConvertQuote(long id,CancellationToken ct){await Require("WMS.PROCUREMENT.ORDER.MANAGE",ct);var orderId=await service.ConvertQuoteToOrderAsync(id,UserId(),ct);return Ok(ApiResponse<object>.Ok(new{orderId},"Satınalma siparişi oluşturuldu."));}

    [HttpPost("orders")]
    public async Task<IActionResult> CreateOrder(CreatePurchaseOrderRequest request,CancellationToken ct){await Require("WMS.PROCUREMENT.ORDER.MANAGE",ct);var id=await service.CreateOrderAsync(request,UserId(),ct);return Ok(ApiResponse<object>.Ok(new{id},"Satınalma siparişi oluşturuldu."));}

    [HttpPost("orders/{id:long}/{action}")]
    public async Task<IActionResult> OrderAction(long id,string action,ProcurementTransitionRequest request,CancellationToken ct){await Require(action.Equals("approve",StringComparison.OrdinalIgnoreCase)||action.Equals("reject",StringComparison.OrdinalIgnoreCase)?"WMS.PROCUREMENT.APPROVE":"WMS.PROCUREMENT.ORDER.MANAGE",ct);await service.TransitionOrderAsync(id,action,request,UserId(),ct);return Ok(ApiResponse<bool>.Ok(true,"Sipariş durumu güncellendi."));}

    [HttpGet("receipt-source/open-lines")]
    public async Task<IActionResult> OpenReceiptLines([FromQuery]long? purchaseOrderId,CancellationToken ct){await Require("WMS.PROCUREMENT.VIEW",ct);return Ok(ApiResponse<IReadOnlyList<ProcurementReceiptSourceLine>>.Ok(await service.GetOpenReceiptSourceLinesAsync(purchaseOrderId,ct)));}

    private long UserId()=>long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier),out var id)?id:throw AppException.Unauthorized("Kullanıcı kimliği bulunamadı.");
    private async Task Require(string code,CancellationToken ct){if(!await permissions.HasPermissionAsync(User,code,ct))throw AppException.Forbidden("Bu işlem için yetkiniz bulunmuyor.");}
}
