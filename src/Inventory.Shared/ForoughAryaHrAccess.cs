namespace Inventory.Shared;

/// <summary>
/// ================== منابع انسانی فروغ آریا (برنامهٔ پیشین) — قاعدهٔ «کاربر مجاز» ==================
/// <para>
/// در تمام بخش‌های این ماژول (کمبوها، فهرست‌ها، گزارش‌ها و محاسبات) فقط کاربرانی نمایش داده
/// می‌شوند که هر دو شرط زیر را داشته باشند:
/// </para>
/// <list type="number">
///   <item>حساب کاربری‌شان <b>فعال</b> باشد (Users.IsActive).</item>
///   <item>به ماژول منابع انسانی فروغ آریا <b>دسترسی</b> داشته باشند؛ یعنی دست‌کم یکی از
///   <see cref="Permissions"/> از طریق یک نقش <b>فعال</b> به آن‌ها داده شده باشد
///   (کاربران قدیمیِ بدون نقش RBAC: فقط سطح‌های <see cref="LegacyRoles"/>).</item>
/// </list>
/// <para>
/// این فهرست دقیقاً همان مجوزهایی است که منوی «منابع انسانی فروغ آریا» را در نوار کنار نمایش
/// می‌دهند (ForoughAryaHrNavigation)؛ اگر آیتمی به آن منو اضافه/کم شد، این فهرست هم باید همگام شود.
/// مجوزهای منابع انسانی اصلی (HrCore/HrMain/FaAtt/FaPay/...) عمداً اینجا نیستند — دو ماژول مجزا هستند.
/// </para>
/// </summary>
public static class ForoughAryaHrAccess
{
    public static readonly string[] Permissions =
    {
        "Attendance.SelfCheckin",
        "Attendance.ViewAll",
        "Attendance.ManageShifts",
        "LeaveRequests.Request",
        "LeaveRequests.Approve",
        "LeaveRequests.Report",
        "HrPay.Read",
        "HrPay.Manage",
    };

    /// <summary>
    /// سطح‌های قدیمی (ستون Users.Role) که در نبودِ نقش RBAC فعال، به این ماژول دسترسی دارند
    /// (همان رفتار ورود به سیستم: Admin همهٔ مجوزها را دارد و Operator/Accountant حضور و غیاب را).
    /// </summary>
    public static readonly string[] LegacyRoles = { "Admin", "Operator", "Accountant" };
}
