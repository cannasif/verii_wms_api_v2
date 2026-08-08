using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using verii_wms_api_v2.Modules.WarehouseAssistant.Application;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class OpenAiWarehouseAssistantIntentResolverTests
{
    [Fact]
    public async Task External_intent_request_disables_storage_and_forces_a_strict_read_only_contract()
    {
        var handler = new CaptureHandler();
        using var httpClient = new HttpClient(handler);
        var resolver = new OpenAiWarehouseAssistantIntentResolver(
            httpClient,
            Options.Create(new WarehouseAssistantOptions
            {
                EnableOpenAiIntentResolution = true,
                ApiKey = "test-key",
                Model = "test-model"
            }),
            new WarehouseAssistantIntentResolver(),
            NullLogger<OpenAiWarehouseAssistantIntentResolver>.Instance);

        var result = await resolver.ResolveAsync("ambiguous warehouse question", null);

        Assert.Equal(WarehouseAssistantIntent.Unknown, result.Intent);
        Assert.False(string.IsNullOrWhiteSpace(handler.RequestBody));
        Assert.False(string.IsNullOrWhiteSpace(handler.ClientRequestId));
        using var document = JsonDocument.Parse(handler.RequestBody!);
        var root = document.RootElement;
        Assert.False(root.GetProperty("store").GetBoolean());
        Assert.False(root.GetProperty("parallel_tool_calls").GetBoolean());
        Assert.Equal(300, root.GetProperty("max_output_tokens").GetInt32());
        var tool = root.GetProperty("tools")[0];
        Assert.True(tool.GetProperty("strict").GetBoolean());
        Assert.False(tool.GetProperty("parameters").GetProperty("additionalProperties").GetBoolean());
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public string? RequestBody { get; private set; }
        public string? ClientRequestId { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            ClientRequestId = request.Headers.GetValues("X-Client-Request-Id").Single();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"output\":[]}", Encoding.UTF8, "application/json")
            };
        }
    }
}
