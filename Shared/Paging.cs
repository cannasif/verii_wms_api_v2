using System.Text.Json.Serialization;

namespace verii_wms_api_v2.Shared;

public sealed class PagedRequest
{
    private string? _search;

    // CRM sözleşmesi. Page alanı eski istemciler için geriye uyumluluk sağlar.
    public int PageNumber { get; init; } = 1;
    public int Page { get; init; }
    public int PageSize { get; init; } = 10;

    // SearchFields gönderen yeni istemcilerde eski modül içi geniş OR filtreleri
    // çalıştırılmaz. Ham değer ortak, allowlist tabanlı arama katmanında kullanılır.
    public string? Search
    {
        get => HasExplicitSearchFields ? null : _search;
        init => _search = value;
    }

    public IReadOnlyList<string> SearchFields { get; init; } = Array.Empty<string>();
    public string? SortBy { get; init; }
    public string SortDirection { get; init; } = "asc";
    public string FilterLogic { get; init; } = "and";
    public IReadOnlyList<AdvancedFilterRequest> Filters { get; init; } = Array.Empty<AdvancedFilterRequest>();

    [JsonIgnore]
    public int EffectivePageNumber => Page > 0 ? Page : PageNumber;

    [JsonIgnore]
    public string? EffectiveSearch => _search;

    [JsonIgnore]
    public bool HasExplicitSearchFields => SearchFields.Count > 0;

    [JsonIgnore]
    internal bool SearchApplied { get; private set; }

    internal void MarkSearchApplied() => SearchApplied = true;
}

public sealed record AdvancedFilterRequest(string Column, string Operator, string? Value);

public sealed class PagedResponse<T>
{
    public IReadOnlyList<T> Items { get; init; } = Array.Empty<T>();
    public int TotalCount { get; init; }
    public int PageNumber { get; init; }
    public int Page => PageNumber;
    public int PageSize { get; init; }
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling(TotalCount / (double)PageSize) : 0;
    public bool HasPreviousPage => PageNumber > 1;
    public bool HasNextPage => PageNumber < TotalPages;
}
