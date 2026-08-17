using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Shared;

public static class PagedQueryExtensions
{
    public const int DefaultPageSize = 10;
    public const int DefaultMaxPageSize = 500;
    public const int DefaultMaxSearchLength = 200;

    public static async Task<PagedResponse<T>> ToPagedResponseAsync<T>(
        this IQueryable<T> query,
        PagedRequest? request,
        CancellationToken cancellationToken = default,
        int maxPageSize = DefaultMaxPageSize)
    {
        request ??= new PagedRequest();
        ValidateRequest(request, maxPageSize);

        // Yeni grid/dropdown sözleşmesi arama alanlarını açıkça gönderir.
        // Modül özel bir eşleme ile aramayı daha önce uygulamadıysa yalnızca
        // dışarı açılan DTO alanlarından üretilen güvenli allowlist kullanılır.
        if (request.HasExplicitSearchFields && !request.SearchApplied)
            query = query.ApplySearch(request);

        // Positional record DTO projeksiyonlarında EF, `new Dto(...).Property`
        // ifadesini özellikle OrderBy içinde SQL'e çeviremeyebilir. Üye erişimini
        // constructor'ın gerçek argümanına indirger; COUNT ve sayfalama sunucuda kalır.
        query = RewriteProjectionMemberAccess(query);

        return await ExecutePagedQueryAsync(
            query, query, request, cancellationToken, maxPageSize).ConfigureAwait(false);
    }

