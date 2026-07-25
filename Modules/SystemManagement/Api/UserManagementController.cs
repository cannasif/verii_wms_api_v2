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
    [HttpPost("paged")]
    public async Task<IActionResult> Paged(PagedRequest request, CancellationToken ct)
    { await Require("SYSTEM.USERS.VIEW", ct); return Ok(ApiResponse<PagedResponse<UserGridRow>>.Ok(await service.GetPagedAsync(request, ct))); }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Detail(long id, CancellationToken ct)
    { await Require("SYSTEM.USERS.VIEW", ct); return Ok(ApiResponse<UserDetailResponse>.Ok(await service.GetByIdAsync(id, ct))); }

    [HttpPost]
    public async Task<IActionResult> Create(CreateUserRequest request, CancellationToken ct)
    { await Require("SYSTEM.USERS.MANAGE", ct); return Ok(ApiResponse<object>.Ok(await service.CreateAsync(request, ct), "Kullanıcı oluşturuldu.")); }

    [HttpPut("{id:long}"), HttpPost("{id:long}/update")]
    public async Task<IActionResult> Update(long id, UpdateUserRequest request, CancellationToken ct)
    { await Require("SYSTEM.USERS.MANAGE", ct); return Ok(ApiResponse<bool>.Ok(await service.UpdateAsync(id, request, ct), "Kullanıcı güncellendi.")); }

    [HttpDelete("{id:long}"), HttpPost("{id:long}/delete")]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    { await Require("SYSTEM.USERS.MANAGE", ct); return Ok(ApiResponse<bool>.Ok(await service.DeactivateAsync(id, ct), "Kullanıcı pasife alındı.")); }

    private async Task Require(string code, CancellationToken ct)
    { if (!await permissions.HasPermissionAsync(User, code, ct)) throw AppException.Forbidden(); }
}
