using Inventory.Shared.Dtos;

namespace Inventory.Api.Services;

/// <summary>قرارداد سرویس احراز هویت و مدیریت کاربران.</summary>
public interface IAuthService
{
    /// <summary>ورود و صدور توکن JWT. null = نام کاربری/رمز اشتباه.</summary>
    Task<LoginResponse?> LoginAsync(LoginRequest request);

    // ---------------- مدیریت کاربران (فقط مدیر) ----------------
    Task<List<UserDto>> GetUsersAsync(Paging.Request? pagination = null);
    /// <summary>ایجاد/ویرایش کاربر؛ callerIsAdmin=false یعنی اپراتور است و تغییر شمارهٔ خودش در این مسیر ممنوع است.</summary>
    Task<UserDto> SaveUserAsync(UserDto dto, int currentUserId, bool callerIsAdmin = true);
    Task DeleteUserAsync(int id, int currentUserId, bool callerIsAdmin = true);

    /// <summary>تغییر رمز عبور توسط خود کاربر (با تأیید رمز فعلی).</summary>
    Task ChangePasswordAsync(int userId, string currentPassword, string newPassword);

    /// <summary>داشبورد معرف بر اساس شناسه معرف.</summary>
    Task<ReferrerDashboard> GetReferrerDashboardAsync(int referrerId);
}
