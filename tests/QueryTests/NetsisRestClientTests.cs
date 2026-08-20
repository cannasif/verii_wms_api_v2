using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using verii_wms_api_v2.Modules.ErpIntegration.Application;
using verii_wms_api_v2.Modules.ErpIntegration.Infrastructure;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class NetsisRestClientTests
{
    [Fact]
    public async Task Unauthorized_response_refreshes_token_once_and_rebuilds_post_request()
    {
        var handler = new QueueHandler(
            _ => new HttpResponseMessage(HttpStatusCode.Unauthorized),
            _ => Json(HttpStatusCode.OK, """{"isSuccessful":true,"data":{"fisNo":"GR-1"}}"""));
        var tokens = new FakeTokenService();
        var client = CreateClient(handler, tokens);

        var result = await client.CreateItemSlipAsync(SampleRequest(), CancellationToken.None);

        Assert.True(result.BusinessSucceeded);
        Assert.Equal("GR-1", result.Data?.Data?.FisNo);
        Assert.Equal([false, true], tokens.ForceRefreshCalls);
        Assert.Equal(["7", "7"], tokens.BranchCalls);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task Timeout_after_post_is_marked_commit_uncertain()
    {
        var handler = new QueueHandler(_ => throw new TaskCanceledException("timeout"));
        var client = CreateClient(handler, new FakeTokenService());

        var result = await client.CreateItemSlipAsync(SampleRequest(), CancellationToken.None);

        Assert.False(result.TransportSucceeded);
        Assert.True(result.CommitUncertain);
        Assert.Equal("ERP_TIMEOUT_COMMIT_UNCERTAIN", result.ErrorCode);
    }

    [Fact]
    public async Task Http_200_with_business_error_is_not_successful()
    {
        var handler = new QueueHandler(
            _ => Json(HttpStatusCode.OK, """{"isSuccessful":false,"errorCode":"E42","errorDesc":"Belge reddedildi"}"""));
        var client = CreateClient(handler, new FakeTokenService());

        var result = await client.CreateItemSlipAsync(SampleRequest(), CancellationToken.None);

        Assert.True(result.TransportSucceeded);
        Assert.False(result.BusinessSucceeded);
        Assert.False(result.CommitUncertain);
        Assert.Equal("E42", result.ErrorCode);
        Assert.Equal("Belge reddedildi", result.ErrorMessage);
    }

    [Fact]
    public async Task Create_hydrates_document_references_from_nested_provider_payload()
    {
        var handler = new QueueHandler(
            _ => Json(HttpStatusCode.OK,
                """{"isSuccessful":true,"data":{"result":{"FATIRS_NO":"VIR00042","BELGE_NO":"IRS-42","KAYIT_NO":4815,"ReferansNo":"REF-42"}}}"""));
        var client = CreateClient(handler, new FakeTokenService());

        var result = await client.CreateItemSlipAsync(SampleRequest(), CancellationToken.None);

        Assert.True(result.BusinessSucceeded);
        Assert.Equal("VIR00042", result.Data?.Data?.FisNo);
        Assert.Equal("IRS-42", result.Data?.Data?.BelgeNo);
        Assert.Equal("4815", result.Data?.Data?.KayitNo);
        Assert.Equal("REF-42", result.Data?.Data?.ReferenceNumber);
    }

    [Fact]
    public async Task Create_omits_empty_projects_and_applies_date_defaults_before_serialization()
    {
        string? payload = null;
        var handler = new QueueHandler(request =>
        {
            payload = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return Json(HttpStatusCode.OK, """{"isSuccessful":true}""");
        });
        var request = SampleRequest();
        request.FatUst.ProjeKodu = " ";
        request.FatUst.Tarih = default;
        request.FatUst.FiiliTarih = default;
        request.Kalems[0].ProjeKodu = null;

        var before = DateTime.Now;
        var result = await CreateClient(handler, new FakeTokenService())
            .CreateItemSlipAsync(request, CancellationToken.None);
        var after = DateTime.Now;

        Assert.True(result.BusinessSucceeded);
        Assert.Null(request.FatUst.ProjeKodu);
        Assert.Null(request.Kalems[0].ProjeKodu);
        Assert.InRange(request.FatUst.Tarih, before, after);
        Assert.InRange(request.FatUst.FiiliTarih, before, after);
        Assert.DoesNotContain("Proje_Kodu", payload);
        Assert.DoesNotContain("ProjeKodu", payload);
        Assert.DoesNotContain("\"Seri\":", payload);
        Assert.DoesNotContain("SIPARIS_TEST", payload);
        Assert.DoesNotContain("YapKod", payload);
    }

    [Fact]
    public async Task Create_preserves_and_serializes_real_project_codes()
    {
        string? payload = null;
        var handler = new QueueHandler(request =>
        {
            payload = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return Json(HttpStatusCode.OK, """{"isSuccessful":true}""");
        });
        var request = SampleRequest();
        request.FatUst.ProjeKodu = " PRJ-01 ";
        request.Kalems[0].ProjeKodu = " PRJ-01 ";

        var result = await CreateClient(handler, new FakeTokenService())
            .CreateItemSlipAsync(request, CancellationToken.None);

        Assert.True(result.BusinessSucceeded);
        Assert.Equal("PRJ-01", request.FatUst.ProjeKodu);
        Assert.Equal("PRJ-01", request.Kalems[0].ProjeKodu);
        Assert.Contains("\"Proje_Kodu\":\"PRJ-01\"", payload);
        Assert.Contains("\"ProjeKodu\":\"PRJ-01\"", payload);
    }

    [Fact]
    public async Task Create_serializes_open_goods_receipt_type_as_netsis_numeric_value_two()
    {
        string? payload = null;
        var handler = new QueueHandler(request =>
        {
            payload = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return Json(HttpStatusCode.OK, """{"isSuccessful":true}""");
        });
        var request = SampleRequest();
        request.FatUst.Tipi = NetsisItemSlipInvoiceType.DomesticOpen;

        var result = await CreateClient(handler, new FakeTokenService())
            .CreateItemSlipAsync(request, CancellationToken.None);

        Assert.True(result.BusinessSucceeded);
        Assert.Contains("\"TIPI\":2", payload);
    }

    [Fact]
    public async Task Create_sends_local_transfer_source_and_destination_with_FatKalem_wire_members()
    {
        string? payload = null;
        var handler = new QueueHandler(requestMessage =>
        {
            payload = requestMessage.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return Json(HttpStatusCode.OK, """{"isSuccessful":true}""");
        });
        var request = SampleRequest();
        request.FaturaTip = NetsisItemSlipDocumentTypes.LocalWarehouseTransfer;
        request.FatUst.Tip = NetsisItemSlipDocumentTypes.LocalWarehouseTransfer;
        request.FatUst.Tipi = NetsisItemSlipInvoiceType.Empty;
        request.FatUst.DepoKodu = 2;
        request.Kalems[0].DepoKodu = 2;
        request.Kalems[0].CikisDepoKodu = 2;
        request.Kalems[0].GirisDepoKodu = 1;

        var result = await CreateClient(handler, new FakeTokenService())
            .CreateItemSlipAsync(request, CancellationToken.None);

        Assert.True(result.BusinessSucceeded);
        Assert.NotNull(payload);
        using var document = JsonDocument.Parse(payload!);
        var line = document.RootElement.GetProperty("Kalems")[0];
        Assert.Equal(2, line.GetProperty("DEPO_KODU").GetInt32());
        Assert.Equal(1, line.GetProperty("Gir_Depo_Kodu").GetInt32());
        Assert.False(line.TryGetProperty("CikisDepoKodu", out _));
        Assert.False(line.TryGetProperty("GirisDepoKodu", out _));
    }

    [Fact]
    public async Task Delete_uses_composite_item_slip_endpoint_and_accepts_empty_no_content()
    {
        HttpMethod? method = null;
        string? path = null;
        var handler = new QueueHandler(request =>
        {
            method = request.Method;
            path = request.RequestUri?.PathAndQuery;
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        var client = CreateClient(handler, new FakeTokenService());

        var result = await client.DeleteItemSlipAsync(
            new NetsisItemSlipDeleteRequest(3, "VIR 00042", "TED/001"),
            CancellationToken.None);

        Assert.True(result.TransportSucceeded);
        Assert.True(result.BusinessSucceeded);
        Assert.False(result.CommitUncertain);
        Assert.Equal(HttpMethod.Delete, method);
        Assert.Equal("/api/v2/ItemSlips/ftAIrs;VIR%2000042;TED%2F001", path);
    }

    [Fact]
    public async Task Delete_unauthorized_response_refreshes_token_once()
    {
        var handler = new QueueHandler(
            _ => new HttpResponseMessage(HttpStatusCode.Unauthorized),
            _ => Json(HttpStatusCode.OK, """{"isSuccessful":true}"""));
        var tokens = new FakeTokenService();
        var client = CreateClient(handler, tokens);

        var result = await client.DeleteItemSlipAsync(
            new NetsisItemSlipDeleteRequest(2, "SIR00092", "MUS001", "9"),
            CancellationToken.None);

        Assert.True(result.BusinessSucceeded);
        Assert.Equal([false, true], tokens.ForceRefreshCalls);
        Assert.Equal(["9", "9"], tokens.BranchCalls);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task Delete_timeout_is_commit_uncertain_and_does_not_report_success()
    {
        var handler = new QueueHandler(_ => throw new TaskCanceledException("timeout"));
        var client = CreateClient(handler, new FakeTokenService());

        var result = await client.DeleteItemSlipAsync(
            new NetsisItemSlipDeleteRequest(3, "AIR00092", "TED001"),
            CancellationToken.None);

        Assert.False(result.BusinessSucceeded);
        Assert.True(result.CommitUncertain);
        Assert.Equal("ERP_DELETE_TIMEOUT_COMMIT_UNCERTAIN", result.ErrorCode);
    }

    [Fact]
    public async Task Delete_not_found_requires_reconciliation()
    {
        var handler = new QueueHandler(
            _ => Json(HttpStatusCode.NotFound, """{"isSuccessful":false,"errorCode":"NOT_FOUND"}"""));
        var client = CreateClient(handler, new FakeTokenService());

        var result = await client.DeleteItemSlipAsync(
            new NetsisItemSlipDeleteRequest(3, "AIR00092", "TED001"),
            CancellationToken.None);

        Assert.False(result.BusinessSucceeded);
        Assert.False(result.CommitUncertain);
        Assert.Equal(HttpStatusCode.NotFound, (HttpStatusCode?)result.HttpStatusCode);
    }

    [Fact]
    public void Warehouse_transfer_delete_allows_empty_customer_segment()
    {
        var providerId = new NetsisItemSlipDeleteRequest(5, "DAT0001", null).ToProviderId();

        Assert.Equal("ftLokalDepo;DAT0001;", providerId);
    }

    private static NetsisRestClient CreateClient(HttpMessageHandler handler, INetsisTokenService tokenService)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://netsis.local") };
        var options = Options.Create(new NetsisOptions
        {
            Enabled = true,
            Rest = new NetsisRestOptions
            {
                BaseUrl = "http://netsis.local",
                ItemSlipsPath = "/api/v2/ItemSlips"
            }
        });
        return new NetsisRestClient(httpClient, tokenService, options, NullLogger<NetsisRestClient>.Instance);
    }

    private static NetsisItemSlipRequest SampleRequest() => new()
    {
        FaturaTip = 3,
        FatUst = new NetsisItemSlipHeader
        {
            CariKod = "C-1",
            FisNo = "GR-1",
            BelgeNo = "IRS-1",
            Tarih = DateTime.UtcNow,
            FiiliTarih = DateTime.UtcNow,
            Tip = 3,
            SubeKodu = 7
        },
        Kalems = [new NetsisItemSlipLine { StokKodu = "S-1", Miktar = 1, DepoKodu = 1 }]
    };

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private sealed class FakeTokenService : INetsisTokenService
    {
        public List<bool> ForceRefreshCalls { get; } = [];
        public List<string?> BranchCalls { get; } = [];
        public Task<string> GetAccessTokenAsync(
            string? branchCode,
            bool forceRefresh,
            CancellationToken cancellationToken)
        {
            BranchCalls.Add(branchCode);
            ForceRefreshCalls.Add(forceRefresh);
            return Task.FromResult(forceRefresh ? "refreshed-token" : "cached-token");
        }
    }

    private sealed class QueueHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] responses)
        : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new(responses);
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            if (_responses.Count == 0) throw new InvalidOperationException("Test yanıtı kalmadı.");
            return Task.FromResult(_responses.Dequeue()(request));
        }
    }
}
