using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Components.Forms;

namespace Inventory.Client.Services;

/// <summary>نتیجهٔ برداشتِ پیوست‌های ایمیل برای انتقال به نامه.</summary>
public sealed class EmailAttachmentTransfer
{
    /// <summary>فایل‌های آمادهٔ آپلود به‌عنوان پیوستِ نامه.</summary>
    public List<IBrowserFile> Files { get; } = new();

    /// <summary>نامِ پیوست‌هایی که از سقف مجاز عبور کردند.</summary>
    public List<string> TooBig { get; } = new();

    /// <summary>نامِ پیوست‌هایی که دریافتشان از سرور ایمیل ناموفق بود.</summary>
    public List<string> Failed { get; } = new();

    public long TotalBytes { get; set; }

    public int Count => Files.Count;
}

/// <summary>
/// برداشتِ «همهٔ» پیوست‌های یک ایمیل — با هر فرمتی — برای انتقال به پیوست‌های نامه.
///
/// چرا این کلاس وجود دارد: هم «تبدیل به نامهٔ وارده» و هم «تبدیل به نامهٔ داخلی» باید
/// دقیقاً یک رفتار داشته باشند؛ قبلاً منطق در هر صفحه جدا نوشته شده بود و با هم
/// تفاوت داشت (یکی تصاویر را رد می‌کرد، دیگری فقط پیوست‌های غیر inline را می‌برد).
///
/// قواعد:
///   • هیچ فیلتری روی نوع/پسوند فایل نیست (تصویر، PDF، Word، Excel، PowerPoint،
///     ZIP/RAR، صدا، ویدیو، متن و هر پسوند دیگر).
///   • تصویرِ داخل متنِ ایمیل هم علاوه بر دیده‌شدن در متن، به‌عنوان فایل ضمیمه می‌آید.
///   • کنترل حجم دو بار انجام می‌شود: با Sizeِ اعلامی «پیش از دانلود» و با طول واقعی
///     «پس از دانلود» — مطابق سقف سرور (پیش‌فرض ۲۰ مگابایت هر فایل/۱۰۰ مگابایت مجموع).
///   • نام یا نوعِ نامعتبر باعث شکست آپلود نمی‌شود؛ نام جایگزین و نوع از پسوند استنباط
///     می‌شود.
/// </summary>
public static class EmailAttachmentTransferService
{
    public const long DefaultMaxFileBytes = 20L * 1024 * 1024;
    public const long DefaultMaxTotalBytes = 100L * 1024 * 1024;

    public static async Task<EmailAttachmentTransfer> CollectAsync(
        IEnumerable<EmailAttachmentDto>? attachments,
        IEmailClientService? service,
        long maxFileBytes = DefaultMaxFileBytes,
        long maxTotalBytes = DefaultMaxTotalBytes)
    {
        var result = new EmailAttachmentTransfer();
        if (attachments is null || service is null) return result;

        foreach (var attachment in attachments)
        {
            var displayName = string.IsNullOrWhiteSpace(attachment.RealName) ? "پیوست" : attachment.RealName;

            // ۱) کنترل پیش از دانلود (با حجم اعلامی)
            if (attachment.Size > maxFileBytes || result.TotalBytes + attachment.Size > maxTotalBytes)
            {
                result.TooBig.Add(displayName);
                continue;
            }

            // ۲) دانلود واقعی از سرور ایمیل
            byte[] data;
            string name;
            try { (data, name) = await service.DownloadAttachmentAsync(attachment.AttachmentId); }
            catch { result.Failed.Add(displayName); continue; }
            if (data is not { Length: > 0 }) { result.Failed.Add(displayName); continue; }

            // ۳) کنترل حجم پس از دانلود (Size اعلامی ممکن است دقیق نباشد)
            if (data.Length > maxFileBytes || result.TotalBytes + data.Length > maxTotalBytes)
            {
                result.TooBig.Add(displayName);
                continue;
            }

            result.TotalBytes += data.Length;
            if (string.IsNullOrWhiteSpace(name)) name = displayName;
            result.Files.Add(new MemoryBrowserFile(name, NormalizeContentType(attachment.ContentType, name), data));
        }

        return result;
    }

    /// <summary>
    /// نوع MIME را برای ارسال امن آماده می‌کند: نوعِ اعلامی اگر معتبر باشد حفظ می‌شود،
    /// وگرنه از پسوند فایل استنباط و در نهایت application/octet-stream می‌شود.
    /// این‌طور ساخت StreamContent هیچ‌وقت با MediaType نامعتبر خطا نمی‌دهد.
    /// </summary>
    public static string NormalizeContentType(string? contentType, string fileName)
    {
        var raw = (contentType ?? "").Split(';')[0].Trim();
        if (raw.Length > 0 && raw.Contains('/') && raw.All(c => c > 32 && c < 127))
            return raw;

        var ext = (System.IO.Path.GetExtension(fileName) ?? "").ToLowerInvariant();
        return ext switch
        {
            ".pdf" => "application/pdf",
            ".doc" => "application/msword",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xls" => "application/vnd.ms-excel",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".ppt" => "application/vnd.ms-powerpoint",
            ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            ".txt" => "text/plain",
            ".csv" => "text/csv",
            ".zip" => "application/zip",
            ".rar" => "application/vnd.rar",
            ".7z" => "application/x-7z-compressed",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            ".heic" => "image/heic",
            ".mp3" => "audio/mpeg",
            ".wav" => "audio/wav",
            ".mp4" => "video/mp4",
            ".mov" => "video/quicktime",
            _ => "application/octet-stream"
        };
    }
}
