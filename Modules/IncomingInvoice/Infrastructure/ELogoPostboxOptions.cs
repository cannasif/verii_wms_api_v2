namespace verii_wms_api_v2.Modules.IncomingInvoice.Infrastructure;

public sealed class ELogoPostboxOptions
{
    public const string SectionName = "ELogoPostbox";
    public string EndpointUrl { get; set; } = "https://pb.elogo.com.tr/PostBoxService.svc";
    public string ApplicationName { get; set; } = "eLogoPDF";
    public string Version { get; set; } = "1.0";
    public int TimeoutSeconds { get; set; } = 120;
    public long MaximumPayloadBytes { get; set; } = 50 * 1024 * 1024;
    public string[] AllowedHosts { get; set; } = ["pb.elogo.com.tr"];
}
