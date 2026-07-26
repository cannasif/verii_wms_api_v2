namespace verii_wms_api_v2.Modules.ProjectSettings.Application;

public sealed record ProjectSettingsResponse(long Id, string NumberLocale, int DecimalPlaces, string DateFormat,
    string TimeFormat, string YearFormat, string TimeZoneId, bool SendSerialsToErp, long? CreatedBy, DateTime? CreatedDate,
    long? UpdatedBy, DateTime? UpdatedDate);

public sealed record UpdateProjectSettingsRequest(string NumberLocale, int DecimalPlaces, string DateFormat,
    string TimeFormat, string YearFormat, string TimeZoneId, bool SendSerialsToErp = true);

public interface IProjectSettingsService
{
    Task<ProjectSettingsResponse> GetAsync(CancellationToken cancellationToken = default);
    Task<ProjectSettingsResponse> UpdateAsync(UpdateProjectSettingsRequest request, CancellationToken cancellationToken = default);
}
