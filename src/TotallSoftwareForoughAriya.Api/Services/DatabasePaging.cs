using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services;

public static partial class Paging
{
    public static int Offset(int page, int pageSize)
        => (int)Math.Min(int.MaxValue, (long)(Math.Max(1, page) - 1) * Math.Max(1, pageSize));

    /// <summary>
    /// Explicit, single-query paging state passed from a collection endpoint to its
    /// service. It is never ambient: auxiliary queries and internal service calls
    /// cannot accidentally inherit the HTTP request's offset.
    /// </summary>
    public sealed class Request
    {
        public int Skip { get; }
        public int? Take { get; }
        public bool IsPaged => Requested(Skip, Take);
        public int? Total { get; private set; }

        public Request(int skip, int? take)
        {
            Skip = Math.Max(0, skip);
            Take = take is > 0 ? take : null;
        }

        public object Result<T>(IEnumerable<T> mappedItems)
        {
            var items = mappedItems.ToList();
            if (!IsPaged) return items;
            if (Total == null && items.Count != 0)
                throw new InvalidOperationException("The result query was not paged in the database.");
            return new { total = Total ?? 0, items };
        }

        public object DictionaryResult<TKey, TValue>(IDictionary<TKey, TValue> items) where TKey : notnull
        {
            if (!IsPaged) return items;
            if (Total == null) throw new InvalidOperationException("The dictionary query was not paged in the database.");
            return new { total = Total.Value, items };
        }

        internal void SetTotal(int total)
        {
            if (Total.HasValue)
                throw new InvalidOperationException("A paging request must be applied to exactly one result query.");
            Total = total;
        }
    }

    /// <summary>
    /// Keep existing service DTO mapping, but count and page its source in SQL.
    /// The callback must pass the request to ToPageListAsync, after all filters
    /// and a deterministic order. Never filter, sort or slice the mapped page.
    /// An early empty result (e.g. no employee for the user) remains supported.
    /// </summary>
    public static async Task<object> ResultAsync<T>(Func<Request, Task<List<T>>> load, int skip, int? take)
    {
        var request = new Request(skip, take);
        var items = await load(request);
        if (!request.IsPaged) return items;
        if (request.Total == null && items.Count != 0)
            throw new InvalidOperationException("The service did not apply database pagination.");
        return new { total = request.Total ?? 0, items };
    }
}

public static class DatabasePagingExtensions
{
    /// <summary>
    /// Materializes only the requested page. Intentionally accepts IQueryable,
    /// not IEnumerable; calling this after ToList/AsEnumerable is a compile error.
    /// Existing caps apply only to legacy unpaged callers, never to total counts.
    /// </summary>
    public static async Task<List<T>> ToPageListAsync<T>(this IQueryable<T> query,
        Paging.Request? pagination, int? defaultCap = null, CancellationToken cancellationToken = default)
    {
        if (pagination?.IsPaged != true)
            return await (defaultCap is > 0 ? query.Take(defaultCap.Value) : query).ToListAsync(cancellationToken);

        pagination.SetTotal(await query.CountAsync(cancellationToken));
        if (pagination.Skip > 0) query = query.Skip(pagination.Skip);
        if (pagination.Take is > 0) query = query.Take(pagination.Take.Value);
        return await query.ToListAsync(cancellationToken);
    }
}
