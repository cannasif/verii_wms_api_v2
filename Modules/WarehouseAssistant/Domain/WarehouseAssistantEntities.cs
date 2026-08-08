using verii_wms_api_v2.Shared.Domain;

namespace verii_wms_api_v2.Modules.WarehouseAssistant.Domain;

public sealed class WarehouseAssistantConversation : BaseEntity
{
    public long UserId { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTime LastMessageAtUtc { get; set; }
    public bool IsArchived { get; set; }
    public ICollection<WarehouseAssistantMessage> Messages { get; set; } = [];
}
public sealed class WarehouseAssistantMessage : BaseEntity
{
    public long ConversationId { get; set; }
    public WarehouseAssistantConversation Conversation { get; set; } = null!;
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? Intent { get; set; }
    public string? Scope { get; set; }
    public string? ToolName { get; set; }
    public string? ResponseDataJson { get; set; }
    public string? ContextJson { get; set; }
    public Guid CorrelationId { get; set; }
}
