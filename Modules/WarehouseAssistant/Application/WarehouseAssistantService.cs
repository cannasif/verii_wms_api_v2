using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using verii_wms_api_v2.Modules.Audit.Application;
using verii_wms_api_v2.Modules.Audit.Domain;
using verii_wms_api_v2.Modules.BarcodeDesigner.Application;
using verii_wms_api_v2.Modules.GoodsReceipt.Domain;
using verii_wms_api_v2.Modules.Identity.Application;
using verii_wms_api_v2.Modules.Identity.Domain;
using verii_wms_api_v2.Modules.Location.Domain;
using verii_wms_api_v2.Modules.ProjectSettings.Domain;
using verii_wms_api_v2.Modules.StockBalance.Domain;
using verii_wms_api_v2.Modules.StockMovement.Domain;
using verii_wms_api_v2.Modules.WarehouseAssistant.Domain;
using verii_wms_api_v2.Modules.WarehouseAssistant.Localization;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;
using WarehouseEntity = verii_wms_api_v2.Modules.Warehouse.Domain.Warehouse;
using StockEntity = verii_wms_api_v2.Modules.Stock.Domain.Stock;
using static verii_wms_api_v2.Modules.WarehouseAssistant.Localization.WarehouseAssistantMessageKeys;

namespace verii_wms_api_v2.Modules.WarehouseAssistant.Application;

public sealed partial class WarehouseAssistantService : IWarehouseAssistantService
{
    private const int MaximumMessageLength = 1000;
    private const int MaximumResultCount = 50;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IUnitOfWork unitOfWork;
    private readonly IWarehouseAssistantIntentResolver intentResolver;
    private readonly IAuditLogWriter audit;
    private readonly TimeProvider timeProvider;
    private readonly IWarehouseBarcodeResolver? barcodeResolver;
    private readonly IStringLocalizer<WarehouseAssistantResource>? localizer;
    private readonly IWarehouseAssistantRoutingDiagnostics? routingDiagnostics;

    public WarehouseAssistantService(
        IUnitOfWork unitOfWork,
        IWarehouseAssistantIntentResolver intentResolver,
        IAuditLogWriter audit,
        TimeProvider timeProvider,
        IWarehouseBarcodeResolver? barcodeResolver = null,
        IStringLocalizer<WarehouseAssistantResource>? localizer = null,
        IWarehouseAssistantRoutingDiagnostics? routingDiagnostics = null)
    {
        this.unitOfWork = unitOfWork;
        this.intentResolver = intentResolver;
        this.audit = audit;
        this.timeProvider = timeProvider;
        this.barcodeResolver = barcodeResolver;
        this.localizer = localizer;
        this.routingDiagnostics = routingDiagnostics;
    }

    public Task<WarehouseAssistantCapabilities> GetCapabilitiesAsync(
        WarehouseAssistantAccess access,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var examples = new List<string> { M(CapabilityExampleMyActivities) };
        if (access.CanQueryAllUsers) examples.Add(M(CapabilityExampleUserActivities));
        if (access.CanViewStockBalances)
        {
            examples.Add(M(CapabilityExampleSerialBalance));
            examples.Add(M(CapabilityExampleStockBalance));
        }
        if (access.CanViewStockMovements && access.CanViewGoodsReceipts)
            examples.Add(M(CapabilityExampleSerialReceipt));
        if (access.CanViewStockBalances)
            examples.Add(M(CapabilityExampleBarcode));
        if (access.CanViewStockMovements)
            examples.Add(M(CapabilityExampleMovement));
        if (CanQueryAnyTasks(access))
            examples.Add(M(CapabilityExampleTasks));
        if (access.CanViewSteelVehicles)
            examples.Add(M(CapabilityExampleSteelVehicles));
        if (access.CanViewWarehouseTransfers)
            examples.Add(M(CapabilityExampleWarehouseTransfers));
        if (access.CanViewProductionTransfers)
            examples.Add(M(CapabilityExampleProductionTransfers));
        examples.Add(M(CapabilityExampleShiftBrief));
        if (CanQueryOperationalExceptions(access))
            examples.Add(M(CapabilityExampleOperationalExceptions));
        if (access.CanViewStockMovements && access.CanViewStockBalances)
            examples.Add(M(CapabilityExampleTraceability));
        if (CanQueryProcessBlockers(access))
            examples.Add(M(CapabilityExampleProcessBlockers));
        if (examples.Count >= 2)
            examples.Add(string.Join("; ", examples.Take(2)));

        var routing = routingDiagnostics?.GetRoutingInfo()
            ?? new WarehouseAssistantRoutingInfo("2.4.0", "LocalSemantic", false, null);
        return Task.FromResult(new WarehouseAssistantCapabilities(
            access.CanQueryAllUsers,
            access.CanViewStockBalances,
            access.CanViewStockMovements && access.CanViewGoodsReceipts,
            access.CanViewStockBalances,
            access.CanViewStockMovements,
            CanQueryAnyTasks(access),
            M(access.CanQueryAllUsers ? ScopeAll : ScopeSelf),
            examples,
            access.CanViewGoodsReceipts,
            true,
            access.CanViewSteelVehicles,
            access.CanViewWarehouseTransfers || access.CanViewProductionTransfers,
            true,
            CanQueryOperationalExceptions(access),
            access.CanViewStockMovements && access.CanViewStockBalances,
            CanQueryProcessBlockers(access),
            routing.Version,
            routing.RoutingMode,
            routing.SemanticRoutingAvailable,
            routing.SemanticModel));
    }

