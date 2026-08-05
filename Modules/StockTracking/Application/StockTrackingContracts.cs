using verii_wms_api_v2.Modules.StockTracking.Domain;
using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using verii_wms_api_v2.Shared;

namespace verii_wms_api_v2.Modules.StockTracking.Application;

public sealed record StockTrackingPolicyUpsertRequest(
    string BranchCode, string PolicyCode, string DisplayName, StockTrackingPolicyScope Scope,
    long? StockId, string? StockGroupCode, int Priority, StockTrackingType TrackingType,
    bool RequireSerial, SerialQuantityRule SerialQuantityRule, bool AutoGenerateSerials, bool RequireLot,
    bool RequireManufacturingDate, bool RequireExpirationDate, int? MinimumRemainingShelfLifeDays,
    bool IsActive, DateTimeOffset EffectiveFromUtc, DateTimeOffset? EffectiveToUtc, string? Description);

public sealed record StockTrackingPolicyRow(
    long Id, string BranchCode, string PolicyCode, string DisplayName, StockTrackingPolicyScope Scope,
    long? StockId, string? StockCode, string? StockName, string? StockGroupCode,
    int Version, int Priority, StockTrackingType TrackingType, bool RequireSerial,
    SerialQuantityRule SerialQuantityRule, bool AutoGenerateSerials, bool RequireLot, bool RequireManufacturingDate,
    bool RequireExpirationDate, int? MinimumRemainingShelfLifeDays, bool IsActive,
    DateTimeOffset EffectiveFromUtc, DateTimeOffset? EffectiveToUtc, string? Description,
    byte[] ConcurrencyToken, long? CreatedBy, DateTime? CreatedDate);

public sealed record EffectiveStockTrackingPolicy(
    long StockId, string StockCode, string? StockGroupCode, StockTrackingType TrackingType,
    bool RequireSerial, SerialQuantityRule SerialQuantityRule, bool AutoGenerateSerials, bool RequireLot,
    bool RequireManufacturingDate, bool RequireExpirationDate, int? MinimumRemainingShelfLifeDays,
    bool HasPolicy, string Source, long? PolicyId, int? PolicyVersion, string? PolicyCode);

public sealed record StockTrackingSettings(
    long StockId, string StockCode, string StockName, string BranchCode, string? StockGroupCode,
    StockTrackingType TrackingType, bool RequireSerial, SerialQuantityRule SerialQuantityRule,
    bool AutoGenerateSerials, string? SerialMaskTemplate, long? NextSerialSequence,
    string? SerialRuleConcurrencyToken, bool RequireLot, bool RequireManufacturingDate, bool RequireExpirationDate,
    int? MinimumRemainingShelfLifeDays, bool HasStockOverride, string Source,
    int? Version, string? ConcurrencyToken);

public sealed record UpdateStockTrackingSettingsRequest(
    string BranchCode, bool RequireSerial, SerialQuantityRule SerialQuantityRule,
    bool AutoGenerateSerials, string? SerialMaskTemplate, bool RequireLot,
    bool RequireManufacturingDate, bool RequireExpirationDate,
    int? MinimumRemainingShelfLifeDays, string? ConcurrencyToken, string? SerialRuleConcurrencyToken);

public sealed record StockTrackingCapture(
    decimal Quantity, string? LotNo, string? SerialNo, DateOnly? ManufacturingDate, DateOnly? ExpirationDate);

public interface IStockTrackingPolicyService
{
    Task<PagedResponse<StockTrackingPolicyRow>> GetPagedAsync(PagedRequest request, CancellationToken ct = default);
    Task<long> CreateAsync(StockTrackingPolicyUpsertRequest request, long actor, CancellationToken ct = default);
    Task<long> CreateNextVersionAsync(long id, StockTrackingPolicyUpsertRequest request, long actor, string? concurrencyToken, CancellationToken ct = default);
    Task DeleteAsync(long id, long actor, CancellationToken ct = default);
    Task<StockTrackingSettings> GetStockSettingsAsync(string branchCode, long stockId, CancellationToken ct = default);
    Task<StockTrackingSettings> UpdateStockSettingsAsync(long stockId, UpdateStockTrackingSettingsRequest request, long actor, CancellationToken ct = default);
}

public interface IStockTrackingPolicyResolver
{
    Task<EffectiveStockTrackingPolicy> ResolveAsync(string branchCode, long stockId, CancellationToken ct = default);
}

public static class StockTrackingPolicyGuard
{
    public static void ValidateSerialQuantity(
        EffectiveStockTrackingPolicy policy,
        decimal quantity,
        string? serialNo)
    {
        if (string.IsNullOrWhiteSpace(serialNo)) return;
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
        if (policy.SerialQuantityRule == SerialQuantityRule.OneSerialPerBaseUnit && quantity != 1)
            throw new StockTrackingPolicyViolationException(
                $"{policy.StockCode} için her stok birimi ayrı seriyle ve 1 miktarla işlenmelidir.");
    }

