namespace Inventory.Client.Services;

/// <summary>
/// منبع واحد «مجوز مشاهده» برای منوی کناری.
/// <para>
/// قانون: هر ماژول یک اکشن «مشاهده» دارد (<c>View</c> / <c>Read</c> / <c>Access</c>).
/// اگر مدیر در «تنظیمات ← نقش‌ها و دسترسی‌ها» تیک مشاهدهٔ یک ماژول را بردارد،
/// آن آیتم <b>اصلاً</b> در منو رندر نمی‌شود — حتی اگر اکشن‌های دیگر (ایجاد/ویرایش/حذف)
/// همان ماژول تیک خورده باشند. گروه منو هم وقتی همهٔ آیتم‌هایش پنهان شوند رندر نمی‌شود.
/// </para>
/// <para>
/// چند ماژول اکشن مشاهدهٔ استاندارد ندارند و دسترسی‌شان به‌ازای هر بخش است
/// (مثلاً <c>Dashboards.Financial</c> یا <c>LeaveRequests.Request</c>)؛ برای آن‌ها
/// «داشتن حداقل یک دسترسی از ماژول» معادل مشاهده در نظر گرفته می‌شود.
/// </para>
/// </summary>
public static class ModuleAccess
{
    /// <summary>اکشن‌هایی که در همهٔ ماژول‌ها معنی «مشاهده» می‌دهند.</summary>
    private static readonly string[] StandardViewActions = { "View", "Read", "Access" };

    /// <summary>
    /// ماژول‌هایی که اکشن مشاهدهٔ استاندارد ندارند؛ ملاکشان «حداقل یک دسترسی از ماژول» است.
    /// </summary>
    private static readonly HashSet<string> PerSectionModules = new(StringComparer.OrdinalIgnoreCase)
    {
        "ItRequests",     // Create / ViewCompany / ViewDepartment / Expert / Manage
        "ReferrerPanel",  // MyDashboard / MyProducts / MyWallet / MyCard
        "LeaveRequests",  // Request / Approve / Report
        "Attendance",     // SelfCheckin / ViewAll / ManageShifts / Report
        "Dashboards",     // Financial / Management / Hardware
        "ReportPages"     // Kardex / Reorder
    };

    /// <summary>
    /// آیا کاربر مجوز مشاهدهٔ این ماژول را دارد؟
    /// <para>
    /// عمداً «دورزدن مدیر» (IsAdmin) اینجا اعمال <b>نمی‌شود</b>: نقش Admin در
    /// <c>AuthService</c> و <c>RbacSeeder</c> همهٔ پرمیشن‌ها را می‌گیرد، پس به‌طور پیش‌فرض
    /// همه‌چیز را می‌بیند؛ اما اگر مدیر تیک «مشاهده» را برداشت، منو باید به آن احترام بگذارد.
    /// </para>
    /// </summary>
    public static bool CanSee(IAuthState auth, string module)
    {
        if (string.IsNullOrWhiteSpace(module)) return false;

        foreach (var action in StandardViewActions)
            if (auth.Has($"{module}.{action}")) return true;

        // ماژول‌های بدون اکشن مشاهدهٔ استاندارد: هر دسترسی‌ای از ماژول = مشاهده
        return PerSectionModules.Contains(module) && auth.HasModule(module);
    }

    /// <summary>آیا کاربر مجوز مشاهدهٔ حداقل یکی از این ماژول‌ها را دارد؟ (برای عنوان گروه‌های منو)</summary>
    public static bool CanSeeAny(IAuthState auth, params string[] modules) =>
        modules.Any(m => CanSee(auth, m));
}
