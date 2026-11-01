using Inventory.Shared;
using QuestPDF.Drawing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Inventory.Api.Services.Export;

/// <summary>
/// تبدیل <see cref="ExportSpec"/> به PDF فارسی راست‌به‌چپ با QuestPDF.
///
/// نکات فنی:
///   • فونت وزیرمتن از Resources/fonts بارگذاری می‌شود (شکل‌دهی حروف فارسی).
///   • با ContentFromRightToLeft کل چیدمان صفحه راست‌چین می‌شود.
///   • ارقام به فارسی تبدیل می‌شوند چون خروجی برای چاپ است نه محاسبه.
/// </summary>
public static class PdfWriter
{
    private static bool _fontsRegistered;
    private static readonly object FontLock = new();

    private const string Purple = "#4B2FB8";
    private const string PurpleDark = "#2E1A80";
    private const string HeaderBg = "#4B2FB8";
    private const string SoftBg = "#F3F0FF";
    private const string StripeBg = "#FAF9FF";
    private const string TotalBg = "#E8E3FF";
    private const string DangerBg = "#FDECEC";
    private const string SuccessBg = "#E9F8F0";
    private const string BorderColor = "#D8D4EE";
    private const string Muted = "#777777";

    private const string Font = "Vazirmatn";
    private const string FontBold = "Vazirmatn-Bold";

    static PdfWriter()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    private static void EnsureFonts()
    {
        if (_fontsRegistered) return;

        lock (FontLock)
        {
            if (_fontsRegistered) return;

            var dir = Path.Combine(AppContext.BaseDirectory, "Resources", "fonts");
            var regular = Path.Combine(dir, "Vazirmatn-Regular.ttf");
            var bold = Path.Combine(dir, "Vazirmatn-Bold.ttf");

            if (File.Exists(regular))
                FontManager.RegisterFontWithCustomName(Font, File.OpenRead(regular));
            if (File.Exists(bold))
                FontManager.RegisterFontWithCustomName(FontBold, File.OpenRead(bold));

            _fontsRegistered = true;
        }
    }

    public static byte[] Build(ExportSpec spec)
        => Document.Create(c => Compose(c, spec)).GeneratePdf();

    /// <summary>
    /// چیدمان سند را می‌سازد. جدا از <see cref="Build"/> است تا بتوان همین سند را
    /// به‌جای PDF، به تصویر هم رندر کرد (پیش‌نمایش و آزمون چشمی).
    /// </summary>
    public static void Compose(IDocumentContainer container, ExportSpec spec)
    {
        EnsureFonts();

        var columns = spec.PdfColumns();

        container.Page(page =>
        {
            page.Size(spec.Landscape ? PageSizes.A4.Landscape() : PageSizes.A4);
            page.Margin(24);
            page.ContentFromRightToLeft();
            page.DefaultTextStyle(t => t.FontFamily(Font).FontSize(9).LineHeight(1.4f));

            ComposeHeader(page.Header(), spec);
            ComposeContent(page.Content(), spec, columns);
            ComposeFooter(page.Footer(), spec);
        });
    }

    // =====================================================================
    // سربرگ صفحه — روی همه‌ی صفحات تکرار می‌شود
    // =====================================================================