    internal static async Task<PagedResponse<T>> ToPagedResponseAsync<T, TCount>(
        this IQueryable<T> query,
        IQueryable<TCount> countQuery,
        PagedRequest request,
        CancellationToken cancellationToken = default,
        int maxPageSize = DefaultMaxPageSize)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(countQuery);
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request, maxPageSize);
        if (request.HasExplicitSearchFields && !request.SearchApplied)
            throw new InvalidOperationException("Ayrı count sorgusunda genel arama her iki sorguya da önceden uygulanmalıdır.");

        return await ExecutePagedQueryAsync(
            RewriteProjectionMemberAccess(query),
            RewriteProjectionMemberAccess(countQuery),
            request,
            cancellationToken,
            maxPageSize).ConfigureAwait(false);
    }

    private static async Task<PagedResponse<T>> ExecutePagedQueryAsync<T, TCount>(
        IQueryable<T> query,
        IQueryable<TCount> countQuery,
        PagedRequest request,
        CancellationToken cancellationToken,
        int maxPageSize)
    {
        var pageNumber = NormalizePageNumber(request.EffectivePageNumber);
        var pageSize = NormalizePageSize(request.PageSize, maxPageSize);
        var skipLong = (long)(pageNumber - 1) * pageSize;
        var skip = skipLong > int.MaxValue ? int.MaxValue : (int)skipLong;

        var totalCount = await countQuery.CountAsync(cancellationToken).ConfigureAwait(false);
        var items = await query
            .Skip(skip)
            .Take(pageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new PagedResponse<T>
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = pageNumber,
            PageSize = pageSize
        };
    }

    public static int NormalizePageNumber(int pageNumber) => pageNumber < 1 ? 1 : pageNumber;

    public static int NormalizePageSize(int pageSize, int maxPageSize = DefaultMaxPageSize)
    {
        var normalized = pageSize < 1 ? DefaultPageSize : pageSize;
        return maxPageSize < 1 ? normalized : Math.Min(normalized, maxPageSize);
    }

    private static void ValidateRequest(PagedRequest request, int maxPageSize)
    {
        if (request.EffectiveSearch?.Length > DefaultMaxSearchLength)
            throw AppException.BadRequest($"Arama metni en fazla {DefaultMaxSearchLength} karakter olabilir.");
        if (maxPageSize > 0 && request.PageSize > maxPageSize)
            throw AppException.BadRequest($"Sayfa boyutu en fazla {maxPageSize} olabilir.");
        if (request.EffectivePageNumber > int.MaxValue / Math.Max(1, NormalizePageSize(request.PageSize, maxPageSize)))
            throw AppException.BadRequest("İstenen sayfa numarası desteklenen sınırı aşıyor.");
    }

    internal static IQueryable<T> RewriteProjectionMemberAccess<T>(IQueryable<T> query)
    {
        var expression = new ProjectionMemberAccessVisitor().Visit(query.Expression)
            ?? query.Expression;
        return query.Provider.CreateQuery<T>(expression);
    }

    private sealed class ProjectionMemberAccessVisitor : ExpressionVisitor
    {
        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Method.DeclaringType == typeof(Queryable)
                && node.Arguments.Count == 2
                && node.Method.Name is nameof(Queryable.Where)
                    or nameof(Queryable.OrderBy)
                    or nameof(Queryable.OrderByDescending)
                    or nameof(Queryable.ThenBy)
                    or nameof(Queryable.ThenByDescending))
            {
                var visitedSource = Visit(node.Arguments[0]);
                if (visitedSource is MethodCallExpression
                    {
                        Method.DeclaringType: not null,
                        Method.Name: nameof(Queryable.Select),
                        Arguments.Count: 2
                    } select
                    && select.Method.DeclaringType == typeof(Queryable)
                    && StripQuote(select.Arguments[1]) is LambdaExpression selector
                    && StripQuote(node.Arguments[1]) is LambdaExpression operation
                    && selector.Parameters.Count == 1
                    && operation.Parameters.Count == 1)
                {
                    var inlinedBody = new ReplaceExpressionVisitor(operation.Parameters[0], selector.Body)
                        .Visit(operation.Body) ?? operation.Body;
                    inlinedBody = Visit(inlinedBody);

                    var sourceType = selector.Parameters[0].Type;
                    var inlinedLambda = Expression.Lambda(inlinedBody, selector.Parameters);
                    var genericArguments = node.Method.Name == nameof(Queryable.Where)
                        ? new[] { sourceType }
                        : new[] { sourceType, inlinedBody.Type };
                    var pushedOperation = Expression.Call(
                        typeof(Queryable),
                        node.Method.Name,
                        genericArguments,
                        select.Arguments[0],
                        Expression.Quote(inlinedLambda));

                    return Expression.Call(
                        typeof(Queryable),
                        nameof(Queryable.Select),
                        [sourceType, selector.ReturnType],
                        pushedOperation,
                        select.Arguments[1]);
                }

                return node.Update(node.Object, [visitedSource, Visit(node.Arguments[1])]);
            }

            return base.VisitMethodCall(node);
        }

        protected override Expression VisitMember(MemberExpression node)
        {
            var target = StripConvert(node.Expression);
            if (target is NewExpression created)
            {
                var index = FindMemberIndex(created, node.Member.Name);
                if (index >= 0)
                    return Visit(created.Arguments[index]);
            }

            if (target is MemberInitExpression initialized)
            {
                var binding = initialized.Bindings
                    .OfType<MemberAssignment>()
                    .FirstOrDefault(x => x.Member.Name.Equals(node.Member.Name, StringComparison.OrdinalIgnoreCase));
                if (binding is not null)
                    return Visit(binding.Expression);
            }

            return base.VisitMember(node);
        }

        private static int FindMemberIndex(NewExpression expression, string memberName)
        {
            if (expression.Members is not null)
            {
                for (var index = 0; index < expression.Members.Count; index++)
                    if (expression.Members[index].Name.Equals(memberName, StringComparison.OrdinalIgnoreCase))
                        return index;
            }

            var parameters = expression.Constructor?.GetParameters();
            if (parameters is null) return -1;
            for (var index = 0; index < parameters.Length; index++)
                if (parameters[index].Name?.Equals(memberName, StringComparison.OrdinalIgnoreCase) == true)
                    return index;
            return -1;
        }

        private static Expression? StripConvert(Expression? expression)
        {
            while (expression is UnaryExpression
                   {
                       NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked
                   } convert)
                expression = convert.Operand;
            return expression;
        }

        private static Expression StripQuote(Expression expression)
        {
            while (expression is UnaryExpression { NodeType: ExpressionType.Quote } quote)
                expression = quote.Operand;
            return expression;
        }

        private sealed class ReplaceExpressionVisitor(Expression source, Expression replacement) : ExpressionVisitor
        {
            public override Expression? Visit(Expression? node) =>
                node == source ? replacement : base.Visit(node);
        }
    }
}
