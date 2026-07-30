namespace verii_wms_api_v2.Modules.ErpMirror.Application;

public sealed record MirrorSyncResult(string Entity, int SourceCount, int Inserted, int Updated, int Deactivated);
public sealed record WarehouseMirrorDto(long Id, string BranchCode, int WarehouseCode, string WarehouseName,
    long? DefaultGoodsReceiptLocationId, DateTime? LastSyncDate, long? CreatedBy, DateTime? CreatedDate,
    long? UpdatedBy, DateTime? UpdatedDate);
public sealed record StockMirrorDto(long Id, string BranchCode, short BusinessUnitCode, string ErpStockCode, string StockName, string UnitCode, string? ManufacturerCode, string? GroupCode, string? Code1, string? Code2, string? Code3, string? Code4, string? Code5, DateTime? LastSyncDate, long? CreatedBy, DateTime? CreatedDate, long? UpdatedBy, DateTime? UpdatedDate);
public sealed record CustomerMirrorDto(long Id, string BranchCode, short BusinessUnitCode, string CustomerCode, string CustomerName, DateTime? LastSyncDate, long? CreatedBy, DateTime? CreatedDate, long? UpdatedBy, DateTime? UpdatedDate);
public sealed record ConfigurationCodeMirrorDto(long Id, string BranchCode, string ConfigurationCode, string Description, string? ConfigurableStockCode, long? StockId, DateTime? LastSyncDate, long? CreatedBy, DateTime? CreatedDate, long? UpdatedBy, DateTime? UpdatedDate);
