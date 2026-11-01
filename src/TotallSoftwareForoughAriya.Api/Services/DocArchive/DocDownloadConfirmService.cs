using Microsoft.Extensions.Caching.Memory;

namespace Inventory.Api.Services.DocArchive;

public interface IDocDownloadConfirmService
{
    /// <summary>آیا این کاربر برای این مدرک در بازه زمانی معتبر، رمزش را تایید کرده است؟</summary>
    bool IsConfirmed(int userId, int documentId);

    /// <summary>ثبت تایید موفق — برای مدت کوتاه (پیش‌فرض ۱۵ دقیقه) معتبر است.</summary>
    void Confirm(int userId, int documentId);
}

/// <summary>
/// اعطای موقت «تایید مجدد رمز» برای دانلود/مشاهده فایل‌های مدارک محرمانه.
/// در حافظه نگهداری می‌شود — با ری‌استارت برنامه همه اعطاها باطل می‌شوند (امن‌تر).
/// </summary>
public class DocDownloadConfirmService : IDocDownloadConfirmService
{
    private static readonly TimeSpan GrantLifetime = TimeSpan.FromMinutes(15);

    private readonly IMemoryCache _cache;

    public DocDownloadConfirmService(IMemoryCache cache) => _cache = cache;

    private static string Key(int userId, int documentId) => $"docdl:{userId}:{documentId}";

    public bool IsConfirmed(int userId, int documentId) => _cache.TryGetValue(Key(userId, documentId), out _);

    public void Confirm(int userId, int documentId) =>
        _cache.Set(Key(userId, documentId), true, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = GrantLifetime
        });
}
