using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using CustomerEntity = verii_wms_api_v2.Modules.Customer.Domain.Customer;
using StockEntity = verii_wms_api_v2.Modules.Stock.Domain.Stock;
using static verii_wms_api_v2.Modules.WarehouseAssistant.Localization.WarehouseAssistantMessageKeys;

namespace verii_wms_api_v2.Modules.WarehouseAssistant.Application;

public sealed partial class WarehouseAssistantService
{
    private const int MaximumEntitySearchPool = 5000;
    private const int MaximumEntitySuggestions = 8;
    private const decimal MinimumEntityMatchScore = 0.56m;

    private async Task<EntityLookupResult<StockEntity>> ResolveStockAsync(
        string? structuredQuery,
        string message,
        string branchCode,
        CancellationToken ct)
    {
        var terms = ExtractEntitySearchTerms(structuredQuery, message, EntityKind.Stock);
        if (terms.Count == 0)
            return EntityLookupResult<StockEntity>.Empty(string.Empty);

        var stocks = await unitOfWork.Repository<StockEntity>().Query()
            .Where(x => x.BranchCode == branchCode)
            .OrderBy(x => x.Id)
            .Take(MaximumEntitySearchPool)
            .ToListAsync(ct);

        var ranked = stocks
            .Select(stock => new RankedEntity<StockEntity>(
                stock,
                stock.Id,
                stock.ErpStockCode,
                stock.StockName,
                ScoreEntity(terms, message, stock.ErpStockCode, stock.StockName)))
            .Where(x => x.Score.Score >= MinimumEntityMatchScore)
            .OrderByDescending(x => x.Score.IsExactCode)
            .ThenByDescending(x => x.Score.IsExactName)
            .ThenByDescending(x => x.Score.Score)
            .ThenBy(x => x.Code, StringComparer.OrdinalIgnoreCase)
            .Take(MaximumEntitySuggestions)
            .ToArray();

        var exact = ranked.Where(x => x.Score.IsExactCode).ToArray();
        if (exact.Length == 1)
            return new EntityLookupResult<StockEntity>(exact[0].Entity, [], terms[0]);

        var exactNames = ranked.Where(x => x.Score.IsExactName).ToArray();
        if (exact.Length == 0 && exactNames.Length == 1)
            return new EntityLookupResult<StockEntity>(exactNames[0].Entity, [], terms[0]);

        return new EntityLookupResult<StockEntity>(
            null,
            ranked.Select(x => ToCandidate(EntityKind.Stock, x.Id, x.Code, x.Name, x.Score, message)).ToArray(),
            terms[0]);
    }

    private async Task<EntityLookupResult<SupplierMatch>> ResolveSupplierAsync(
        string? structuredQuery,
        string message,
        string branchCode,
        CancellationToken ct)
    {
        var terms = ExtractEntitySearchTerms(structuredQuery, message, EntityKind.Customer);
        if (terms.Count == 0)
            return EntityLookupResult<SupplierMatch>.Empty(string.Empty);

        var customers = await unitOfWork.Repository<CustomerEntity>().Query()
            .Where(x => x.BranchCode == branchCode)
            .OrderBy(x => x.Id)
            .Select(x => new SupplierMatch(x.Id, x.CustomerCode, x.CustomerName))
            .Take(MaximumEntitySearchPool)
            .ToListAsync(ct);

        var historical = await unitOfWork.Repository<verii_wms_api_v2.Modules.GoodsReceipt.Domain.GoodsReceiptHeader>().Query()
            .Where(x => x.BranchCode == branchCode
                && x.SupplierCodeSnapshot != null
                && x.SupplierNameSnapshot != null)
            .Select(x => new SupplierMatch(x.SupplierId, x.SupplierCodeSnapshot!, x.SupplierNameSnapshot!))
            .Distinct()
            .Take(1000)
            .ToListAsync(ct);

        var source = customers
            .Concat(historical)
            .GroupBy(x => NormalizeComparable(x.Code), StringComparer.Ordinal)
            .Select(x => x.OrderByDescending(row => row.Id.HasValue).First())
            .ToArray();
        var ranked = source
            .Select(customer => new RankedEntity<SupplierMatch>(
                customer,
                customer.Id,
                customer.Code,
                customer.Name,
                ScoreEntity(terms, message, customer.Code, customer.Name)))
            .Where(x => x.Score.Score >= MinimumEntityMatchScore)
            .OrderByDescending(x => x.Score.IsExactCode)
            .ThenByDescending(x => x.Score.IsExactName)
            .ThenByDescending(x => x.Score.Score)
            .ThenBy(x => x.Code, StringComparer.OrdinalIgnoreCase)
            .Take(MaximumEntitySuggestions)
            .ToArray();

        var exact = ranked.Where(x => x.Score.IsExactCode).ToArray();
        if (exact.Length == 1)
            return new EntityLookupResult<SupplierMatch>(exact[0].Entity, [], terms[0]);

        var exactNames = ranked.Where(x => x.Score.IsExactName).ToArray();
        if (exact.Length == 0 && exactNames.Length == 1)
            return new EntityLookupResult<SupplierMatch>(exactNames[0].Entity, [], terms[0]);

        return new EntityLookupResult<SupplierMatch>(
            null,
            ranked.Select(x => ToCandidate(EntityKind.Customer, x.Id, x.Code, x.Name, x.Score, message)).ToArray(),
            terms[0]);
    }

