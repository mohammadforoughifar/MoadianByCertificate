using MoudianMiddleware.Contracts.ViewModels.Moadian;

namespace MoudianMiddleware.CoreLayer.Interfaces.Services;

/// <summary>مدیریت تنظیمات غیرو محرمانه‌ی اتصال مودیان برای tenant جاری.</summary>
public interface IMoadianSettingService
{
    /// <summary>دریافت تنظیمات tenant جاری؛ اگر تنظیم نشده باشد null برمی‌گرداند.</summary>
    Task<MoadianSettingViewModel?> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>ایجاد یا به‌روزرسانی تنظیمات پس از اعتبارسنجی.</summary>
    Task<MoadianSettingViewModel> SaveAsync(MoadianSettingWriteRequest request, CancellationToken cancellationToken = default);
}
