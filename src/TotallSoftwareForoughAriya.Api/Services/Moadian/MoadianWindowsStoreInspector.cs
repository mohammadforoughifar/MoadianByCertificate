using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Inventory.Shared.Dtos;

namespace Inventory.Api.Services.Moadian;

/// <summary>
/// کاوشگر و امضاکنندهٔ مستقیم مخزن گواهی ویندوز (Windows Certificate Store — certmgr.msc / certlm.msc):
///   • خواندن تمام گواهی‌های نصب‌شده در Personal (CurrentUser\My و LocalMachine\My) بدون نیاز به خروجی .pfx.
///   • پشتیبانی کامل از کلیدهای خصوصی غیرقابل‌خروجی (Non-Exportable CNG / CAPI — حالتی که گزینهٔ
///     «Yes, export the private key» در ویزارد Export ویندوز خاکستری/غیرفعال است).
///   • پشتیبانی از گواهی‌های توکن سخت‌افزاری / کارت هوشمند ثبت‌شده در Windows SmartCard CSP/KSP.
///   • تشخیص دقیق گواهی‌های فاقد کلید خصوصی (صرفاً فایل .crt نصب‌شده بدون کلید) و خطاهای عدم اتصال توکن.
/// </summary>
public static class MoadianWindowsStoreInspector
{
    public sealed record ValidatedWindowsStoreCertificate(
        string StoreLocation,
        string Subject,
        string Issuer,
        string SerialNumber,
        string Thumbprint,
        DateTime NotBeforeUtc,
        DateTime NotAfterUtc);

    /// <summary>
    /// پاک‌سازی اثر انگشت SHA-1 (حذف فاصله‌ها، دونقطه، خط تیره و کاراکترهای مخفی یونیکد LTR/RTL کپی‌شده از certmgr.msc).
    /// </summary>
    public static string NormalizeThumbprint(string? rawThumbprint)
    {
        if (string.IsNullOrWhiteSpace(rawThumbprint))
            return string.Empty;

        var sb = new StringBuilder(rawThumbprint.Length);
        foreach (var ch in rawThumbprint)
        {
            if (ch is (>= '0' and <= '9') or (>= 'a' and <= 'f') or (>= 'A' and <= 'F'))
                sb.Append(char.ToUpperInvariant(ch));
        }
        return sb.ToString();
    }