    private ExecutionResult EntityClarification(
        WarehouseAssistantIntent intent,
        string searchTerm,
        IReadOnlyList<WarehouseAssistantEntityCandidateRow> candidates)
    {
        var shownTerm = string.IsNullOrWhiteSpace(searchTerm) ? "?" : searchTerm;
        return new ExecutionResult(
            intent,
            "authorized",
            "resolve-entity-reference",
            M(candidates.Count > 0 ? EntityClarificationFound : EntityClarificationNotFound, shownTerm),
            [], [], [], [], null, [], [],
            new WarehouseAssistantContext(null, null, null),
            [],
            EntityCandidates: candidates);
    }

    private WarehouseAssistantEntityCandidateRow ToCandidate(
        EntityKind kind,
        long? id,
        string code,
        string name,
        EntityScore score,
        string originalMessage) => new(
            kind == EntityKind.Stock ? "stock" : "customer",
            id,
            code,
            name,
            score.MatchedBy,
            decimal.Round(score.Score, 3),
            BuildEntitySelectionMessage(kind, originalMessage, code));

    private WarehouseAssistantEntityCandidateRow ToExactCandidate(
        EntityKind kind,
        long? id,
        string code,
        string name,
        string originalMessage) => ToCandidate(
            kind,
            id,
            code,
            name,
            new EntityScore(1m, true, false, "code"),
            originalMessage);

    private string BuildEntitySelectionMessage(EntityKind kind, string originalMessage, string code)
    {
        if (localizer is not null)
            return M(kind == EntityKind.Stock ? EntitySelectionStock : EntitySelectionCustomer, originalMessage, code);
        return kind == EntityKind.Stock
            ? $"{originalMessage}\nKastettiğim stok kodu: \"{code}\"."
            : $"{originalMessage}\nKastettiğim cari kodu: \"{code}\".";
    }

