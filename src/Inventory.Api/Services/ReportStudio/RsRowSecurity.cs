using System.Security.Claims;
using Inventory.Api.Services.Core;

namespace Inventory.Api.Services.ReportStudio;

// =====================================================================
//  امنیت سطح سطر (Row-Level Security)
//
//  قانون پیش‌فرض و سخت‌گیرانه: هر کاربر فقط نامه‌های «خودش» را می‌بیند.
//  نامهٔ خودش یعنی:
//    • سازندهٔ نامه باشد (CreatorUserId / CreateUserId)
//    • یا نامه به او ارجاع شده باشد (Erja.ReciverUserId)
//    • یا خودش ارجاع را فرستاده باشد (Erja.SenderUserId)
//
//  استثنا: مجوز «ViewAll» روی ماژول نامه. اگر مدیر این تیک را برای
//  کاربر/نقشی فعال کند، آن شخص نامه‌های دیگران را هم می‌بیند.
//
//  نکتهٔ مهم: این فیلتر *بعد از* اشتراک‌گذاری گزارش هم اعمال می‌شود.
//  یعنی اگر گزارشی را با کسی share کنید، او همان گزارش را می‌بیند ولی
//  فقط روی سطرهای مجاز خودش. اشتراک گزارش ≠ اشتراک داده.
// =====================================================================

/// <summary>محدودهٔ دید کاربر روی دادهٔ نامه‌ها.</summary>
public sealed class RsRowScope
{
    /// <summary>کاربر جاری.</summary>
    public int UserId { get; init; }

    /// <summary>آیا اجازهٔ دیدن نامه‌های همه را دارد؟</summary>
    public bool CanSeeAllLetters { get; init; }

    /// <summary>آیا اجازهٔ دیدن همهٔ دستورهای کار را دارد؟ فقط مدیر سیستم.</summary>
    public bool CanSeeAllWorkOrders { get; init; }

    /// <summary>وقتی true است فیلتر نامه لازم نیست. روی دستور کار اثر ندارد.</summary>
    public bool Unrestricted => CanSeeAllLetters;

    public static RsRowScope All(int userId) => new()
    {
        UserId = userId,
        CanSeeAllLetters = true,
        CanSeeAllWorkOrders = true
    };
}

public interface IRsRowSecurity
{
    Task<RsRowScope> GetScopeAsync(ClaimsPrincipal user);
}

public sealed class RsRowSecurity : IRsRowSecurity
{
    /// <summary>ماژول‌هایی که مجوز ViewAll آن‌ها دسترسی کامل به نامه‌ها می‌دهد.</summary>
    private static readonly string[] LetterModules =
        { "InnerLetters", "OutgoingLetters", "IncomingLetters" };

    private readonly IEffectivePermissions _perms;
    public RsRowSecurity(IEffectivePermissions perms) => _perms = perms;

    private static int UserId(ClaimsPrincipal u) =>
        int.TryParse(u.FindFirstValue(ClaimTypes.NameIdentifier), out var v) ? v : 0;

    public async Task<RsRowScope> GetScopeAsync(ClaimsPrincipal user)
    {
        var uid = UserId(user);

        // ادمین همیشه همه را می‌بیند
        if (user.IsInRole("Admin") || user.FindFirstValue(ClaimTypes.Role) == "Admin")
            return RsRowScope.All(uid);

        var perms = await _perms.GetAsync(user);

        // تیک استثنا: ViewAll روی هر کدام از ماژول‌های نامه
        var all = LetterModules.Any(m => perms.Contains($"{m}.ViewAll"));

        return new RsRowScope { UserId = uid, CanSeeAllLetters = all };
    }
}
