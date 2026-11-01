using System.ComponentModel.DataAnnotations;

// نکته: در این پروژه موجودیت‌های حوزهٔ اداری در namespace Inventory.Api.Data
// تعریف می‌شوند (مسیر پوشه با namespace یکسان نیست) — همسان با OutgoingLetter.cs
namespace Inventory.Api.Data;

// ============================================================
//  رونوشت‌گیرندگان نامه صادره — جدول مستقل
//  تا پیش از این، رونوشت یک ستون متنی آزاد (OutgoingLetters.CopyTo)
//  بود که محدودیت‌هایی داشت: امکان ویرایش/حذفِ یک ردیف نبود،
//  ترتیب قابل کنترل نبود و چاپ فقط یک رشتهٔ خام را نمایش می‌داد.
//
//  این جدول هر رونوشت‌گیرنده را یک ردیفِ مستقل نگه می‌دارد تا
//  کاربر در فرم ایجاد نامه صادره آن‌ها را «یکی‌یکی» با دکمهٔ +
//  بیفزاید و چاپِ «با رونوشت» از همین جدول بخواند.
// ============================================================
public class OutgoingLetterCopyTo
{
    public int Id { get; set; }

    /// <summary>کلید نامه صادره (برابر با OutgoingLetter.Id / LetterSource.Id)</summary>
    public int OutgoingLetterId { get; set; }

    /// <summary>ترتیب نمایش در چاپ و فرم (از ۱ شروع می‌شود)</summary>
    public int RowNo { get; set; } = 1;

    /// <summary>نام رونوشت‌گیرنده — سازمان یا شخص</summary>
    [MaxLength(300)] public string Name { get; set; } = "";

    /// <summary>توضیح/سمتِ رونوشت‌گیرنده (اختیاری — مثلا «مدیرعامل»)</summary>
    [MaxLength(300)] public string? Desc { get; set; }

    /// <summary>شماره/کد داخلی مقصد (اختیاری)</summary>
    [MaxLength(100)] public string? RefNo { get; set; }

    public int CreatorUserId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public bool IsDelete { get; set; }
}
