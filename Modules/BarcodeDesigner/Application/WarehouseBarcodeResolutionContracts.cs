namespace verii_wms_api_v2.Modules.BarcodeDesigner.Application;

public enum WarehouseBarcodePurpose
{
    Lookup = 0,
    Inbound = 1,
    Outbound = 2
}

public sealed record ResolveWarehouseBarcodeRequest(
    string Barcode,
    string BranchCode,
    WarehouseBarcodePurpose Purpose,
    long? WarehouseId = null,
    long? ExpectedStockId = null,
    long? ExpectedLocationId = null);

public sealed record WarehouseBarcodeBalanceCandidate(
    long BalanceId,
    long WarehouseId,
    long LocationId,
    string LocationCode,
    string LocationName,
    long StockId,
    long? YapCodeId,
    string UnitCode,
    string? LotNo,
    string? SerialNo,
    string StockStatus,
    decimal AvailableQuantity);

public sealed record ResolvedWarehouseBarcode(
    string RawBarcode,
    string Source,
    long StockId,
    string StockCode,
    string StockName,
    long? YapCodeId,
    string? YapCode,
    decimal? Quantity,
    string UnitCode,
    string? LotNo,
    string? SerialNo,
    DateOnly? ManufacturingDate,
    DateOnly? ExpirationDate,
    bool RequireSerial,
    bool RequireLot,
    bool RequireManufacturingDate,
    bool RequireExpirationDate,
    IReadOnlyList<string> MissingFields,
    IReadOnlyList<WarehouseBarcodeBalanceCandidate> BalanceCandidates,
    long? SuggestedLocationId,
    bool CanExecute);

public interface IWarehouseBarcodeResolver
{
    Task<ResolvedWarehouseBarcode> ResolveAsync(
        ResolveWarehouseBarcodeRequest request,
        CancellationToken cancellationToken = default);
}
