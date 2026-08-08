using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using verii_wms_api_v2.Modules.Identity.Domain;
using verii_wms_api_v2.Modules.WarehouseAssistant.Domain;
using verii_wms_api_v2.Shared.Infrastructure;

namespace verii_wms_api_v2.Modules.WarehouseAssistant.Infrastructure;

public sealed class WarehouseAssistantConversationConfiguration : BaseEntityConfiguration<WarehouseAssistantConversation>
{
    protected override void ConfigureEntity(EntityTypeBuilder<WarehouseAssistantConversation> builder)
    {
        builder.ToTable("RII_WAREHOUSE_ASSISTANT_CONVERSATIONS");
        builder.Property(x => x.Title).HasMaxLength(160).IsRequired();
        builder.Property(x => x.LastMessageAtUtc).IsRequired();
        builder.Property(x => x.IsArchived).HasDefaultValue(false).IsRequired();
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.UserId, x.BranchCode, x.IsArchived, x.LastMessageAtUtc })
            .HasDatabaseName("IX_RII_WAREHOUSE_ASSISTANT_CONVERSATIONS_User_Branch_LastMessage");
    }
}
public sealed class WarehouseAssistantMessageConfiguration : BaseEntityConfiguration<WarehouseAssistantMessage>
{
    protected override void ConfigureEntity(EntityTypeBuilder<WarehouseAssistantMessage> builder)
    {
        builder.ToTable("RII_WAREHOUSE_ASSISTANT_MESSAGES");
        builder.Property(x => x.Role).HasMaxLength(16).IsRequired();
        builder.Property(x => x.Content).HasMaxLength(4000).IsRequired();
        builder.Property(x => x.Intent).HasMaxLength(64);
        builder.Property(x => x.Scope).HasMaxLength(32);
        builder.Property(x => x.ToolName).HasMaxLength(80);
        builder.Property(x => x.ResponseDataJson).HasColumnType("nvarchar(max)");
        builder.Property(x => x.ContextJson).HasColumnType("nvarchar(max)");
        builder.Property(x => x.CorrelationId).IsRequired();
        builder.HasOne(x => x.Conversation).WithMany(x => x.Messages)
            .HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.ConversationId, x.CreatedDate })
            .HasDatabaseName("IX_RII_WAREHOUSE_ASSISTANT_MESSAGES_Conversation_CreatedDate");
        builder.HasIndex(x => x.CorrelationId)
            .HasDatabaseName("IX_RII_WAREHOUSE_ASSISTANT_MESSAGES_CorrelationId");
    }
}
