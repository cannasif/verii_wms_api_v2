namespace verii_wms_api_v2.Modules.Kkd.Application;

/// <summary>
/// Üretimin ProductionTransferBarcodeInput.Parse'ı ile aynı "StokKodu**SeriNo" ayracı.
/// Elle yazarken stok kodunu ve seriyi birleştirmek için; fiziksel barkod okutmada
/// (GS1) zaten ayraca gerek yok, ham metin doğrudan genel çözücüye gider.
/// </summary>
internal static class KkdBarcodeInput
{
    internal const string Separator = "**";

    internal sealed record Parsed(string Raw, string? StockCode, string? SerialNo);

    internal static Parsed Parse(string barcode)
    {
        var raw = (barcode ?? string.Empty).Trim();
        var separatorIndex = raw.IndexOf(Separator, StringComparison.Ordinal);
        if (separatorIndex < 0)
            return new(raw, null, null);

        var stockCode = raw[..separatorIndex].Trim();
        var serialNo = raw[(separatorIndex + Separator.Length)..].Trim();
        if (string.IsNullOrWhiteSpace(stockCode) || string.IsNullOrWhiteSpace(serialNo))
            return new(raw, null, null);

        return new(raw, stockCode, serialNo);
    }
}
