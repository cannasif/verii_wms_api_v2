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
public sealed record CustomerDto(short SubeKodu, short IsletmeKodu, string CariKod, string? CariIsim);
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
    decimal? OrderedQuantity, decimal? DeliveredQuantity, decimal? RemainingQuantity,
    decimal? PlannedQuantity, decimal? AvailableQuantity);

public sealed record GoodsReceiptOpenOrderLineDto(
    string Mode, string SiparisNo, int OrderId, string? StockCode, string? StockName,
    string? UnitCode, string? YapCode, string? YapDescription, string? CustomerCode, string? CustomerName,
    int? BranchCode, int? TargetWarehouseCode, string? ProjectCode, DateTime? OrderDate,
    decimal? OrderedQuantity, decimal? DeliveredQuantity, decimal? RemainingQuantity,
    decimal? PlannedQuantity, decimal? AvailableQuantity);

public sealed record WarehouseTransferOpenOrderHeaderDto(
    string Mode,string OrderNumber,int? OrderId,string? CustomerCode,string? CustomerName,
    int? BranchCode,int? TargetWarehouseCode,string? ProjectCode,DateTime? OrderDate,
    decimal? OrderedQuantity,decimal? DeliveredQuantity,decimal? RemainingQuantity,
    decimal? PlannedQuantity,decimal? AvailableQuantity);

public sealed record WarehouseTransferOpenOrderLineDto(
    string Mode,string OrderNumber,int OrderId,string? StockCode,string? StockName,
    string? YapCode,string? YapDescription,string? CustomerCode,string? CustomerName,
    int? BranchCode,int? TargetWarehouseCode,string? ProjectCode,DateTime? OrderDate,
    decimal? OrderedQuantity,decimal? DeliveredQuantity,decimal? RemainingQuantity,
    decimal? PlannedQuantity,decimal? AvailableQuantity);

public sealed record ShipmentOpenOrderHeaderDto(
    string Mode,string OrderNumber,long? OrderId,string? CustomerCode,string? CustomerName,
    int? BranchCode,int? TargetWarehouseCode,string? ProjectCode,DateTime? OrderDate,
    decimal? OrderedQuantity,decimal? DeliveredQuantity,decimal? RemainingQuantity,
    decimal? PlannedQuantity,decimal? AvailableQuantity);
public sealed record ShipmentOpenOrderLineDto(
    string Mode,string OrderNumber,long OrderId,string? StockCode,string? StockName,
    string? YapCode,string? YapDescription,string? CustomerCode,string? CustomerName,
    int? BranchCode,int? TargetWarehouseCode,string? ProjectCode,DateTime? OrderDate,
    decimal? OrderedQuantity,decimal? DeliveredQuantity,decimal? RemainingQuantity,
    decimal? PlannedQuantity,decimal? AvailableQuantity);