    public async Task<IReadOnlyList<WarehouseAssistantConversationRow>> GetConversationsAsync(
        long actorUserId,
        string branchCode,
        CancellationToken ct = default) =>
        await unitOfWork.Repository<WarehouseAssistantConversation>().Query()
            .Where(x => x.UserId == actorUserId && x.BranchCode == branchCode && !x.IsArchived)
            .OrderByDescending(x => x.LastMessageAtUtc)
            .Take(30)
            .Select(x => new WarehouseAssistantConversationRow(x.Id, x.Title, x.LastMessageAtUtc, x.IsArchived))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<WarehouseAssistantMessageRow>> GetMessagesAsync(
        long conversationId,
        long actorUserId,
        string branchCode,
        CancellationToken ct = default)
    {
        await EnsureConversationOwnershipAsync(conversationId, actorUserId, branchCode, ct);
        var rows = await unitOfWork.Repository<WarehouseAssistantMessage>().Query()
            .Where(x => x.ConversationId == conversationId && x.BranchCode == branchCode)
            .OrderBy(x => x.CreatedDate).ThenBy(x => x.Id)
            .Take(200)
            .Select(x => new
            {
                x.Id,
                x.Role,
                x.Content,
                x.Intent,
                x.Scope,
                x.CreatedDate,
                x.ResponseDataJson
            })
            .ToListAsync(ct);
        return rows.Select(x => new WarehouseAssistantMessageRow(
            x.Id,
            x.Role,
            x.Content,
            x.Intent,
            x.Scope,
            x.CreatedDate,
            x.Role == "assistant"
                ? RestoreChatResponse(conversationId, x.Id, x.Content, x.Intent, x.Scope, x.ResponseDataJson)
                : null)).ToArray();
    }

    public async Task<WarehouseAssistantChatResponse> AskAsync(
        AskWarehouseAssistantRequest request,
        long actorUserId,
        string branchCode,
        WarehouseAssistantAccess access,
        CancellationToken ct = default)
    {
        var message = ValidateMessage(request.Message);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var conversation = await ResolveConversationAsync(request.ConversationId, message, actorUserId, branchCode, now, ct);
        var context = await GetContextAsync(conversation.Id, branchCode, ct);
        var parameterHint = ValidateParameterHint(request.ParameterHint);
        var resolution = parameterHint is null
            ? await intentResolver.ResolveAsync(message, context, ct)
            : new WarehouseAssistantIntentResolution(
                WarehouseAssistantIntent.ParameterHelp,
                WarehouseAssistantDatePreset.Today,
                null,
                null,
                null,
                null,
                false,
                1m,
                "catalog",
                ParameterModule: parameterHint.Module,
                ParameterField: parameterHint.Field,
                ParameterValue: parameterHint.Value);
        var correlationId = Guid.NewGuid();
        var queryPlan = ExpandQueryPlan(resolution);
        var providerMode = queryPlan.Count > 1 && !resolution.ProviderMode.Contains("compound", StringComparison.OrdinalIgnoreCase)
            ? $"{resolution.ProviderMode}-compound"
            : resolution.ProviderMode;
        var interpretations = queryPlan.Select(item => new WarehouseAssistantInterpretationRow(
            item.Intent,
            item.Confidence,
            item.ProviderMode.Contains("hybrid", StringComparison.OrdinalIgnoreCase),
            item.DateFrom,
            item.DateTo,
            item.SerialNo,
            item.Barcode,
            item.VehiclePlateQuery,
            item.TransferDocumentQuery,
            item.DocumentQuery,
            item.TransferScope)).ToArray();

        var userMessage = new WarehouseAssistantMessage
        {
            ConversationId = conversation.Id,
            Role = "user",
            Content = message,
            BranchCode = branchCode,
            CreatedBy = actorUserId,
            CreatedDate = now,
            CorrelationId = correlationId
        };
        await unitOfWork.Repository<WarehouseAssistantMessage>().AddAsync(userMessage, ct);

        var result = await ExecuteQueryPlanAsync(queryPlan, message, actorUserId, branchCode, access, ct);
        result = result with
        {
            Evidence = BuildEvidence(result),
            Context = BuildConversationContext(context, result.Context, queryPlan, message, result.Intent)
        };
        var responseData = new StoredResponseData
        {
            ProviderMode = providerMode,
            Activities = result.Activities,
            SerialBalances = result.SerialBalances,
            SerialReceipts = result.SerialReceipts,
            StockLocations = result.StockLocations,
            Barcode = result.Barcode,
            Movements = result.Movements,
            Tasks = result.Tasks,
            GoodsReceipts = result.GoodsReceipts ?? [],
            ParameterGuides = result.ParameterGuides ?? [],
            SteelVehicles = result.SteelVehicles ?? [],
            Transfers = result.Transfers ?? [],
            EntityCandidates = result.EntityCandidates ?? [],
            SummaryMetrics = result.SummaryMetrics ?? [],
            Exceptions = result.Exceptions ?? [],
            TraceabilityEvents = result.TraceabilityEvents ?? [],
            Evidence = result.Evidence ?? [],
            Interpretations = interpretations,
            Suggestions = result.Suggestions
        };
        var assistantMessage = new WarehouseAssistantMessage
        {
            ConversationId = conversation.Id,
            Role = "assistant",
            Content = result.Answer,
            Intent = result.Intent.ToString(),
            Scope = result.Scope,
            ToolName = result.ToolName,
            ResponseDataJson = JsonSerializer.Serialize(responseData, JsonOptions),
            ContextJson = JsonSerializer.Serialize(result.Context, JsonOptions),
            BranchCode = branchCode,
            CreatedBy = actorUserId,
            CreatedDate = now,
            CorrelationId = correlationId
        };
        await unitOfWork.Repository<WarehouseAssistantMessage>().AddAsync(assistantMessage, ct);
        conversation.LastMessageAtUtc = now;
        conversation.UpdatedBy = actorUserId;
        conversation.UpdatedDate = now;
        unitOfWork.Repository<WarehouseAssistantConversation>().Update(conversation);
        await unitOfWork.SaveChangesAsync(ct);

        await audit.WriteAsync(new AuditLogWriteEntry(
            "warehouse-assistant.query",
            "WarehouseAssistantConversation",
            conversation.Id.ToString(),
            "Succeeded",
            "warehouse-assistant",
            NewValues: new
            {
                Intent = result.Intent.ToString(),
                QueryIntents = queryPlan.Select(x => x.Intent.ToString()).ToArray(),
                ProviderMode = providerMode,
                result.Scope,
                result.ToolName,
                QueryCount = queryPlan.Count,
                ResultCount = result.Activities.Count + result.SerialBalances.Count + result.SerialReceipts.Count
                    + result.StockLocations.Count + result.Movements.Count + result.Tasks.Count
                    + (result.GoodsReceipts?.Count ?? 0) + (result.ParameterGuides?.Count ?? 0)
                    + (result.SteelVehicles?.Count ?? 0) + (result.Transfers?.Count ?? 0)
                    + (result.Exceptions?.Count ?? 0) + (result.TraceabilityEvents?.Count ?? 0)
                    + (result.Barcode is null ? 0 : 1),
                CorrelationId = correlationId
            },
            ChangedFields: ["Intent", "QueryIntents", "ProviderMode", "Scope", "ToolName"]), ct);

        return new WarehouseAssistantChatResponse(
            conversation.Id,
            assistantMessage.Id,
            result.Answer,
            result.Intent,
            result.Scope,
            providerMode,
            result.Activities,
            result.SerialBalances,
            result.SerialReceipts,
            result.StockLocations,
            result.Barcode,
            result.Movements,
            result.Tasks,
            result.Suggestions,
            result.GoodsReceipts ?? [],
            result.ParameterGuides ?? [],
            result.SteelVehicles ?? [],
            result.Transfers ?? [],
            result.EntityCandidates ?? [],
            result.SummaryMetrics ?? [],
            result.Exceptions ?? [],
            result.TraceabilityEvents ?? [],
            result.Evidence ?? [],
            interpretations);
    }

