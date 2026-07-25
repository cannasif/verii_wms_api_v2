using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using verii_wms_api_v2.Modules.Smtp.Domain;
using verii_wms_api_v2.Shared.Infrastructure;
namespace verii_wms_api_v2.Modules.Smtp.Infrastructure;
public sealed class SmtpSettingConfiguration : BaseEntityConfiguration<SmtpSetting>
{
    protected override void ConfigureEntity(EntityTypeBuilder<SmtpSetting> b) { b.ToTable("RII_SMTP_SETTING"); b.Property(x=>x.Host).HasMaxLength(200).IsRequired(); b.Property(x=>x.Username).HasMaxLength(200); b.Property(x=>x.PasswordEncrypted).HasMaxLength(2000); b.Property(x=>x.FromEmail).HasMaxLength(200); b.Property(x=>x.FromName).HasMaxLength(200); }
}
