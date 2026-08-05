namespace verii_wms_api_v2.Modules.WarehouseTransfer.Application;

internal readonly record struct WarehouseTransferSerialBalanceKey(
    long StockId,
    long? YapCodeId,
    long WarehouseId,
    long LocationId,
    string UnitCode,
    string LotNo,
    string SerialNo,
    string StockStatus)
{
    public static WarehouseTransferSerialBalanceKey Create(
        long stockId,
        long? yapCodeId,
        long warehouseId,
        long locationId,
        string unitCode,
        string? lotNo,
        string serialNo,
        string stockStatus) => new(
            stockId,
            yapCodeId,
            warehouseId,
            locationId,
            unitCode.Trim().ToUpperInvariant(),
            lotNo?.Trim().ToUpperInvariant() ?? string.Empty,
            serialNo.Trim().ToUpperInvariant(),
            stockStatus.Trim());
}
