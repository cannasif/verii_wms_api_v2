using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using verii_wms_api_v2.Modules.Dashboard.Application;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.Dashboard.Api;

[Authorize, ApiController, Route("api/dashboard")]
public sealed class DashboardController(IDashboardService service) : ControllerBase
{
    [HttpGet("summary")]
    public async Task<IActionResult> Summary(
        [FromHeader(Name = "X-Branch-Code")] string? branchCode,
        CancellationToken cancellationToken)
    {
        var result = await service.GetSummaryAsync(
            CurrentUserId(),
            branchCode,
            cancellationToken);
        return Ok(ApiResponse<DashboardSummary>.Ok(result));
    }

    private long CurrentUserId() =>
        long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? id
            : throw AppException.Unauthorized("Geçersiz kullanıcı oturumu.");
}
