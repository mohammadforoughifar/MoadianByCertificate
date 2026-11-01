using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Inventory.Shared.Dtos;

namespace Inventory.Client.Services;

/// <summary>نتیجهٔ ساختِ متن نامه از روی ایمیل.</summary>
public sealed class EmailLetterContentResult
{
    public string Html { get; set; } = "";
    public int InlineImages { get; set; }
    public int AttachedImages { get; set; }
    public int Skipped { get; set; }
    public bool BodyWasEmpty { get; set; }

    /// <summary>
    /// شناسهٔ پیوست‌هایی که «داخل متن نامه» جاسازی شدند. این‌ها نباید دوباره به
    /// عنوان فایل پیوستِ نامه ثبت شوند؛ وگرنه همان تصویر هم داخل متن دیده می‌شود
    /// و هم به‌شکل فایل، و کاربر حس می‌کند تصویر «به‌صورت فایل» آمده است.
    /// </summary>
    public HashSet<int> EmbeddedIds { get; } = new();
}

/// <summary>
/// ساختِ متنِ نامه از روی یک ایمیل، با همهٔ جزئیات — بدون وابستگی به پاک‌سازی‌کنندهٔ
/// سمت کلاینت (این نسخه خودکفاست و در درخت کدِ قدیمی‌تر هم کار می‌کند).
///
/// مشکلی که حل می‌کند:
///   • تصویرِ درونِ متنِ ایمیل در بدنه با آدرسِ نسبیِ
///     <c>/api/email/attachments/{id}/download</c> نگه داشته می‌شود. این آدرس فقط در
///     صفحهٔ ایمیل کار می‌کند؛ آن‌جا مطلق می‌شود و توکنِ دسترسی هم به آن اضافه می‌شود
///     (تگ img نمی‌تواند هدر Bearer بفرستد). در متنِ نامه همان آدرس به میزبانِ برنامه
///     می‌خورد و بدون توکن ۴۰۱ می‌گیرد ⇒ تصویر دیده نمی‌شد.
///   • بعضی ایمیل‌ها متن ندارند و محتوایشان «فقط یک عکس پیوست» است؛ آن تصویر هم باید
///     داخل متن نامه بیاید، وگرنه متن نامه خالی به‌نظر می‌رسد.
///
/// راه‌حل: تصاویر یک‌بار دانلود و به‌صورت <c>data:image/…;base64,…</c> داخل خودِ متن
/// جاسازی می‌شوند؛ نتیجه «خودکفا» است (نمایش، چاپ و ذخیرهٔ بعدی، بدون توکن).
/// </summary>
public static class EmailLetterContent
{
    private const long MaxImageBytes = 5_000_000;
    private const long MaxTotalBytes = 12_000_000;
    private const int MaxAttachedImages = 10;

