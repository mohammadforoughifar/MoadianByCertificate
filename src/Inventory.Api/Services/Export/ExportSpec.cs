namespace Inventory.Api.Services.Export;

// =====================================================================
// مدل توصیف یک گزارش برای خروجی گرفتن
//
// هر گزارش یک بار به شکل «ExportSpec» توصیف می‌شود و سپس دو موتور
// مستقل (ExcelWriter و PdfWriter) آن را به xlsx و pdf تبدیل می‌کنند.
// به این ترتیب منطق هر گزارش فقط یک جا نوشته می‌شود.
// =====================================================================

/// <summary>نوع داده‌ی ستون — قالب‌بندی و ترازبندی از روی آن تعیین می‌شود.</summary>
public enum ExportValueKind
{
    /// <summary>متن ساده — راست‌چین</summary>
    Text = 0,

    /// <summary>عدد صحیح (تعداد سطر، شماره)</summary>
    Int = 1,

    /// <summary>عدد اعشاری — مقدار و تعداد</summary>
    Number = 2,

    /// <summary>مبلغ ریالی — با جداکننده هزارگان</summary>
    Money = 3,

    /// <summary>تاریخ — در PDF شمسی، در اکسل تاریخ واقعی</summary>
    Date = 4,

    /// <summary>تاریخ و ساعت</summary>
    DateTime = 5,

    /// <summary>درصد</summary>
    Percent = 6,

    /// <summary>بله / خیر</summary>
    Bool = 7
}

/// <summary>سبک نمایشی یک سطر.</summary>
public enum ExportRowStyle
{
    Normal = 0,

    /// <summary>سطر جمع کل — پررنگ با پس‌زمینه</summary>
    Total = 1,

    /// <summary>سطر جمع جزء / سرگروه</summary>
    Subtotal = 2,

    /// <summary>سطر برجسته — مثلاً کسری انبار</summary>
    Danger = 3,

    /// <summary>سطر برجسته مثبت — مثلاً اضافی انبار</summary>
    Success = 4,

    /// <summary>سطر افتتاحیه یا توضیحی</summary>
    Muted = 5
}

/// <summary>تعریف یک ستون گزارش.</summary>
public class ExportColumn
{
    public ExportColumn() { }

    public ExportColumn(string title, ExportValueKind kind = ExportValueKind.Text, double width = 0)
    {
        Title = title;
        Kind = kind;
        Width = width;
    }

    public string Title { get; set; } = "";

    public ExportValueKind Kind { get; set; } = ExportValueKind.Text;

    /// <summary>عرض نسبی در PDF و عرض کاراکتری در اکسل — صفر یعنی خودکار.</summary>
    public double Width { get; set; }

    /// <summary>این ستون در سطر جمع، جمع زده شود.</summary>
    public bool Sum { get; set; }

    /// <summary>ستون در PDF حذف شود (فقط در اکسل بیاید) — برای گزارش‌های پهن.</summary>
    public bool ExcelOnly { get; set; }

    /// <summary>متن طولانی است و باید در PDF بشکند.</summary>
    public bool Wrap { get; set; }
}

/// <summary>یک سطر گزارش.</summary>
public class ExportRow
{
    public ExportRow() { }

    public ExportRow(params object?[] values) => Values = values.ToList();

    public List<object?> Values { get; set; } = new();

    public ExportRowStyle Style { get; set; } = ExportRowStyle.Normal;

    /// <summary>تورفتگی سطر (درخت حساب‌ها) — تعداد سطح.</summary>
    public int Indent { get; set; }

    public ExportRow With(ExportRowStyle style) { Style = style; return this; }
    public ExportRow WithIndent(int indent) { Indent = indent; return this; }
}

/// <summary>یک برچسب فیلتر یا فراداده که بالای گزارش چاپ می‌شود.</summary>
public record ExportMeta(string Label, string Value);

/// <summary>توصیف کامل یک گزارش جدولی.</summary>
public class ExportSpec
{
    /// <summary>عنوان اصلی — تیتر صفحه و نام کاربرگ.</summary>
    public string Title { get; set; } = "گزارش";

    /// <summary>زیرعنوان — مثلاً نام کالا یا حساب.</summary>
    public string? Subtitle { get; set; }

    /// <summary>نام ماژول — در پاورقی چاپ می‌شود.</summary>
    public string? Module { get; set; }

    /// <summary>نام فایل بدون پسوند — اگر خالی باشد از عنوان ساخته می‌شود.</summary>
    public string? FileBaseName { get; set; }

    /// <summary>فیلترهای اعمال‌شده — بالای گزارش به شکل چیپ نمایش داده می‌شوند.</summary>
    public List<ExportMeta> Meta { get; set; } = new();

    public List<ExportColumn> Columns { get; set; } = new();

    public List<ExportRow> Rows { get; set; } = new();

    /// <summary>سطر جمع کل — اگر null باشد و ستونی Sum داشته باشد، خودکار ساخته می‌شود.</summary>
    public ExportRow? TotalRow { get; set; }

    /// <summary>کارت‌های خلاصه بالای گزارش (عنوان، مقدار).</summary>
    public List<ExportMeta> Summary { get; set; } = new();

    /// <summary>یادداشت‌های پایین گزارش.</summary>
    public List<string> Notes { get; set; } = new();

    /// <summary>صفحه افقی — برای گزارش‌های پهن.</summary>
    public bool Landscape { get; set; }

    /// <summary>ستون شماره ردیف اضافه شود.</summary>
    public bool ShowRowNumbers { get; set; } = true;

    /// <summary>در PDF ارقام فارسی شوند.</summary>
    public bool PersianDigits { get; set; } = true;

    /// <summary>نام فایل نهایی با پسوند.</summary>
    public string FileName(string extension)
    {
        var name = FileBaseName;

        if (string.IsNullOrWhiteSpace(name))
        {
            // عنوان فارسی برای نام فایل مناسب نیست؛ به لاتین ساده تبدیل می‌کنیم
            name = "report";
        }

        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmm");
        return $"{name}-{stamp}.{extension}";
    }

    /// <summary>ستون‌هایی که در PDF نمایش داده می‌شوند.</summary>
    public List<ExportColumn> PdfColumns() => Columns.Where(c => !c.ExcelOnly).ToList();

    /// <summary>
    /// سطر جمع را برمی‌گرداند؛ اگر تعریف نشده باشد ولی ستون‌های Sum وجود داشته باشد،
    /// آن را از روی داده‌ها می‌سازد.
    /// </summary>
    public ExportRow? EffectiveTotalRow()
    {
        if (TotalRow is not null) return TotalRow;
        if (!Columns.Any(c => c.Sum) || Rows.Count == 0) return null;

        var row = new ExportRow { Style = ExportRowStyle.Total };

        for (var i = 0; i < Columns.Count; i++)
        {
            if (!Columns[i].Sum) { row.Values.Add(null); continue; }

            decimal sum = 0;
            foreach (var r in Rows)
            {
                if (r.Style is ExportRowStyle.Total or ExportRowStyle.Subtotal) continue;
                if (i < r.Values.Count && r.Values[i] is not null && TryDecimal(r.Values[i], out var d)) sum += d;
            }
            row.Values.Add(sum);
        }

        return row;
    }

    internal static bool TryDecimal(object? value, out decimal result)
    {
        switch (value)
        {
            case decimal d: result = d; return true;
            case int i: result = i; return true;
            case long l: result = l; return true;
            case double db: result = (decimal)db; return true;
            case float f: result = (decimal)f; return true;
            default: result = 0; return false;
        }
    }
}
