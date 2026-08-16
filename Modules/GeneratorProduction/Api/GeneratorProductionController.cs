using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using verii_wms_api_v2.Modules.AccessControl.Application;
using verii_wms_api_v2.Modules.GeneratorProduction.Application;
using verii_wms_api_v2.Modules.Identity.Infrastructure;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.GeneratorProduction.Api;

[Authorize, ApiController, Route("api/generator-production")]
public sealed class GeneratorProductionController(IGeneratorProductionService service, IPermissionAuthorizationService permissions) : ControllerBase
{
    [HttpGet("overview")]
    public async Task<IActionResult> Overview(CancellationToken ct) { await Require("WMS.GENERATOR_PRODUCTION.VIEW", ct); return Ok(ApiResponse<GeneratorOverviewResult>.Ok(await service.GetOverviewAsync(ct))); }

    [HttpPost("projects/paged")]
    public async Task<IActionResult> Projects(PagedRequest request, CancellationToken ct) { await Require("WMS.GENERATOR_PRODUCTION.VIEW", ct); return Ok(ApiResponse<PagedResponse<GeneratorProjectRow>>.Ok(await service.GetProjectsAsync(request, ct))); }

    [HttpGet("projects/{id:long}")]
    public async Task<IActionResult> Project(long id, CancellationToken ct) { await Require("WMS.GENERATOR_PRODUCTION.VIEW", ct); return Ok(ApiResponse<GeneratorProjectDetail>.Ok(await service.GetProjectAsync(id, ct))); }

    [HttpGet("projects/{id:long}/operations")]
    public async Task<IActionResult> ProjectOperations(long id, CancellationToken ct)
    {
        await Require("WMS.GENERATOR_PRODUCTION.VIEW", ct);
        return Ok(ApiResponse<IReadOnlyList<GeneratorScheduleRow>>.Ok(await service.GetProjectOperationsAsync(id, ct)));
    }

    [HttpPost("projects")]
    public async Task<IActionResult> CreateProject(CreateGeneratorProjectRequest request, CancellationToken ct)
    {
        await Require("WMS.GENERATOR_PRODUCTION.CREATE", ct);
        return Ok(ApiResponse<GeneratorProjectDetail>.Ok(await service.CreateProjectAsync(request, UserId(), ct), "Jeneratör üretim projesi oluşturuldu."));
    }

    [HttpPut("projects/{id:long}"), HttpPost("projects/{id:long}/update")]
    public async Task<IActionResult> UpdateProject(long id, UpdateGeneratorProjectRequest request, CancellationToken ct)
    {
        await Require("WMS.GENERATOR_PRODUCTION.CREATE", ct);
        return Ok(ApiResponse<GeneratorProjectDetail>.Ok(await service.UpdateProjectAsync(id, request, UserId(), ct), "Jeneratör üretim projesi güncellendi."));
    }

    [HttpPost("projects/{id:long}/release")]
    public async Task<IActionResult> ReleaseProject(long id, ReleaseGeneratorProjectRequest request, CancellationToken ct)
    {
        await Require("WMS.GENERATOR_PRODUCTION.PLAN", ct);
        return Ok(ApiResponse<GeneratorProjectDetail>.Ok(
            await service.ReleaseProjectAsync(id, request, UserId(), ct), "Jeneratör üretim projesi üretime serbest bırakıldı."));
    }

    [HttpDelete("projects/{id:long}"), HttpPost("projects/{id:long}/delete")]
    public async Task<IActionResult> DeleteProject(long id, CancellationToken ct)
    {
        await Require("WMS.GENERATOR_PRODUCTION.CREATE", ct); await service.DeleteProjectAsync(id, UserId(), ct);
        return Ok(ApiResponse<bool>.Ok(true, "Jeneratör üretim projesi silindi."));
    }

    [HttpGet("definitions")]
    public async Task<IActionResult> Definitions(CancellationToken ct) { await Require("WMS.GENERATOR_PRODUCTION.SETTINGS.VIEW", ct); return Ok(ApiResponse<GeneratorDefinitionsResult>.Ok(await service.GetDefinitionsAsync(ct))); }

    [HttpGet("definitions/policy")]
    public async Task<IActionResult> Policy(CancellationToken ct)
    {
        await Require("WMS.GENERATOR_PRODUCTION.SETTINGS.VIEW", ct);
        return Ok(ApiResponse<GeneratorPolicyRow>.Ok(await service.GetPolicyAsync(ct)));
    }

    [HttpPut("definitions/policy"), HttpPost("definitions/policy/update")]
    public async Task<IActionResult> UpdatePolicy(UpdateGeneratorPolicyRequest request, CancellationToken ct)
    {
        await Require("WMS.GENERATOR_PRODUCTION.SETTINGS.MANAGE", ct);
        return Ok(ApiResponse<GeneratorPolicyRow>.Ok(
            await service.UpdatePolicyAsync(request, UserId(), ct), "Jeneratör üretim parametreleri kaydedildi."));
    }