    private async Task<ExecutionResult> ExecuteIntentAsync(
        WarehouseAssistantIntentResolution resolution,
        string originalMessage,
        long actorUserId,
        string branchCode,
        WarehouseAssistantAccess access,
        CancellationToken ct)
    {
        return resolution.Intent switch
        {
            WarehouseAssistantIntent.MyActivities or WarehouseAssistantIntent.UserActivities =>
                await ExecuteActivitiesAsync(resolution, originalMessage, actorUserId, branchCode, access, ct),
            WarehouseAssistantIntent.SerialBalance =>
                await ExecuteSerialBalanceAsync(resolution, actorUserId, branchCode, access, ct),
            WarehouseAssistantIntent.SerialReceiptHistory =>
                await ExecuteSerialReceiptAsync(resolution, actorUserId, branchCode, access, ct),
            WarehouseAssistantIntent.StockLocationBalance =>
                await ExecuteStockLocationAsync(resolution, originalMessage, actorUserId, branchCode, access, ct),
            WarehouseAssistantIntent.BarcodeLookup =>
                await ExecuteBarcodeLookupAsync(resolution, actorUserId, branchCode, access, ct),
            WarehouseAssistantIntent.StockMovementHistory =>
                await ExecuteStockMovementHistoryAsync(resolution, originalMessage, actorUserId, branchCode, access, ct),
            WarehouseAssistantIntent.AssignedTasks =>
                await ExecuteAssignedTasksAsync(resolution, originalMessage, actorUserId, branchCode, access, ct),
            WarehouseAssistantIntent.GoodsReceiptAnalysis =>
                await ExecuteGoodsReceiptAnalysisAsync(resolution, originalMessage, actorUserId, branchCode, access, ct),
            WarehouseAssistantIntent.SteelVehicleAnalysis =>
                await ExecuteSteelVehicleAnalysisAsync(resolution, branchCode, access, ct),
            WarehouseAssistantIntent.WarehouseTransferAnalysis =>
                await ExecuteWarehouseTransferAnalysisAsync(resolution, actorUserId, branchCode, access, ct),
            WarehouseAssistantIntent.ShiftBrief =>
                await ExecuteShiftBriefAsync(resolution, originalMessage, actorUserId, branchCode, access, ct),
            WarehouseAssistantIntent.OperationalExceptions =>
                await ExecuteOperationalExceptionsAsync(actorUserId, branchCode, access, ct),
            WarehouseAssistantIntent.Traceability =>
                await ExecuteTraceabilityAsync(resolution, actorUserId, branchCode, access, ct),
            WarehouseAssistantIntent.ProcessBlockers =>
                await ExecuteProcessBlockersAsync(resolution, actorUserId, branchCode, access, ct),
            WarehouseAssistantIntent.ParameterHelp => ExecuteParameterHelp(resolution),
            WarehouseAssistantIntent.Help => HelpResult(access),
            _ => UnknownResult(access, resolution.ClarificationQuestion)
        };
    }

    private async Task<ExecutionResult> ExecuteActivitiesAsync(
        WarehouseAssistantIntentResolution resolution,
        string message,
        long actorUserId,
        string branchCode,
        WarehouseAssistantAccess access,
        CancellationToken ct)
    {
        var target = await ResolveActivityTargetAsync(message, resolution, actorUserId, access.CanQueryAllUsers, ct);
        var (startUtc, endUtc, periodLabel) = await ResolveDateRangeAsync(resolution.DatePreset, ct);
        var logs = unitOfWork.Repository<AuditLog>().Query()
            .Where(x => x.BranchCode == branchCode
                && x.CreatedDate >= startUtc && x.CreatedDate < endUtc
                && x.Source != "warehouse-assistant");
        if (target.UserId.HasValue) logs = logs.Where(x => x.PerformedByUserId == target.UserId.Value);
        else if (!target.AllUsers) logs = logs.Where(x => x.PerformedByUserId == actorUserId);

        var rawRows = await logs.OrderByDescending(x => x.CreatedDate)
            .Take(MaximumResultCount)
            .Select(x => new
            {
                x.Id,
                x.ActionType,
                x.EntityType,
                x.EntityId,
                x.Result,
                x.PerformedByUserId,
                x.PerformedByUserEmail,
                OccurredAtUtc = x.CreatedDate!.Value
            })
            .ToListAsync(ct);
        var names = await ResolveUserNamesAsync(rawRows.Select(x => x.PerformedByUserId), ct);
        var rows = rawRows.Select(x => new WarehouseAssistantActivityRow(
            x.Id,
            x.ActionType,
            HumanizeAction(x.ActionType),
            x.EntityType,
            x.EntityId,
            x.Result,
            x.PerformedByUserId,
            DisplayUser(x.PerformedByUserId, x.PerformedByUserEmail, names),
            x.OccurredAtUtc)).ToArray();

        var forcedSelf = !access.CanQueryAllUsers && (resolution.RequestsAllUsers || target.RequestedAnotherUser);
        var answer = rows.Length == 0
            ? M(ActivityNone, periodLabel, target.DisplayName)
            : M(ActivityFound, periodLabel, target.DisplayName, rows.Length);
        if (forcedSelf)
            answer = M(ActivityForcedSelf) + " " + answer;

        return new ExecutionResult(
            resolution.Intent,
            target.AllUsers ? "all-users" : target.UserId == actorUserId ? "self" : "selected-user",
            "query-audit-activities",
            answer,
            rows,
            [],
            [],
            [],
            null,
            [],
            [],
            new WarehouseAssistantContext(
                null,
                null,
                null,
                TargetUserQuery: target.UserId == actorUserId ? null : target.DisplayName,
                RequestsAllUsers: target.AllUsers),
            [M(CapabilityExampleMyActivities), M(CapabilityExampleMyActivities)]);
    }

