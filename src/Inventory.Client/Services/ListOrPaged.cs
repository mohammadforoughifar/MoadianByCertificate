using System.Text.Json;
using Inventory.Shared.Dtos;

namespace Inventory.Client.Services;

/// <summary>
/// خواندن «فهرست» از API وقتی شکل پاسخ ممکن است آرایهٔ ساده یا PagedResult باشد.
/// بعضی endpointهای نامه‌ها در سمت سرور به PagedResult (با صفحه‌بندی) تغییر کرده‌اند؛
/// بدون این لایهٔ سازگاری، دی‌سریالایز آرایه/آبجکت باعث خطا و خالی‌شدن فهرست در کلاینت می‌شود.
/// </summary>
public static class ListOrPaged
{
    private static readonly JsonSerializerOptions Opts = new() { PropertyNameCaseInsensitive = true };

    public static async Task<List<T>> GetAsync<T>(IApiClient api, string path)
    {
        try
        {
            var el = await api.GetAsync<JsonElement>(path);
            return Normalize<T>(el);
        }
        catch
        {
            return new List<T>();
        }
    }

    public static async Task<PagedResult<T>> GetPagedAsync<T>(IApiClient api, string path)
    {
        // Do not turn authorization/network errors into an apparently empty page.
        var el = await api.GetAsync<JsonElement>(path);
        return NormalizePaged<T>(el);
    }

    /// <summary>آرایه → همان آرایه؛ آبجکت دارای items/Items → items؛ در غیر این صورت فهرست خالی.</summary>
    public static List<T> Normalize<T>(JsonElement el)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.Array:
                return el.Deserialize<List<T>>(Opts) ?? new List<T>();

            case JsonValueKind.Object:
                foreach (var prop in el.EnumerateObject())
                {
                    if (string.Equals(prop.Name, "items", StringComparison.OrdinalIgnoreCase) && prop.Value.ValueKind == JsonValueKind.Array)
                    {
                        return prop.Value.Deserialize<List<T>>(Opts) ?? new List<T>();
                    }
                }
                return new List<T>();

            default:
                return new List<T>();
        }
    }

    public static PagedResult<T> NormalizePaged<T>(JsonElement el)
    {
        if (el.ValueKind == JsonValueKind.Object)
        {
            var res = new PagedResult<T>();
            foreach (var prop in el.EnumerateObject())
            {
                if (string.Equals(prop.Name, "items", StringComparison.OrdinalIgnoreCase) && prop.Value.ValueKind == JsonValueKind.Array)
                {
                    res.Items = prop.Value.Deserialize<List<T>>(Opts) ?? new List<T>();
                }
                else if ((string.Equals(prop.Name, "totalCount", StringComparison.OrdinalIgnoreCase) || string.Equals(prop.Name, "total", StringComparison.OrdinalIgnoreCase)) && prop.Value.ValueKind == JsonValueKind.Number)
                {
                    res.TotalCount = prop.Value.GetInt32();
                }
                else if (string.Equals(prop.Name, "page", StringComparison.OrdinalIgnoreCase) && prop.Value.ValueKind == JsonValueKind.Number)
                {
                    res.Page = prop.Value.GetInt32();
                }
                else if (string.Equals(prop.Name, "pageSize", StringComparison.OrdinalIgnoreCase) && prop.Value.ValueKind == JsonValueKind.Number)
                {
                    res.PageSize = prop.Value.GetInt32();
                }
            }
            return res;
        }
        else if (el.ValueKind == JsonValueKind.Array)
        {
            var list = el.Deserialize<List<T>>(Opts) ?? new List<T>();
            return new PagedResult<T>
            {
                Items = list,
                TotalCount = list.Count,
                Page = 1,
                PageSize = Math.Max(1, list.Count)
            };
        }
        return new PagedResult<T>();
    }
}
