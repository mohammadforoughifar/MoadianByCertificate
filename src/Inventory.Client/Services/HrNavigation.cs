namespace Inventory.Client.Services;

/// <summary>Single UI navigation catalogue. API permissions remain authoritative.</summary>
public static class HrNavigation
{
    public record Entry(string Href, string Title, string Icon, params string[] Permissions)
    {
        public bool Allowed(Func<string, bool> has, bool admin) => admin || Permissions.Any(has);
    }
    public record Section(string Id, string Title, string Icon, Entry[] Items);
    private static Entry E(string href, string title, string icon, params string[] permissions) => new(href, title, icon, permissions);
    public static readonly Section[] Sections =
    [
        new("my", "کارتابل من", "bi-inbox", [
            E("my", "داشبورد من", "bi-grid-1x2", "FaAtt.Read", "FaAtt.Create", "FaPay.Read", "FaCom.Read", "FaLms.Read", "HrCore.Read"),
            E("hr-main/my", "پرونده‌ی من", "bi-person-vcard", "HrCore.Read", "FaAtt.Read", "FaPay.Read", "FaLms.Read", "FaCom.Read"),
            E("fa-att/clock", "ثبت ورود و خروج", "bi-fingerprint", "FaAtt.Read", "FaAtt.Create"),
            E("fa-att/my-leaves", "مرخصی‌های من", "bi-calendar-heart", "FaAtt.Read"),
            E("fa-att/approvals", "تأییدهای من", "bi-person-check", "FaAtt.Read"),
            E("fa-pay/my", "فیش‌های من", "bi-receipt", "FaPay.Read", "FaPay.Manage"),
            E("fa-lms/my", "دوره‌های من", "bi-person-video3", "FaLms.Read"),
            E("fa-com", "تابلوی اعلان‌ها", "bi-megaphone", "FaCom.Read", "FaCom.Manage"),
            E("fa-com/my", "تیکت‌های من", "bi-inbox", "FaCom.Read", "FaCom.Manage"),
            E("fa-com/polls", "نظرسنجی‌ها", "bi-bar-chart", "FaCom.Read"),
            E("fa-com/my-suggestions", "پیشنهادهای من", "bi-lightbulb", "FaCom.Read", "FaCom.Manage")]),
        new("people", "پرسنل و کارگزینی", "bi-people", [
            E("hr-core", "داشبورد کارگزینی", "bi-speedometer2", "HrCore.Read"),
            E("hr-main/employees", "فهرست پرسنل", "bi-person-lines-fill", "HrCore.Read"),
            E("hr-core/profile-requests", "درخواست‌های ویرایش پرونده", "bi-person-gear", "HrCore.Read"),
            E("hr-main/employees/new", "ثبت پرسنل", "bi-person-plus", "HrCore.Create"),
            E("hr-core/contracts", "قراردادها", "bi-file-earmark-text", "HrCore.Read"),
            E("hr-core/decrees", "احکام", "bi-patch-check", "HrCore.Read"),
            E("hr-core/recruitment", "جذب و استخدام", "bi-person-plus", "HrCore.Read"),
            E("hr-core/onboarding", "شروع همکاری", "bi-person-check", "HrCore.Read"),
            E("hr-core/trials", "دوره آزمایشی", "bi-hourglass-split", "HrCore.Read"),
            E("hr-core/job-history", "سوابق شغلی", "bi-clock-history", "HrCore.Read"),
            E("hr-core/exit", "پایان همکاری", "bi-box-arrow-right", "HrCore.Read")]),
        new("org", "ساختار سازمانی", "bi-diagram-3", [
            // مجوزهای تفکیکی (Company/Branches/Org/Positions/Calendar/Rules) همان‌طور که در
            // HrMainController چک می‌شوند؛ مجوز عمومی HrMain.Read همچنان همه را باز می‌کند.
            E("hr-main/company", "اطلاعات شرکت", "bi-building", "HrMain.Read", "HrMain.Company"),
            E("hr-main/branches", "شعب و دفاتر", "bi-shop", "HrMain.Read", "HrMain.Branches"),
            E("hr-main/org", "ساختار سازمانی", "bi-diagram-3", "HrMain.Read", "HrMain.Org"),
            E("hr-main/org/change-log", "تاریخچه و مقایسه ساختار", "bi-clock-history", "HrMain.Read", "HrMain.Org"),
            E("hr-main/positions", "پست‌های سازمانی", "bi-person-badge", "HrMain.Read", "HrMain.Positions"),
            E("hr-main/calendar", "تقویم و تعطیلات", "bi-calendar3", "HrMain.Read", "HrMain.Calendar"),
            E("hr-main/rules", "قوانین پیش‌فرض", "bi-sliders", "HrMain.Read", "HrMain.Rules")]),
        new("attendance", "حضور و مرخصی", "bi-calendar2-check", [
            E("fa-att/daily", "وضعیت روزانه", "bi-calendar2-check", "FaAtt.Read"),
            E("fa-att/calendar", "تقویم غیبت", "bi-calendar3", "FaAtt.Read"),
            E("fa-att/shifts", "شیفت‌های کاری", "bi-clock-history", "FaAtt.Read"),
            E("fa-att/planner", "برنامه‌ریزی شیفت", "bi-calendar3-range", "FaAtt.Read", "FaAtt.Create", "FaAtt.Update", "FaAtt.Manage"),
            E("fa-att/missions", "مأموریت‌ها", "bi-briefcase", "FaAtt.Read"),
            E("fa-att/leaves", "مدیریت مرخصی", "bi-calendar2-week", "FaAtt.Manage"),
            E("fa-att/balances", "مانده مرخصی", "bi-piggy-bank", "FaAtt.Manage")]),
        new("pay", "حقوق و دستمزد", "bi-wallet2", [
            E("fa-pay", "دوره‌های حقوق", "bi-cash-stack", "FaPay.Read", "FaPay.Manage"),
            E("fa-pay/adjustments", "ثبت‌های ماهانه", "bi-pencil-square", "FaPay.Manage"),
            E("fa-pay/loans", "وام و مساعده", "bi-cash-coin", "FaPay.Read", "FaPay.Manage"),
            E("fa-pay/arrears", "معوقات", "bi-hourglass-split", "FaPay.Read", "FaPay.Manage"),
            E("fa-pay/settlements", "تسویه پایان همکاری", "bi-file-earmark-check", "FaPay.Read", "FaPay.Manage"),
            E("fa-pay/compare", "مقایسه دوره‌ها", "bi-arrow-left-right", "FaPay.Read", "FaPay.Manage")]),
        new("growth", "آموزش و ارزیابی", "bi-mortarboard", [
            E("fa-lms", "نمای کلی آموزش", "bi-mortarboard", "FaLms.Read"),
            E("fa-lms/courses", "دوره‌ها", "bi-collection", "FaLms.Read"),
            E("fa-lms/calendar", "تقویم آموزش", "bi-calendar3", "FaLms.Read"),
            E("fa-lms/certificates", "گواهی‌ها", "bi-award", "FaLms.Read"),
            E("fa-lms/needs", "نیازسنجی", "bi-clipboard-data", "FaLms.Manage"),
            E("fa-lms/enrollments", "کارتابل ثبت‌نام", "bi-inbox", "FaLms.Manage"),
            E("fa-lms/grading", "کارتابل تصحیح", "bi-pencil-square", "FaLms.Manage"),
            E("fa-lms/budgets", "بودجه آموزش", "bi-wallet2", "FaLms.Manage"),
            E("hr-core/appraisals", "ارزیابی عملکرد", "bi-graph-up-arrow", "HrCore.Read")]),
        new("com", "ارتباطات و اطلاع‌رسانی", "bi-chat-square-text", [
            E("fa-com/announcements", "مدیریت اطلاعیه‌ها", "bi-megaphone", "FaCom.Manage"),
            E("fa-com/tickets", "کارتابل تیکت‌ها", "bi-ticket-detailed", "FaCom.Manage"),
            E("fa-com/suggestions", "کارتابل پیشنهادها", "bi-inbox", "FaCom.Manage")]),
        new("reports", "گزارش‌ها", "bi-bar-chart-line", [
            E("hr-core/insights", "داشبورد مدیر", "bi-speedometer2", "HrCore.Manage"),
            E("hr-core/data-quality", "کیفیت داده پرسنل", "bi-clipboard2-x", "HrCore.Manage"),
            E("fa-att/reports", "گزارش حضور و غیاب", "bi-calendar2-check", "FaAtt.Manage"),
            E("fa-pay/reports", "گزارش‌های حقوق", "bi-cash-stack", "FaPay.Read", "FaPay.Manage"),
            E("fa-lms/reports", "اثربخشی آموزش", "bi-mortarboard", "FaLms.Manage"),
            E("hr-core/reports", "گزارش‌ساز پرسنلی", "bi-file-earmark-spreadsheet", "HrCore.Manage"),
            E("hr-core/audit", "تاریخچه عملیات", "bi-clipboard-data", "HrCore.Manage")]),
        new("setup", "تنظیمات", "bi-gear", [
            E("hr-main/settings", "مرکز تنظیمات", "bi-gear", "HrMain.Read", "HrCore.Read", "FaPay.Manage", "FaAtt.Manage", "FaCom.Manage", "FaLms.Manage", "FaLms.Read"),
            E("hr-core/contract-templates", "قالب‌های قرارداد", "bi-layout-text-window-reverse", "HrCore.Read"),
            E("fa-att/leave-types", "انواع مرخصی", "bi-tags", "FaAtt.Manage"),
            E("fa-att/devices", "دستگاه‌های تردد", "bi-fingerprint", "FaAtt.Manage"),
            E("fa-pay/items", "اقلام حقوقی", "bi-list-check", "FaPay.Manage"),
            E("fa-pay/settings", "تنظیمات حقوق و مالیات", "bi-gear", "FaPay.Manage"),
            E("fa-lms/instructors", "مدرس‌ها", "bi-person-video3", "FaLms.Read"),
            E("fa-lms/banks", "بانک سؤال", "bi-collection", "FaLms.Manage")])
    ];
    // «سامانه‌های پیشین» (برنامهٔ پیشین منابع انسانی) از این فهرست خارج شد و
    // به‌صورت منوی مستقل «منابع انسانی فروغ آریا» نمایش داده می‌شود
    // (نگاه کنید به ForoughAryaHrNavigation و NavMenu.razor).
    public static Section[] Visible(Func<string, bool> has, bool admin) => Sections
        .Select(s => s with { Items = s.Items.Where(i => i.Allowed(has, admin)).ToArray() }).Where(s => s.Items.Length > 0).ToArray();
    public static bool CanEnter(Func<string, bool> has, bool admin) => Visible(has, admin).Length > 0;
    public static string Path(string path) => path.Split('?', '#')[0].Trim('/').ToLowerInvariant();
    private static bool At(string path, string root) => path == root || path.StartsWith(root + "/", StringComparison.Ordinal);
    public static bool IsWorkspace(string path)
    {
        path = Path(path);
        // ریشه‌های برنامهٔ پیشین (attendance، payroll، leave و…) به ForoughAryaHrNavigation منتقل شدند.
        return new[] { "hr-main", "hr-core", "fa-att", "fa-pay", "fa-lms", "fa-com" }.Any(p => At(path, p));
    }
    public static Entry? Find(string path)
    {
        path = Path(path);
        return Sections.SelectMany(s => s.Items).Where(i => At(path, i.Href))
            .OrderByDescending(i => i.Href.Length).FirstOrDefault();
    }
    public static string? SectionId(string path)
    {
        path = Path(path);
        if (path.StartsWith("hr-main/section/")) return Sections.FirstOrDefault(s => path == "hr-main/section/" + s.Id)?.Id;
        var entry = Find(path);
        return Sections.FirstOrDefault(s => s.Items.Contains(entry!))?.Id;
    }
    public static string Title(string path)
    {
        path = Path(path);
        if (path == "hr-main") return "خانه منابع انسانی";
        if (path.StartsWith("hr-main/section/")) return Sections.FirstOrDefault(s => s.Id == SectionId(path))?.Title ?? "منابع انسانی";
        if (path.StartsWith("hr-main/employees/") && path.EndsWith("/profile")) return "پرونده پرسنل";
        if (path.StartsWith("hr-main/employees/") && int.TryParse(path.Split('/').Last(), out _)) return "ویرایش پرسنل";
        return Find(path)?.Title ?? "منابع انسانی";
    }
}
