namespace verii_wms_api_v2.Modules.ProjectSettings.Application;

public sealed record ProjectSettingsResponse(long Id, string NumberLocale, int DecimalPlaces, string DateFormat,
    string TimeFormat, string YearFormat, string TimeZoneId, bool SendSerialsToErp, int PasswordMinimumLength,
    int PasswordMaximumLength, long? CreatedBy, DateTime? CreatedDate, long? UpdatedBy, DateTime? UpdatedDate);

public sealed record UpdateProjectSettingsRequest(string NumberLocale, int DecimalPlaces, string DateFormat,
    string TimeFormat, string YearFormat, string TimeZoneId, bool SendSerialsToErp = true,
    int PasswordMinimumLength = 6);

public interface IProjectSettingsService
{
    Task<ProjectSettingsResponse> GetAsync(CancellationToken cancellationToken = default);
    Task<ProjectSettingsResponse> UpdateAsync(UpdateProjectSettingsRequest request, CancellationToken cancellationToken = default);
}
