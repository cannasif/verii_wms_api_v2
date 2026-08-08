namespace verii_wms_api_v2.Modules.ProductionTransfer.Application;

using verii_wms_api_v2.Modules.WarehouseTransfer.Domain;
using verii_wms_api_v2.Shared.Application.Exceptions;

internal static class ProductionTransferBarcodeInput
{
    internal const string Separator = "**";
    internal const string SerialCompositeFormatMessage = "Serili okutma stokKodu**seriNo formatında olmalıdır.";

    internal sealed record Parsed(string Raw, string? StockCode, string? SerialNo)
    {
        public string ResolutionBarcode =>
            string.IsNullOrWhiteSpace(SerialNo) ? Raw : SerialNo!;
    }

    internal sealed record ResolveContext(
        long? StockId,
        long? LocationId,
        long? YapCodeId,
        string? UnitCode);

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

    internal static void EnsureBarcodeFormat(Parsed input, IReadOnlyList<ProductionTransferPickingRowDto> openRows)
    {
        if (input.StockCode is not null && input.SerialNo is null)
            throw AppException.BadRequest(SerialCompositeFormatMessage);

        if (input.StockCode is not null)
            return;

        if (openRows.Any(x => x.CanPick
            && string.IsNullOrWhiteSpace(x.SerialNo)
            && SameStockCode(x.StockCode, input.Raw)))
            return;

        if (openRows.Any(x => x.CanPick && !string.IsNullOrWhiteSpace(x.SerialNo)))
            throw AppException.BadRequest(SerialCompositeFormatMessage);
    }

    internal static void EnsureResolvableBarcode(
        Parsed input,
        IReadOnlyList<ProductionTransferPickingRowDto> openRows,
        IReadOnlyList<ProductionTransferPickingRowDto> allRows)
    {
        if (FindMatchingOpenRow(input, openRows) is not null)
        {
            EnsureBarcodeFormat(input, openRows);
            return;
        }

        var unavailableRow = FindUnavailableRow(input, openRows);
        if (unavailableRow is not null)
            throw UnavailableBalance(unavailableRow);

        var alreadyPickedRow = FindAlreadyPickedRow(input, allRows);
        if (alreadyPickedRow is not null)
            throw AlreadyPicked(alreadyPickedRow);

        EnsureBarcodeFormat(input, openRows);
    }

    internal static ProductionTransferPickingRowDto? FindUnavailableRow(
        Parsed input,
        IReadOnlyList<ProductionTransferPickingRowDto> openRows) =>
        FindUnavailableSerialRow(input, openRows) ?? FindUnavailableNonSerialRow(input, openRows);

    internal static ProductionTransferPickingRowDto? FindUnavailableNonSerialRow(
        Parsed input,
        IReadOnlyList<ProductionTransferPickingRowDto> openRows)
    {
        if (input.StockCode is not null || string.IsNullOrWhiteSpace(input.Raw))
            return null;

        return openRows.FirstOrDefault(x => x.RemainingQuantity > 0
            && !x.CanPick
            && string.IsNullOrWhiteSpace(x.SerialNo)
            && SameStockCode(x.StockCode, input.Raw));
    }

    internal static ProductionTransferPickingRowDto? FindUnavailableSerialRow(
        Parsed input,
        IReadOnlyList<ProductionTransferPickingRowDto> openRows)
    {
        var candidates = openRows
            .Where(x => x.RemainingQuantity > 0
                && !x.CanPick
                && !string.IsNullOrWhiteSpace(x.SerialNo))
            .ToArray();

        if (!string.IsNullOrWhiteSpace(input.StockCode) && !string.IsNullOrWhiteSpace(input.SerialNo))
        {
            return candidates.FirstOrDefault(x =>
                SameStockCode(x.StockCode, input.StockCode)
                && SameTrackingValue(x.SerialNo, input.SerialNo));
        }

        if (input.StockCode is null && !string.IsNullOrWhiteSpace(input.Raw))
        {
            var serialMatches = candidates
                .Where(x => SameTrackingValue(x.SerialNo, input.Raw))
                .ToArray();
            if (serialMatches.Length == 1)
                return serialMatches[0];
        }

        return null;
    }

    internal static AppException UnavailableBalance(ProductionTransferPickingRowDto row) =>
        string.IsNullOrWhiteSpace(row.SerialNo)
            ? AppException.Conflict(
                $"{row.StockCode} stoğu toplama listesinde görünüyor ancak kaynak depoda kullanılabilir stok bakiyesi bulunmuyor.")
            : AppException.Conflict(
                $"{row.StockCode} / {row.SerialNo} serisi toplama listesinde görünüyor ancak kaynak depoda kullanılabilir stok bakiyesi bulunmuyor.");