    /// <summary>
    /// فهرست‌برداری غیرمخرب (Passive — بدون فراخوانی PIN یا پنجرهٔ کارت هوشمند) از مخزن گواهی سیستم.
    /// </summary>
    public static MdnWindowsStoreDiscoverResultDto Discover()
    {
        var result = new MdnWindowsStoreDiscoverResultDto
        {
            IsWindowsOs = OperatingSystem.IsWindows()
        };

        var items = new List<MdnWindowsStoreCertificateItemDto>();
        foreach (var (location, code, display) in GetSearchStores(preferredLocation: null))
        {
            try
            {
                using var store = new X509Store(StoreName.My, location);
                store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
                var certs = store.Certificates;
                try
                {
                    foreach (var cert in certs)
                    {
                        var cn = cert.GetNameInfo(X509NameType.SimpleName, false);
                        items.Add(new MdnWindowsStoreCertificateItemDto
                        {
                            StoreLocation = code,
                            StoreDisplayName = display,
                            Thumbprint = NormalizeThumbprint(cert.Thumbprint),
                            CommonName = string.IsNullOrWhiteSpace(cn) ? cert.Subject : cn,
                            Subject = cert.Subject,
                            Issuer = cert.Issuer,
                            SerialNumber = cert.SerialNumber,
                            KeyAlgorithm = cert.PublicKey.Oid?.FriendlyName ?? cert.PublicKey.Oid?.Value ?? "",
                            NotBefore = cert.NotBefore.ToLocalTime(),
                            NotAfter = cert.NotAfter.ToLocalTime(),
                            IsExpired = cert.NotAfter.ToUniversalTime() < DateTime.UtcNow,
                            IsCertificateAuthority = MoadianPkcs11Inspector.IsCaCertificate(cert),
                            HasPrivateKey = cert.HasPrivateKey
                        });
                    }
                }
                finally
                {
                    foreach (var cert in certs)
                        cert.Dispose();
                }
            }
            catch
            {
                // اگر پوشهٔ مخزن هنوز ایجاد نشده یا دسترسی LocalMachine محدود است، بدون خطا عبور می‌کنیم.
            }
        }

        result.Certificates = items
            .OrderBy(c => c.IsCertificateAuthority ? 1 : 0)
            .ThenByDescending(c => c.HasPrivateKey)
            .ThenBy(c => c.IsExpired ? 1 : 0)
            .ThenByDescending(c => c.NotAfter)
            .ToList();

        var validWithKey = result.Certificates.Count(c => !c.IsCertificateAuthority && c.HasPrivateKey && !c.IsExpired);
        var expiredWithKey = result.Certificates.Count(c => !c.IsCertificateAuthority && c.HasPrivateKey && c.IsExpired);
        var publicOnly = result.Certificates.Count(c => !c.IsCertificateAuthority && !c.HasPrivateKey);

        if (validWithKey > 0)
        {
            result.Summary =
                $"تعداد {validWithKey} گواهی امضای معتبر و دارای کلید خصوصی در مخزن ویندوز یافت شد. حتی اگر گزینهٔ «Yes, export the private key» در ویندوز غیرفعال (خاکستری) باشد، از همین بخش مستقیماً و بدون نیاز به خروجی .pfx قابل امضا است.";
        }
        else if (expiredWithKey > 0)
        {
            result.Summary =
                $"تعداد {expiredWithKey} گواهی دارای کلید خصوصی در مخزن ویندوز یافت شد، اما مهلت اعتبار آن به پایان رسیده است (منقضی‌شده). برای ارسال صورتحساب به سامانه مودیان باید گواهی تمدیدشده جایگزین شود.";
        }
        else if (publicOnly > 0)
        {
            result.Summary =
                $"تعداد {publicOnly} گواهی مودی در پوشهٔ Personal ویندوز دیده شد اما هیچ‌کدام کلید خصوصی ندارند (HasPrivateKey = خیر). یعنی قبلاً فقط فایل عمومی گواهی (.crt / .cer) بدون کلید خصوصی نصب شده است — دقیقاً به همین دلیل گزینهٔ «Yes, export the private key» در ویندوز غیرفعال است! اگر گواهی شما روی توکن USB صادر شده است، توکن را وصل کرده و از زبانهٔ «توکن سخت‌افزاری (PKCS#11)» استفاده کنید.";
        }
        else if (result.Certificates.Count > 0)
        {
            result.Summary =
                "فقط گواهی‌های ریشه/میانی مرکز صدور (CA) در پوشهٔ Personal دیده شد و گواهی امضای مودی در این پوشه وجود ندارد.";
        }
        else
        {
            result.Summary = OperatingSystem.IsWindows()
                ? "هیچ گواهی‌ای در پوشهٔ Personal مخزن ویندوز (certmgr.msc / certlm.msc) یافت نشد."
                : "سرور فعلی روی سیستم‌عامل غیر ویندوز اجرا می‌شود و مخزن Personal کاربر فعلی خالی است.";
        }

        return result;
    }

