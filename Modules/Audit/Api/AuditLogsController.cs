using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using verii_wms_api_v2.Modules.AccessControl.Application;
using verii_wms_api_v2.Modules.Audit.Application;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.Audit.Api;

[Authorize, ApiController, Route("api/audit-logs")]
public sealed class AuditLogsController(IAuditLogQueryService service, IPermissionAuthorizationService permissions) : ControllerBase
{
    [HttpPost("paged"), HttpPost("query")]
    public async Task<IActionResult> Paged(PagedRequest request, CancellationToken ct) { await Require(ct); return Ok(ApiResponse<PagedResponse<AuditLogRow>>.Ok(await service.GetPagedAsync(request, ct))); }
    [HttpGet("{id:long}")]
    public async Task<IActionResult> Detail(long id, CancellationToken ct) { await Require(ct); return Ok(ApiResponse<AuditLogDetail>.Ok(await service.GetByIdAsync(id, ct))); }
    private async Task Require(CancellationToken ct) { if (!await permissions.HasPermissionAsync(User, "SYSTEM.AUDIT.VIEW", ct)) throw AppException.Forbidden("Audit kayıtlarını görüntüleme yetkiniz bulunmuyor."); }
}
