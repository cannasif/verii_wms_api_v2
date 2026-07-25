using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using verii_wms_api_v2.Modules.AccessControl.Application;
using verii_wms_api_v2.Modules.ProjectSettings.Application;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.ProjectSettings.Api;

[Authorize, ApiController, Route("api/project-settings")]
public sealed class ProjectSettingsController(IProjectSettingsService service, IPermissionAuthorizationService permissions) : ControllerBase
{
    [HttpGet("current")]
    public async Task<IActionResult> Current(CancellationToken ct) => Ok(ApiResponse<ProjectSettingsResponse>.Ok(await service.GetAsync(ct)));

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        await RequireAsync("SYSTEM.PROJECT_SETTINGS.VIEW", ct);
        return Ok(ApiResponse<ProjectSettingsResponse>.Ok(await service.GetAsync(ct)));
    }

    [HttpPut]
    public async Task<IActionResult> Update(UpdateProjectSettingsRequest request, CancellationToken ct)
    {
        await RequireAsync("SYSTEM.PROJECT_SETTINGS.MANAGE", ct);
        return Ok(ApiResponse<ProjectSettingsResponse>.Ok(await service.UpdateAsync(request, ct), "Proje ayarları kaydedildi."));
    }

    private async Task RequireAsync(string permission, CancellationToken ct)
    {
        if (!await permissions.HasPermissionAsync(User, permission, ct)) throw AppException.Forbidden();
    }
}