    public static void ValidateSerialMovementQuantity(
        EffectiveStockTrackingPolicy policy,
        decimal requestedQuantity,
        decimal availableQuantityAtSource,
        string? serialNo)
    {
        if (string.IsNullOrWhiteSpace(serialNo)) return;
        ValidateSerialQuantity(policy, requestedQuantity, serialNo);

        if (availableQuantityAtSource <= 0)
            throw new StockTrackingPolicyViolationException(
                $"{policy.StockCode} / {serialNo.Trim()} serisi seçilen kaynak rafta kullanılabilir değil.");

        if (policy.SerialQuantityRule == SerialQuantityRule.OneSerialPerBaseUnit
            && availableQuantityAtSource != 1)
            throw new StockTrackingPolicyViolationException(
                $"{policy.StockCode} / {serialNo.Trim()} adet-serisinin kaynak raf bakiyesi 1 olmalıdır; mevcut bakiye {availableQuantityAtSource:0.######}. " +
                "Seri bakiyesi veri bütünlüğü kontrolü gerektiriyor.");

        if (policy.SerialQuantityRule == SerialQuantityRule.OneSerialPerLine
            && requestedQuantity != availableQuantityAtSource)
            throw new StockTrackingPolicyViolationException(
                $"{policy.StockCode} / {serialNo.Trim()} tek bir levha/palet serisidir ve raflar arasında kısmi bölünemez. " +
                $"Kaynak raftaki {availableQuantityAtSource:0.######} miktarın tamamını seçin veya önce kontrollü seri bölme işlemi yapın; istenen miktar {requestedQuantity:0.######}.");
    }

    public static void Validate(
        EffectiveStockTrackingPolicy policy,
        decimal requestedQuantity,
        StockTrackingType submittedTrackingType,
        IReadOnlyCollection<StockTrackingCapture> captures,
        bool requireCompleteCapture)
    {
        if (requestedQuantity <= 0) throw new ArgumentOutOfRangeException(nameof(requestedQuantity));
        // ResolveAsync her stok için (eşleşen kural yoksa Takipsiz varsayılanıyla)
        // kesin bir sonuç üretir. İstemcinin takip boyutunu seçmesine izin verilmez.
        if (submittedTrackingType != policy.TrackingType)
            throw new StockTrackingPolicyViolationException(
                $"{policy.StockCode} takip tipi kullanıcı tarafından değiştirilemez. Beklenen: {Display(policy.TrackingType)}.");

        if (captures.Count == 0)
        {
            if (requireCompleteCapture && policy.TrackingType != StockTrackingType.None)
                throw new StockTrackingPolicyViolationException($"{policy.StockCode} için seri/lot bilgileri tamamlanmalıdır.");
            return;
        }

        if (captures.Any(x => x.Quantity <= 0))
            throw new StockTrackingPolicyViolationException("Seri/lot miktarı sıfırdan büyük olmalıdır.");
        var capturedQuantity = captures.Sum(x => x.Quantity);
        if (capturedQuantity > requestedQuantity || (requireCompleteCapture && capturedQuantity != requestedQuantity))
            throw new StockTrackingPolicyViolationException(
                $"Seri/lot dağılımı işlem miktarıyla eşleşmelidir. Beklenen {requestedQuantity}, girilen {capturedQuantity}.");
        if (policy.RequireSerial && captures.Any(x => string.IsNullOrWhiteSpace(x.SerialNo)))
            throw new StockTrackingPolicyViolationException($"{policy.StockCode} için seri numarası zorunludur.");
        if (policy.RequireLot && captures.Any(x => string.IsNullOrWhiteSpace(x.LotNo)))
            throw new StockTrackingPolicyViolationException($"{policy.StockCode} için lot numarası zorunludur.");
        if (policy.RequireManufacturingDate && captures.Any(x => !x.ManufacturingDate.HasValue))
            throw new StockTrackingPolicyViolationException($"{policy.StockCode} için üretim tarihi zorunludur.");
        if (policy.RequireExpirationDate && captures.Any(x => !x.ExpirationDate.HasValue))
            throw new StockTrackingPolicyViolationException($"{policy.StockCode} için son kullanma tarihi zorunludur.");

        var serials = captures.Where(x => !string.IsNullOrWhiteSpace(x.SerialNo))
            .Select(x => x.SerialNo!.Trim()).ToArray();
        if (serials.Length != serials.Distinct(StringComparer.OrdinalIgnoreCase).Count())
            throw new StockTrackingPolicyViolationException("Aynı seri numarası bir işlem satırında tekrar edemez.");

        foreach (var capture in captures)
            ValidateSerialQuantity(policy, capture.Quantity, capture.SerialNo);

        if (policy.SerialQuantityRule == SerialQuantityRule.OneSerialPerBaseUnit)
        {
            if (decimal.Truncate(requestedQuantity) != requestedQuantity)
                throw new StockTrackingPolicyViolationException($"{policy.StockCode} seri adetli takip edilir; kesirli miktar kullanılamaz.");
            if (captures.Any(x => x.Quantity != 1) || (requireCompleteCapture && captures.Count != decimal.ToInt32(requestedQuantity)))
                throw new StockTrackingPolicyViolationException(
                    $"{policy.StockCode} için her birim ayrı ve benzersiz seriyle girilmelidir. Beklenen seri adedi: {requestedQuantity:0}.");
        }

        if (policy.MinimumRemainingShelfLifeDays.HasValue)
        {
            var minimum = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(policy.MinimumRemainingShelfLifeDays.Value);
            if (captures.Any(x => x.ExpirationDate.HasValue && x.ExpirationDate.Value < minimum))
                throw new StockTrackingPolicyViolationException(
                    $"{policy.StockCode} için son kullanma tarihi en az {policy.MinimumRemainingShelfLifeDays.Value} gün ileride olmalıdır.");
        }
    }

    private static string Display(StockTrackingType value) => value switch
    {
        StockTrackingType.Serial => "Seri",
        StockTrackingType.Lot => "Lot",
        StockTrackingType.LotAndSerial => "Lot + Seri",
        _ => "Takipsiz"
    };
}

public sealed class StockTrackingPolicyViolationException(string message) : Exception(message);
