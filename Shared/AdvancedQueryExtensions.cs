using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Shared;

public static class AdvancedQueryExtensions
{
    public const string TurkishCaseInsensitiveSearchCollation = "Turkish_100_CI_AI";

    public static IReadOnlyList<string> TurkishSearchVariants(string term) =>
        BuildStringSearchVariants(term ?? string.Empty, TurkishCaseInsensitiveSearchCollation).ToArray();

    private const int MaximumFilterCount = 20;
    private const int MaximumSearchFieldCount = 12;
    private const int MaximumSearchTermCount = 10;
    private const int MaximumColumnLength = 100;
    private const int MaximumOperatorLength = 30;
    private const int MaximumFilterValueLength = 500;

    public static IQueryable<T> ApplySearch<T>(
        this IQueryable<T> query,
        PagedRequest request,
        IReadOnlyCollection<string>? defaultColumns = null) =>
        query.ApplySearch(request, CreatePublicSearchColumnMapping<T>(), defaultColumns);

    public static IQueryable<T> ApplySearch<T>(
        this IQueryable<T> query,
        PagedRequest request,
        IReadOnlyDictionary<string, string> columnMapping,
        IReadOnlyCollection<string>? defaultColumns = null,
        string? stringCollation = null)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(columnMapping);

        request.MarkSearchApplied();
        var search = request.EffectiveSearch?.Trim();
        if (string.IsNullOrWhiteSpace(search)) return query;
        if (columnMapping.Count == 0)
            throw new InvalidOperationException("En az bir aranabilir kolon tanımlanmalıdır.");

        var requestedColumns = request.SearchFields
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (requestedColumns.Length == 0)
            requestedColumns = (defaultColumns is { Count: > 0 } ? defaultColumns : columnMapping.Keys)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        if (requestedColumns.Length > MaximumSearchFieldCount)
            throw AppException.BadRequest($"En fazla {MaximumSearchFieldCount} arama alanı seçilebilir.");

        var parameter = Expression.Parameter(typeof(T), "x");
        var members = requestedColumns.Select(column =>
        {
            if (column.Length > MaximumColumnLength)
                throw AppException.BadRequest($"Arama alanı en fazla {MaximumColumnLength} karakter olabilir.");

            var path = ResolveColumn(
                column,
                columnMapping,
                message => AppException.BadRequest(message));
            var resolved = ResolvePath(parameter, typeof(T), path)
                ?? throw AppException.BadRequest($"'{column}' aranabilir bir kolon değildir.");
            if (!SupportsGeneralSearch(resolved.member.Type))
                throw AppException.BadRequest($"'{column}' genel aramayı destekleyen bir kolon değildir.");
            return resolved.member;
        }).ToArray();

        var terms = search
            .Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (terms.Length > MaximumSearchTermCount)
            throw AppException.BadRequest($"Arama metni en fazla {MaximumSearchTermCount} kelime içerebilir.");

        Expression? allTerms = null;
        foreach (var term in terms)
        {
            Expression? anyColumn = null;
            foreach (var member in members)
            {
                var current = BuildGeneralSearchMatch(member, term, stringCollation);
                if (current is null) continue;
                anyColumn = anyColumn is null ? current : Expression.OrElse(anyColumn, current);
            }

            anyColumn ??= Expression.Constant(false);
            allTerms = allTerms is null ? anyColumn : Expression.AndAlso(allTerms, anyColumn);
        }