    private static void ComposeHeader(IContainer container, ExportSpec spec)
    {
        container.Column(col =>
        {
            col.Item().Row(row =>
            {
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text(spec.Title)
                        .FontFamily(FontBold).FontSize(16).FontColor(Purple);

                    if (!string.IsNullOrWhiteSpace(spec.Subtitle))
                        c.Item().PaddingTop(2).Text(spec.Subtitle)
                            .FontSize(10).FontColor("#444444");
                });

                row.ConstantItem(160).AlignLeft().Column(c =>
                {
                    c.Item().AlignLeft().Text("سامانه یکپارچه مالی و انبار")
                        .FontSize(8).FontColor(Muted);
                    c.Item().AlignLeft().Text(Digits($"تاریخ صدور: {PersianDate.ToShort(DateTime.Now)}", spec))
                        .FontSize(8).FontColor(Muted);
                    c.Item().AlignLeft().Text(Digits($"ساعت {DateTime.Now:HH:mm}", spec))
                        .FontSize(8).FontColor(Muted);
                });
            });

            col.Item().PaddingTop(6).LineHorizontal(1.4f).LineColor(Purple);
        });
    }

    // =====================================================================
    // بدنه
    // =====================================================================

    private static void ComposeContent(IContainer container, ExportSpec spec, List<ExportColumn> columns)
    {
        container.PaddingTop(10).Column(col =>
        {
            // ---------- چیپ فیلترها ----------
            if (spec.Meta.Count > 0)
            {
                col.Item().PaddingBottom(8).Row(row =>
                {
                    foreach (var m in spec.Meta)
                    {
                        row.AutoItem().PaddingLeft(6).Background(SoftBg)
                            .Border(0.6f).BorderColor(BorderColor)
                            .PaddingVertical(3).PaddingHorizontal(7)
                            .Text(t =>
                            {
                                t.Span($"{m.Label}: ").FontSize(8).FontColor(Muted);
                                t.Span(Digits(m.Value, spec)).FontSize(8).FontFamily(FontBold).FontColor("#333333");
                            });
                    }
                });
            }

            // ---------- کارت‌های خلاصه ----------
            if (spec.Summary.Count > 0)
            {
                col.Item().PaddingBottom(10).Row(row =>
                {
                    foreach (var s in spec.Summary)
                    {
                        row.RelativeItem().PaddingLeft(6).Background(SoftBg)
                            .Border(0.6f).BorderColor(BorderColor).Padding(7)
                            .Column(c =>
                            {
                                c.Item().Text(s.Label).FontSize(7.5f).FontColor(Muted);
                                c.Item().PaddingTop(2).Text(Digits(s.Value, spec))
                                    .FontFamily(FontBold).FontSize(11).FontColor(PurpleDark);
                            });
                    }
                });
            }

            // ---------- جدول ----------
            if (columns.Count == 0 || spec.Rows.Count == 0)
            {
                col.Item().PaddingVertical(30).AlignCenter()
                    .Text("داده‌ای برای نمایش در این گزارش وجود ندارد.")
                    .FontSize(11).FontColor(Muted);
            }
            else
            {
                col.Item().Table(table => BuildTable(table, spec, columns));
            }

            // ---------- یادداشت‌ها ----------
            if (spec.Notes.Count > 0)
            {
                col.Item().PaddingTop(12).Column(c =>
                {
                    foreach (var note in spec.Notes)
                        c.Item().Text("• " + Digits(note, spec)).FontSize(8).FontColor(Muted);
                });
            }
        });
    }

    private static void BuildTable(TableDescriptor table, ExportSpec spec, List<ExportColumn> columns)
    {
        // ---------- تعریف ستون‌ها ----------
        table.ColumnsDefinition(def =>
        {
            if (spec.ShowRowNumbers) def.ConstantColumn(24);

            foreach (var c in columns)
            {
                if (c.Width > 0) def.ConstantColumn((float)c.Width);
                else def.RelativeColumn();
            }
        });

        // ---------- سربرگ جدول (روی هر صفحه تکرار می‌شود) ----------
        table.Header(header =>
        {
            if (spec.ShowRowNumbers) HeaderCell(header.Cell(), "ردیف");
            foreach (var c in columns) HeaderCell(header.Cell(), c.Title);
        });

        // ---------- سطرها ----------
        var index = 1;
        foreach (var row in spec.Rows)
        {
            var isTotalish = row.Style is ExportRowStyle.Total or ExportRowStyle.Subtotal;
            var bg = RowBackground(row.Style, index);

            if (spec.ShowRowNumbers)
            {
                var cell = table.Cell();
                BodyCell(cell, isTotalish ? "" : Digits(index.ToString(), spec), bg,
                    align: "center", bold: false, muted: true);
            }

            for (var i = 0; i < columns.Count; i++)
            {
                var col = columns[i];
                var value = i < row.Values.Count ? row.Values[i] : null;
                var text = Format(value, col.Kind, spec);

                var indent = i == 0 ? row.Indent : 0;

                BodyCell(table.Cell(), text, bg, AlignOf(col.Kind), isTotalish, false, indent, col.Wrap);
            }

            if (!isTotalish) index++;
        }

        // ---------- سطر جمع ----------
        var total = spec.EffectiveTotalRow();
        if (total is not null)
        {
            if (spec.ShowRowNumbers) BodyCell(table.Cell(), "", TotalBg, "center", true);

            for (var i = 0; i < columns.Count; i++)
            {
                var col = columns[i];
                var value = i < total.Values.Count ? total.Values[i] : null;

                // اگر مقدار جمع نداریم ولی ستون اول است، برچسب «جمع کل» می‌گذاریم
                var text = value is null && i == 0 && !col.Sum
                    ? "جمع کل"
                    : Format(value, col.Kind, spec);

                BodyCell(table.Cell(), text, TotalBg, AlignOf(col.Kind), true);
            }
        }
    }

    private static void HeaderCell(IContainer cell, string text)
    {
        cell.Background(HeaderBg).Border(0.5f).BorderColor("#3A2490")
            .PaddingVertical(5).PaddingHorizontal(3)
            .AlignCenter().AlignMiddle()
            .Text(text).FontFamily(FontBold).FontSize(8.5f).FontColor("#FFFFFF");
    }

    private static void BodyCell(IContainer cell, string text, string? bg, string align,
        bool bold = false, bool muted = false, int indent = 0, bool wrap = false)
    {
        // ShowEntire مانع می‌شود سطری که متنش دو خطی شده، وسط مرز صفحه نصف شود
        var c = cell.ShowEntire().Border(0.4f).BorderColor(BorderColor);
        if (bg is not null) c = c.Background(bg);

        c = c.PaddingVertical(3.5f).PaddingHorizontal(4);
        if (indent > 0) c = c.PaddingRight(4 + indent * 10);

        c = align switch
        {
            "center" => c.AlignCenter(),
            "left" => c.AlignLeft(),
            _ => c.AlignRight()
        };

        var t = c.AlignMiddle().Text(text).FontSize(8);
        if (bold) t = t.FontFamily(FontBold);
        if (muted) t.FontColor("#999999");
    }

    private static string? RowBackground(ExportRowStyle style, int index) => style switch
    {
        ExportRowStyle.Total or ExportRowStyle.Subtotal => TotalBg,
        ExportRowStyle.Danger => DangerBg,
        ExportRowStyle.Success => SuccessBg,
        ExportRowStyle.Muted => SoftBg,
        _ => index % 2 == 0 ? StripeBg : null
    };

    private static string AlignOf(ExportValueKind kind) => kind switch
    {
        ExportValueKind.Money or ExportValueKind.Number => "left",
        ExportValueKind.Int or ExportValueKind.Date or ExportValueKind.DateTime
            or ExportValueKind.Percent or ExportValueKind.Bool => "center",
        _ => "right"
    };

    // =====================================================================
    // پاورقی با شماره صفحه
    // =====================================================================

    private static void ComposeFooter(IContainer container, ExportSpec spec)
    {
        container.PaddingTop(6).Column(col =>
        {
            col.Item().LineHorizontal(0.6f).LineColor(BorderColor);
            col.Item().PaddingTop(4).Row(row =>
            {
                row.RelativeItem().Text(spec.Module ?? "گزارش سیستم")
                    .FontSize(7.5f).FontColor(Muted);

                row.RelativeItem().AlignCenter().Text(t =>
                {
                    t.DefaultTextStyle(s => s.FontSize(7.5f).FontColor(Muted));
                    t.Span("صفحه ");
                    // شماره صفحه را QuestPDF خودش تولید می‌کند، پس تبدیل ارقام
                    // باید از طریق Format انجام شود نه Digits سراسری
                    t.CurrentPageNumber().Format(n => Digits(n?.ToString(), spec));
                    t.Span(" از ");
                    t.TotalPages().Format(n => Digits(n?.ToString(), spec));
                });

                row.RelativeItem().AlignLeft()
                    .Text(Digits($"{PersianDate.ToShort(DateTime.Now)}", spec))
                    .FontSize(7.5f).FontColor(Muted);
            });
        });
    }

    // =====================================================================
    // قالب‌بندی مقادیر
    // =====================================================================

    internal static string Format(object? value, ExportValueKind kind, ExportSpec spec)
    {
        if (value is null) return "";

        switch (kind)
        {
            case ExportValueKind.Money:
                return ExportSpec.TryDecimal(value, out var m)
                    ? (m == 0 ? "—" : Digits(Fa.Money(m), spec))
                    : Digits(value.ToString(), spec);

            case ExportValueKind.Number:
                return ExportSpec.TryDecimal(value, out var n)
                    ? Digits(Fa.Number(n), spec)
                    : Digits(value.ToString(), spec);

            case ExportValueKind.Int:
                return ExportSpec.TryDecimal(value, out var i)
                    ? Digits(((long)i).ToString("#,##0"), spec)
                    : Digits(value.ToString(), spec);

            case ExportValueKind.Percent:
                return ExportSpec.TryDecimal(value, out var p)
                    ? Digits(p.ToString("0.#") + "٪", spec)
                    : Digits(value.ToString(), spec);

            case ExportValueKind.Date:
                return value is DateTime d ? Digits(PersianDate.ToShort(d), spec) : Digits(value.ToString(), spec);

            case ExportValueKind.DateTime:
                return value is DateTime dt ? Digits(PersianDate.ToShortWithTime(dt), spec) : Digits(value.ToString(), spec);

            case ExportValueKind.Bool:
                return value is bool b ? (b ? "بله" : "خیر") : value.ToString() ?? "";

            default:
                return Digits(value.ToString(), spec);
        }
    }

    private static string Digits(string? text, ExportSpec spec)
        => spec.PersianDigits ? Fa.Digits(text) : (text ?? "");
}
