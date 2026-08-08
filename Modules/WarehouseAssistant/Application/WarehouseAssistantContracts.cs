namespace verii_wms_api_v2.Modules.WarehouseAssistant.Application;

public static class WarehouseAssistantPermissions
{
    public const string QueryAllUsers = "WMS.WAREHOUSE_ASSISTANT.QUERY_ALL_USERS";
}
public enum WarehouseAssistantIntent
{
    Help = 1,
    MyActivities = 2,
    UserActivities = 3,
    SerialBalance = 4,
    SerialReceiptHistory = 5,
    StockLocationBalance = 6,
    Unknown = 99
}

public enum WarehouseAssistantDatePreset
{
    Today = 1,
    Yesterday = 2,
    LastSevenDays = 3,
    ThisWeek = 4,
    LastThirtyDays = 5
}

public sealed record WarehouseAssistantAccess(
    bool CanQueryAllUsers,
    bool CanViewStockBalances,
    bool CanViewStockMovements,
    bool CanViewGoodsReceipts);

public sealed record AskWarehouseAssistantRequest(long? ConversationId, string Message);

public sealed record WarehouseAssistantCapabilities(
    bool CanQueryAllUsers,
    bool CanQuerySerialBalances,
    bool CanQuerySerialReceiptHistory,
    string ScopeLabel,
    IReadOnlyList<string> ExampleQuestions);

public sealed record WarehouseAssistantConversationRow(
    long Id,
    string Title,
    DateTime LastMessageAtUtc,
    bool IsArchived);

public sealed record WarehouseAssistantMessageRow(
    long Id,
    string Role,
    string Content,
    string? Intent,
    string? Scope,
    DateTime? CreatedDate);

public sealed record WarehouseAssistantActivityRow(
    long Id,
    string Action,
    string Description,
    string EntityType,
    string EntityId,
    string Result,
    long? UserId,
    string UserDisplayName,
    DateTime OccurredAtUtc);

public sealed record WarehouseAssistantSerialBalanceRow(
    long Id,
    string SerialNo,
    long StockId,
    string StockCode,
    string StockName,
    int WarehouseCode,
    string WarehouseName,
    string LocationCode,
    string LocationName,
    string? LotNo,
    string UnitCode,
    string StockStatus,
    decimal Quantity,
    decimal ReservedQuantity,
    decimal AvailableQuantity,
    DateTime LastTransactionAtUtc);

public sealed record WarehouseAssistantSerialReceiptRow(
    long MovementEntryId,
    string SerialNo,
    string StockCode,
    string StockName,
    string GoodsReceiptNo,
    long GoodsReceiptId,
    int WarehouseCode,
    string WarehouseName,
    string LocationCode,
    string LocationName,
    decimal Quantity,
    string UnitCode,
    DateTime ReceivedAtUtc,
    long? ReceivedByUserId,
    string ReceivedByDisplayName);

public sealed record WarehouseAssistantStockLocationRow(
    long StockId,
    string StockCode,
    string StockName,
    int WarehouseCode,
    string WarehouseName,
    string LocationCode,
    string LocationName,
    string UnitCode,
    decimal Quantity,
    decimal ReservedQuantity,
    decimal AvailableQuantity);

public sealed record WarehouseAssistantChatResponse(
    long ConversationId,
    long MessageId,
    string Answer,
    WarehouseAssistantIntent Intent,
    string Scope,
    string ProviderMode,
    IReadOnlyList<WarehouseAssistantActivityRow> Activities,
    IReadOnlyList<WarehouseAssistantSerialBalanceRow> SerialBalances,
    IReadOnlyList<WarehouseAssistantSerialReceiptRow> SerialReceipts,
    IReadOnlyList<WarehouseAssistantStockLocationRow> StockLocations,
    IReadOnlyList<string> Suggestions);

public sealed record WarehouseAssistantContext(string? SerialNo, long? StockId, string? StockCode);

public sealed record WarehouseAssistantIntentResolution(
    WarehouseAssistantIntent Intent,
    WarehouseAssistantDatePreset DatePreset,
    string? SerialNo,
    string? StockQuery,
    string? TargetUserQuery,
    bool RequestsAllUsers,
    decimal Confidence,
    string ProviderMode);

public interface IWarehouseAssistantIntentResolver
{
    Task<WarehouseAssistantIntentResolution> ResolveAsync(
        string message,
        WarehouseAssistantContext? context,
        CancellationToken cancellationToken = default);
}

public interface IWarehouseAssistantService
{
    Task<WarehouseAssistantCapabilities> GetCapabilitiesAsync(WarehouseAssistantAccess access, CancellationToken ct = default);
    Task<IReadOnlyList<WarehouseAssistantConversationRow>> GetConversationsAsync(long actorUserId, string branchCode, CancellationToken ct = default);
    Task<IReadOnlyList<WarehouseAssistantMessageRow>> GetMessagesAsync(long conversationId, long actorUserId, string branchCode, CancellationToken ct = default);
    Task<WarehouseAssistantChatResponse> AskAsync(AskWarehouseAssistantRequest request, long actorUserId, string branchCode, WarehouseAssistantAccess access, CancellationToken ct = default);
}
