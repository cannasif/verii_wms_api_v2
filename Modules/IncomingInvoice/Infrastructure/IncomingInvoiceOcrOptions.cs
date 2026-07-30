namespace verii_wms_api_v2.Modules.IncomingInvoice.Infrastructure;

public sealed class IncomingInvoiceOcrOptions
{
    public const string SectionName = "IncomingInvoiceOcr";

    public string Provider { get; set; } = "AzureDocumentIntelligence";
    public string? Endpoint { get; set; }
    public string? ApiKey { get; set; }
    public string ModelId { get; set; } = "prebuilt-invoice";
    public string ApiVersion { get; set; } = "2024-11-30";
    public int TimeoutSeconds { get; set; } = 90;
    public int MaximumFileSizeMb { get; set; } = 20;
}