    [HttpPut("definitions/rules/{id:long}"), HttpPost("definitions/rules/{id:long}/update")]
    public async Task<IActionResult> UpdateRule(long id, UpdateGeneratorRuleRequest request, CancellationToken ct)
    {
        await Require("WMS.GENERATOR_PRODUCTION.SETTINGS.MANAGE", ct);
        return Ok(ApiResponse<GeneratorRuleRow>.Ok(
            await service.UpdateRuleAsync(id, request, UserId(), ct), "Planlama kuralı kaydedildi."));
    }

    [HttpPost("definitions/products")]
    public async Task<IActionResult> CreateProduct(SaveGeneratorProductRequest request, CancellationToken ct)
    {
        await Require("WMS.GENERATOR_PRODUCTION.SETTINGS.MANAGE", ct);
        return Ok(ApiResponse<GeneratorProductRow>.Ok(await service.SaveProductAsync(null, request, UserId(), ct), "Jeneratör ürün tanımı oluşturuldu."));
    }

    [HttpPut("definitions/products/{id:long}"), HttpPost("definitions/products/{id:long}/update")]
    public async Task<IActionResult> UpdateProduct(long id, SaveGeneratorProductRequest request, CancellationToken ct)
    {
        await Require("WMS.GENERATOR_PRODUCTION.SETTINGS.MANAGE", ct);
        return Ok(ApiResponse<GeneratorProductRow>.Ok(await service.SaveProductAsync(id, request, UserId(), ct), "Jeneratör ürün tanımı kaydedildi."));
    }

    [HttpDelete("definitions/products/{id:long}"), HttpPost("definitions/products/{id:long}/delete")]
    public async Task<IActionResult> DeleteProduct(long id, CancellationToken ct)
    {
        await Require("WMS.GENERATOR_PRODUCTION.SETTINGS.MANAGE", ct); await service.DeleteProductAsync(id, UserId(), ct);
        return Ok(ApiResponse<bool>.Ok(true, "Jeneratör ürün tanımı silindi."));
    }

    [HttpPost("definitions/station-capabilities")]
    public async Task<IActionResult> CreateStationCapability(SaveGeneratorStationCapabilityRequest request, CancellationToken ct)
    {
        await Require("WMS.GENERATOR_PRODUCTION.SETTINGS.MANAGE", ct);
        return Ok(ApiResponse<GeneratorStationCapabilityRow>.Ok(await service.SaveStationCapabilityAsync(null, request, UserId(), ct), "İstasyon yeteneği oluşturuldu."));
    }

    [HttpPut("definitions/station-capabilities/{id:long}"), HttpPost("definitions/station-capabilities/{id:long}/update")]
    public async Task<IActionResult> UpdateStationCapability(long id, SaveGeneratorStationCapabilityRequest request, CancellationToken ct)
    {
        await Require("WMS.GENERATOR_PRODUCTION.SETTINGS.MANAGE", ct);
        return Ok(ApiResponse<GeneratorStationCapabilityRow>.Ok(await service.SaveStationCapabilityAsync(id, request, UserId(), ct), "İstasyon yeteneği kaydedildi."));
    }

    [HttpDelete("definitions/station-capabilities/{id:long}"), HttpPost("definitions/station-capabilities/{id:long}/delete")]
    public async Task<IActionResult> DeleteStationCapability(long id, CancellationToken ct)
    {
        await Require("WMS.GENERATOR_PRODUCTION.SETTINGS.MANAGE", ct); await service.DeleteStationCapabilityAsync(id, UserId(), ct);
        return Ok(ApiResponse<bool>.Ok(true, "İstasyon yeteneği silindi."));
    }

    [HttpPost("definitions/materials")]
    public async Task<IActionResult> CreateMaterial(SaveGeneratorOperationMaterialRequest request, CancellationToken ct)
    {
        await Require("WMS.GENERATOR_PRODUCTION.SETTINGS.MANAGE", ct);
        return Ok(ApiResponse<GeneratorOperationMaterialRow>.Ok(await service.SaveOperationMaterialAsync(null, request, UserId(), ct), "Operasyon malzemesi oluşturuldu."));
    }

    [HttpPut("definitions/materials/{id:long}"), HttpPost("definitions/materials/{id:long}/update")]
    public async Task<IActionResult> UpdateMaterial(long id, SaveGeneratorOperationMaterialRequest request, CancellationToken ct)
    {
        await Require("WMS.GENERATOR_PRODUCTION.SETTINGS.MANAGE", ct);
        return Ok(ApiResponse<GeneratorOperationMaterialRow>.Ok(await service.SaveOperationMaterialAsync(id, request, UserId(), ct), "Operasyon malzemesi kaydedildi."));
    }

