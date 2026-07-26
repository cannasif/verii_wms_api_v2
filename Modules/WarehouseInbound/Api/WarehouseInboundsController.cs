using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using verii_wms_api_v2.Modules.AccessControl.Application;
using verii_wms_api_v2.Modules.ErpIntegration.Application;
using verii_wms_api_v2.Modules.WarehouseInbound.Application;
using verii_wms_api_v2.Modules.WarehouseInbound.Localization;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.WarehouseInbound.Api;

[Authorize, ApiController, Route("api/warehouse-inbounds")]
public sealed class WarehouseInboundsController(
    IWarehouseInboundService service,
    IWarehouseInboundOperationsService operations,
    IWarehouseInboundTaskService tasks,
    IWarehouseInboundLabelService labels,
    IWarehouseInboundExecutionService executions,
    IWarehouseInboundLifecycleService lifecycle,
    IOperationCancellationCoordinator cancellationCoordinator,
    IPermissionAuthorizationService permissions,
    IStringLocalizer<WarehouseInboundResource> localizer) : ControllerBase
{
    [HttpPost("from-orders")]
    public async Task<IActionResult> CreateFromOrders(CreateOrderBasedWarehouseInboundRequest request, CancellationToken cancellationToken)
    {
        await Require("WMS.WAREHOUSE_INBOUND.CREATE", cancellationToken);
        var result = await service.CreateFromOrdersAsync(request, CurrentUserId(), cancellationToken);
        return Ok(ApiResponse<CreateWarehouseInboundResult>.Ok(result, localizer[WarehouseInboundMessageKeys.Created].Value));
    }

    [HttpPost("orderless")]
    public async Task<IActionResult> CreateOrderless(CreateManualWarehouseInboundRequest request, CancellationToken cancellationToken)
    {
        await Require("WMS.WAREHOUSE_INBOUND.CREATE", cancellationToken);
        return Ok(ApiResponse<ManualWarehouseInboundResult>.Ok(await operations.CreateOrderlessTaskAsync(request, CurrentUserId(), cancellationToken)));
    }

    [HttpPost("direct")]
    public async Task<IActionResult> CreateDirect(CreateManualWarehouseInboundRequest request, CancellationToken cancellationToken)
    {
        await Require("WMS.WAREHOUSE_INBOUND.RECEIVE", cancellationToken);
        return Ok(ApiResponse<ManualWarehouseInboundResult>.Ok(await operations.CreateDirectReceiptAsync(request, CurrentUserId(), cancellationToken)));
    }

    [HttpPost("paged")]
    public async Task<IActionResult> GetPaged(PagedRequest request, CancellationToken cancellationToken)
    {
        await Require("WMS.WAREHOUSE_INBOUND.VIEW", cancellationToken);
        return Ok(ApiResponse<PagedResponse<WarehouseInboundGridRow>>.Ok(await operations.GetPagedAsync(request, cancellationToken)));
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> GetDetail(long id, CancellationToken cancellationToken)
    {
        await Require("WMS.WAREHOUSE_INBOUND.VIEW", cancellationToken);
        return Ok(ApiResponse<WarehouseInboundDetail>.Ok(await operations.GetDetailAsync(id, cancellationToken)));
    }

    [HttpPost("tasks/paged")]
    public async Task<IActionResult> GetTasksPaged(PagedRequest request, CancellationToken cancellationToken)
    {
        await Require("WMS.WAREHOUSE_INBOUND.VIEW", cancellationToken);
        return Ok(ApiResponse<PagedResponse<WarehouseInboundTaskGridRow>>.Ok(await tasks.GetPagedAsync(request, CurrentUserId(), false, cancellationToken)));
    }

    [HttpPost("tasks/assigned/paged")]
    public async Task<IActionResult> GetMyAssignedTasks(PagedRequest request, CancellationToken cancellationToken)
    {
        await Require("WMS.WAREHOUSE_INBOUND.RECEIVE", cancellationToken);
        return Ok(ApiResponse<PagedResponse<WarehouseInboundTaskGridRow>>.Ok(await tasks.GetPagedAsync(request, CurrentUserId(), true, cancellationToken)));
    }

    [HttpGet("tasks/{id:long}")]
    public async Task<IActionResult> GetTaskDetail(long id, CancellationToken cancellationToken)
    {
        await RequireAny(["WMS.WAREHOUSE_INBOUND.VIEW", "WMS.WAREHOUSE_INBOUND.RECEIVE"], cancellationToken);
        return Ok(ApiResponse<WarehouseInboundTaskDetail>.Ok(await tasks.GetDetailAsync(id, CurrentUserId(), cancellationToken)));
    }

    [HttpPut("tasks/{id:long}/assignments")]
    public async Task<IActionResult> ReplaceTaskAssignments(long id, ReplaceWarehouseInboundTaskAssignmentsRequest request, CancellationToken cancellationToken)
    {
        await Require("WMS.WAREHOUSE_INBOUND.UPDATE", cancellationToken);
        return Ok(ApiResponse<WarehouseInboundTaskDetail>.Ok(await tasks.ReplaceAssignmentsAsync(id, request, CurrentUserId(), cancellationToken), "Emir atamaları güncellendi."));
    }

    [HttpPost("tasks/{id:long}/accept")]
    public async Task<IActionResult> AcceptTask(long id, CancellationToken cancellationToken)
    {
        await Require("WMS.WAREHOUSE_INBOUND.RECEIVE", cancellationToken);
        return Ok(ApiResponse<WarehouseInboundTaskDetail>.Ok(await tasks.AcceptAsync(id, CurrentUserId(), cancellationToken), "Mal kabul emri kabul edildi."));
    }

    [HttpPost("tasks/{id:long}/start")]
    public async Task<IActionResult> StartTask(long id, CancellationToken cancellationToken)
    {
        await Require("WMS.WAREHOUSE_INBOUND.RECEIVE", cancellationToken);
        return Ok(ApiResponse<WarehouseInboundTaskDetail>.Ok(await tasks.StartAsync(id, CurrentUserId(), cancellationToken), "Mal kabul emri başlatıldı."));
    }

    [HttpPost("{id:long}/label-batches")]
    public async Task<IActionResult> GenerateLabels(long id, GenerateWarehouseInboundLabelBatchRequest request, CancellationToken cancellationToken)
    {
        await Require("WMS.WAREHOUSE_INBOUND.CREATE", cancellationToken);
        return Ok(ApiResponse<WarehouseInboundLabelBatchDetail>.Ok(await labels.GenerateAsync(id, request, CurrentUserId(), cancellationToken), "Ön etiket paketi oluşturuldu."));
    }

    [HttpPost("label-batches/paged")]
    public async Task<IActionResult> GetLabelBatches(PagedRequest request, CancellationToken cancellationToken)
    {
        await RequireAny(["WMS.WAREHOUSE_INBOUND.VIEW", "WMS.WAREHOUSE_INBOUND.RECEIVE"], cancellationToken);
        return Ok(ApiResponse<PagedResponse<WarehouseInboundLabelBatchRow>>.Ok(await labels.GetPagedAsync(request, cancellationToken)));
    }

    [HttpGet("label-batches/{id:long}")]
    public async Task<IActionResult> GetLabelBatch(long id, CancellationToken cancellationToken)
    {
        await RequireAny(["WMS.WAREHOUSE_INBOUND.VIEW", "WMS.WAREHOUSE_INBOUND.RECEIVE"], cancellationToken);
        return Ok(ApiResponse<WarehouseInboundLabelBatchDetail>.Ok(await labels.GetAsync(id, cancellationToken)));
    }

    [HttpGet("{id:long}/labels")]
    public async Task<IActionResult> GetReceiptLabels(long id, [FromQuery] long? lineId, CancellationToken cancellationToken)
    {
        await RequireAny(["WMS.WAREHOUSE_INBOUND.VIEW", "WMS.WAREHOUSE_INBOUND.RECEIVE"], cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<WarehouseInboundLabelRow>>.Ok(
            await labels.GetForReceiptAsync(id, lineId, cancellationToken)));
    }

    [HttpPost("labels/printed")]
    public async Task<IActionResult> MarkLabelsPrinted(MarkWarehouseInboundLabelsPrintedRequest request, CancellationToken cancellationToken)
    {
        await Require("WMS.BARCODE_DESIGNER.PRINT", cancellationToken);
        await labels.MarkPrintedAsync(request, CurrentUserId(), cancellationToken);
        return Ok(ApiResponse<bool>.Ok(true, "Etiket yazdırma kaydı işlendi."));
    }

    [HttpPost("labels/{id:long}/void")]
    public async Task<IActionResult> VoidLabel(long id, VoidWarehouseInboundLabelRequest request, CancellationToken cancellationToken)
    {
        await Require("WMS.WAREHOUSE_INBOUND.UPDATE", cancellationToken);
        await labels.VoidAsync(id, request, CurrentUserId(), cancellationToken);
        return Ok(ApiResponse<bool>.Ok(true, "Etiket iptal edildi."));
    }

    [HttpPost("tasks/{id:long}/receive")]
    public async Task<IActionResult> ReceiveTaskScan(long id, ReceiveWarehouseInboundTaskRequest request, CancellationToken cancellationToken)
    {
        await Require("WMS.WAREHOUSE_INBOUND.RECEIVE", cancellationToken);
        return Ok(ApiResponse<ReceiveWarehouseInboundTaskResult>.Ok(await executions.ReceiveAsync(id, request, CurrentUserId(), cancellationToken), "Barkod doğrulandı ve kabul işlendi."));
    }

    private long CurrentUserId() => long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : throw AppException.Unauthorized("Geçersiz kullanıcı oturumu.");
    [HttpPost("{id:long}/approve")]
    public async Task<IActionResult> Approve(long id, WarehouseInboundTransitionRequest request, CancellationToken cancellationToken)
    {
        await Require("WMS.WAREHOUSE_INBOUND.RELEASE", cancellationToken);
        return Ok(ApiResponse<WarehouseInboundLifecycleResult>.Ok(
            await lifecycle.ApproveAsync(id, request, CurrentUserId(), cancellationToken),
            "Mal kabul onaylandı."));
    }

    [HttpPost("{id:long}/short-close")]
    public async Task<IActionResult> ShortClose(long id, ShortCloseWarehouseInboundRequest request, CancellationToken cancellationToken)
    {
        await Require("WMS.WAREHOUSE_INBOUND.COMPLETE", cancellationToken);
        return Ok(ApiResponse<WarehouseInboundLifecycleResult>.Ok(
            await lifecycle.ShortCloseAsync(id, request, CurrentUserId(), cancellationToken),
            "Eksik miktarlar kısa kapatıldı."));
    }

    [HttpPost("{id:long}/putaway")]
    public async Task<IActionResult> Putaway(long id, PutawayWarehouseInboundRequest request, CancellationToken cancellationToken)
    {
        await Require("WMS.WAREHOUSE_INBOUND.COMPLETE", cancellationToken);
        return Ok(ApiResponse<WarehouseInboundLifecycleResult>.Ok(
            await lifecycle.PutawayAsync(id, request, CurrentUserId(), cancellationToken),
            "Mal kabul raf yerleştirmesi işlendi."));
    }

    [HttpPost("{id:long}/cancel")]
    public async Task<IActionResult> Cancel(long id, WarehouseInboundTransitionRequest request, CancellationToken cancellationToken)
    {
        await Require("WMS.WAREHOUSE_INBOUND.CANCEL", cancellationToken);
        return Ok(ApiResponse<OperationCancellationResult>.Ok(
            await cancellationCoordinator.CancelWarehouseInboundAsync(
                id, request, CurrentUserId(), cancellationToken),
            "Ambar giriş güvenli iptal sürecinde işlendi."));
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
