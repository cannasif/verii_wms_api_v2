using verii_wms_api_v2.Shared;

namespace verii_wms_api_v2.Modules.AccessControl.Application;

public sealed record PermissionRequest(string Code, string Name, string? Description, bool IsActive, bool AvailableOnWeb, bool AvailableOnMobile);
public sealed record GroupRequest(string Name, string? Description, bool IsSystemAdmin, bool IsActive, IReadOnlyList<long> PermissionIds);
public sealed record IdListRequest(IReadOnlyList<long> Ids);
public sealed record PermissionGridRow(long Id, string Code, string Name, string? Description, bool IsActive, bool AvailableOnWeb, bool AvailableOnMobile, long? CreatedBy, DateTime? CreatedDate, long? UpdatedBy, DateTime? UpdatedDate);
public sealed record GroupGridRow(long Id, string Name, string? Description, bool IsSystemAdmin, bool IsActive, int PermissionCount, long? CreatedBy, DateTime? CreatedDate, long? UpdatedBy, DateTime? UpdatedDate);
public sealed record GroupDetail(long Id, string Name, string? Description, bool IsSystemAdmin, bool IsActive, IReadOnlyList<long> PermissionIds, IReadOnlyList<string> PermissionCodes);
public sealed record GroupStats(int Total, int Active, int SystemAdmin);
public sealed record MyPermissionsResponse(bool IsSystemAdmin, IReadOnlyList<string> Permissions);

public interface IAccessControlService
{
    Task<PagedResponse<PermissionGridRow>> GetPermissionsAsync(PagedRequest request, CancellationToken ct);
    Task<IReadOnlyList<PermissionGridRow>> GetActivePermissionCatalogAsync(CancellationToken ct);
    Task<long> CreatePermissionAsync(PermissionRequest request, CancellationToken ct);
    Task UpdatePermissionAsync(long id, PermissionRequest request, CancellationToken ct);
    Task DeletePermissionAsync(long id, CancellationToken ct);
    Task<PagedResponse<GroupGridRow>> GetGroupsAsync(PagedRequest request, CancellationToken ct);
    Task<GroupStats> GetGroupStatsAsync(CancellationToken ct);
    Task<long> CreateGroupAsync(GroupRequest request, CancellationToken ct);
    Task<GroupDetail> GetGroupAsync(long id, CancellationToken ct);
    Task UpdateGroupAsync(long id, GroupRequest request, CancellationToken ct);
    Task DeleteGroupAsync(long id, CancellationToken ct);
    Task<IReadOnlyList<long>> GetUserGroupsAsync(long userId, CancellationToken ct);
    Task SetUserGroupsAsync(long userId, IReadOnlyList<long> ids, CancellationToken ct);
    Task<MyPermissionsResponse> GetMyPermissionsAsync(long userId, string? role, CancellationToken ct);
}
