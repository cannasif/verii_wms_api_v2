using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Serialization;
using verii_wms_api_v2.Modules.Identity.Infrastructure;
using verii_wms_api_v2.Modules.StockBalance.Application;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Exceptions;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class AdvancedQueryExtensionsTests
{
    [Fact]
    public void Enum_filter_is_parsed_case_insensitively()
    {
        var rows = Rows().ApplyAdvancedFilters(Request("Status", "equals", "released")).ToList();

        Assert.Single(rows);
        Assert.Equal(2, rows[0].Id);
    }

    [Fact]
    public void Invalid_enum_value_returns_bad_request()
    {
        var exception = Assert.Throws<AppException>(() =>
            Rows().ApplyAdvancedFilters(Request("Status", "equals", "does-not-exist")).ToList());

        Assert.Equal(400, exception.StatusCode);
        Assert.Contains("enum", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Invalid_column_returns_bad_request_instead_of_being_ignored()
    {
        var exception = Assert.Throws<AppException>(() =>
            Rows().ApplyAdvancedFilters(Request("MissingColumn", "equals", "1")).ToList());

        Assert.Equal(400, exception.StatusCode);
        Assert.Contains("filtrelenebilir", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Text_operator_cannot_be_used_for_number_column()
    {
        var exception = Assert.Throws<AppException>(() =>
            Rows().ApplyAdvancedFilters(Request("Quantity", "contains", "10")).ToList());

        Assert.Equal(400, exception.StatusCode);
        Assert.Contains("kullanılamaz", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Nullable_not_equals_includes_null_values()
    {
        var rows = Rows().ApplyAdvancedFilters(Request("OptionalCode", "notEquals", "5")).ToList();

        Assert.Equal([1, 3], rows.Select(x => x.Id).Order().ToArray());
    }

    [Fact]
    public void Is_null_operator_does_not_require_a_value()
    {
        var request = new PagedRequest
        {
            Filters = [new AdvancedFilterRequest("OptionalCode", "isNull", null)]
        };

        var rows = Rows().ApplyAdvancedFilters(request).ToList();

        Assert.Equal([1, 3], rows.Select(x => x.Id).Order().ToArray());
    }

    [Fact]
    public void Sort_adds_id_as_a_stable_tie_breaker()
    {
        var request = new PagedRequest { SortBy = "SortKey", SortDirection = "asc" };

        var rows = Rows().ApplySort(request, nameof(QueryRow.SortKey)).ToList();

        Assert.Equal([1, 2, 3], rows.Select(x => x.Id).ToArray());
    }

    [Fact]
    public void Invalid_sort_direction_returns_bad_request()
    {
        var request = new PagedRequest { SortBy = "Id", SortDirection = "sideways" };

        var exception = Assert.Throws<AppException>(() => Rows().ApplySort(request, nameof(QueryRow.Id)).ToList());

        Assert.Equal(400, exception.StatusCode);
    }

    [Fact]
    public void Column_mapping_is_an_exact_allow_list()
    {
        var mapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["quantity"] = nameof(QueryRow.Quantity)
        };

        var exception = Assert.Throws<AppException>(() =>
            Rows().ApplyAdvancedFilters(Request("Status", "equals", "Draft"), mapping).ToList());

        Assert.Equal(400, exception.StatusCode);
        Assert.Contains("izin verilen", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Search_uses_default_columns_and_does_not_search_unselected_fields()
    {
        var request = new PagedRequest { Search = "ONLY-PREFIX" };

        var rows = SearchRows()
            .ApplySearch(request, SearchColumns(), ["code", "name"])
            .ToList();

        Assert.Empty(rows);
    }

    [Fact]
    public void User_can_expand_search_to_an_optional_allowed_column()
    {
        var request = new PagedRequest
        {
            Search = "ONLY-PREFIX",
            SearchFields = ["code", "name", "prefix"]
        };

        var rows = SearchRows()
            .ApplySearch(request, SearchColumns(), ["code", "name"])
            .ToList();

        Assert.Single(rows);
        Assert.Equal(1, rows[0].Id);
    }

    [Fact]
    public void User_can_reduce_search_to_one_allowed_column()
    {
        var request = new PagedRequest
        {
            Search = "PRODUCTION",
            SearchFields = ["code"]
        };

        var rows = SearchRows()
            .ApplySearch(request, SearchColumns(), ["code", "name"])
            .ToList();

        Assert.Empty(rows);
    }

    [Fact]
    public void Search_terms_can_match_different_selected_columns()
    {
        var request = new PagedRequest
        {
            Search = "UR PRODUCTION",
            SearchFields = ["code", "name"]
        };

        var rows = SearchRows()
            .ApplySearch(request, SearchColumns(), ["code", "name"])
            .ToList();

        Assert.Single(rows);
        Assert.Equal(1, rows[0].Id);
    }

    [Fact]
    public void Sql_search_leaves_selected_text_column_unwrapped()
    {
        using var db = SqlServerContext();
        var query = db.Customers.Select(x => new CustomerSearchRow(x.Id, x.CustomerCode, x.CustomerName));
        var request = new PagedRequest
        {
            Search = "sabit",
            SearchFields = ["name"]
        };
        var columns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["code"] = nameof(CustomerSearchRow.Code),
            ["name"] = nameof(CustomerSearchRow.Name)
        };

        var sql = query.ApplySearch(
                request,
                columns,
                ["code", "name"])
            .ToQueryString();

        Assert.Contains("LIKE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("COLLATE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("REPLACE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("LOWER", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CustomerCode] LIKE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Turkish_search_uses_one_linear_pattern_for_mixed_i_variants()
    {
        using var db = SqlServerContext();
        var query = db.Customers.Select(x => new CustomerSearchRow(x.Id, x.CustomerCode, x.CustomerName));
        var request = new PagedRequest
        {
            Search = "alisveris",
            SearchFields = ["name"]
        };
        var columns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["code"] = nameof(CustomerSearchRow.Code),
            ["name"] = nameof(CustomerSearchRow.Name)
        };

        var sql = query.ApplySearch(
                request,
                columns,
                ["code", "name"])
            .ToQueryString();

        Assert.Equal(2, CountOccurrences(sql, "[iıî]"));
        Assert.Equal(2, CountOccurrences(sql, "[sş]"));
        Assert.Equal(1, CountOccurrences(sql, " LIKE "));
        Assert.DoesNotContain(" OR ", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("COLLATE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("REPLACE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("LOWER", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("cagri", "Çağrı")]
    [InlineData("gorus", "GÖRÜŞ")]
    [InlineData("isik", "ışık")]
    [InlineData("SUBE", "şube")]
    [InlineData("alisveris", "ALIŞVERİŞ")]
    [InlineData("kar", "kâr")]
    [InlineData("sukunet", "sükûnet")]
    public void Ascii_turkish_client_fallback_matches_equivalent_text(string search, string stored)
    {
        var request = new PagedRequest { Search = search, SearchFields = ["name"] };
        var rows = new[] { new SearchRow(1, "TR-1", stored, "") }.AsQueryable()
            .ApplySearch(request, SearchColumns(), ["name"])
            .ToList();

        Assert.Single(rows);
    }

    [Theory]
    [InlineData("İşlemci", "TSMC A19 Islemci Çip")]
    [InlineData("Monitor", "DELL UltraSharp U2723QE 27\" 4K IPS Monitör")]
    [InlineData("Endustriyel", "Endüstriyel Otomasyon Sistemleri Ltd. Sti.")]
    [InlineData("Sertunc", "Sertunç direkçi")]
    [InlineData("direkci", "Sertunç direkçi")]
    [InlineData("Oner", "Öner kaya")]
    [InlineData("Gokce", "Gülçin Gökçe")]
    [InlineData("Ozcimen", "Dilara Özçimen")]
    [InlineData("Yurt-Ici", "Yurt-Içi Lojistik ve Gümrükleme Hizmetleri")]
    [InlineData("İçin", "Bu Cari Uzun Isimli Carilerin Testi Için Kullanilacaktir")]
    public void Production_stock_and_customer_names_match_ascii_turkish_frontend_input(
        string search,
        string stored)
    {
        var request = new PagedRequest { Search = search, SearchFields = ["name"] };
        var rows = new[] { new SearchRow(1, "REAL-DATA", stored, "") }.AsQueryable()
            .ApplySearch(request, SearchColumns(), ["name"])
            .ToList();

        Assert.Single(rows);
    }

    [Fact]
    public void Frontend_paged_payload_preserves_selected_field_and_turkish_pattern()
    {
        const string payload = """
            {"pageNumber":1,"pageSize":20,"search":"cagri alisveris","searchFields":["name"]}
            """;
        var request = JsonSerializer.Deserialize<PagedRequest>(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(request);

        using var db = SqlServerContext();
        var query = db.Customers.Select(x => new CustomerSearchRow(x.Id, x.CustomerCode, x.CustomerName));
        var sql = query.ApplySearch(request, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["code"] = nameof(CustomerSearchRow.Code),
                ["name"] = nameof(CustomerSearchRow.Name)
            }, ["code", "name"])
            .ToQueryString();

        Assert.Contains("CustomerName", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CustomerCode] LIKE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[cç]", sql, StringComparison.Ordinal);
        Assert.Contains("[iıî]", sql, StringComparison.Ordinal);
        Assert.Contains(" AND ", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sql_like_control_characters_are_literal_and_escaped_once()
    {
        var pattern = AsciiTurkishSearch.BuildContainsPattern(@"100%_[]^\");

        Assert.Equal(@"%100\%\_\[\]\^\\%", pattern);
        Assert.True(AsciiTurkishSearch.Contains(@"REF-100%_[]^\-OK", @"100%_[]^\"));
    }

    [Fact]
    public void Search_rejects_a_field_outside_the_allow_list()
    {
        var request = new PagedRequest
        {
            Search = "UR",
            SearchFields = ["description"]
        };

        var exception = Assert.Throws<AppException>(() =>
            SearchRows().ApplySearch(request, SearchColumns(), ["code", "name"]).ToList());

        Assert.Equal(400, exception.StatusCode);
        Assert.Contains("izin verilen", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Automatic_search_allow_list_uses_only_public_serialized_scalar_fields()
    {
        var request = new PagedRequest
        {
            Search = "UR-001",
            SearchFields = ["code"]
        };

        var rows = SearchRows().ApplySearch(request).ToList();

        Assert.Single(rows);
        Assert.Equal(1, rows[0].Id);
    }

    [Fact]
    public void Automatic_search_allow_list_rejects_json_ignored_fields()
    {
        var request = new PagedRequest
        {
            Search = "internal",
            SearchFields = ["internalSearchText"]
        };
        var rows = new[]
        {
            new AutoSearchRow { Id = 1, Code = "PUBLIC", InternalSearchText = "internal" }
        }.AsQueryable();

        var exception = Assert.Throws<AppException>(() => rows.ApplySearch(request).ToList());

        Assert.Equal(400, exception.StatusCode);
        Assert.Contains("izin verilen", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Explicit_search_scope_keeps_the_public_search_contract_raw()
    {
        var request = new PagedRequest
        {
            Search = "warehouse",
            SearchFields = ["code"]
        };

        Assert.Equal("warehouse", request.Search);
        Assert.Equal("warehouse", request.EffectiveSearch);
        Assert.Null(request.LegacySearch);
        Assert.True(request.HasExplicitSearchFields);
    }

    [Fact]
    public void Search_without_explicit_scope_remains_available_to_legacy_paged_queries()
    {
        var request = new PagedRequest { Search = "  depo  " };

        Assert.Equal("  depo  ", request.LegacySearch);
    }

    [Fact]
    public void Search_supports_exact_numeric_record_id()
    {
        var request = new PagedRequest
        {
            Search = "2",
            SearchFields = ["id"]
        };

        var rows = SearchRows()
            .ApplySearch(request, SearchColumns(), ["code", "name"])
            .ToList();

        Assert.Single(rows);
        Assert.Equal(2, rows[0].Id);
    }

    [Fact]
    public void Non_numeric_text_returns_no_result_when_only_numeric_field_is_selected()
    {
        var request = new PagedRequest
        {
            Search = "NOT-AN-ID",
            SearchFields = ["id"]
        };

        var rows = SearchRows()
            .ApplySearch(request, SearchColumns(), ["code", "name"])
            .ToList();

        Assert.Empty(rows);
    }

    [Fact]
    public void Nested_path_requires_an_explicit_mapping()
    {
        var exception = Assert.Throws<AppException>(() =>
            Rows().ApplyAdvancedFilters(Request("Status.Name", "equals", "Draft")).ToList());

        Assert.Equal(400, exception.StatusCode);
        Assert.Contains("doğrudan kullanılamaz", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Oversized_filter_value_is_rejected()
    {
        var exception = Assert.Throws<AppException>(() =>
            Rows().ApplyAdvancedFilters(Request("OptionalCode", "equals", new string('1', 501))).ToList());

        Assert.Equal(400, exception.StatusCode);
        Assert.Contains("500", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Joined_grid_columns_translate_to_sql_after_projection()
    {
        using var db = SqlServerContext();
        var joined =
            from balance in db.LocationStockBalances
            join warehouse in db.Warehouses on balance.WarehouseId equals warehouse.Id
            join location in db.Locations on balance.LocationId equals location.Id
            join stock in db.Stocks on balance.StockId equals stock.Id
            select new { Balance = balance, Warehouse = warehouse, Location = location, Stock = stock };
        var query = joined.Select(x => new LocationBalanceRow(
            x.Balance.Id, x.Balance.BranchCode, x.Warehouse.Id, x.Warehouse.WarehouseCode, x.Warehouse.WarehouseName,
            x.Location.Id, x.Location.Code, x.Location.Name, x.Stock.Id, x.Stock.ErpStockCode, x.Stock.StockName,
            x.Balance.YapCodeId, null, x.Balance.UnitCode, x.Balance.LotNo, x.Balance.SerialNo, x.Balance.StockStatus,
            x.Balance.Quantity, x.Balance.ReservedQuantity, x.Balance.AvailableQuantity, x.Balance.LastMovementEntryId,
            x.Balance.LastTransactionDate, x.Balance.CreatedBy, x.Balance.CreatedDate, x.Balance.UpdatedBy, x.Balance.UpdatedDate));
        var request = new PagedRequest
        {
            SortBy = nameof(LocationBalanceRow.StockCode),
            Filters = [new AdvancedFilterRequest(nameof(LocationBalanceRow.WarehouseName), "startsWith", "A")]
        };

        var filtered = query.ApplyAdvancedFilters(request);
        var sql = filtered.ApplySort(request, nameof(LocationBalanceRow.LastTransactionDate))
            .ToQueryString();

        Assert.Contains("ORDER BY", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LIKE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Enum_filter_translates_to_sql_server_query()
    {
        using var db = SqlServerContext();
        var status = db.GoodsReceiptHeaders.Select(x => new GoodsReceiptStatusRow(x.Id, x.Status));
        var request = new PagedRequest
        {
            Filters = [new AdvancedFilterRequest(nameof(GoodsReceiptStatusRow.Status), "equals", "Draft")]
        };

        var sql = status.ApplyAdvancedFilters(request)
            .ApplySort(request, nameof(GoodsReceiptStatusRow.Id))
            .ToQueryString();

        Assert.Contains("WHERE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ORDER BY", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Left_join_grid_projection_translates_after_filter_and_sort()
    {
        using var db = SqlServerContext();
        var locations = db.Locations;
        var query =
            from location in locations
            join warehouse in db.Warehouses on location.WarehouseId equals warehouse.Id
            join parentLocation in locations on location.ParentLocationId equals parentLocation.Id into parents
            from parent in parents.DefaultIfEmpty()
            select new LocationGridProjection
            {
                Id = location.Id,
                WarehouseName = warehouse.WarehouseName,
                Code = location.Code,
                ParentCode = parent == null ? null : parent.Code
            };
        var request = new PagedRequest
        {
            SortBy = nameof(LocationGridProjection.ParentCode),
            Filters = [new AdvancedFilterRequest(nameof(LocationGridProjection.WarehouseName), "startsWith", "A")]
        };

        var sql = query.ApplyAdvancedFilters(request)
            .ApplySort(request, nameof(LocationGridProjection.Code))
            .ToQueryString();

        Assert.Contains("LEFT JOIN", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ORDER BY", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Stringified_enum_grid_filter_translates_to_sql_server_query()
    {
        using var db = SqlServerContext();
        var rows = db.GoodsReceiptHeaders.Select(x => new StringStatusRow(x.Id, x.Status.ToString()));
        var request = new PagedRequest
        {
            Filters = [new AdvancedFilterRequest(nameof(StringStatusRow.Status), "equals", "Draft")]
        };

        var sql = rows.ApplyAdvancedFilters(request)
            .ApplySort(request, nameof(StringStatusRow.Id))
            .ToQueryString();

        Assert.Contains("WHERE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Paging_accepts_a_lean_count_query_with_a_different_projection()
    {
        var options = new DbContextOptionsBuilder<WmsDbContext>()
            .UseInMemoryDatabase($"paged-count-{Guid.NewGuid():N}")
            .Options;
        await using var db = new WmsDbContext(options);
        var items = db.Warehouses.Select(x => new SearchRow(
            x.Id,
            x.WarehouseCode.ToString(),
            x.WarehouseName,
            x.BranchCode));
        var count = db.Warehouses.Select(x => x.Id);

        var page = await items.ToPagedResponseAsync(
            count,
            new PagedRequest { PageNumber = 1, PageSize = 20 });

        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
    }

    private static PagedRequest Request(string column, string operation, string? value) => new()
    {
        Filters = [new AdvancedFilterRequest(column, operation, value)]
    };

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var offset = 0;
        while ((offset = source.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }

        return count;
    }

    private static IQueryable<QueryRow> Rows() => new[]
    {
        new QueryRow(3, 1, QueryStatus.Draft, 30, null),
        new QueryRow(1, 1, QueryStatus.Draft, 10, null),
        new QueryRow(2, 1, QueryStatus.Released, 20, 5)
    }.AsQueryable();

    private static IQueryable<SearchRow> SearchRows() => new[]
    {
        new SearchRow(1, "UR-001", "PRODUCTION ORDER", "ONLY-PREFIX"),
        new SearchRow(2, "GR-001", "GOODS RECEIPT", "MK")
    }.AsQueryable();

    private static IReadOnlyDictionary<string, string> SearchColumns() =>
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = nameof(SearchRow.Id),
            ["code"] = nameof(SearchRow.Code),
            ["name"] = nameof(SearchRow.Name),
            ["prefix"] = nameof(SearchRow.Prefix)
        };

    private static WmsDbContext SqlServerContext()
    {
        var options = new DbContextOptionsBuilder<WmsDbContext>()
            .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=QueryTranslationOnly;Trusted_Connection=True;")
            .Options;
        return new WmsDbContext(options);
    }

    private sealed record QueryRow(long Id, int SortKey, QueryStatus Status, decimal Quantity, int? OptionalCode);
    private sealed record SearchRow(long Id, string Code, string Name, string Prefix);
    private sealed record CustomerSearchRow(long Id, string Code, string Name);
    private sealed class AutoSearchRow
    {
        public long Id { get; init; }
        public string Code { get; init; } = string.Empty;
        [JsonIgnore] public string InternalSearchText { get; init; } = string.Empty;
    }
    private sealed record GoodsReceiptStatusRow(long Id, verii_wms_api_v2.Modules.WarehouseOperations.Domain.WarehouseOperationStatus Status);
    private sealed record StringStatusRow(long Id, string Status);
    private sealed class LocationGridProjection
    {
        public long Id { get; init; }
        public string WarehouseName { get; init; } = string.Empty;
        public string Code { get; init; } = string.Empty;
        public string? ParentCode { get; init; }
    }
    private enum QueryStatus { Draft, Released }
}
