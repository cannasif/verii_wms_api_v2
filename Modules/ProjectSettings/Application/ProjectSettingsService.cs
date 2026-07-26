using Microsoft.Extensions.Caching.Memory;
using verii_wms_api_v2.Modules.Audit.Application;
using verii_wms_api_v2.Modules.ProjectSettings.Domain;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.ProjectSettings.Application;

public sealed class ProjectSettingsService(IUnitOfWork unitOfWork, IMemoryCache cache, IAuditLogWriter audit) : IProjectSettingsService
{
    private const string CacheKey = "project-settings:global";
    private static readonly HashSet<string> NumberLocales = ["tr-TR", "en-US", "de-DE"];
    private static readonly HashSet<string> DateFormats = ["dd.MM.yyyy", "MM/dd/yyyy", "yyyy-MM-dd"];
    private static readonly HashSet<string> TimeFormats = ["HH:mm", "HH:mm:ss", "hh:mm a", "hh:mm:ss a"];
    private static readonly HashSet<string> YearFormats = ["yyyy", "yy"];
    private static readonly HashSet<string> TimeZones = ["Europe/Istanbul", "UTC", "Europe/Berlin", "America/New_York"];
    private IGenericRepository<ProjectSetting> Settings => unitOfWork.Repository<ProjectSetting>();

    public async Task<ProjectSettingsResponse> GetAsync(CancellationToken cancellationToken = default)
    {
        if (cache.TryGetValue<ProjectSettingsResponse>(CacheKey, out var cached) && cached is not null) return cached;
        var entity = await Settings.FirstOrDefaultAsync(x => x.SettingKey == "GLOBAL", cancellationToken: cancellationToken)
            ?? DefaultEntity();
        var response = ToResponse(entity);
        cache.Set(CacheKey, response, TimeSpan.FromMinutes(5));
        return response;
    }

    public async Task<ProjectSettingsResponse> UpdateAsync(UpdateProjectSettingsRequest request, CancellationToken cancellationToken = default)
    {
        var normalized = ValidateAndNormalize(request);
        var entity = await Settings.FirstOrDefaultAsync(x => x.SettingKey == "GLOBAL", tracking: true, cancellationToken);
        if (entity is null)
        {
            entity = DefaultEntity();
            await Settings.AddAsync(entity, cancellationToken);
        }
        var old = ToResponse(entity);
        entity.NumberLocale = normalized.NumberLocale;
        entity.DecimalPlaces = normalized.DecimalPlaces;
        entity.DateFormat = normalized.DateFormat;
        entity.TimeFormat = normalized.TimeFormat;
        entity.YearFormat = normalized.YearFormat;
        entity.TimeZoneId = normalized.TimeZoneId;
        entity.SendSerialsToErp = normalized.SendSerialsToErp;
        Settings.Update(entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        var response = ToResponse(entity);
        cache.Remove(CacheKey);
        cache.Set(CacheKey, response, TimeSpan.FromMinutes(5));
        await audit.WriteAsync(new AuditLogWriteEntry("project-settings.update", "ProjectSetting", entity.Id.ToString(), "Succeeded", "project-settings",
            OldValues: old, NewValues: response, ChangedFields: ["NumberLocale", "DecimalPlaces", "DateFormat", "TimeFormat", "YearFormat", "TimeZoneId", "SendSerialsToErp"]), cancellationToken);
        return response;
    }

    private static UpdateProjectSettingsRequest ValidateAndNormalize(UpdateProjectSettingsRequest request)
    {
        var locale = request.NumberLocale?.Trim() ?? "";
        var date = request.DateFormat?.Trim() ?? "";
        var time = request.TimeFormat?.Trim() ?? "";
        var year = request.YearFormat?.Trim() ?? "";
        var zone = request.TimeZoneId?.Trim() ?? "";
        if (!NumberLocales.Contains(locale)) throw AppException.BadRequest("Desteklenmeyen sayı formatı.");
        if (request.DecimalPlaces is < 0 or > 6) throw AppException.BadRequest("Ondalık basamak 0-6 arasında olmalıdır.");
        if (!DateFormats.Contains(date)) throw AppException.BadRequest("Desteklenmeyen tarih formatı.");
        if (!TimeFormats.Contains(time)) throw AppException.BadRequest("Desteklenmeyen saat formatı.");
        if (!YearFormats.Contains(year)) throw AppException.BadRequest("Desteklenmeyen yıl formatı.");
        if (!TimeZones.Contains(zone)) throw AppException.BadRequest("Desteklenmeyen zaman dilimi.");
        return new(locale, request.DecimalPlaces, date, time, year, zone, request.SendSerialsToErp);
    }

    private static ProjectSetting DefaultEntity() => new() { SettingKey = "GLOBAL", BranchCode = "0" };
    private static ProjectSettingsResponse ToResponse(ProjectSetting x) => new(x.Id, x.NumberLocale, x.DecimalPlaces,
        x.DateFormat, x.TimeFormat, x.YearFormat, x.TimeZoneId, x.SendSerialsToErp,
        x.CreatedBy, x.CreatedDate, x.UpdatedBy, x.UpdatedDate);
}