    private async Task<ExecutionResult> ExecuteSerialBalanceAsync(
        WarehouseAssistantIntentResolution resolution,
        long actorUserId,
        string branchCode,
        WarehouseAssistantAccess access,
        CancellationToken ct)
    {
        if (!access.CanViewStockBalances)
            return Denied(resolution.Intent, M(SerialBalanceDenied));
        if (string.IsNullOrWhiteSpace(resolution.SerialNo))
            return MissingEntity(resolution.Intent, M(SerialRequired));

        var serialNo = resolution.SerialNo.Trim();
        var warehouseAccess = await UserWarehouseAccessService.ResolveAsync(unitOfWork, actorUserId, branchCode, ct);
        var balances = unitOfWork.Repository<LocationStockBalance>().Query()
            .Where(x => x.BranchCode == branchCode && x.SerialNo.ToUpper() == serialNo.ToUpper() && x.Quantity != 0);
        if (warehouseAccess.IsRestricted)
            balances = balances.Where(x => warehouseAccess.WarehouseIds.Contains(x.WarehouseId));

        var rows = await (from balance in balances
                          join warehouse in unitOfWork.Repository<WarehouseEntity>().Query() on balance.WarehouseId equals warehouse.Id
                          join location in unitOfWork.Repository<WarehouseLocation>().Query() on balance.LocationId equals location.Id
                          join stock in unitOfWork.Repository<StockEntity>().Query() on balance.StockId equals stock.Id
                          orderby balance.AvailableQuantity descending, warehouse.WarehouseCode, location.Code
                          select new WarehouseAssistantSerialBalanceRow(
                              balance.Id, balance.SerialNo, stock.Id, stock.ErpStockCode, stock.StockName,
                              warehouse.WarehouseCode, warehouse.WarehouseName, location.Code, location.Name,
                              balance.LotNo == "" ? null : balance.LotNo, balance.UnitCode, balance.StockStatus,
                              balance.Quantity, balance.ReservedQuantity, balance.AvailableQuantity, balance.LastTransactionDate))
            .Take(MaximumResultCount)
            .ToListAsync(ct);

        var answer = rows.Count == 0
            ? M(SerialBalanceNone, serialNo)
            : M(SerialBalanceFound, serialNo, rows.Count, rows.Sum(x => x.Quantity), rows[0].UnitCode, rows.Sum(x => x.AvailableQuantity));
        return new ExecutionResult(
            resolution.Intent, "authorized-warehouses", "query-serial-balance", answer,
            [], rows, [], [], null, [], [], new WarehouseAssistantContext(serialNo, rows.FirstOrDefault()?.StockId, rows.FirstOrDefault()?.StockCode),
            [M(CapabilityExampleSerialReceipt)]);
    }

    private async Task<ExecutionResult> ExecuteSerialReceiptAsync(
        WarehouseAssistantIntentResolution resolution,
        long actorUserId,
        string branchCode,
        WarehouseAssistantAccess access,
        CancellationToken ct)
    {
        if (!access.CanViewStockMovements || !access.CanViewGoodsReceipts)
            return Denied(resolution.Intent, M(SerialReceiptDenied));
        if (string.IsNullOrWhiteSpace(resolution.SerialNo))
            return MissingEntity(resolution.Intent, M(SerialReceiptRequired));

        var serialNo = resolution.SerialNo.Trim();
        var warehouseAccess = await UserWarehouseAccessService.ResolveAsync(unitOfWork, actorUserId, branchCode, ct);
        var entries = unitOfWork.Repository<StockMovementEntry>().Query()
            .Where(x => x.BranchCode == branchCode && x.SerialNo != null
                && x.SerialNo.ToUpper() == serialNo.ToUpper() && x.QuantityDelta > 0);
        if (warehouseAccess.IsRestricted)
            entries = entries.Where(x => warehouseAccess.WarehouseIds.Contains(x.WarehouseId));

        var operations = unitOfWork.Repository<StockMovementOperation>().Query();
        var raw = await (from entry in entries
                         join operation in operations on entry.OperationId equals operation.Id
                         join receipt in unitOfWork.Repository<GoodsReceiptHeader>().Query() on operation.ReferenceId equals receipt.Id
                         join warehouse in unitOfWork.Repository<WarehouseEntity>().Query() on entry.WarehouseId equals warehouse.Id
                         join location in unitOfWork.Repository<WarehouseLocation>().Query() on entry.LocationId equals location.Id
                         join stock in unitOfWork.Repository<StockEntity>().Query() on entry.StockId equals stock.Id
                         where operation.ReferenceType == "GoodsReceipt"
                             && operation.Status == StockMovementStatuses.Posted
                             && !operations.Any(reversal => reversal.ReversalOfOperationId == operation.Id)
                         orderby entry.OccurredAt descending
                         select new
                         {
                             Entry = entry,
                             Receipt = receipt,
                             Warehouse = warehouse,
                             Location = location,
                             Stock = stock,
                             ActorUserId = entry.CreatedBy ?? operation.CreatedBy ?? receipt.ReceivedBy ?? receipt.CompletedBy ?? receipt.CreatedBy
                         })
            .Take(20)
            .ToListAsync(ct);
        var names = await ResolveUserNamesAsync(raw.Select(x => x.ActorUserId), ct);
        var rows = raw.Select(x => new WarehouseAssistantSerialReceiptRow(
            x.Entry.Id,
            x.Entry.SerialNo!,
            x.Stock.ErpStockCode,
            x.Stock.StockName,
            string.IsNullOrWhiteSpace(x.Receipt.DocumentNo) ? $"#{x.Receipt.Id}" : x.Receipt.DocumentNo,
            x.Receipt.Id,
            x.Warehouse.WarehouseCode,
            x.Warehouse.WarehouseName,
            x.Location.Code,
            x.Location.Name,
            x.Entry.QuantityDelta,
            x.Entry.UnitCode,
            x.Entry.OccurredAt,
            x.ActorUserId,
            DisplayUser(x.ActorUserId, null, names))).ToArray();

        var answer = rows.Length == 0
            ? M(SerialReceiptNone, serialNo)
            : M(SerialReceiptFound, serialNo, rows.Length, rows[0].GoodsReceiptNo, rows[0].ReceivedByDisplayName);
        return new ExecutionResult(
            resolution.Intent, "authorized-warehouses", "query-serial-goods-receipt-history", answer,
            [], [], rows, [], null, [], [], new WarehouseAssistantContext(serialNo, raw.FirstOrDefault()?.Stock.Id, raw.FirstOrDefault()?.Stock.ErpStockCode),
            [M(SuggestionSerialBalance, serialNo)]);
    }

