using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Inventory.Shared.Dtos;
using Inventory.Shared.Security;

namespace Inventory.Client.Services;

/// <summary>نتیجهٔ ساختِ «متن نامه» از روی یک ایمیل.</summary>
public sealed class EmailLetterBody
{
    /// <summary>HTML آمادهٔ ریختن در ادیتور نامه.</summary>
    public string Html { get; set; } = "";

    /// <summary>تصاویری که از «داخل متن ایمیل» جاسازی شدند.</summary>
    public int InlineImages { get; set; }

    /// <summary>تصاویرِ پیوستی که به‌عنوان بخش تصویر داخل متن نامه آمدند.</summary>
    public int AttachedImages { get; set; }

    /// <summary>تصاویری که منتقل نشدند (حجم/قالب/خطای دانلود).</summary>
    public int Skipped { get; set; }

    /// <summary>نامِ تصاویری که منتقل نشدند (برای پیام به کاربر).</summary>
    public List<string> SkippedNames { get; } = new();

    /// <summary>بدنهٔ متنیِ ایمیل خالی بوده و محتوا فقط تصویر/پیوست است.</summary>
    public bool BodyWasEmpty { get; set; }

    /// <summary>
    /// شناسهٔ پیوست‌هایی که «داخل متن نامه» جاسازی شدند؛ این‌ها نباید دوباره به‌عنوان
    /// فایل پیوستِ نامه ثبت شوند (وگرنه تصویر هم در متن دیده می‌شود و هم به‌شکل فایل).
    /// </summary>
    public HashSet<int> EmbeddedIds { get; } = new();
}

/// <summary>
/// ساختِ کاملِ متنِ نامه از روی یک ایمیل — «همهٔ جزئیاتِ متن ایمیل داخل متن نامه».
///
/// چرا این کلاس لازم شد:
///   • بدنهٔ ایمیل تصاویرِ درون‌متن را با آدرسِ نسبیِ
///     <c>/api/email/attachments/{id}/download</c> نگه می‌دارد. این آدرس فقط در
///     «صفحهٔ ایمیل» کار می‌کند؛ آن‌جا مطلق می‌شود و توکنِ دسترسی هم به آن اضافه
///     می‌شود (تگ img نمی‌تواند هدر Bearer بفرستد). در متن نامه همان آدرس به
///     میزبانِ برنامه (نه سرور API) می‌خورد و بدون توکن هم ۴۰۱ می‌گیرد ⇒ تصویر
///     دیده نمی‌شد.
///   • بعضی ایمیل‌ها هیچ متنِ HTML ندارند و محتوایشان «فقط یک عکس پیوست» است
///     (مثل عکسِ ارسالی از گوشی). بدنهٔ چنین ایمیلی خالی/یک جملهٔ کوتاه است و
///     تصویر در بخش «پیوست» می‌ماند؛ در نتیجه متن نامه خالی به‌نظر می‌رسید.
///
/// راه‌حل: تصویرها یک‌بار دانلود و به‌صورت <c>data:image/…;base64,…</c> داخل خودِ
/// متن جاسازی می‌شوند. نتیجه «خودکفا» است: در فرم ثبت، در نمایش نامه، در چاپ و
/// در ذخیره‌های بعدی بدون توکن و بدون وابستگی به عمرِ توکن کار می‌کند.
/// </summary>
public static class EmailLetterBodyBuilder
{
    /// <summary>سقف حجم هر تصویر (۵ مگابایت).</summary>
    private const long MaxImageBytes = 5_000_000;

    /// <summary>سقف مجموع تصاویرِ جاسازی‌شده در یک نامه (۱۲ مگابایت).</summary>
    private const long MaxTotalBytes = 12_000_000;

    /// <summary>بیشترین تعداد تصویرِ پیوستی که داخل متن نامه می‌آید.</summary>
    private const int MaxAttachedImages = 10;

