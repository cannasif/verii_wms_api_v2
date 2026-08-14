using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using verii_wms_api_v2.Modules.ErpIntegration.Domain;
using verii_wms_api_v2.Shared.Infrastructure;

namespace verii_wms_api_v2.Modules.ErpIntegration.Infrastructure;

public sealed class ErpPostingRecordConfiguration : BaseEntityConfiguration<ErpPostingRecord>
{
    protected override void ConfigureEntity(EntityTypeBuilder<ErpPostingRecord> builder)
    {
        builder.ToTable("RII_ERP_POSTING_RECORDS");
        builder.Property(x => x.SourceDocumentNo).HasMaxLength(100).IsRequired();
        builder.Property(x => x.RequestHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ErpDocumentNo).HasMaxLength(100);
        builder.Property(x => x.ErpWaybillNo).HasMaxLength(100);
        builder.Property(x => x.ErpRecordNo).HasMaxLength(100);
        builder.Property(x => x.ErpReferenceNo).HasMaxLength(150);
        builder.Property(x => x.LastErrorCode).HasMaxLength(100);
        builder.Property(x => x.LastErrorMessage).HasMaxLength(4000);
        builder.Property(x => x.TraceId).HasMaxLength(100);
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasIndex(x => new { x.BranchCode, x.SourceType, x.SourceEntityId })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("UX_RII_ERP_POSTING_SOURCE");
        builder.HasIndex(x => new { x.Status, x.UpdatedDate })
            .HasDatabaseName("IX_RII_ERP_POSTING_STATUS_UPDATED");
    }
}

public sealed class ErpIntegrationAttemptConfiguration : BaseEntityConfiguration<ErpIntegrationAttempt>
{
    protected override void ConfigureEntity(EntityTypeBuilder<ErpIntegrationAttempt> builder)
    {
        builder.ToTable("RII_ERP_INTEGRATION_ATTEMPTS");
        builder.Property(x => x.Operation).HasMaxLength(100).IsRequired();
        builder.Property(x => x.HttpMethod).HasMaxLength(10).IsRequired();
        builder.Property(x => x.Endpoint).HasMaxLength(500).IsRequired();
        builder.Property(x => x.RequestHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.RequestPayload).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.ErrorCode).HasMaxLength(100);
        builder.Property(x => x.ErrorMessage).HasMaxLength(4000);
        builder.Property(x => x.ProviderResponse).HasMaxLength(8000);
        builder.Property(x => x.TraceId).HasMaxLength(100);
        builder.HasOne(x => x.PostingRecord)
            .WithMany()
            .HasForeignKey(x => x.ErpPostingRecordId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.ErpPostingRecordId, x.AttemptNo })
            .IsUnique()
            .HasDatabaseName("UX_RII_ERP_ATTEMPT_NO");
        builder.HasIndex(x => new { x.StartedAtUtc, x.IsSuccessful })
            .HasDatabaseName("IX_RII_ERP_ATTEMPT_STARTED_STATUS");
    }
}

public sealed class ErpCancellationRecordConfiguration : BaseEntityConfiguration<ErpCancellationRecord>
{
    protected override void ConfigureEntity(EntityTypeBuilder<ErpCancellationRecord> builder)
    {
        builder.ToTable("RII_ERP_CANCELLATION_RECORDS");
        builder.Property(x => x.RequestHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.LastErrorCode).HasMaxLength(100);
        builder.Property(x => x.LastErrorMessage).HasMaxLength(4000);
        builder.Property(x => x.TraceId).HasMaxLength(100);
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasOne(x => x.PostingRecord)
            .WithMany()
            .HasForeignKey(x => x.ErpPostingRecordId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.ErpPostingRecordId)
            .IsUnique()
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("UX_RII_ERP_CANCELLATION_POSTING");
        builder.HasIndex(x => new { x.Status, x.UpdatedDate })
            .HasDatabaseName("IX_RII_ERP_CANCELLATION_STATUS_UPDATED");
    }
}

public sealed class ErpCancellationAttemptConfiguration : BaseEntityConfiguration<ErpCancellationAttempt>
{
    protected override void ConfigureEntity(EntityTypeBuilder<ErpCancellationAttempt> builder)
    {
        builder.ToTable("RII_ERP_CANCELLATION_ATTEMPTS");
        builder.Property(x => x.Operation).HasMaxLength(100).IsRequired();
        builder.Property(x => x.HttpMethod).HasMaxLength(10).IsRequired();
        builder.Property(x => x.Endpoint).HasMaxLength(500).IsRequired();
        builder.Property(x => x.ErrorCode).HasMaxLength(100);
        builder.Property(x => x.ErrorMessage).HasMaxLength(4000);
        builder.Property(x => x.ProviderResponse).HasMaxLength(8000);
        builder.Property(x => x.TraceId).HasMaxLength(100);
        builder.HasOne(x => x.CancellationRecord)
            .WithMany()
            .HasForeignKey(x => x.ErpCancellationRecordId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.ErpCancellationRecordId, x.AttemptNo })
            .IsUnique()
            .HasDatabaseName("UX_RII_ERP_CANCELLATION_ATTEMPT_NO");
    }
}
