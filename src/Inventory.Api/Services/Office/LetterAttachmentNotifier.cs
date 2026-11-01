using Inventory.Api.Data;
using Inventory.Api.Hubs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Inventory.Api.Services.Office;

/// <summary>
/// اعلان «پیوست جدید» برای نامه‌ها.
///
/// قاعدهٔ کارفرما: وقتی پیوست تازه‌ای به نامه اضافه می‌شود، همهٔ افرادی که در گردش آن نامه هستند و
/// نامه را <b>رویت کرده‌اند</b> باید اعلان بگیرند. کسانی که هنوز نامه را باز نکرده‌اند اعلان جداگانه
/// نمی‌گیرند (نامه خودش در کارتابلشان نخوانده است). بارگذارندهٔ فایل هم اعلان نمی‌گیرد.
///
/// «رویت» بر پایهٔ ستون‌های موجود تعیین می‌شود:
///   • ردیف ارجاع (<see cref="Inventory.Api.Entities.Office.Erja"/>) با IsRead = true → گیرنده‌ای که نامه را خوانده است.
///   • نامهٔ صادره: ردیف امضاکننده (OutgoingLetterSigners) با IsRead = true.
///   • ایجادکنندهٔ نامه (در هر سه نوع) که عملاً نامه را دیده است.
/// </summary>
public static class LetterAttachmentNotifier
{
    /// <summary>
    /// ارسال اعلان «پیوست جدید» به همهٔ حاضرانِ رویت‌کردهٔ گردش نامه.
    /// </summary>
    /// <param name="db">کانتکست دیتابیس (همان درخواست جاری).</param>
    /// <param name="services">IServiceProvider درخواست (HttpContext.RequestServices) برای گرفتن INotifyService.</param>
    /// <param name="letterSourceId">شناسهٔ نامه در جدول مشترک LetterSources.</param>
    /// <param name="fileName">نام فایل پیوست‌شده.</param>
    /// <param name="uploaderUserId">کاربری که پیوست را افزوده است.</param>
    /// <param name="uploaderName">نام نمایشی همان کاربر.</param>
    /// <returns>تعداد افرادی که اعلان گرفتند.</returns>
    public static async Task<int> NotifyNewAttachmentAsync(AppDbContext db, IServiceProvider services,
        int letterSourceId, string? fileName, int uploaderUserId, string? uploaderName)
    {
        if (letterSourceId <= 0) return 0;

        var letter = await db.LetterSources.AsNoTracking()
            .Where(x => x.Id == letterSourceId && !x.IsDelete)
            .Select(x => new
            {
                x.SourceType,
                CreatorUserId = x.InnerLetter != null ? (int?)x.InnerLetter.CreatorUserId
                              : x.OutgoingLetter != null ? (int?)x.OutgoingLetter.CreatorUserId
                              : x.IncomingLetter != null ? (int?)x.IncomingLetter.CreateUserId
                              : null,
                Title = x.InnerLetter != null ? x.InnerLetter.Title
                      : x.OutgoingLetter != null ? x.OutgoingLetter.Title
                      : x.IncomingLetter != null ? x.IncomingLetter.Title
                      : null,
                Number = x.InnerLetter != null ? x.InnerLetter.LetterNumber
                       : x.OutgoingLetter != null ? x.OutgoingLetter.SadereNumber
                       : x.IncomingLetter != null ? x.IncomingLetter.LetterNumber
                       : null
            })
            .FirstOrDefaultAsync();
        if (letter == null) return 0;

        // ---------- گیرندگانی که نامه را خوانده‌اند ----------
        var readers = await db.Erjas.AsNoTracking()
            .Where(e => e.SourceId == letterSourceId && !e.IsDelete && e.IsRead)
            .Select(e => e.ReciverUserId)
            .Distinct()
            .ToListAsync();

        // نامهٔ صادره: امضاکنندگانی که نامه را دیده‌اند
        if (letter.SourceType == 2)
        {
            var signerReaders = await db.OutgoingLetterSigners.AsNoTracking()
                .Where(s => s.SourceId == letterSourceId && !s.IsDelete && s.IsRead)
                .Select(s => s.UserId)
                .Distinct()
                .ToListAsync();
            readers.AddRange(signerReaders);
        }

        // ایجادکنندهٔ نامه هم در گردش است و نامه را دیده
        if (letter.CreatorUserId is > 0) readers.Add(letter.CreatorUserId.Value);

        var targets = readers.Where(id => id > 0 && id != uploaderUserId).Distinct().ToList();
        if (targets.Count == 0) return 0;

        var (formName, route) = letter.SourceType switch
        {
            1 => ("نامه داخلی", "/letters"),
            2 => ("نامه صادره", "/outgoing-letters"),
            3 => ("نامه وارده", "/incoming-letters"),
            _ => ("نامه", "/letters")
        };

        var numberTag = string.IsNullOrWhiteSpace(letter.Number) ? "" : $"{letter.Number} — ";
        var titleTag = string.IsNullOrWhiteSpace(letter.Title) ? "" : $"«{letter.Title}» — ";
        var fileTag = string.IsNullOrWhiteSpace(fileName) ? "پیوست جدید" : $"فایل: {fileName}";
        var body = $"{numberTag}{titleTag}{fileTag}";
        var fromName = string.IsNullOrWhiteSpace(uploaderName) ? "سیستم" : uploaderName!;
        var link = $"{route}?open={letterSourceId}";

        var notify = services.GetRequiredService<INotifyService>();
        foreach (var uid in targets)
            await notify.SendAsync(uid, "پیوست جدید برای نامه 📎", body, fromName, formName, link);

        return targets.Count;
    }
}
