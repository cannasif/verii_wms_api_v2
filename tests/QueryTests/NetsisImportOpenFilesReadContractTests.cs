using System.Text.Json;
using Microsoft.Data.SqlClient;
using verii_wms_api_v2.Modules.NetsisRead.Application;
using verii_wms_api_v2.Modules.NetsisRead.Application.Dtos;
using verii_wms_api_v2.Modules.NetsisRead.Infrastructure;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class NetsisImportOpenFilesReadContractTests
{
    [Fact]
    public async Task Read_uses_the_function_with_an_explicit_stable_column_contract()
    {
        var executor = new CapturingNetsisQueryExecutor();
        var service = new NetsisReadService(executor);

        var result = await service.GetImportOpenFilesAsync(CancellationToken.None);

        Assert.Empty(result);
        var query = Assert.Single(executor.Queries);
        Assert.Equal("RII_FN_ITHALAT_ACIK_DOSYALAR", query.Operation);
        Assert.Contains(
            "SELECT DOSYANO, CARI_KOD, CARI_ISIM, TESLIM_CARI_KOD, TESLIM_CARI_ISIM",
            query.Sql,
            StringComparison.Ordinal);
        Assert.Contains("FROM dbo.RII_FN_ITHALAT_ACIK_DOSYALAR()", query.Sql, StringComparison.Ordinal);
        Assert.Contains("ORDER BY DOSYANO", query.Sql, StringComparison.Ordinal);
        Assert.DoesNotContain("SELECT *", query.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(query.Parameters);
    }

    [Fact]
    public void Dto_preserves_the_function_nullability_contract()
    {
        var row = new NetsisImportOpenFileDto("ITH000000000001", "150.001", null, null, null);

        var json = JsonSerializer.Serialize(row, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("\"fileNumber\":\"ITH000000000001\"", json, StringComparison.Ordinal);
        Assert.Contains("\"customerCode\":\"150.001\"", json, StringComparison.Ordinal);
        Assert.Contains("\"customerName\":null", json, StringComparison.Ordinal);
        Assert.Contains("\"deliveryCustomerCode\":null", json, StringComparison.Ordinal);
        Assert.Contains("\"deliveryCustomerName\":null", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Open_file_lookup_is_trimmed_and_case_insensitive()
    {
        NetsisImportOpenFileDto[] openFiles =
        [
            new("ITH-2026-ABC", "320.001", "Tedarikçi", null, null)
        ];

        var result = NetsisImportOpenFilePolicy.FindOpenFile("  ith-2026-abc  ", openFiles);

        Assert.Same(openFiles[0], result);
    }

    [Fact]
    public void Open_file_lookup_rejects_an_invalid_file_number_contract()
    {
        Assert.Throws<ArgumentException>(() =>
            NetsisImportOpenFilePolicy.FindOpenFile("   ", []));
        Assert.Throws<ArgumentException>(() =>
            NetsisImportOpenFilePolicy.FindOpenFile(new string('X', 21), []));
    }

    [Fact]
    public async Task Import_orders_use_the_customer_code_from_the_open_file()
    {
        var executor = new CapturingNetsisQueryExecutor();
        executor.ImportFiles.Add(new(
            "ITH-2026-007", "320.777", "Dosya Carisi", null, null));
        var service = new NetsisReadService(executor);

        var result = await service.GetGoodsReceiptImportOpenOrdersAsync(
            "ith-2026-007", "0", includeUnavailable: false, CancellationToken.None);

        Assert.Equal("320.777", result.ImportFile.CustomerCode);
        var lineQuery = Assert.Single(executor.Queries, query =>
            query.Operation == "RII_FN_GR_OPENORDERS_LINE");
        var customerParameter = Assert.Single(lineQuery.Parameters, parameter =>
            parameter.ParameterName == "@customerCode");
        Assert.Equal("320.777", customerParameter.Value);
    }

    [Fact]
    public async Task Import_orders_reject_a_file_that_is_no_longer_open()
    {
        var service = new NetsisReadService(new CapturingNetsisQueryExecutor());

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.GetGoodsReceiptImportOpenOrdersAsync(
                "ITH-CLOSED", "0", includeUnavailable: false, CancellationToken.None));

        Assert.Contains("artık açık değildir", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Installer_contains_the_cross_database_function_definition()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
        var sql = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Modules",
            "NetsisRead",
            "Infrastructure",
            "Sql",
            "InstallNetsisReadFunctions.sql"));

        Assert.Contains("CREATE OR ALTER FUNCTION [dbo].[RII_FN_ITHALAT_ACIK_DOSYALAR]()", sql, StringComparison.Ordinal);
        Assert.Contains("FROM V3RIICO..TBLITHDOSYAMAS AS X", sql, StringComparison.Ordinal);
        Assert.Contains("X.CARI_KOD2 AS TESLIM_CARI_KOD", sql, StringComparison.Ordinal);
        Assert.Contains("Z.CARI_ISIM AS TESLIM_CARI_ISIM", sql, StringComparison.Ordinal);
        Assert.Contains("WHERE X.KAPALI = 'H'", sql, StringComparison.Ordinal);
    }

    private sealed record CapturedQuery(
        string Operation,
        string Sql,
        IReadOnlyList<SqlParameter> Parameters);

    private sealed class CapturingNetsisQueryExecutor : INetsisQueryExecutor
    {
        public List<CapturedQuery> Queries { get; } = [];
        public List<NetsisImportOpenFileDto> ImportFiles { get; } = [];

        public Task<List<T>> QueryAsync<T>(
            string operation,
            string sql,
            Func<SqlDataReader, T> map,
            CancellationToken cancellationToken,
            params SqlParameter[] parameters)
        {
            Queries.Add(new CapturedQuery(operation, sql, parameters));
            if (typeof(T) == typeof(NetsisImportOpenFileDto))
                return Task.FromResult(ImportFiles.Cast<T>().ToList());
            return Task.FromResult(new List<T>());
        }
    }
}
