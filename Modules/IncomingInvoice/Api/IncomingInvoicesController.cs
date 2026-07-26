using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using verii_wms_api_v2.Modules.AccessControl.Application;
using verii_wms_api_v2.Modules.IncomingInvoice.Application;
using verii_wms_api_v2.Modules.IncomingInvoice.Domain;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.IncomingInvoice.Api;

[Authorize, ApiController, Route("api/incoming-invoices")]
public sealed class IncomingInvoicesController(
    IIncomingInvoiceService service,
    IPermissionAuthorizationService permissions) : ControllerBase
{
    [HttpPost("import")]
    public async Task<IActionResult> Import(
        ImportIncomingInvoiceRequest request, CancellationToken ct)
    {
        await Require("WMS.INCOMING_INVOICE.IMPORT", ct);
        var result = await service.ImportAsync(request, CurrentUserId(), ct);
        return Ok(ApiResponse<IncomingInvoiceImportResult>.Ok(
            result, result.Replayed ? "Fatura daha önce arşivlenmiş." : "Fatura güvenli arşive alındı."));
    }

    [HttpPost("paged")]
    public async Task<IActionResult> GetPaged(
        [FromQuery] string branchCode, PagedRequest request, CancellationToken ct)
    {
        await Require("WMS.INCOMING_INVOICE.VIEW", ct);
        return Ok(ApiResponse<PagedResponse<IncomingInvoiceGridRow>>.Ok(
            await service.GetPagedAsync(branchCode, request, ct)));
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Get(
        long id, [FromQuery] string branchCode, CancellationToken ct)
    {
        await Require("WMS.INCOMING_INVOICE.VIEW", ct);
        return Ok(ApiResponse<IncomingInvoiceDetail>.Ok(
            await service.GetAsync(id, branchCode, ct)));
    }

    [HttpGet("{id:long}/documents/{format}")]
    public async Task<IActionResult> GetDocument(
        long id,
        IncomingInvoiceDocumentFormat format,
        [FromQuery] string branchCode,
        CancellationToken ct)
    {
        await Require("WMS.INCOMING_INVOICE.VIEW", ct);
        var file = await service.OpenDocumentAsync(id, format, branchCode, ct);
        return File(file.Content, file.ContentType, file.FileName, enableRangeProcessing: true);
    }

    [HttpPost("{id:long}/goods-receipts")]
    public async Task<IActionResult> CreateGoodsReceipt(
        long id,
        CreateIncomingInvoiceGoodsReceiptRequest request,
        CancellationToken ct)
    {
        await Require("WMS.INCOMING_INVOICE.CREATE_GOODS_RECEIPT", ct);
        var result = await service.CreateGoodsReceiptAsync(id, request, CurrentUserId(), ct);
        return Ok(ApiResponse<IncomingInvoiceGoodsReceiptResult>.Ok(
            result,
            result.Replayed
                ? "Bu istek için oluşturulmuş mal kabul emri açıldı."
                : "Fatura kalemlerinden mal kabul emri oluşturuldu."));
    }

    private long CurrentUserId() =>
        long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? id
            : throw AppException.Unauthorized("Geçersiz kullanıcı oturumu.");

    private async Task Require(string code, CancellationToken ct)
    {
        if (!await permissions.HasPermissionAsync(User, code, ct))
            throw AppException.Forbidden();
    }
}
