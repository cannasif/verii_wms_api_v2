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
    SteelVehicleAnalysis = 12,
    WarehouseTransferAnalysis = 13,
    ShiftBrief = 14,
    OperationalExceptions = 15,
    Traceability = 16,
    ProcessBlockers = 17,
    Composite = 18,
    WarehouseOverview = 19,
    LocationInventory = 20,
    InventoryInsights = 21,
    InventoryCountAnalysis = 22,
    GeneratorProductionAnalysis = 23,
    NavigationHelp = 24,
    Unknown = 99
}

public enum WarehouseAssistantQueryKind
{
    None = 0,
    WarehouseCount = 1,
    WarehouseList = 2,
    WarehouseLocations = 3,
    WarehouseStockTotals = 4,
    LocationContents = 10,
    LocationEmptyCheck = 11,
    LocationCapacity = 12,
    LocationListByType = 13,
    ZeroStock = 20,
    NonZeroStock = 21,
    RankedStock = 22,
    StockGroupComparison = 23,
    CriticalStockUnsupported = 24,
    InventoryCountList = 30,
    InventoryCountVariance = 31,
    ProductionProjects = 40,
    ProductionOperations = 41,
    ProductionMaterialShortages = 42,
    ProductionQualityWaiting = 43,
    ProductionPlannedVsActual = 44,
    ProductionOverdue = 45,
    ProductionProjectStatus = 46,
    Navigation = 50
}

public enum WarehouseAssistantStockMeasure
{
    Physical = 1,
    Available = 2,
    Reserved = 3
}

public enum WarehouseAssistantSortDirection
{
    None = 0,
    QuantityAscending = 1,
    QuantityDescending = 2,
    VarianceDescending = 3,
    DateDescending = 4
}

public enum WarehouseAssistantTransferScope
{
    All = 0,
    InterWarehouse = 1,
    Production = 2
}

public enum WarehouseAssistantDatePreset
{
    Today = 1,
    Yesterday = 2,
    LastSevenDays = 3,
    ThisWeek = 4,
    LastThirtyDays = 5,
    LastWeek = 6
}

public sealed record WarehouseAssistantRoutingInfo(
    string Version,
    string RoutingMode,
    bool SemanticRoutingAvailable,
    string? SemanticModel);

public sealed record WarehouseAssistantAccess(
    bool CanQueryAllUsers,
    bool CanViewStockBalances,
    bool CanViewStockMovements,
    bool CanViewGoodsReceipts,
    bool CanViewWarehouseTransfers = false,
    bool CanViewShipping = false,
    bool CanViewWarehouseInbound = false,
    bool CanViewWarehouseOutbound = false,
    bool CanViewProductionTransfers = false,
    bool CanViewSteelVehicles = false,
    bool CanViewQuality = false,
    bool CanViewPacking = false,
    bool CanViewProcurement = false,
    bool CanViewKkd = false,
    bool CanViewSystemHealth = false);

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
    bool CanExplainParameters = true,
    bool CanQuerySteelVehicleAnalysis = false,
    bool CanQueryTransferAnalysis = false,
    bool CanQueryShiftBrief = true,
    bool CanQueryOperationalExceptions = false,
    bool CanQueryTraceability = false,
    bool CanQueryProcessBlockers = false,
    string AssistantVersion = "2.5.0",
    string RoutingMode = "LocalHybrid",
    bool SemanticRoutingAvailable = false,
    string? SemanticModel = null,
    bool CanRunCompoundQueries = true);

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

public sealed record WarehouseAssistantSteelVehicleRow(
    long VehicleCheckInId,
    string PlateNo,
    string? TrailerPlateNo,
    string DriverName,
    string? CarrierName,
    int DeclaredSteelSheetCount,
    int AcceptedPlateCount,
    int UnresolvedPlateCount,
    string Status,
    DateTimeOffset CheckedInAtUtc,
    DateOnly BusinessDate,
    string? CustomerCode,
    string? CustomerName);

public sealed record WarehouseAssistantTransferRow(
    long TransferId,
    string DocumentNo,
    DateOnly DocumentDate,
    string BusinessContext,
    int SourceWarehouseCode,
    string SourceWarehouseName,
    int TargetWarehouseCode,
    string TargetWarehouseName,
    string Status,
    string ApprovalStatus,
    string ErpIntegrationStatus,
    int LineCount,
    string UnitCode,
    decimal RequestedQuantity,
    decimal PickedQuantity,
    decimal ShippedQuantity,
    decimal ReceivedQuantity,
    decimal PutawayQuantity,
    decimal ShortClosedQuantity,
    string? ExternalReferenceNo,
    DateTimeOffset? CompletedAtUtc);

public sealed record WarehouseAssistantEntityCandidateRow(
    string EntityType,
    long? EntityId,
    string Code,
    string Name,
    string MatchedBy,
    decimal MatchScore,
    string SelectionMessage);

public sealed record WarehouseAssistantSummaryMetricRow(
    string Key,
    string Label,
    decimal Value,
    string Unit,
    string Severity,
    string Module,
    string? Route = null);

public sealed record WarehouseAssistantExceptionRow(
    string Code,
    string Severity,
    string Module,
    string Title,
    string Description,
    string EntityType,
    long? EntityId,
    string? DocumentNo,
    string Status,
    DateTimeOffset? DetectedAtUtc,
    decimal? AgeHours,
    string SuggestedAction,
    string? Route = null);

