using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using verii_wms_api_v2.Modules.WarehouseAssistant.Application;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class InProcessWarehouseAssistantTests
{
    [Fact]
    public void Routing_requires_no_external_or_local_model_service()
    {
        var routing = CreateResolver().GetRoutingInfo();

        Assert.Equal("InProcessNlp", routing.RoutingMode);
        Assert.False(routing.SemanticRoutingAvailable);
        Assert.Null(routing.SemanticModel);
    }

    [Theory]
    [InlineData("Abi şu 01/013 vardı ya, mağazada falan nerede duruyor ve elimizde kaç tane kalmış?", WarehouseAssistantIntent.StockLocationBalance)]
    [InlineData("ASD'den dün ne gelmiş, ne kadar almışız bir bakabilir misin?", WarehouseAssistantIntent.GoodsReceiptAnalysis)]
    [InlineData("Şey bu DTG-1'in başından beri nerelere gittiğini görebiliyor muyuz?", WarehouseAssistantIntent.Traceability)]
    [InlineData("34 ABC 124 dünkü tırın sac kabulü var mı?", WarehouseAssistantIntent.SteelVehicleAnalysis)]
    [InlineData("Üretime yolladığımız malzemelerden yarım kalan olmuş mu?", WarehouseAssistantIntent.WarehouseTransferAnalysis)]
    [InlineData("Fazla kabul ayarını açarsam süreçte ne değişiyor?", WarehouseAssistantIntent.ParameterHelp)]
    public async Task Understands_conversational_warehouse_phrasing_without_a_model(
        string question,
        WarehouseAssistantIntent expected)
    {
        var result = await CreateResolver().ResolveAsync(question, null);

        Assert.Equal(expected, result.Intent);
        Assert.StartsWith("local-inprocess", result.ProviderMode, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Correction_replaces_serial_context_with_the_requested_stock()
    {
        var context = new WarehouseAssistantContext(
            "DTG-1",
            13,
            "01/013",
            LastIntent: WarehouseAssistantIntent.SerialBalance,
            LastResolvedQuestion: "DTG-1 serisi nerede?");

        var result = await CreateResolver().ResolveAsync(
            "Hayır seri değil; demek istediğim 01/013 malzemesi nerede ve ne kadar var?",
            context);

        Assert.Equal(WarehouseAssistantIntent.StockLocationBalance, result.Intent);
        Assert.Null(result.SerialNo);
        Assert.Contains("01/013", result.StockQuery, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("local-inprocess-conversation-v2.8", result.ProviderMode);
    }

    [Fact]
    public async Task Correction_uses_the_new_plate_instead_of_previous_context()
    {
        var context = new WarehouseAssistantContext(
            null,
            null,
            null,
            VehiclePlate: "34 ABC 123",
            LastIntent: WarehouseAssistantIntent.SteelVehicleAnalysis);

        var result = await CreateResolver().ResolveAsync(
            "Yok plaka yanlış, 34 ABC 124 olacaktı; dünkü sac girişine bak.",
            context);

        Assert.Equal(WarehouseAssistantIntent.SteelVehicleAnalysis, result.Intent);
        Assert.Equal("34 ABC 124", result.VehiclePlateQuery);
        Assert.Equal(WarehouseAssistantDatePreset.Yesterday, result.DatePreset);
    }

    [Fact]
    public async Task Date_correction_uses_the_positive_date_phrase()
    {
        var result = await CreateResolver().ResolveAsync(
            "Dün değil, bugün yaptığım işlemleri göster.",
            null);

        Assert.Equal(WarehouseAssistantIntent.MyActivities, result.Intent);
        Assert.Equal(WarehouseAssistantDatePreset.Today, result.DatePreset);
        Assert.True(result.HasExplicitDateFilter);
    }

    [Fact]
    public async Task One_sentence_can_produce_two_independent_read_only_queries()
    {
        var result = await CreateResolver().ResolveAsync(
            "Bugün yaptığım işlemleri ve bana atanmış açık emirleri getir.",
            null);

        Assert.Equal(WarehouseAssistantIntent.MyActivities, result.Intent);
        var additional = Assert.Single(result.AdditionalQueries!);
        Assert.Equal(WarehouseAssistantIntent.AssignedTasks, additional.Intent);
        Assert.Equal("local-inprocess-compound-v2.8", result.ProviderMode);
    }

    [Fact]
    public async Task Elliptical_second_clause_reuses_the_stock_subject()
    {
        var result = await CreateResolver().ResolveAsync(
            "01/013 malzemesi nerede ve hareketlerini ayrı ayrı göster.",
            null);

        Assert.Equal(WarehouseAssistantIntent.StockLocationBalance, result.Intent);
        var additional = Assert.Single(result.AdditionalQueries!);
        Assert.Equal(WarehouseAssistantIntent.StockMovementHistory, additional.Intent);
        Assert.Contains("01/013", additional.StockQuery, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Elliptical_second_clause_preserves_a_serial_subject()
    {
        var result = await CreateResolver().ResolveAsync(
            "DTG-1 serisi nerede ve hareketlerini de göster.",
            null);

        Assert.Equal(WarehouseAssistantIntent.SerialBalance, result.Intent);
        var additional = Assert.Single(result.AdditionalQueries!);
        Assert.Equal(WarehouseAssistantIntent.StockMovementHistory, additional.Intent);
        Assert.Equal("DTG-1", additional.SerialNo);
    }

    [Fact]
    public async Task Accented_conversation_connector_creates_a_compound_plan()
    {
        var result = await CreateResolver().ResolveAsync(
            "Bugün yaptığım işlemleri göster; ayrıca bana atanmış açık emirleri getir.",
            null);

        Assert.Equal(WarehouseAssistantIntent.MyActivities, result.Intent);
        Assert.Equal(WarehouseAssistantIntent.AssignedTasks, Assert.Single(result.AdditionalQueries!).Intent);
    }

    [Fact]
    public async Task Follow_up_reuses_only_the_validated_conversation_subject()
    {
        var context = new WarehouseAssistantContext(
            null,
            13,
            "01/013",
            LastIntent: WarehouseAssistantIntent.StockLocationBalance,
            LastResolvedQuestion: "01/013 nerede?");

        var result = await CreateResolver().ResolveAsync("Peki hareketlerinde dün ne olmuş?", context);

        Assert.Equal(WarehouseAssistantIntent.StockMovementHistory, result.Intent);
        Assert.Contains("01/013", result.StockQuery, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(WarehouseAssistantDatePreset.Yesterday, result.DatePreset);
    }

    [Theory]
    [InlineData("WT-2026-001 transferini onayla")]
    [InlineData("01/013 stok miktarını düzelt")]
    [InlineData("Bu irsaliyeyi ERP'ye gönder")]
    [InlineData("Ahmet'e yeni toplama emri ata")]
    public async Task Read_only_policy_rejects_mutation_requests_before_planning(string question)
    {
        var result = await CreateResolver().ResolveAsync(question, null);

        Assert.Equal(WarehouseAssistantIntent.Unknown, result.Intent);
        Assert.Equal("local-policy-write-rejected-v2.8", result.ProviderMode);
    }

    [Fact]
    public async Task Planner_is_fast_and_does_not_have_model_warmup_cost()
    {
        var resolver = CreateResolver();
        var questions = new[]
        {
            "01/013 malzemesi nerede ve ne kadar var?",
            "Bugün yaptığım işlemleri göster.",
            "Bana atanmış açık emirleri getir.",
            "ASD'den geçen hafta neler gelmiş?",
            "DTG-1 hangi işlemlerden geçmiş?"
        };
        var stopwatch = Stopwatch.StartNew();
        for (var index = 0; index < 1_000; index++)
            await resolver.ResolveAsync(questions[index % questions.Length], null);
        stopwatch.Stop();

        // Keep this deliberately generous for shared CI agents running the full suite in parallel.
        // A local model/network implementation would exceed this budget by orders of magnitude.
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(15), $"Elapsed: {stopwatch.Elapsed}");
    }

    private static LocalHybridWarehouseAssistantIntentResolver CreateResolver() => new(
        new WarehouseAssistantIntentResolver(),
        Options.Create(new WarehouseAssistantOptions()),
        NullLogger<LocalHybridWarehouseAssistantIntentResolver>.Instance);
}
