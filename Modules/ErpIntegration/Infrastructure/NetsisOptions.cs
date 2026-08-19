using verii_wms_api_v2.Modules.ErpIntegration.Application;

namespace verii_wms_api_v2.Modules.ErpIntegration.Infrastructure;

public sealed class NetsisOptions
{
    public const string SectionName = "Netsis";
    public bool Enabled { get; set; }
    public NetsisRestOptions Rest { get; set; } = new();
}

public sealed class NetsisRestOptions
{
    public string BaseUrl { get; set; } = string.Empty;
    public string LoginPath { get; set; } = "/api/v2/token";
    public string ItemSlipsPath { get; set; } = "/api/v2/ItemSlips";
    public int TimeoutSeconds { get; set; } = 30;
    public bool AllowInvalidSslCertificate { get; set; }
    public int DefaultTokenLifetimeMinutes { get; set; } = 60;
    public int TokenExpirySkewSeconds { get; set; } = 30;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string BranchCode { get; set; } = string.Empty;
    public string DbName { get; set; } = string.Empty;
    public string DbUser { get; set; } = string.Empty;
    public string DbPassword { get; set; } = string.Empty;
    public string DbType { get; set; } = "0";
    public int GoodsReceiptDocumentType { get; set; } = 3;
    public NetsisItemSlipInvoiceType GoodsReceiptInvoiceType { get; set; } =
        NetsisItemSlipInvoiceType.DomesticOpen;
    public int WarehouseTransferDocumentType { get; set; } =
        NetsisItemSlipDocumentTypes.LocalWarehouseTransfer;
    public int ShipmentDocumentType { get; set; } = 2;
    public bool AutoUpdateRegisteredNumber { get; set; } = true;
    public bool SendSerialsToErp { get; set; } = true;
    public string? GoodsReceiptSeries { get; set; }
    public string? WarehouseTransferSeries { get; set; }
    public string? ShipmentSeries { get; set; }
}
