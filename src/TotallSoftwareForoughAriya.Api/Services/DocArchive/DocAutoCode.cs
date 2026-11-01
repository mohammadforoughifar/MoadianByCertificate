using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services.DocArchive;

/// <summary>
/// شماره‌گذار خودکار کد مدرک (تنظیمات تک‌ردیف DocCodeSettings) — مشترک بین
/// ساخت مدرکِ دستی و ورود انبوه.
/// «سازگار با الگو» همان تعریف بازشمارش یک‌باره است: پیشوند + دست‌کم یک رقم
/// که همهٔ ادامهٔ متن هم رقم باشد (مثل DOC-00012).
/// </summary>
public static class DocAutoCode
{
    /// <summary>ردیف تنظیمات را فقط وقتی برمی‌گرداند که شماره‌گذار فعال باشد.</summary>
    public static Task<DocCodeSettings?> EnabledSettingsAsync(AppDbContext db)
        => db.DocCodeSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Id == 1 && s.Enabled);

    /// <summary>
    /// ردیف تنظیمات بدون شرط فعال بودن (ردیف Id=1 هنگام استارتاپ همیشه ساخته می‌شود).
    /// ورود انبوه از این استفاده می‌کند تا حتی با شماره‌گذار خاموش هم جلوی ورود فایل گرفته نشود.
    /// </summary>
    public static Task<DocCodeSettings?> RowAsync(AppDbContext db)
        => db.DocCodeSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Id == 1);

    /// <summary>
    /// آیا کد با الگوی شماره‌گذار (پیشوند + ارقام) سازگار است؟
    /// دقیقاً همان منطق MatchesPattern در بازشمارش یک‌بارهٔ DocSettingsController.
    /// </summary>
    public static bool MatchesFormula(string? code, string? prefix)
    {
        if (string.IsNullOrEmpty(code)) return false;
        var p = (prefix ?? "").Trim();
        return code.StartsWith(p, StringComparison.OrdinalIgnoreCase)
               && code.Length > p.Length
               && code[p.Length..].All(char.IsDigit);
    }

    /// <summary>
    /// تخصیص اتمیک کد بعدی از شماره‌گذار خودکار؛ اگر شماره‌گذار غیرفعال باشد null برمی‌گرداند.
    /// هم‌زمانی با UPDATE شرطی (NextNumber قبلی) کنترل می‌شود.
    /// <paramref name="allowWhenDisabled"/> برای ورود انبوه است: حتی اگر سوییچ کلی خاموش باشد
    /// هم شماره تخصیص می‌یابد تا ورود هیچ فایلی متوقف نشود (الگو/شمارنده همان ردیف استفاده می‌شود).
    /// </summary>
    public static async Task<string?> AllocateNextCodeAsync(AppDbContext db, bool allowWhenDisabled = false)
    {
        for (var attempt = 0; attempt < 6; attempt++)
        {
            var st = await db.DocCodeSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Id == 1);
            if (st is null || (!st.Enabled && !allowWhenDisabled)) return null;

            var prefix = (st.Prefix ?? "").Trim();
            var pad = Math.Clamp(st.Padding, 3, 12);
            var n = st.NextNumber;

            // پرش از روی کدهایی که قبلاً (مثلاً به‌صورت دستی) با همین الگو ثبت شده‌اند
            while (await db.Documents.AnyAsync(d => d.Code == prefix + n.ToString().PadLeft(pad, '0')))
                n++;

            var affected = await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE DocCodeSettings SET NextNumber = {n + 1} WHERE Id = 1 AND NextNumber = {st.NextNumber}");
            if (affected == 1)
                return prefix + n.ToString().PadLeft(pad, '0');
            // هم‌زمان دیگری شماره را برداشت — دوباره تلاش کن
        }
        throw new InvalidOperationException("تخصیص خودکار کد مدرک به‌خاطر تداخل هم‌زمانی ناموفق بود؛ دوباره تلاش کنید.");
    }
}
