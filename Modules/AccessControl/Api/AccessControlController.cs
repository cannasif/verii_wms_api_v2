using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using verii_wms_api_v2.Modules.AccessControl.Application;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.AccessControl.Api;

[Authorize, ApiController, Route("api/access-control")]
public sealed class AccessControlController(IAccessControlService service, IPermissionAuthorizationService permissions) : ControllerBase
{
    [HttpPost("permissions/paged")]
    public async Task<IActionResult> Permissions(PagedRequest request, CancellationToken ct) { await Require("SYSTEM.PERMISSIONS.VIEW", ct); return Ok(ApiResponse<PagedResponse<PermissionGridRow>>.Ok(await service.GetPermissionsAsync(request, ct))); }
    [HttpPost("permissions")]
    public async Task<IActionResult> CreatePermission(PermissionRequest request, CancellationToken ct) { await Require("SYSTEM.PERMISSIONS.MANAGE", ct); return Ok(ApiResponse<object>.Ok(new { id = await service.CreatePermissionAsync(request, ct) }, "İzin tanımı oluşturuldu.")); }
    [HttpPut("permissions/{id:long}"), HttpPost("permissions/{id:long}/update")]
    public async Task<IActionResult> UpdatePermission(long id, PermissionRequest request, CancellationToken ct) { await Require("SYSTEM.PERMISSIONS.MANAGE", ct); await service.UpdatePermissionAsync(id, request, ct); return Ok(ApiResponse<bool>.Ok(true, "İzin tanımı güncellendi.")); }
    [HttpDelete("permissions/{id:long}"), HttpPost("permissions/{id:long}/delete")]
    public async Task<IActionResult> DeletePermission(long id, CancellationToken ct) { await Require("SYSTEM.PERMISSIONS.MANAGE", ct); await service.DeletePermissionAsync(id, ct); return Ok(ApiResponse<bool>.Ok(true, "İzin tanımı silindi.")); }

    [HttpPost("groups/paged")]
    public async Task<IActionResult> Groups(PagedRequest request, CancellationToken ct) { await Require("SYSTEM.PERMISSIONS.VIEW", ct); return Ok(ApiResponse<PagedResponse<GroupGridRow>>.Ok(await service.GetGroupsAsync(request, ct))); }
    [HttpGet("groups/stats")]
    public async Task<IActionResult> GroupStats(CancellationToken ct) { await Require("SYSTEM.PERMISSIONS.VIEW", ct); return Ok(ApiResponse<GroupStats>.Ok(await service.GetGroupStatsAsync(ct))); }
    [HttpPost("groups")]
    public async Task<IActionResult> CreateGroup(GroupRequest request, CancellationToken ct) { await Require("SYSTEM.PERMISSIONS.MANAGE", ct); return Ok(ApiResponse<object>.Ok(new { id = await service.CreateGroupAsync(request, ct) }, "Yetki grubu oluşturuldu.")); }
    [HttpGet("groups/{id:long}")]
    public async Task<IActionResult> Group(long id, CancellationToken ct) { await Require("SYSTEM.PERMISSIONS.VIEW", ct); return Ok(ApiResponse<GroupDetail>.Ok(await service.GetGroupAsync(id, ct))); }
    [HttpPut("groups/{id:long}"), HttpPost("groups/{id:long}/update")]
    public async Task<IActionResult> UpdateGroup(long id, GroupRequest request, CancellationToken ct) { await Require("SYSTEM.PERMISSIONS.MANAGE", ct); await service.UpdateGroupAsync(id, request, ct); return Ok(ApiResponse<bool>.Ok(true, "Yetki grubu güncellendi.")); }
    [HttpDelete("groups/{id:long}"), HttpPost("groups/{id:long}/delete")]
    public async Task<IActionResult> DeleteGroup(long id, CancellationToken ct) { await Require("SYSTEM.PERMISSIONS.MANAGE", ct); await service.DeleteGroupAsync(id, ct); return Ok(ApiResponse<bool>.Ok(true, "Yetki grubu silindi.")); }

    [HttpGet("users/{userId:long}/groups")]
    public async Task<IActionResult> UserGroups(long userId, CancellationToken ct) { await Require("SYSTEM.USERS.VIEW", ct); return Ok(ApiResponse<IReadOnlyList<long>>.Ok(await service.GetUserGroupsAsync(userId, ct))); }
    [HttpPost("users/{userId:long}/groups")]
    public async Task<IActionResult> SetUserGroups(long userId, IdListRequest request, CancellationToken ct) { await Require("SYSTEM.USERS.MANAGE", ct); await service.SetUserGroupsAsync(userId, request.Ids, ct); return Ok(ApiResponse<bool>.Ok(true, "Kullanıcı yetki grupları güncellendi.")); }
    [HttpGet("me/permissions")]
    public async Task<IActionResult> MyPermissions(CancellationToken ct)
    {
        var userId = long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;
        return Ok(ApiResponse<MyPermissionsResponse>.Ok(await service.GetMyPermissionsAsync(userId, User.FindFirstValue(ClaimTypes.Role), ct)));
    }

    private async Task Require(string code, CancellationToken ct) { if (!await permissions.HasPermissionAsync(User, code, ct)) throw AppException.Forbidden(); }
}
