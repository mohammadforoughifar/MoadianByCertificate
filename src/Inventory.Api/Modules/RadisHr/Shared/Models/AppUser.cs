namespace RadisHr.Shared.Models;

/// <summary>
/// حساب کاربری — جایگزین سمت سرور برای radisHrPasswordAccountsV005.
/// این تنها بخشی است که عمداً ارتقا یافته: رمز عبور با PBKDF2 و نمک اختصاصی
/// ذخیره می‌شود، نه به‌صورت متن آشکار در localStorage.
/// </summary>
public class AppUser
{
    public int Id { get; set; }
    /// <summary>کلید نقش: hr, ceo, guard, hse, warehouse, production, finance, accounting</summary>
    public string UserKey { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string RoleTitle { get; set; } = "";

    public string PasswordHash { get; set; } = "";
    public string PasswordSalt { get; set; } = "";
    /// <summary>در اولین ورود باید رمز شخصی ساخته شود (مطابق رفتار نسخهٔ اصلی)</summary>
    public bool MustChangePassword { get; set; } = true;
    public DateTime? PasswordChangedAt { get; set; }
    /// <summary>سیاست انقضای رمز: passwordMaxAgeDays = ۳۰ روز</summary>
    public const int PasswordMaxAgeDays = 30;

    public DateTime? LastLoginAt { get; set; }
    public int FailedAttempts { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>دسترسی صفحات هر نقش — پورت جدول users از app.js + v016-workflows.js</summary>
public static class RolePages
{
    public static readonly Dictionary<string, (string Name, string Role, string[] Pages, string Home)> Map = new()
    {
        ["hr"] = ("مدیر اداری", "اداری",
            new[] { "employees", "employeeEntry", "attendance", "payroll", "statutoryRules",
                    "organizationStructure", "organizationSettings", "hse", "notices" }, "employees"),

        ["ceo"] = ("مدیرعامل", "مدیریت ارشد",
            new[] { "dashboard", "employees", "employeeEntry", "payroll",
                    "organizationStructure", "hse", "notices" }, "dashboard"),

        ["guard"] = ("مالک فرآیند نگهبانی", "نگهبانی",
            new[] { "attendance", "notices" }, "attendance"),

        ["hse"] = ("مسئول HSE", "ایمنی، بهداشت و محیط‌زیست",
            new[] { "hse", "notices" }, "hse"),

        ["warehouse"] = ("انباردار", "ثبت تحویل PPE",
            new[] { "hse", "notices" }, "hse"),

        ["production"] = ("مدیر تولید", "تأیید دریافت PPE",
            new[] { "hse", "notices", "productionDaily" }, "hse"),

        ["finance"] = ("کاربر مالی", "ثبت مساعده پرداخت‌شده",
            new[] { "finance", "notices" }, "finance"),

        ["accounting"] = ("مدیر مالی حسابداری", "کنترل و تأیید حقوق",
            new[] { "accounting", "payroll", "notices" }, "accounting")
    };

    public static readonly Dictionary<string, string> PageTitles = new()
    {
        ["dashboard"] = "داشبورد",
        ["employees"] = "فهرست پرسنل",
        ["employeeEntry"] = "ورود اطلاعات پرسنل",
        ["attendance"] = "حضور و غیاب",
        ["payroll"] = "حقوق و دستمزد",
        ["statutoryRules"] = "الزامات سالانه حقوق",
        ["organizationStructure"] = "واحدها و ماتریس پرسنل",
        ["organizationSettings"] = "تقویم و ساعات کاری",
        ["hse"] = "ایمنی و حوادث",
        ["finance"] = "مالی",
        ["accounting"] = "مالی حسابداری",
        ["productionDaily"] = "گزارش روزانه حضور پرسنل",
        ["notices"] = "اطلاعیه‌ها"
    };
}
