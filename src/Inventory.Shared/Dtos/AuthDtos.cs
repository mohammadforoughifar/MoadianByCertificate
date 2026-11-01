namespace Inventory.Shared.Dtos;

/// <summary>درخواست ورود.</summary>
public class LoginRequest
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}

/// <summary>درخواست تغییر رمز عبور توسط خود کاربر.</summary>
public class ChangePasswordRequest
{
    public string CurrentPassword { get; set; } = "";
    public string NewPassword { get; set; } = "";
}

/// <summary>پاسخ ورود موفق.</summary>
public class LoginResponse
{
    public int UserId { get; set; }
    public string Token { get; set; } = "";
    public string Username { get; set; } = "";

    /// <summary>نقش: Admin | Referrer</summary>
    public string Role { get; set; } = "";

    /// <summary>در صورت نقش معرف، شناسه معرف مرتبط</summary>
    public int? ReferrerId { get; set; }

    /// <summary>نام نمایشی (نام معرف در صورت وجود)</summary>
    public string DisplayName { get; set; } = "";

    /// <summary>نام نقش‌های RBAC کاربر</summary>
    public List<string> RoleNames { get; set; } = new();

    /// <summary>دسترسی‌های مؤثر کاربر به شکل "Module.Action" — مبنای نمایش منو و صفحات</summary>
    public List<string> Permissions { get; set; } = new();
}

/// <summary>کاربر سیستم (برای مدیریت کاربران).</summary>
public class UserDto
{
    public int Id { get; set; }
    public string Username { get; set; } = "";

    /// <summary>فقط هنگام ایجاد/تغییر رمز پر می‌شود</summary>
    public string? Password { get; set; }

    /// <summary>نقش: Admin | Referrer</summary>
    public string Role { get; set; } = "Admin";

    /// <summary>نقش‌های RBAC انتخاب‌شده (از بخش نقش‌ها و دسترسی‌ها)</summary>
    public List<int>? RoleIds { get; set; }

    /// <summary>نام نقش‌های RBAC (فقط برای نمایش)</summary>
    public List<string>? RoleNames { get; set; }

    /// <summary>نام</summary>
    public string? FirstName { get; set; }

    /// <summary>نام خانوادگی</summary>
    public string? LastName { get; set; }

    /// <summary>شماره موبایل — برای پیام بله/ایتا</summary>
    public string? Mobile { get; set; }

    /// <summary>شناسه چت بله (پر بودن = کاربر بله دارد)</summary>
    public string? BaleChatId { get; set; }

    /// <summary>شناسه چت ایتا (پر بودن = کاربر ایتا دارد)</summary>
    public string? EitaaChatId { get; set; }

    public int? ReferrerId { get; set; }
    public string? ReferrerName { get; set; }
    public string? SignaturePath { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// واحد سازمانیِ کاربر (جدول Organizations) — همان واحدی که در «تعریف واحدهای سازمانی»
    /// برای هر شرکت ساخته می‌شود و جزء «واحد» در شمارهٔ نامه از آن خوانده می‌شود.
    /// </summary>
    public int? OrganizationId { get; set; }

    /// <summary>
    /// سمت‌های فعالِ کاربر (عنوان سمت‌هایی که هنگام تعریفِ دستیِ کاربر ثبت شده‌اند).
    /// هنگام ذخیره، این فهرست با سمت‌های ثبت‌شده هم‌گام می‌شود؛ اگر null باشد
    /// سمت‌های کاربر دست‌نخورده می‌مانند.
    /// </summary>
    public List<string>? Positions { get; set; }
}

/// <summary>خلاصه داشبورد معرف.</summary>
public class ReferrerDashboard
{
    public string ReferrerName { get; set; } = "";
    public string? CompanyName { get; set; }
    public string? Phone { get; set; }

    /// <summary>اجازه مشاهده کالاهای موجود</summary>
    public bool CanViewProducts { get; set; }
    public decimal GoodsCommissionPercent { get; set; }
    public decimal ServiceCommissionPercent { get; set; }

    public int OrderCount { get; set; }
    public decimal TotalSales { get; set; }
    public decimal TotalCommission { get; set; }
    public decimal TotalPaid { get; set; }
    public decimal WalletBalance { get; set; }

    /// <summary>پورسانت ماه جاری (شمسی)</summary>
    public decimal MonthCommission { get; set; }

    /// <summary>آخرین اسناد فروش دارای پورسانت</summary>
    public List<ReferrerSaleRow> RecentSales { get; set; } = new();

    /// <summary>آخرین پرداخت‌ها</summary>
    public List<ReferrerPayment> RecentPayments { get; set; } = new();
}

/// <summary>سطر فروش در داشبورد معرف.</summary>
public class ReferrerSaleRow
{
    public string Number { get; set; } = "";
    public DateTime Date { get; set; }
    public string? CustomerName { get; set; }
    public decimal Amount { get; set; }
    public decimal Commission { get; set; }
}
