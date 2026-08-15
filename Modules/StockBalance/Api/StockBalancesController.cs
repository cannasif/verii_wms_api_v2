using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using verii_wms_api_v2.Modules.AccessControl.Application;
using verii_wms_api_v2.Modules.Audit.Application;
using verii_wms_api_v2.Modules.StockBalance.Application;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.StockBalance.Api;

[Authorize, ApiController, Route("api/stock-balances")]
public sealed class StockBalancesController(IStockBalanceService service, IOpeningBalanceImportService openingImport,
    IPermissionAuthorizationService permissions, IAuditLogWriter audit) : ControllerBase
{
    [HttpGet("opening-import/template")]
    public async Task<IActionResult> DownloadOpeningTemplate([FromQuery] string branchCode = "0",
        CancellationToken ct = default)
    {
        await RequireAsync("WMS.STOCK_MOVEMENTS.POST", ct);
        var bytes = await openingImport.CreateTemplateAsync(branchCode, ct);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"wms-v2-ilk-raf-bakiyesi-{DateTime.UtcNow:yyyyMMdd}.xlsx");
    }

    [HttpPost("opening-import"), RequestSizeLimit(OpeningBalanceImportService.MaxFileSize)]
    public async Task<IActionResult> ImportOpeningBalance(IFormFile? file,
        [FromQuery] string branchCode, [FromQuery] string idempotencyKey, CancellationToken ct)
    {
        await RequireAsync("WMS.STOCK_MOVEMENTS.POST", ct);
        ValidateXlsx(file, OpeningBalanceImportService.MaxFileSize);
        await using var stream = file!.OpenReadStream();
        var result = await openingImport.ImportAsync(stream, branchCode, idempotencyKey, ct);
        return Ok(ApiResponse<OpeningBalanceImportResult>.Ok(result,
            result.IsReplay ? "İlk bakiye aktarımının önceki sonucu döndürüldü." : $"{result.TotalRows} ilk bakiye satırı kaydedildi."));
    }

    [HttpPost("resolve-serial-locations")]
    public async Task<IActionResult> ResolveSerialLocations(ResolveSerialLocationsRequest request, CancellationToken ct)
    {
        await RequireAsync("WMS.LOCATIONS.VIEW", ct);
        return Ok(ApiResponse<IReadOnlyList<SerialLocationMatchDto>>.Ok(await service.ResolveSerialLocationsAsync(request, ct)));
    }

    [HttpGet("stocks/{stockId:long}/locations")]
    public async Task<IActionResult> ResolveStockLocations(long stockId, [FromQuery] long warehouseId,
        [FromQuery] string branchCode, [FromQuery] long? yapCodeId, [FromQuery] string? excludeLocationIds,
        [FromQuery] bool includeOnHand = false, CancellationToken ct = default)
    {
        await RequireAsync("WMS.LOCATIONS.VIEW", ct);
        var excluded = ParseLocationIds(excludeLocationIds);
        return Ok(ApiResponse<IReadOnlyList<StockLocationBalanceDto>>.Ok(
            await service.ResolveStockLocationsAsync(branchCode, warehouseId, stockId, yapCodeId, excluded, includeOnHand, ct)));
    }

    [HttpPost("locations/paged")]
    public async Task<IActionResult> Locations(PagedRequest request, CancellationToken ct)
    {
        await RequireAsync("WMS.STOCK_BALANCES.VIEW", ct);
        return Ok(ApiResponse<PagedResponse<LocationBalanceRow>>.Ok(await service.GetLocationBalancesAsync(request, ct)));
    }

    [HttpPost("warehouses/paged")]
    public async Task<IActionResult> Warehouses(PagedRequest request, CancellationToken ct)
    {
        await RequireAsync("WMS.STOCK_BALANCES.VIEW", ct);
        return Ok(ApiResponse<PagedResponse<WarehouseBalanceRow>>.Ok(await service.GetWarehouseBalancesAsync(request, ct)));
    }

    [HttpPost("serials/paged")]
    public async Task<IActionResult> Serials(PagedRequest request, CancellationToken ct)
    {
        await RequireAsync("WMS.STOCK_BALANCES.VIEW", ct);
        return Ok(ApiResponse<PagedResponse<SerialBalanceRow>>.Ok(await service.GetSerialBalancesAsync(request, ct)));
    }

    [HttpPost("serials/{id:long}/movements/paged")]
    public async Task<IActionResult> SerialMovements(long id, PagedRequest request, CancellationToken ct)
    {
        await RequireAsync("WMS.STOCK_BALANCES.VIEW", ct);
        return Ok(ApiResponse<PagedResponse<SerialMovementHistoryRow>>.Ok(await service.GetSerialMovementHistoryAsync(id, request, ct)));
    }

    [HttpGet("warehouses/{id:long}/drill-down")]
    public async Task<IActionResult> DrillDown(long id, CancellationToken ct)
    {
        await RequireAsync("WMS.STOCK_BALANCES.VIEW", ct);
        return Ok(ApiResponse<StockBalanceDrillDown>.Ok(await service.GetDrillDownAsync(id, ct)));
    }

    [HttpGet("warehouses/{warehouseId:long}/inventory")]
    public async Task<IActionResult> WarehouseInventory(long warehouseId, CancellationToken ct)
    {
        await RequireAsync("WMS.STOCK_BALANCES.VIEW", ct);
        return Ok(ApiResponse<WarehouseInventoryLookup>.Ok(await service.GetWarehouseInventoryLookupAsync(warehouseId, ct)));
    }

    [HttpGet("locations/{locationId:long}/inventory")]
    public async Task<IActionResult> LocationInventory(long locationId, CancellationToken ct)
    {
        await RequireAsync("WMS.STOCK_BALANCES.VIEW", ct);
        return Ok(ApiResponse<LocationInventoryLookup>.Ok(await service.GetLocationInventoryLookupAsync(locationId, ct)));
    }

    [HttpGet("serials/{id:long}")]
    public async Task<IActionResult> SerialInventory(long id, CancellationToken ct)
    {
        await RequireAsync("WMS.STOCK_BALANCES.VIEW", ct);
        return Ok(ApiResponse<SerialInventoryLookup>.Ok(await service.GetSerialInventoryLookupAsync(id, ct)));
    }

    [HttpGet("lots/inventory")]
    public async Task<IActionResult> LotInventory([FromQuery] string? lotNo, CancellationToken ct)
    {
        await RequireAsync("WMS.STOCK_BALANCES.VIEW", ct);
        return Ok(ApiResponse<LotInventoryLookup>.Ok(await service.GetLotInventoryLookupAsync(lotNo, ct)));
    }

    [HttpGet("reconciliation/summary")]
    public async Task<IActionResult> ReconciliationSummary(CancellationToken ct)
    {
        await RequireAsync("WMS.STOCK_BALANCES.RECONCILE", ct);
        return Ok(ApiResponse<ReconciliationSummary>.Ok(await service.GetReconciliationSummaryAsync(ct)));
    }

    [HttpPost("reconciliation/issues/paged")]
    public async Task<IActionResult> ReconciliationIssues(PagedRequest request, CancellationToken ct)
    {
        await RequireAsync("WMS.STOCK_BALANCES.RECONCILE", ct);
        return Ok(ApiResponse<PagedResponse<ReconciliationIssue>>.Ok(await service.GetReconciliationIssuesAsync(request, ct)));
    }

    [HttpPost("rebuild")]
    public async Task<IActionResult> Rebuild(CancellationToken ct)
    {
        await RequireAsync("WMS.STOCK_BALANCES.RECONCILE", ct);
        var result = await service.RebuildAsync(ct);
        await audit.WriteAsync(new AuditLogWriteEntry("stock-balance.rebuild", "StockBalanceProjection", "stock-balance-v1", "Succeeded", "stock-balance",
            NewValues: result, ChangedFields: ["LocationProjection", "WarehouseProjection"]), ct);
        return Ok(ApiResponse<ProjectionRebuildResult>.Ok(result, "Stok bakiyesi projection yeniden oluşturuldu."));
    }

    private async Task RequireAsync(string permission, CancellationToken ct)
    {
        if (!await permissions.HasPermissionAsync(User, permission, ct)) throw AppException.Forbidden();
    }

    private static void ValidateXlsx(IFormFile? file, int maxSize)
    {
        if (file is null || file.Length == 0) throw AppException.BadRequest("Yüklenecek XLSX dosyası zorunludur.");
        if (file.Length > maxSize) throw AppException.BadRequest("XLSX dosyası en fazla 5 MB olabilir.");
        if (!string.Equals(Path.GetExtension(file.FileName), ".xlsx", StringComparison.OrdinalIgnoreCase))
            throw AppException.BadRequest("Yalnızca .xlsx dosyası yüklenebilir.");
    }

    private static long[] ParseLocationIds(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return [];
        return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => long.TryParse(x, out var id) && id > 0 ? id : 0)
            .Where(x => x > 0)
            .Distinct()
            .ToArray();
    }
}
