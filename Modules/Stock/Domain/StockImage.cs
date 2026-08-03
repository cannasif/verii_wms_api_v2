using verii_wms_api_v2.Shared.Domain;

namespace verii_wms_api_v2.Modules.Stock.Domain;

public sealed class StockImage : BaseEntity
{
    public long StockId { get; set; }
    public Stock Stock { get; set; } = null!;
    public string FileUrl { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileLength { get; set; }
    public string? AltText { get; set; }
    public int SortOrder { get; set; }
    public bool IsPrimary { get; set; }
}
