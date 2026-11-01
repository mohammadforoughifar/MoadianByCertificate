using FluentValidation;
using MoudianMiddleware.Contracts.ViewModels.Moadian;

namespace MoudianMiddleware.CoreLayer.Validators.Moadian;

/// <summary>اعتبارسنجی ساختاری مواد credential؛ نیازهای وابسته به حالت امضا در سرویس اعمال می‌شوند.</summary>
public sealed class SaveMoadianCredentialRequestValidator : AbstractValidator<SaveMoadianCredentialRequest>
{
    /// <summary>تعریف محدودیت‌های payload گواهی، کلید و توکن.</summary>
    public SaveMoadianCredentialRequestValidator()
    {
        RuleFor(x => x.CertificateBase64).MaximumLength(8000);
        // Base64 ciphertext expands the plaintext; keep the maximum within the 16K mapped DB column.
        RuleFor(x => x.PrivateKeyPem).MaximumLength(12000);
        RuleFor(x => x.Pkcs11LibraryPath).MaximumLength(500);
        RuleFor(x => x.Pkcs11CertificateAlias).MaximumLength(200);
        RuleFor(x => x.Pkcs11Pin).MaximumLength(256);
    }
}
