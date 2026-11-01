using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services;

public static class DatabaseTreePaging
{
    /// <summary>
    /// Page roots in SQL, then hydrate only their descendants. Total counts roots,
    /// not nodes. Both root/child queries must retain the same authorization scope.
    /// The caller supplies deterministic ordering and its own orphan-root policy.
    /// </summary>
    public static async Task<List<T>> ReadTreePageAsync<T>(
        this IQueryable<T> all, IQueryable<T> roots,
        Func<int[], IQueryable<T>> children, Func<T, int> key,
        Paging.Request? pagination) where T : class
    {
        if (pagination?.IsPaged != true) return await all.ToListAsync();
        var result = await roots.ToPageListAsync(pagination);
        var seen = result.Select(key).ToHashSet();
        var frontier = seen.ToArray();
        while (frontier.Length > 0)
        {
            var next = new List<T>();
            // Bound parameter lists for SQL Server and large sibling sets.
            foreach (var batch in frontier.Chunk(500))
                next.AddRange(await children(batch).ToListAsync());
            var added = next.Where(row => seen.Add(key(row))).ToList();
            result.AddRange(added);
            frontier = added.Select(key).ToArray();
        }
        return result;
    }
}
