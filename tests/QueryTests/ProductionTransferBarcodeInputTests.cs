using verii_wms_api_v2.Modules.BarcodeDesigner.Application;
using verii_wms_api_v2.Modules.ProductionTransfer.Application;
using verii_wms_api_v2.Modules.WarehouseTransfer.Domain;
using verii_wms_api_v2.Shared.Application.Exceptions;
using Xunit;

namespace verii_wms_api_v2.tests.QueryTests;

public sealed class ProductionTransferBarcodeInputTests
{
    [Fact]
    public void Parse_reads_stock_code_and_serial_from_composite_barcode()
    {
        var parsed = ProductionTransferBarcodeInput.Parse("01/013**UTG-1");

        Assert.Equal("01/013", parsed.StockCode);
        Assert.Equal("UTG-1", parsed.SerialNo);
        Assert.Equal("UTG-1", parsed.ResolutionBarcode);
    }

    [Fact]
    public void Parse_keeps_plain_stock_code_including_hyphenated_codes()
    {
        var parsed = ProductionTransferBarcodeInput.Parse("100134-1");

        Assert.Null(parsed.StockCode);
        Assert.Null(parsed.SerialNo);
        Assert.Equal("100134-1", parsed.ResolutionBarcode);
        Assert.Null(WarehouseBarcodeParser.TryParse(parsed.Raw));
    }

    [Fact]
    public void FindMatchingOpenRow_matches_hyphenated_plain_stock_code()
    {
        var openRows = new[] { NonSerialRow("100134-1") };

        var matched = ProductionTransferBarcodeInput.FindMatchingOpenRow(
            ProductionTransferBarcodeInput.Parse("100134-1"),
            openRows);

        Assert.NotNull(matched);
        Assert.Equal("100134-1", matched!.StockCode);
        Assert.Null(matched.SerialNo);
    }

    [Fact]
    public void EnsureBarcodeFormat_allows_plain_stock_code_for_non_serial_rows()
    {
        var openRows = new[]
        {
            SerialRow("01/013", "UTG-1"),
            NonSerialRow("01/019"),
        };

        var exception = Record.Exception(() =>
            ProductionTransferBarcodeInput.EnsureBarcodeFormat(
                ProductionTransferBarcodeInput.Parse("01/019"),
                openRows));

        Assert.Null(exception);
    }

    [Fact]
    public void EnsureBarcodeFormat_allows_plain_stock_code_for_single_open_serial_row()
    {
        var openRows = new[] { SerialRow("01/026", "UTG-9") };

        var exception = Record.Exception(() =>
            ProductionTransferBarcodeInput.EnsureBarcodeFormat(
                ProductionTransferBarcodeInput.Parse("01/026"),
                openRows));

        Assert.Null(exception);
    }

    [Fact]
    public void FindMatchingOpenRow_matches_plain_stock_code_for_single_open_serial_row()
    {
        var openRows = new[] { SerialRow("01/026", "UTG-9") };

        var matched = ProductionTransferBarcodeInput.FindMatchingOpenRow(
            ProductionTransferBarcodeInput.Parse("01/026"),
            openRows);

        Assert.NotNull(matched);
        Assert.Equal("UTG-9", matched!.SerialNo);
    }

    [Fact]
    public void EnrichFromMatchedRow_adds_serial_context_for_plain_stock_scan()
    {
        var enriched = ProductionTransferBarcodeInput.EnrichFromMatchedRow(
            ProductionTransferBarcodeInput.Parse("01/026"),
            SerialRow("01/026", "UTG-9"));

        Assert.Equal("01/026", enriched.StockCode);
        Assert.Equal("UTG-9", enriched.SerialNo);
        Assert.Equal("UTG-9", enriched.ResolutionBarcode);
    }

    [Fact]
    public void EnsureBarcodeFormat_requires_composite_format_for_serial_only_open_rows()
    {
        var openRows = new[] { SerialRow("01/013", "UTG-1") };

        var error = Assert.Throws<AppException>(() =>
            ProductionTransferBarcodeInput.EnsureBarcodeFormat(
                ProductionTransferBarcodeInput.Parse("UTG-1"),
                openRows));

        Assert.Equal(ProductionTransferBarcodeInput.SerialCompositeFormatMessage, error.Message);
    }

