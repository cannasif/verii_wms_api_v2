namespace verii_wms_api_v2.Modules.WarehouseAssistant.Application;

/// <summary>
/// Resource limits for the in-process warehouse language engine. The assistant does not
/// call an external AI API, a local model server or a Python process.
/// </summary>
public sealed class WarehouseAssistantOptions
{
    public const string SectionName = "WarehouseAssistant";

    public string Version { get; set; } = "2.8.0";
    public int MaximumMessageCharacters { get; set; } = 2_000;
    public int MaximumQueriesPerMessage { get; set; } = 3;
    public int MaximumConversationSegments { get; set; } = 6;
}
