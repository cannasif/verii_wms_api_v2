using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.AccessControl.Domain;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;

namespace verii_wms_api_v2.Modules.AccessControl.Application;

public interface IPermissionAuthorizationService
{
    Task<bool> HasPermissionAsync(ClaimsPrincipal principal, string permissionCode, CancellationToken cancellationToken = default);
}

public sealed class PermissionAuthorizationService(IUnitOfWork unitOfWork) : IPermissionAuthorizationService
{
    public async Task<bool> HasPermissionAsync(ClaimsPrincipal principal, string permissionCode, CancellationToken cancellationToken = default)
    {
        if (string.Equals(principal.FindFirstValue(ClaimTypes.Role), "superadmin", StringComparison.OrdinalIgnoreCase)) return true;
        if (!long.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return false;
        return await unitOfWork.Repository<UserPermissionGroup>().Query().AnyAsync(link => link.UserId == userId && link.PermissionGroup.IsActive
            && (link.PermissionGroup.IsSystemAdmin || link.PermissionGroup.GroupPermissions.Any(groupPermission => groupPermission.PermissionDefinition.IsActive && groupPermission.PermissionDefinition.Code == permissionCode)), cancellationToken);
    }
}