        return allTerms is null
            ? query
            : PagedQueryExtensions.RewriteProjectionMemberAccess(
                query.Where(Expression.Lambda<Func<T, bool>>(allTerms, parameter)));
    }

    private static IReadOnlyDictionary<string, string> CreatePublicSearchColumnMapping<T>() =>
        typeof(T)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property =>
                property.CanRead
                && property.GetIndexParameters().Length == 0
                && property.GetCustomAttribute<JsonIgnoreAttribute>() is null
                && SupportsGeneralSearch(property.PropertyType))
            .ToDictionary(
                property => property.Name,
                property => property.Name,
                StringComparer.OrdinalIgnoreCase);

    private static bool SupportsGeneralSearch(Type propertyType)
    {
        var type = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
        return type == typeof(string)
            || type == typeof(Guid)
            || type == typeof(bool)
            || type.IsEnum
            || IsNumericType(type);
    }

    private static Expression? BuildGeneralSearchMatch(Expression member, string term, string? stringCollation)
    {
        var propertyType = member.Type;
        var underlying = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
        if (underlying == typeof(string))
        {
            var notNull = Expression.NotEqual(member, Expression.Constant(null, typeof(string)));
            var searchTarget = string.IsNullOrWhiteSpace(stringCollation)
                ? member
                : Expression.Call(
                    typeof(RelationalDbFunctionsExtensions),
                    nameof(RelationalDbFunctionsExtensions.Collate),
                    [typeof(string)],
                    Expression.Property(null, typeof(EF), nameof(EF.Functions)),
                    member,
                    Expression.Constant(stringCollation));
            Expression? containsAnyVariant = null;
            foreach (var variant in BuildStringSearchVariants(term, stringCollation))
            {
                var contains = Expression.Call(
                    searchTarget,
                    typeof(string).GetMethod(nameof(string.Contains), [typeof(string)])!,
                    Expression.Constant(variant));
                containsAnyVariant = containsAnyVariant is null
                    ? contains
                    : Expression.OrElse(containsAnyVariant, contains);
            }

            return Expression.AndAlso(notNull, containsAnyVariant ?? Expression.Constant(false));
        }

        if (!TryParseGeneralSearchValue(term, underlying, out var parsed)) return null;

        Expression valueMember = member;
        Expression? hasValue = null;
        if (Nullable.GetUnderlyingType(propertyType) is not null)
        {
            hasValue = Expression.Property(member, "HasValue");
            valueMember = Expression.Property(member, "Value");
        }

        var equals = Expression.Equal(valueMember, Expression.Constant(parsed, underlying));
        return hasValue is null ? equals : Expression.AndAlso(hasValue, equals);
    }

    private static IReadOnlyCollection<string> BuildStringSearchVariants(string term, string? stringCollation)
    {
        var trimmed = term.Trim();
        if (string.IsNullOrWhiteSpace(stringCollation)
            || !string.Equals(stringCollation, TurkishCaseInsensitiveSearchCollation, StringComparison.OrdinalIgnoreCase)
            || !trimmed.Any(IsTurkishIVariant))
            return [trimmed];

        // Turkish collation doğru olarak i/İ ile ı/I çiftlerini birbirinden ayırır.
        // WMS kullanıcıları ERP kodlarında Türkçe ve ASCII klavyeyi karışık kullandığı
        // için iki meşru yazımı da aratırız; tek bir yazıma zorlayıp veri kaçırmayız.
        var dotted = string.Concat(trimmed.Select(character => IsTurkishIVariant(character) ? 'i' : character));
        var dotless = string.Concat(trimmed.Select(character => IsTurkishIVariant(character) ? 'ı' : character));
        return new[] { trimmed, dotted, dotless }
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsTurkishIVariant(char character) =>
        character is 'i' or 'İ' or 'ı' or 'I';

    private static bool TryParseGeneralSearchValue(string term, Type targetType, out object? parsed)
    {
        parsed = null;
        var value = term.Trim();
        if (targetType.IsEnum)
        {
            if (!Enum.TryParse(targetType, value, true, out var enumValue)
                || enumValue is null
                || !Enum.IsDefined(targetType, enumValue))
                return false;
            parsed = enumValue;
            return true;
        }

        if (targetType == typeof(Guid))
        {
            if (!Guid.TryParse(value, out var guid)) return false;
            parsed = guid;
            return true;
        }

        if (targetType == typeof(bool))
        {
            if (bool.TryParse(value, out var boolean)) { parsed = boolean; return true; }
            if (value == "1") { parsed = true; return true; }
            if (value == "0") { parsed = false; return true; }
            return false;
        }

        if (!IsNumericType(targetType)) return false;
        try
        {
            parsed = Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
            return parsed is not null;
        }
        catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException)
        {
            return false;
        }
    }

    private static bool IsNumericType(Type type) =>
        type == typeof(byte)
        || type == typeof(sbyte)
        || type == typeof(short)
        || type == typeof(ushort)
        || type == typeof(int)
        || type == typeof(uint)
        || type == typeof(long)
        || type == typeof(ulong)
        || type == typeof(float)
        || type == typeof(double)
        || type == typeof(decimal);

    public static IQueryable<T> ApplyAdvancedFilters<T>(this IQueryable<T> query, PagedRequest request, IReadOnlyDictionary<string, string>? columnMapping = null)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(request);

        var filters = request.Filters?.ToList() ?? [];
        if (filters.Count > MaximumFilterCount)
            throw AppException.BadRequest($"En fazla {MaximumFilterCount} gelişmiş filtre uygulanabilir.");

        var filterLogic = NormalizeFilterLogic(request.FilterLogic);
        if (filters.Count == 0) return query;

        var parameter = Expression.Parameter(typeof(T), "x");
        Expression? combined = null;
        for (var index = 0; index < filters.Count; index++)
        {
            var filter = filters[index];
            if (string.IsNullOrWhiteSpace(filter.Column))
                throw InvalidFilter(index, "kolon adı zorunludur");
            if (filter.Column.Length > MaximumColumnLength)
                throw InvalidFilter(index, $"kolon adı en fazla {MaximumColumnLength} karakter olabilir");
            if (filter.Operator?.Length > MaximumOperatorLength)
                throw InvalidFilter(index, $"operatör en fazla {MaximumOperatorLength} karakter olabilir");
            if (filter.Value?.Length > MaximumFilterValueLength)
                throw InvalidFilter(index, $"değer en fazla {MaximumFilterValueLength} karakter olabilir");

            var requestedColumn = ResolveColumn(
                filter.Column.Trim(),
                columnMapping,
                message => InvalidFilter(index, message));
            var resolved = ResolvePath(parameter, typeof(T), requestedColumn)
                ?? throw InvalidFilter(index, $"'{filter.Column}' filtrelenebilir bir kolon değildir");

            var expression = BuildFilter(resolved.member, resolved.property.PropertyType, filter, index);
            combined = combined is null
                ? expression
                : filterLogic == FilterLogic.Or
                    ? Expression.OrElse(combined, expression)
                    : Expression.AndAlso(combined, expression);
        }

        return PagedQueryExtensions.RewriteProjectionMemberAccess(
            query.Where(Expression.Lambda<Func<T, bool>>(combined!, parameter)));
    }

    public static IQueryable<T> ApplySort<T>(this IQueryable<T> query, PagedRequest request, string fallbackProperty, IReadOnlyDictionary<string, string>? columnMapping = null)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(request);

        if (request.SortBy?.Length > MaximumColumnLength)
            throw AppException.BadRequest($"Sıralama kolonu en fazla {MaximumColumnLength} karakter olabilir.");

        var descending = ParseSortDirection(request.SortDirection);
        var parameter = Expression.Parameter(typeof(T), "x");
        var requested = string.IsNullOrWhiteSpace(request.SortBy)
            ? fallbackProperty
            : ResolveColumn(
                request.SortBy,
                columnMapping,
                message => AppException.BadRequest(message));
        var resolved = ResolvePath(parameter, typeof(T), requested);
        if (resolved is null)
        {
            if (!string.IsNullOrWhiteSpace(request.SortBy))
                throw AppException.BadRequest($"'{request.SortBy}' sıralanabilir bir kolon değildir.");

            resolved = ResolvePath(parameter, typeof(T), fallbackProperty)
                ?? throw new InvalidOperationException($"Varsayılan sıralama kolonu '{fallbackProperty}', {typeof(T).Name} üzerinde bulunamadı.");
        }

        var lambda = Expression.Lambda(resolved.Value.member, parameter);
        var method = descending ? nameof(Queryable.OrderByDescending) : nameof(Queryable.OrderBy);
        var call = Expression.Call(typeof(Queryable), method, [typeof(T), resolved.Value.member.Type], query.Expression, Expression.Quote(lambda));
        var sorted = query.Provider.CreateQuery<T>(call);

        // Offset pagination must always have a fully unique order. Without the
        // Id tie-breaker, equal sort values can move between pages.
        var id = ResolvePath(parameter, typeof(T), "Id");
        if (id is null || requested.Equals("Id", StringComparison.OrdinalIgnoreCase))
            return PagedQueryExtensions.RewriteProjectionMemberAccess(sorted);

        var idLambda = Expression.Lambda(id.Value.member, parameter);
        var thenMethod = descending ? nameof(Queryable.ThenByDescending) : nameof(Queryable.ThenBy);
        var stableCall = Expression.Call(
            typeof(Queryable),
            thenMethod,
            [typeof(T), id.Value.member.Type],
            sorted.Expression,
            Expression.Quote(idLambda));
        return PagedQueryExtensions.RewriteProjectionMemberAccess(
            sorted.Provider.CreateQuery<T>(stableCall));
    }

    private static string ResolveColumn(
        string column,
        IReadOnlyDictionary<string, string>? mapping,
        Func<string, AppException> invalid)
    {
        if (mapping is null)
        {
            if (column.Contains('.', StringComparison.Ordinal))
                throw invalid($"'{column}' iç içe alan yolu doğrudan kullanılamaz");
            return column;
        }

        var key = mapping.Keys.FirstOrDefault(x => string.Equals(x, column, StringComparison.OrdinalIgnoreCase));
        if (key is null)
            throw invalid($"'{column}' izin verilen kolonlar arasında değildir");
        return mapping[key];
    }

    private static (Expression member, PropertyInfo property)? ResolvePath(Expression root, Type rootType, string path)
    {
        Expression member = root; PropertyInfo? property = null; var type = rootType;
        var segments = path.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length is < 1 or > 4) return null;
        foreach (var segment in segments)
        {
            property = type.GetProperty(segment, BindingFlags.IgnoreCase | BindingFlags.Public | BindingFlags.Instance);
            if (property is null) return null;
            member = Expression.Property(member, property); type = property.PropertyType;
        }
        return property is null ? null : (member, property);
    }

    private static Expression BuildFilter(Expression member, Type propertyType, AdvancedFilterRequest filter, int filterIndex)
    {
        var underlying = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
        var operation = ParseOperator(filter.Operator, filterIndex);

        if (operation is FilterOperation.IsNull or FilterOperation.IsNotNull)
        {
            if (propertyType.IsValueType && Nullable.GetUnderlyingType(propertyType) is null)
                throw InvalidFilter(filterIndex, $"'{filter.Column}' kolonu null karşılaştırmasını desteklemiyor");

            var nullValue = Expression.Constant(null, propertyType);
            return operation == FilterOperation.IsNull
                ? Expression.Equal(member, nullValue)
                : Expression.NotEqual(member, nullValue);
        }

        if (filter.Value is null)
            throw InvalidFilter(filterIndex, $"'{filter.Column}' filtresi için değer zorunludur");

        try
        {
            if (underlying == typeof(string))
            {
                if (operation is not (FilterOperation.Contains or FilterOperation.NotContains or FilterOperation.Equals
                    or FilterOperation.NotEquals or FilterOperation.StartsWith or FilterOperation.EndsWith))
                    throw InvalidFilter(filterIndex, $"'{filter.Operator}' metin kolonunda kullanılamaz");

                var value = Expression.Constant(filter.Value);
                var notNull = Expression.NotEqual(member, Expression.Constant(null, typeof(string)));
                var isNull = Expression.Equal(member, Expression.Constant(null, typeof(string)));
                var methodName = operation switch
                {
                    FilterOperation.Equals or FilterOperation.NotEquals => nameof(string.Equals),
                    FilterOperation.StartsWith => nameof(string.StartsWith),
                    FilterOperation.EndsWith => nameof(string.EndsWith),
                    _ => nameof(string.Contains)
                };
                var stringComparison = Expression.Call(member, typeof(string).GetMethod(methodName, [typeof(string)])!, value);
                return operation switch
                {
                    FilterOperation.NotEquals or FilterOperation.NotContains =>
                        Expression.OrElse(isNull, Expression.Not(stringComparison)),
                    _ => Expression.AndAlso(notNull, stringComparison)
                };
            }

            if (operation is FilterOperation.Contains or FilterOperation.NotContains or FilterOperation.StartsWith or FilterOperation.EndsWith)
                throw InvalidFilter(filterIndex, $"'{filter.Operator}' operatörü {underlying.Name} kolonunda kullanılamaz");

            var converted = ParseValue(filter.Value, underlying, filterIndex, filter.Column);
            var constant = Expression.Constant(converted, underlying);
            Expression valueMember = member; Expression? hasValue = null;
            if (Nullable.GetUnderlyingType(propertyType) is not null) { hasValue = Expression.Property(member, "HasValue"); valueMember = Expression.Property(member, "Value"); }
            var comparison = operation switch
            {
                FilterOperation.NotEquals => Expression.NotEqual(valueMember, constant),
                FilterOperation.GreaterThan => Expression.GreaterThan(valueMember, constant),
                FilterOperation.GreaterThanOrEqual => Expression.GreaterThanOrEqual(valueMember, constant),
                FilterOperation.LessThan => Expression.LessThan(valueMember, constant),
                FilterOperation.LessThanOrEqual => Expression.LessThanOrEqual(valueMember, constant),
                FilterOperation.Equals => Expression.Equal(valueMember, constant),
                _ => throw InvalidFilter(filterIndex, $"'{filter.Operator}' operatörü desteklenmiyor")
            };

            if (hasValue is null) return comparison;
            return operation == FilterOperation.NotEquals
                ? Expression.OrElse(Expression.Not(hasValue), comparison)
                : Expression.AndAlso(hasValue, comparison);
        }
        catch (AppException)
        {
            throw;
        }
        catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException or ArgumentException)
        {
            throw InvalidFilter(filterIndex, $"'{filter.Value}' değeri '{filter.Column}' kolonu için geçersiz");
        }
    }

    private static object ParseValue(string rawValue, Type targetType, int filterIndex, string column)
    {
        var value = rawValue.Trim();
        if (targetType.IsEnum)
        {
            if (!Enum.TryParse(targetType, value, true, out var enumValue) || enumValue is null || !Enum.IsDefined(targetType, enumValue))
                throw InvalidFilter(filterIndex, $"'{rawValue}' değeri '{column}' enum kolonu için geçersiz");
            return enumValue;
        }

        if (targetType == typeof(Guid))
            return Guid.TryParse(value, out var guid) ? guid : throw InvalidFilter(filterIndex, $"'{rawValue}' geçerli bir Guid değildir");
        if (targetType == typeof(DateOnly))
            return DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var dateOnly)
                ? dateOnly : throw InvalidFilter(filterIndex, $"'{rawValue}' geçerli bir ISO tarih değildir");
        if (targetType == typeof(TimeOnly))
            return TimeOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var timeOnly)
                ? timeOnly : throw InvalidFilter(filterIndex, $"'{rawValue}' geçerli bir saat değildir");
        if (targetType == typeof(DateTimeOffset))
            return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.RoundtripKind, out var dateTimeOffset)
                ? dateTimeOffset : throw InvalidFilter(filterIndex, $"'{rawValue}' geçerli bir ISO tarih/saat değildir");
        if (targetType == typeof(DateTime))
            return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.RoundtripKind, out var dateTime)
                ? dateTime : throw InvalidFilter(filterIndex, $"'{rawValue}' geçerli bir ISO tarih/saat değildir");
        if (targetType == typeof(bool))
        {
            if (bool.TryParse(value, out var boolean)) return boolean;
            if (value == "1") return true;
            if (value == "0") return false;
            throw InvalidFilter(filterIndex, $"'{rawValue}' geçerli bir boolean değildir");
        }

        return Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture)
            ?? throw InvalidFilter(filterIndex, $"'{rawValue}' değeri '{column}' kolonu için geçersiz");
    }

    private static FilterOperation ParseOperator(string? value, int filterIndex) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "eq" or "equal" or "equals" or "=" => FilterOperation.Equals,
            "ne" or "neq" or "notequal" or "notequals" or "!=" or "<>" => FilterOperation.NotEquals,
            "contains" => FilterOperation.Contains,
            "notcontains" => FilterOperation.NotContains,
            "startswith" => FilterOperation.StartsWith,
            "endswith" => FilterOperation.EndsWith,
            "gt" or ">" => FilterOperation.GreaterThan,
            "gte" or ">=" => FilterOperation.GreaterThanOrEqual,
            "lt" or "<" => FilterOperation.LessThan,
            "lte" or "<=" => FilterOperation.LessThanOrEqual,
            "isnull" => FilterOperation.IsNull,
            "isnotnull" => FilterOperation.IsNotNull,
            _ => throw InvalidFilter(filterIndex, $"'{value}' filtre operatörü desteklenmiyor")
        };

    private static FilterLogic NormalizeFilterLogic(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            null or "" or "and" => FilterLogic.And,
            "or" => FilterLogic.Or,
            _ => throw AppException.BadRequest("Filtre mantığı yalnızca 'and' veya 'or' olabilir.")
        };

    private static bool ParseSortDirection(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            null or "" or "asc" => false,
            "desc" => true,
            _ => throw AppException.BadRequest("Sıralama yönü yalnızca 'asc' veya 'desc' olabilir.")
        };

    private static AppException InvalidFilter(int index, string message) =>
        AppException.BadRequest($"{index + 1}. gelişmiş filtre geçersiz: {message}.");

    private enum FilterLogic { And, Or }
    private enum FilterOperation
    {
        Equals,
        NotEquals,
        Contains,
        NotContains,
        StartsWith,
        EndsWith,
        GreaterThan,
        GreaterThanOrEqual,
        LessThan,
        LessThanOrEqual,
        IsNull,
        IsNotNull
    }
}