public sealed record WarehouseAssistantTraceabilityEventRow(
    string EventKey,
    DateTimeOffset OccurredAtUtc,
    string Stage,
    string EventType,
    string DocumentType,
    long? DocumentId,
    string? DocumentNo,
    long StockId,
    string StockCode,
    string StockName,
    string? SerialNo,
    string? LotNo,
    decimal Quantity,
    string UnitCode,
    int? WarehouseCode,
    string? WarehouseName,
    string? LocationCode,
    string? LocationName,
    string Status,
    string ActorDisplayName,
    bool IsReversal,
    string? Route = null);

public sealed record WarehouseAssistantEvidenceRow(
    string Source,
    string Tool,
    int RecordCount,
    DateTimeOffset GeneratedAtUtc,
    DateTimeOffset? DataAsOfUtc,
    string Scope,
    string Filters,
    bool IsTruncated,
    string? Route = null);

public sealed record WarehouseAssistantInterpretationRow(
    WarehouseAssistantIntent Intent,
    decimal Confidence,
    bool UsedLocalSemanticModel,
    DateOnly? DateFrom,
    DateOnly? DateTo,
    string? SerialNo,
    string? Barcode,
    string? VehiclePlate,
    string? TransferDocumentNo,
    string? DocumentNo,
    WarehouseAssistantTransferScope TransferScope,
    WarehouseAssistantQueryKind QueryKind = WarehouseAssistantQueryKind.None,
    string? WarehouseQuery = null,
    string? LocationQuery = null,
    string? StockGroupQuery = null,
    string? ProjectQuery = null,
    string? StatusQuery = null,
    WarehouseAssistantStockMeasure? StockMeasure = null,
    WarehouseAssistantSortDirection Sort = WarehouseAssistantSortDirection.None,
    int? Limit = null,
    bool ExcludeZero = false,
    bool ExcludeCancelled = false,
    bool ActiveOnly = false,
    string? NavigationTopic = null,
    IReadOnlyList<string>? ReasonCodes = null);

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
    IReadOnlyList<WarehouseAssistantParameterGuideRow>? ParameterGuides = null,
    IReadOnlyList<WarehouseAssistantSteelVehicleRow>? SteelVehicles = null,
    IReadOnlyList<WarehouseAssistantTransferRow>? Transfers = null,
    IReadOnlyList<WarehouseAssistantEntityCandidateRow>? EntityCandidates = null,
    IReadOnlyList<WarehouseAssistantSummaryMetricRow>? SummaryMetrics = null,
    IReadOnlyList<WarehouseAssistantExceptionRow>? Exceptions = null,
    IReadOnlyList<WarehouseAssistantTraceabilityEventRow>? TraceabilityEvents = null,
    IReadOnlyList<WarehouseAssistantEvidenceRow>? Evidence = null,
    IReadOnlyList<WarehouseAssistantInterpretationRow>? Interpretations = null);

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
    string? ParameterValue = null,
    string? VehiclePlate = null,
    string? TransferDocumentNo = null,
    WarehouseAssistantTransferScope? TransferScope = null,
    string? DocumentNo = null,
    WarehouseAssistantIntent? LastIntent = null,
    string? LastResolvedQuestion = null,
    string? PendingQuestion = null,
    string? TargetUserQuery = null,
    bool? RequestsAllUsers = null,
    WarehouseAssistantDatePreset? LastDatePreset = null,
    string? WarehouseQuery = null,
    string? LocationQuery = null,
    string? ProjectQuery = null,
    WarehouseAssistantQueryKind QueryKind = WarehouseAssistantQueryKind.None,
    WarehouseAssistantStockMeasure? StockMeasure = null);

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
    string? ParameterValue = null,
    string? VehiclePlateQuery = null,
    string? TransferDocumentQuery = null,
    WarehouseAssistantTransferScope TransferScope = WarehouseAssistantTransferScope.All,
    bool HasExplicitDateFilter = false,
    string? DocumentQuery = null,
    string? ClarificationQuestion = null,
    IReadOnlyList<WarehouseAssistantIntentResolution>? AdditionalQueries = null,
    WarehouseAssistantQueryKind QueryKind = WarehouseAssistantQueryKind.None,
    string? WarehouseQuery = null,
    string? LocationQuery = null,
    string? StockGroupQuery = null,
    string? ProjectQuery = null,
    string? StatusQuery = null,
    WarehouseAssistantStockMeasure? StockMeasure = null,
    WarehouseAssistantSortDirection Sort = WarehouseAssistantSortDirection.None,
    int? Limit = null,
    bool ExcludeZero = false,
    bool ExcludeCancelled = false,
    bool ActiveOnly = false,
    string? NavigationTopic = null,
    IReadOnlyList<string>? ReasonCodes = null);

public interface IWarehouseAssistantIntentResolver
{
    Task<WarehouseAssistantIntentResolution> ResolveAsync(
        string message,
        WarehouseAssistantContext? context,
        CancellationToken cancellationToken = default);
}

public interface IWarehouseAssistantRoutingDiagnostics
{
    WarehouseAssistantRoutingInfo GetRoutingInfo();
}

public interface IWarehouseAssistantService
{
    Task<WarehouseAssistantCapabilities> GetCapabilitiesAsync(WarehouseAssistantAccess access, CancellationToken ct = default);
    Task<IReadOnlyList<WarehouseAssistantConversationRow>> GetConversationsAsync(long actorUserId, string branchCode, CancellationToken ct = default);
    Task<IReadOnlyList<WarehouseAssistantMessageRow>> GetMessagesAsync(long conversationId, long actorUserId, string branchCode, CancellationToken ct = default);
    Task<WarehouseAssistantChatResponse> AskAsync(AskWarehouseAssistantRequest request, long actorUserId, string branchCode, WarehouseAssistantAccess access, CancellationToken ct = default);
    Task ArchiveConversationAsync(long conversationId, long actorUserId, string branchCode, CancellationToken ct = default);
}
