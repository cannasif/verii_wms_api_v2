using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using verii_wms_api_v2.Modules.Identity.Domain;
using verii_wms_api_v2.Shared.Infrastructure;

namespace verii_wms_api_v2.Modules.Identity.Infrastructure;

public sealed class PasswordResetTokenConfiguration : BaseEntityConfiguration<PasswordResetToken>
{
    protected override void ConfigureEntity(EntityTypeBuilder<PasswordResetToken> builder)
    {
        builder.ToTable("RII_PASSWORD_RESET_TOKENS");
        builder.Property(x => x.TokenHash).HasMaxLength(64).IsFixedLength().IsRequired();
        builder.Property(x => x.RequestedByIp).HasMaxLength(64);
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasIndex(x => x.TokenHash).IsUnique();
        builder.HasIndex(x => new { x.UserId, x.ExpiresAt });
        builder.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