    private static IReadOnlyList<string> ExtractEntitySearchTerms(
        string? structuredQuery,
        string message,
        EntityKind kind)
    {
        var source = string.Join(' ', new[] { structuredQuery, message }.Where(x => !string.IsNullOrWhiteSpace(x)));
        var result = new List<string>();
        var contextPattern = kind == EntityKind.Stock
            ? @"(?:([\p{L}\p{N}][\p{L}\p{N}._/-]{1,80})\s+(?:stok\w*|ürün\w*|urun\w*|malzeme\w*|mamul\w*)|(?:stok|ürün|urun|malzeme|mamul)\w*\s*(?:kodu|kod|adı|adi|no)?\s*(?:[:=#]\s*)?([\p{L}\p{N}][\p{L}\p{N}._/-]{1,80}))"
            : @"(?:([\p{L}\p{N}][\p{L}\p{N}._/-]{1,80})\s+(?:cari\w*|müşteri\w*|musteri\w*|tedarikçi\w*|tedarikci\w*)|(?:cari|müşteri|musteri|tedarikçi|tedarikci)\w*\s*(?:kodu|kod|adı|adi|no)?\s*(?:[:=#]\s*)?([\p{L}\p{N}][\p{L}\p{N}._/-]{1,80}))";
        result.AddRange(Regex.Matches(source, contextPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            .Select(match => match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value)
            .Where(x => !string.IsNullOrWhiteSpace(x)));
        if (kind == EntityKind.Customer)
            result.AddRange(ExtractTurkishSourceReferences(source));
        result.AddRange(Regex.Matches(source, "[\"'“”]([^\"'“”]{2,100})[\"'“”]")
            .Select(x => x.Groups[1].Value.Trim()));
        result.AddRange(Regex.Matches(source, @"\b[\p{L}\p{N}]+(?:[-/._][\p{L}\p{N}]+)+\b")
            .Select(x => x.Value.Trim()));

        var normalized = WarehouseAssistantIntentResolver.Normalize(source);
        var words = Regex.Matches(normalized, @"[\p{L}\p{N}]{2,80}")
            .Select(x => x.Value)
            .Where(x => !IsEntityStopWord(x, kind));
        result.AddRange(words);

        return result
            .Select(x => x.Trim(' ', '.', ',', ';', ':', '?', '!', '(', ')'))
            .Where(x => x.Length >= 2)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(30)
            .ToArray();
    }

    private static bool HasExplicitEntityReference(string message, EntityKind kind)
    {
        if (Regex.IsMatch(message, "[\"'“”][^\"'“”]{2,100}[\"'“”]")) return true;
        if (Regex.IsMatch(message, @"\b[\p{L}\p{N}]+(?:[-/._][\p{L}\p{N}]+)+\b")) return true;
        if (kind == EntityKind.Customer && ExtractTurkishSourceReferences(message).Any()) return true;
        var pattern = kind == EntityKind.Stock
            ? @"(?:([\p{L}\p{N}][\p{L}\p{N}._/-]{1,80})\s+(?:stok\w*|ürün\w*|urun\w*|malzeme\w*|mamul\w*)|(?:stok|ürün|urun|malzeme|mamul)\w*\s*(?:kodu|kod|adı|adi|no)?\s*(?:[:=#]\s*)?([\p{L}\p{N}][\p{L}\p{N}._/-]{1,80}))"
            : @"(?:([\p{L}\p{N}][\p{L}\p{N}._/-]{1,80})\s+(?:cari\w*|müşteri\w*|musteri\w*|tedarikçi\w*|tedarikci\w*)|(?:cari|müşteri|musteri|tedarikçi|tedarikci)\w*\s*(?:kodu|kod|adı|adi|no)?\s*(?:[:=#]\s*)?([\p{L}\p{N}][\p{L}\p{N}._/-]{1,80}))";
        return Regex.Matches(message, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            .Select(match => match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value)
            .Select(WarehouseAssistantIntentResolver.Normalize)
            .Any(value => value.Length >= 2 && !IsEntityStopWord(value, kind));
    }

    private static string? ExtractUntypedEntityReference(string message)
    {
        var sourceReference = ExtractTurkishSourceReferences(message).FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(sourceReference)) return sourceReference;

        var match = Regex.Match(
            message,
            @"\b([\p{L}\p{N}][\p{L}\p{N}._/-]{1,80})\s+(?:için|icin|firmas\w*|company|vendor|supplier)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success) return null;
        var value = match.Groups[1].Value.Trim();
        var normalized = WarehouseAssistantIntentResolver.Normalize(value);
        return IsEntityStopWord(normalized, EntityKind.Stock) || IsEntityStopWord(normalized, EntityKind.Customer)
            ? null
            : value;
    }

    private static IEnumerable<string> ExtractTurkishSourceReferences(string message) =>
        Regex.Matches(
                message,
                @"(?<![\p{L}\p{N}])([\p{L}\p{N}][\p{L}\p{N}._/-]{1,80})\s*['’]?\s*(?:den|dan|ten|tan)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            .Select(match => match.Groups[1].Value.Trim())
            .Where(value =>
            {
                var normalized = WarehouseAssistantIntentResolver.Normalize(value);
                return normalized.Length >= 2
                    && !IsEntityStopWord(normalized, EntityKind.Customer)
                    && normalized is not "dun" and not "bugun" and not "hafta" and not "ay" and not "depo";
            });

    private static EntityScore ScoreEntity(
        IReadOnlyList<string> terms,
        string originalMessage,
        string code,
        string name)
    {
        var normalizedMessage = WarehouseAssistantIntentResolver.Normalize(originalMessage);
        var normalizedCode = WarehouseAssistantIntentResolver.Normalize(code);
        var normalizedName = WarehouseAssistantIntentResolver.Normalize(name);
        var compactCode = NormalizeComparable(code);
        var isExactCode = terms.Any(term => NormalizeComparable(term) == compactCode)
            || ContainsWholeValue(normalizedMessage, normalizedCode);
        var isExactName = normalizedName.Length >= 3
            && (terms.Any(term => WarehouseAssistantIntentResolver.Normalize(term) == normalizedName)
                || normalizedMessage.Contains(normalizedName, StringComparison.Ordinal));
        if (isExactCode) return new EntityScore(1m, true, isExactName, "code");
        if (isExactName) return new EntityScore(0.98m, false, true, "name");

        var codeScore = terms
            .Select(term => Similarity(NormalizeComparable(term), compactCode, true))
            .DefaultIfEmpty(0m)
            .Max();
        var nameScore = terms
            .Select(term => Similarity(WarehouseAssistantIntentResolver.Normalize(term), normalizedName, false))
            .DefaultIfEmpty(0m)
            .Max();
        var tokenScore = TokenCoverage(terms, normalizedName);
        nameScore = Math.Max(nameScore, tokenScore);

        return codeScore >= nameScore
            ? new EntityScore(codeScore, false, false, "code")
            : new EntityScore(nameScore, false, false, "name");
    }

    private static decimal Similarity(string query, string candidate, bool code)
    {
        if (string.IsNullOrWhiteSpace(query) || string.IsNullOrWhiteSpace(candidate)) return 0m;
        if (candidate.StartsWith(query, StringComparison.Ordinal)) return code ? 0.93m : 0.90m;
        if (candidate.Contains(query, StringComparison.Ordinal)) return code ? 0.86m : 0.83m;
        if (query.Contains(candidate, StringComparison.Ordinal) && candidate.Length >= 3) return code ? 0.82m : 0.79m;
        var distance = DamerauLevenshteinDistance(query, candidate);
        var ratio = 1m - ((decimal)distance / Math.Max(query.Length, candidate.Length));
        return ratio < 0 ? 0 : ratio;
    }

    private static decimal TokenCoverage(IReadOnlyList<string> terms, string normalizedName)
    {
        var nameTokens = Regex.Matches(normalizedName, @"[\p{L}\p{N}]{3,80}")
            .Select(x => x.Value)
            .ToArray();
        if (nameTokens.Length == 0) return 0m;
        var queryTokens = terms
            .Select(WarehouseAssistantIntentResolver.Normalize)
            .Where(x => x.Length >= 3)
            .ToArray();
        if (queryTokens.Length == 0) return 0m;

        var bestMatches = nameTokens
            .Select(nameToken => queryTokens.Max(queryToken => Similarity(queryToken, nameToken, false)))
            .OrderByDescending(x => x)
            .ToArray();
        var strongest = bestMatches[0];
        var average = bestMatches.Average();
        return Math.Max(strongest * 0.88m, average);
    }

    private static int DamerauLevenshteinDistance(string source, string target)
    {
        var matrix = new int[source.Length + 1, target.Length + 1];
        for (var i = 0; i <= source.Length; i++) matrix[i, 0] = i;
        for (var j = 0; j <= target.Length; j++) matrix[0, j] = j;
        for (var i = 1; i <= source.Length; i++)
        {
            for (var j = 1; j <= target.Length; j++)
            {
                var cost = source[i - 1] == target[j - 1] ? 0 : 1;
                matrix[i, j] = Math.Min(
                    Math.Min(matrix[i - 1, j] + 1, matrix[i, j - 1] + 1),
                    matrix[i - 1, j - 1] + cost);
                if (i > 1 && j > 1 && source[i - 1] == target[j - 2] && source[i - 2] == target[j - 1])
                    matrix[i, j] = Math.Min(matrix[i, j], matrix[i - 2, j - 2] + cost);
            }
        }
        return matrix[source.Length, target.Length];
    }

    private static string NormalizeComparable(string value) => Regex.Replace(
        WarehouseAssistantIntentResolver.Normalize(value),
        @"[^\p{L}\p{N}]",
        string.Empty);

    private static bool ContainsWholeValue(string source, string value)
    {
        if (value.Length < 2) return false;
        return Regex.IsMatch(source, $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(value)}(?![\p{{L}}\p{{N}}])");
    }

    private static bool IsEntityStopWord(string value, EntityKind kind)
    {
        var common = new HashSet<string>(StringComparer.Ordinal)
        {
            "acaba", "adet", "adi", "adli", "ara", "arasi", "bakiye", "bana", "benzer", "bugun",
            "bul", "cari", "carisi", "carisine", "depo", "goster", "hangi", "hareket", "icin", "ile",
            "kac", "kodu", "kodlu", "mal", "miktar", "nerede", "olan", "olarak", "stok", "stoku",
            "stokun", "stokunun", "tarih", "urun", "urunu", "urunler", "var", "yapildi", "yapilan",
            "customer", "item", "material", "product", "show", "stock", "supplier", "vendor", "where",
            "gecen", "onceki", "dun", "bugun", "hafta", "ay"
        };
        if (common.Contains(value)) return true;
        if (kind == EntityKind.Stock && (value.StartsWith("stok", StringComparison.Ordinal) || value.StartsWith("urun", StringComparison.Ordinal)))
            return true;
        if (kind == EntityKind.Customer && (value.StartsWith("cari", StringComparison.Ordinal) || value.StartsWith("muster", StringComparison.Ordinal)))
            return true;
        return DateOnly.TryParse(value, out _) || (value.All(char.IsDigit) && value.Length == 4);
    }

    private enum EntityKind { Stock, Customer }

    private sealed record EntityScore(decimal Score, bool IsExactCode, bool IsExactName, string MatchedBy);

    private sealed record RankedEntity<TEntity>(
        TEntity Entity,
        long? Id,
        string Code,
        string Name,
        EntityScore Score);

    private sealed record EntityLookupResult<TEntity>(
        TEntity? Entity,
        IReadOnlyList<WarehouseAssistantEntityCandidateRow> Candidates,
        string SearchTerm)
        where TEntity : class
    {
        public static EntityLookupResult<TEntity> Empty(string searchTerm) => new(null, [], searchTerm);
    }
}
