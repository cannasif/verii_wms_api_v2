using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using verii_wms_api_v2.Modules.AccessControl.Application;
using verii_wms_api_v2.Modules.SystemManagement.Application.Users;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.SystemManagement.Api;

[Authorize, ApiController, Route("api/users")]
public sealed class UserManagementController(IUserManagementService service, IPermissionAuthorizationService permissions) : ControllerBase
{
    private const long MaxImportFileSize = UserManagementService.MaxImportFileSize;
    private const long MaxImportRequestSize = MaxImportFileSize + (1024 * 1024);
    private const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private const string BinaryContentType = "application/octet-stream";

    [HttpPost("paged")]
    public async Task<IActionResult> Paged(PagedRequest request, CancellationToken ct)
    { await Require("SYSTEM.USERS.VIEW", ct); return Ok(ApiResponse<PagedResponse<UserGridRow>>.Ok(await service.GetPagedAsync(request, ct))); }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Detail(long id, CancellationToken ct)
    { await Require("SYSTEM.USERS.VIEW", ct); return Ok(ApiResponse<UserDetailResponse>.Ok(await service.GetByIdAsync(id, ct))); }

    [HttpPost]
    public async Task<IActionResult> Create(CreateUserRequest request, CancellationToken ct)
    { await Require("SYSTEM.USERS.MANAGE", ct); return Ok(ApiResponse<object>.Ok(await service.CreateAsync(request, ct), "Kullanıcı oluşturuldu.")); }

    [HttpGet("import-template")]
    public async Task<IActionResult> DownloadImportTemplate(CancellationToken ct)
    {
        await Require("SYSTEM.USERS.MANAGE", ct);
        var file = await service.CreateImportTemplateAsync(ct);
        return File(file, XlsxContentType, "wms-kullanici-aktarim-sablonu.xlsx");
    }

    [HttpPost("import")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxImportRequestSize)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxImportFileSize)]
    public async Task<IActionResult> Import([FromForm] IFormFile? file, CancellationToken ct)
    {
        await Require("SYSTEM.USERS.MANAGE", ct);
        ValidateImportFile(file);
        await using var stream = file!.OpenReadStream();
        var result = await service.ImportAsync(stream, ct);
        return Ok(ApiResponse<UserImportResult>.Ok(
            result,
            $"{result.CreatedCount} kullanıcı oluşturuldu; {result.SkippedCount} satır atlandı, {result.FailedCount} satır başarısız."));
    }

    [HttpPut("{id:long}"), HttpPost("{id:long}/update")]
    public async Task<IActionResult> Update(long id, UpdateUserRequest request, CancellationToken ct)
    { await Require("SYSTEM.USERS.MANAGE", ct); return Ok(ApiResponse<bool>.Ok(await service.UpdateAsync(id, request, ct), "Kullanıcı güncellendi.")); }

    [HttpPut("{id:long}/warehouse-assignments")]
    public async Task<IActionResult> UpdateWarehouseAssignments(long id, UpdateUserWarehouseAssignmentsRequest request, CancellationToken ct)
    {
        if (!await permissions.HasPermissionAsync(User, "WMS.GOODS_RECEIPT.SETTINGS.MANAGE", ct)
            && !await permissions.HasPermissionAsync(User, "SYSTEM.USERS.MANAGE", ct))
            throw AppException.Forbidden();
        return Ok(ApiResponse<IReadOnlyList<long>>.Ok(
            await service.UpdateWarehouseAssignmentsAsync(id, request, ct),
            "Kullanıcının mal kabul depo yetkileri güncellendi."));
    }

    [HttpDelete("{id:long}"), HttpPost("{id:long}/delete")]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    { await Require("SYSTEM.USERS.MANAGE", ct); return Ok(ApiResponse<bool>.Ok(await service.DeactivateAsync(id, ct), "Kullanıcı pasife alındı.")); }

    private async Task Require(string code, CancellationToken ct)
    { if (!await permissions.HasPermissionAsync(User, code, ct)) throw AppException.Forbidden(); }

    private static void ValidateImportFile(IFormFile? file)
    {
        if (file is null || file.Length == 0)
            throw AppException.BadRequest("Yüklenecek XLSX dosyası zorunludur.");
        if (file.Length > MaxImportFileSize)
            throw AppException.BadRequest("XLSX dosyası en fazla 5 MB olabilir.");
        if (!Path.GetExtension(file.FileName).Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
            throw AppException.BadRequest("Yalnızca .xlsx uzantılı Excel dosyaları yüklenebilir.");
        if (!string.Equals(file.ContentType, XlsxContentType, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(file.ContentType, BinaryContentType, StringComparison.OrdinalIgnoreCase))
            throw AppException.BadRequest($"Geçersiz dosya içerik türü. Beklenen: {XlsxContentType}.");
    }
}