    private async Task<ExecutionResult> ExecuteStockLocationAsync(
        WarehouseAssistantIntentResolution resolution,
        string message,
        long actorUserId,
        string branchCode,
        WarehouseAssistantAccess access,
        CancellationToken ct)
    {
        if (!access.CanViewStockBalances)
            return Denied(resolution.Intent, M(StockBalanceDenied));

        var stockLookup = await ResolveStockAsync(resolution.StockQuery, message, branchCode, ct);
        var stock = stockLookup.Entity;
        if (stock is null)
            return string.IsNullOrWhiteSpace(stockLookup.SearchTerm)
                ? MissingEntity(resolution.Intent, M(StockRequired))
                : EntityClarification(resolution.Intent, stockLookup.SearchTerm, stockLookup.Candidates);

        var warehouseAccess = await UserWarehouseAccessService.ResolveAsync(unitOfWork, actorUserId, branchCode, ct);
        var balances = unitOfWork.Repository<LocationStockBalance>().Query()
            .Where(x => x.BranchCode == branchCode && x.StockId == stock.Id && x.Quantity != 0);
        if (warehouseAccess.IsRestricted)
            balances = balances.Where(x => warehouseAccess.WarehouseIds.Contains(x.WarehouseId));
        var rows = await (from balance in balances
                          join warehouse in unitOfWork.Repository<WarehouseEntity>().Query() on balance.WarehouseId equals warehouse.Id
                          join location in unitOfWork.Repository<WarehouseLocation>().Query() on balance.LocationId equals location.Id
                          orderby balance.AvailableQuantity descending, warehouse.WarehouseCode, location.Code
                          select new WarehouseAssistantStockLocationRow(
                              stock.Id, stock.ErpStockCode, stock.StockName,
                              warehouse.WarehouseCode, warehouse.WarehouseName,
                              location.Code, location.Name, balance.UnitCode,
                              balance.Quantity, balance.ReservedQuantity, balance.AvailableQuantity))
            .Take(MaximumResultCount)
            .ToListAsync(ct);
        var answer = rows.Count == 0
            ? M(StockBalanceNone, stock.ErpStockCode, stock.StockName)
            : M(StockBalanceFound, stock.ErpStockCode, stock.StockName, rows.Count, rows.Sum(x => x.AvailableQuantity), rows[0].UnitCode);
        return new ExecutionResult(
            resolution.Intent, "authorized-warehouses", "query-stock-location-balance", answer,
            [], [], [], rows, null, [], [], new WarehouseAssistantContext(null, stock.Id, stock.ErpStockCode),
            [M(SuggestionSerialBalance, stock.ErpStockCode)]);
    }

    private ExecutionResult HelpResult(WarehouseAssistantAccess access)
    {
        var suggestions = new List<string> { M(CapabilityExampleMyActivities) };
        if (access.CanQueryAllUsers) suggestions.Add(M(CapabilityExampleUserActivities));
        if (access.CanViewStockBalances) suggestions.Add(M(CapabilityExampleSerialBalance));
        if (access.CanViewStockMovements && access.CanViewGoodsReceipts) suggestions.Add(M(CapabilityExampleSerialReceipt));
        return new ExecutionResult(
            WarehouseAssistantIntent.Help,
            access.CanQueryAllUsers ? "all-users-available" : "self",
            "help",
            M(HelpAnswer),
            [], [], [], [], null, [], [], new WarehouseAssistantContext(null, null, null), suggestions);
    }

    private ExecutionResult UnknownResult(WarehouseAssistantAccess access, string? clarificationQuestion = null)
    {
        var help = HelpResult(access);
        return help with
        {
            Intent = WarehouseAssistantIntent.Unknown,
            ToolName = "none",
            Answer = string.IsNullOrWhiteSpace(clarificationQuestion) ? M(UnknownAnswer) : clarificationQuestion.Trim()
        };
    }

    private ExecutionResult Denied(WarehouseAssistantIntent intent, string answer) => new(
        intent, "denied", "authorization-check", answer, [], [], [], [], null, [], [],
        new WarehouseAssistantContext(null, null, null), [M(CapabilityExampleMyActivities)]);

    private static ExecutionResult MissingEntity(WarehouseAssistantIntent intent, string answer) => new(
        intent, "authorized", "validation", answer, [], [], [], [], null, [], [],
        new WarehouseAssistantContext(null, null, null), []);

