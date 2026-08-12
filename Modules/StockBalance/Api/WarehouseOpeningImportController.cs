using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using verii_wms_api_v2.Modules.AccessControl.Application;
using verii_wms_api_v2.Modules.StockBalance.Application;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.StockBalance.Api;

[Authorize, ApiController, Route("api/warehouse-opening-import")]
public sealed class WarehouseOpeningImportController(
    IWarehouseOpeningImportService service,
    IPermissionAuthorizationService permissions) : ControllerBase
{
    [HttpGet("template")]
    public async Task<IActionResult> DownloadTemplate(
        [FromQuery] string branchCode = "0",
        CancellationToken cancellationToken = default)
    {
        await RequirePermissions(cancellationToken);
        var bytes = await service.CreateTemplateAsync(branchCode, cancellationToken);
        return File(
            bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"wms-v2-depo-acilis-{DateTime.UtcNow:yyyyMMdd}.xlsx");
    }

    [HttpPost("preview"), RequestSizeLimit(WarehouseOpeningImportService.MaxFileSize)]
    public async Task<IActionResult> Preview(
        IFormFile? file,
        [FromQuery] string branchCode,
        CancellationToken cancellationToken)
    {
        await RequirePermissions(cancellationToken);
        ValidateFile(file);
        await using var stream = file!.OpenReadStream();
        var result = await service.PreviewAsync(stream, branchCode, cancellationToken);
        return Ok(ApiResponse<WarehouseOpeningPreview>.Ok(
            result,
            "Dosyanın raf tanımları ve açılış bakiyeleri kayıt yapılmadan doğrulandı."));
    }

    [HttpPost("commit"), RequestSizeLimit(WarehouseOpeningImportService.MaxFileSize)]
    public async Task<IActionResult> Commit(
        IFormFile? file,
        [FromQuery] string branchCode,
        [FromQuery] string previewHash,
        [FromQuery] string idempotencyKey,
        [FromQuery] bool replaceExistingBalances,
        [FromQuery] string balanceSnapshotHash,
        CancellationToken cancellationToken)
    {
        await RequirePermissions(cancellationToken);
        ValidateFile(file);
        await using var stream = file!.OpenReadStream();
        var result = await service.ImportAsync(
            stream, branchCode, previewHash, idempotencyKey,
            replaceExistingBalances, balanceSnapshotHash, cancellationToken);
        return Ok(ApiResponse<WarehouseOpeningImportResult>.Ok(
            result,
            "Raf tanımları ve ilk stok/seri bakiyeleri güvenli partiler halinde kaydedildi."));
    }

    private async Task RequirePermissions(CancellationToken cancellationToken)
    {
        if (!await permissions.HasPermissionAsync(
                User, "WMS.LOCATIONS.CREATE", cancellationToken)
            || !await permissions.HasPermissionAsync(
                User, "WMS.STOCK_MOVEMENTS.POST", cancellationToken))
            throw AppException.Forbidden(
                "Depo açılış aktarımı için raf oluşturma ve stok hareketi yetkileri birlikte gereklidir.");
    }

    private static void ValidateFile(IFormFile? file)
    {
        if (file is null || file.Length == 0)
            throw AppException.BadRequest("Yüklenecek XLSX dosyası zorunludur.");
        if (file.Length > WarehouseOpeningImportService.MaxFileSize)
            throw AppException.BadRequest("XLSX dosyası en fazla 64 MB olabilir.");
        if (!string.Equals(
                Path.GetExtension(file.FileName), ".xlsx", StringComparison.OrdinalIgnoreCase))
            throw AppException.BadRequest("Yalnızca .xlsx dosyası yüklenebilir.");
    }
}
