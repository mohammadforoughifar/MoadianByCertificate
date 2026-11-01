using Microsoft.AspNetCore.Hosting;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using QRCoder;
using System.Drawing;
using SkiaSharp;
using SkiaSharp.HarfBuzz;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;

namespace Inventory.Api.Services.Watermark;

/// <summary>
/// ==================== واترمارک سمت سرور (سوختن روی خود فایل) ====================
/// برخلاف پوشش CSS سمت کلاینت که با عنصر DOM روی صفحه کشیده می‌شد و در دانلود/کپیِ
/// بایت فایل حذف می‌شد، این سرویس نام کاربر + زمان مشاهده را به‌صورت کاشی مورب روی
/// پیکسل‌های خود تصویر یا روی صفحات PDF می‌نشاند و بایتِ جدید برمی‌گرداند.
///
///   • تصاویر PNG/JPEG/WEBP/BMP/GIF  → با SkiaSharp + شکل‌دهی HarfBuzz (فونت وزیرمتن)
///   • TIFF/HEIC                     → ابتدا به PNG تبدیل و سپس واترمارک
///   • PDF                           → اسپرایت شفاف واترمارک روی همه صفحات با PDFsharp
///
/// فایل اصلی در حافظه دست‌نخورده می‌ماند؛ این سرویس فقط برای پاسخ Preview/Download
/// یک نسخهٔ «حک‌شده» تولید می‌کند. هر درخواست، زمان/نام بینندهٔ همان لحظه را دارد.
/// </summary>
public interface IServerWatermarkService
{
    /// <summary>آیا این نوع فایل قابلیت حک‌کردن واترمارک روی بایت‌هایش را دارد؟</summary>
    bool CanStamp(string contentType, string fileName);

    /// <summary>
    /// اگر فایل قابل حک باشد نسخهٔ واترمارک‌شده برمی‌گرداند در غیر این صورت null.
    /// </summary>
    WatermarkStampResult? Stamp(byte[] source, string contentType, string fileName, string viewerLine);

    /// <summary>نسخه با QR اصالت — واترمارک + QR گوشه صفحه برای ردیابی چاپ.</summary>
    WatermarkStampResult? StampWithQr(byte[] source, string contentType, string fileName, string viewerLine, string qrPayload);
}

public sealed class WatermarkStampResult
{
    public required byte[] Bytes { get; init; }
    public required string ContentType { get; init; }
    public required string FileName { get; init; }
}

public class ServerWatermarkService : IServerWatermarkService
{
    private readonly IWebHostEnvironment _env;

    private static SKTypeface? _typeface;
    private static readonly object TypefaceLock = new();

    // رنگ واترمارک (آبی سرمه‌ای هم‌خانواده با پوشش قبلی) — alpha روی ۲۵۵
    private const byte WatermarkAlpha = 46;      // ≈ ۱۸٪
    private static readonly SKColor WatermarkColor = new(30, 64, 175, WatermarkAlpha);

    public ServerWatermarkService(IWebHostEnvironment env)
    {
        _env = env;
    }

