using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using verii_wms_api_v2.Modules.AccessControl.Application;
using verii_wms_api_v2.Modules.BarcodeDesigner.Application;
using verii_wms_api_v2.Modules.BarcodeDesigner.Domain;
using verii_wms_api_v2.Modules.BarcodeDesigner.Localization;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.BarcodeDesigner.Api;

[ApiController, Route("api/barcode-designer"), Authorize]
public sealed class BarcodeDesignerController(IBarcodeDesignerService service, IBarcodePolicyService policy, IPermissionAuthorizationService permissions, IStringLocalizer<BarcodeDesignerResource> localizer) : ControllerBase
{
    [HttpPost("templates/paged")] public async Task<IActionResult> Paged(PagedRequest request, CancellationToken ct) { await Require("WMS.BARCODE_DESIGNER.VIEW", ct); return Ok(ApiResponse<PagedResponse<BarcodeTemplateGridRow>>.Ok(await service.GetPagedAsync(request, ct))); }
    [HttpGet("templates/{id:long}")] public async Task<IActionResult> Get(long id, CancellationToken ct) { await Require("WMS.BARCODE_DESIGNER.VIEW", ct); return Ok(ApiResponse<BarcodeTemplateGridRow>.Ok(await service.GetByIdAsync(id, ct))); }
    [HttpGet("templates/{id:long}/versions")] public async Task<IActionResult> Versions(long id, CancellationToken ct) { await Require("WMS.BARCODE_DESIGNER.VIEW", ct); return Ok(ApiResponse<IReadOnlyList<BarcodeTemplateVersionRow>>.Ok(await service.GetVersionsAsync(id, ct))); }
    [HttpGet("templates/{id:long}/draft")] public async Task<IActionResult> Draft(long id, CancellationToken ct) { await Require("WMS.BARCODE_DESIGNER.VIEW", ct); return Ok(ApiResponse<BarcodeTemplateVersionRow?>.Ok(await service.GetDraftAsync(id, ct))); }
    [HttpGet("schema-fields")] public async Task<IActionResult> Schema(CancellationToken ct) { await Require("WMS.BARCODE_DESIGNER.VIEW", ct); return Ok(ApiResponse<IReadOnlyList<BarcodeSchemaField>>.Ok(service.GetSchemaFields())); }
    [HttpPost("templates")] public async Task<IActionResult> Create(BarcodeTemplateUpsertRequest request, CancellationToken ct) { await Require("WMS.BARCODE_DESIGNER.CREATE", ct); var id = await service.CreateAsync(request, ct); return Ok(ApiResponse<object>.Ok(new { id }, localizer[BarcodeDesignerMessageKeys.Created].Value)); }
    [HttpPut("templates/{id:long}"), HttpPost("templates/{id:long}/update")] public async Task<IActionResult> Update(long id, BarcodeTemplateUpsertRequest request, CancellationToken ct) { await Require("WMS.BARCODE_DESIGNER.UPDATE", ct); await service.UpdateAsync(id, request, ct); return Ok(ApiResponse<bool>.Ok(true, localizer[BarcodeDesignerMessageKeys.Updated].Value)); }
    [HttpDelete("templates/{id:long}"), HttpPost("templates/{id:long}/delete")] public async Task<IActionResult> Delete(long id, CancellationToken ct) { await Require("WMS.BARCODE_DESIGNER.DELETE", ct); await service.DeleteAsync(id, ct); return Ok(ApiResponse<bool>.Ok(true, localizer[BarcodeDesignerMessageKeys.Deleted].Value)); }
    [HttpPost("templates/{id:long}/drafts")] public async Task<IActionResult> SaveDraft(long id, BarcodeDraftSaveRequest request, CancellationToken ct) { await Require("WMS.BARCODE_DESIGNER.UPDATE", ct); return Ok(ApiResponse<BarcodeTemplateVersionRow>.Ok(await service.SaveDraftAsync(id, request, ct), localizer[BarcodeDesignerMessageKeys.DraftSaved].Value)); }
    [HttpPost("templates/{id:long}/publish")] public async Task<IActionResult> Publish(long id, BarcodePublishRequest request, CancellationToken ct) { await Require("WMS.BARCODE_DESIGNER.PUBLISH", ct); return Ok(ApiResponse<BarcodeTemplateVersionRow>.Ok(await service.PublishAsync(id, request, ct), localizer[BarcodeDesignerMessageKeys.Published].Value)); }
    [HttpGet("policy")] public async Task<IActionResult> Policy(CancellationToken ct) { await Require("WMS.BARCODE_POLICY.VIEW", ct); return Ok(ApiResponse<BarcodePolicyResponse>.Ok(await policy.GetAsync(ct))); }
    [HttpPut("policy/profiles/{scope}"), HttpPost("policy/profiles/{scope}/update")] public async Task<IActionResult> UpdateProfile(BarcodePolicyScope scope, BarcodePolicyProfileUpdateRequest request, CancellationToken ct) { await Require("WMS.BARCODE_POLICY.MANAGE", ct); return Ok(ApiResponse<BarcodePolicyResponse>.Ok(await policy.UpdateProfileAsync(scope, request, ct))); }
    [HttpPost("policy/{scope}/preview")] public async Task<IActionResult> Preview(BarcodePolicyScope scope, BarcodeGenerateRequest request, CancellationToken ct) { await Require("WMS.BARCODE_POLICY.VIEW", ct); return Ok(ApiResponse<BarcodePreviewResponse>.Ok(await policy.PreviewAsync(scope, request, ct))); }
    [HttpPost("policy/{scope}/generate")] public async Task<IActionResult> Generate(BarcodePolicyScope scope, BarcodeGenerateRequest request, CancellationToken ct) { await Require("WMS.BARCODE_POLICY.GENERATE", ct); return Ok(ApiResponse<BarcodePreviewResponse>.Ok(await policy.GenerateAsync(scope, request, ct))); }
    [HttpPost("generated/paged")] public async Task<IActionResult> GeneratedPaged(PagedRequest request, CancellationToken ct) { await Require("WMS.BARCODE_POLICY.VIEW", ct); return Ok(ApiResponse<PagedResponse<GeneratedBarcodeRow>>.Ok(await policy.GetGeneratedPagedAsync(request, ct))); }
    private async Task Require(string code, CancellationToken ct) { if (!await permissions.HasPermissionAsync(User, code, ct)) throw AppException.Forbidden(localizer[BarcodeDesignerMessageKeys.Forbidden].Value); }
}
