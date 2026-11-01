using Inventory.Shared;
using Inventory.Client.Extensions;

namespace Inventory.Client.Pages;

/// <summary>هلپرهای نمایشی ارتباطات داخلی فروغ آریا (FaCom §۱۵)</summary>
public static class FaComUi
{
    public static string TicketStatus(int s) => s switch
    {
        0 => "جدید", 1 => "در حال بررسی", 2 => "پاسخ داده‌شده", 3 => "بسته‌شده", _ => "—"
    };

    public static string StatusBadge(int s) => s switch
    {
        0 => "text-bg-warning",
        1 => "text-bg-primary",
        2 => "text-bg-success",
        3 => "text-bg-secondary",
        _ => "text-bg-light"
    };

    public static string Category(int c) => c switch
    {
        0 => "سایر", 1 => "مرخصی", 2 => "حقوق و دستمزد", 3 => "بیمه", 4 => "قرارداد", 5 => "آموزش", _ => "—"
    };

    public static string Priority(int p) => p switch { 0 => "کم", 1 => "متوسط", 2 => "زیاد", _ => "—" };

    public static string PriorityBadge(int p) => p switch
    {
        0 => "text-bg-secondary",
        1 => "text-bg-info",
        2 => "text-bg-danger",
        _ => "text-bg-light"
    };

    public static string Audience(int a) => a switch { 0 => "همه", 1 => "واحد خاص", _ => "—" };

    public static string Dt(DateTime d) => d.ToFa();
    public static string Dt(DateTime? d) => d == null ? "—" : d.Value.ToFa();

    public static string TicketUrl(int id) => $"/fa-com/tickets/{id}";
    public static string MyTicketUrl(int id) => $"/fa-com/my/{id}";

    public static string SuggestionStatus(int s) => s switch
    {
        0 => "جدید", 1 => "در بررسی", 2 => "پذیرفته‌شده", 3 => "ردشده", 4 => "اجراشده", _ => "—"
    };

    public static string SuggestionBadge(int s) => s switch
    {
        0 => "text-bg-warning", 1 => "text-bg-primary", 2 => "text-bg-success",
        3 => "text-bg-danger", 4 => "text-bg-dark", _ => "text-bg-light"
    };

    public static string SuggestionCategory(int c) => c switch
    {
        0 => "پیشنهاد", 1 => "انتقاد", 2 => "سایر", _ => "—"
    };
}