    /// <summary>آدرسِ پیوستِ ایمیل — نسبی یا مطلق، با یا بدون توکن/پارامتر.</summary>
    private static readonly Regex AttachmentUrl = new(
        @"(?:https?://[^""'\s<>]+)?/api/email/attachments/(?<id>\d+)/download[^""'\s<>)]*",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CidReference = new(
        @"cid:(?<cid>[^""'\s<>)]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex BrokenImage = new(
        @"<img\b[^>]*\bsrc\s*=\s*""""[^>]*>|<img\b(?![^>]*\bsrc\s*=)[^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly string[] ImageExtensions =
        { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp" };

    public static async Task<EmailLetterContentResult> BuildAsync(EmailMessageDetailDto? email, IEmailClientService? service)
    {
        var result = new EmailLetterContentResult();
        if (email is null || service is null) return result;

        var ctx = new Ctx { Email = email, Service = service };

        // ۱) بدنه: حذف بلوک‌های خطرناک/قالب‌های ایمیل و اسکریپت‌ها تا کادر فرم سیاه نشود
        var html = Clean(email.Body ?? "");

        // ۲) ارجاع‌های cid: که سرور ترمیم نکرده → آدرسِ پیوستِ همان نام فایل
        html = ResolveCidReferences(html, email.Attachments);

        // ۳) تصاویرِ داخل متن را داخل خودِ متن جاسازی کن.
        //    ابتدا همهٔ شناسه‌ها «به‌صورت async» حل می‌شوند، بعد جای‌گذاری انجام می‌شود؛
        //    در Blazor WebAssembly نمی‌توان داخل یک lambda روی Task منتظر ماند
        //    (GetAwaiter().GetResult() در مرورگر قفل می‌کند/استثنا می‌دهد).
        if (html.Contains("/api/email/attachments/", StringComparison.OrdinalIgnoreCase))
        {
            var ids = AttachmentUrl.Matches(html)
                .Select(m => int.TryParse(m.Groups["id"].Value, out var id) ? id : 0)
                .Where(id => id > 0)
                .Distinct()
                .ToList();

            var resolved = new Dictionary<int, string?>();
            foreach (var id in ids) resolved[id] = await ResolveAsync(ctx, id);

            html = AttachmentUrl.Replace(html, m =>
            {
                if (!int.TryParse(m.Groups["id"].Value, out var id)) return m.Value;
                if (resolved.TryGetValue(id, out var uri) && uri != null)
                {
                    ctx.InlineIds.Add(id);
                    result.InlineImages++;
                    result.EmbeddedIds.Add(id);
                    return uri;
                }
                result.Skipped++;
                return ""; // در پاس بعدی به یادداشت تبدیل می‌شود
            });

            if (result.Skipped > 0)
                html = BrokenImage.Replace(html,
                    "<span style=\"color:#64748b;font-size:12px\">[این تصویر درون متن ایمیل قابل انتقال نبود — پیام اصلی را در پست الکترونیک ببینید]</span>");
        }

        // ۴) تصاویرِ پیوستی (ایمیل‌های عکس‌محور) → بخشِ «تصویر پیوست ایمیل» داخل متن
        var section = new StringBuilder();
        var images = email.Attachments
            .Where(a => !ctx.InlineIds.Contains(a.AttachmentId))
            .Where(IsImageAttachment)
            .Take(MaxAttachedImages)
            .ToList();

        foreach (var attachment in images)
        {
            var uri = await ResolveAsync(ctx, attachment.AttachmentId);
            if (uri is null)
            {
                result.Skipped++;
                continue;
            }
            result.AttachedImages++;
            result.EmbeddedIds.Add(attachment.AttachmentId);
            AppendImageSection(section, uri);
        }

        // ۴-ب) تضمین: اگر ایمیل تصویر دارد ولی هیچ تصویری در متن ننشست، همهٔ تصاویر را بیاور
        if (result.AttachedImages == 0 && !html.Contains("<img", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var attachment in images)
            {
                var uri = await ResolveAsync(ctx, attachment.AttachmentId);
                if (uri is null) continue;
                result.AttachedImages++;
                result.EmbeddedIds.Add(attachment.AttachmentId);
                AppendImageSection(section, uri);
            }
        }

        // ۵) بدنهٔ متنی خالی است؟ (ایمیل فقط-عکس)
        var plain = WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]*>", " "));
        result.BodyWasEmpty = string.IsNullOrWhiteSpace(plain.Replace("\u00a0", " "));
        // اگر ایمیل فقط تصویر است، هیچ جملهٔ توضیحی اضافه نمی‌شود تا متن نامه دقیقاً
        // مثل همان ایمیل دریافتی دیده شود؛ یادداشت فقط برای ایمیلِ کاملاً خالی می‌آید.
        if (result.BodyWasEmpty && result.AttachedImages + result.InlineImages == 0)
            html = "<p style=\"color:#64748b;font-size:12px\">بدنهٔ این ایمیل خالی است.</p>" + html;

        result.Html = html + section;
        return result;
    }

    // ------------------------------------------------------------------ کمکی

    private sealed class Ctx
    {
        public EmailMessageDetailDto Email { get; init; } = new();
        public IEmailClientService Service { get; init; } = null!;
        public Dictionary<int, string?> Cache { get; } = new();
        public HashSet<int> InlineIds { get; } = new();
        public long UsedBytes;
    }

    /// <summary>
    /// افزودن تصویر به متن نامه — بدون هیچ برچسب، نام فایل یا جملهٔ توضیحی؛
    /// متن نامه باید «شبیه خودِ ایمیل دریافتی» باشد و کاربر نباید بفهمد تصویر
    /// از یک فایل پیوست آمده است. alt عمداً خالی است و فقط برای دسترس‌پذیری
    /// نامِ کوتاه «تصویر» می‌ماند.
    /// </summary>
    private static void AppendImageSection(StringBuilder sb, string uri)
    {
        sb.Append("<p style=\"margin:8px 0;text-align:center\">")
          .Append("<img src=\"").Append(uri).Append("\" alt=\"تصویر\"")
          .Append(" style=\"max-width:100%;height:auto\" />")
          .Append("</p>");
    }

    private static async Task<string?> ResolveAsync(Ctx ctx, int attachmentId)
    {
        if (ctx.Cache.TryGetValue(attachmentId, out var cached)) return cached;

        var att = ctx.Email.Attachments.FirstOrDefault(a => a.AttachmentId == attachmentId);
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

        var mime = DetectImageMime(att?.ContentType, att?.RealName, data);
        if (mime is null) return ctx.Cache[attachmentId] = null;

        ctx.UsedBytes += data.Length;
        return ctx.Cache[attachmentId] = $"data:{mime};base64,{Convert.ToBase64String(data)}";
    }

    /// <summary>پاک‌سازی سبک: حذف style/script/iframe و رویدادهای inline؛ نگه‌داشتن ساختار و تصاویر.</summary>
    private static string Clean(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return "";
        var s = html;

        s = Regex.Replace(s, @"<\s*(script|style|iframe|object|embed|svg|math|link|meta)\b[^>]*>.*?<\s*/\s*\1\s*>", "",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        s = Regex.Replace(s, @"<\s*/?\s*(script|style|iframe|object|embed|svg|math|link|meta)\b[^>]*>", "",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        s = Regex.Replace(s, @"<!--.*?-->", "", RegexOptions.Singleline);
        // رویدادهای inline (onclick=…) و javascript: حذف شوند
        s = Regex.Replace(s, @"\son[a-z]+\s*=\s*(""[^""]*""|'[^']*'|[^\s>]+)", "", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"(href|src)\s*=\s*(['""])\s*javascript:[^'""]*\2", "$1=\"#\"", RegexOptions.IgnoreCase);
        // متنِ سفید روی کاغذ سفید نامه ناپیدا است
        s = Regex.Replace(s, @"color\s*:\s*(#fff|#ffffff|white|rgb\(\s*255\s*,\s*255\s*,\s*255\s*\))",
            "color:#1f2937", RegexOptions.IgnoreCase);
        return s;
    }

    private static string ResolveCidReferences(string html, List<EmailAttachmentDto> attachments)
    {
        if (string.IsNullOrEmpty(html) || !html.Contains("cid:", StringComparison.OrdinalIgnoreCase)
            || attachments.Count == 0) return html;

        foreach (Match match in CidReference.Matches(html))
        {
            var cid = match.Groups["cid"].Value;
            if (string.IsNullOrWhiteSpace(cid)) continue;

            var decoded = SafeUnescape(cid);
            var hit = attachments.FirstOrDefault(a => !string.IsNullOrWhiteSpace(a.RealName)
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

    private static bool IsImageAttachment(EmailAttachmentDto attachment)
    {
        if (!string.IsNullOrWhiteSpace(attachment.ContentType)
            && attachment.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            return true;

        var name = attachment.RealName ?? "";
        return ImageExtensions.Any(ext => name.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
    }

    private static string? DetectImageMime(string? contentType, string? fileName, byte[] data)
    {
        var mime = (contentType ?? "").Split(';')[0].Trim().ToLowerInvariant();
        switch (mime)
        {
            case "image/png": return "image/png";
            case "image/jpeg":
            case "image/jpg":
            case "image/pjpeg": return "image/jpeg";
            case "image/gif": return "image/gif";
            case "image/webp": return "image/webp";
            case "image/bmp":
            case "image/x-ms-bmp": return "image/bmp";
        }

        var name = (fileName ?? "").ToLowerInvariant();
        if (name.EndsWith(".png")) return "image/png";
        if (name.EndsWith(".jpg") || name.EndsWith(".jpeg")) return "image/jpeg";
        if (name.EndsWith(".gif")) return "image/gif";
        if (name.EndsWith(".webp")) return "image/webp";
        if (name.EndsWith(".bmp")) return "image/bmp";

        if (data.Length < 12) return null;
        if (data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47) return "image/png";
        if (data[0] == 0x42 && data[1] == 0x4D) return "image/bmp";
        if (data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF) return "image/jpeg";
        if (data[0] == 0x47 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x38) return "image/gif";
        if (data[0] == 0x52 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x46 &&
            data[8] == 0x57 && data[9] == 0x45 && data[10] == 0x42 && data[11] == 0x50) return "image/webp";
        return null;
    }
}
