namespace RadisHr.Shared.Models;

/// <summary>
/// الزامات قانونی سالانه حقوق — پورت مستقیم DEFAULT_RULES از assets/statutory-rules.js
/// هیچ عددی تغییر نکرده است.
/// </summary>
public class StatutoryRules
{
    public int Id { get; set; }
    public int Year { get; set; } = 1405;

    public decimal MinimumDailyWage { get; set; } = 5_541_850m;
    public decimal SeniorityDaily { get; set; } = 166_667m;
    public decimal HousingMonthly { get; set; } = 30_000_000m;
    public decimal FoodMonthly { get; set; } = 22_000_000m;
    public decimal MarriageMonthly { get; set; } = 5_000_000m;
    public decimal ChildMultiplier { get; set; } = 3m;

    public decimal WorkerInsuranceRate { get; set; } = 7m;
    public decimal EmployerInsuranceRate { get; set; } = 23m;
    public decimal MaximumInsurableDailyMultiplier { get; set; } = 7m;

    public decimal OvertimePremiumRate { get; set; } = 40m;
    public decimal NightWorkPremiumRate { get; set; } = 35m;
    public decimal FridayWorkPremiumRate { get; set; } = 40m;
    public decimal ShiftMorningEveningRate { get; set; } = 10m;
    public decimal ShiftThreeShiftRate { get; set; } = 15m;
    public decimal ShiftNightRotationRate { get; set; } = 22.5m;
    public decimal MissionMinimumDailyMultiplier { get; set; } = 1m;

    public decimal TaxMonthlyExemption { get; set; } = 400_000_000m;

    /// <summary>
    /// پله‌های مالیاتی. مقدار اولیه عمداً خالی است: EF Core هنگام خواندن از پایگاه داده،
    /// ردیف‌های بارگذاری‌شده را به همین مجموعه «اضافه» می‌کند. اگر اینجا مقدار پیش‌فرض
    /// بگذاریم، پس از خواندن ۱۰ پله به‌دست می‌آید (۵ پیش‌فرض + ۵ ذخیره‌شده).
    /// برای ساخت یک نمونهٔ کامل با مقادیر رسمی از CreateDefault استفاده کنید.
    /// </summary>
    public List<TaxBracket> TaxBrackets { get; set; } = new();

    /// <summary>پله‌های مالیاتی رسمی ۱۴۰۵ — مطابق DEFAULT_RULES نسخهٔ اصلی</summary>
    public static List<TaxBracket> DefaultTaxBrackets() => new()
    {
        new TaxBracket { Ceiling = 800_000_000m,   Rate = 10m, Ordinal = 0 },
        new TaxBracket { Ceiling = 1_000_000_000m, Rate = 15m, Ordinal = 1 },
        new TaxBracket { Ceiling = 1_200_000_000m, Rate = 20m, Ordinal = 2 },
        new TaxBracket { Ceiling = 1_400_000_000m, Rate = 25m, Ordinal = 3 },
        new TaxBracket { Ceiling = null,           Rate = 30m, Ordinal = 4 }
    };

    /// <summary>نمونهٔ کامل با تمام مقادیر پیش‌فرض رسمی (شامل پله‌های مالیاتی)</summary>
    public static StatutoryRules CreateDefault(int year = 1405) =>
        new() { Year = year, TaxBrackets = DefaultTaxBrackets() };

    public InsurableItems Insurable { get; set; } = new();

    public string SourceReference { get; set; } =
        "مصوبه شورای عالی کار مورخ ۱۴۰۴/۱۲/۲۴؛ مصوبه هیأت وزیران حق مسکن ۱۴۰۵؛ نامه مالیاتی ۲۰۰/۱۰۰۵/ص مورخ ۱۴۰۵/۰۱/۳۱؛ بخشنامه دستمزد مبنای کسر حق‌بیمه ۱۴۰۵";

    public string SourceNote { get; set; } =
        "مبالغ به ریال است. حق اولاد برای هر فرزند واجد شرایط، سه برابر حداقل مزد روزانه محاسبه می‌شود.";

    public List<string> SourceUrls { get; set; } = new()
    {
        "https://dolat.ir/detail/480104",
        "https://dolat.ir/detail/480541",
        "https://dotic.ir/news/20326/",
        "https://tamin.ir/news/item/197716/"
    };

    public string EffectiveFrom { get; set; } = "1405/01/01";
    public DateTime? UpdatedAt { get; set; }
    public string UpdatedBy { get; set; } = "مقادیر پایه رسمی V014";
}

public class TaxBracket
{
    public int Id { get; set; }
    public int StatutoryRulesId { get; set; }
    public int Ordinal { get; set; }
    /// <summary>null یعنی بدون سقف (آخرین پلکان)</summary>
    public decimal? Ceiling { get; set; }
    public decimal Rate { get; set; }
}

/// <summary>اقلام مشمول کسر حق‌بیمه — همان مقادیر rules.insurable</summary>
public class InsurableItems
{
    public bool Base { get; set; } = true;
    public bool Seniority { get; set; } = true;
    public bool Housing { get; set; } = true;
    public bool Food { get; set; } = true;
    public bool Marriage { get; set; } = true;
    public bool Child { get; set; } = false;
    public bool Overtime { get; set; } = true;
    public bool Shift { get; set; } = true;
    public bool Night { get; set; } = true;
    public bool Friday { get; set; } = true;
    public bool Mission { get; set; } = false;
    public bool Attraction { get; set; } = true;
    public bool Supervisor { get; set; } = true;
    public bool Performance { get; set; } = true;
    public bool Agreed { get; set; } = true;
}
