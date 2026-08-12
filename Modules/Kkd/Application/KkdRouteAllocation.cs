using verii_wms_api_v2.Modules.BarcodeDesigner.Application;

namespace verii_wms_api_v2.Modules.Kkd.Application;

/// <summary>
/// Üretimin ProductionTransferRouteAllocation.AllocateGreedyNonSerial'ının sade KKD karşılığı.
/// KKD görev satırlarında üretimdeki gibi kardeş-satır klonlamaya gerek yok — bir satırın
/// birden fazla rafa bölünmesi doğrudan KkdPreparationTaskLineLocation satırlarıyla temsil edilir.
/// Raf adayları, paylaşılan barkod çözücünün zaten hesapladığı WarehouseBarcodeBalanceCandidate
/// listesinden (bkz. KkdPreparationTaskService) geliyor — burada sadece dağıtım algoritması var.
/// </summary>
internal static class KkdRouteAllocation
{
    internal sealed record Chunk(long? LocationId, decimal Quantity, string? SerialNo, string? LotNo);

    /// <summary>Serisiz: ihtiyaç bitene kadar rafları sırayla (en çok bakiyeliden) tüket.</summary>
    internal static IReadOnlyList<Chunk> AllocateGreedy(
        decimal needed,
        IReadOnlyCollection<WarehouseBarcodeBalanceCandidate> candidates)
    {
        if (needed <= 0) return [];
        var remaining = needed;
        var result = new List<Chunk>();
        foreach (var candidate in candidates.Where(x => string.IsNullOrWhiteSpace(x.SerialNo)))
        {
            if (remaining <= 0) break;
            var take = Math.Min(remaining, candidate.AvailableQuantity);
            if (take <= 0) continue;
            result.Add(new(candidate.LocationId, take, null, candidate.LotNo));
            remaining -= take;
        }
        if (remaining > 0)
            result.Add(new(null, remaining, null, null));
        return result;
    }

    /// <summary>Serili: her seri 1 birim; ihtiyaç kadar farklı seriyi ayrı raf/seri satırı olarak al.</summary>
    internal static IReadOnlyList<Chunk> AllocateSerial(
        int needed,
        IReadOnlyCollection<WarehouseBarcodeBalanceCandidate> candidates)
    {
        if (needed <= 0) return [];
        var result = candidates
            .Where(x => !string.IsNullOrWhiteSpace(x.SerialNo))
            .Take(needed)
            .Select(x => new Chunk(x.LocationId, 1m, x.SerialNo, x.LotNo))
            .ToList();
        for (var i = result.Count; i < needed; i++)
            result.Add(new(null, 1m, null, null));
        return result;
    }
}
