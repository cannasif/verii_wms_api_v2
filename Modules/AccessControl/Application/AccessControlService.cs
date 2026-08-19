using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.AccessControl.Domain;
using verii_wms_api_v2.Modules.Audit.Application;
using verii_wms_api_v2.Modules.Identity.Domain;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.AccessControl.Application;

public sealed class AccessControlService(IUnitOfWork unitOfWork, IAuditLogWriter audit) : IAccessControlService
{
    private IGenericRepository<PermissionDefinition> PermissionDefinitions => unitOfWork.Repository<PermissionDefinition>();
    private IGenericRepository<PermissionGroup> PermissionGroups => unitOfWork.Repository<PermissionGroup>();
    private IGenericRepository<PermissionGroupPermission> GroupPermissions => unitOfWork.Repository<PermissionGroupPermission>();
    private IGenericRepository<UserPermissionGroup> UserGroups => unitOfWork.Repository<UserPermissionGroup>();
    private IGenericRepository<User> Users => unitOfWork.Repository<User>();

    public async Task<PagedResponse<PermissionGridRow>> GetPermissionsAsync(PagedRequest request, CancellationToken ct)
    {
        var search = request.LegacySearch?.Trim();
        var query = PermissionDefinitions.Query().Where(x => string.IsNullOrWhiteSpace(search) || x.Code.Contains(search) || x.Name.Contains(search) || (x.Description != null && x.Description.Contains(search)))
            .Select(x => new PermissionGridRow(x.Id, x.Code, x.Name, x.Description, x.IsActive, x.AvailableOnWeb, x.AvailableOnMobile, x.CreatedBy, x.CreatedDate, x.UpdatedBy, x.UpdatedDate))
            .ApplyAdvancedFilters(request).ApplySort(request, nameof(PermissionGridRow.Code));
        return await query.ToPagedResponseAsync(request, ct);
    }

    public async Task<IReadOnlyList<PermissionGridRow>> GetActivePermissionCatalogAsync(CancellationToken ct) =>
        await PermissionDefinitions.Query()
            .Where(x => x.IsActive)
            .OrderBy(x => x.Code)
            .Select(x => new PermissionGridRow(
                x.Id,
                x.Code,
                x.Name,
                x.Description,
                x.IsActive,
                x.AvailableOnWeb,
                x.AvailableOnMobile,
                x.CreatedBy,
                x.CreatedDate,
                x.UpdatedBy,
                x.UpdatedDate))
            .ToListAsync(ct);

    public async Task<long> CreatePermissionAsync(PermissionRequest request, CancellationToken ct)
    {
        await ValidatePermission(request, null, ct);
        var entity = new PermissionDefinition { Code = request.Code.Trim().ToUpperInvariant(), Name = request.Name.Trim(), Description = Normalize(request.Description), IsActive = request.IsActive, AvailableOnWeb = request.AvailableOnWeb, AvailableOnMobile = request.AvailableOnMobile };
        await PermissionDefinitions.AddAsync(entity, ct); await unitOfWork.SaveChangesAsync(ct);
        await audit.WriteAsync(new AuditLogWriteEntry("permission.create", "PermissionDefinition", entity.Id.ToString(), "Succeeded", "access-control", NewValues: PermissionSnapshot(entity), ChangedFields: PermissionFields), ct);
        return entity.Id;
    }

    public async Task UpdatePermissionAsync(long id, PermissionRequest request, CancellationToken ct)
    {
        var entity = await PermissionDefinitions.FindByIdAsync(id, true, ct) ?? throw AppException.NotFound("İzin tanımı bulunamadı.");
        await ValidatePermission(request, id, ct); var old = PermissionSnapshot(entity);
        entity.Code = request.Code.Trim().ToUpperInvariant(); entity.Name = request.Name.Trim(); entity.Description = Normalize(request.Description); entity.IsActive = request.IsActive; entity.AvailableOnWeb = request.AvailableOnWeb; entity.AvailableOnMobile = request.AvailableOnMobile; entity.UpdatedDate = DateTime.UtcNow;
        await unitOfWork.SaveChangesAsync(ct); await audit.WriteAsync(new AuditLogWriteEntry("permission.update", "PermissionDefinition", id.ToString(), "Succeeded", "access-control", OldValues: old, NewValues: PermissionSnapshot(entity), ChangedFields: PermissionFields), ct);
    }

