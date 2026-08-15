using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using verii_wms_api_v2.Modules.AccessControl.Application;
using verii_wms_api_v2.Modules.Quality.Application;
using verii_wms_api_v2.Shared;

namespace verii_wms_api_v2.Modules.Quality.Api;

[Authorize, ApiController, Route("api/quality/reports")]
public sealed class QualityReportsController(
    IQualityReportService reports,
    IPermissionAuthorizationService permissions) : ControllerBase
{
    [HttpPost("inspections/paged")]
    public async Task<IActionResult> InspectionsPaged(PagedRequest request, CancellationToken ct)
    {
        await Require(ct);
        return Ok(ApiResponse<PagedResponse<QualityInspectionReportRow>>.Ok(
            await reports.GetInspectionsPagedAsync(request, ct)));
    }

    [HttpGet("inspections/{id:long}")]
    public async Task<IActionResult> InspectionDetail(long id, CancellationToken ct)
    {
        await Require(ct);
        return Ok(ApiResponse<QualityInspectionReportDetailDto>.Ok(
            await reports.GetInspectionDetailAsync(id, ct)));
    }

    [HttpPost("stocks/paged")]
    public async Task<IActionResult> StocksPaged(PagedRequest request, CancellationToken ct)
    {
        await Require(ct);
        return Ok(ApiResponse<PagedResponse<QualityStockReportRow>>.Ok(
            await reports.GetStocksPagedAsync(request, ct)));
    }

    private async Task Require(CancellationToken ct)
    {
        if (!await permissions.HasPermissionAsync(User, "WMS.QUALITY.INSPECTIONS.VIEW", ct))
            throw Shared.Application.Exceptions.AppException.Forbidden();
    }
}
