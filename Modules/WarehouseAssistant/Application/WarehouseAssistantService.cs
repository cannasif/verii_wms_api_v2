using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Audit.Application;
using verii_wms_api_v2.Modules.Audit.Domain;
using verii_wms_api_v2.Modules.GoodsReceipt.Domain;
using verii_wms_api_v2.Modules.Identity.Application;
using verii_wms_api_v2.Modules.Identity.Domain;
using verii_wms_api_v2.Modules.Location.Domain;
using verii_wms_api_v2.Modules.StockBalance.Domain;
using verii_wms_api_v2.Modules.StockMovement.Domain;
using verii_wms_api_v2.Modules.WarehouseAssistant.Domain;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;
using WarehouseEntity = verii_wms_api_v2.Modules.Warehouse.Domain.Warehouse;
using StockEntity = verii_wms_api_v2.Modules.Stock.Domain.Stock;

namespace verii_wms_api_v2.Modules.WarehouseAssistant.Application;

public sealed class WarehouseAssistantService(
    IUnitOfWork unitOfWork,
    IWarehouseAssistantIntentResolver intentResolver,
    IAuditLogWriter audit,
    TimeProvider timeProvider) : IWarehouseAssistantService
{
    private const int MaximumMessageLength = 1000;
    private const int MaximumResultCount = 50;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task<WarehouseAssistantCapabilities> GetCapabilitiesAsync(
        WarehouseAssistantAccess access,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var examples = new List<string> { "Bugün yaptığım işlemleri göster" };
        if (access.CanQueryAllUsers) examples.Add("Ahmet Demir kullanıcısının bugün yaptığı işlemleri göster");
        if (access.CanViewStockBalances)
        {
            examples.Add("DTG-1 seri bakiyesi hangi depo ve raflarda?");
            examples.Add("01/013 stok kodlu ürün hangi raflarda var?");
        }
        if (access.CanViewStockMovements && access.CanViewGoodsReceipts)
            examples.Add("DTG-1 serisi ne zaman ve kim tarafından mal kabul edildi?");

        return Task.FromResult(new WarehouseAssistantCapabilities(
            access.CanQueryAllUsers,
            access.CanViewStockBalances,
            access.CanViewStockMovements && access.CanViewGoodsReceipts,
            access.CanQueryAllUsers ? "Tüm kullanıcılar ve yetkili olduğunuz WMS verileri" : "Yalnız kendi işlemleriniz ve yetkili olduğunuz WMS verileri",
            examples));
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
        return await unitOfWork.Repository<WarehouseAssistantMessage>().Query()
            .Where(x => x.ConversationId == conversationId && x.BranchCode == branchCode)
            .OrderBy(x => x.CreatedDate).ThenBy(x => x.Id)
            .Take(200)
            .Select(x => new WarehouseAssistantMessageRow(x.Id, x.Role, x.Content, x.Intent, x.Scope, x.CreatedDate))
            .ToListAsync(ct);
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
        var resolution = await intentResolver.ResolveAsync(message, context, ct);
        var correlationId = Guid.NewGuid();

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

        var result = await ExecuteIntentAsync(resolution, message, actorUserId, branchCode, access, ct);
        var responseData = new
        {
            result.Activities,
            result.SerialBalances,
            result.SerialReceipts,
            result.StockLocations
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
                result.Scope,
                result.ToolName,
                ResultCount = result.Activities.Count + result.SerialBalances.Count + result.SerialReceipts.Count + result.StockLocations.Count,
                CorrelationId = correlationId
            },
            ChangedFields: ["Intent", "Scope", "ToolName"]), ct);

        return new WarehouseAssistantChatResponse(
            conversation.Id,
            assistantMessage.Id,
            result.Answer,
            result.Intent,
            result.Scope,
            resolution.ProviderMode,
            result.Activities,
            result.SerialBalances,
            result.SerialReceipts,
            result.StockLocations,
            result.Suggestions);
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
            WarehouseAssistantIntent.Help => HelpResult(access),
            _ => UnknownResult(access)
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
        var (startUtc, endUtc, periodLabel) = ResolveDateRange(resolution.DatePreset);
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
            ? $"{periodLabel} için {target.DisplayName} adına kayıtlı bir işlem bulunamadı."
            : $"{periodLabel} için {target.DisplayName} adına {rows.Length} işlem buldum. En yeni işlemler üstte gösteriliyor.";
        if (forcedSelf)
            answer = "Yetkiniz yalnız kendi işlem kayıtlarınızı görmenize izin verdiği için sonuçlar size göre sınırlandı. " + answer;

        return new ExecutionResult(
            resolution.Intent,
            target.AllUsers ? "all-users" : target.UserId == actorUserId ? "self" : "selected-user",
            "query-audit-activities",
            answer,
            rows,
            [],
            [],
            [],
            new WarehouseAssistantContext(null, null, null),
            ["Bugün yaptığım işlemler", "Son 7 gündeki işlemlerim"]);
    }

    private async Task<ExecutionResult> ExecuteSerialBalanceAsync(
        WarehouseAssistantIntentResolution resolution,
        long actorUserId,
        string branchCode,
        WarehouseAssistantAccess access,
        CancellationToken ct)
    {
        if (!access.CanViewStockBalances)
            return Denied(resolution.Intent, "Seri bakiyelerini görmek için stok bakiyesi görüntüleme yetkisi gereklidir.");
        if (string.IsNullOrWhiteSpace(resolution.SerialNo))
            return MissingEntity(resolution.Intent, "Seri numarasını da yazar mısınız? Örnek: “DTG-1 seri bakiyesi nerede?”");

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
            ? $"“{serialNo}” serisi için yetkili olduğunuz depolarda aktif bakiye bulunamadı."
            : $"“{serialNo}” serisi {rows.Count} depo/raf bakiyesinde bulundu. Toplam {rows.Sum(x => x.Quantity):0.###} {rows[0].UnitCode}, kullanılabilir {rows.Sum(x => x.AvailableQuantity):0.###} {rows[0].UnitCode}.";
        return new ExecutionResult(
            resolution.Intent, "authorized-warehouses", "query-serial-balance", answer,
            [], rows, [], [], new WarehouseAssistantContext(serialNo, rows.FirstOrDefault()?.StockId, rows.FirstOrDefault()?.StockCode),
            [$"{serialNo} serisi ne zaman ve kim tarafından mal kabul edildi?"]);
    }

    private async Task<ExecutionResult> ExecuteSerialReceiptAsync(
        WarehouseAssistantIntentResolution resolution,
        long actorUserId,
        string branchCode,
        WarehouseAssistantAccess access,
        CancellationToken ct)
    {
        if (!access.CanViewStockMovements || !access.CanViewGoodsReceipts)
            return Denied(resolution.Intent, "Serinin giriş geçmişini görmek için stok hareketi ve mal kabul görüntüleme yetkileri gereklidir.");
        if (string.IsNullOrWhiteSpace(resolution.SerialNo))
            return MissingEntity(resolution.Intent, "Mal kabul geçmişini arayacağınız seri numarasını yazar mısınız?");

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
            ? $"“{serialNo}” serisi için aktif bir mal kabul giriş hareketi bulunamadı. Kayıt ters çevrilmiş veya farklı bir operasyonla açılmış olabilir."
            : $"“{serialNo}” serisinin {rows.Length} mal kabul giriş kaydını buldum. İlk görünen kayıt {rows[0].GoodsReceiptNo} belgesiyle {rows[0].ReceivedByDisplayName} tarafından işlendi.";
        return new ExecutionResult(
            resolution.Intent, "authorized-warehouses", "query-serial-goods-receipt-history", answer,
            [], [], rows, [], new WarehouseAssistantContext(serialNo, raw.FirstOrDefault()?.Stock.Id, raw.FirstOrDefault()?.Stock.ErpStockCode),
            [$"{serialNo} seri bakiyesi hangi raflarda?"]);
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
            return Denied(resolution.Intent, "Stok ve raf bakiyelerini görmek için stok bakiyesi görüntüleme yetkisi gereklidir.");

        var stock = await ResolveStockAsync(message, branchCode, ct);
        if (stock is null)
            return MissingEntity(resolution.Intent, "Stok kodunu veya ürün adını daha açık yazar mısınız? Örnek: “01/013 stok kodlu ürün hangi raflarda var?”");

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
            ? $"{stock.ErpStockCode} - {stock.StockName} için yetkili olduğunuz depolarda aktif raf bakiyesi bulunamadı."
            : $"{stock.ErpStockCode} - {stock.StockName} {rows.Count} raf bakiyesinde bulundu. Kullanılabilir toplam miktar {rows.Sum(x => x.AvailableQuantity):0.###} {rows[0].UnitCode}.";
        return new ExecutionResult(
            resolution.Intent, "authorized-warehouses", "query-stock-location-balance", answer,
            [], [], [], rows, new WarehouseAssistantContext(null, stock.Id, stock.ErpStockCode),
            [$"{stock.ErpStockCode} ürününün seri bakiyelerini göster"]);
    }

    private static ExecutionResult HelpResult(WarehouseAssistantAccess access)
    {
        var suggestions = new List<string> { "Bugün yaptığım işlemler" };
        if (access.CanQueryAllUsers) suggestions.Add("Ahmet Demir bugün hangi işlemleri yaptı?");
        if (access.CanViewStockBalances) suggestions.Add("DTG-1 seri bakiyesi hangi raflarda?");
        if (access.CanViewStockMovements && access.CanViewGoodsReceipts) suggestions.Add("DTG-1 serisini kim ve ne zaman içeri aldı?");
        return new ExecutionResult(
            WarehouseAssistantIntent.Help,
            access.CanQueryAllUsers ? "all-users-available" : "self",
            "help",
            "İşlem geçmişinizi, yetkiniz varsa kullanıcı hareketlerini, stok/ürün/malzeme raf bakiyelerini ve seri mal kabul geçmişini sorabilirsiniz. Veriler yalnız yetkili olduğunuz şube ve depolarla sınırlandırılır.",
            [], [], [], [], new WarehouseAssistantContext(null, null, null), suggestions);
    }

    private static ExecutionResult UnknownResult(WarehouseAssistantAccess access)
    {
        var help = HelpResult(access);
        return help with
        {
            Intent = WarehouseAssistantIntent.Unknown,
            ToolName = "none",
            Answer = "Soruyu güvenli bir WMS sorgusuna dönüştüremedim. Stok/ürün/malzeme kodu, seri numarası, kullanıcı ve zaman bilgisini açıkça yazarak tekrar deneyin."
        };
    }

    private static ExecutionResult Denied(WarehouseAssistantIntent intent, string answer) => new(
        intent, "denied", "authorization-check", answer, [], [], [], [],
        new WarehouseAssistantContext(null, null, null), ["Bugün yaptığım işlemler"]);

    private static ExecutionResult MissingEntity(WarehouseAssistantIntent intent, string answer) => new(
        intent, "authorized", "validation", answer, [], [], [], [],
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
                    .Where(v => !string.IsNullOrWhiteSpace(v) && WarehouseAssistantIntentResolver.Normalize(message).Contains(WarehouseAssistantIntentResolver.Normalize(v), StringComparison.Ordinal))
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
            return new ActivityTarget(null, true, "tüm kullanıcılar", false);

        return new ActivityTarget(actorUserId, false, await GetUserDisplayNameAsync(actorUserId, ct), false);
    }

    private async Task<StockEntity?> ResolveStockAsync(string message, string branchCode, CancellationToken ct)
    {
        var candidates = ExtractStockCandidates(message);
        foreach (var candidate in candidates)
        {
            var upper = candidate.ToUpper();
            var exact = await unitOfWork.Repository<StockEntity>().Query()
                .FirstOrDefaultAsync(x => x.BranchCode == branchCode && x.ErpStockCode.ToUpper() == upper, ct);
            if (exact is not null) return exact;
        }

        var search = candidates.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(search)) return null;
        var normalizedSearch = search.ToUpper();
        return await unitOfWork.Repository<StockEntity>().Query()
            .Where(x => x.BranchCode == branchCode
                && (x.ErpStockCode.ToUpper().Contains(normalizedSearch) || x.StockName.ToUpper().Contains(normalizedSearch)))
            .OrderBy(x => x.ErpStockCode)
            .FirstOrDefaultAsync(ct);
    }

    private static IReadOnlyList<string> ExtractStockCandidates(string message)
    {
        var result = new List<string>();
        var quoted = Regex.Matches(message, "[\"']([^\"']{2,100})[\"']").Select(x => x.Groups[1].Value.Trim());
        result.AddRange(quoted);
        var codeLike = Regex.Matches(message, @"\b[A-Za-z0-9]+(?:[-/._][A-Za-z0-9]+)+\b").Select(x => x.Value.Trim());
        result.AddRange(codeLike);
        var explicitMatch = Regex.Match(message,
            @"(?:stok|ürün|urun|malzeme|mamul|parça|parca)\s*(?:kodu|kod|adı|adi|no)?\s*(?:[:=#]\s*)?([A-Za-z0-9][A-Za-z0-9._/\-]{1,80})",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (explicitMatch.Success)
        {
            var value = explicitMatch.Groups[1].Value.Trim();
            if (!ContainsAny(WarehouseAssistantIntentResolver.Normalize(value), ["bakiye", "nerede", "hangi", "miktar", "kac"]))
                result.Add(value);
        }
        return result.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
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
            return existing ?? throw AppException.NotFound("Depo asistanı konuşması bulunamadı.");
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

    private async Task EnsureConversationOwnershipAsync(long conversationId, long actorUserId, string branchCode, CancellationToken ct)
    {
        var exists = await unitOfWork.Repository<WarehouseAssistantConversation>().Query()
            .AnyAsync(x => x.Id == conversationId && x.UserId == actorUserId && x.BranchCode == branchCode && !x.IsArchived, ct);
        if (!exists) throw AppException.NotFound("Depo asistanı konuşması bulunamadı.");
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

    private (DateTime StartUtc, DateTime EndUtc, string Label) ResolveDateRange(WarehouseAssistantDatePreset preset)
    {
        var utcNow = timeProvider.GetUtcNow();
        var zone = ResolveIstanbulTimeZone();
        var localNow = TimeZoneInfo.ConvertTime(utcNow, zone);
        var today = localNow.Date;
        DateTime startLocal;
        DateTime endLocal;
        string label;
        switch (preset)
        {
            case WarehouseAssistantDatePreset.Yesterday:
                startLocal = today.AddDays(-1); endLocal = today; label = "Dün"; break;
            case WarehouseAssistantDatePreset.LastSevenDays:
                startLocal = today.AddDays(-6); endLocal = today.AddDays(1); label = "Son 7 gün"; break;
            case WarehouseAssistantDatePreset.ThisWeek:
                var offset = ((int)today.DayOfWeek + 6) % 7;
                startLocal = today.AddDays(-offset); endLocal = today.AddDays(1); label = "Bu hafta"; break;
            case WarehouseAssistantDatePreset.LastThirtyDays:
                startLocal = today.AddDays(-29); endLocal = today.AddDays(1); label = "Son 30 gün"; break;
            default:
                startLocal = today; endLocal = today.AddDays(1); label = "Bugün"; break;
        }
        return (
            TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(startLocal, DateTimeKind.Unspecified), zone),
            TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(endLocal, DateTimeKind.Unspecified), zone),
            label);
    }

    private static TimeZoneInfo ResolveIstanbulTimeZone()
    {
        foreach (var id in new[] { "Europe/Istanbul", "Turkey Standard Time" })
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
        return names.GetValueOrDefault(userId, $"Kullanıcı #{userId}");
    }

    private static string DisplayUser(long? userId, string? email, IReadOnlyDictionary<long, string> names) =>
        userId.HasValue && names.TryGetValue(userId.Value, out var name)
            ? name
            : !string.IsNullOrWhiteSpace(email) ? email : "Sistem";

    private static string HumanizeAction(string action) => action.ToLowerInvariant() switch
    {
        var x when x.StartsWith("goods-receipt") => "Mal kabul işlemi",
        var x when x.StartsWith("warehouse-transfer") => "Depolar arası transfer işlemi",
        var x when x.StartsWith("production-transfer") => "Üretime transfer işlemi",
        var x when x.StartsWith("shipment") => "Sevk işlemi",
        var x when x.StartsWith("warehouse-inbound") => "Ambar giriş işlemi",
        var x when x.StartsWith("warehouse-outbound") => "Ambar çıkış işlemi",
        var x when x.StartsWith("quality") => "Kalite işlemi",
        var x when x.StartsWith("packing") => "Paketleme işlemi",
        var x when x.StartsWith("stock") => "Stok işlemi",
        var x when x.StartsWith("user") => "Kullanıcı işlemi",
        _ => action.Replace('.', ' ').Replace('-', ' ')
    };

    private static string ValidateMessage(string? value)
    {
        var message = value?.Trim();
        if (string.IsNullOrWhiteSpace(message)) throw AppException.BadRequest("Depo asistanına sorulacak mesaj zorunludur.");
        if (message.Length > MaximumMessageLength) throw AppException.BadRequest($"Mesaj en fazla {MaximumMessageLength} karakter olabilir.");
        return message;
    }

    private static bool ContainsAny(string value, IEnumerable<string> candidates) =>
        candidates.Any(candidate => value.Contains(candidate, StringComparison.Ordinal));

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
        WarehouseAssistantContext Context,
        IReadOnlyList<string> Suggestions);
}
