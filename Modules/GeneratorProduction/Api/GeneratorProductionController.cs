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

    private long UserId() => long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : throw AppException.Unauthorized("Kullanıcı kimliği bulunamadı.");
    private async Task Require(string code, CancellationToken ct) { if (!await permissions.HasPermissionAsync(User, code, ct)) throw AppException.Forbidden("Bu işlem için yetkiniz bulunmuyor."); }
}