    private static readonly HashSet<string> RasterExts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".jfif", ".gif", ".webp", ".bmp",
        ".tif", ".tiff", ".heic", ".heif"
    };

    private static readonly HashSet<string> ConvertFirstExts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".tif", ".tiff", ".heic", ".heif"
    };

    private static readonly HashSet<string> JpegExts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".jfif"
    };

    public bool CanStamp(string contentType, string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return false;
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        if (ext == ".pdf" || (contentType ?? "").Contains("pdf", StringComparison.OrdinalIgnoreCase))
            return true;
        if (RasterExts.Contains(ext)) return true;
        // نوع محتوای image/ ولی پسوند غیراستاندارد
        if ((contentType ?? "").StartsWith("image/", StringComparison.OrdinalIgnoreCase)
            && !(contentType ?? "").Contains("svg", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    public WatermarkStampResult? Stamp(byte[] source, string contentType, string fileName, string viewerLine)
    {
        if (source is not { Length: > 0 } || string.IsNullOrWhiteSpace(viewerLine)) return null;
        try
        {
            var ext = Path.GetExtension(fileName).ToLowerInvariant();
            if (ext == ".pdf" || (contentType ?? "").Contains("pdf", StringComparison.OrdinalIgnoreCase))
                return StampPdf(source, fileName, viewerLine);
            return StampRaster(source, ext, fileName, viewerLine);
        }
        catch
        {
            // هر خطای غیرمنتظره = عدم حک (اندپوینت نسخه اصلی را برمی‌گرداند)
            return null;
        }
    }

    // ================================================================
    // تصاویر
    // ================================================================

    private WatermarkStampResult? StampRaster(byte[] source, string ext, string fileName, string viewerLine)
    {
        // TIFF/HEIC: ابتدا با ImageSharp به PNG تبدیل (همان مسیر تبدیل پیش‌نمایش)
        byte[] decodedBytes = source;
        if (ConvertFirstExts.Contains(ext))
        {
            var png = ToPngViaImageSharp(source);
            if (png is null) return null;
            decodedBytes = png;
        }

        using var original = SKBitmap.Decode(decodedBytes);
        if (original is null || original.Width <= 0 || original.Height <= 0) return null;
        int w = original.Width, h = original.Height;

        // بوم سفید — پس از حک، خروجی همیشه مات و یکدست است (بدون مشکل الفایِ پیش‌ضرب)
        using var canvasBmp = new SKBitmap(w, h);
        using (var canvas = new SKCanvas(canvasBmp))
        {
            canvas.Clear(SKColors.White);
            using var srcImage = SKImage.FromBitmap(original);
            canvas.DrawImage(srcImage, new SKRect(0, 0, w, h));
            DrawTiledWatermark(canvas, w, h, viewerLine, WatermarkColor);
        }

        bool asJpeg = JpegExts.Contains(ext);
        string outExt;
        byte[]? outBytes;
        if (asJpeg)
        {
            outExt = ".jpg";
            outBytes = Encode(canvasBmp, SKEncodedImageFormat.Jpeg, 90);
            if (outBytes is null) return null;
            return new WatermarkStampResult
            {
                Bytes = outBytes,
                ContentType = "image/jpeg",
                FileName = Path.ChangeExtension(fileName, outExt)
            };
        }

        outExt = ".png";
        outBytes = Encode(canvasBmp, SKEncodedImageFormat.Png, 95);
        if (outBytes is null) return null;
        return new WatermarkStampResult
        {
            Bytes = outBytes,
            ContentType = "image/png",
            FileName = Path.ChangeExtension(fileName, outExt)
        };
    }

    // ================================================================
    // PDF — اسپرایت شفاف واترمارک روی همه صفحات
    // ================================================================

    private WatermarkStampResult? StampPdf(byte[] source, string fileName, string viewerLine)
    {
        using var inMs = new MemoryStream(source, 0, source.Length, true, true);
        using var doc = PdfReader.Open(inMs, PdfDocumentOpenMode.Modify);

        // اسپرایت‌ها بر اساس ابعاد صفحه کش می‌شوند و بین صفحات هم‌اندازه مشترک‌اند
        var spriteCache = new Dictionary<(int W, int H), byte[]>();

        foreach (var page in doc.Pages)
        {
            double pw = page.Width.Point, ph = page.Height.Point;
            if (pw <= 1 || ph <= 1) continue;

            // وضوح ~180dpi تا روی چاپ هم خوانا بماند
            int sw = Math.Max(64, (int)Math.Round(pw * 2.2));
            int sh = Math.Max(64, (int)Math.Round(ph * 2.2));

            if (!spriteCache.TryGetValue((sw, sh), out var spritePng))
            {
                using var sprite = BuildSprite(sw, sh, viewerLine);
                spritePng = Encode(sprite, SKEncodedImageFormat.Png, 95) ?? Array.Empty<byte>();
                spriteCache[(sw, sh)] = spritePng;
            }
            if (spritePng.Length == 0) continue;

            using var imgMs = new MemoryStream(spritePng, 0, spritePng.Length, true, true);
            using var ximg = XImage.FromStream(imgMs); // بافر عمومی: PdfSharp برای خواندن نیاز به GetBuffer دارد
            using (var gfx = XGraphics.FromPdfPage(page))
            {
                gfx.DrawImage(ximg, new XRect(0, 0, pw, ph));
            }
        }

        using var outMs = new MemoryStream();
        doc.Save(outMs, false);
        return new WatermarkStampResult
        {
            Bytes = outMs.ToArray(),
            ContentType = "application/pdf",
            FileName = fileName
        };
    }

    // ================================================================
    // رسم کاشی مورب
    // ================================================================

    private void DrawTiledWatermark(SKCanvas canvas, int w, int h, string viewerLine, SKColor color)
    {
        var typeface = GetTypeface();
        if (typeface is null) return;

        int fontSize = Math.Clamp((int)Math.Round(Math.Min(w, h) * 0.034), 18, 110);
        using var font = new SKFont(typeface, fontSize);
        var shaped = ShapeLine(typeface, font, viewerLine);
        if (shaped.Blob is null) return;
        using (shaped.Blob)
        {
            float lineW = Math.Max(shaped.Width, 1);
            float lineH = fontSize * 1.9f;
            float diag = MathF.Sqrt(w * w + h * h);

            using var paint = new SKPaint { IsAntialias = true, Color = color };
            canvas.Save();
            canvas.Translate(w / 2f, h / 2f);
            canvas.RotateDegrees(-28);

            bool alt = false;
            for (float y = -diag; y < diag; y += lineH * 1.7f)
            {
                float xStart = alt ? -diag - lineW * 0.75f : -diag;
                for (float x = xStart; x < diag; x += lineW * 1.35f)
                {
                    canvas.DrawText(shaped.Blob, x, y, paint);
                }
                alt = !alt;
            }
            canvas.Restore();
        }
    }

    private SKBitmap BuildSprite(int w, int h, string viewerLine)
    {
        var bmp = new SKBitmap(w, h);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColor.Empty); // شفاف — PDFsharp شفافیت را با SMask حفظ می‌کند
        DrawTiledWatermark(canvas, w, h, viewerLine, WatermarkColor);
        return bmp;
    }

    private static (SKTextBlob? Blob, float Width) ShapeLine(SKTypeface typeface, SKFont font, string line)
    {
        try
        {
            using var paint = new SKPaint { Typeface = typeface, TextSize = font.Size, IsAntialias = true };
            using var shaper = new SKShaper(typeface);
            var res = shaper.Shape(line, paint);
            if (res is null || res.Codepoints.Length == 0) return (null, 0);

            ushort[] glyphs = new ushort[res.Codepoints.Length];
            for (int i = 0; i < glyphs.Length; i++)
                glyphs[i] = (ushort)res.Codepoints[i];

            var builder = new SKTextBlobBuilder();
            builder.AddPositionedRun(glyphs.AsSpan(), font, res.Points.AsSpan());
            var blob = builder.Build();
            return (blob, res.Width);
        }
        catch
        {
            return (null, 0);
        }
    }

    // ================================================================
    // QR + واترمارک — برای چاپ
    // ================================================================
    public WatermarkStampResult? StampWithQr(byte[] source, string contentType, string fileName, string viewerLine, string qrPayload)
    {
        if (string.IsNullOrWhiteSpace(qrPayload)) return Stamp(source, contentType, fileName, viewerLine);
        try
        {
            var ext = Path.GetExtension(fileName).ToLowerInvariant();
            if (ext == ".pdf" || (contentType ?? "").Contains("pdf", StringComparison.OrdinalIgnoreCase))
                return StampPdfWithQr(source, fileName, viewerLine, qrPayload);
            return StampRasterWithQr(source, ext, fileName, viewerLine, qrPayload);
        }
        catch { return Stamp(source, contentType, fileName, viewerLine); }
    }

    private WatermarkStampResult? StampRasterWithQr(byte[] source, string ext, string fileName, string viewerLine, string qrPayload)
    {
        byte[] decodedBytes = source;
        if (ConvertFirstExts.Contains(ext))
        {
            var png = ToPngViaImageSharp(source);
            if (png is null) return null;
            decodedBytes = png;
        }
        using var original = SKBitmap.Decode(decodedBytes);
        if (original is null || original.Width <= 0 || original.Height <= 0) return null;
        int w = original.Width, h = original.Height;
        using var canvasBmp = new SKBitmap(w, h);
        using (var canvas = new SKCanvas(canvasBmp))
        {
            canvas.Clear(SKColors.White);
            using var srcImage = SKImage.FromBitmap(original);
            canvas.DrawImage(srcImage, new SKRect(0, 0, w, h));
            DrawTiledWatermark(canvas, w, h, viewerLine, WatermarkColor);
            DrawQrCorner(canvas, w, h, qrPayload, viewerLine);
        }
        bool asJpeg = JpegExts.Contains(ext);
        string outExt; byte[]? outBytes;
        if (asJpeg)
        {
            outExt = ".jpg"; outBytes = Encode(canvasBmp, SKEncodedImageFormat.Jpeg, 90);
            if (outBytes is null) return null;
            return new WatermarkStampResult { Bytes = outBytes, ContentType = "image/jpeg", FileName = Path.ChangeExtension(fileName, outExt) };
        }
        outExt = ".png"; outBytes = Encode(canvasBmp, SKEncodedImageFormat.Png, 95);
        if (outBytes is null) return null;
        return new WatermarkStampResult { Bytes = outBytes, ContentType = "image/png", FileName = Path.ChangeExtension(fileName, outExt) };
    }

    private WatermarkStampResult? StampPdfWithQr(byte[] source, string fileName, string viewerLine, string qrPayload)
    {
        using var inMs = new MemoryStream(source, 0, source.Length, true, true);
        using var doc = PdfReader.Open(inMs, PdfDocumentOpenMode.Modify);
        var spriteCache = new Dictionary<(int W, int H), byte[]>();
        SKBitmap? qrBmp = null;
        try { qrBmp = BuildQrBitmap(qrPayload, 260); } catch { }
        foreach (var page in doc.Pages)
        {
            double pw = page.Width.Point, ph = page.Height.Point;
            if (pw <= 1 || ph <= 1) continue;
            int sw = Math.Max(64, (int)Math.Round(pw * 2.2));
            int sh = Math.Max(64, (int)Math.Round(ph * 2.2));
            if (!spriteCache.TryGetValue((sw, sh), out var spritePng))
            {
                using var sprite = BuildSprite(sw, sh, viewerLine);
                // افزودن QR به اسپرایت PDF در گوشه پایین-چپ
                if (qrBmp != null)
                {
                    using var c = new SKCanvas(sprite);
                    DrawQrOnSprite(c, sw, sh, qrBmp, viewerLine);
                }
                spritePng = Encode(sprite, SKEncodedImageFormat.Png, 95) ?? Array.Empty<byte>();
                spriteCache[(sw, sh)] = spritePng;
            }
            if (spritePng.Length == 0) continue;
            using var imgMs = new MemoryStream(spritePng, 0, spritePng.Length, true, true);
            using var ximg = XImage.FromStream(imgMs);
            using (var gfx = XGraphics.FromPdfPage(page)) gfx.DrawImage(ximg, new XRect(0, 0, pw, ph));
        }
        qrBmp?.Dispose();
        using var outMs = new MemoryStream(); doc.Save(outMs, false);
        return new WatermarkStampResult { Bytes = outMs.ToArray(), ContentType = "application/pdf", FileName = fileName };
    }

    private void DrawQrCorner(SKCanvas canvas, int w, int h, string payload, string viewerLine)
    {
        try
        {
            using var qr = BuildQrBitmap(payload, Math.Clamp(Math.Min(w, h) / 6, 110, 220));
            if (qr == null) return;
            int margin = Math.Clamp(Math.Min(w, h) / 45, 8, 18);
            int box = qr.Width + margin * 2 + 6; // حاشیه سفید + قاب
            int x = w - box - margin;
            int y = h - box - margin;
            // پس‌زمینه سفید با گوشه گرد و سایه
            using var bgPaint = new SKPaint { IsAntialias = true, Color = new SKColor(255, 255, 255, 230) };
            canvas.DrawRoundRect(new SKRect(x, y, x + box, y + box), 10, 10, bgPaint);
            using var borderPaint = new SKPaint { IsAntialias = true, Color = new SKColor(15, 23, 42, 18), Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
            canvas.DrawRoundRect(new SKRect(x, y, x + box, y + box), 10, 10, borderPaint);
            canvas.DrawBitmap(qr, new SKRect(x + margin, y + margin, x + margin + qr.Width, y + margin + qr.Height));
            // متن کوچک زیر QR — کد مدرک
            var tf = GetTypeface();
            if (tf != null)
            {
                using var f = new SKFont(tf, Math.Clamp(qr.Width / 14f, 7, 10));
                using var tp = new SKPaint { IsAntialias = true, Color = new SKColor(15, 23, 42, 190) };
                var txt = viewerLine.Length > 42 ? viewerLine[..42] + "…" : viewerLine;
                var blob = ShapeLine(tf, f, txt);
                if (blob.Blob != null) { using (blob.Blob) canvas.DrawText(blob.Blob, x + box / 2f - blob.Width / 2f, y + box - 4, tp); }
            }
        }
        catch { }
    }

    private void DrawQrOnSprite(SKCanvas canvas, int sw, int sh, SKBitmap qr, string viewerLine)
    {
        int margin = Math.Clamp(Math.Min(sw, sh) / 55, 10, 22);
        int size = Math.Clamp(Math.Min(sw, sh) / 7, 120, 200);
        using var scaled = ResizeBitmap(qr, size, size);
        int x = margin + 6;
        int y = sh - size - margin - 18;
        using var bg = new SKPaint { IsAntialias = true, Color = new SKColor(255, 255, 255, 235) };
        canvas.DrawRoundRect(new SKRect(x - 6, y - 6, x + size + 6, y + size + 16), 8, 8, bg);
        canvas.DrawBitmap(scaled, new SKRect(x, y, x + size, y + size));
    }

    private SKBitmap? BuildQrBitmap(string payload, int size)
    {
        try
        {
            using var gen = new QRCodeGenerator();
            var data = gen.CreateQrCode(payload, QRCodeGenerator.ECCLevel.Q);
            using var qr = new PngByteQRCode(data);
            var pngBytes = qr.GetGraphic(Math.Max(3, size / 33), System.Drawing.Color.FromArgb(15,23,42), System.Drawing.Color.White, true);
            var bmp = SKBitmap.Decode(pngBytes);
            if (bmp == null) return null;
            if (bmp.Width != size || bmp.Height != size)
            {
                var resized = ResizeBitmap(bmp, size, size);
                bmp.Dispose();
                return resized;
            }
            return bmp;
        }
        catch { return null; }
    }

    private static SKBitmap ResizeBitmap(SKBitmap src, int w, int h)
    {
        var dst = new SKBitmap(w, h);
        using var canvas = new SKCanvas(dst);
        canvas.Clear(SKColors.Transparent);
        var paint = new SKPaint { IsAntialias = true, FilterQuality = SKFilterQuality.Medium };
        canvas.DrawBitmap(src, new SKRect(0, 0, src.Width, src.Height), new SKRect(0, 0, w, h), paint);
        return dst;
    }

    // ================================================================
    // ابزار
    // ================================================================

    private static byte[]? Encode(SKBitmap bmp, SKEncodedImageFormat format, int quality)
    {
        using var image = SKImage.FromBitmap(bmp);
        using var data = image.Encode(format, quality);
        return data?.ToArray();
    }

    private static byte[]? ToPngViaImageSharp(byte[] source)
    {
        try
        {
            using var image = SixLabors.ImageSharp.Image.Load(source);
            image.Mutate(x => x.AutoOrient());
            if (image.Width > 2400 || image.Height > 2400)
            {
                image.Mutate(x => x.Resize(new SixLabors.ImageSharp.Processing.ResizeOptions
                {
                    Mode = SixLabors.ImageSharp.Processing.ResizeMode.Max,
                    Size = new SixLabors.ImageSharp.Size(2400, 2400)
                }));
            }
            using var ms = new MemoryStream();
            image.Save(ms, new PngEncoder());
            return ms.ToArray();
        }
        catch
        {
            return null;
        }
    }

    private SKTypeface? GetTypeface()
    {
        if (_typeface is not null) return _typeface;
        lock (TypefaceLock)
        {
            if (_typeface is not null) return _typeface;

            // اول پوشه فونت‌های خروجی (انتشار)، بعد ریشه محتوا (توسعه)
            var dirs = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "Resources", "fonts"),
                Path.Combine(_env.ContentRootPath, "Resources", "fonts")
            };
            foreach (var dir in dirs)
            {
                foreach (var name in new[] { "Vazirmatn-Bold.ttf", "Vazirmatn-Regular.ttf" })
                {
                    var p = Path.Combine(dir, name);
                    if (File.Exists(p))
                    {
                        _typeface = SKTypeface.FromFile(p);
                        return _typeface;
                    }
                }
            }
        }
        return null;
    }
}
