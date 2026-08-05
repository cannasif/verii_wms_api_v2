using System.Net.Mail;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.AccessControl.Domain;
using verii_wms_api_v2.Modules.Audit.Application;
using verii_wms_api_v2.Modules.Identity.Domain;
using verii_wms_api_v2.Modules.Identity.Application;
using WarehouseEntity = verii_wms_api_v2.Modules.Warehouse.Domain.Warehouse;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.SystemManagement.Application.Users;

public sealed partial class UserManagementService(
    IUnitOfWork unitOfWork,
    IAuditLogWriter audit,
    IIdentitySessionValidator sessionValidator,
    IPasswordPolicyService passwordPolicy) : IUserManagementService
{
    private static readonly IReadOnlyDictionary<string, string> AllowedRoles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    { ["User"] = "User", ["Manager"] = "Manager", ["Admin"] = "Admin" };

    private IGenericRepository<User> Users => unitOfWork.Repository<User>();
    private IGenericRepository<UserDetail> Details => unitOfWork.Repository<UserDetail>();
    private IGenericRepository<PermissionGroup> Groups => unitOfWork.Repository<PermissionGroup>();
    private IGenericRepository<UserPermissionGroup> UserGroups => unitOfWork.Repository<UserPermissionGroup>();
    private IGenericRepository<RefreshTokenSession> RefreshTokens => unitOfWork.Repository<RefreshTokenSession>();
    private IGenericRepository<UserWarehouseAssignment> UserWarehouses => unitOfWork.Repository<UserWarehouseAssignment>();

    public async Task<PagedResponse<UserGridRow>> GetPagedAsync(PagedRequest request, CancellationToken cancellationToken)
    {
        var search = request.Search?.Trim();
        var query = Users.Query().Include(x => x.Detail)
            .Where(x => string.IsNullOrWhiteSpace(search) || x.Username.Contains(search) || x.Email.Contains(search) || (x.Detail != null && (x.Detail.FirstName.Contains(search) || x.Detail.LastName.Contains(search))))
            .Select(x => new UserGridRow(x.Id, x.Username, x.Email, x.Role, x.IsActive, x.LastLoginAt, x.Detail != null ? x.Detail.FirstName : "", x.Detail != null ? x.Detail.LastName : "", null, x.Detail != null ? x.Detail.CreatedDate : null, null, x.Detail != null ? x.Detail.UpdatedDate : null))
            .ApplyAdvancedFilters(request).ApplySort(request, nameof(UserGridRow.Username));
        return await query.ToPagedResponseAsync(request, cancellationToken);
    }

    public async Task<UserDetailResponse> GetByIdAsync(long id, CancellationToken cancellationToken)
    {
        var userGroups = UserGroups.Query();
        var userWarehouses = UserWarehouses.Query();
        return await Users.Query().Where(x => x.Id == id).Select(x => new UserDetailResponse(x.Id, x.Username, x.Email, x.Role, x.IsActive, x.LastLoginAt,
            x.Detail != null ? x.Detail.FirstName : "", x.Detail != null ? x.Detail.LastName : "", x.Detail != null ? x.Detail.Phone : null,
            userGroups.Where(link => link.UserId == x.Id).Select(link => link.PermissionGroupId).OrderBy(groupId => groupId).ToList(),
            userWarehouses.Where(link => link.UserId == x.Id).Select(link => link.WarehouseId).OrderBy(warehouseId => warehouseId).ToList()))
            .FirstOrDefaultAsync(cancellationToken) ?? throw AppException.NotFound("Kullanıcı bulunamadı.");
    }

    public async Task<object> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken)
    {
        await ValidateAsync(request.Username, request.Email, request.Password, request.FirstName, request.LastName, request.PhoneNumber, request.Role, request.PermissionGroupIds, request.WarehouseIds, null, true, false, cancellationToken);
        return await unitOfWork.ExecuteInTransactionAsync<object>(async ct =>
        {
            var groupIds = request.PermissionGroupIds.Distinct().OrderBy(x => x).ToList();
            var user = new User { Username = request.Username.Trim(), Email = request.Email.Trim().ToLowerInvariant(), Role = AllowedRoles[request.Role.Trim()], IsActive = request.IsActive, PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password), PasswordLength = request.Password.Length };
            await Users.AddAsync(user, ct); await unitOfWork.SaveChangesAsync(ct);
            await Details.AddAsync(new UserDetail { UserId = user.Id, FirstName = request.FirstName?.Trim() ?? "", LastName = request.LastName?.Trim() ?? "", Phone = Normalize(request.PhoneNumber), CreatedDate = DateTime.UtcNow }, ct);
            await SetGroupsAsync(user.Id, groupIds, ct);
            await SetWarehousesAsync(user.Id, request.WarehouseIds ?? [], ct);
            await unitOfWork.SaveChangesAsync(ct);
            var warehouseIds = (request.WarehouseIds ?? []).Distinct().OrderBy(x => x).ToArray();
            await audit.WriteAsync(new AuditLogWriteEntry("user.create", "User", user.Id.ToString(), "Succeeded", "identity", NewValues: Snapshot(user, request.FirstName, request.LastName, request.PhoneNumber, groupIds, warehouseIds), ChangedFields: ["Username", "Email", "FirstName", "LastName", "PhoneNumber", "Role", "IsActive", "PermissionGroupIds", "WarehouseIds"]), ct);
            return new { user.Id, user.Username, user.Email };
        }, cancellationToken);
    }

    public async Task<bool> UpdateAsync(long id, UpdateUserRequest request, CancellationToken cancellationToken)
    {
        var user = await Users.Query(tracking: true).Include(x => x.Detail).FirstOrDefaultAsync(x => x.Id == id, cancellationToken) ?? throw AppException.NotFound("Kullanıcı bulunamadı.");
        var primary = user.Id == 1 || user.Role.Equals("superadmin", StringComparison.OrdinalIgnoreCase);
        var username = primary ? user.Username : request.Username;
        var role = primary ? user.Role : request.Role;
        await ValidateAsync(username, request.Email, request.Password, request.FirstName, request.LastName, request.PhoneNumber, role, request.PermissionGroupIds, request.WarehouseIds, id, false, primary, cancellationToken);
        var oldGroups = await UserGroups.Query().Where(x => x.UserId == id).Select(x => x.PermissionGroupId).OrderBy(x => x).ToListAsync(cancellationToken);
        var oldWarehouses = await UserWarehouses.Query().Where(x => x.UserId == id).Select(x => x.WarehouseId).OrderBy(x => x).ToListAsync(cancellationToken);
        var oldValues = Snapshot(user, user.Detail?.FirstName, user.Detail?.LastName, user.Detail?.Phone, oldGroups, oldWarehouses);
        var nextGroups = request.PermissionGroupIds.Distinct().OrderBy(x => x).ToList();
        var nextWarehouses = request.WarehouseIds?.Distinct().OrderBy(x => x).ToList() ?? oldWarehouses;
        var previousIsActive = user.IsActive;
        var invalidateSession = false;
        await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            user.Username = username.Trim(); user.Email = request.Email.Trim().ToLowerInvariant(); user.Role = primary ? user.Role : AllowedRoles[role.Trim()]; user.IsActive = primary || request.IsActive;
            if (user.IsActive != previousIsActive)
            {
                user.TokenVersion++;
                invalidateSession = true;
                await RevokeSessionsAsync(user.Id, user.IsActive ? "UserReactivated" : "UserDeactivated", ct);
            }
            if (!string.IsNullOrWhiteSpace(request.Password))
            {
                user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);
                user.PasswordLength = request.Password.Length;
                user.TokenVersion++;
                invalidateSession = true;
                await RevokeSessionsAsync(user.Id, "PasswordChangedByAdministrator", ct);
            }
            if (user.Detail is null) { user.Detail = new UserDetail { UserId = user.Id, CreatedDate = DateTime.UtcNow }; await Details.AddAsync(user.Detail, ct); }
            user.Detail.FirstName = request.FirstName?.Trim() ?? ""; user.Detail.LastName = request.LastName?.Trim() ?? ""; user.Detail.Phone = Normalize(request.PhoneNumber); user.Detail.UpdatedDate = DateTime.UtcNow;
            await SetGroupsAsync(user.Id, nextGroups, ct);
            if (request.WarehouseIds is not null)
                await SetWarehousesAsync(user.Id, nextWarehouses, ct);
            await unitOfWork.SaveChangesAsync(ct);
            var nextValues = Snapshot(user, user.Detail.FirstName, user.Detail.LastName, user.Detail.Phone, nextGroups, nextWarehouses);
            await audit.WriteAsync(new AuditLogWriteEntry("user.update", "User", user.Id.ToString(), "Succeeded", "identity", OldValues: oldValues, NewValues: nextValues, ChangedFields: ChangedFields(oldValues, nextValues, request.Password)), ct);
            return true;
        }, cancellationToken);
        if (invalidateSession) sessionValidator.Invalidate(id);
        return true;
    }

    public async Task<bool> DeactivateAsync(long id, CancellationToken cancellationToken)
    {
        var user = await Users.FirstOrDefaultAsync(x => x.Id == id, true, cancellationToken) ?? throw AppException.NotFound("Kullanıcı bulunamadı.");
        if (user.Id == 1 || user.Role.Equals("superadmin", StringComparison.OrdinalIgnoreCase)) throw AppException.Forbidden("Ana sistem kullanıcısı pasife alınamaz.");
        if (!user.IsActive) return true;
        user.IsActive = false;
        user.TokenVersion++;
        await RevokeSessionsAsync(user.Id, "UserDeactivated", cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        sessionValidator.Invalidate(user.Id);
        await audit.WriteAsync(new AuditLogWriteEntry("user.deactivate", "User", id.ToString(), "Succeeded", "identity", OldValues: new { IsActive = true }, NewValues: new { IsActive = false }, ChangedFields: ["IsActive"]), cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<long>> UpdateWarehouseAssignmentsAsync(
        long id,
        UpdateUserWarehouseAssignmentsRequest request,
        CancellationToken cancellationToken)
    {
        var user = await Users.FirstOrDefaultAsync(x => x.Id == id, false, cancellationToken)
            ?? throw AppException.NotFound("Kullanıcı bulunamadı.");
        var selected = request.WarehouseIds.Distinct().OrderBy(x => x).ToList();
        if (user.Role.Equals("Admin", StringComparison.OrdinalIgnoreCase)
            || user.Role.Equals("superadmin", StringComparison.OrdinalIgnoreCase))
            selected.Clear();
        if (await unitOfWork.Repository<WarehouseEntity>().CountAsync(x => selected.Contains(x.Id), cancellationToken) != selected.Count)
            throw AppException.BadRequest("Geçersiz depo seçildi.");
        var previous = await UserWarehouses.Query()
            .Where(x => x.UserId == id)
            .Select(x => x.WarehouseId)
            .OrderBy(x => x)
            .ToListAsync(cancellationToken);
        if (previous.SequenceEqual(selected))
            return selected;

        return await unitOfWork.ExecuteInTransactionAsync<IReadOnlyList<long>>(async ct =>
        {
            await SetWarehousesAsync(id, selected, ct);
            await unitOfWork.SaveChangesAsync(ct);
            await audit.WriteAsync(new AuditLogWriteEntry(
                "user.warehouse-assignments.update",
                "User",
                id.ToString(),
                "Succeeded",
                "goods-receipt",
                OldValues: new { WarehouseIds = previous },
                NewValues: new { WarehouseIds = selected },
                ChangedFields: ["WarehouseIds"]), ct);
            return selected;
        }, cancellationToken);
    }

    private async Task ValidateAsync(string? usernameValue, string? emailValue, string? password, string? firstName, string? lastName, string? phone, string? roleValue, IReadOnlyList<long> groupIds, IReadOnlyList<long>? warehouseIds, long? currentId, bool passwordRequired, bool allowSuperAdmin, CancellationToken ct)
    {
        var username = usernameValue?.Trim() ?? ""; var email = emailValue?.Trim().ToLowerInvariant() ?? ""; var role = roleValue?.Trim() ?? "";
        if (username.Length is < 3 or > 100 || !UsernamePattern().IsMatch(username)) throw AppException.BadRequest("Kullanıcı adı 3-100 karakter olmalı ve yalnızca harf, rakam, nokta, tire veya alt çizgi içermelidir.");
        if (email.Length > 200 || !MailAddress.TryCreate(email, out _)) throw AppException.BadRequest("Geçerli bir e-posta adresi giriniz.");
        if (passwordRequired && string.IsNullOrWhiteSpace(password)) throw AppException.BadRequest("Şifre zorunludur.");
        if (!string.IsNullOrEmpty(password)) await passwordPolicy.ValidateAsync(password, ct);
        if (firstName?.Length > 100 || lastName?.Length > 100 || phone?.Length > 40) throw AppException.BadRequest("Profil alanlarının uzunluğu geçersiz.");
        if (!AllowedRoles.ContainsKey(role) && !(allowSuperAdmin && role.Equals("superadmin", StringComparison.OrdinalIgnoreCase))) throw AppException.BadRequest("Geçersiz kullanıcı rolü.");
        if (await Users.AnyAsync(x => x.Id != currentId && x.Username == username, ct)) throw AppException.Conflict("Bu kullanıcı adı zaten kullanılıyor.");
        if (await Users.AnyAsync(x => x.Id != currentId && x.Email == email, ct)) throw AppException.Conflict("Bu e-posta adresi zaten kullanılıyor.");
        var ids = groupIds.Distinct().ToList();
        if (await Groups.CountAsync(x => ids.Contains(x.Id) && x.IsActive, ct) != ids.Count) throw AppException.BadRequest("Geçersiz veya pasif yetki grubu seçildi.");
        if (!allowSuperAdmin)
        {
            var hasSystemAdminGroup = await Groups.AnyAsync(x => ids.Contains(x.Id) && x.IsSystemAdmin && x.IsActive, ct);
            var hasAdminRole = role.Equals("Admin", StringComparison.OrdinalIgnoreCase);
            if (hasSystemAdminGroup != hasAdminRole)
                throw AppException.BadRequest("Admin rolü yalnızca System Administrators grubu ile birlikte kullanılabilir. Grup seçildiğinde rol Admin olmalı; grup seçili değilse Admin rolü verilemez.");
        }
        var selectedWarehouses = (warehouseIds ?? []).Distinct().ToList();
        if (await unitOfWork.Repository<WarehouseEntity>().CountAsync(x => selectedWarehouses.Contains(x.Id), ct) != selectedWarehouses.Count)
            throw AppException.BadRequest("Geçersiz depo seçildi.");
    }

    private async Task SetGroupsAsync(long userId, IReadOnlyCollection<long> groupIds, CancellationToken ct)
    {
        var selected = groupIds.ToHashSet();
        var links = await UserGroups.Query(tracking: true, ignoreQueryFilters: true).Where(x => x.UserId == userId).ToListAsync(ct);
        foreach (var link in links) { var keep = selected.Remove(link.PermissionGroupId); link.IsDeleted = !keep; link.DeletedDate = keep ? null : DateTime.UtcNow; link.UpdatedDate = DateTime.UtcNow; }
        await UserGroups.AddRangeAsync(selected.Select(groupId => new UserPermissionGroup { UserId = userId, PermissionGroupId = groupId }), ct);
    }

    private async Task SetWarehousesAsync(long userId, IReadOnlyCollection<long> warehouseIds, CancellationToken ct)
    {
        var selected = warehouseIds.ToHashSet();
        var warehouseBranches = await unitOfWork.Repository<WarehouseEntity>().Query()
            .Where(x => selected.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.BranchCode, ct);
        var links = await UserWarehouses.Query(tracking: true, ignoreQueryFilters: true)
            .Where(x => x.UserId == userId).ToListAsync(ct);
        foreach (var link in links)
        {
            var keep = selected.Remove(link.WarehouseId);
            link.IsDeleted = !keep;
            link.DeletedDate = keep ? null : DateTime.UtcNow;
            link.UpdatedDate = DateTime.UtcNow;
            if (keep && warehouseBranches.TryGetValue(link.WarehouseId, out var branchCode))
                link.BranchCode = branchCode;
        }
        await UserWarehouses.AddRangeAsync(selected.Select(warehouseId =>
            new UserWarehouseAssignment
            {
                UserId = userId,
                WarehouseId = warehouseId,
                BranchCode = warehouseBranches[warehouseId]
            }));
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private async Task RevokeSessionsAsync(long userId, string reason, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var sessions = await RefreshTokens.Query(tracking: true)
            .Where(x => x.UserId == userId && x.RevokedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var session in sessions)
        {
            session.RevokedAt = now;
            session.RevokedReason = reason;
        }
    }

    private static UserAuditState Snapshot(User user, string? firstName, string? lastName, string? phone, IReadOnlyCollection<long> groups, IReadOnlyCollection<long> warehouses) => new(user.Username, user.Email, firstName?.Trim() ?? "", lastName?.Trim() ?? "", Normalize(phone), user.Role, user.IsActive, groups.OrderBy(x => x).ToArray(), warehouses.OrderBy(x => x).ToArray());
    private static IReadOnlyCollection<string> ChangedFields(UserAuditState oldValues, UserAuditState nextValues, string? password)
    {
        var fields = new List<string>();
        if (oldValues.Username != nextValues.Username) fields.Add("Username");
        if (oldValues.Email != nextValues.Email) fields.Add("Email");
        if (oldValues.FirstName != nextValues.FirstName) fields.Add("FirstName");
        if (oldValues.LastName != nextValues.LastName) fields.Add("LastName");
        if (oldValues.PhoneNumber != nextValues.PhoneNumber) fields.Add("PhoneNumber");
        if (oldValues.Role != nextValues.Role) fields.Add("Role");
        if (oldValues.IsActive != nextValues.IsActive) fields.Add("IsActive");
        if (!oldValues.PermissionGroupIds.SequenceEqual(nextValues.PermissionGroupIds)) fields.Add("PermissionGroupIds");
        if (!oldValues.WarehouseIds.SequenceEqual(nextValues.WarehouseIds)) fields.Add("WarehouseIds");
        if (!string.IsNullOrWhiteSpace(password)) fields.Add("Password");
        return fields;
    }

    private sealed record UserAuditState(string Username, string Email, string FirstName, string LastName, string? PhoneNumber, string Role, bool IsActive, IReadOnlyList<long> PermissionGroupIds, IReadOnlyList<long> WarehouseIds);

    [GeneratedRegex("^[a-zA-Z0-9._-]+$", RegexOptions.CultureInvariant)] private static partial Regex UsernamePattern();
}
