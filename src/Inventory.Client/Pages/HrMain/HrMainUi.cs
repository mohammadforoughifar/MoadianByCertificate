namespace Inventory.Client.Pages;

/// <summary>هلپرهای نمایشی «منابع انسانی اصلی» (HrMain)</summary>
public static class HrMainUi
{
    public static string OrgLevel(int l) => l switch
    {
        0 => "شرکت", 1 => "واحد", 2 => "دپارتمان", 3 => "تیم", _ => "—"
    };

    public static string LevelBadge(int l) => l switch
    {
        0 => "text-bg-primary",
        1 => "text-bg-info",
        2 => "text-bg-warning",
        3 => "text-bg-success",
        _ => "text-bg-light"
    };

    public static string LevelIcon(int l) => l switch
    {
        0 => "bi-building-fill",
        1 => "bi-diagram-3-fill",
        2 => "bi-diagram-2-fill",
        3 => "bi-people-fill",
        _ => "bi-circle"
    };

    public static string BranchType(int t) => t switch
    {
        0 => "شعبه", 1 => "دفتر", 2 => "نمایندگی", _ => "—"
    };

    public static string LanguageName(string? lang) => (lang ?? "").ToLowerInvariant() switch
    {
        "en" => "انگلیسی",
        _ => "فارسی"
    };

    public static string CalendarName(string? cal) => (cal ?? "").ToLowerInvariant() switch
    {
        "gregorian" => "میلادی",
        _ => "شمسی (جلالی)"
    };

    public static string HolidayKind(bool isOfficial) => isOfficial ? "رسمی" : "شرکتی";

    public static string HolidayBadge(bool isOfficial) =>
        isOfficial ? "text-bg-danger" : "text-bg-info";
}
