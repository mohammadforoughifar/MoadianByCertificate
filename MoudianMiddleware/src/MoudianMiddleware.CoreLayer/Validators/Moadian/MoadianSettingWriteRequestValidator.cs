using FluentValidation;
using MoudianMiddleware.Contracts.Enums;
using MoudianMiddleware.Contracts.ViewModels.Moadian;

namespace MoudianMiddleware.CoreLayer.Validators.Moadian;

/// <summary>اعتبارسنجی تنظیمات اتصال و URLهای محیط‌های مودیان.</summary>
public sealed class MoadianSettingWriteRequestValidator : AbstractValidator<MoadianSettingWriteRequest>
{
    /// <summary>تعریف قواعد محیط، شناسه‌ی حافظه و امضای آزمایشی.</summary>
    public MoadianSettingWriteRequestValidator()
    {
        RuleFor(x => x.MemoryId).NotEmpty().Length(6).Matches("^[A-Za-z0-9]{6}$")
            .WithMessage("شناسه‌ی حافظه‌ی مالیاتی باید شش نویسه‌ی حرفی/عددی باشد.");
        RuleFor(x => x.EconomicCode).NotEmpty().MaximumLength(20);
        RuleFor(x => x.NationalId).MaximumLength(20);
        RuleFor(x => x.BranchCode).MaximumLength(50);
        RuleFor(x => x.SignatureMode).IsInEnum();
        RuleFor(x => x.ActiveEnvironment).IsInEnum();
        RuleFor(x => x.IsSignatureExperimentalApproved)
            .Equal(true)
            .When(x => x.SignatureMode == MoadianSignatureMode.PrivateKeyOnly)
            .WithMessage("استفاده از امضای private-key-only نیازمند تأیید صریح است.");

        RuleFor(x => x.SandboxBaseUrl)
            .NotEmpty()
            .When(x => x.ActiveEnvironment == MoadianEnvironment.Sandbox)
            .WithMessage("برای محیط Sandbox باید نشانی را صریحاً وارد کنید؛ مقدار پیش‌فرض حدس زده نمی‌شود.");
        RuleFor(x => x.SandboxBaseUrl)
            .Must(IsValidHttpUrl)
            .When(x => !string.IsNullOrWhiteSpace(x.SandboxBaseUrl))
            .WithMessage("نشانی Sandbox باید URL مطلق HTTP(S)، بدون user-info یا query/fragment باشد.");
        RuleFor(x => x.ProductionBaseUrl)
            .Must(IsValidHttpsUrl)
            .When(x => !string.IsNullOrWhiteSpace(x.ProductionBaseUrl))
            .WithMessage("نشانی Production باید URL مطلق HTTPS، بدون user-info یا query/fragment باشد.");
        RuleFor(x => x.SandboxBaseUrl).MaximumLength(300);
        RuleFor(x => x.ProductionBaseUrl).MaximumLength(300);
    }

    private static bool IsValidHttpUrl(string? value) => IsValidUrl(value, requireHttps: false);
    private static bool IsValidHttpsUrl(string? value) => IsValidUrl(value, requireHttps: true);

    private static bool IsValidUrl(string? value, bool requireHttps)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var supportedScheme = uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
        return supportedScheme
            && (!requireHttps || uri.Scheme == Uri.UriSchemeHttps)
            && string.IsNullOrEmpty(uri.UserInfo)
            && string.IsNullOrEmpty(uri.Query)
            && string.IsNullOrEmpty(uri.Fragment);
    }
}
