using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using verii_wms_api_v2.Modules.AccessControl.Application;
using verii_wms_api_v2.Modules.DocumentSeries.Application;
using verii_wms_api_v2.Modules.DocumentSeries.Domain;
using verii_wms_api_v2.Modules.DocumentSeries.Localization;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.DocumentSeries.Api;

[ApiController, Route("api/document-series"), Authorize]
public sealed class DocumentSeriesController(
    IDocumentSeriesService service,
    IPermissionAuthorizationService permissions,
    IStringLocalizer<DocumentSeriesResource> localizer) : ControllerBase
{
    [HttpPost("paged")]
    public async Task<IActionResult> GetPaged(PagedRequest request, CancellationToken cancellationToken)
    {
        await RequireAsync("WMS.DOCUMENT_SERIES.VIEW", cancellationToken);
        return Ok(ApiResponse<PagedResponse<DocumentSeriesGridRow>>.Ok(await service.GetPagedAsync(request, cancellationToken)));
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> GetById(long id, CancellationToken cancellationToken)
    {
        await RequireAsync("WMS.DOCUMENT_SERIES.VIEW", cancellationToken);
        return Ok(ApiResponse<DocumentSeriesGridRow>.Ok(await service.GetByIdAsync(id, cancellationToken)));
    }

    [HttpGet("lookup")]
    public async Task<IActionResult> GetLookup([FromQuery] WmsDocumentType documentType, [FromQuery] long? warehouseId, CancellationToken cancellationToken)
    {
        await RequireAsync("WMS.DOCUMENT_SERIES.VIEW", cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<DocumentSeriesLookupRow>>.Ok(await service.GetLookupAsync(documentType, warehouseId, cancellationToken)));
    }

    [HttpPost]
    public async Task<IActionResult> Create(DocumentSeriesUpsertRequest request, CancellationToken cancellationToken)
    {
        await RequireAsync("WMS.DOCUMENT_SERIES.CREATE", cancellationToken);
        var id = await service.CreateAsync(request, cancellationToken);
        return Ok(ApiResponse<object>.Ok(new { id }, localizer[DocumentSeriesMessageKeys.Created].Value));
    }

    [HttpPut("{id:long}"), HttpPost("{id:long}/update")]
    public async Task<IActionResult> Update(long id, DocumentSeriesUpsertRequest request, CancellationToken cancellationToken)
    {
        await RequireAsync("WMS.DOCUMENT_SERIES.UPDATE", cancellationToken);
        await service.UpdateAsync(id, request, cancellationToken);
        return Ok(ApiResponse<bool>.Ok(true, localizer[DocumentSeriesMessageKeys.Updated].Value));
    }

    [HttpDelete("{id:long}"), HttpPost("{id:long}/delete")]
    public async Task<IActionResult> Delete(long id, CancellationToken cancellationToken)
    {
        await RequireAsync("WMS.DOCUMENT_SERIES.DELETE", cancellationToken);
        await service.DeleteAsync(id, cancellationToken);
        return Ok(ApiResponse<bool>.Ok(true, localizer[DocumentSeriesMessageKeys.Deleted].Value));
    }

    private async Task RequireAsync(string permission, CancellationToken cancellationToken)
    {
        if (!await permissions.HasPermissionAsync(User, permission, cancellationToken))
            throw AppException.Forbidden(localizer[DocumentSeriesMessageKeys.Forbidden].Value);
    }
}