    public async Task DeletePermissionAsync(long id, CancellationToken ct)
    {
        var entity = await PermissionDefinitions.FindByIdAsync(id, true, ct) ?? throw AppException.NotFound("İzin tanımı bulunamadı."); var old = PermissionSnapshot(entity);
        entity.IsDeleted = true; entity.IsActive = false; entity.DeletedDate = DateTime.UtcNow;
        var links = await GroupPermissions.Query(true).Where(x => x.PermissionDefinitionId == id).ToListAsync(ct); foreach (var link in links) { link.IsDeleted = true; link.DeletedDate = DateTime.UtcNow; }
        await unitOfWork.SaveChangesAsync(ct); await audit.WriteAsync(new AuditLogWriteEntry("permission.delete", "PermissionDefinition", id.ToString(), "Succeeded", "access-control", OldValues: old, ChangedFields: ["IsDeleted", "IsActive"]), ct);
    }

    public async Task<PagedResponse<GroupGridRow>> GetGroupsAsync(PagedRequest request, CancellationToken ct)
    {
        var search = request.LegacySearch?.Trim();
        var query = PermissionGroups.Query().Where(x => string.IsNullOrWhiteSpace(search) || x.Name.Contains(search) || (x.Description != null && x.Description.Contains(search)))
            .Select(x => new GroupGridRow(x.Id, x.Name, x.Description, x.IsSystemAdmin, x.IsProtected, x.TemplateKey, x.IsActive, x.GroupPermissions.Count, x.CreatedBy, x.CreatedDate, x.UpdatedBy, x.UpdatedDate,x.Name+" "+(x.Description??"")))
            .ApplySearch(request,new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase)
            {
                ["id"]=nameof(GroupGridRow.Id),["name"]=nameof(GroupGridRow.NameSearchText),
                ["permissionCount"]=nameof(GroupGridRow.PermissionCount),
                ["createdBy"]=nameof(GroupGridRow.CreatedBy),["updatedBy"]=nameof(GroupGridRow.UpdatedBy)
            },["name"])
            .ApplyAdvancedFilters(request).ApplySort(request, nameof(GroupGridRow.Name));
        return await query.ToPagedResponseAsync(request, ct);
    }

    public async Task<GroupStats> GetGroupStatsAsync(CancellationToken ct) => new(await PermissionGroups.CountAsync(null, ct), await PermissionGroups.CountAsync(x => x.IsActive, ct), await PermissionGroups.CountAsync(x => x.IsSystemAdmin, ct));

    public async Task<long> CreateGroupAsync(GroupRequest request, CancellationToken ct)
    {
        await ValidateGroup(request, null, ct);
        return await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var ids = request.PermissionIds.Distinct().OrderBy(x => x).ToList();
            var entity = new PermissionGroup { Name = request.Name.Trim(), Description = Normalize(request.Description), IsSystemAdmin = false, IsActive = request.IsActive };
            await PermissionGroups.AddAsync(entity, token); await unitOfWork.SaveChangesAsync(token); await SetGroupPermissions(entity.Id, ids, token); await unitOfWork.SaveChangesAsync(token);
            await audit.WriteAsync(new AuditLogWriteEntry("permission-group.create", "PermissionGroup", entity.Id.ToString(), "Succeeded", "access-control", NewValues: GroupSnapshot(entity, ids), ChangedFields: GroupFields), token);
            return entity.Id;
        }, ct);
    }

    public async Task<long> CopyGroupAsync(long id, CopyGroupRequest request, CancellationToken ct)
    {
        var source = await PermissionGroups.Query().FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw AppException.NotFound("Yetki grubu bulunamadı.");
        await ValidateGroup(new GroupRequest(request.Name, request.Description, false, true, []), null, ct);

        return await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var permissionIds = source.IsSystemAdmin
                ? await PermissionDefinitions.Query().Where(x => x.IsActive).Select(x => x.Id).OrderBy(x => x).ToListAsync(token)
                : await GroupPermissions.Query().Where(x => x.PermissionGroupId == id && x.PermissionDefinition.IsActive).Select(x => x.PermissionDefinitionId).Distinct().OrderBy(x => x).ToListAsync(token);
            var entity = new PermissionGroup
            {
                Name = request.Name.Trim(),
                Description = Normalize(request.Description) ?? $"{source.Name} grubundan kopyalandı.",
                IsSystemAdmin = false,
                IsProtected = false,
                TemplateKey = null,
                IsActive = true
            };
            await PermissionGroups.AddAsync(entity, token);
            await unitOfWork.SaveChangesAsync(token);
            await SetGroupPermissions(entity.Id, permissionIds, token);
            await unitOfWork.SaveChangesAsync(token);
            await audit.WriteAsync(new AuditLogWriteEntry("permission-group.copy", "PermissionGroup", entity.Id.ToString(), "Succeeded", "access-control", NewValues: new { SourceGroupId = source.Id, Group = GroupSnapshot(entity, permissionIds) }, ChangedFields: GroupFields), token);
            return entity.Id;
        }, ct);
    }

    public async Task<GroupDetail> GetGroupAsync(long id, CancellationToken ct) => await PermissionGroups.Query().Where(x => x.Id == id)
        .Select(x => new GroupDetail(x.Id, x.Name, x.Description, x.IsSystemAdmin, x.IsProtected, x.TemplateKey, x.IsActive, x.GroupPermissions.Select(p => p.PermissionDefinitionId).OrderBy(value => value).ToList(), x.GroupPermissions.Select(p => p.PermissionDefinition.Code).OrderBy(value => value).ToList()))
        .FirstOrDefaultAsync(ct) ?? throw AppException.NotFound("Yetki grubu bulunamadı.");

    public async Task UpdateGroupAsync(long id, GroupRequest request, CancellationToken ct)
    {
        var entity = await PermissionGroups.FindByIdAsync(id, true, ct) ?? throw AppException.NotFound("Yetki grubu bulunamadı.");
        if (entity.IsProtected || entity.IsSystemAdmin) throw AppException.Forbidden("Varsayılan yetki grupları düzenlenemez; kopyalayarak özelleştirebilirsiniz."); await ValidateGroup(request, id, ct);
        var oldIds = await GroupPermissions.Query().Where(x => x.PermissionGroupId == id).Select(x => x.PermissionDefinitionId).OrderBy(x => x).ToListAsync(ct); var old = GroupSnapshot(entity, oldIds); var nextIds = request.PermissionIds.Distinct().OrderBy(x => x).ToList();
        await unitOfWork.ExecuteInTransactionAsync(async token => { entity.Name = request.Name.Trim(); entity.Description = Normalize(request.Description); entity.IsActive = request.IsActive; entity.UpdatedDate = DateTime.UtcNow; await SetGroupPermissions(id, nextIds, token); await unitOfWork.SaveChangesAsync(token); await audit.WriteAsync(new AuditLogWriteEntry("permission-group.update", "PermissionGroup", id.ToString(), "Succeeded", "access-control", OldValues: old, NewValues: GroupSnapshot(entity, nextIds), ChangedFields: GroupFields), token); return true; }, ct);
    }

    public async Task DeleteGroupAsync(long id, CancellationToken ct)
    {
        var entity = await PermissionGroups.FindByIdAsync(id, true, ct) ?? throw AppException.NotFound("Yetki grubu bulunamadı."); if (entity.IsProtected || entity.IsSystemAdmin) throw AppException.Forbidden("Varsayılan yetki grupları silinemez; yalnızca görüntülenebilir ve kopyalanabilir.");
        var ids = await GroupPermissions.Query().Where(x => x.PermissionGroupId == id).Select(x => x.PermissionDefinitionId).OrderBy(x => x).ToListAsync(ct); var old = GroupSnapshot(entity, ids);
        await unitOfWork.ExecuteInTransactionAsync(async token => { entity.IsDeleted = true; entity.IsActive = false; entity.DeletedDate = DateTime.UtcNow; var links = await GroupPermissions.Query(true).Where(x => x.PermissionGroupId == id).ToListAsync(token); var users = await UserGroups.Query(true).Where(x => x.PermissionGroupId == id).ToListAsync(token); foreach (var link in links) { link.IsDeleted = true; link.DeletedDate = DateTime.UtcNow; } foreach (var link in users) { link.IsDeleted = true; link.DeletedDate = DateTime.UtcNow; } await unitOfWork.SaveChangesAsync(token); await audit.WriteAsync(new AuditLogWriteEntry("permission-group.delete", "PermissionGroup", id.ToString(), "Succeeded", "access-control", OldValues: old, ChangedFields: ["IsDeleted", "IsActive"]), token); return true; }, ct);
    }

    public async Task<IReadOnlyList<long>> GetUserGroupsAsync(long userId, CancellationToken ct) => await UserGroups.Query().Where(x => x.UserId == userId).Select(x => x.PermissionGroupId).OrderBy(x => x).ToListAsync(ct);

    public async Task SetUserGroupsAsync(long userId, IReadOnlyList<long> ids, CancellationToken ct)
    {
        var user = await Users.FirstOrDefaultAsync(x => x.Id == userId, true, ct) ?? throw AppException.NotFound("Kullanıcı bulunamadı."); var selected = ids.Distinct().OrderBy(x => x).ToList();
        if (await PermissionGroups.CountAsync(x => selected.Contains(x.Id) && x.IsActive, ct) != selected.Count) throw AppException.BadRequest("Geçersiz veya pasif yetki grubu seçimi.");
        var hasSystemAdminGroup = await PermissionGroups.AnyAsync(x => selected.Contains(x.Id) && x.IsSystemAdmin, ct);
        var old = await GetUserGroupsAsync(userId, ct);
        var oldRole = user.Role;
        if (!user.Role.Equals("superadmin", StringComparison.OrdinalIgnoreCase))
            user.Role = hasSystemAdminGroup ? "Admin" : user.Role.Equals("Admin", StringComparison.OrdinalIgnoreCase) ? "User" : user.Role;
        await SetUserGroupLinks(userId, selected, ct); await unitOfWork.SaveChangesAsync(ct);
        await audit.WriteAsync(new AuditLogWriteEntry("user.permission-groups.update", "User", userId.ToString(), "Succeeded", "access-control", OldValues: new { PermissionGroupIds = old, Role = oldRole }, NewValues: new { PermissionGroupIds = selected, user.Role }, ChangedFields: oldRole == user.Role ? ["PermissionGroupIds"] : ["PermissionGroupIds", "Role"]), ct);
    }

    public async Task<MyPermissionsResponse> GetMyPermissionsAsync(long userId, string? role, CancellationToken ct)
    {
        if (string.Equals(role, "superadmin", StringComparison.OrdinalIgnoreCase)) return new(true, []);
        var systemAdmin = await UserGroups.Query().AnyAsync(x => x.UserId == userId && x.PermissionGroup.IsActive && x.PermissionGroup.IsSystemAdmin, ct);
        var codes = await UserGroups.Query().Where(x => x.UserId == userId && x.PermissionGroup.IsActive).SelectMany(x => x.PermissionGroup.GroupPermissions).Where(x => x.PermissionDefinition.IsActive).Select(x => x.PermissionDefinition.Code).Distinct().ToListAsync(ct);
        return new(systemAdmin, codes);
    }

    private async Task ValidatePermission(PermissionRequest request, long? id, CancellationToken ct) { var code = request.Code?.Trim().ToUpperInvariant() ?? ""; var name = request.Name?.Trim() ?? ""; if (code.Length is < 3 or > 150 || name.Length is < 2 or > 200 || request.Description?.Length > 500) throw AppException.BadRequest("İzin alanları geçersiz."); if (await PermissionDefinitions.AnyAsync(x => x.Id != id && x.Code == code, ct)) throw AppException.Conflict("Aynı izin kodu zaten mevcut."); }
    private async Task ValidateGroup(GroupRequest request, long? id, CancellationToken ct) { var name = request.Name?.Trim() ?? ""; if (name.Length is < 2 or > 150 || request.Description?.Length > 500) throw AppException.BadRequest("Grup alanları geçersiz."); if (await PermissionGroups.AnyAsync(x => x.Id != id && x.Name == name, ct)) throw AppException.Conflict("Aynı isimde bir yetki grubu zaten mevcut."); var ids = request.PermissionIds.Distinct().ToList(); if (await PermissionDefinitions.CountAsync(x => ids.Contains(x.Id) && x.IsActive, ct) != ids.Count) throw AppException.BadRequest("Geçersiz veya pasif izin seçimi."); }
    private async Task SetGroupPermissions(long groupId, IReadOnlyCollection<long> ids, CancellationToken ct) { var selected = ids.ToHashSet(); var links = await GroupPermissions.Query(true, true).Where(x => x.PermissionGroupId == groupId).ToListAsync(ct); foreach (var link in links) { var keep = selected.Remove(link.PermissionDefinitionId); link.IsDeleted = !keep; link.DeletedDate = keep ? null : DateTime.UtcNow; link.UpdatedDate = DateTime.UtcNow; } await GroupPermissions.AddRangeAsync(selected.Select(id => new PermissionGroupPermission { PermissionGroupId = groupId, PermissionDefinitionId = id }), ct); }
    private async Task SetUserGroupLinks(long userId, IReadOnlyCollection<long> ids, CancellationToken ct) { var selected = ids.ToHashSet(); var links = await UserGroups.Query(true, true).Where(x => x.UserId == userId).ToListAsync(ct); foreach (var link in links) { var keep = selected.Remove(link.PermissionGroupId); link.IsDeleted = !keep; link.DeletedDate = keep ? null : DateTime.UtcNow; link.UpdatedDate = DateTime.UtcNow; } await UserGroups.AddRangeAsync(selected.Select(id => new UserPermissionGroup { UserId = userId, PermissionGroupId = id }), ct); }
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static object PermissionSnapshot(PermissionDefinition x) => new { x.Code, x.Name, x.Description, x.IsActive, x.AvailableOnWeb, x.AvailableOnMobile };
    private static object GroupSnapshot(PermissionGroup x, IReadOnlyCollection<long> ids) => new { x.Name, x.Description, x.IsSystemAdmin, x.IsProtected, x.TemplateKey, x.IsActive, PermissionIds = ids.OrderBy(v => v).ToArray() };
    private static readonly string[] PermissionFields = ["Code", "Name", "Description", "IsActive", "AvailableOnWeb", "AvailableOnMobile"];
    private static readonly string[] GroupFields = ["Name", "Description", "IsActive", "PermissionIds"];
}
