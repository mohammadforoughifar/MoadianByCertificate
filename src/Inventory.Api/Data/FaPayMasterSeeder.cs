namespace Inventory.Api.Data;

/// <summary>
/// داده پایه حقوق و دستمزد (تنظیمات + پلکان مالیاتی مثال + اقلام پیش‌فرض).
/// همیشه و بدون شرط پرچم اجرا می‌شود (هر جدول اگر خالی باشد).
/// مقادیر مالیاتی «مثال شروع» هستند و HR باید طبق بخشنامه جاری اصلاحشان کند.
/// </summary>
public static class FaPayMasterSeeder
{
    public static void Seed(AppDbContext db)
    {
        if (!db.FaPaySettings.Any())
        {
            db.FaPaySettings.Add(new FaPaySettings
            {
                Note = "مقادیر شروع §۸ — معافیت و پلکان مالیاتی طبق بخشنامه جاری اصلاح شود."
            });
            db.SaveChanges();
        }

        if (!db.FaPayTaxBrackets.Any())
        {
            db.FaPayTaxBrackets.AddRange(
                new FaPayTaxBracket { FromAmount = 0, ToAmount = 100_000_000, Rate = 10, SortOrder = 1 },
                new FaPayTaxBracket { FromAmount = 100_000_000, ToAmount = 250_000_000, Rate = 15, SortOrder = 2 },
                new FaPayTaxBracket { FromAmount = 250_000_000, ToAmount = null, Rate = 20, SortOrder = 3 });
            db.SaveChanges();
        }

        if (!db.FaPayItemTypes.Any())
        {
            db.FaPayItemTypes.AddRange(
                new FaPayItemType { Code = "HOUSING", Name = "حق مسکن", Kind = FaPayItemKind.Earning, IsFixed = true, SortOrder = 1 },
                new FaPayItemType { Code = "FOOD", Name = "خواربار", Kind = FaPayItemKind.Earning, IsFixed = true, SortOrder = 2 },
                new FaPayItemType { Code = "TRANS", Name = "ایاب و ذهاب", Kind = FaPayItemKind.Earning, IsFixed = true, SortOrder = 3 },
                new FaPayItemType { Code = "KARANEH", Name = "کارانه", Kind = FaPayItemKind.Earning, IsFixed = false, SortOrder = 4 },
                new FaPayItemType { Code = "PADASH", Name = "پاداش", Kind = FaPayItemKind.Earning, IsFixed = false, SortOrder = 5 },
                new FaPayItemType { Code = "MISSION", Name = "حق مأموریت", Kind = FaPayItemKind.Earning, IsFixed = false, SortOrder = 6 },
                new FaPayItemType { Code = "ADVANCE", Name = "مساعده", Kind = FaPayItemKind.Deduction, IsFixed = false, SortOrder = 7 });
            db.SaveChanges();
        }
    }
}
