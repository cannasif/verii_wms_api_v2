using System.Text.Json.Serialization;

namespace verii_wms_api_v2.Shared;

public sealed class PagedRequest
{
    private IReadOnlyList<string> searchFields = Array.Empty<string>();
    private IReadOnlyList<AdvancedFilterRequest> filters = Array.Empty<AdvancedFilterRequest>();
    private IReadOnlyList<long> actorUserIds = Array.Empty<long>();

    // CRM sözleşmesi. Page alanı eski istemciler için geriye uyumluluk sağlar.
    public int PageNumber { get; init; } = 1;
    public int Page { get; init; }
    public int PageSize { get; init; } = 10;

    // İstemcinin gönderdiği ham arama metni sözleşmede aynen korunur.
    public string? Search { get; init; }

    public IReadOnlyList<string> SearchFields
    {
        get => searchFields;
        init => searchFields = value ?? Array.Empty<string>();
    }
    public string? SortBy { get; init; }
    public string SortDirection { get; init; } = "asc";
    public string FilterLogic { get; init; } = "and";
    public IReadOnlyList<AdvancedFilterRequest> Filters
    {
        get => filters;
        init => filters = value ?? Array.Empty<AdvancedFilterRequest>();
    }
    public IReadOnlyList<long> ActorUserIds
    {
        get => actorUserIds;
        init => actorUserIds = value ?? Array.Empty<long>();
    }
    public bool ActorIncludeSystem { get; init; }

    [JsonIgnore]
    public int EffectivePageNumber => Page > 0 ? Page : PageNumber;

    [JsonIgnore]
    public string? EffectiveSearch => Search;

    [JsonIgnore]
    public bool HasExplicitSearchFields => SearchFields.Count > 0;

    // SearchFields gönderen istemciler ortak allowlist tabanlı arama katmanına
    // gider. Bu değer yalnız geriye uyumlu modül içi geniş OR sorguları içindir.
    [JsonIgnore]
    internal string? LegacySearch => HasExplicitSearchFields ? null : Search;

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