    /// <summary>
    /// آدرسِ پیوستِ ایمیل — با میزبان یا بدون میزبان، و با هر پارامترِ اضافه
    /// (مثل <c>?access_token=…</c>). کل مقدارِ آدرس تا پایانِ صفت خوانده می‌شود.
    /// </summary>
    private static readonly Regex AttachmentUrl = new(
        @"(?:https?://[^""'\s<>]+)?/api/email/attachments/(?<id>\d+)/download[^""'\s<>)]*",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>ارجاعِ <c>cid:</c> که بعضی سرورها بدون ترمیم در بدنه باقی می‌گذارند.</summary>
    private static readonly Regex CidReference = new(
        @"cid:(?<cid>[^""'\s<>)]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>تگ تصویری که آدرسش قابل انتقال نبوده (src خالی شده).</summary>
    private static readonly Regex BrokenImage = new(
        @"<img\b[^>]*\bsrc\s*=\s*""""[^>]*>|<img\b(?![^>]*\bsrc\s*=)[^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly string[] ImageExtensions =
        { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".tif", ".tiff", ".heic", ".heif" };

    /// <summary>
    /// ساختِ متن نامه از روی ایمیل: بدنهٔ پاک‌سازی‌شده + تصاویرِ درون‌متن +
    /// تصاویرِ پیوستی (به‌صورت بخشِ تصویر در متن).
    /// </summary>
    public static async Task<EmailLetterBody> BuildAsync(EmailMessageDetailDto? email, IEmailClientService? service)
    {
        var result = new EmailLetterBody();
        if (email is null || service is null) return result;

        var ctx = new EmbedContext { Email = email, Service = service };

        // ۱) پاک‌سازی بدنه (حذف <style>، اگرِ ایمیل‌های تبلیغاتی، متنِ سفید و…)
        var html = RichLetterHtml.Sanitize(email.Body);

        // ۲) ارجاع‌های cid: که سرور ترمیم نکرده را با «نام فایل پیوست» به آدرس پیوست تبدیل کن
        html = ResolveCidReferences(html, email.Attachments);

        // ۳) تصاویری که داخل متن به پیوست ارجاع داده‌اند را داخل خودِ متن جاسازی کن
        if (html.Contains("/api/email/attachments/", StringComparison.OrdinalIgnoreCase))
        {
            var ids = AttachmentUrl.Matches(html)
                .Select(m => int.TryParse(m.Groups["id"].Value, out var id) ? id : 0)
                .Where(id => id > 0)
                .Distinct()
                .ToList();

            var dataUris = new Dictionary<int, string?>();
            foreach (var id in ids) dataUris[id] = await ResolveAsync(ctx, id);

            html = AttachmentUrl.Replace(html, m =>
            {
                if (!int.TryParse(m.Groups["id"].Value, out var id)) return m.Value;
                if (dataUris.TryGetValue(id, out var uri) && uri != null)
                {
                    ctx.InlineIds.Add(id);
                    result.InlineImages++;
                    result.EmbeddedIds.Add(id);
                    return uri;
                }
                result.Skipped++;
                var name = email.Attachments.FirstOrDefault(a => a.AttachmentId == id)?.RealName;
                if (!string.IsNullOrWhiteSpace(name)) result.SkippedNames.Add(name!);
                // src خالی می‌ماند تا در پاس بعدی تگ به یادداشت تبدیل شود
                return "";
            });
        }

        // تصویرِ شکسته (آدرسی که قابل انتقال نبود) → یادداشت خوانا، نه تصویر خالی
        if (result.Skipped > 0)
            html = BrokenImage.Replace(html,
                "<span style=\"color:#64748b;font-size:12px\">[این تصویر درون متن ایمیل قابل انتقال نبود — پیام اصلی را در پست الکترونیک ببینید]</span>");

        // ۴) تصاویرِ پیوستی: ایمیل‌های «عکس‌محور» بدنهٔ متنی ندارند و تمام محتوا در
        //    پیوست است. این تصاویر به‌صورت بخشِ «تصویر پیوست ایمیل» داخل متن نامه می‌آیند.
        var imageAttachments = email.Attachments
            .Where(a => !ctx.InlineIds.Contains(a.AttachmentId))
            .Where(IsImageAttachment)
            .Take(MaxAttachedImages)
            .ToList();

        var section = new StringBuilder();
        foreach (var attachment in imageAttachments)
        {
            var uri = await ResolveAsync(ctx, attachment.AttachmentId);
            if (uri is null)
            {
                result.Skipped++;
                if (!string.IsNullOrWhiteSpace(attachment.RealName)) result.SkippedNames.Add(attachment.RealName);
                continue;
            }

            result.AttachedImages++;
            result.EmbeddedIds.Add(attachment.AttachmentId);
            AppendImage(section, uri);
        }

        // ۴-ب) تضمین نهایی: اگر ایمیل تصویر دارد ولی هیچ تصویری داخل متن ننشست
        //      (مثلاً همهٔ ارجاع‌های بدنه ترمیم نشده بودند)، همهٔ تصاویرِ پیوستی
        //      دوباره و این بار «بدون توجه به inline بودن» اضافه می‌شوند تا متن
        //      نامه هرگز بدون تصویر نماند.
        if (result.AttachedImages == 0 && !html.Contains("<img", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var attachment in email.Attachments.Where(IsImageAttachment)
                         .Take(MaxAttachedImages - result.AttachedImages).ToList())
            {
                var uri = await ResolveAsync(ctx, attachment.AttachmentId);
                if (uri is null) continue;

                result.AttachedImages++;
                result.EmbeddedIds.Add(attachment.AttachmentId);
                AppendImage(section, uri);
            }
        }

        // ۵) بدنهٔ متنی خالی است؟ (ایمیل عکس‌محور یا پیامِ بدون متن)
        var plain = Regex.Replace(html ?? "", "<[^>]*>", " ");
        plain = WebUtility.HtmlDecode(plain);
        result.BodyWasEmpty = string.IsNullOrWhiteSpace(plain.Replace("\u00a0", " "));

        // اگر ایمیل فقط تصویر است هیچ جملهٔ توضیحی اضافه نمی‌شود تا متن نامه دقیقاً
        // مثل همان ایمیل دیده شود؛ یادداشت فقط برای ایمیلِ کاملاً خالی می‌آید.
        if (result.BodyWasEmpty && result.AttachedImages + result.InlineImages == 0)
            html = "<p style=\"color:#64748b;font-size:12px\">بدنهٔ این ایمیل خالی است.</p>" + html;

        result.Html = html + section.ToString();
        return result;
    }

    // ---------------------------------------------------------------- کمکی‌ها

    /// <summary>
    /// درج تصویر در متن نامه — بدون برچسب، نام فایل یا جملهٔ توضیحی؛ متن نامه باید
    /// «مثل خودِ ایمیل دریافتی» دیده شود و کاربر نباید بفهمد تصویر از فایل پیوست آمده.
    /// </summary>
    private static void AppendImage(StringBuilder sb, string uri)
    {
        sb.Append("<p style=\"margin:8px 0;text-align:center\">")
          .Append("<img src=\"").Append(uri).Append("\" alt=\"تصویر\"")
          .Append(" style=\"max-width:100%;height:auto\" />")
          .Append("</p>");
    }

    private sealed class EmbedContext
    {
        public EmailMessageDetailDto Email { get; init; } = new();
        public IEmailClientService Service { get; init; } = null!;
        public Dictionary<int, string?> Cache { get; } = new();
        public HashSet<int> InlineIds { get; } = new();
        public long UsedBytes;
    }

    /// <summary>data URI یک پیوست (با کش) — در صورت نامناسب بودن، null.</summary>
    private static async Task<string?> ResolveAsync(EmbedContext ctx, int attachmentId)
    {
        if (ctx.Cache.TryGetValue(attachmentId, out var cached)) return cached;

        var att = ctx.Email.Attachments.FirstOrDefault(a => a.AttachmentId == attachmentId);

        // سقف‌ها: پیش از دانلود با حجمِ اعلامی، پس از دانلود با حجمِ واقعی
        if (att != null && att.Size > MaxImageBytes) return ctx.Cache[attachmentId] = null;
        if (ctx.UsedBytes >= MaxTotalBytes) return ctx.Cache[attachmentId] = null;

        byte[] data;
        try
        {
            var (bytes, _) = await ctx.Service.DownloadAttachmentAsync(attachmentId);
            data = bytes ?? Array.Empty<byte>();
        }
        catch { return ctx.Cache[attachmentId] = null; }

        if (data.Length == 0) return ctx.Cache[attachmentId] = null;
        if (data.Length > MaxImageBytes || ctx.UsedBytes + data.Length > MaxTotalBytes)
            return ctx.Cache[attachmentId] = null;

        // فقط قالب‌هایی که پاک‌سازی‌کنندهٔ متن نامه (RichLetterHtml) می‌پذیرد
        var mime = DetectImageMime(att?.ContentType, att?.RealName, data);
        if (mime is null) return ctx.Cache[attachmentId] = null;

        ctx.UsedBytes += data.Length;
        return ctx.Cache[attachmentId] = $"data:{mime};base64,{Convert.ToBase64String(data)}";
    }

    /// <summary>
    /// ارجاع‌های <c>cid:نام‌فایل</c> را به آدرسِ پیوستِ همان فایل تبدیل می‌کند تا در
    /// مرحلهٔ بعد جاسازی شوند. سرور این کار را هنگام Sync انجام می‌دهد، ولی ایمیل‌های
    /// قدیمی یا Content-IDهای غیرمتعارف ممکن است ترمیم نشده باشند.
    /// </summary>
    private static string ResolveCidReferences(string html, List<EmailAttachmentDto> attachments)
    {
        if (string.IsNullOrEmpty(html) || !html.Contains("cid:", StringComparison.OrdinalIgnoreCase))
            return html;
        if (attachments.Count == 0) return html;

        foreach (Match match in CidReference.Matches(html))
        {
            var cid = match.Groups["cid"].Value;
            if (string.IsNullOrWhiteSpace(cid)) continue;

            var decoded = SafeUnescape(cid);
            var hit = attachments.FirstOrDefault(a =>
                    !string.IsNullOrWhiteSpace(a.RealName)
                    && (string.Equals(a.RealName, cid, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(a.RealName, decoded, StringComparison.OrdinalIgnoreCase)
                        || cid.StartsWith(a.RealName, StringComparison.OrdinalIgnoreCase)
                        || decoded.StartsWith(a.RealName, StringComparison.OrdinalIgnoreCase)));

            if (hit is null) continue;
            html = html.Replace($"cid:{cid}", $"/api/email/attachments/{hit.AttachmentId}/download",
                StringComparison.OrdinalIgnoreCase);
        }
        return html;
    }

    private static string SafeUnescape(string value)
    {
        var trimmed = value.Replace("&lt;", "").Replace("&gt;", "").Trim('<', '>');
        try { return Uri.UnescapeDataString(trimmed); }
        catch { return trimmed; }
    }

    /// <summary>آیا این پیوست یک تصویر است؟ (از ContentType یا پسوندِ نام فایل)</summary>
    private static bool IsImageAttachment(EmailAttachmentDto attachment)
    {
        if (!string.IsNullOrWhiteSpace(attachment.ContentType)
            && attachment.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            return true;

        var name = attachment.RealName ?? "";
        return ImageExtensions.Any(ext => name.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// نوعِ MIME تصویر: اول از ContentType، بعد از پسوندِ نام فایل و در آخر از
    /// «امضای فایل» (چند بایت اول). خروجی فقط از قالب‌های مجازِ پاک‌سازی‌کننده است.
    /// </summary>
    private static string? DetectImageMime(string? contentType, string? fileName, byte[] data)
    {
        var byHeader = NormalizeMime(contentType);
        if (byHeader != null) return byHeader;

        var name = (fileName ?? "").ToLowerInvariant();
        if (name.EndsWith(".png")) return "image/png";
        if (name.EndsWith(".jpg") || name.EndsWith(".jpeg")) return "image/jpeg";
        if (name.EndsWith(".gif")) return "image/gif";
        if (name.EndsWith(".webp")) return "image/webp";
        if (name.EndsWith(".bmp")) return "image/bmp";

        return SniffMime(data);
    }

    private static string? NormalizeMime(string? contentType)
    {
        var mime = (contentType ?? "").Split(';')[0].Trim().ToLowerInvariant();
        return mime switch
        {
            "image/png" => "image/png",
            "image/jpeg" or "image/jpg" or "image/pjpeg" => "image/jpeg",
            "image/gif" => "image/gif",
            "image/webp" => "image/webp",
            "image/bmp" or "image/x-ms-bmp" => "image/bmp",
            _ => null
        };
    }

    private static string? SniffMime(byte[] data)
    {
        if (data.Length < 12) return null;
        // PNG: 89 50 4E 47
        if (data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47) return "image/png";
        // BMP: "BM"
        if (data[0] == 0x42 && data[1] == 0x4D) return "image/bmp";
        // JPEG: FF D8 FF
        if (data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF) return "image/jpeg";
        // GIF: GIF8
        if (data[0] == 0x47 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x38) return "image/gif";
        // WEBP: RIFF....WEBP
        if (data[0] == 0x52 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x46 &&
            data[8] == 0x57 && data[9] == 0x45 && data[10] == 0x42 && data[11] == 0x50) return "image/webp";
        return null;
    }
}
