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
    BarcodeLookup = 7,
    StockMovementHistory = 8,
    AssignedTasks = 9,
    GoodsReceiptAnalysis = 10,
    ParameterHelp = 11,
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
    bool CanViewGoodsReceipts,
    bool CanViewWarehouseTransfers = false,
    bool CanViewShipping = false,
    bool CanViewWarehouseInbound = false,
    bool CanViewWarehouseOutbound = false,
    bool CanViewProductionTransfers = false);

public sealed record WarehouseAssistantParameterHint(
    string Module,
    string Field,
    string? Value = null);

public sealed record AskWarehouseAssistantRequest(
    long? ConversationId,
    string Message,
    WarehouseAssistantParameterHint? ParameterHint = null);

public sealed record WarehouseAssistantCapabilities(
    bool CanQueryAllUsers,
    bool CanQuerySerialBalances,
    bool CanQuerySerialReceiptHistory,
    bool CanQueryBarcode,
    bool CanQueryStockMovements,
    bool CanQueryAssignedTasks,
    string ScopeLabel,
    IReadOnlyList<string> ExampleQuestions,
    bool CanQueryGoodsReceiptAnalysis = false,
    bool CanExplainParameters = true);

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
    DateTime? CreatedDate,
    WarehouseAssistantChatResponse? Result);

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

public sealed record WarehouseAssistantBarcodeRow(
    string Barcode,
    string Source,
    long StockId,
    string StockCode,
    string StockName,
    long? YapCodeId,
    string? YapCode,
    decimal? EncodedQuantity,
    string UnitCode,
    string? LotNo,
    string? SerialNo,
    DateOnly? ManufacturingDate,
    DateOnly? ExpirationDate,
    bool RequireSerial,
    bool RequireLot,
    bool RequireManufacturingDate,
    bool RequireExpirationDate,
    IReadOnlyList<string> MissingFields);

public sealed record WarehouseAssistantMovementRow(
    long EntryId,
    long OperationId,
    string OperationType,
    string OperationStatus,
    string? ReferenceType,
    string? ReferenceNo,
    long? ReferenceId,
    long StockId,
    string StockCode,
    string StockName,
    int WarehouseCode,
    string WarehouseName,
    string LocationCode,
    string LocationName,
    decimal QuantityDelta,
    string UnitCode,
    string? LotNo,
    string? SerialNo,
    string StockStatus,
    DateTime OccurredAtUtc,
    bool IsReversal);

public sealed record WarehouseAssistantTaskRow(
    string Module,
    long TaskId,
    string TaskNo,
    string TaskType,
    string Status,
    byte Priority,
    long DocumentId,
    string DocumentNo,
    long WarehouseId,
    int WarehouseCode,
    string WarehouseName,
    decimal PlannedQuantity,
    decimal ProcessedQuantity,
    decimal RemainingQuantity,
    DateTimeOffset? PlannedAtUtc,
    DateTimeOffset? DueAtUtc,
    long? AssigneeUserId,
    string AssigneeDisplayName);

public sealed record WarehouseAssistantGoodsReceiptRow(
    long GoodsReceiptId,
    string DocumentNo,
    DateOnly DocumentDate,
    DateTimeOffset? ReceivedAtUtc,
    long? SupplierId,
    string SupplierCode,
    string SupplierName,
    int WarehouseCode,
    string WarehouseName,
    long StockId,
    string StockCode,
    string StockName,
    string? YapCode,
    string UnitCode,
    decimal ReceivedQuantity,
    decimal AcceptedQuantity,
    decimal RejectedQuantity,
    decimal QuarantineQuantity,
    decimal PutawayQuantity,
    string Status,
    string QualityStatus,
    string ErpIntegrationStatus,
    long? ReceivedByUserId,
    string ReceivedByDisplayName);

public sealed record WarehouseAssistantParameterGuideRow(
    string Module,
    string Field,
    string? Value);

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
    WarehouseAssistantBarcodeRow? Barcode,
    IReadOnlyList<WarehouseAssistantMovementRow> Movements,
    IReadOnlyList<WarehouseAssistantTaskRow> Tasks,
    IReadOnlyList<string> Suggestions,
    IReadOnlyList<WarehouseAssistantGoodsReceiptRow>? GoodsReceipts = null,
    IReadOnlyList<WarehouseAssistantParameterGuideRow>? ParameterGuides = null);

public sealed record WarehouseAssistantContext(
    string? SerialNo,
    long? StockId,
    string? StockCode,
    string? Barcode = null,
    long? SupplierId = null,
    string? SupplierCode = null,
    string? SupplierName = null,
    DateOnly? DateFrom = null,
    DateOnly? DateTo = null,
    string? ParameterModule = null,
    string? ParameterField = null,
    string? ParameterValue = null);

public sealed record WarehouseAssistantIntentResolution(
    WarehouseAssistantIntent Intent,
    WarehouseAssistantDatePreset DatePreset,
    string? SerialNo,
    string? StockQuery,
    string? Barcode,
    string? TargetUserQuery,
    bool RequestsAllUsers,
    decimal Confidence,
    string ProviderMode,
    DateOnly? DateFrom = null,
    DateOnly? DateTo = null,
    string? SupplierQuery = null,
    string? ParameterModule = null,
    string? ParameterField = null,
    string? ParameterValue = null);

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
    Task ArchiveConversationAsync(long conversationId, long actorUserId, string branchCode, CancellationToken ct = default);
}