    internal static ProductionTransferPickingRowDto? FindAlreadyPickedRow(
        Parsed input,
        IReadOnlyList<ProductionTransferPickingRowDto> allRows)
    {
        var openRows = allRows.Where(x => x.RemainingQuantity > 0).ToArray();
        if (FindMatchingOpenRow(input, openRows) is not null)
            return null;
        if (FindUnavailableRow(input, openRows) is not null)
            return null;

        var completedRows = allRows
            .Where(x => x.RemainingQuantity <= 0 && x.ProcessedQuantity > 0)
            .ToArray();
        if (completedRows.Length == 0)
            return null;

        if (!string.IsNullOrWhiteSpace(input.StockCode) && !string.IsNullOrWhiteSpace(input.SerialNo))
        {
            return completedRows.FirstOrDefault(x =>
                SameStockCode(x.StockCode, input.StockCode)
                && x.SerialNo is not null
                && SameTrackingValue(x.SerialNo, input.SerialNo));
        }

        if (input.StockCode is null && !string.IsNullOrWhiteSpace(input.Raw))
        {
            var completedMatches = completedRows
                .Where(x => string.IsNullOrWhiteSpace(x.SerialNo) && SameStockCode(x.StockCode, input.Raw))
                .ToArray();
            if (completedMatches.Length == 0)
                return null;

            var hasOpenSameStock = openRows.Any(x =>
                string.IsNullOrWhiteSpace(x.SerialNo) && SameStockCode(x.StockCode, input.Raw));
            return hasOpenSameStock ? null : completedMatches[0];
        }

        return null;
    }

    internal static AppException AlreadyPicked(ProductionTransferPickingRowDto row) =>
        AlreadyPicked(row.StockCode, row.SerialNo, row.SourceLocationCode);

    internal static AppException AlreadyPicked(string stockCode, string? serialNo = null, string? locationCode = null) =>
        string.IsNullOrWhiteSpace(serialNo)
            ? AppException.Conflict(
                !string.IsNullOrWhiteSpace(locationCode)
                    ? $"{stockCode} stoğu {locationCode} rafı için zaten toplandı."
                    : $"{stockCode} stoğu zaten toplandı.")
            : AppException.Conflict($"{stockCode} / {serialNo} serisi zaten toplandı.");

    internal static ProductionTransferPickingRowDto? FindMatchingOpenRow(
        Parsed input,
        IReadOnlyList<ProductionTransferPickingRowDto> openRows)
    {
        if (!string.IsNullOrWhiteSpace(input.StockCode) && !string.IsNullOrWhiteSpace(input.SerialNo))
        {
            return openRows.FirstOrDefault(x => x.CanPick
                && SameStockCode(x.StockCode, input.StockCode)
                && x.SerialNo is not null
                && SameTrackingValue(x.SerialNo, input.SerialNo));
        }

        if (input.StockCode is null)
        {
            return openRows.FirstOrDefault(x => x.CanPick
                && string.IsNullOrWhiteSpace(x.SerialNo)
                && SameStockCode(x.StockCode, input.Raw));
        }

        return null;
    }

    internal static ResolveContext BuildResolveContext(
        Parsed input,
        IReadOnlyList<ProductionTransferPickingRowDto> openRows,
        WarehouseTransferHeader header,
        WarehouseTransferLine? matchedLine = null,
        ProductionTransferPickingRowDto? matchedRow = null)
    {
        if (matchedLine is not null)
        {
            var locationId = matchedRow?.SourceLocationId ?? matchedLine.DefaultSourceLocationId;
            return new(matchedLine.StockId, locationId, matchedLine.YapCodeId, matchedLine.UnitCode);
        }

        if (!string.IsNullOrWhiteSpace(input.StockCode) && !string.IsNullOrWhiteSpace(input.SerialNo))
        {
            var exactRows = openRows
                .Where(x => x.CanPick
                    && SameStockCode(x.StockCode, input.StockCode)
                    && x.SerialNo is not null
                    && SameTrackingValue(x.SerialNo, input.SerialNo))
                .ToArray();
            if (exactRows.Length == 1)
            {
                var line = header.Lines.Single(x => x.Id == exactRows[0].WtLineId);
                return new(
                    line.StockId,
                    exactRows[0].SourceLocationId ?? line.DefaultSourceLocationId,
                    line.YapCodeId,
                    line.UnitCode);
            }
        }

        if (!string.IsNullOrWhiteSpace(input.StockCode))
        {
            var stockIds = header.Lines
                .Where(x => SameStockCode(x.StockCodeSnapshot, input.StockCode))
                .Select(x => x.StockId)
                .Distinct()
                .ToArray();
            if (stockIds.Length == 1)
            {
                var line = header.Lines.First(x => x.StockId == stockIds[0]);
                return new(stockIds[0], line.DefaultSourceLocationId, line.YapCodeId, line.UnitCode);
            }
        }

        return new(null, null, null, null);
    }

    internal static bool SameStockCode(string? left, string? right) =>
        string.Equals(
            string.IsNullOrWhiteSpace(left) ? null : left.Trim(),
            string.IsNullOrWhiteSpace(right) ? null : right.Trim(),
            StringComparison.OrdinalIgnoreCase);

    private static bool SameTrackingValue(string? left, string? right) =>
        string.Equals(
            string.IsNullOrWhiteSpace(left) ? null : left.Trim(),
            string.IsNullOrWhiteSpace(right) ? null : right.Trim(),
            StringComparison.OrdinalIgnoreCase);
}