    [Fact]
    public void EnsureResolvableBarcode_reports_already_picked_before_serial_format_requirement()
    {
        var allRows = new[]
        {
            PickedNonSerialRow("01/019", "A1"),
            SerialRow("01/013", "UTG-1"),
        };
        var openRows = allRows.Where(x => x.RemainingQuantity > 0).ToArray();

        var error = Assert.Throws<AppException>(() =>
            ProductionTransferBarcodeInput.EnsureResolvableBarcode(
                ProductionTransferBarcodeInput.Parse("01/019"),
                openRows,
                allRows));

        Assert.Contains("zaten toplandı", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(ProductionTransferBarcodeInput.SerialCompositeFormatMessage, error.Message);
    }

    [Fact]
    public void EnsureResolvableBarcode_reports_missing_balance_for_unavailable_serial_row()
    {
        var openRows = new[] { UnavailableSerialRow("01/013", "UTG-9") };

        var error = Assert.Throws<AppException>(() =>
            ProductionTransferBarcodeInput.EnsureResolvableBarcode(
                ProductionTransferBarcodeInput.Parse("01/013**UTG-9"),
                openRows,
                openRows));

        Assert.Contains("kullanılabilir stok bakiyesi bulunmuyor", error.Message, StringComparison.Ordinal);
        Assert.Contains("UTG-9", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EnsureResolvableBarcode_reports_missing_balance_for_unavailable_non_serial_row()
    {
        var allRows = new[]
        {
            UnavailableNonSerialRow("01/019"),
            SerialRow("01/013", "UTG-1"),
        };
        var openRows = allRows.Where(x => x.RemainingQuantity > 0).ToArray();

        var error = Assert.Throws<AppException>(() =>
            ProductionTransferBarcodeInput.EnsureResolvableBarcode(
                ProductionTransferBarcodeInput.Parse("01/019"),
                openRows,
                allRows));

        Assert.Contains("01/019", error.Message, StringComparison.Ordinal);
        Assert.Contains("kullanılabilir stok bakiyesi bulunmuyor", error.Message, StringComparison.Ordinal);
        Assert.NotEqual(ProductionTransferBarcodeInput.SerialCompositeFormatMessage, error.Message);
    }

    [Fact]
    public void EnsureResolvableBarcode_reports_missing_balance_when_assigned_location_is_not_pickable()
    {
        var openRows = new[]
        {
            new ProductionTransferPickingRowDto(
                400, 20, 2, 26, "01/026", 19, "01/013", "Non serial", null, 3, 3, 0, false),
        };

        var error = Assert.Throws<AppException>(() =>
            ProductionTransferBarcodeInput.EnsureResolvableBarcode(
                ProductionTransferBarcodeInput.Parse("01/013"),
                openRows,
                openRows));

        Assert.Contains("01/013", error.Message, StringComparison.Ordinal);
        Assert.Contains("kullanılabilir stok bakiyesi bulunmuyor", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EnsureResolvableBarcode_reports_missing_balance_when_assigned_location_barcode_is_scanned()
    {
        var openRows = new[]
        {
            new ProductionTransferPickingRowDto(
                400, 20, 2, 26, "01/026", 19, "01/013", "Non serial", null, 3, 3, 0, false),
        };

        var error = Assert.Throws<AppException>(() =>
            ProductionTransferBarcodeInput.EnsureResolvableBarcode(
                ProductionTransferBarcodeInput.Parse("01/026"),
                openRows,
                openRows));

        Assert.Contains("01/013", error.Message, StringComparison.Ordinal);
        Assert.Contains("kullanılabilir stok bakiyesi bulunmuyor", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildResolveContext_returns_line_location_for_exact_serial_match()
    {
        var header = new WarehouseTransferHeader
        {
            Lines =
            [
                new()
                {
                    Id = 10,
                    LineNo = 1,
                    StockId = 13,
                    StockCodeSnapshot = "01/013",
                    DefaultSourceLocationId = 5,
                    UnitCode = "ADET",
                },
            ],
        };
        var openRows = new[]
        {
            SerialRow("01/013", "UTG-1", wtLineId: 10, sourceLocationId: 5),
            SerialRow("01/013", "UTG-2", wtLineId: 10, sourceLocationId: 5),
        };

        var context = ProductionTransferBarcodeInput.BuildResolveContext(
            ProductionTransferBarcodeInput.Parse("01/013**UTG-1"),
            openRows,
            header);

        Assert.Equal(13, context.StockId);
        Assert.Equal(5, context.LocationId);
        Assert.Equal("ADET", context.UnitCode);
    }

    private static ProductionTransferPickingRowDto SerialRow(
        string stockCode,
        string serialNo,
        long wtLineId = 10,
        long sourceLocationId = 5) =>
        new(100, wtLineId, 1, sourceLocationId, "A-01", 13, stockCode, "Test", serialNo, 1, 1, 0, true);

    private static ProductionTransferPickingRowDto NonSerialRow(string stockCode) =>
        new(200, 20, 2, 6, "B-01", 19, stockCode, "Non serial", null, 5, 5, 0, true);

    private static ProductionTransferPickingRowDto UnavailableSerialRow(string stockCode, string serialNo) =>
        new(300, 10, 1, null, null, 13, stockCode, "Test", serialNo, 1, 1, 0, false);

    private static ProductionTransferPickingRowDto UnavailableNonSerialRow(string stockCode) =>
        new(400, 20, 2, null, null, 19, stockCode, "Non serial", null, 5, 5, 0, false);

    [Fact]
    public void FindAlreadyPickedRow_returns_completed_serial_but_not_when_sibling_serial_is_open()
    {
        var allRows = new[]
        {
            PickedSerialRow("01/013", "UTG-1"),
            SerialRow("01/013", "UTG-2"),
        };

        var picked = ProductionTransferBarcodeInput.FindAlreadyPickedRow(
            ProductionTransferBarcodeInput.Parse("01/013**UTG-1"),
            allRows);

        Assert.NotNull(picked);
        Assert.Equal("UTG-1", picked!.SerialNo);
        Assert.Null(ProductionTransferBarcodeInput.FindAlreadyPickedRow(
            ProductionTransferBarcodeInput.Parse("01/013**UTG-2"),
            allRows));
    }

    [Fact]
    public void FindAlreadyPickedRow_returns_completed_non_serial_only_when_no_open_row_on_other_rack()
    {
        var allRows = new[]
        {
            PickedNonSerialRow("01/019", "A1"),
            OpenNonSerialRow("01/019", "A2", 3),
        };

        Assert.Null(ProductionTransferBarcodeInput.FindAlreadyPickedRow(
            ProductionTransferBarcodeInput.Parse("01/019"),
            allRows));

        var onlyCompleted = new[] { PickedNonSerialRow("01/019", "A1"), PickedNonSerialRow("01/019", "A2") };
        var picked = ProductionTransferBarcodeInput.FindAlreadyPickedRow(
            ProductionTransferBarcodeInput.Parse("01/019"),
            onlyCompleted);

        Assert.NotNull(picked);
    }

    [Fact]
    public void FindAlreadyPickedRow_prefers_unavailable_balance_over_already_picked()
    {
        var allRows = new[]
        {
            PickedNonSerialRow("01/019", "A1"),
            UnavailableNonSerialRow("01/019"),
        };

        Assert.Null(ProductionTransferBarcodeInput.FindAlreadyPickedRow(
            ProductionTransferBarcodeInput.Parse("01/019"),
            allRows));
        var openRows = allRows.Where(x => x.RemainingQuantity > 0).ToArray();
        Assert.Throws<AppException>(() =>
            ProductionTransferBarcodeInput.EnsureResolvableBarcode(
                ProductionTransferBarcodeInput.Parse("01/019"),
                openRows,
                allRows));
    }

    private static ProductionTransferPickingRowDto PickedSerialRow(string stockCode, string serialNo) =>
        new(500, 10, 1, 5, "A1", 13, stockCode, "Test", serialNo, 1, 0, 1, false);

    private static ProductionTransferPickingRowDto PickedNonSerialRow(string stockCode, string locationCode) =>
        new(600, 20, 2, 6, locationCode, 19, stockCode, "Test", null, 2, 0, 2, false);

    private static ProductionTransferPickingRowDto OpenNonSerialRow(string stockCode, string locationCode, decimal remaining) =>
        new(601, 21, 2, 7, locationCode, 19, stockCode, "Test", null, 3, remaining, 0, true);
}
