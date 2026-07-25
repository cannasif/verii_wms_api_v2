using verii_wms_api_v2.Modules.Identity.Domain;
using verii_wms_api_v2.Shared.Domain;

namespace verii_wms_api_v2.Modules.AccessControl.Domain;

public sealed class PermissionDefinition : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public bool AvailableOnWeb { get; set; } = true;
    public bool AvailableOnMobile { get; set; }
    public ICollection<PermissionGroupPermission> GroupPermissions { get; set; } = new List<PermissionGroupPermission>();
}

public sealed class PermissionGroup : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsSystemAdmin { get; set; }
    public bool IsActive { get; set; } = true;
    public ICollection<PermissionGroupPermission> GroupPermissions { get; set; } = new List<PermissionGroupPermission>();
    public ICollection<UserPermissionGroup> UserGroups { get; set; } = new List<UserPermissionGroup>();
}

public sealed class PermissionGroupPermission : BaseEntity
{
    public long PermissionGroupId { get; set; }
    public PermissionGroup PermissionGroup { get; set; } = null!;
    public long PermissionDefinitionId { get; set; }
    public PermissionDefinition PermissionDefinition { get; set; } = null!;
}

public sealed class UserPermissionGroup : BaseEntity
{
    public long UserId { get; set; }
    public User User { get; set; } = null!;
    public long PermissionGroupId { get; set; }
    public PermissionGroup PermissionGroup { get; set; } = null!;
}