    private async Task<ActivityTarget> ResolveActivityTargetAsync(
        string message,
        WarehouseAssistantIntentResolution resolution,
        long actorUserId,
        bool canQueryAllUsers,
        CancellationToken ct)
    {
        var normalized = WarehouseAssistantIntentResolver.Normalize(message);
        var requestsSelf = ContainsAny(normalized, ["yaptigim", "benim", "kendim", "kendi islem", "islemlerim"]);
        if (!canQueryAllUsers || requestsSelf)
        {
            var selfName = await GetUserDisplayNameAsync(actorUserId, ct);
            return new ActivityTarget(actorUserId, false, selfName, !canQueryAllUsers && !requestsSelf && resolution.Intent == WarehouseAssistantIntent.UserActivities);
        }

        var targetText = string.IsNullOrWhiteSpace(resolution.TargetUserQuery)
            ? message
            : resolution.TargetUserQuery;
        var users = await (from user in unitOfWork.Repository<User>().Query()
                           join detail in unitOfWork.Repository<UserDetail>().Query() on user.Id equals detail.UserId into details
                           from detail in details.DefaultIfEmpty()
                           where user.IsActive
                           select new { user.Id, user.Username, user.Email, FirstName = detail != null ? detail.FirstName : "", LastName = detail != null ? detail.LastName : "" })
            .Take(1000)
            .ToListAsync(ct);
        var target = users
            .Select(x => new
            {
                User = x,
                FullName = $"{x.FirstName} {x.LastName}".Trim(),
                MatchLength = new[] { x.Username, x.Email, $"{x.FirstName} {x.LastName}".Trim() }
                    .Where(v => !string.IsNullOrWhiteSpace(v) && WarehouseAssistantIntentResolver.Normalize(targetText).Contains(WarehouseAssistantIntentResolver.Normalize(v), StringComparison.Ordinal))
                    .Select(v => v.Length)
                    .DefaultIfEmpty(0)
                    .Max()
            })
            .Where(x => x.MatchLength > 0)
            .OrderByDescending(x => x.MatchLength)
            .FirstOrDefault();
        if (target is not null)
            return new ActivityTarget(target.User.Id, false, string.IsNullOrWhiteSpace(target.FullName) ? target.User.Username : target.FullName, true);
        if (resolution.RequestsAllUsers)
            return new ActivityTarget(null, true, M(AllUsers), false);

        return new ActivityTarget(actorUserId, false, await GetUserDisplayNameAsync(actorUserId, ct), false);
    }

