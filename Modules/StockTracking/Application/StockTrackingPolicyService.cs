using System.Data;
using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Audit.Application;
using verii_wms_api_v2.Modules.SerialNumberPolicy.Domain;
using verii_wms_api_v2.Modules.StockTracking.Domain;
using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;
using StockEntity = verii_wms_api_v2.Modules.Stock.Domain.Stock;

namespace verii_wms_api_v2.Modules.StockTracking.Application;

public sealed class StockTrackingPolicyService(IUnitOfWork uow, IAuditLogWriter audit)
    : IStockTrackingPolicyService, IStockTrackingPolicyResolver
{
    private IGenericRepository<StockTrackingPolicy> Policies => uow.Repository<StockTrackingPolicy>();
    private IGenericRepository<SerialNumberRule> SerialRules => uow.Repository<SerialNumberRule>();

    public async Task<PagedResponse<StockTrackingPolicyRow>> GetPagedAsync(PagedRequest request, CancellationToken ct = default)
    {
        var filteredPolicies = Policies.Query().ApplyAdvancedFilters(request);
        var query =
            from policy in filteredPolicies
            join stock in uow.Repository<StockEntity>().Query() on policy.StockId equals stock.Id into stockJoin
            from stock in stockJoin.DefaultIfEmpty()
            select new { Policy = policy, Stock = stock };
        var search = request.Search?.Trim();
        query = query.Where(x => string.IsNullOrWhiteSpace(search)
            || x.Policy.PolicyCode.Contains(search) || x.Policy.DisplayName.Contains(search)
            || (x.Stock != null && x.Stock.ErpStockCode.Contains(search))
            || (x.Stock != null && x.Stock.StockName.Contains(search))
            || (x.Policy.StockGroupCode != null && x.Policy.StockGroupCode.Contains(search)));

        var descending = string.Equals(request.SortDirection, "desc", StringComparison.OrdinalIgnoreCase);
        var sorted = request.SortBy?.Trim().ToLowerInvariant() switch
        {
            "policycode" => descending ? query.OrderByDescending(x => x.Policy.PolicyCode) : query.OrderBy(x => x.Policy.PolicyCode),
            "displayname" => descending ? query.OrderByDescending(x => x.Policy.DisplayName) : query.OrderBy(x => x.Policy.DisplayName),
            "scope" => descending ? query.OrderByDescending(x => x.Policy.Scope) : query.OrderBy(x => x.Policy.Scope),
            "stockcode" => descending ? query.OrderByDescending(x => x.Stock == null ? null : x.Stock.ErpStockCode) : query.OrderBy(x => x.Stock == null ? null : x.Stock.ErpStockCode),
            "trackingtype" => descending ? query.OrderByDescending(x => x.Policy.TrackingType) : query.OrderBy(x => x.Policy.TrackingType),
            "serialquantityrule" => descending ? query.OrderByDescending(x => x.Policy.SerialQuantityRule) : query.OrderBy(x => x.Policy.SerialQuantityRule),
            "isactive" => descending ? query.OrderByDescending(x => x.Policy.IsActive) : query.OrderBy(x => x.Policy.IsActive),
            "createddate" => descending ? query.OrderByDescending(x => x.Policy.CreatedDate) : query.OrderBy(x => x.Policy.CreatedDate),
            _ => descending ? query.OrderByDescending(x => x.Policy.Id) : query.OrderBy(x => x.Policy.Id)
        };
        var pageNumber = PagedQueryExtensions.NormalizePageNumber(request.EffectivePageNumber);
        var pageSize = PagedQueryExtensions.NormalizePageSize(request.PageSize);
        var totalCount = await query.CountAsync(ct);
        var items = await sorted.Skip((pageNumber - 1) * pageSize).Take(pageSize)
            .Select(x => new StockTrackingPolicyRow(
                x.Policy.Id, x.Policy.BranchCode, x.Policy.PolicyCode, x.Policy.DisplayName, x.Policy.Scope,
                x.Policy.StockId, x.Stock == null ? null : x.Stock.ErpStockCode, x.Stock == null ? null : x.Stock.StockName,
                x.Policy.StockGroupCode, x.Policy.Version, x.Policy.Priority, x.Policy.TrackingType, x.Policy.RequireSerial,
                x.Policy.SerialQuantityRule, x.Policy.AutoGenerateSerials, x.Policy.RequireLot, x.Policy.RequireManufacturingDate,
                x.Policy.RequireExpirationDate, x.Policy.MinimumRemainingShelfLifeDays, x.Policy.IsActive,
                x.Policy.EffectiveFromUtc, x.Policy.EffectiveToUtc, x.Policy.Description,
                x.Policy.RowVersion, x.Policy.CreatedBy, x.Policy.CreatedDate))
            .ToListAsync(ct);
        return new PagedResponse<StockTrackingPolicyRow>
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = pageNumber,
            PageSize = pageSize
        };
    }

    public async Task<long> CreateAsync(StockTrackingPolicyUpsertRequest request, long actor, CancellationToken ct = default)
    {
        var entity = new StockTrackingPolicy();
        await ApplyAsync(entity, request, null, ct);
        entity.Version = 1;
        entity.CreatedBy = actor;
        entity.CreatedDate = DateTime.UtcNow;
        await Policies.AddAsync(entity, ct);
        await uow.SaveChangesAsync(ct);
        await audit.WriteAsync(new("stock-tracking-policy.create", nameof(StockTrackingPolicy), entity.Id.ToString(),
            "Succeeded", "stock-tracking", NewValues: Snapshot(entity), ChangedFields: ["Policy"]), ct);
        return entity.Id;
    }

    public async Task<long> CreateNextVersionAsync(long id, StockTrackingPolicyUpsertRequest request, long actor, string? concurrencyToken, CancellationToken ct = default)
    {
        var current = await Policies.FindByIdAsync(id, true, ct)
            ?? throw AppException.NotFound("Stok takip politikası bulunamadı.");
        ApplyVersion(current, concurrencyToken);
        var next = new StockTrackingPolicy();
        await ApplyAsync(next, request, current.Id, ct);
        next.PolicyCode = current.PolicyCode;
        next.Version = current.Version + 1;
        next.CreatedBy = actor;
        next.CreatedDate = DateTime.UtcNow;
        current.IsActive = false;
        current.EffectiveToUtc = DateTimeOffset.UtcNow;
        current.UpdatedBy = actor;
        current.UpdatedDate = DateTime.UtcNow;
        await Policies.AddAsync(next, ct);
        await uow.SaveChangesAsync(ct);
        await audit.WriteAsync(new("stock-tracking-policy.version", nameof(StockTrackingPolicy), next.Id.ToString(),
            "Succeeded", "stock-tracking", OldValues: Snapshot(current), NewValues: Snapshot(next),
            ChangedFields: ["Version"]), ct);
        return next.Id;
    }

    public async Task DeleteAsync(long id, long actor, CancellationToken ct = default)
    {
        var entity = await Policies.FindByIdAsync(id, true, ct)
            ?? throw AppException.NotFound("Stok takip politikası bulunamadı.");
        entity.IsActive = false;
        entity.DeletedBy = actor;
        await Policies.SoftDeleteAsync(id, ct);
        await uow.SaveChangesAsync(ct);
    }

    public async Task<StockTrackingSettings> GetStockSettingsAsync(
        string branchCode,
        long stockId,
        CancellationToken ct = default)
    {
        var branch = Branch(branchCode);
        var stock = await uow.Repository<StockEntity>().FirstOrDefaultAsync(
            x => x.Id == stockId && x.BranchCode == branch, false, ct)
            ?? throw AppException.NotFound("Stok bulunamadı.");
        var effective = await ResolveAsync(branch, stockId, ct);
        var stockOverride = effective.PolicyId.HasValue
            ? await Policies.FirstOrDefaultAsync(
                x => x.Id == effective.PolicyId.Value
                    && x.Scope == StockTrackingPolicyScope.Stock
                    && x.StockId == stockId,
                false,
                ct)
            : null;
        var now = DateTimeOffset.UtcNow;
        var serialRule = await SerialRules.Query()
            .Where(x => x.BranchCode == branch
                && x.Scope == SerialRuleScope.Stock
                && x.StockId == stockId
                && x.IsActive
                && x.EffectiveFromUtc <= now
                && (!x.EffectiveToUtc.HasValue || x.EffectiveToUtc > now))
            .OrderByDescending(x => x.Version)
            .FirstOrDefaultAsync(ct);

        return new StockTrackingSettings(
            stock.Id,
            stock.ErpStockCode,
            stock.StockName,
            stock.BranchCode,
            stock.GroupCode,
            effective.TrackingType,
            effective.RequireSerial,
            effective.SerialQuantityRule,
            effective.AutoGenerateSerials,
            serialRule?.MaskTemplate,
            serialRule?.NextSequence,
            serialRule is null ? null : Convert.ToBase64String(serialRule.RowVersion),
            effective.RequireLot,
            effective.RequireManufacturingDate,
            effective.RequireExpirationDate,
            effective.MinimumRemainingShelfLifeDays,
            stockOverride is not null,
            effective.Source,
            stockOverride?.Version,
            stockOverride is null ? null : Convert.ToBase64String(stockOverride.RowVersion));
    }

    public Task<StockTrackingSettings> UpdateStockSettingsAsync(
        long stockId,
        UpdateStockTrackingSettingsRequest request,
        long actor,
        CancellationToken ct = default) =>
        uow.ExecuteInTransactionAsync(async token =>
        {
            var branch = Branch(request.BranchCode);
            var stock = await uow.Repository<StockEntity>().FirstOrDefaultAsync(
                x => x.Id == stockId && x.BranchCode == branch, false, token)
                ?? throw AppException.NotFound("Stok bulunamadı.");
            var current = await Policies.Query()
                .Where(x => x.BranchCode == branch
                    && x.Scope == StockTrackingPolicyScope.Stock
                    && x.StockId == stockId
                    && x.IsActive)
                .OrderByDescending(x => x.Version)
                .FirstOrDefaultAsync(token);
            var now = DateTimeOffset.UtcNow;
            var serialRule = request.RequireSerial
                ? request.SerialQuantityRule
                : SerialQuantityRule.NotApplicable;
            var autoGenerateSerials = request.RequireSerial && request.AutoGenerateSerials;
            var serialMask = request.RequireSerial
                ? NormalizeSerialMask(request.SerialMaskTemplate, autoGenerateSerials)
                : null;
            if (autoGenerateSerials && serialRule != SerialQuantityRule.OneSerialPerBaseUnit)
                throw AppException.BadRequest("Otomatik seri üretimi için her birim ayrı seriyle takip edilmelidir.");
            var currentSerialRule = await SerialRules.Query(true)
                .Where(x => x.BranchCode == branch
                    && x.Scope == SerialRuleScope.Stock
                    && x.StockId == stockId
                    && x.IsActive)
                .OrderByDescending(x => x.Version)
                .FirstOrDefaultAsync(token);

            if (current is not null
                && current.RequireSerial == request.RequireSerial
                && current.SerialQuantityRule == serialRule
                && current.AutoGenerateSerials == autoGenerateSerials
                && current.RequireLot == request.RequireLot
                && current.RequireManufacturingDate == request.RequireManufacturingDate
                && current.RequireExpirationDate == request.RequireExpirationDate
                && current.MinimumRemainingShelfLifeDays == request.MinimumRemainingShelfLifeDays
                && ((!request.RequireSerial && currentSerialRule is null)
                    || (request.RequireSerial && currentSerialRule?.MaskTemplate == serialMask)))
            {
                ApplyVersion(current, request.ConcurrencyToken);
                if (currentSerialRule is not null)
                    ApplyVersion(currentSerialRule, request.SerialRuleConcurrencyToken);
                return await GetStockSettingsAsync(branch, stockId, token);
            }

            var internalRequest = new StockTrackingPolicyUpsertRequest(
                branch,
                $"STOCK-{stock.Id}",
                $"{stock.ErpStockCode} takip ayarları",
                StockTrackingPolicyScope.Stock,
                stock.Id,
                null,
                1000,
                DeriveTrackingType(request.RequireLot, request.RequireSerial),
                request.RequireSerial,
                serialRule,
                autoGenerateSerials,
                request.RequireLot,
                request.RequireManufacturingDate,
                request.RequireExpirationDate,
                request.MinimumRemainingShelfLifeDays,
                true,
                now,
                null,
                "Stok kartından yönetilen takip ayarı.");

            StockTrackingPolicy next;
            object? oldValues = null;
            if (current is null)
            {
                next = new StockTrackingPolicy();
                await ApplyAsync(next, internalRequest, null, token);
                next.Version = 1;
            }
            else
            {
                ApplyVersion(current, request.ConcurrencyToken);
                oldValues = Snapshot(current);
                next = new StockTrackingPolicy();
                await ApplyAsync(next, internalRequest, current.Id, token);
                next.PolicyCode = current.PolicyCode;
                next.Version = current.Version + 1;
                current.IsActive = false;
                current.EffectiveToUtc = now;
                current.UpdatedBy = actor;
                current.UpdatedDate = DateTime.UtcNow;
            }

            next.CreatedBy = actor;
            next.CreatedDate = DateTime.UtcNow;
            await Policies.AddAsync(next, token);
            if (request.RequireSerial)
                await UpsertStockSerialRuleAsync(stock, currentSerialRule, serialMask!, request.SerialRuleConcurrencyToken, actor, now, token);
            else if (currentSerialRule is not null)
            {
                ApplyVersion(currentSerialRule, request.SerialRuleConcurrencyToken);
                currentSerialRule.IsActive = false;
                currentSerialRule.EffectiveToUtc = now;
                currentSerialRule.UpdatedBy = actor;
                currentSerialRule.UpdatedDate = DateTime.UtcNow;
            }
            await uow.SaveChangesAsync(token);
            await audit.WriteAsync(new(
                current is null ? "stock.tracking-settings.create" : "stock.tracking-settings.update",
                nameof(StockEntity),
                stock.Id.ToString(),
                "Succeeded",
                "stock",
                OldValues: oldValues,
                NewValues: Snapshot(next),
                ChangedFields:
                [
                    nameof(next.RequireSerial),
                    nameof(next.SerialQuantityRule),
                    nameof(next.AutoGenerateSerials),
                    nameof(next.RequireLot),
                    nameof(next.RequireManufacturingDate),
                    nameof(next.RequireExpirationDate),
                    nameof(next.MinimumRemainingShelfLifeDays)
                ]), token);
            return await GetStockSettingsAsync(branch, stockId, token);
        }, ct, IsolationLevel.Serializable);

    public async Task<EffectiveStockTrackingPolicy> ResolveAsync(string branchCode, long stockId, CancellationToken ct = default)
    {
        var branch = Branch(branchCode);
        var stock = await uow.Repository<StockEntity>().FirstOrDefaultAsync(
            x => x.Id == stockId && x.BranchCode == branch, false, ct)
            ?? throw AppException.BadRequest("Stok bulunamadı.");
        var now = DateTimeOffset.UtcNow;
        var candidates = await Policies.Query().Where(x =>
            x.BranchCode == branch && x.IsActive && x.EffectiveFromUtc <= now
            && (!x.EffectiveToUtc.HasValue || x.EffectiveToUtc > now)
            && (x.Scope == StockTrackingPolicyScope.BranchDefault
                || (x.Scope == StockTrackingPolicyScope.Stock && x.StockId == stockId)
                || (x.Scope == StockTrackingPolicyScope.StockGroup && x.StockGroupCode == stock.GroupCode)))
            .ToListAsync(ct);
        var policy = candidates.OrderByDescending(x => x.Scope).ThenByDescending(x => x.Priority)
            .ThenByDescending(x => x.Version).FirstOrDefault();
        if (policy is null)
            return new(stock.Id, stock.ErpStockCode, stock.GroupCode, StockTrackingType.None,
                false, SerialQuantityRule.NotApplicable, false, false, false, false, null,
                false, "NoPolicy", null, null, null);
        return new(stock.Id, stock.ErpStockCode, stock.GroupCode, policy.TrackingType,
            policy.RequireSerial, policy.SerialQuantityRule, policy.AutoGenerateSerials, policy.RequireLot,
            policy.RequireManufacturingDate, policy.RequireExpirationDate,
            policy.MinimumRemainingShelfLifeDays, true, policy.Scope.ToString(),
            policy.Id, policy.Version, policy.PolicyCode);
    }

    private async Task ApplyAsync(StockTrackingPolicy entity, StockTrackingPolicyUpsertRequest request, long? currentId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.PolicyCode) || string.IsNullOrWhiteSpace(request.DisplayName)
            || request.Priority is < 0 or > 1000 || request.MinimumRemainingShelfLifeDays is < 0
            || request.EffectiveToUtc <= request.EffectiveFromUtc)
            throw AppException.BadRequest("Stok takip politikası alanları geçersiz.");
        var expectedType = DeriveTrackingType(request.RequireLot, request.RequireSerial);
        if (request.TrackingType != expectedType)
            throw AppException.BadRequest($"Takip tipi seçilen zorunluluklarla uyuşmuyor. Beklenen: {expectedType}.");
        if (!request.RequireSerial && request.SerialQuantityRule != SerialQuantityRule.NotApplicable)
            throw AppException.BadRequest("Seri miktar kuralı yalnızca seri zorunlu olduğunda kullanılabilir.");
        if (request.RequireSerial && request.SerialQuantityRule == SerialQuantityRule.NotApplicable)
            throw AppException.BadRequest("Seri zorunluysa seri miktar kuralı seçilmelidir.");
        if (request.AutoGenerateSerials && !request.RequireSerial)
            throw AppException.BadRequest("Otomatik seri üretimi yalnızca seri takibi açık stoklarda kullanılabilir.");
        if (request.AutoGenerateSerials && request.SerialQuantityRule != SerialQuantityRule.OneSerialPerBaseUnit)
            throw AppException.BadRequest("Otomatik seri üretimi için her birim ayrı seriyle takip edilmelidir.");
        if (request.MinimumRemainingShelfLifeDays.HasValue && !request.RequireExpirationDate)
            throw AppException.BadRequest("Minimum raf ömrü için son kullanma tarihi zorunlu olmalıdır.");

        var branch = Branch(request.BranchCode);
        var stockId = request.Scope == StockTrackingPolicyScope.Stock ? request.StockId : null;
        var group = request.Scope == StockTrackingPolicyScope.StockGroup ? Clean(request.StockGroupCode, 50) : null;
        if (request.Scope == StockTrackingPolicyScope.Stock
            && (!stockId.HasValue || !await uow.Repository<StockEntity>().AnyAsync(x => x.Id == stockId && x.BranchCode == branch, ct)))
            throw AppException.BadRequest("Kapsam stoğu bulunamadı.");
        if (request.Scope == StockTrackingPolicyScope.StockGroup && string.IsNullOrWhiteSpace(group))
            throw AppException.BadRequest("Stok grubu zorunludur.");
        if (await Policies.AnyAsync(x => x.Id != currentId && x.BranchCode == branch && x.IsActive
            && x.Scope == request.Scope && x.StockId == stockId && x.StockGroupCode == group, ct))
            throw AppException.Conflict("Bu kapsam için aktif bir stok takip politikası zaten var.");

        entity.BranchCode = branch;
        entity.PolicyCode = request.PolicyCode.Trim().ToUpperInvariant();
        entity.DisplayName = request.DisplayName.Trim();
        entity.Scope = request.Scope;
        entity.StockId = stockId;
        entity.StockGroupCode = group;
        entity.Priority = request.Priority;
        entity.TrackingType = expectedType;
        entity.RequireSerial = request.RequireSerial;
        entity.SerialQuantityRule = request.SerialQuantityRule;
        entity.AutoGenerateSerials = request.AutoGenerateSerials;
        entity.RequireLot = request.RequireLot;
        entity.RequireManufacturingDate = request.RequireManufacturingDate;
        entity.RequireExpirationDate = request.RequireExpirationDate;
        entity.MinimumRemainingShelfLifeDays = request.MinimumRemainingShelfLifeDays;
        entity.IsActive = request.IsActive;
        entity.EffectiveFromUtc = request.EffectiveFromUtc.ToUniversalTime();
        entity.EffectiveToUtc = request.EffectiveToUtc?.ToUniversalTime();
        entity.Description = Clean(request.Description, 500);
    }

    private static StockTrackingType DeriveTrackingType(bool lot, bool serial) =>
        (lot, serial) switch
        {
            (true, true) => StockTrackingType.LotAndSerial,
            (true, false) => StockTrackingType.Lot,
            (false, true) => StockTrackingType.Serial,
            _ => StockTrackingType.None
        };

    private static object Snapshot(StockTrackingPolicy x) => new
    {
        x.Id, x.PolicyCode, x.Version, x.Scope, x.StockId, x.StockGroupCode, x.TrackingType,
        x.RequireSerial, x.SerialQuantityRule, x.AutoGenerateSerials, x.RequireLot, x.RequireManufacturingDate,
        x.RequireExpirationDate, x.MinimumRemainingShelfLifeDays, x.EffectiveFromUtc, x.EffectiveToUtc, x.IsActive
    };

    private static void ApplyVersion(StockTrackingPolicy x, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        try { x.RowVersion = Convert.FromBase64String(value); }
        catch { throw AppException.Conflict("Kayıt güncellik bilgisi geçersiz."); }
    }

    private async Task UpsertStockSerialRuleAsync(
        StockEntity stock, SerialNumberRule? current, string mask, string? concurrencyToken,
        long actor, DateTimeOffset now, CancellationToken ct)
    {
        if (current is not null && current.MaskTemplate == mask)
        {
            ApplyVersion(current, concurrencyToken);
            return;
        }
        if (current is not null)
        {
            ApplyVersion(current, concurrencyToken);
            current.IsActive = false;
            current.EffectiveToUtc = now;
            current.UpdatedBy = actor;
            current.UpdatedDate = DateTime.UtcNow;
        }
        await SerialRules.AddAsync(new SerialNumberRule
        {
            BranchCode = stock.BranchCode,
            RuleCode = $"STOCK-{stock.Id}",
            DisplayName = $"{stock.ErpStockCode} seri kuralı",
            Scope = SerialRuleScope.Stock,
            StockId = stock.Id,
            Version = (current?.Version ?? 0) + 1,
            Priority = 1000,
            MaskTemplate = mask,
            CharacterSet = SerialCharacterSet.UpperAlphaNumeric,
            UniquenessScope = SerialUniquenessScope.Stock,
            MinLength = 1,
            MaxLength = 100,
            TrimWhitespace = true,
            NormalizeToUpper = true,
            NextSequence = current?.NextSequence ?? 1,
            IsRequired = true,
            IsActive = true,
            EffectiveFromUtc = now,
            Description = "Stok kartından yönetilen seri üretim ve doğrulama kuralı.",
            CreatedBy = actor,
            CreatedDate = DateTime.UtcNow
        }, ct);
    }

    private static void ApplyVersion(SerialNumberRule x, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        try { x.RowVersion = Convert.FromBase64String(value); }
        catch { throw AppException.Conflict("Seri kuralı güncellik bilgisi geçersiz."); }
    }

    private static string NormalizeSerialMask(string? value, bool automatic)
    {
        var mask = string.IsNullOrWhiteSpace(value) ? "{STOCK}-{YY}{MM}-{N:6}" : value.Trim();
        if (mask.Length > 250)
            throw AppException.BadRequest("Seri maskesi en fazla 250 karakter olabilir.");
        var tokenPattern = @"\{(?:STOCK|GROUP|YYYY|YY|MM|DD|[NAX]:[1-9]\d?)\}";
        var remainder = System.Text.RegularExpressions.Regex.Replace(mask, tokenPattern, string.Empty);
        if (remainder.Contains('{') || remainder.Contains('}'))
            throw AppException.BadRequest("Seri maskesinde desteklenmeyen alan var.");
        var sequenceTokens = System.Text.RegularExpressions.Regex.Matches(mask, @"\{N:[1-9]\d?\}").Count;
        if (automatic && sequenceTokens != 1)
            throw AppException.BadRequest("Seri maskesinde tam bir adet sıra alanı bulunmalıdır. Örnek: {STOCK}-{YY}{MM}-{N:6}.");
        if (automatic && System.Text.RegularExpressions.Regex.IsMatch(mask, @"\{[AX]:[1-9]\d?\}"))
            throw AppException.BadRequest("Otomatik seri maskesinde rastgele A/X alanları kullanılamaz.");
        return mask;
    }

    private static string Branch(string? value) => string.IsNullOrWhiteSpace(value) ? "0" : value.Trim();
    private static string? Clean(string? value, int max)
    {
        var result = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        return result?.Length > max ? result[..max] : result;
    }
}
