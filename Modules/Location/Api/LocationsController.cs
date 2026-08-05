using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using verii_wms_api_v2.Modules.AccessControl.Application;
using verii_wms_api_v2.Modules.Location.Application;
using verii_wms_api_v2.Modules.Location.Localization;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.Location.Api;

[Authorize, ApiController, Route("api/locations")]
public sealed class LocationsController(ILocationService service, ILocationImportService importService,
    IPermissionAuthorizationService permissions, IStringLocalizer<LocationResource> localizer) : ControllerBase
{
    [HttpGet("import/template")]
    public async Task<IActionResult> DownloadImportTemplate([FromQuery] string branchCode = "0",
        CancellationToken cancellationToken = default)
    {
        await RequireAsync("WMS.LOCATIONS.CREATE", cancellationToken);
        var bytes = await importService.CreateTemplateAsync(branchCode, cancellationToken);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"wms-v2-raf-ilk-aktarim-{DateTime.UtcNow:yyyyMMdd}.xlsx");
    }

    [HttpPost("import"), RequestSizeLimit(LocationImportService.MaxFileSize)]
    public async Task<IActionResult> Import(IFormFile? file, [FromQuery] string branchCode = "0",
        CancellationToken cancellationToken = default)
    {
        await RequireAsync("WMS.LOCATIONS.CREATE", cancellationToken);
        ValidateXlsx(file, LocationImportService.MaxFileSize);
        await using var stream = file!.OpenReadStream();
        var result = await importService.ImportAsync(stream, branchCode, cancellationToken);
        return Ok(ApiResponse<LocationImportResult>.Ok(result, $"{result.CreatedRows} raf tanımı oluşturuldu."));
    }

    [HttpPost("paged")]
    public async Task<IActionResult> GetPaged(PagedRequest request, CancellationToken cancellationToken)
    {
        await RequireAsync("WMS.LOCATIONS.VIEW", cancellationToken);
        return Ok(ApiResponse<PagedResponse<LocationGridRow>>.Ok(await service.GetPagedAsync(request, cancellationToken)));
    }

    [HttpGet("stats")]
    public async Task<IActionResult> GetStats(CancellationToken cancellationToken)
    {
        await RequireAsync("WMS.LOCATIONS.VIEW", cancellationToken);
        return Ok(ApiResponse<LocationStats>.Ok(await service.GetStatsAsync(cancellationToken)));
    }

    [HttpGet("lookup")]
    public async Task<IActionResult> GetLookup([FromQuery] long warehouseId, [FromQuery] bool includeInactive, CancellationToken cancellationToken)
    {
        await RequireAsync("WMS.LOCATIONS.VIEW", cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<LocationLookupRow>>.Ok(await service.GetLookupAsync(warehouseId, includeInactive, cancellationToken)));
    }

    [HttpGet("putaway-suggestions")]
    public async Task<IActionResult> GetPutawaySuggestions(
        [FromQuery] long warehouseId,
        [FromQuery] long? stockId,
        [FromQuery] string? stockCode,
        [FromQuery] long? yapCodeId,
        [FromQuery] decimal quantity = 1,
        [FromQuery] int limit = 5,
        CancellationToken cancellationToken = default)
    {
        await RequireAsync("WMS.LOCATIONS.VIEW", cancellationToken);
        var rows = await service.GetPutawaySuggestionsAsync(
            warehouseId, stockId, stockCode, yapCodeId, quantity, limit, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<PutawayLocationSuggestion>>.Ok(rows));
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> GetById(long id, CancellationToken cancellationToken)
    {
        await RequireAsync("WMS.LOCATIONS.VIEW", cancellationToken);
        return Ok(ApiResponse<LocationGridRow>.Ok(await service.GetByIdAsync(id, cancellationToken)));
    }

    [HttpPost]
    public async Task<IActionResult> Create(LocationUpsertRequest request, CancellationToken cancellationToken)
    {
        await RequireAsync("WMS.LOCATIONS.CREATE", cancellationToken);
        var id = await service.CreateAsync(request, cancellationToken);
        return Ok(ApiResponse<object>.Ok(new { id }, localizer[LocationMessageKeys.Created].Value));
    }

    [HttpPut("{id:long}"), HttpPost("{id:long}/update")]
    public async Task<IActionResult> Update(long id, LocationUpsertRequest request, CancellationToken cancellationToken)
    {
        await RequireAsync("WMS.LOCATIONS.UPDATE", cancellationToken);
        await service.UpdateAsync(id, request, cancellationToken);
        return Ok(ApiResponse<bool>.Ok(true, localizer[LocationMessageKeys.Updated].Value));
    }

    [HttpDelete("{id:long}"), HttpPost("{id:long}/delete")]
    public async Task<IActionResult> Delete(long id, CancellationToken cancellationToken)
    {
        await RequireAsync("WMS.LOCATIONS.DELETE", cancellationToken);
        await service.DeleteAsync(id, cancellationToken);
        return Ok(ApiResponse<bool>.Ok(true, localizer[LocationMessageKeys.Deleted].Value));
    }

    private async Task RequireAsync(string permission, CancellationToken cancellationToken)
    {
        if (!await permissions.HasPermissionAsync(User, permission, cancellationToken)) throw AppException.Forbidden(localizer[LocationMessageKeys.Forbidden].Value);
    }

    private static void ValidateXlsx(IFormFile? file, int maxSize)
    {
        if (file is null || file.Length == 0) throw AppException.BadRequest("Yüklenecek XLSX dosyası zorunludur.");
        if (file.Length > maxSize) throw AppException.BadRequest("XLSX dosyası en fazla 5 MB olabilir.");
        if (!string.Equals(Path.GetExtension(file.FileName), ".xlsx", StringComparison.OrdinalIgnoreCase))
            throw AppException.BadRequest("Yalnızca .xlsx dosyası yüklenebilir.");
    }
}
