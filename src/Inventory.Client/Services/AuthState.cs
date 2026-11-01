using Inventory.Shared.Dtos;
using Microsoft.JSInterop;

namespace Inventory.Client.Services;

/// <summary>قرارداد وضعیت احراز هویت سمت کلاینت.</summary>
public interface IAuthState
{
    bool IsLoggedIn { get; }
    bool IsAdmin { get; }
    bool IsOperator { get; }
    bool IsReferrer { get; }

    /// <summary>مدیر یا اپراتور — دسترسی به بخش‌های عملیاتی</summary>
    bool CanOperate { get; }
    string? Token { get; }
    int UserId { get; }
    string DisplayName { get; }
    string Role { get; }

    /// <summary>شناسه معرف مرتبط با کاربر جاری (اگر کاربر معرف باشد)</summary>
    int? ReferrerId { get; }

    /// <summary>بررسی دسترسی دقیق: Has("Orders.Read") یا Has("Dashboards.Financial")</summary>
    bool Has(string permission);

    /// <summary>آیا کاربر حداقل یک دسترسی از این ماژول دارد؟</summary>
    bool HasModule(string module);

    /// <summary>
    /// آیا کاربر مجوز «مشاهده» این ماژول را دارد؟ مبنای نمایش آیتم در منوی کناری.
    /// اگر مدیر در «تنظیمات ← نقش‌ها و دسترسی‌ها» تیک مشاهدهٔ ماژول را بردارد،
    /// آن آیتم (و گروه منوی مربوطه در صورت خالی‌شدن) اصلاً رندر نمی‌شود.
    /// </summary>
    bool CanSee(string module);
    event Action? Changed;

    Task InitializeAsync();
    Task SignInAsync(LoginResponse login);
    Task SignOutAsync();
}

/// <summary>نگه‌داری توکن JWT و اطلاعات کاربر جاری (ذخیره در localStorage).</summary>
public class AuthState : IAuthState
{
    private readonly IJSRuntime _js;
    private LoginResponse? _session;

    public AuthState(IJSRuntime js) => _js = js;

    public bool IsLoggedIn => _session is not null;
    public bool IsAdmin => _session?.Role == "Admin";
    public bool IsOperator => _session?.Role == "Operator";
    public bool IsReferrer => _session?.Role == "Referrer";
    public bool CanOperate => IsAdmin || IsOperator;
    public string? Token => _session?.Token;
    public int UserId => _session?.UserId ?? 0;
    public string DisplayName => _session?.DisplayName ?? "";
    public string Role => _session?.Role ?? "";
    public int? ReferrerId => _session?.ReferrerId;

    // ================== بررسی دسترسی‌های RBAC ==================
    private HashSet<string>? _permCache;
    private HashSet<string> Perms =>
        _permCache ??= new HashSet<string>(_session?.Permissions ?? new List<string>(), StringComparer.OrdinalIgnoreCase);

    public bool Has(string permission) => Perms.Contains(permission);

    public bool HasModule(string module) =>
        Perms.Any(p => p.StartsWith(module + ".", StringComparison.OrdinalIgnoreCase));

    public bool CanSee(string module) => ModuleAccess.CanSee(this, module);

    public event Action? Changed;

