using System.Globalization;
using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services;

// ============================================================
//  سرویس ساختار شماره اندیکاتور — پورت StructureLetterService کارفرما
//  خروجی نمونه با ترتیب واحد/شماره/سال:  MQ/1/1405
//
//  جزء «واحد» در طرح مرجع از چارت سازمانی گرفته می‌شود:
//      GetOrganizationNameUniqAsync(sematId)  ←  Organization.NameUniq
//  اینجا همان منطق با IOrganizationServices پیاده شده است:
//    • اگر سمت صادرکننده (CreatorSematId) موجود باشد → NameUniq سازمانِ آن سمت
//    • وگرنه → سازمانِ پیش‌فرض (تنظیم‌شده در صفحه‌ی «تنظیمات ← ساختار شماره نامه»)
//  مقدار ثابت appsettings (Letters:UnitCode) فقط برای «سید سازمان اولیه» استفاده می‌شود.
// ============================================================

public interface ILetterStratureService
{
    /// <summary>
    /// ساخت شماره اندیکاتور کامل بر اساس ساختار ذخیره‌شده (معادل TotalNumberAsync مرجع).
    /// date: تاریخ مبنای جزء «سال» (پیش‌فرض: اکنون — برای بازسازی شماره نامه‌های قدیمی، تاریخ ثبت نامه پاس داده می‌شود)
    /// sematId: سمت صادرکننده — مبنای «واحد» (سازمانِ سمت)؛ null یعنی سازمان پیش‌فرض
    /// userId: شناسه صادرکننده — برای bagian «واحد» از واحد اختصاص یافته به کاربر استفاده شود
    /// </summary>
    Task<string> TotalNumberAsync(int number, int typeForm, DateTime? date = null, int? sematId = null, int? userId = null);

    /// <summary>اجزای ساختار برای فرم مشخص (به ترتیب)</summary>
    Task<List<string>> GetStructureAsync(int typeForm);

    /// <summary>ثبت/جایگزینی ساختار فرم (معادل Add/EditStratureLetter مرجع)</summary>
    Task SetStructureAsync(int typeForm, List<string> parts);

    /// <summary>
    /// اجزای معتبر ساختار — «واحد» | «شماره» | «سال»
    /// </summary>
    Task<List<string>> GetValidPartsAsync();
}

public class LetterStratureService : ILetterStratureService
{
    /// <summary>اجزای مجاز ساختار (فارسی — همان کلیدهای ذخیره‌شده در LetterStratures.TypeStrature)</summary>
    public static readonly string[] ValidParts = { "واحد", "شماره", "سال" };

    private readonly AppDbContext _db;
    private readonly IOrganizationServices _org;

    public LetterStratureService(AppDbContext db, IOrganizationServices org)
    {
        _db = db;
        _org = org;
    }

    public async Task<string> TotalNumberAsync(int number, int typeForm, DateTime? date = null, int? sematId = null, int? userId = null)
    {
        if (number == -1) return string.Empty;

        var structure = await _db.LetterStratures
            .Where(s => s.TypeForm == typeForm)
            .OrderBy(s => s.StratureId)
            .Select(s => s.TypeStrature)
            .ToListAsync();

        if (structure.Count == 0) return string.Empty;

        var pc = new PersianCalendar();
        var year = pc.GetYear(date ?? DateTime.Now);

        // «واحد» — از سازمانِ سمتِ صادرکننده، وگرنه سازمانِ کاربر، وگرنه سازمانِ پیش‌فرض
        var unit = await _org.GetOrganizationNameUniqAsync(sematId);
        if (string.IsNullOrEmpty(unit) && userId is > 0)
            unit = await _org.GetOrganizationNameUniqByUserIdAsync(userId);

        var parts = new List<string>();
        foreach (var item in structure)
        {
            switch (item)
            {
                case "سال":
                    parts.Add(year.ToString());
                    break;
                case "واحد":
                    // اگر سازمانی تعریف نشده، جزء «واحد» انداشته می‌شود تا شماره بدون شکاف ساخته شود
                    if (!string.IsNullOrEmpty(unit)) parts.Add(unit);
                    break;
                case "شماره":
                    parts.Add(number.ToString());
                    break;
            }
        }
        return string.Join("/", parts);
    }

    public async Task<List<string>> GetStructureAsync(int typeForm) =>
        await _db.LetterStratures
            .Where(s => s.TypeForm == typeForm)
            .OrderBy(s => s.StratureId)
            .Select(s => s.TypeStrature)
            .ToListAsync();

    public async Task SetStructureAsync(int typeForm, List<string> parts)
    {
        parts ??= new List<string>();

        // اعتبارسنجی: اجزای مجاز، بدون تکرار، «شماره» الزامی، حداقل «شماره + یک جزء دیگر»
        if (parts.Count == 0)
            throw new Exception("ساختار نمی‌تواند خالی باشد.");
        if (parts.Count > ValidParts.Length)
            throw new Exception($"ساختار نمی‌تواند بیشتر از {ValidParts.Length} جزء داشته باشد.");
        if (parts.Any(p => !ValidParts.Contains(p)))
            throw new Exception($"جزء نامعتبر است — اجزای مجاز: {string.Join("، ", ValidParts)}.");
        if (!parts.Contains("شماره"))
            throw new Exception("جزء «شماره» در ساختار الزامی است.");
        if (parts.Count < 2)
            throw new Exception("ساختار باید حداقل «شماره + یک جزء دیگر» داشته باشد.");

        var old = _db.LetterStratures.Where(s => s.TypeForm == typeForm);
        _db.LetterStratures.RemoveRange(old);
        foreach (var p in parts)
            _db.LetterStratures.Add(new LetterStrature { TypeForm = typeForm, TypeStrature = p });
        await _db.SaveChangesAsync();
    }

    public Task<List<string>> GetValidPartsAsync() => Task.FromResult(ValidParts.ToList());
}
