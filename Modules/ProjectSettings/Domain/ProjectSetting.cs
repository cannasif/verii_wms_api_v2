using verii_wms_api_v2.Shared.Domain;

namespace verii_wms_api_v2.Modules.ProjectSettings.Domain;

public sealed class ProjectSetting : BaseEntity
{
    public string SettingKey { get; set; } = "GLOBAL";
    public string NumberLocale { get; set; } = "tr-TR";
    public int DecimalPlaces { get; set; } = 2;
    public string DateFormat { get; set; } = "dd.MM.yyyy";
    public string TimeFormat { get; set; } = "HH:mm";
    public string YearFormat { get; set; } = "yyyy";
    public string TimeZoneId { get; set; } = "Europe/Istanbul";
    public bool SendSerialsToErp { get; set; } = true;
}
