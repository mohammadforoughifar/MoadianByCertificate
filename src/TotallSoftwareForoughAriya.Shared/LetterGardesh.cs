using Inventory.Shared.Dtos;

namespace Inventory.Shared;

// =====================================================================
//  سطل‌بندی افرادِ «در گردش» یک نامه — برای قابلیت «کپی نامه»
//
//  چرا لازم است؟
//  دکمهٔ «کپی نامه» پیش از این فقط عنوان و متن نامهٔ مبدأ را به فرمِ نامهٔ
//  جدید منتقل می‌کرد و همهٔ گیرندگان از دست می‌رفتند (پیام خودِ برنامه هم
//  می‌گفت «گیرندگان را انتخاب و ارسال کنید»). در نتیجه کاربر مجبور بود
//  تک‌تک افرادِ گردش نامهٔ قبلی را دستی پیدا و دوباره انتخاب کند.
//
//  قرارداد: هنگام کپی، هر فردِ در گردش دقیقاً در همان نقشی از پیش انتخاب
//  می‌شود که در نامهٔ مبدأ داشته است —
//      گیرنده (Erja.Type == "گیرنده") → فیلد «گیرنده»
//      ارجاع  (Erja.Type == "ارجاع")   → فیلد «ارجاع»
//      هامش   (Erja.Type == "هامش")    → فیلد «هامش (رونوشت)»
//
//  هر کاربر حداکثر در «یک» سطل قرار می‌گیرد؛ اگر در گردش چند نقش داشته باشد
//  (مثلاً اول گیرنده بوده و بعداً ارجاع خورده) نقشِ اولویت‌دارتر نگه داشته
//  می‌شود تا در فرم جدید دوبار انتخاب نشود.
// =====================================================================

/// <summary>افراد در گردش یک نامه، تفکیک‌شده بر اساس نقش.</summary>
public class LetterCopyRecipients
{
    /// <summary>گیرندگان اصلی — <c>Erja.Type == "گیرنده"</c></summary>
    public HashSet<int> Girande { get; set; } = new();

    /// <summary>ارجاع‌شوندگان — <c>Erja.Type == "ارجاع"</c></summary>
    public HashSet<int> Erja { get; set; } = new();

    /// <summary>هامش / رونوشت — <c>Erja.Type == "هامش"</c></summary>
    public HashSet<int> Hamesh { get; set; } = new();

    /// <summary>جمع افرادِ یکتا در هر سه نقش.</summary>
    public int Total => Girande.Count + Erja.Count + Hamesh.Count;

    public bool IsEmpty => Total == 0;
}

/// <summary>ابزار خواندن درخت گردش نامه و استخراج افرادِ در گردش.</summary>
public static class LetterGardesh
{
    // نقش‌های معتبرِ Erja.Type — همان مقادیری که سمت API نوشته می‌شوند
    // (ErjaService: "ارجاع"/"هامش"؛ مقدار پیش‌فرض موجودیت Erja: "گیرنده").
    public const string TypeGirande = "گیرنده";
    public const string TypeErja = "ارجاع";
    public const string TypeHamesh = "هامش";

    // اولویت نقش‌ها: هرچه کوچک‌تر، اولویت بالاتر.
    private const int PGirande = 0;
    private const int PErja = 1;
    private const int PHamesh = 2;

    /// <summary>
    /// صاف‌کردن درخت گردش (با همهٔ زیرشاخه‌ها) و سطل‌بندی افراد بر اساس نقش‌شان.
    /// </summary>
    /// <param name="nodes">ریشه‌های درخت گردش — خروجی <c>GetGardeshAsync</c>.</param>
    /// <param name="excludeUserId">
    /// کاربری که نباید انتخاب شود؛ معمولاً خودِ کاربر جاری که دارد کپی می‌گیرد.
    /// کمبوهای انتخاب گیرنده در فرم‌ها فهرست کاربر جاری را نشان نمی‌دهند
    /// (<c>u.Id != MyUserId</c>)، پس انتخاب‌کردن او باعث شناسهٔ بدون نام می‌شود.
    /// </param>
    public static LetterCopyRecipients CollectRecipients(
        this IEnumerable<ErjaTreeNodeDto>? nodes, int? excludeUserId = null)
    {
        var best = new Dictionary<int, int>();   // userId → بهترین (کمترین) اولویت نقش دیده‌شده
        if (nodes != null)
            foreach (var root in nodes)
                Visit(root);

        var result = new LetterCopyRecipients();
        foreach (var kv in best)
        {
            switch (kv.Value)
            {
                case PErja: result.Erja.Add(kv.Key); break;
                case PHamesh: result.Hamesh.Add(kv.Key); break;
                default: result.Girande.Add(kv.Key); break;
            }
        }
        return result;

        void Visit(ErjaTreeNodeDto node)
        {
            Offer(node.ReciverUserId, PriorityOf(node.Type));
            if (node.Children != null)
                foreach (var child in node.Children)
                    Visit(child);
        }

        void Offer(int userId, int priority)
        {
            if (userId <= 0) return;
            if (excludeUserId is int ex && ex > 0 && userId == ex) return;
            if (best.TryGetValue(userId, out var current))
            {
                if (priority < current) best[userId] = priority;
            }
            else best[userId] = priority;
        }
    }

    /// <summary>نگاشت <c>Erja.Type</c> به اولویت نقش. نوع ناشناخته ← گیرنده.</summary>
    private static int PriorityOf(string? type) => (type ?? "").Trim() switch
    {
        TypeErja => PErja,
        TypeHamesh => PHamesh,
        _ => PGirande,   // «گیرنده» و هر مقدار ناشناخته/خالی
    };
}
