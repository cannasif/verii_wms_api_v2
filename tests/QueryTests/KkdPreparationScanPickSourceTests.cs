using verii_wms_api_v2.Modules.BarcodeDesigner.Application;
using verii_wms_api_v2.Modules.Kkd.Application;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class KkdPreparationScanPickSourceTests
{
    [Fact]
    public void Unique_candidate_allows_auto_pick()
    {
        var resolved = Resolved(candidates: [Candidate(locationId: 11)]);

        Assert.True(KkdPreparationScanPickService.HasUniquePickSource(resolved));
    }

    [Fact]
    public void Multiple_racks_block_auto_pick_until_one_is_suggested()
    {
        var unresolved = Resolved(candidates: [Candidate(11), Candidate(12)]);
        var suggested = Resolved(
            suggestedLocationId: 12,
            candidates: [Candidate(11), Candidate(12)]);

        Assert.False(KkdPreparationScanPickService.HasUniquePickSource(unresolved));
        Assert.True(KkdPreparationScanPickService.HasUniquePickSource(suggested));
    }

    [Fact]
    public void Select_requires_explicit_rack_when_several_candidates_match()
    {
        var resolved = Resolved(candidates:
        [
            Candidate(11, serial: "A1"),
            Candidate(12, serial: "A2"),
        ]);

        Assert.Null(KkdPreparationScanPickService.SelectBalanceCandidate(resolved, null, null, null));
        Assert.Equal(12, KkdPreparationScanPickService.SelectBalanceCandidate(resolved, 12, null, null)?.LocationId);
        Assert.Equal("A1", KkdPreparationScanPickService.SelectBalanceCandidate(resolved, null, "a1", null)?.SerialNo);
    }

    [Fact]
    public void Single_candidate_is_used_when_no_rack_is_posted()
    {
        var resolved = Resolved(candidates: [Candidate(11, serial: "S9", lot: "L1")]);

        var selected = KkdPreparationScanPickService.SelectBalanceCandidate(resolved, null, null, null);

        Assert.NotNull(selected);
        Assert.Equal(11, selected.LocationId);
        Assert.Equal("S9", selected.SerialNo);
        Assert.Equal("L1", selected.LotNo);
    }

    private static ResolvedWarehouseBarcode Resolved(
        long? suggestedLocationId = null,
        params WarehouseBarcodeBalanceCandidate[] candidates) =>
        new(
            RawBarcode: "STK-1",
            Source: "StockCode",
            StockId: 41,
            StockCode: "STK-1",
            StockName: "Eldiven",
            YapCodeId: null,
            YapCode: null,
            Quantity: 1m,
            UnitCode: "AD",
            LotNo: null,
            SerialNo: null,
            ManufacturingDate: null,
            ExpirationDate: null,
            RequireSerial: false,
            RequireLot: false,
            RequireManufacturingDate: false,
            RequireExpirationDate: false,
            MissingFields: [],
            BalanceCandidates: candidates,
            SuggestedLocationId: suggestedLocationId,
            CanExecute: true);

    private static WarehouseBarcodeBalanceCandidate Candidate(
        long locationId,
        string? serial = null,
        string? lot = null) =>
        new(
            BalanceId: locationId,
            WarehouseId: 1,
            LocationId: locationId,
            LocationCode: $"R{locationId}",
            LocationName: $"Raf {locationId}",
            StockId: 41,
            YapCodeId: null,
            UnitCode: "AD",
            LotNo: lot,
            SerialNo: serial,
            StockStatus: "Available",
            AvailableQuantity: 4m);
}
