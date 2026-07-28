using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using verii_wms_api_v2.Modules.AccessControl.Application;
using verii_wms_api_v2.Modules.ErpIntegration.Application;
using verii_wms_api_v2.Modules.GoodsReceipt.Application;
using verii_wms_api_v2.Modules.GoodsReceipt.Localization;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.GoodsReceipt.Api;

[Authorize, ApiController, Route("api/goods-receipts")]
public sealed class GoodsReceiptsController(
    IGoodsReceiptService service,
    IGoodsReceiptOperationsService operations,
    IGoodsReceiptTaskService tasks,
    IGoodsReceiptLabelService labels,
    IGoodsReceiptExecutionService executions,
    IGoodsReceiptLifecycleService lifecycle,
    IOperationCancellationCoordinator cancellationCoordinator,
    IGoodsReceiptRoutingService routing,
    IPermissionAuthorizationService permissions,
    IStringLocalizer<GoodsReceiptResource> localizer) : ControllerBase
{
    [HttpPost("from-orders")]
    public async Task<IActionResult> CreateFromOrders(CreateOrderBasedGoodsReceiptRequest request, CancellationToken cancellationToken)
    {
        await Require("WMS.GOODS_RECEIPT.CREATE", cancellationToken);
        var result = await service.CreateFromOrdersAsync(request, CurrentUserId(), cancellationToken);
        return Ok(ApiResponse<CreateGoodsReceiptResult>.Ok(result, localizer[GoodsReceiptMessageKeys.Created].Value));
    }

    [HttpPost("orderless")]
    public async Task<IActionResult> CreateOrderless(CreateManualGoodsReceiptRequest request, CancellationToken cancellationToken)
    {
        await Require("WMS.GOODS_RECEIPT.CREATE", cancellationToken);
        return Ok(ApiResponse<ManualGoodsReceiptResult>.Ok(await operations.CreateOrderlessTaskAsync(request, CurrentUserId(), cancellationToken)));
    }

    [HttpPost("direct")]
    public async Task<IActionResult> CreateDirect(CreateManualGoodsReceiptRequest request, CancellationToken cancellationToken)
    {
        await Require("WMS.GOODS_RECEIPT.RECEIVE", cancellationToken);
        return Ok(ApiResponse<ManualGoodsReceiptResult>.Ok(await operations.CreateDirectReceiptAsync(request, CurrentUserId(), cancellationToken)));
    }

    [HttpPost("paged")]
    public async Task<IActionResult> GetPaged(PagedRequest request, CancellationToken cancellationToken)
    {
        await Require("WMS.GOODS_RECEIPT.VIEW", cancellationToken);
        return Ok(ApiResponse<PagedResponse<GoodsReceiptGridRow>>.Ok(await operations.GetPagedAsync(request, cancellationToken)));
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> GetDetail(long id, CancellationToken cancellationToken)
    {
        await Require("WMS.GOODS_RECEIPT.VIEW", cancellationToken);
        return Ok(ApiResponse<GoodsReceiptDetail>.Ok(await operations.GetDetailAsync(id, cancellationToken)));
    }

    [HttpPost("{id:long}/routes/warehouse-transfer")]
    public async Task<IActionResult> CreateWarehouseTransfer(
        long id,
        CreateGoodsReceiptTransferRequest request,
        CancellationToken cancellationToken)
    {
        await Require("WMS.WAREHOUSE_TRANSFER.CREATE", cancellationToken);
        return Ok(ApiResponse<GoodsReceiptRoutingResult>.Ok(
            await routing.CreateTransferAsync(id, request, CurrentUserId(), cancellationToken),
            "Depolar arası transfer taslağı mal kabulden oluşturuldu."));
    }

    [HttpPost("{id:long}/routes/warehouse-outbound")]
    public async Task<IActionResult> CreateWarehouseOutbound(
        long id,
        CreateGoodsReceiptOutboundRequest request,
        CancellationToken cancellationToken)
    {
        await Require("WMS.WAREHOUSE_OUTBOUND.CREATE", cancellationToken);
        return Ok(ApiResponse<GoodsReceiptRoutingResult>.Ok(
            await routing.CreateOutboundAsync(id, request, CurrentUserId(), cancellationToken),
            "Ambar çıkış taslağı mal kabulden oluşturuldu."));
    }

    [HttpPost("{id:long}/routes/split")]
    public async Task<IActionResult> CreateSplitRouting(
        long id,
        CreateGoodsReceiptSplitRoutingRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Transfer is not null)
            await Require("WMS.WAREHOUSE_TRANSFER.CREATE", cancellationToken);
        if (request.Outbound is not null)
            await Require("WMS.WAREHOUSE_OUTBOUND.CREATE", cancellationToken);
        return Ok(ApiResponse<GoodsReceiptSplitRoutingResult>.Ok(
            await routing.CreateSplitAsync(id, request, CurrentUserId(), cancellationToken),
            "Mal kabul kalemleri transfer ve ambar çıkış belgelerine atomik olarak dağıtıldı."));
    }

    [HttpPost("tasks/paged")]
    public async Task<IActionResult> GetTasksPaged(PagedRequest request, CancellationToken cancellationToken)
    {
        await Require("WMS.GOODS_RECEIPT.VIEW", cancellationToken);
        return Ok(ApiResponse<PagedResponse<GoodsReceiptTaskGridRow>>.Ok(await tasks.GetPagedAsync(request, CurrentUserId(), false, cancellationToken)));
    }

    [HttpPost("tasks/assigned/paged")]
    public async Task<IActionResult> GetMyAssignedTasks(PagedRequest request, CancellationToken cancellationToken)
    {
        await Require("WMS.GOODS_RECEIPT.RECEIVE", cancellationToken);
        return Ok(ApiResponse<PagedResponse<GoodsReceiptTaskGridRow>>.Ok(await tasks.GetPagedAsync(request, CurrentUserId(), true, cancellationToken)));
    }

    [HttpGet("tasks/{id:long}")]
    public async Task<IActionResult> GetTaskDetail(long id, CancellationToken cancellationToken)
    {
        await RequireAny(["WMS.GOODS_RECEIPT.VIEW", "WMS.GOODS_RECEIPT.RECEIVE"], cancellationToken);
        return Ok(ApiResponse<GoodsReceiptTaskDetail>.Ok(await tasks.GetDetailAsync(id, CurrentUserId(), cancellationToken)));
    }

    [HttpPut("tasks/{id:long}/assignments")]
    public async Task<IActionResult> ReplaceTaskAssignments(long id, ReplaceGoodsReceiptTaskAssignmentsRequest request, CancellationToken cancellationToken)
    {
        await Require("WMS.GOODS_RECEIPT.UPDATE", cancellationToken);
        return Ok(ApiResponse<GoodsReceiptTaskDetail>.Ok(await tasks.ReplaceAssignmentsAsync(id, request, CurrentUserId(), cancellationToken), "Emir atamaları güncellendi."));
    }

    [HttpPost("tasks/{id:long}/accept")]
    public async Task<IActionResult> AcceptTask(long id, CancellationToken cancellationToken)
    {
        await Require("WMS.GOODS_RECEIPT.RECEIVE", cancellationToken);
        return Ok(ApiResponse<GoodsReceiptTaskDetail>.Ok(await tasks.AcceptAsync(id, CurrentUserId(), cancellationToken), "Mal kabul emri kabul edildi."));
    }

    [HttpPost("tasks/{id:long}/start")]
    public async Task<IActionResult> StartTask(long id, CancellationToken cancellationToken)
    {
        await Require("WMS.GOODS_RECEIPT.RECEIVE", cancellationToken);
        return Ok(ApiResponse<GoodsReceiptTaskDetail>.Ok(await tasks.StartAsync(id, CurrentUserId(), cancellationToken), "Mal kabul emri başlatıldı."));
    }

    [HttpPost("{id:long}/label-batches")]
    public async Task<IActionResult> GenerateLabels(long id, GenerateGoodsReceiptLabelBatchRequest request, CancellationToken cancellationToken)
    {
        var canManageAllLabels = await permissions.HasPermissionAsync(User, "WMS.GOODS_RECEIPT.CREATE", cancellationToken);
        if (!canManageAllLabels)
            await Require("WMS.GOODS_RECEIPT.RECEIVE", cancellationToken);
        return Ok(ApiResponse<GoodsReceiptLabelBatchDetail>.Ok(
            await labels.GenerateAsync(id, request, CurrentUserId(),
                restrictToActorAssignment: !canManageAllLabels, cancellationToken),
            "Ön etiket paketi oluşturuldu."));
    }

    [HttpPost("label-batches/paged")]
    public async Task<IActionResult> GetLabelBatches(PagedRequest request, CancellationToken cancellationToken)
    {
        await RequireAny(["WMS.GOODS_RECEIPT.VIEW", "WMS.GOODS_RECEIPT.RECEIVE"], cancellationToken);
        return Ok(ApiResponse<PagedResponse<GoodsReceiptLabelBatchRow>>.Ok(await labels.GetPagedAsync(request, cancellationToken)));
    }

    [HttpGet("label-batches/{id:long}")]
    public async Task<IActionResult> GetLabelBatch(long id, CancellationToken cancellationToken)
    {
        await RequireAny(["WMS.GOODS_RECEIPT.VIEW", "WMS.GOODS_RECEIPT.RECEIVE"], cancellationToken);
        return Ok(ApiResponse<GoodsReceiptLabelBatchDetail>.Ok(await labels.GetAsync(id, cancellationToken)));
    }

    [HttpGet("{id:long}/labels")]
    public async Task<IActionResult> GetReceiptLabels(long id, [FromQuery] long? lineId, CancellationToken cancellationToken)
    {
        await RequireAny(["WMS.GOODS_RECEIPT.VIEW", "WMS.GOODS_RECEIPT.RECEIVE"], cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<GoodsReceiptLabelRow>>.Ok(
            await labels.GetForReceiptAsync(id, lineId, cancellationToken)));
    }

    [HttpPost("labels/printed")]
    public async Task<IActionResult> MarkLabelsPrinted(MarkGoodsReceiptLabelsPrintedRequest request, CancellationToken cancellationToken)
    {
        var canPrintAllLabels = await permissions.HasPermissionAsync(User, "WMS.BARCODE_DESIGNER.PRINT", cancellationToken);
        if (!canPrintAllLabels)
            await Require("WMS.GOODS_RECEIPT.RECEIVE", cancellationToken);
        await labels.MarkPrintedAsync(request, CurrentUserId(),
            restrictToActorAssignment: !canPrintAllLabels, cancellationToken);
        return Ok(ApiResponse<bool>.Ok(true, "Etiket yazdırma kaydı işlendi."));
    }

    [HttpPost("labels/{id:long}/void")]
    public async Task<IActionResult> VoidLabel(long id, VoidGoodsReceiptLabelRequest request, CancellationToken cancellationToken)
    {
        await Require("WMS.GOODS_RECEIPT.UPDATE", cancellationToken);
        await labels.VoidAsync(id, request, CurrentUserId(), cancellationToken);
        return Ok(ApiResponse<bool>.Ok(true, "Etiket iptal edildi."));
    }

    [HttpPost("tasks/{id:long}/receive")]
    public async Task<IActionResult> ReceiveTaskScan(long id, ReceiveGoodsReceiptTaskRequest request, CancellationToken cancellationToken)
    {
        await Require("WMS.GOODS_RECEIPT.RECEIVE", cancellationToken);
        return Ok(ApiResponse<ReceiveGoodsReceiptTaskResult>.Ok(await executions.ReceiveAsync(id, request, CurrentUserId(), cancellationToken), "Barkod doğrulandı ve kabul işlendi."));
    }

    private long CurrentUserId() => long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : throw AppException.Unauthorized("Geçersiz kullanıcı oturumu.");
    [HttpPost("{id:long}/approve")]
    public async Task<IActionResult> Approve(long id, GoodsReceiptTransitionRequest request, CancellationToken cancellationToken)
    {
        await Require("WMS.GOODS_RECEIPT.RELEASE", cancellationToken);
        return Ok(ApiResponse<GoodsReceiptLifecycleResult>.Ok(
            await lifecycle.ApproveAsync(id, request, CurrentUserId(), cancellationToken),
            "Mal kabul onaylandı."));
    }

    [HttpPost("{id:long}/short-close")]
    public async Task<IActionResult> ShortClose(long id, ShortCloseGoodsReceiptRequest request, CancellationToken cancellationToken)
    {
        await Require("WMS.GOODS_RECEIPT.COMPLETE", cancellationToken);
        return Ok(ApiResponse<GoodsReceiptLifecycleResult>.Ok(
            await lifecycle.ShortCloseAsync(id, request, CurrentUserId(), cancellationToken),
            "Eksik miktarlar kısa kapatıldı."));
    }

    [HttpPost("{id:long}/putaway")]
    public async Task<IActionResult> Putaway(long id, PutawayGoodsReceiptRequest request, CancellationToken cancellationToken)
    {
        await Require("WMS.GOODS_RECEIPT.COMPLETE", cancellationToken);
        return Ok(ApiResponse<GoodsReceiptLifecycleResult>.Ok(
            await lifecycle.PutawayAsync(id, request, CurrentUserId(), cancellationToken),
            "Mal kabul raf yerleştirmesi işlendi."));
    }

    [HttpPost("{id:long}/cancel")]
    public async Task<IActionResult> Cancel(long id, GoodsReceiptTransitionRequest request, CancellationToken cancellationToken)
    {
        await Require("WMS.GOODS_RECEIPT.CANCEL", cancellationToken);
        return Ok(ApiResponse<OperationCancellationResult>.Ok(
            await cancellationCoordinator.CancelGoodsReceiptAsync(
                id, request, CurrentUserId(), cancellationToken),
            "Mal kabul güvenli iptal sürecinde işlendi."));
    }

    private async Task Require(string code, CancellationToken cancellationToken)
    {
        if (!await permissions.HasPermissionAsync(User, code, cancellationToken)) throw AppException.Forbidden();
    }
    private async Task RequireAny(IEnumerable<string> codes, CancellationToken cancellationToken)
    {
        foreach (var code in codes)
            if (await permissions.HasPermissionAsync(User, code, cancellationToken)) return;
        throw AppException.Forbidden();
    }
}
