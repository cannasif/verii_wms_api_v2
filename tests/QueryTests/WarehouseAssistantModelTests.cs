using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Identity.Infrastructure;
using verii_wms_api_v2.Modules.WarehouseAssistant.Domain;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class WarehouseAssistantModelTests
{
    [Fact]
    public void Conversation_and_message_tables_are_user_scoped_and_soft_deletable()
    {
        var options = new DbContextOptionsBuilder<WmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        using var db = new WmsDbContext(options);

        var conversation = db.Model.FindEntityType(typeof(WarehouseAssistantConversation));
        var message = db.Model.FindEntityType(typeof(WarehouseAssistantMessage));

        Assert.Equal("RII_WAREHOUSE_ASSISTANT_CONVERSATIONS", conversation?.GetTableName());
        Assert.Equal("RII_WAREHOUSE_ASSISTANT_MESSAGES", message?.GetTableName());
        Assert.NotEmpty(conversation!.GetDeclaredQueryFilters());
        Assert.NotEmpty(message!.GetDeclaredQueryFilters());
        Assert.Contains(conversation!.GetIndexes(), index =>
            index.Properties.Select(x => x.Name).SequenceEqual(new[] { "UserId", "BranchCode", "IsArchived", "LastMessageAtUtc" }));
    }
}
