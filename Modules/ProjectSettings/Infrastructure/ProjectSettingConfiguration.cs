using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using verii_wms_api_v2.Modules.ProjectSettings.Domain;
using verii_wms_api_v2.Shared.Infrastructure;

namespace verii_wms_api_v2.Modules.ProjectSettings.Infrastructure;

public sealed class ProjectSettingConfiguration : BaseEntityConfiguration<ProjectSetting>
{
    protected override void ConfigureEntity(EntityTypeBuilder<ProjectSetting> builder)
    {
        builder.ToTable("RII_PROJECT_SETTINGS");
        builder.Property(x => x.SettingKey).HasMaxLength(30).IsRequired();
        builder.Property(x => x.NumberLocale).HasMaxLength(20).IsRequired();
        builder.Property(x => x.DecimalPlaces).IsRequired();
        builder.Property(x => x.DateFormat).HasMaxLength(30).IsRequired();
        builder.Property(x => x.TimeFormat).HasMaxLength(30).IsRequired();
        builder.Property(x => x.YearFormat).HasMaxLength(10).IsRequired();
        builder.Property(x => x.TimeZoneId).HasMaxLength(100).IsRequired();
        builder.HasIndex(x => x.SettingKey).IsUnique().HasFilter("[IsDeleted] = 0").HasDatabaseName("UX_RII_PROJECT_SETTINGS_KEY");
    }
}
