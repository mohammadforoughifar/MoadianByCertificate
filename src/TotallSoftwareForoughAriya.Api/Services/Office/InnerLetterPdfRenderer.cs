using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Inventory.Api.Services.Office.Outgoing;
using QuestPDF.Drawing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Inventory.Api.Services.Office;

internal sealed record InnerLetterHameshRow(string Receiver, string Instruction, string Action);
internal sealed record InnerLetterPrintData(string Number, DateTime Date, string Title, string Body,
    string Receivers, string Creator, IReadOnlyList<InnerLetterHameshRow> Hamesh, string ReferralDates = "");

/// <summary>Pure PDF composition: no database, signatures, closing phrase, stamp or page-number footer.</summary>
internal static class InnerLetterPdfRenderer
{
    private static readonly object FontLock = new();
    private static bool _fontsRegistered;

    internal static byte[] Render(InnerLetterPrintData data, bool a5, bool withHamesh,
        byte[]? homepaBackground, bool attachment, string fontDirectory)
    {
        EnsureFonts(fontDirectory);
        QuestPDF.Settings.License = LicenseType.Community;
        var homepa = homepaBackground is { Length: > 0 };
        var fontSize = a5 ? 9f : 11f;
        var pc = new PersianCalendar();
        var date = PersianDigits($"{pc.GetYear(data.Date):0000}/{pc.GetMonth(data.Date):00}/{pc.GetDayOfMonth(data.Date):00}");
        var number = PersianDigits(data.Number);
        var body = PlainText(data.Body);

        void HeaderField(IContainer container, HomepaLetterheadLayout.Field field, string value, bool center = false)
        {
            var box = container.PaddingLeft(field.Left).PaddingTop(field.Top)
                .Width(field.Width).Height(field.Height).AlignMiddle();
            box = center ? box.AlignCenter() : box.AlignRight();
            box.ScaleToFit().Text(value).FontFamily("Vazirmatn-Bold").FontSize(a5 ? 7 : 9)
                .FontColor(Colors.White).LineHeight(1.2f).DirectionFromRightToLeft();
        }

        return Document.Create(root => root.Page(page =>
        {
            page.Size(a5 ? PageSizes.A5 : PageSizes.A4);
            // Non-Homepa backgrounds are overlaid by PDFsharp after rendering.
            page.PageColor(Colors.Transparent);
            page.ContentFromRightToLeft();
            page.DefaultTextStyle(x => x.FontFamily("Vazirmatn").FontSize(fontSize)
                .LineHeight(1.3f).DirectionFromRightToLeft());
            if (homepa)
            {
                page.MarginTop(0);
                page.MarginHorizontal(0);
                page.MarginBottom(a5 ? 64 : 90);
                // QuestPDF's page background and header are repeated on EVERY page, including overflow.
                page.Background().Image(homepaBackground!).FitUnproportionally();
                page.Header().ContentFromLeftToRight().Height(a5 ? 112 : 160).Layers(layers =>
                {
                    layers.PrimaryLayer().Extend();
                    // پیوند/پیوست هم‌سطح با شماره و تاریخ باشد (Right) و در یک خط بماند؛
                    // ScaleToFit اندازهсниازک می‌کند تا خط شکسته نشود و کادر عمودی بزرگ نشود.
                    HeaderField(layers.Layer(), HomepaLetterheadLayout.Number(a5), number);
                    HeaderField(layers.Layer(), HomepaLetterheadLayout.Date(a5), date);
                    HeaderField(layers.Layer(), HomepaLetterheadLayout.Attachment(a5), attachment ? "دارد" : "ندارد", false);
                });
            }
            else
            {
                page.MarginTop(a5 ? 90 : 130);
                page.MarginHorizontal(a5 ? 30 : 46);
                page.MarginBottom(a5 ? 42 : 60);
                page.Header().AlignLeft().Column(c =>
                {
                    c.Item().Text($"شماره نامه: {number}");
                    c.Item().Text($"تاریخ ثبت: {date}");
                    c.Item().Text($"تاریخ ارجاع: {(string.IsNullOrWhiteSpace(data.ReferralDates) ? "—" : data.ReferralDates)}");
                    c.Item().Text($"پیوست: {(attachment ? "دارد" : "ندارد")}");
                });
            }

            var content = page.Content();
            if (homepa) content = content.PaddingTop(a5 ? 12 : 16).PaddingHorizontal(a5 ? 32 : 46);
            content.Column(c =>
            {
                c.Spacing(a5 ? 5 : 7);
                c.Item().Text($"عنوان: {data.Title}").Bold().AlignRight();
                c.Item().Text($"گیرندگان: {(string.IsNullOrWhiteSpace(data.Receivers) ? "—" : data.Receivers)}").AlignRight();
                c.Item().Text($"ایجادکننده: {(string.IsNullOrWhiteSpace(data.Creator) ? "—" : data.Creator)}").AlignRight();
                c.Item().PaddingVertical(5).LineHorizontal(0.7f).LineColor("#999999");
                // Never ShowEntire/Height/ClampLines the body: it must flow to subsequent pages.
                c.Item().Text(body).AlignRight();
                if (withHamesh && data.Hamesh.Count > 0)
                {
                    c.Item().PaddingTop(8).Text("هامش:").Bold().AlignRight();
                    foreach (var row in data.Hamesh)
                    {
                        var details = new[] { PlainText(row.Action), PlainText(row.Instruction) }
                            .Where(x => !string.IsNullOrWhiteSpace(x));
                        var detail = string.Join(" — ", details);
                        c.Item().Text($"• {row.Receiver}{(detail.Length > 0 ? " — " + detail : "")}").AlignRight();
                    }
                }
                // No automatic closing, signatory section, stamp, or generated footer below internal letters.
            });
        })).GeneratePdf();
    }

    internal static string PlainText(string? html)
    {
        var text = Regex.Replace(html ?? "", @"<\s*br\s*/?\s*>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"</\s*(p|div|li|h[1-6]|tr)\s*>", "\n", RegexOptions.IgnoreCase);
        text = WebUtility.HtmlDecode(Regex.Replace(text, "<[^>]+>", ""));
        return Regex.Replace(text, @"[^\S\r\n]+", " ").Trim();
    }

    private static string PersianDigits(string value) => new(value.Select(c => c is >= '0' and <= '9' ? "۰۱۲۳۴۵۶۷۸۹"[c - '0'] : c).ToArray());

    private static void EnsureFonts(string directory)
    {
        lock (FontLock)
        {
            if (_fontsRegistered) return;
            using var regular = File.OpenRead(Path.Combine(directory, "Vazirmatn-Regular.ttf"));
            using var bold = File.OpenRead(Path.Combine(directory, "Vazirmatn-Bold.ttf"));
            FontManager.RegisterFontWithCustomName("Vazirmatn", regular);
            FontManager.RegisterFontWithCustomName("Vazirmatn-Bold", bold);
            _fontsRegistered = true;
        }
    }
}