    /// <summary>
    /// بررسی کامل گواهی انتخاب‌شده در مخزن ویندوز و انجام تست واقعی امضای دیجیتال SHA-256 بدون استخراج کلید خصوصی.
    /// </summary>
    public static ValidatedWindowsStoreCertificate ValidateAndInspectSelection(
        string? thumbprint,
        string? preferredLocation = null)
    {
        var cleanThumb = NormalizeThumbprint(thumbprint);
        if (string.IsNullOrWhiteSpace(cleanThumb))
            throw new InvalidOperationException(
                "اثر انگشت (Thumbprint) گواهی مخزن ویندوز انتخاب نشده است. ابتدا دکمهٔ «مشاهده گواهی‌های نصب‌شده در ویندوز» را بزنید و گواهی مورد نظر را انتخاب کنید.");

        var (cert, foundLocation) = FindCertificateByThumbprint(cleanThumb, preferredLocation);
        using (cert)
        {
            var cn = cert.GetNameInfo(X509NameType.SimpleName, false);
            var title = string.IsNullOrWhiteSpace(cn) ? cert.Subject : cn;

            if (MoadianPkcs11Inspector.IsCaCertificate(cert))
            {
                throw new InvalidOperationException(
                    $"علت عدم تطابق: گواهی انتخاب‌شده («{title}») متعلق به «مرکز صدور گواهی (CA)» است، نه گواهی امضای مودی! لطفاً گواهی اصلی مودی/شرکت را انتخاب کنید.");
            }

            if (!cert.HasPrivateKey)
            {
                throw new InvalidOperationException(
                    $"علت خطا: گواهی «{title}» در مخزن ویندوز فاقد کلید خصوصی است (HasPrivateKey = خیر). " +
                    "این یعنی فقط فایل عمومی گواهی (.crt / .cer) در ویندوز نصب شده و کلید خصوصی همراه آن در ویندوز وجود ندارد — دقیقاً به همین دلیل گزینهٔ «Yes, export the private key» نیز در certmgr غیرفعال است!\n" +
                    "راهنما:\n" +
                    "• اگر گواهی از مراکز صدور (GICA) روی توکن USB صادر شده است، توکن را وصل کنید و از زبانهٔ «توکن سخت‌افزاری (PKCS#11)» استفاده نمایید.\n" +
                    "• اگر گواهی نرم‌افزاری بوده است، کلید خصوصی فقط روی همان رایانه‌ای وجود دارد که درخواست اولیهٔ صدور گواهی (CSR) روی آن ساخته شده است.");
            }

            using var pubRsa = cert.GetRSAPublicKey()
                ?? throw new InvalidOperationException(
                    $"کلید عمومی گواهی «{title}» از نوع RSA نیست؛ سامانه مودیان فقط کلید RSA را می‌پذیرد.");

            if (pubRsa.KeySize < 2048)
                throw new InvalidOperationException($"طول کلید گواهی باید حداقل ۲۰۴۸ بیت باشد (فعلی: {pubRsa.KeySize} بیت).");

            try
            {
                using var privRsa = cert.GetRSAPrivateKey()
                    ?? throw new InvalidOperationException(
                        $"کلید خصوصی RSA برای گواهی «{title}» توسط سیستم‌عامل قابل بازگشایی نیست.");

                var challenge = RandomNumberGenerator.GetBytes(32);
                var sig = privRsa.SignData(challenge, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                if (!pubRsa.VerifyData(challenge, sig, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
                {
                    throw new InvalidOperationException(
                        $"عدم تطابق ریاضی: امضای تولیدشده توسط کلید مخزن ویندوز با کلید عمومی گواهی «{title}» هم‌خوانی ندارد!");
                }
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(TranslateWindowsStoreException(ex, title));
            }

            return new ValidatedWindowsStoreCertificate(
                foundLocation,
                cert.Subject,
                cert.Issuer,
                cert.SerialNumber,
                cleanThumb,
                cert.NotBefore.ToUniversalTime(),
                cert.NotAfter.ToUniversalTime());
        }
    }

    /// <summary>
    /// بازگشایی گواهی و هندل کلید خصوصی RSA (بدون استخراج کلید) برای امضای مستقیم JWS در درگاه سامانه مودیان.
    /// فراخواننده مسئول Dispose کردن هر دو شیء بازگشتی پس از اتمام درخواست SDK است.
    /// </summary>
    public static (X509Certificate2 Certificate, RSA PrivateKey) OpenSigningMaterial(string? thumbprint)
    {
        var cleanThumb = NormalizeThumbprint(thumbprint);
        if (string.IsNullOrWhiteSpace(cleanThumb))
            throw new InvalidOperationException("اثر انگشت (Thumbprint) گواهی مخزن ویندوز در پروفایل مودیان ثبت نشده است.");

        var (cert, _) = FindCertificateByThumbprint(cleanThumb, preferredLocation: null);
        try
        {
            var cn = cert.GetNameInfo(X509NameType.SimpleName, false);
            var title = string.IsNullOrWhiteSpace(cn) ? cert.Subject : cn;

            if (!cert.HasPrivateKey)
            {
                throw new InvalidOperationException(
                    $"گواهی «{title}» در مخزن ویندوز فاقد کلید خصوصی است. لطفاً تنظیمات گواهی امضا را بررسی کنید.");
            }

            RSA? rsa;
            try
            {
                rsa = cert.GetRSAPrivateKey();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(TranslateWindowsStoreException(ex, title));
            }

            if (rsa is null)
                throw new InvalidOperationException($"کلید خصوصی RSA برای گواهی «{title}» از مخزن ویندوز قابل دسترسی نیست.");

            return (cert, rsa);
        }
        catch
        {
            cert.Dispose();
            throw;
        }
    }

    // ==================== ابزارهای داخلی جستجو و ترجمه خطا ====================

    private static (X509Certificate2 Certificate, string StoreLocationCode) FindCertificateByThumbprint(
        string normalizedThumbprint,
        string? preferredLocation)
    {
        foreach (var (location, code, _) in GetSearchStores(preferredLocation))
        {
            try
            {
                using var store = new X509Store(StoreName.My, location);
                store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
                var certs = store.Certificates;
                X509Certificate2? matched = null;
                foreach (var c in certs)
                {
                    if (matched is null &&
                        string.Equals(NormalizeThumbprint(c.Thumbprint), normalizedThumbprint, StringComparison.OrdinalIgnoreCase))
                    {
                        matched = c;
                    }
                    else
                    {
                        c.Dispose();
                    }
                }

                if (matched is not null)
                    return (matched, code);
            }
            catch
            {
                // ادامه جستجو در مخزن بعدی
            }
        }

        throw new InvalidOperationException(
            $"گواهی با اثر انگشت «{normalizedThumbprint}» در مخزن Personal ویندوز (CurrentUser یا LocalMachine) یافت نشد. اگر گواهی را در حساب کاربری دیگری نصب کرده‌اید یا توکن USB جدا شده است، دوباره فهرست گواهی‌های ویندوز را اسکن کنید.");
    }

    private static List<(StoreLocation Location, string Code, string Display)> GetSearchStores(string? preferredLocation)
    {
        var currentUser = (StoreLocation.CurrentUser, "CurrentUser", "کاربر فعلی ویندوز (certmgr.msc — Personal)");
        var localMachine = (StoreLocation.LocalMachine, "LocalMachine", "کل سیستم ویندوز (certlm.msc — Local Computer)");

        if (!OperatingSystem.IsWindows())
            return new List<(StoreLocation, string, string)> { currentUser };

        if (string.Equals(preferredLocation, "LocalMachine", StringComparison.OrdinalIgnoreCase))
            return new List<(StoreLocation, string, string)> { localMachine, currentUser };

        return new List<(StoreLocation, string, string)> { currentUser, localMachine };
    }

    private static string TranslateWindowsStoreException(Exception ex, string certTitle)
    {
        var root = ex.InnerException ?? ex;
        var msg = root.Message ?? string.Empty;
        var hr = (uint)root.HResult;

        // خطاهای استاندارد SmartCard / USB Token در ویندوز (SCARD_E_NO_SMARTCARD, SCARD_W_REMOVED_CARD, NTE_DEVICE_NOT_READY)
        if (hr is 0x8010000Cu or 0x80100069u or 0x8010001Du or 0x80090030u ||
            msg.Contains("smart card", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("smartcard", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("device is not ready", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("no media", StringComparison.OrdinalIgnoreCase))
        {
            return $"علت خطا: گواهی «{certTitle}» در مخزن ویندوز (certmgr.msc) ثبت شده، اما کلید خصوصی آن داخل «توکن سخت‌افزاری USB / کارت هوشمند» قرار دارد که هم‌اکنون به پورت USB سیستم متصل نیست!\n" +
                   "همین موضوع دلیل غیرفعال بودن گزینهٔ «Yes, export the private key» در ویندوز نیز هست (کلید خصوصی سخت‌افزاری هیچ‌گاه قابل خروجی به فایل .pfx نیست).\n" +
                   "راه‌حل: توکن USB امضای الکترونیکی را به سیستم وصل کنید و دوباره امتحان نمایید (یا برای ثبت دائمی رمز پین بدون پنجرهٔ ویندوز، از زبانهٔ «توکن سخت‌افزاری (PKCS#11)» استفاده کنید).";
        }

        // لغو پنجرهٔ PIN توسط کاربر (SCARD_W_CANCELLED_BY_USER / ERROR_CANCELLED)
        if (hr is 0x8010006Eu or 0x800704C7u ||
            msg.Contains("cancel", StringComparison.OrdinalIgnoreCase))
        {
            return $"عملیات ورود رمز پین توکن / کارت هوشمند برای گواهی «{certTitle}» توسط کاربر لغو شد. لطفاً دوباره امتحان کرده و رمز پین توکن را وارد کنید (یا از زبانهٔ «توکن سخت‌افزاری PKCS#11» استفاده نمایید).";
        }

        // عدم دسترسی به Key Container در ویندوز (NTE_BAD_KEYSET / NTE_PERM / Access Denied)
        if (hr is 0x80090016u or 0x80090010u or 0x80070005u ||
            msg.Contains("Keyset does not exist", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("Access is denied", StringComparison.OrdinalIgnoreCase))
        {
            return $"دسترسی به کلید خصوصی گواهی «{certTitle}» در مخزن ویندوز امکان‌پذیر نیست (کد خطا: 0x{hr:X8}). " +
                   "اگر کلید روی توکن USB است، از اتصال توکن اطمینان حاصل کنید؛ و اگر گواهی در Local Computer (certlm.msc) نصب شده است، از بخش All Tasks -> Manage Private Keys دسترسی خواندن (Read) را به حساب کاربری اجرای برنامه بدهید.";
        }

        return $"امضای آزمایشی با کلید گواهی «{certTitle}» در مخزن ویندوز ناموفق بود (0x{hr:X8}): {msg}";
    }
}
