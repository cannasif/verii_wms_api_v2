using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using verii_wms_api_v2.Modules.SerialNumberPolicy.Domain;
using verii_wms_api_v2.Modules.StockMovement.Domain;
using verii_wms_api_v2.Shared.Infrastructure;
using StockEntity = verii_wms_api_v2.Modules.Stock.Domain.Stock;

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
        b.Property(x => x.NextSequence).HasDefaultValue(1L);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasIndex(x => new { x.BranchCode, x.RuleCode, x.Version }).IsUnique()
            .HasFilter("[IsDeleted] = 0").HasDatabaseName("UX_RII_SERIAL_RULE_CODE_VERSION");
        b.HasIndex(x => new { x.BranchCode, x.Scope, x.StockId, x.StockGroupCode, x.IsActive, x.EffectiveFromUtc })
            .HasDatabaseName("IX_RII_SERIAL_RULE_RESOLVE");
    }
}

public sealed class StockSerialRegistryConfiguration : BaseEntityConfiguration<StockSerialRegistry>
{
    protected override void ConfigureEntity(EntityTypeBuilder<StockSerialRegistry> b)
    {
        b.ToTable("RII_STOCK_SERIAL_REGISTRY");
        b.Property(x => x.SerialNo).HasMaxLength(100).IsRequired();
        b.Property(x => x.NormalizedSerialNo).HasMaxLength(100).IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.GenerationRequestKey).HasMaxLength(100).IsRequired();
        b.Property(x => x.SourceOperationType).HasMaxLength(50);
        b.Property(x => x.VoidedReason).HasMaxLength(500);
        b.Property(x => x.RowVersion).IsRowVersion();

        // Deliberately has no IsDeleted filter: a serial can never be reused for the same stock.
        b.HasIndex(x => new { x.StockId, x.NormalizedSerialNo })
            .IsUnique()
            .HasDatabaseName("UX_RII_STOCK_SERIAL_STOCK_NUMBER");
        b.HasIndex(x => new { x.StockId, x.GenerationRequestKey, x.GenerationOrdinal })
            .IsUnique()
            .HasDatabaseName("UX_RII_STOCK_SERIAL_IDEMPOTENCY");
        b.HasIndex(x => new { x.StockId, x.Status })
            .HasDatabaseName("IX_RII_STOCK_SERIAL_STATUS");
        b.HasOne<StockEntity>().WithMany().HasForeignKey(x => x.StockId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<SerialNumberRule>().WithMany().HasForeignKey(x => x.SerialNumberRuleId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<StockMovementOperation>().WithMany().HasForeignKey(x => x.LastStockMovementOperationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