    public async Task InitializeAsync()
    {
        try
        {
            var json = await _js.InvokeAsync<string?>("localStorage.getItem", "authSession");
            if (!string.IsNullOrWhiteSpace(json))
                _session = System.Text.Json.JsonSerializer.Deserialize<LoginResponse>(json,
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            _permCache = null;
        }
        catch { _session = null; _permCache = null; }
        Changed?.Invoke();
    }

    public async Task SignInAsync(LoginResponse login)
    {
        _session = login;
        _permCache = null;
        await _js.InvokeVoidAsync("localStorage.setItem", "authSession",
            System.Text.Json.JsonSerializer.Serialize(login));
        Changed?.Invoke();
    }

    public async Task SignOutAsync()
    {
        try { await _js.InvokeVoidAsync("appPush.clearLocal"); } catch { }
        _session = null;
        _permCache = null;
        await _js.InvokeVoidAsync("localStorage.removeItem", "authSession");
        Changed?.Invoke();
    }
}

/// <summary>قرارداد سرویس احراز هویت.</summary>
public interface IAuthApi
{
    Task<PagedResult<ReferrerProductItem>> GetMyProductsPagedAsync(int skip, int take, string? search = null);
    Task<PagedResult<ReferrerPayment>> GetMyPaymentsPagedAsync(int skip, int take);
    Task<LoginResponse> LoginAsync(LoginRequest request);
    Task<List<UserDto>> GetUsersAsync();
    Task<UserDto> SaveUserAsync(UserDto user);
    Task DeleteUserAsync(int id);
    Task<ReferrerDashboard> GetMyDashboardAsync();
    Task<List<ReferrerPayment>> GetMyPaymentsAsync();
    Task<List<ReferrerProductItem>> GetMyProductsAsync(string? search = null);
    Task ChangePasswordAsync(ChangePasswordRequest request);
    Task<UserProfileDto> GetMyProfileAsync();
    Task RequestMobileOtpAsync(string mobile);
    Task VerifyMobileOtpAsync(string code);
    Task RequestEmailOtpAsync(string email);
    Task VerifyEmailOtpAsync(string code);
    Task ClearMyEmailAsync();
}

/// <summary>پیاده‌سازی سرویس احراز هویت سمت کلاینت.</summary>
public class AuthApi : IAuthApi
{
    private readonly IApiClient _api;
    public AuthApi(IApiClient api) => _api = api;

    public Task<PagedResult<ReferrerProductItem>> GetMyProductsPagedAsync(int skip, int take, string? search = null)
        => ListOrPaged.GetPagedAsync<ReferrerProductItem>(_api, $"api/my/products?skip={skip}&take={take}&search={Uri.EscapeDataString(search ?? "")}");

    public Task<PagedResult<ReferrerPayment>> GetMyPaymentsPagedAsync(int skip, int take)
        => ListOrPaged.GetPagedAsync<ReferrerPayment>(_api, $"api/my/payments?skip={skip}&take={take}");


    public Task<LoginResponse> LoginAsync(LoginRequest request)
        => _api.PostAsync<LoginResponse>("api/auth/login", request);

    public Task<List<UserDto>> GetUsersAsync()
        => _api.GetAsync<List<UserDto>>("api/users");

    public Task<UserDto> SaveUserAsync(UserDto user)
        => _api.PostAsync<UserDto>("api/users", user);

    public Task DeleteUserAsync(int id)
        => _api.DeleteAsync($"api/users/{id}");

    public Task<ReferrerDashboard> GetMyDashboardAsync()
        => _api.GetAsync<ReferrerDashboard>("api/my/dashboard");

    public Task<List<ReferrerPayment>> GetMyPaymentsAsync()
        => _api.GetAsync<List<ReferrerPayment>>("api/my/payments");

    public Task<List<ReferrerProductItem>> GetMyProductsAsync(string? search = null)
        => _api.GetAsync<List<ReferrerProductItem>>($"api/my/products?search={Uri.EscapeDataString(search ?? "")}");

    public Task ChangePasswordAsync(ChangePasswordRequest request)
        => _api.PostAsync<object>("api/auth/change-password", request);

    public Task<UserProfileDto> GetMyProfileAsync()
        => _api.GetAsync<UserProfileDto>("api/profile");

    public Task RequestMobileOtpAsync(string mobile)
        => _api.PostAsync<object>("api/profile/mobile/otp", new UserContactOtpRequestDto { Destination = mobile });

    public Task VerifyMobileOtpAsync(string code)
        => _api.PostAsync<object>("api/profile/mobile/verify", new UserContactOtpVerifyDto { Code = code });

    public Task RequestEmailOtpAsync(string email)
        => _api.PostAsync<object>("api/profile/email/otp", new UserContactOtpRequestDto { Destination = email });

    public Task VerifyEmailOtpAsync(string code)
        => _api.PostAsync<object>("api/profile/email/verify", new UserContactOtpVerifyDto { Code = code });

    public Task ClearMyEmailAsync()
        => _api.DeleteAsync("api/profile/email");
}