    [HttpDelete("definitions/materials/{id:long}"), HttpPost("definitions/materials/{id:long}/delete")]
    public async Task<IActionResult> DeleteMaterial(long id, CancellationToken ct)
    {
        await Require("WMS.GENERATOR_PRODUCTION.SETTINGS.MANAGE", ct); await service.DeleteOperationMaterialAsync(id, UserId(), ct);
        return Ok(ApiResponse<bool>.Ok(true, "Operasyon malzemesi silindi."));
    }

    [HttpPost("definitions/bootstrap")]
    public async Task<IActionResult> Bootstrap(CancellationToken ct)
    {
        await Require("WMS.GENERATOR_PRODUCTION.SETTINGS.MANAGE", ct);
        return Ok(ApiResponse<GeneratorBootstrapResult>.Ok(await service.BootstrapDefinitionsAsync(UserId(), ct), "SA/RA/FA jeneratör üretim tanımları oluşturuldu."));
    }

    [HttpPost("planning/preview")]
    public async Task<IActionResult> Preview(GeneratorPlanPreviewRequest request, CancellationToken ct)
    {
        await Require("WMS.GENERATOR_PRODUCTION.PLAN", ct);
        return Ok(ApiResponse<GeneratorPlanPreviewResult>.Ok(await service.PreviewPlanAsync(request, ct)));
    }

    [HttpPost("planning/apply")]
    public async Task<IActionResult> Apply(GeneratorPlanApplyRequest request, CancellationToken ct)
    {
        await Require("WMS.GENERATOR_PRODUCTION.PLAN", ct);
        return Ok(ApiResponse<GeneratorPlanApplyResult>.Ok(await service.ApplyPlanAsync(request, UserId(), ct), "Jeneratör üretim planı uygulandı."));
    }

    [HttpGet("planning/assistant")]
    public async Task<IActionResult> Assistant(CancellationToken ct)
    {
        await Require("WMS.GENERATOR_PRODUCTION.PLAN", ct);
        return Ok(ApiResponse<GeneratorPlanningAssistantResult>.Ok(await service.GetPlanningAssistantAsync(ct)));
    }

    [HttpGet("schedule")]
    public async Task<IActionResult> Schedule([FromQuery] DateTime fromUtc, [FromQuery] DateTime toUtc, CancellationToken ct)
    {
        await Require("WMS.GENERATOR_PRODUCTION.VIEW", ct);
        return Ok(ApiResponse<IReadOnlyList<GeneratorScheduleRow>>.Ok(await service.GetScheduleAsync(fromUtc, toUtc, ct)));
    }

    [HttpGet("planning/revisions")]
    public async Task<IActionResult> Revisions([FromQuery] long? projectId, [FromQuery] int take = 100, CancellationToken ct = default)
    {
        await Require("WMS.GENERATOR_PRODUCTION.VIEW", ct);
        return Ok(ApiResponse<IReadOnlyList<GeneratorPlanRevisionRow>>.Ok(await service.GetPlanRevisionsAsync(projectId, take, ct)));
    }

    [HttpPost("operations/{operationId:long}/transition")]
    public async Task<IActionResult> TransitionOperation(long operationId, GeneratorOperationTransitionRequest request, CancellationToken ct)
    {
        await Require("WMS.GENERATOR_PRODUCTION.OPERATE", ct);
        return Ok(ApiResponse<GeneratorScheduleRow>.Ok(
            await service.TransitionOperationAsync(operationId, request, UserId(), ct), "Jeneratör üretim operasyonu güncellendi."));
    }

    [HttpPost("operations/{operationId:long}/quality-decision")]
    public async Task<IActionResult> DecideOperationQuality(long operationId, GeneratorQualityDecisionRequest request, CancellationToken ct)
    {
        await Require("WMS.QUALITY.INSPECTIONS.DECIDE", ct);
        return Ok(ApiResponse<GeneratorScheduleRow>.Ok(
            await service.DecideOperationQualityAsync(operationId, request, UserId(), ct), "Jeneratör üretim kalite kararı kaydedildi."));
    }

    [HttpPut("operations/{operationId:long}/schedule"), HttpPost("operations/{operationId:long}/schedule")]
    public async Task<IActionResult> UpdateOperationSchedule(long operationId, UpdateGeneratorOperationScheduleRequest request, CancellationToken ct)
    {
        await Require("WMS.GENERATOR_PRODUCTION.PLAN", ct);
        return Ok(ApiResponse<GeneratorScheduleRow>.Ok(
            await service.UpdateOperationScheduleAsync(operationId, request, UserId(), ct), "Operasyon planı güncellendi."));
    }

    private long UserId() => long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : throw AppException.Unauthorized("Kullanıcı kimliği bulunamadı.");
    private async Task Require(string code, CancellationToken ct) { if (!await permissions.HasPermissionAsync(User, code, ct)) throw AppException.Forbidden("Bu işlem için yetkiniz bulunmuyor."); }
}
