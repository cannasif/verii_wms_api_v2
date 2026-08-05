using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using verii_wms_api_v2.Modules.AccessControl.Domain;
using verii_wms_api_v2.Shared.Infrastructure;

namespace verii_wms_api_v2.Modules.AccessControl.Infrastructure;

public sealed class PermissionDefinitionConfiguration : BaseEntityConfiguration<PermissionDefinition>
{
    protected override void ConfigureEntity(EntityTypeBuilder<PermissionDefinition> b) { b.ToTable("RII_PERMISSION_DEFINITIONS"); b.Property(x=>x.Code).HasMaxLength(150).IsRequired(); b.Property(x=>x.Name).HasMaxLength(200).IsRequired(); b.Property(x=>x.Description).HasMaxLength(500); b.HasIndex(x=>x.Code).IsUnique().HasFilter("[IsDeleted] = 0"); }
}
public sealed class PermissionGroupConfiguration : BaseEntityConfiguration<PermissionGroup>
{
    protected override void ConfigureEntity(EntityTypeBuilder<PermissionGroup> b) { b.ToTable("RII_PERMISSION_GROUPS"); b.Property(x=>x.Name).HasMaxLength(150).IsRequired(); b.Property(x=>x.Description).HasMaxLength(500); b.Property(x=>x.TemplateKey).HasMaxLength(80); b.HasIndex(x=>x.Name).IsUnique().HasFilter("[IsDeleted] = 0"); b.HasIndex(x=>x.TemplateKey).IsUnique().HasFilter("[TemplateKey] IS NOT NULL AND [IsDeleted] = 0"); }
}
public sealed class PermissionGroupPermissionConfiguration : BaseEntityConfiguration<PermissionGroupPermission>
{
    protected override void ConfigureEntity(EntityTypeBuilder<PermissionGroupPermission> b) { b.ToTable("RII_PERMISSION_GROUP_PERMISSIONS"); b.HasOne(x=>x.PermissionGroup).WithMany(x=>x.GroupPermissions).HasForeignKey(x=>x.PermissionGroupId).OnDelete(DeleteBehavior.Cascade); b.HasOne(x=>x.PermissionDefinition).WithMany(x=>x.GroupPermissions).HasForeignKey(x=>x.PermissionDefinitionId).OnDelete(DeleteBehavior.Cascade); b.HasIndex(x=>new{x.PermissionGroupId,x.PermissionDefinitionId}).IsUnique().HasFilter("[IsDeleted] = 0"); }
}
public sealed class UserPermissionGroupConfiguration : BaseEntityConfiguration<UserPermissionGroup>
{
    protected override void ConfigureEntity(EntityTypeBuilder<UserPermissionGroup> b) { b.ToTable("RII_USER_PERMISSION_GROUPS"); b.HasOne(x=>x.User).WithMany().HasForeignKey(x=>x.UserId).OnDelete(DeleteBehavior.Cascade); b.HasOne(x=>x.PermissionGroup).WithMany(x=>x.UserGroups).HasForeignKey(x=>x.PermissionGroupId).OnDelete(DeleteBehavior.Cascade); b.HasIndex(x=>new{x.UserId,x.PermissionGroupId}).IsUnique().HasFilter("[IsDeleted] = 0"); }
}
