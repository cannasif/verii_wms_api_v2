using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using verii_wms_api_v2.Modules.Identity.Domain;
using verii_wms_api_v2.Shared.Infrastructure;

namespace verii_wms_api_v2.Modules.Identity.Infrastructure;

public sealed class RefreshTokenSessionConfiguration : BaseEntityConfiguration<RefreshTokenSession>
{
    protected override void ConfigureEntity(EntityTypeBuilder<RefreshTokenSession> builder)
    {
        builder.ToTable("RII_REFRESH_TOKEN_SESSIONS");
        builder.Property(x => x.TokenHash).HasMaxLength(64).IsFixedLength().IsRequired();
        builder.Property(x => x.ReplacedByTokenHash).HasMaxLength(64).IsFixedLength();
        builder.Property(x => x.RevokedReason).HasMaxLength(100);
        builder.Property(x => x.CreatedByIp).HasMaxLength(64);
        builder.Property(x => x.RevokedByIp).HasMaxLength(64);
        builder.Property(x => x.UserAgent).HasMaxLength(500);
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasIndex(x => x.TokenHash).IsUnique();
        builder.HasIndex(x => new { x.UserId, x.FamilyId });
        builder.HasIndex(x => x.ExpiresAt);
        builder.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