    private async Task<WarehouseAssistantConversation> ResolveConversationAsync(
        long? conversationId,
        string message,
        long actorUserId,
        string branchCode,
        DateTime now,
        CancellationToken ct)
    {
        if (conversationId.HasValue)
        {
            var existing = await unitOfWork.Repository<WarehouseAssistantConversation>().Query(true)
                .FirstOrDefaultAsync(x => x.Id == conversationId.Value && x.UserId == actorUserId && x.BranchCode == branchCode && !x.IsArchived, ct);
            return existing ?? throw AppException.NotFound(M(ConversationNotFound));
        }

        var conversation = new WarehouseAssistantConversation
        {
            UserId = actorUserId,
            Title = message.Length <= 80 ? message : message[..77] + "...",
            LastMessageAtUtc = now,
            BranchCode = branchCode,
            CreatedBy = actorUserId,
            CreatedDate = now
        };
        await unitOfWork.Repository<WarehouseAssistantConversation>().AddAsync(conversation, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return conversation;
    }

    public async Task ArchiveConversationAsync(
        long conversationId,
        long actorUserId,
        string branchCode,
        CancellationToken ct = default)
    {
        var conversation = await unitOfWork.Repository<WarehouseAssistantConversation>().Query(true)
            .FirstOrDefaultAsync(x => x.Id == conversationId
                && x.UserId == actorUserId
                && x.BranchCode == branchCode
                && !x.IsArchived, ct)
            ?? throw AppException.NotFound(M(ConversationNotFound));
        conversation.IsArchived = true;
        conversation.UpdatedBy = actorUserId;
        conversation.UpdatedDate = timeProvider.GetUtcNow().UtcDateTime;
        unitOfWork.Repository<WarehouseAssistantConversation>().Update(conversation);
        await unitOfWork.SaveChangesAsync(ct);
        await audit.WriteAsync(new AuditLogWriteEntry(
            "warehouse-assistant.conversation.archive",
            "WarehouseAssistantConversation",
            conversation.Id.ToString(),
            "Succeeded",
            "warehouse-assistant",
            NewValues: new { conversation.IsArchived },
            ChangedFields: [nameof(conversation.IsArchived)]), ct);
    }

    private static WarehouseAssistantChatResponse? RestoreChatResponse(
        long conversationId,
        long messageId,
        string answer,
        string? intentValue,
        string? scope,
        string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var stored = JsonSerializer.Deserialize<StoredResponseData>(json, JsonOptions);
            if (stored is null) return null;
            var intent = Enum.TryParse<WarehouseAssistantIntent>(intentValue, true, out var parsed)
                ? parsed
                : WarehouseAssistantIntent.Unknown;
            return new WarehouseAssistantChatResponse(
                conversationId,
                messageId,
                answer,
                intent,
                scope ?? "authorized",
                stored.ProviderMode,
                stored.Activities ?? [],
                stored.SerialBalances ?? [],
                stored.SerialReceipts ?? [],
                stored.StockLocations ?? [],
                stored.Barcode,
                stored.Movements ?? [],
                stored.Tasks ?? [],
                stored.Suggestions ?? [],
                stored.GoodsReceipts ?? [],
                stored.ParameterGuides ?? [],
                stored.SteelVehicles ?? [],
                stored.Transfers ?? [],
                stored.EntityCandidates ?? [],
                stored.SummaryMetrics ?? [],
                stored.Exceptions ?? [],
                stored.TraceabilityEvents ?? [],
                stored.Evidence ?? [],
                stored.Interpretations ?? []);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task EnsureConversationOwnershipAsync(long conversationId, long actorUserId, string branchCode, CancellationToken ct)
    {
        var exists = await unitOfWork.Repository<WarehouseAssistantConversation>().Query()
            .AnyAsync(x => x.Id == conversationId && x.UserId == actorUserId && x.BranchCode == branchCode && !x.IsArchived, ct);
        if (!exists) throw AppException.NotFound(M(ConversationNotFound));
    }

    private async Task<WarehouseAssistantContext?> GetContextAsync(long conversationId, string branchCode, CancellationToken ct)
    {
        var json = await unitOfWork.Repository<WarehouseAssistantMessage>().Query()
            .Where(x => x.ConversationId == conversationId && x.BranchCode == branchCode && x.Role == "assistant" && x.ContextJson != null)
            .OrderByDescending(x => x.CreatedDate).ThenByDescending(x => x.Id)
            .Select(x => x.ContextJson)
            .FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<WarehouseAssistantContext>(json, JsonOptions); }
        catch (JsonException) { return null; }
    }

    private async Task<(DateTime StartUtc, DateTime EndUtc, string Label)> ResolveDateRangeAsync(
        WarehouseAssistantDatePreset preset,
        CancellationToken ct,
        DateOnly? explicitFrom = null,
        DateOnly? explicitTo = null)
    {
        var utcNow = timeProvider.GetUtcNow();
        var configuredZone = await unitOfWork.Repository<ProjectSetting>().Query()
            .Where(x => x.SettingKey == "GLOBAL")
            .Select(x => x.TimeZoneId)
            .FirstOrDefaultAsync(ct);
        var zone = ResolveTimeZone(configuredZone);
        var localNow = TimeZoneInfo.ConvertTime(utcNow, zone);
        var today = localNow.Date;
        DateTime startLocal;
        DateTime endLocal;
        string label;
        if (explicitFrom.HasValue)
        {
            var from = explicitFrom.Value;
            var to = explicitTo ?? from;
            if (to < from) (from, to) = (to, from);
            if (to.DayNumber - from.DayNumber > 366)
                throw AppException.BadRequest(M(DateRangeTooLarge));
            startLocal = from.ToDateTime(TimeOnly.MinValue);
            endLocal = to.AddDays(1).ToDateTime(TimeOnly.MinValue);
            label = M(DateExplicitRange, from, to);
        }
        else switch (preset)
        {
            case WarehouseAssistantDatePreset.Yesterday:
                startLocal = today.AddDays(-1); endLocal = today; label = M(DateYesterday); break;
            case WarehouseAssistantDatePreset.LastSevenDays:
                startLocal = today.AddDays(-6); endLocal = today.AddDays(1); label = M(DateLastSevenDays); break;
            case WarehouseAssistantDatePreset.ThisWeek:
                var offset = ((int)today.DayOfWeek + 6) % 7;
                startLocal = today.AddDays(-offset); endLocal = today.AddDays(1); label = M(DateThisWeek); break;
            case WarehouseAssistantDatePreset.LastWeek:
                var currentWeekOffset = ((int)today.DayOfWeek + 6) % 7;
                endLocal = today.AddDays(-currentWeekOffset);
                startLocal = endLocal.AddDays(-7);
                label = M(DateLastWeek);
                break;
            case WarehouseAssistantDatePreset.LastThirtyDays:
                startLocal = today.AddDays(-29); endLocal = today.AddDays(1); label = M(DateLastThirtyDays); break;
            default:
                startLocal = today; endLocal = today.AddDays(1); label = M(DateToday); break;
        }
        return (
            TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(startLocal, DateTimeKind.Unspecified), zone),
            TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(endLocal, DateTimeKind.Unspecified), zone),
            label);
    }

    private static TimeZoneInfo ResolveTimeZone(string? configuredZone)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(configuredZone)) candidates.Add(configuredZone.Trim());
        if (string.Equals(configuredZone, "Europe/Istanbul", StringComparison.OrdinalIgnoreCase))
            candidates.Add("Turkey Standard Time");
        candidates.Add("Europe/Istanbul");
        candidates.Add("Turkey Standard Time");
        foreach (var id in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.Utc;
    }

    private async Task<Dictionary<long, string>> ResolveUserNamesAsync(IEnumerable<long?> userIds, CancellationToken ct)
    {
        var ids = userIds.Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToArray();
        if (ids.Length == 0) return [];
        return await (from user in unitOfWork.Repository<User>().Query()
                      join detail in unitOfWork.Repository<UserDetail>().Query() on user.Id equals detail.UserId into details
                      from detail in details.DefaultIfEmpty()
                      where ids.Contains(user.Id)
                      select new
                      {
                          user.Id,
                          Name = detail != null && (detail.FirstName != "" || detail.LastName != "")
                              ? (detail.FirstName + " " + detail.LastName).Trim()
                              : user.Username
                      }).ToDictionaryAsync(x => x.Id, x => x.Name, ct);
    }

    private async Task<string> GetUserDisplayNameAsync(long userId, CancellationToken ct)
    {
        var names = await ResolveUserNamesAsync([userId], ct);
        return names.GetValueOrDefault(userId, M(UserNumber, userId));
    }

    private string DisplayUser(long? userId, string? email, IReadOnlyDictionary<long, string> names) =>
        userId.HasValue && names.TryGetValue(userId.Value, out var name)
            ? name
            : !string.IsNullOrWhiteSpace(email) ? email : M(SystemUser);

    private string HumanizeAction(string action) => action.ToLowerInvariant() switch
    {
        var x when x.StartsWith("goods-receipt") => M(ActionGoodsReceipt),
        var x when x.StartsWith("warehouse-transfer") => M(ActionWarehouseTransfer),
        var x when x.StartsWith("production-transfer") => M(ActionProductionTransfer),
        var x when x.StartsWith("shipment") => M(ActionShipment),
        var x when x.StartsWith("warehouse-inbound") => M(ActionWarehouseInbound),
        var x when x.StartsWith("warehouse-outbound") => M(ActionWarehouseOutbound),
        var x when x.StartsWith("quality") => M(ActionQuality),
        var x when x.StartsWith("packing") => M(ActionPacking),
        var x when x.StartsWith("stock") => M(ActionStock),
        var x when x.StartsWith("user") => M(ActionUser),
        _ => action.Replace('.', ' ').Replace('-', ' ')
    };

    private string ValidateMessage(string? value)
    {
        var message = value?.Trim();
        if (string.IsNullOrWhiteSpace(message)) throw AppException.BadRequest(M(MessageRequired));
        if (message.Length > MaximumMessageLength) throw AppException.BadRequest(M(MessageTooLong, MaximumMessageLength));
        return message;
    }

    private static bool ContainsAny(string value, IEnumerable<string> candidates) =>
        candidates.Any(candidate => value.Contains(candidate, StringComparison.Ordinal));

    private WarehouseAssistantParameterHint? ValidateParameterHint(WarehouseAssistantParameterHint? hint)
    {
        if (hint is null) return null;
        var modules = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "inbound", "goodsReceipt", "outbound", "shipping", "transfer", "quality", "packing",
            "project", "subcontracting", "production", "kkd", "procurement", "barcode"
        };
        var module = hint.Module?.Trim() ?? string.Empty;
        var field = hint.Field?.Trim() ?? string.Empty;
        var value = string.IsNullOrWhiteSpace(hint.Value) ? null : hint.Value.Trim();
        if (!modules.Contains(module)
            || !Regex.IsMatch(field, @"^[A-Za-z][A-Za-z0-9]{0,79}$", RegexOptions.CultureInvariant)
            || (value is not null && !Regex.IsMatch(value, @"^[A-Za-z0-9_.-]{1,80}$", RegexOptions.CultureInvariant)))
            throw AppException.BadRequest(M(ParameterHintInvalid));
        return new WarehouseAssistantParameterHint(module, field, value);
    }

    private ExecutionResult ExecuteParameterHelp(WarehouseAssistantIntentResolution resolution)
    {
        if (string.IsNullOrWhiteSpace(resolution.ParameterModule) || string.IsNullOrWhiteSpace(resolution.ParameterField))
            return MissingEntity(resolution.Intent, M(ParameterHelpRequired));
        var row = new WarehouseAssistantParameterGuideRow(
            resolution.ParameterModule,
            resolution.ParameterField,
            resolution.ParameterValue);
        return new ExecutionResult(
            resolution.Intent,
            "authorized",
            "explain-parameter-catalog",
            M(ParameterHelpAnswer),
            [], [], [], [], null, [], [],
            new WarehouseAssistantContext(
                null, null, null,
                ParameterModule: row.Module,
                ParameterField: row.Field,
                ParameterValue: row.Value),
            [],
            ParameterGuides: [row]);
    }

    private string M(string key, params object[] arguments) => localizer is null
        ? key
        : localizer[key, arguments].Value;

    private static bool CanQueryAnyTasks(WarehouseAssistantAccess access) =>
        access.CanViewGoodsReceipts
        || access.CanViewWarehouseTransfers
        || access.CanViewShipping
        || access.CanViewWarehouseInbound
        || access.CanViewWarehouseOutbound
        || access.CanViewProductionTransfers;

    private static bool CanQueryOperationalExceptions(WarehouseAssistantAccess access) =>
        access.CanViewStockBalances
        || access.CanViewGoodsReceipts
        || access.CanViewWarehouseTransfers
        || access.CanViewProductionTransfers
        || access.CanViewShipping
        || access.CanViewQuality
        || access.CanViewPacking;

    private static bool CanQueryProcessBlockers(WarehouseAssistantAccess access) =>
        access.CanViewGoodsReceipts
        || access.CanViewWarehouseTransfers
        || access.CanViewProductionTransfers
        || access.CanViewShipping
        || access.CanViewQuality
        || access.CanViewPacking;

    private sealed record ActivityTarget(long? UserId, bool AllUsers, string DisplayName, bool RequestedAnotherUser);

    private sealed record ExecutionResult(
        WarehouseAssistantIntent Intent,
        string Scope,
        string ToolName,
        string Answer,
        IReadOnlyList<WarehouseAssistantActivityRow> Activities,
        IReadOnlyList<WarehouseAssistantSerialBalanceRow> SerialBalances,
        IReadOnlyList<WarehouseAssistantSerialReceiptRow> SerialReceipts,
        IReadOnlyList<WarehouseAssistantStockLocationRow> StockLocations,
        WarehouseAssistantBarcodeRow? Barcode,
        IReadOnlyList<WarehouseAssistantMovementRow> Movements,
        IReadOnlyList<WarehouseAssistantTaskRow> Tasks,
        WarehouseAssistantContext Context,
        IReadOnlyList<string> Suggestions,
        IReadOnlyList<WarehouseAssistantGoodsReceiptRow>? GoodsReceipts = null,
        IReadOnlyList<WarehouseAssistantParameterGuideRow>? ParameterGuides = null,
        IReadOnlyList<WarehouseAssistantSteelVehicleRow>? SteelVehicles = null,
        IReadOnlyList<WarehouseAssistantTransferRow>? Transfers = null,
        IReadOnlyList<WarehouseAssistantEntityCandidateRow>? EntityCandidates = null,
        IReadOnlyList<WarehouseAssistantSummaryMetricRow>? SummaryMetrics = null,
        IReadOnlyList<WarehouseAssistantExceptionRow>? Exceptions = null,
        IReadOnlyList<WarehouseAssistantTraceabilityEventRow>? TraceabilityEvents = null,
        IReadOnlyList<WarehouseAssistantEvidenceRow>? Evidence = null);

    private sealed class StoredResponseData
    {
        public string ProviderMode { get; init; } = "deterministic";
        public IReadOnlyList<WarehouseAssistantActivityRow> Activities { get; init; } = [];
        public IReadOnlyList<WarehouseAssistantSerialBalanceRow> SerialBalances { get; init; } = [];
        public IReadOnlyList<WarehouseAssistantSerialReceiptRow> SerialReceipts { get; init; } = [];
        public IReadOnlyList<WarehouseAssistantStockLocationRow> StockLocations { get; init; } = [];
        public WarehouseAssistantBarcodeRow? Barcode { get; init; }
        public IReadOnlyList<WarehouseAssistantMovementRow> Movements { get; init; } = [];
        public IReadOnlyList<WarehouseAssistantTaskRow> Tasks { get; init; } = [];
        public IReadOnlyList<WarehouseAssistantGoodsReceiptRow> GoodsReceipts { get; init; } = [];
        public IReadOnlyList<WarehouseAssistantParameterGuideRow> ParameterGuides { get; init; } = [];
        public IReadOnlyList<WarehouseAssistantSteelVehicleRow> SteelVehicles { get; init; } = [];
        public IReadOnlyList<WarehouseAssistantTransferRow> Transfers { get; init; } = [];
        public IReadOnlyList<WarehouseAssistantEntityCandidateRow> EntityCandidates { get; init; } = [];
        public IReadOnlyList<WarehouseAssistantSummaryMetricRow> SummaryMetrics { get; init; } = [];
        public IReadOnlyList<WarehouseAssistantExceptionRow> Exceptions { get; init; } = [];
        public IReadOnlyList<WarehouseAssistantTraceabilityEventRow> TraceabilityEvents { get; init; } = [];
        public IReadOnlyList<WarehouseAssistantEvidenceRow> Evidence { get; init; } = [];
        public IReadOnlyList<WarehouseAssistantInterpretationRow> Interpretations { get; init; } = [];
        public IReadOnlyList<string> Suggestions { get; init; } = [];
    }
}
