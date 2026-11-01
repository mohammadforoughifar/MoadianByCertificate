using QuestPDF.Drawing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Inventory.Api.Services.Pdf;

/// <summary>زیرساخت مشترک PDF فارسی ماژول‌های منابع انسانی (فونت وزیرمتن + هلپرهای چاپ).</summary>
public static class HrPdf
{
    public const string Font = "HrFa";
    public const string FontBold = "HrFa-Bold";

    private static bool _fontsOk;
    private static readonly object FontLock = new();

    public static void EnsureFonts()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        if (_fontsOk) return;
        lock (FontLock)
        {
            if (_fontsOk) return;
            try
            {
                var dir = Path.Combine(AppContext.BaseDirectory, "Resources", "fonts");
                var reg = Path.Combine(dir, "Vazirmatn-Regular.ttf");
                var bold = Path.Combine(dir, "Vazirmatn-Bold.ttf");
                if (File.Exists(reg))
                    FontManager.RegisterFontWithCustomName(Font, File.OpenRead(reg));
                if (File.Exists(bold))
                    FontManager.RegisterFontWithCustomName(FontBold, File.OpenRead(bold));
            }
            catch { }
            _fontsOk = true;
        }
    }

    /// <summary>بارگذاری امن لوگوی شرکت برای سربرگ (فقط png/jpg/webp — در صورت خطا null).</summary>
    public static async Task<byte[]?> TryLoadLogoAsync(string? webRoot, string? logoPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(webRoot) || string.IsNullOrWhiteSpace(logoPath)) return null;
            var ext = Path.GetExtension(logoPath).ToLowerInvariant();
            if (ext is not (".png" or ".jpg" or ".jpeg" or ".webp")) return null;
            var full = Path.Combine(webRoot, logoPath.TrimStart('/', '\\'));
            if (!File.Exists(full)) return null;
            return await File.ReadAllBytesAsync(full);
        }
        catch { return null; }
    }

    /// <summary>یک ردیف برچسب/مقدار در جدول اطلاعات.</summary>
    public static void KvRow(TableDescriptor t, string label, string value)
    {
        t.Cell().Element(Lbl).Text(label).FontSize(9).FontColor(Colors.Grey.Darken2);
        t.Cell().Element(Val).Text(value).FontFamily(FontBold).FontSize(10);
    }

    private static IContainer Lbl(IContainer c) =>
        c.Background(Colors.Grey.Lighten3).Border(0.5f).BorderColor(Colors.Grey.Lighten1).Padding(5);

    private static IContainer Val(IContainer c) =>
        c.Border(0.5f).BorderColor(Colors.Grey.Lighten1).Padding(5);
}
