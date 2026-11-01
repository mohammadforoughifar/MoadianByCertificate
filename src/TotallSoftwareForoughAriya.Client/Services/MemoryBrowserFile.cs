using System.IO;
using System.Threading;
using Microsoft.AspNetCore.Components.Forms;

namespace Inventory.Client.Services;

/// <summary>
/// پیاده‌سازیِ حافظه‌ایِ IBrowserFile — برای زمانی که فایل از مرورگر نمی‌آید
/// بلکه از پیش در حافظه داریم (مثلاً پیوست‌های یک ایمیل که قرار است به
/// نامه داخلی تبدیل شود) و می‌خواهیم آن را به همان صفِ پیوستِ فرم نامه بدهیم.
/// </summary>
public sealed class MemoryBrowserFile : IBrowserFile
{
    private readonly byte[] _data;

    public MemoryBrowserFile(string name, string contentType, byte[] data)
    {
        Name = name;
        ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType;
        _data = data ?? System.Array.Empty<byte>();
        Size = _data.Length;
        LastModified = DateTimeOffset.Now;
    }

    public string Name { get; }

    public DateTimeOffset LastModified { get; }

    public long Size { get; }

    public string ContentType { get; }

    public Stream OpenReadStream(long maxAllowedSize = long.MaxValue, CancellationToken cancellationToken = default)
    {
        // بدون محدودیت حجم فایل — هر حجمی مجاز است
        return new MemoryStream(_data, writable: false);
    }
}
