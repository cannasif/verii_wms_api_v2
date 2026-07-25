using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using verii_wms_api_v2.Modules.SystemManagement.Domain;
using verii_wms_api_v2.Shared.Infrastructure;

namespace verii_wms_api_v2.Modules.SystemManagement.Infrastructure;

public sealed class HangfireExecutionLogConfiguration : BaseEntityConfiguration<HangfireExecutionLog>
{
    protected override void ConfigureEntity(EntityTypeBuilder<HangfireExecutionLog> builder)
    {
        builder.ToTable("RII_HANGFIRE_EXECUTION_LOGS");
        builder.Property(x => x.JobKey).HasMaxLength(150).IsRequired();
        builder.Property(x => x.HangfireJobId).HasMaxLength(100);
        builder.Property(x => x.TriggerSource).HasMaxLength(30).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(30).IsRequired();
        builder.Property(x => x.ResultSummary).HasMaxLength(2000);
        builder.Property(x => x.ErrorType).HasMaxLength(500);
        builder.Property(x => x.ErrorMessage).HasMaxLength(4000);
        builder.Property(x => x.StackTrace).HasColumnType("nvarchar(max)");
        builder.HasIndex(x => x.StartedAt);
        builder.HasIndex(x => new { x.JobKey, x.Status });
    }
}
