using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Audit.Application;
using verii_wms_api_v2.Modules.IncomingInvoice.Domain;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.IncomingInvoice.Application;

public sealed class ELogoConnectionService(
    IUnitOfWork unitOfWork,
    IDataProtectionProvider dataProtectionProvider,
    IAuditLogWriter audit) : IELogoConnectionService
{
    private static readonly IReadOnlyDictionary<string,string> SearchColumns=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase)
    {
        ["id"]=nameof(ELogoConnectionRow.Id),["displayName"]=nameof(ELogoConnectionRow.DisplaySearchText),
        ["vkn"]=nameof(ELogoConnectionRow.Vkn),["username"]=nameof(ELogoConnectionRow.Username),
        ["source"]=nameof(ELogoConnectionRow.Source),
        ["createdBy"]=nameof(ELogoConnectionRow.CreatedBy),["updatedBy"]=nameof(ELogoConnectionRow.UpdatedBy)
    };
    private static readonly string[] DefaultSearchColumns=["displayName","vkn","username","source"];
    internal const string ProtectorPurpose = "V3RII.WmsV2.IncomingInvoice.ELogoConnection.Password.v1";
    private readonly IDataProtector protector = dataProtectionProvider.CreateProtector(ProtectorPurpose);
    private IGenericRepository<ELogoConnection> Connections => unitOfWork.Repository<ELogoConnection>();

    public async Task<IReadOnlyList<ELogoConnectionRow>> GetSelectableAsync(string branchCode, CancellationToken ct = default)
    {
        var branch = NormalizeBranch(branchCode);
        var entities = await Connections.Query()
            .Where(x => x.BranchCode == branch && x.IsActive)
            .OrderByDescending(x => x.IsDefault).ThenBy(x => x.DisplayName)
            .ToListAsync(ct);
        return entities.Select(ToRow).ToList();
    }

    public async Task<PagedResponse<ELogoConnectionRow>> GetPagedAsync(
        string branchCode, PagedRequest request, CancellationToken ct = default)
    {
        var branch = NormalizeBranch(branchCode);
        var search = request.LegacySearch?.Trim();
        var query = Connections.Query()
            .Where(x => x.BranchCode == branch
                && (string.IsNullOrWhiteSpace(search)
                    || x.Key.Contains(search)
                    || x.DisplayName.Contains(search)
                    || x.Vkn.Contains(search)
                    || x.Source.Contains(search)))
            .Select(x => new ELogoConnectionRow(
                x.Id, x.BranchCode, x.Key, x.DisplayName, x.Vkn, x.Username, x.Source,
                x.EndpointUrl, x.ApplicationName, x.Version, x.TimeoutSeconds, x.IsActive,
                x.IsDefault, x.PasswordCipherText != null && x.PasswordCipherText != "",
                x.Description, x.CreatedBy, x.CreatedDate, x.UpdatedBy, x.UpdatedDate, x.RowVersion,
                x.DisplayName+" "+x.Key));

        return await query.ApplySearch(request,SearchColumns,DefaultSearchColumns).ApplyAdvancedFilters(request)
            .ApplySort(request, nameof(ELogoConnectionRow.DisplayName))
            .ToPagedResponseAsync(request, ct);
    }

    public async Task<ELogoConnectionRow> GetAsync(long id, string branchCode, CancellationToken ct = default)
    {
        var branch = NormalizeBranch(branchCode);
        var entity = await Connections.FirstOrDefaultAsync(x => x.Id == id && x.BranchCode == branch, false, ct)
            ?? throw AppException.NotFound("eLogo bağlantı tanımı bulunamadı.");
        return ToRow(entity);
    }

    public async Task<ELogoConnectionRow> CreateAsync(SaveELogoConnectionRequest request, CancellationToken ct = default)
    {
        var normalized = Normalize(request, requirePassword: true);
        if (await Connections.AnyAsync(x => x.BranchCode == normalized.BranchCode && x.Key == normalized.Key, ct))
            throw AppException.Conflict("Aynı anahtara sahip eLogo bağlantısı zaten mevcut.");

        return await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            if (normalized.IsDefault) await ClearDefaultAsync(normalized.BranchCode, null, token);
            var entity = new ELogoConnection
            {
                BranchCode = normalized.BranchCode,
                Key = normalized.Key,
                DisplayName = normalized.DisplayName,
                Vkn = normalized.Vkn,
                Username = normalized.Username,
                PasswordCipherText = protector.Protect(normalized.Password!),
                Source = normalized.Source,
                EndpointUrl = normalized.EndpointUrl,
                ApplicationName = normalized.ApplicationName,
                Version = normalized.Version,
                TimeoutSeconds = normalized.TimeoutSeconds,
                IsActive = normalized.IsActive,
                IsDefault = normalized.IsDefault,
                Description = normalized.Description
            };
            await Connections.AddAsync(entity, token);
            await unitOfWork.SaveChangesAsync(token);
            await audit.WriteAsync(new AuditLogWriteEntry(
                "incoming-invoice.connection.create", nameof(ELogoConnection), entity.Id.ToString(),
                "Succeeded", "incoming-invoice", NewValues: SafeSnapshot(entity),
                ChangedFields: ConnectionFields), token);
            return ToRow(entity);
        }, ct);
    }

    public async Task<ELogoConnectionRow> UpdateAsync(
        long id, SaveELogoConnectionRequest request, CancellationToken ct = default)
    {
        var normalized = Normalize(request, requirePassword: false);
        return await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var entity = await Connections.FirstOrDefaultAsync(
                x => x.Id == id && x.BranchCode == normalized.BranchCode, true, token)
                ?? throw AppException.NotFound("eLogo bağlantı tanımı bulunamadı.");
            if (request.RowVersion is { Length: > 0 } && !entity.RowVersion.SequenceEqual(request.RowVersion))
                throw AppException.Conflict("Bağlantı başka bir kullanıcı tarafından güncellendi. Listeyi yenileyin.");

            var old = SafeSnapshot(entity);
            if (normalized.IsDefault) await ClearDefaultAsync(normalized.BranchCode, entity.Id, token);
            entity.DisplayName = normalized.DisplayName;
            entity.Vkn = normalized.Vkn;
            entity.Username = normalized.Username;
            entity.Source = normalized.Source;
            entity.EndpointUrl = normalized.EndpointUrl;
            entity.ApplicationName = normalized.ApplicationName;
            entity.Version = normalized.Version;
            entity.TimeoutSeconds = normalized.TimeoutSeconds;
            entity.IsActive = normalized.IsActive;
            entity.IsDefault = normalized.IsDefault;
            entity.Description = normalized.Description;
            entity.UpdatedDate = DateTime.UtcNow;
            if (!string.IsNullOrWhiteSpace(normalized.Password))
                entity.PasswordCipherText = protector.Protect(normalized.Password);

            await unitOfWork.SaveChangesAsync(token);
            await audit.WriteAsync(new AuditLogWriteEntry(
                "incoming-invoice.connection.update", nameof(ELogoConnection), entity.Id.ToString(),
                "Succeeded", "incoming-invoice", OldValues: old, NewValues: SafeSnapshot(entity),
                ChangedFields: ConnectionFields), token);
            return ToRow(entity);
        }, ct);
    }

    public async Task DeleteAsync(long id, string branchCode, CancellationToken ct = default)
    {
        var branch = NormalizeBranch(branchCode);
        await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var entity = await Connections.FirstOrDefaultAsync(
                x => x.Id == id && x.BranchCode == branch, true, token)
                ?? throw AppException.NotFound("eLogo bağlantı tanımı bulunamadı.");
            var used = await unitOfWork.Repository<IncomingInvoiceHeader>().AnyAsync(
                x => x.ELogoConnectionId == entity.Id, token);
            if (used)
                throw AppException.Conflict("Arşivlenmiş faturaları kullanan bağlantı silinemez; pasife alınabilir.");
            var old = SafeSnapshot(entity);
            entity.IsDeleted = true;
            entity.IsActive = false;
            entity.IsDefault = false;
            entity.DeletedDate = DateTime.UtcNow;
            await unitOfWork.SaveChangesAsync(token);
            await audit.WriteAsync(new AuditLogWriteEntry(
                "incoming-invoice.connection.delete", nameof(ELogoConnection), entity.Id.ToString(),
                "Succeeded", "incoming-invoice", OldValues: old,
                ChangedFields: ["IsDeleted", "IsActive", "IsDefault"]), token);
            return true;
        }, ct);
    }

    internal string UnprotectPassword(ELogoConnection connection)
    {
        if (string.IsNullOrWhiteSpace(connection.PasswordCipherText))
            throw AppException.BadRequest("eLogo bağlantısında web servis şifresi tanımlı değil.");
        try
        {
            return protector.Unprotect(connection.PasswordCipherText);
        }
        catch (CryptographicException)
        {
            throw new AppException(StatusCodes.Status422UnprocessableEntity,
                "eLogo bağlantı şifresi bu sunucuda çözülemedi. Şifreyi yeniden kaydedin.");
        }
    }

    private async Task ClearDefaultAsync(string branch, long? exceptId, CancellationToken ct)
    {
        var defaults = await Connections.Query(tracking: true)
            .Where(x => x.BranchCode == branch && x.IsDefault && x.Id != exceptId)
            .ToListAsync(ct);
        foreach (var item in defaults)
        {
            item.IsDefault = false;
            item.UpdatedDate = DateTime.UtcNow;
        }
    }

    private static SaveELogoConnectionRequest Normalize(SaveELogoConnectionRequest request, bool requirePassword)
    {
        var branch = NormalizeBranch(request.BranchCode);
        var key = Required(request.Key, "Bağlantı anahtarı", 80).ToLowerInvariant();
        var displayName = Required(request.DisplayName, "Firma adı", 200);
        var vkn = new string((request.Vkn ?? string.Empty).Where(char.IsDigit).ToArray());
        if (vkn.Length is not (10 or 11)) throw AppException.BadRequest("VKN/TCKN 10 veya 11 rakam olmalıdır.");
        var username = Required(request.Username, "Kullanıcı adı", 100);
        var source = Required(request.Source, "Şirket/source", 100);
        var password = Optional(request.Password, 500);
        if (requirePassword && password is null) throw AppException.BadRequest("Web servis şifresi zorunludur.");
        if (request.TimeoutSeconds is < 10 or > 600) throw AppException.BadRequest("Zaman aşımı 10-600 saniye arasında olmalıdır.");
        var endpoint = Optional(request.EndpointUrl, 500);
        if (endpoint is not null
            && (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps))
            throw AppException.BadRequest("eLogo servis adresi geçerli bir HTTPS adresi olmalıdır.");
        return request with
        {
            BranchCode = branch,
            Key = key,
            DisplayName = displayName,
            Vkn = vkn,
            Username = username,
            Password = password,
            Source = source,
            EndpointUrl = endpoint,
            ApplicationName = Optional(request.ApplicationName, 100),
            Version = Optional(request.Version, 20),
            Description = Optional(request.Description, 500)
        };
    }

    internal static string NormalizeBranch(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "0" : value.Trim().Length <= 20
            ? value.Trim()
            : throw AppException.BadRequest("Şube kodu en fazla 20 karakter olabilir.");

    private static string Required(string? value, string name, int max)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized)) throw AppException.BadRequest($"{name} zorunludur.");
        if (normalized.Length > max) throw AppException.BadRequest($"{name} en fazla {max} karakter olabilir.");
        return normalized;
    }

    private static string? Optional(string? value, int max)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (normalized?.Length > max) throw AppException.BadRequest($"Alan en fazla {max} karakter olabilir.");
        return normalized;
    }

    private static ELogoConnectionRow ToRow(ELogoConnection x) => new(
        x.Id, x.BranchCode, x.Key, x.DisplayName, x.Vkn, x.Username, x.Source,
        x.EndpointUrl, x.ApplicationName, x.Version, x.TimeoutSeconds, x.IsActive,
        x.IsDefault, !string.IsNullOrWhiteSpace(x.PasswordCipherText), x.Description,
        x.CreatedBy, x.CreatedDate, x.UpdatedBy, x.UpdatedDate, x.RowVersion);

    private static object SafeSnapshot(ELogoConnection x) => new
    {
        x.BranchCode, x.Key, x.DisplayName, x.Vkn, x.Username, x.Source, x.EndpointUrl,
        x.ApplicationName, x.Version, x.TimeoutSeconds, x.IsActive, x.IsDefault,
        IsConfigured = !string.IsNullOrWhiteSpace(x.PasswordCipherText), x.Description
    };

    private static readonly string[] ConnectionFields =
    [
        "Key", "DisplayName", "Vkn", "Username", "Source", "EndpointUrl",
        "ApplicationName", "Version", "TimeoutSeconds", "IsActive", "IsDefault",
        "IsConfigured", "Description"
    ];
}
