using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using verii_wms_api_v2.Modules.Audit.Domain;
using verii_wms_api_v2.Shared.Infrastructure;

namespace verii_wms_api_v2.Modules.Audit.Infrastructure;

public sealed class AuditLogConfiguration : BaseEntityConfiguration<AuditLog>
{
    protected override void ConfigureEntity(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("RII_AUDIT_LOGS");
        builder.Property(x => x.TraceId).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ActionType).HasMaxLength(100).IsRequired();
        builder.Property(x => x.EntityType).HasMaxLength(128).IsRequired();
        builder.Property(x => x.EntityId).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Result).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Source).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(512);
        builder.Property(x => x.FailureReason).HasMaxLength(2000);
        builder.Property(x => x.RequestPath).HasMaxLength(256);
        builder.Property(x => x.RequestMethod).HasMaxLength(16);
        builder.Property(x => x.PerformedByUserEmail).HasMaxLength(256);
        builder.Property(x => x.OldValuesJson).HasColumnType("nvarchar(max)");
        builder.Property(x => x.NewValuesJson).HasColumnType("nvarchar(max)");
        builder.Property(x => x.ChangedFieldsJson).HasColumnType("nvarchar(max)");
        builder.HasIndex(x => x.TraceId).HasDatabaseName("IX_RII_AUDIT_LOGS_TraceId");
        builder.HasIndex(x => new { x.EntityType, x.EntityId, x.CreatedDate }).HasDatabaseName("IX_RII_AUDIT_LOGS_Entity_CreatedDate");
        builder.HasIndex(x => new { x.PerformedByUserId, x.CreatedDate }).HasDatabaseName("IX_RII_AUDIT_LOGS_User_CreatedDate");
        builder.HasIndex(x => new { x.Source, x.ActionType, x.CreatedDate }).HasDatabaseName("IX_RII_AUDIT_LOGS_Source_Action_CreatedDate");
    }
}
