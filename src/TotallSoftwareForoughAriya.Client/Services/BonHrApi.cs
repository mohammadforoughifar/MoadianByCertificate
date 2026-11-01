using System.Net.Http.Headers;
using System.Text.Json;
using Inventory.Shared.Dtos;
using Microsoft.JSInterop;

namespace Inventory.Client.Services;

/// <summary>کلاینت HTTP ماژول منابع انسانی بن‌سازه — مسیرهای /radis-hr/api</summary>
public sealed class BonHrApi
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly IApiClient _api;
    private readonly HttpClient _http;
    private readonly IAuthState _auth;
    private readonly ApiOptions _opts;

    public BonHrApi(IApiClient api, HttpClient http, IAuthState auth, ApiOptions opts)
    {
        _api = api;
        _http = http;
        _auth = auth;
        _opts = opts;
    }

    private static string P(string path) => "radis-hr/" + path.TrimStart('/');

    /// <summary>
    /// آخرین خطایی که بلعیده شده است. همهٔ متدهای این کلاس استثنا را می‌گیرند و مقدار
    /// پیش‌فرض برمی‌گردانند (فهرست خالی، null، false)؛ بدون این، صفحه فقط «خالی» می‌بیند
    /// و کاربر نمی‌تواند «داده‌ای نیست» را از «سرور خطا داد» تشخیص دهد.
    /// صفحه‌ها می‌توانند این را نمایش دهند؛ با هر فراخوانی موفق پاک می‌شود.
    /// </summary>
    public string? LastError { get; private set; }

    private T Fail<T>(Exception ex, T fallback)
    {
        LastError = ex.Message;
        return fallback;
    }

    private void ClearError() => LastError = null;

    public async Task<List<T>> ListAsync<T>(string path)
    {
        try { var r = await _api.GetAsync<List<T>>(P(path)) ?? new(); ClearError(); return r; }
        catch (Exception ex) { return Fail<List<T>>(ex, new()); }
    }

    /// <summary>
    /// نسخهٔ صفحه‌دارِ <see cref="ListAsync{T}"/> — skip/take را به سرور می‌فرستد تا فقط
    /// ردیف‌های همان صفحه از دیتابیس خوانده شود (اندپوینت‌های بن‌سازه همگی
    /// Paging.ResultAsync/QueryAsync دارند و صفحه‌بندی را در SQL اعمال می‌کنند).
    ///
    /// چرا ListAsync کافی نیست: به‌محض فرستادن skip/take، شکل پاسخ سرور از «آرایه»
    /// به «{ total, items }» تغییر می‌کند و دی‌سریالایزِ مستقیم به List&lt;T&gt; استثنا
    /// می‌دهد. ListOrPaged هر دو شکل را می‌فهمد و TotalCount را هم برمی‌گرداند.
    /// </summary>
    /// <param name="path">مسیر نسبت به radis-hr — ممکن است از قبل ?filter=… داشته باشد.</param>
    /// <param name="page">شمارهٔ صفحه، از ۱.</param>
    /// <param name="pageSize">اندازهٔ صفحه؛ باید مثبت باشد.</param>
    public async Task<PagedResult<T>> ListPagedAsync<T>(string path, int page, int pageSize)
    {
        var size = pageSize > 0 ? pageSize : 25;
        var skip = (Math.Max(1, page) - 1) * size;
        var sep = path.Contains('?') ? "&" : "?";
        try
        {
            var res = await ListOrPaged.GetPagedAsync<T>(_api, P($"{path}{sep}skip={skip}&take={size}"));
            ClearError();
            return res;
        }
        catch (Exception ex) { return Fail<PagedResult<T>>(ex, new PagedResult<T>()); }
    }

    public async Task<T?> GetAsync<T>(string path)
    {
        try { var r = await _api.GetAsync<T>(P(path)); ClearError(); return r; }
        catch (Exception ex) { return Fail<T?>(ex, default); }
    }

    public async Task<T?> PostAsync<T>(string path, object? body = null)
    {
        try { var r = await _api.PostAsync<T>(P(path), body); ClearError(); return r; }
        catch (Exception ex) { return Fail<T?>(ex, default); }
    }

    public async Task<T?> PutAsync<T>(string path, object? body = null)
    {
        try { var r = await _api.PutAsync<T>(P(path), body); ClearError(); return r; }
        catch (Exception ex) { return Fail<T?>(ex, default); }
    }

    public async Task<bool> DeleteAsync(string path)
    {
        try { await _api.DeleteAsync(P(path)); ClearError(); return true; }
        catch (Exception ex) { Fail<bool>(ex, false); return false; }
    }

    public async Task<bool> SendAsync(HttpMethod method, string path, object? body = null)
    {
        try
        {
            if (method == HttpMethod.Post) { await _api.PostAsync<object>(P(path), body); return true; }
            if (method == HttpMethod.Put) { await _api.PutAsync<object>(P(path), body); return true; }
            if (method == HttpMethod.Delete) { await _api.DeleteAsync(P(path)); return true; }
            return false;
        }
        catch (Exception ex) { Fail<bool>(ex, false); return false; }
    }

    public Task<T> PostFileAsync<T>(string path, Stream fileStream, string fileName)
        => _api.PostFileAsync<T>(P(path), fileStream, fileName);

    public async Task<T?> PostFormAsync<T>(string path, MultipartFormDataContent content)
    {
        var url = $"{_opts.BaseUrl.TrimEnd('/')}/{P(path)}";
        var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        if (!string.IsNullOrEmpty(_auth.Token))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _auth.Token);
        var resp = await _http.SendAsync(req);
        var text = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode) return default;
        if (string.IsNullOrWhiteSpace(text)) return default;
        return JsonSerializer.Deserialize<T>(text, JsonOpts);
    }

    public async Task DownloadCsvAsync(IJSRuntime js, string fileName, string csv)
    {
        var bytes = System.Text.Encoding.UTF8.GetPreamble()
            .Concat(System.Text.Encoding.UTF8.GetBytes(csv)).ToArray();
        await js.InvokeVoidAsync("saveAsFile", fileName, Convert.ToBase64String(bytes));
    }
}

public static class BonHrNav
{
    public static readonly (string Href, string Icon, string Label, string Section)[] Items =
    {
        ("bon-hr", "bi-speedometer2", "داشبورد", "Dashboard"),
        ("bon-hr/employees", "bi-people", "پرسنل", "Employees"),
        ("bon-hr/employee-entry", "bi-person-plus", "ورود اطلاعات", "EmployeeEntry"),
        ("bon-hr/attendance", "bi-clock-history", "حضور و غیاب", "Attendance"),
        ("bon-hr/payroll", "bi-cash-stack", "حقوق و دستمزد", "Payroll"),
        ("bon-hr/statutory-rules", "bi-journal-text", "الزامات سالانه", "StatutoryRules"),
        ("bon-hr/organization-structure", "bi-diagram-3", "واحدها و ماتریس", "OrgStructure"),
        ("bon-hr/organization-settings", "bi-calendar3", "تقویم و ساعات", "OrgSettings"),
        ("bon-hr/hse", "bi-shield-plus", "ایمنی و حوادث", "Hse"),
        ("bon-hr/finance", "bi-wallet2", "مالی", "Finance"),
        ("bon-hr/accounting", "bi-calculator", "حسابداری حقوق", "Accounting"),
        ("bon-hr/production-daily", "bi-clipboard-data", "گزارش روزانه", "ProductionDaily"),
        ("bon-hr/notices", "bi-bell", "اطلاعیه‌ها", "Notices"),
    };
}
