using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using verii_wms_api_v2.Modules.SerialNumberPolicy.Domain;
using verii_wms_api_v2.Shared.Infrastructure;

namespace verii_wms_api_v2.Modules.SerialNumberPolicy.Infrastructure;

public sealed class SerialNumberRuleConfiguration : BaseEntityConfiguration<SerialNumberRule>
{
    protected override void ConfigureEntity(EntityTypeBuilder<SerialNumberRule> b)
    {
        b.ToTable("RII_SERIAL_NUMBER_RULES");
        b.Property(x => x.RuleCode).HasMaxLength(50).IsRequired();
        b.Property(x => x.DisplayName).HasMaxLength(150).IsRequired();
        b.Property(x => x.Scope).HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.StockGroupCode).HasMaxLength(50);
        b.Property(x => x.MaskTemplate).HasMaxLength(250).IsRequired();
        b.Property(x => x.CharacterSet).HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.UniquenessScope).HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.Description).HasMaxLength(500);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasIndex(x => new { x.BranchCode, x.RuleCode, x.Version }).IsUnique()
            .HasFilter("[IsDeleted] = 0").HasDatabaseName("UX_RII_SERIAL_RULE_CODE_VERSION");
        b.HasIndex(x => new { x.BranchCode, x.Scope, x.StockId, x.StockGroupCode, x.IsActive, x.EffectiveFromUtc })
            .HasDatabaseName("IX_RII_SERIAL_RULE_RESOLVE");
    }
}
