using MoudianMiddleware.Contracts.ViewModels.Moadian;

namespace MoudianMiddleware.CoreLayer.Interfaces.Services;

/// <summary>مدیریت ایمن گواهی و مواد امضای هر مودی.</summary>
public interface IMoadianCredentialService
{
    /// <summary>ثبت/به‌روزرسانی credential؛ کلید و PIN پیش از SaveChanges با AES-GCM رمز می‌شوند.</summary>
    Task<MoadianCredentialViewModel> SaveAsync(SaveMoadianCredentialRequest request, CancellationToken cancellationToken = default);
    /// <summary>دریافت metadata امن credential مودی جاری.</summary>
    Task<MoadianCredentialViewModel?> GetAsync(CancellationToken cancellationToken = default);
}
