namespace Inventory.Api.Data;

/// <summary>
/// قالب‌های آماده قرارداد (§۹: موقت/دائم/پروژه‌ای).
/// همیشه و بدون شرط پرچم اجرا می‌شود (اگر جدول خالی باشد).
/// </summary>
public static class HrContractMasterSeeder
{
    public static void Seed(AppDbContext db)
    {
        if (!db.HrContractTemplates.Any())
        {
            db.HrContractTemplates.AddRange(
                new HrContractTemplate
                {
                    Name = "قرارداد موقت ۱۲ ماهه", Type = HrEmploymentType.Gharardadi, DurationMonths = 12,
                    Terms = "قرارداد مدت‌معین یک‌ساله طبق قانون کار.", SortOrder = 1
                },
                new HrContractTemplate
                {
                    Name = "قرارداد دائم", Type = HrEmploymentType.Rasmi, DurationMonths = null,
                    Terms = "قرارداد بدون مدت‌زمان (دائم).", SortOrder = 2
                },
                new HrContractTemplate
                {
                    Name = "قرارداد پروژه‌ای", Type = HrEmploymentType.Projei, DurationMonths = null,
                    Terms = "قرارداد انجام کار معین / پروژه‌ای؛ تاریخ پایان = پایان پروژه.", SortOrder = 3
                });
            db.SaveChanges();
        }
    }
}
