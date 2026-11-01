namespace Inventory.Client.Services;

/// <summary>
/// فهرست مسیرهای «منابع انسانی فروغ آریا» — برنامهٔ پیشینِ منابع انسانی
/// (حضور و غیاب، مرخصی، حقوق و دستمزد، ارزیابی و… پیشین) که پیش‌تر با عنوان
/// «سامانه‌های پیشین» درون منوی «منابع انسانی اصلی» جا داشت و اکنون به‌صورت
/// یک منوی مستقل در نوار کنار نمایش داده می‌شود.
/// این ماژول کاملاً از «منابع انسانی اصلی» جداست: پرسنل/چارت اختصاصی (fa-hr/*) و جدول‌های مستقل.
/// فهرست مجوزهای این منو با Inventory.Shared.ForoughAryaHrAccess.Permissions همگام است.
/// </summary>
public static class ForoughAryaHrNavigation
{
    private static HrNavigation.Entry E(string href, string title, string icon, params string[] permissions)
        => new(href, title, icon, permissions);

    public static readonly HrNavigation.Entry[] Items =
    [
        E("attendance", "حضور من", "bi-stopwatch", "Attendance.SelfCheckin"),
        E("leave", "مرخصی و مأموریت", "bi-calendar2-check", "LeaveRequests.Request"),
        E("attendance-admin", "مدیریت حضور", "bi-person-gear", "Attendance.ViewAll", "Attendance.ManageShifts"),
        E("hr-admin", "مدیریت مرخصی", "bi-people", "LeaveRequests.Approve", "LeaveRequests.Report"),
        E("work-calendar", "تقویم کاری", "bi-calendar2-week", "Attendance.ManageShifts"),
        E("hr-time", "زمان‌بندی", "bi-clock", "LeaveRequests.Approve", "Attendance.ManageShifts"),
        E("payroll", "حقوق و دستمزد", "bi-cash-stack", "HrPay.Read", "HrPay.Manage"),
        E("hr-performance", "ارزیابی", "bi-graph-up-arrow", "LeaveRequests.Approve", "HrPay.Read"),
        // پرسنل و چارت اختصاصی همین ماژول (جدول FaHrPersonnel) — دیگر به hr-core منابع انسانی اصلی لینک نمی‌شود
        E("fa-hr/personnel", "پرسنل", "bi-person-lines-fill", "LeaveRequests.Approve", "HrPay.Read", "HrPay.Manage", "Attendance.ViewAll"),
        E("fa-hr/org", "چارت سازمانی", "bi-diagram-3", "LeaveRequests.Approve", "HrPay.Read", "HrPay.Manage", "Attendance.ViewAll"),
    ];

    /// <summary>آیتم‌هایی که کاربر فعلی مجوز دیدنشان را دارد (مدیر سیستم همه را می‌بیند).</summary>
    public static HrNavigation.Entry[] Visible(Func<string, bool> has, bool admin)
        => Items.Where(i => i.Allowed(has, admin)).ToArray();

    public static bool CanEnter(Func<string, bool> has, bool admin) => Visible(has, admin).Length > 0;

    public static string Path(string path) => HrNavigation.Path(path);

    private static bool At(string path, string root)
        => path == root || path.StartsWith(root + "/", StringComparison.Ordinal);

    /// <summary>آیا مسیر داده‌شده متعلق به برنامهٔ پیشین (منوی فروغ آریا) است؟</summary>
    public static bool IsWorkspace(string path)
    {
        path = Path(path);
        return Items.Any(i => At(path, i.Href));
    }

    /// <summary>بلندترین مسیر متناظر با صفحهٔ جاری — برای فعال‌کردن لینک منو.</summary>
    public static HrNavigation.Entry? Find(string path)
    {
        path = Path(path);
        return Items.Where(i => At(path, i.Href))
            .OrderByDescending(i => i.Href.Length).FirstOrDefault();
    }

    /// <summary>عنوان صفحهٔ جاری برای منو، تب‌ها و نوار بالا.</summary>
    public static string Title(string path) => Find(path)?.Title ?? "منابع انسانی فروغ آریا";
}
