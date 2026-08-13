namespace verii_wms_api_v2.Modules.NetsisRead.Application.Dtos;

public sealed record BranchDto(short SubeKodu, string? Unvan);
public sealed record WarehouseDto(short DepoKodu, string DepoIsmi, short SubeKodu);
public sealed record StockDto(
    short SubeKodu,
    short IsletmeKodu,
    string StokKodu,
    string UreticiKodu,
    string StokAdi,
    string GrupKodu,
    string Kod1,
    string Kod2,
    string Kod3,
    string Kod4,
    string Kod5,
    string? OlcuBr1);

/// <summary>
/// Read-only projection returned by dbo.RII_FN_STOCK_BALANCE.
/// This is ERP data and is intentionally not tracked as an EF entity.
/// </summary>
public sealed record NetsisStockBalanceDto(
    short? WarehouseCode,
    string StockCode,
    decimal Balance);

public sealed record CustomerDto(
    short SubeKodu,
    short IsletmeKodu,
    string CariKod,
    string? CariIsim,
    string? CariTel,
    string? CariIl,
    string? UlkeKodu,
    string? CariTip,
    string? CariAdres,
    string? CariIlce,
    string? VergiDairesi,
    string? Email,
    string? Web,
    string? CariTel2,
    string? CariTel3);
public sealed record ConfigurationCodeDto(
    string ConfigurationCode,
    string Description,
    short? BranchCode,
    string? ConfigurableStockCode,
    long? StockId);

// Backward-compatible contract for clients still using Netsis field terminology.
public sealed record LegacyYapCodeDto(
    string YapKod,
    string YapAcik,
    short? SubeKodu,
    string? YapilandirilabilirStokKodu,
    long? StockId);

public sealed record GoodsReceiptOpenOrderHeaderDto(
    string Mode, string SiparisNo, int? OrderId, string? CustomerCode, string? CustomerName,
    int? BranchCode, int? TargetWarehouseCode, string? ProjectCode, DateTime? OrderDate,
    DateTime? DeliveryDate,
    decimal? OrderedQuantity, decimal? DeliveredQuantity, decimal? RemainingQuantity,
    decimal? PlannedQuantity, decimal? AvailableQuantity);

public sealed record GoodsReceiptOpenOrderLineDto(
    string Mode, string SiparisNo, int OrderId, int OrderLineSequence, string? StockCode, string? StockName,
    string? UnitCode, string? YapCode, string? YapDescription, string? CustomerCode, string? CustomerName,
    int? BranchCode, int? TargetWarehouseCode, string? ProjectCode, DateTime? OrderDate,
    DateTime? DeliveryDate, decimal? NetUnitPrice, decimal? GrossUnitPrice,
    decimal? OrderedQuantity, decimal? DeliveredQuantity, decimal? RemainingQuantity,
    decimal? PlannedQuantity, decimal? AvailableQuantity);

public sealed record WarehouseTransferOpenOrderHeaderDto(
    string Mode,string OrderNumber,int? OrderId,string? CustomerCode,string? CustomerName,
    int? BranchCode,int? TargetWarehouseCode,string? ProjectCode,DateTime? OrderDate,
    decimal? OrderedQuantity,decimal? DeliveredQuantity,decimal? RemainingQuantity,
    decimal? PlannedQuantity,decimal? AvailableQuantity);

public sealed record WarehouseTransferOpenOrderLineDto(
    string Mode,string OrderNumber,int OrderId,int OrderLineSequence,string? StockCode,string? StockName,
    string? UnitCode,string? YapCode,string? YapDescription,string? CustomerCode,string? CustomerName,
    int? BranchCode,int? TargetWarehouseCode,string? ProjectCode,DateTime? OrderDate,DateTime? DeliveryDate,
    decimal? NetUnitPrice,decimal? GrossUnitPrice,
    decimal? OrderedQuantity,decimal? DeliveredQuantity,decimal? RemainingQuantity,
    decimal? PlannedQuantity,decimal? AvailableQuantity);

public sealed record ShipmentOpenOrderHeaderDto(
    string Mode,string OrderNumber,long? OrderId,string? CustomerCode,string? CustomerName,
    int? BranchCode,int? TargetWarehouseCode,string? ProjectCode,DateTime? OrderDate,
    decimal? OrderedQuantity,decimal? DeliveredQuantity,decimal? RemainingQuantity,
    decimal? PlannedQuantity,decimal? AvailableQuantity);
public sealed record ShipmentOpenOrderLineDto(
    string Mode,string OrderNumber,long OrderId,int OrderLineSequence,string? StockCode,string? StockName,
    string? UnitCode,string? YapCode,string? YapDescription,string? CustomerCode,string? CustomerName,
    int? BranchCode,int? TargetWarehouseCode,string? ProjectCode,DateTime? OrderDate,DateTime? DeliveryDate,
    decimal? NetUnitPrice,decimal? GrossUnitPrice,
    decimal? OrderedQuantity,decimal? DeliveredQuantity,decimal? RemainingQuantity,
    decimal? PlannedQuantity,decimal? AvailableQuantity);

/// <summary>V1 uyumlu KKD cari açık sipariş satırı (RII_FN_KKD_CARIACIKSIPARISGETIR).</summary>
public sealed record KkdCustomerOpenOrderDto(
    string StockCode,
    string? GroupCode,
    string OrderNumber,
    DateTime? OrderDate,
    decimal RemainingQuantity,
    string? CustomerCode,
    int? WarehouseCode,
    long? OrderId,
    string? StockName,
    string? UnitCode,
    string? ProjectCode);

/// <summary>Read-only Netsis production work-order projection. No ERP row is tracked by EF.</summary>
public sealed record ProductionWorkOrderDto(
    string WorkOrderNumber,
    int? BranchCode,
    string StockCode,
    string StockName,
    string? ConfigurationCode,
    decimal WorkOrderQuantity,
    int UnitSequence,
    string? UnitCode,
    decimal RecipeTotal,
    DateTime? WorkOrderDate,
    DateTime? DeliveryDate,
    string? OrderNumber,
    int OrderLineSequence,
    string? ProjectCode,
    int WarehouseCode,
    int IssueWarehouseCode,
    bool IsClosed,
    string? Description = null);

/// <summary>One material component of a Netsis stock recipe.</summary>
public sealed record StockRecipeComponentDto(
    int BranchCode,
    string ProductCode,
    string ProductName,
    string? ProductUnitCode,
    string? ProductConfigurationCode,
    string ComponentStockCode,
    string? ComponentStockName,
    string? ComponentUnitCode,
    string? ComponentConfigurationCode,
    int OperationNumber,
    decimal RecipeTotal,
    decimal RecipeQuantity,
    decimal QuantityPerProduct,
    decimal WasteValue,
    decimal FixedWasteQuantity,
    bool IsQuantityFixed);

/// <summary>A Netsis work order resolved into its material requirements.</summary>
public sealed record ProductionWorkOrderRecipeComponentDto(
    string WorkOrderNumber,
    int? BranchCode,
    string ProductCode,
    string ProductName,
    string? ConfigurationCode,
    decimal WorkOrderQuantity,
    string? ProductUnitCode,
    decimal RecipeTotal,
    string ComponentStockCode,
    string? ComponentStockName,
    string? ComponentUnitCode,
    string? ComponentConfigurationCode,
    int OperationNumber,
    decimal RecipeQuantity,
    decimal QuantityPerProduct,
    decimal WasteValue,
    decimal FixedWasteQuantity,
    bool IsQuantityFixed,
    decimal BaseRequiredQuantity,
    decimal VariableWasteQuantity,
    decimal TotalRequiredQuantity);
