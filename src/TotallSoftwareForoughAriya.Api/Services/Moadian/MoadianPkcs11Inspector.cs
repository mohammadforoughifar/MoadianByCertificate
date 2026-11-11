using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Inventory.Shared.Dtos;
using Net.Pkcs11Interop.X509Store;

namespace Inventory.Api.Services.Moadian;

public static class MoadianPkcs11Inspector
{
    private static readonly (string FileName, string VendorName)[] KnownWindowsLibraries =
    {
        ("shuttlecsp11_3003.dll", "Feitian ePass3003 / ePass3003Auto"),
        ("entersafe.dll", "Feitian ePass / EnterSafe"),
        ("eps2003csp11.dll", "Feitian ePass2003"),
        ("parskey11.dll", "پارس‌کی (ParsKey — امن‌افزار گستر شریف)"),
        ("pk11.dll", "توکن پارس‌کی / PKCS#11"),
        ("eTPKCS11.dll", "SafeNet / Thales eToken (5110 / 5300)"),
        ("IDPrimePKCS1164.dll", "Thales IDPrime (64-bit)"),
        ("IDPrimePKCS11.dll", "Thales IDPrime"),
        ("mtoken_gm3000_pkcs11.dll", "Longmai mToken GM3000"),
        ("gm3000_pkcs11.dll", "Longmai mToken GM3000"),
        ("SignatureP11.dll", "Watchdata ProxKey"),
        ("WDPKCS.dll", "Watchdata USB Token"),
        ("asepkcs.dll", "Athena IDProtect"),
        ("aetpkss1.dll", "A.E.T. SafeSign"),
        ("rtPKCS11ECP.dll", "Rutoken PKCS#11"),
        ("opensc-pkcs11.dll", "OpenSC PKCS#11")
    };

    private static readonly (string FullPath, string VendorName)[] ExtraKnownPaths =
    {
        (@"C:\Program Files\OpenSC Project\OpenSC\pkcs11\opensc-pkcs11.dll", "OpenSC PKCS#11 (64-bit)"),
        (@"C:\Program Files\SafeNet\Authentication\SAC\x64\eTPKCS11.dll", "SafeNet Authentication Client (64-bit)"),
        ("/usr/lib/x86_64-linux-gnu/opensc-pkcs11.so", "OpenSC PKCS#11 (Linux x64)"),
        ("/usr/lib64/opensc-pkcs11.so", "OpenSC PKCS#11 (Linux lib64)"),
        ("/usr/lib/softhsm/libsofthsm2.so", "SoftHSM2 PKCS#11")
    };

    public static MdnPkcs11DiscoverResultDto Discover(string? requestedLibraryPath, string? pin)
    {
        var result = new MdnPkcs11DiscoverResultDto
        {
            Is64BitProcess = Environment.Is64BitProcess
        };

        var candidates = new List<(string Path, string Vendor)>();
        var seen = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        void AddCandidate(string path, string vendor)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            var full = path.Trim();
            if (seen.Add(full))
                candidates.Add((full, vendor));
        }

        if (!string.IsNullOrWhiteSpace(requestedLibraryPath))
        {
            var custom = requestedLibraryPath.Trim();
            if (!File.Exists(custom))
            {
                throw new InvalidOperationException(
                    $"فایل درایور PKCS#11 در مسیر واردشده یافت نشد:\n{custom}\n" +
                    "کادر مسیر را خالی بگذارید و دکمهٔ «جستجوی خودکار درایور و گواهی‌های توکن» را بزنید تا مسیرهای نصب‌شده روی سیستم به صورت خودکار اسکن شوند.");
            }
            AddCandidate(custom, GuessVendorName(custom));
        }

        foreach (var (path, vendor) in ScanInstalledLibraries())
            AddCandidate(path, vendor);

        if (candidates.Count == 0)
        {
            result.Summary = OperatingSystem.IsWindows()
                ? "هیچ فایل درایور استاندارد PKCS#11 در پوشهٔ C:\\Windows\\System32 یافت نشد. لطفاً نرم‌افزار/درایور توکن سخت‌افزاری خود (مانند ePass3003، SafeNet یا ParsKey) را روی سیستم نصب کنید."
                : "هیچ کتابخانهٔ درایور PKCS#11 روی سرور یافت نشد.";
            return result;
        }

        // مرحله اول: بررسی همهٔ درایورها بدون ارسال PIN (تا هرگز شمارندهٔ پین اشتباه روی درایورهای متفرقه کم نشود)
        var librariesWithConnectedToken = new List<(string Path, string Vendor)>();
        foreach (var (libPath, vendor) in candidates)
        {
            var drv = ProbeDriver(libPath, vendor, pin: null, out var publicCerts);
            result.Drivers.Add(drv);
            if (drv.ConnectedTokensCount > 0)
            {
                librariesWithConnectedToken.Add((libPath, vendor));
                result.Certificates.AddRange(publicCerts);
            }
        }

        // مرحله دوم: اگر توکن فیزیکی متصل است و کاربر رمز PIN را وارد کرده، همان درایورِ دارای توکن با PIN بازخوانی می‌شود
        // تا گواهی‌های خصوصی و وضعیت واقعی HasPrivateKey بررسی گردد (و اگر PIN اشتباه است خطای دقیق داده شود).
        if (!string.IsNullOrEmpty(pin) && librariesWithConnectedToken.Count > 0)
        {
            var targets = !string.IsNullOrWhiteSpace(requestedLibraryPath)
                ? librariesWithConnectedToken.Where(x => PathEquals(x.Path, requestedLibraryPath.Trim())).ToList()
                : (librariesWithConnectedToken.Count == 1 ? librariesWithConnectedToken : new List<(string Path, string Vendor)>());

            if (targets.Count == 1)
            {
                var target = targets[0];
                result.Certificates.RemoveAll(c => PathEquals(c.LibraryPath, target.Path));
                var authDrv = ProbeDriver(target.Path, target.Vendor, pin, out var authCerts, throwOnAuthError: true);
                var idx = result.Drivers.FindIndex(d => PathEquals(d.LibraryPath, target.Path));
                if (idx >= 0) result.Drivers[idx] = authDrv;
                result.Certificates.AddRange(authCerts);
            }
        }

        var connectedTokensTotal = result.Drivers.Sum(d => d.ConnectedTokensCount);
        if (result.Certificates.Count > 0)
        {
            var signingCerts = result.Certificates.Count(c => !c.IsCertificateAuthority);
            result.Summary = $"{result.Drivers.Count} درایور PKCS#11، {connectedTokensTotal} توکن متصل و {result.Certificates.Count} گواهی روی توکن شناسایی شد ({signingCerts} گواهی مودی).";
        }
        else if (connectedTokensTotal > 0)
        {
            result.Summary = string.IsNullOrEmpty(pin)
                ? $"توکن سخت‌افزاری شناسایی شد اما گواهی عمومی بدون پین خوانده نشد؛ رمز PIN توکن را وارد کرده و مجدداً دکمهٔ اسکن را بزنید."
                : "توکن سخت‌افزاری متصل است اما هیچ گواهی X.509 روی آن یافت نشد.";
        }
        else
        {
            result.Summary = $"{result.Drivers.Count} درایور PKCS#11 روی سیستم نصب است، اما هیچ توکن سخت‌افزاری متصلی روی درگاه USB دیده نشد. از اتصال توکن به سیستم مطمئن شوید.";
        }

        return result;
    }

    public static ValidatedPkcs11Certificate ValidateAndInspectSelection(
        string libraryPath,
        string? requestedAlias,
        string pin)
    {
        var cleanPath = libraryPath.Trim();
        if (!File.Exists(cleanPath))
        {
            throw new InvalidOperationException(
                $"مسیر فایل کتابخانه PKCS#11 یافت نشد:\n{cleanPath}\n" +
                "از دکمهٔ «جستجوی خودکار درایور و گواهی‌های توکن» برای انتخاب مسیر صحیح استفاده کنید.");
        }

        WarnIf32BitSystemFolderOn64BitProcess(cleanPath);

        byte[]? pinBytes = Encoding.UTF8.GetBytes(pin);
        try
        {
            using var store = OpenStoreWithDiagnostics(cleanPath, pinBytes);
            var initializedTokens = store.Slots
                .Where(s => s.Token is { Info.Initialized: true })
                .ToList();

            if (initializedTokens.Count == 0)
            {
                throw new InvalidOperationException(
                    $"درایور «{Path.GetFileName(cleanPath)}» با موفقیت بارگذاری شد ({store.Slots.Count} درگاه بررسی شد)، اما هیچ توکن سخت‌افزاری فعالی روی درگاه USB متصل نیست!\n" +
                    "• مطمئن شوید توکن USB به سیستم متصل است و چراغ آن روشن است.\n" +
                    "• اگر چند درایور مختلف روی سیستم دارید، دکمهٔ «جستجوی خودکار درایور و گواهی‌های توکن» را بزنید تا درایور منطبق با مدل توکن شما انتخاب شود.");
            }

            var allCerts = new List<(Pkcs11Slot Slot, Pkcs11Token Token, Pkcs11X509Certificate Cert)>();
            foreach (var slot in initializedTokens)
            {
                try
                {
                    foreach (var cert in slot.Token.Certificates)
                    {
                        if (cert.Info?.ParsedCertificate is not null)
                            allCerts.Add((slot, slot.Token, cert));
                    }
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(TranslatePkcs11Exception(ex, cleanPath));
                }
            }

            if (allCerts.Count == 0)
            {
                var tokenNames = string.Join("، ", initializedTokens.Select(s => $"{s.Token.Info.Model} ({s.Token.Info.SerialNumber})"));
                throw new InvalidOperationException(
                    $"توکن سخت‌افزاری ({tokenNames}) شناسایی و باز شد، اما هیچ گواهی X.509 روی آن وجود ندارد!");
            }

            var wanted = (requestedAlias ?? string.Empty).Trim();
            (Pkcs11Slot Slot, Pkcs11Token Token, Pkcs11X509Certificate Cert)? matched = null;

            if (!string.IsNullOrEmpty(wanted))
            {
                // ۱) تطابق دقیق با Label گواهی روی توکن (همان معیاری که SDK رسمی 2.0.29 استفاده می‌کند)
                matched = allCerts.FirstOrDefault(x =>
                    string.Equals(x.Cert.Info.Label, wanted, StringComparison.Ordinal));

                // ۲) تطابق غیرحساس به حروف کوچک/بزرگ یا بدون فاصله‌های اضافی روی Label
                if (matched?.Cert is null)
                {
                    matched = allCerts.FirstOrDefault(x =>
                        string.Equals((x.Cert.Info.Label ?? "").Trim(), wanted, StringComparison.OrdinalIgnoreCase));
                }

                // ۳) اگر کاربر به جای Label، نام صاحب گواهی (CN) یا Thumbprint را وارد کرده بود، خودکار به Label واقعی تبدیل می‌کنیم!
                if (matched?.Cert is null)
                {
                    matched = allCerts.FirstOrDefault(x =>
                    {
                        var x509 = x.Cert.Info.ParsedCertificate;
                        var cn = x509.GetNameInfo(X509NameType.SimpleName, false) ?? "";
                        return string.Equals(cn.Trim(), wanted, StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(x509.Thumbprint, wanted.Replace(":", "").Replace(" ", ""), StringComparison.OrdinalIgnoreCase);
                    });
                }
            }
            else
            {
                // اگر فیلد Alias خالی بود و فقط یک گواهی غیر CA روی توکن وجود دارد، خودکار انتخاب می‌شود
                var nonCa = allCerts.Where(x => !IsCaCertificate(x.Cert.Info.ParsedCertificate)).ToList();
                if (nonCa.Count == 1)
                    matched = nonCa[0];
            }

            if (matched?.Cert is null)
            {
                var availableReport = BuildAvailableTokenCertificatesReport(allCerts);
                throw new InvalidOperationException(
                    (string.IsNullOrEmpty(wanted)
                        ? "بیش از یک گواهی روی توکن وجود دارد؛ لطفاً یکی از گواهی‌های زیر را انتخاب کنید:\n"
                        : $"گواهی با عنوان/نام مستعار «{wanted}» روی توکن یافت نشد!\nگواهی‌های موجود روی توکن شما عبارتند از:\n")
                    + availableReport);
            }

            var chosen = matched.Value.Cert;
            var parsed = chosen.Info.ParsedCertificate;
            var exactLabel = chosen.Info.Label ?? string.Empty;

            if (string.IsNullOrEmpty(exactLabel))
            {
                throw new InvalidOperationException(
                    $"گواهی «{parsed.Subject}» روی توکن فاقد برچسب (CKA_LABEL) است و SDK رسمی مودیان برای شناسایی گواهی به Label نیاز دارد.");
            }

            if (IsCaCertificate(parsed))
            {
                throw new InvalidOperationException(
                    $"علت عدم تطابق: گواهی انتخاب‌شده («{exactLabel}» — {parsed.Subject}) متعلق به «مرکز صدور گواهی (گواهی ریشه/میانی CA)» روی توکن است، نه گواهی امضای مودی! لطفاً گواهی اصلی شرکت/مودی را انتخاب کنید.");
            }

            if (!chosen.HasPrivateKeyObject)
            {
                throw new InvalidOperationException(
                    $"علت عدم تطابق: گواهی «{exactLabel}» ({parsed.Subject}) روی توکن یافت شد، اما کلید خصوصی متناظر با آن روی توکن دیده نمی‌شود (یا رمز PIN واردشده اجازهٔ دسترسی به کلید خصوصی را نداده است).");
            }

            using var pubRsa = parsed.GetRSAPublicKey()
                ?? throw new InvalidOperationException(
                    $"کلید عمومی گواهی «{exactLabel}» از نوع RSA نیست؛ سامانه مودیان فقط کلید RSA را می‌پذیرد.");

            if (pubRsa.KeySize < 2048)
                throw new InvalidOperationException($"طول کلید گواهی روی توکن باید حداقل ۲۰۴۸ بیت باشد (فعلی: {pubRsa.KeySize} بیت).");

            // تست واقعی امضای دیجیتال با کلید خصوصی سخت‌افزاری داخل توکن
            try
            {
                using var tokenPrivRsa = chosen.GetRSAPrivateKey();
                if (tokenPrivRsa is not null)
                {
                    var challenge = RandomNumberGenerator.GetBytes(32);
                    var sig = tokenPrivRsa.SignData(challenge, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                    if (!pubRsa.VerifyData(challenge, sig, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
                    {
                        throw new InvalidOperationException(
                            $"عدم تطابق ریاضی روی توکن: امضای تولیدشده توسط کلید خصوصی توکن با کلید عمومی گواهی «{exactLabel}» مطابقت ندارد!");
                    }
                }
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"امضای آزمایشی با توکن سخت‌افزاری ناموفق بود: {TranslatePkcs11Exception(ex, cleanPath)}");
            }

            return new ValidatedPkcs11Certificate(
                cleanPath,
                exactLabel,
                parsed.Subject,
                parsed.Issuer,
                parsed.SerialNumber,
                parsed.Thumbprint,
                parsed.NotBefore.ToUniversalTime(),
                parsed.NotAfter.ToUniversalTime());
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pinBytes);
        }
    }

    // ابزارهای داخلی اسکن و تشخیص خطا

    private static List<(string Path, string Vendor)> ScanInstalledLibraries()
    {
        var found = new List<(string Path, string Vendor)>();
        var seen = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        void TryAdd(string path, string vendor)
        {
            try
            {
                if (File.Exists(path) && seen.Add(path))
                    found.Add((path, vendor));
            }
            catch { /* دسترسی محدود به مسیر سیستمی */ }
        }

        if (OperatingSystem.IsWindows())
        {
            var sysDir = Environment.SystemDirectory; // در پردازش ۶۴ بیتی همان C:\Windows\System32 است
            foreach (var (fileName, vendor) in KnownWindowsLibraries)
                TryAdd(Path.Combine(sysDir, fileName), vendor);

            try
            {
                if (Directory.Exists(sysDir))
                {
                    foreach (var pattern in new[] { "*csp11*.dll", "*pkcs11*.dll", "*parskey*.dll", "*shuttle*.dll", "*epass*.dll" })
                    {
                        foreach (var file in Directory.EnumerateFiles(sysDir, pattern, SearchOption.TopDirectoryOnly))
                            TryAdd(file, GuessVendorName(file));
                    }
                }
            }
            catch { /* نادیده گرفتن خطاهای احتمالی دسترسی پوشه سیستمی */ }
        }

        foreach (var (fullPath, vendor) in ExtraKnownPaths)
            TryAdd(fullPath, vendor);

        return found;
    }

    private static MdnPkcs11DriverDto ProbeDriver(
        string libraryPath,
        string vendorName,
        string? pin,
        out List<MdnPkcs11CertificateItemDto> certificates,
        bool throwOnAuthError = false)
    {
        certificates = new List<MdnPkcs11CertificateItemDto>();
        var dto = new MdnPkcs11DriverDto
        {
            LibraryPath = libraryPath,
            VendorName = vendorName
        };

        byte[]? pinBytes = string.IsNullOrEmpty(pin) ? null : Encoding.UTF8.GetBytes(pin);
        try
        {
            using var store = OpenStoreWithDiagnostics(libraryPath, pinBytes);
            dto.LoadSucceeded = true;

            foreach (var slot in store.Slots)
            {
                var token = slot.Token;
                if (token is null || !token.Info.Initialized)
                    continue;

                dto.ConnectedTokensCount++;
                List<Pkcs11X509Certificate> tokenCerts;
                try
                {
                    tokenCerts = token.Certificates;
                }
                catch (Exception ex)
                {
                    var translated = TranslatePkcs11Exception(ex, libraryPath);
                    if (throwOnAuthError)
                        throw new InvalidOperationException(translated);
                    dto.DiagnosticMessage = translated;
                    continue;
                }

                foreach (var c in tokenCerts)
                {
                    var x509 = c.Info?.ParsedCertificate;
                    if (x509 is null) continue;

                    var notAfterUtc = x509.NotAfter.ToUniversalTime();
                    certificates.Add(new MdnPkcs11CertificateItemDto
                    {
                        LibraryPath = libraryPath,
                        VendorName = vendorName,
                        SlotDescription = (slot.Info?.Description ?? "").Trim(),
                        TokenLabel = (token.Info.Label ?? "").Trim(),
                        TokenModel = (token.Info.Model ?? "").Trim(),
                        TokenManufacturer = (token.Info.Manufacturer ?? "").Trim(),
                        TokenSerial = (token.Info.SerialNumber ?? "").Trim(),
                        CertificateAlias = c.Info?.Label ?? "",
                        Subject = x509.Subject,
                        CommonName = x509.GetNameInfo(X509NameType.SimpleName, false) ?? x509.Subject,
                        Issuer = x509.Issuer,
                        SerialNumber = x509.SerialNumber,
                        Thumbprint = x509.Thumbprint,
                        NotBefore = x509.NotBefore,
                        NotAfter = x509.NotAfter,
                        IsExpired = notAfterUtc < DateTime.UtcNow,
                        IsCertificateAuthority = IsCaCertificate(x509),
                        HasPrivateKey = c.HasPrivateKeyObject
                    });
                }
            }

            dto.DiagnosticMessage ??= dto.ConnectedTokensCount > 0
                ? $"{dto.ConnectedTokensCount} توکن فعال متصل است ({certificates.Count} گواهی)."
                : "درایور نصب است اما توکنی از این مدل روی درگاه USB متصل نیست.";

            return dto;
        }
        catch (InvalidOperationException) when (throwOnAuthError)
        {
            throw;
        }
        catch (Exception ex)
        {
            var friendly = TranslatePkcs11Exception(ex, libraryPath);
            if (throwOnAuthError)
                throw new InvalidOperationException(friendly);

            dto.LoadSucceeded = false;
            dto.DiagnosticMessage = friendly;
            return dto;
        }
        finally
        {
            if (pinBytes is not null)
                CryptographicOperations.ZeroMemory(pinBytes);
        }
    }

    private static Pkcs11X509Store OpenStoreWithDiagnostics(string libraryPath, byte[]? pinBytes)
    {
        try
        {
            return new Pkcs11X509Store(libraryPath, new StaticPinProvider(pinBytes));
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(TranslatePkcs11Exception(ex, libraryPath));
        }
    }

    private static void WarnIf32BitSystemFolderOn64BitProcess(string path)
    {
        if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess)
            return;

        if (path.Contains(@"\SysWOW64\", StringComparison.OrdinalIgnoreCase) ||
            path.Contains(@"\Program Files (x86)\", StringComparison.OrdinalIgnoreCase))
        {
            var suggested = Path.Combine(Environment.SystemDirectory, Path.GetFileName(path));
            var hint = File.Exists(suggested)
                ? $"نسخهٔ ۶۴ بیتی همین فایل در مسیر «{suggested}» موجود است؛ از آن مسیر استفاده کنید."
                : "لطفاً مسیر ۶۴ بیتی در پوشهٔ C:\\Windows\\System32 را انتخاب کنید.";
            throw new InvalidOperationException(
                $"عدم تطابق معماری ۳۲/۶۴ بیتی: برنامه به صورت ۶۴ بیتی (x64) اجرا می‌شود اما مسیر ۳۲ بیتی («{path}») انتخاب شده است! {hint}");
        }
    }

    private static string TranslatePkcs11Exception(Exception ex, string libraryPath)
    {
        var root = ex.InnerException ?? ex;
        var msg = $"{ex.Message} {root.Message}";

        if (ex is BadImageFormatException || root is BadImageFormatException ||
            msg.Contains("BadImageFormat", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("0x8007000B", StringComparison.OrdinalIgnoreCase))
        {
            var arch = Environment.Is64BitProcess ? "۶۴ بیتی (x64)" : "۳۲ بیتی (x86)";
            return $"عدم تطابق معماری درایور PKCS#11: برنامه به صورت {arch} در حال اجراست اما فایل «{libraryPath}» معماری متفاوتی (۳۲/۶۴ بیت) دارد. فایل درایور داخل C:\\Windows\\System32 را انتخاب کنید.";
        }

        if (ex is DllNotFoundException || root is DllNotFoundException ||
            msg.Contains("Unable to load library", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("0x8007007E", StringComparison.OrdinalIgnoreCase))
        {
            return $"امکان بارگذاری کتابخانهٔ «{libraryPath}» وجود ندارد (فایل یا یکی از پیش‌نیازهای درایور توکن در ویندوز یافت نشد). نصب کامل نرم‌افزار توکن را بررسی کنید.";
        }

        if (msg.Contains("CKR_PIN_INCORRECT", StringComparison.OrdinalIgnoreCase))
            return "رمز پین (PIN) واردشده برای توکن سخت‌افزاری نادرست است! (هشدار: ورود چندبارهٔ رمز اشتباه باعث قفل شدن توکن می‌شود).";

        if (msg.Contains("CKR_PIN_LOCKED", StringComparison.OrdinalIgnoreCase))
            return "توکن سخت‌افزاری به دلیل ورود مکرر رمز اشتباه قفل شده است (CKR_PIN_LOCKED)! لطفاً ابتدا از طریق برنامهٔ مدیریت توکن و رمز PUK/Admin آن را بازگشایی کنید.";

        if (msg.Contains("CKR_PIN_LEN_RANGE", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("CKR_PIN_INVALID", StringComparison.OrdinalIgnoreCase))
            return "طول یا قالب رمز پین (PIN) واردشده معتبر نیست.";

        if (msg.Contains("CKR_PIN_EXPIRED", StringComparison.OrdinalIgnoreCase))
            return "رمز پین توکن منقضی شده است (CKR_PIN_EXPIRED)؛ ابتدا در نرم‌افزار مدیریت توکن رمز جدید تعریف کنید.";

        if (msg.Contains("CKR_TOKEN_NOT_PRESENT", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("CKR_DEVICE_REMOVED", StringComparison.OrdinalIgnoreCase))
            return "توکن سخت‌افزاری روی درگاه USB حضور ندارد یا حین عملیات جدا شده است.";

        return $"خطای ارتباط با درایور توکن «{Path.GetFileName(libraryPath)}»: {root.Message}";
    }

    private static string BuildAvailableTokenCertificatesReport(
        List<(Pkcs11Slot Slot, Pkcs11Token Token, Pkcs11X509Certificate Cert)> allCerts)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < allCerts.Count; i++)
        {
            var (_, token, cert) = allCerts[i];
            var x509 = cert.Info.ParsedCertificate;
            var label = string.IsNullOrEmpty(cert.Info.Label) ? "(بدون Label)" : cert.Info.Label;
            var role = IsCaCertificate(x509) ? "گواهی مرکز صدور (CA)" : "گواهی مودی";
            var keyStatus = cert.HasPrivateKeyObject ? "دارای کلید خصوصی" : "بدون کلید خصوصی";
            sb.AppendLine($"• [{i + 1}] Certificate Alias (Label): \"{label}\" | نوع: {role} ({keyStatus}) | صاحب گواهی: {x509.Subject} | توکن: {token.Info.Model}");
        }
        return sb.ToString().TrimEnd();
    }

    internal static bool IsCaCertificate(X509Certificate2 cert)
    {
        foreach (var ext in cert.Extensions)
        {
            if (ext is X509BasicConstraintsExtension bc && bc.CertificateAuthority)
                return true;
        }
        return false;
    }

    private static string GuessVendorName(string path)
    {
        var file = Path.GetFileName(path);
        foreach (var (knownFile, vendor) in KnownWindowsLibraries)
        {
            if (string.Equals(file, knownFile, StringComparison.OrdinalIgnoreCase))
                return vendor;
        }
        return $"کتابخانه PKCS#11 ({file})";
    }

    private static bool PathEquals(string a, string b)
        => string.Equals(a, b, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private sealed class StaticPinProvider : IPinProvider
    {
        private readonly byte[]? _pinBytes;

        public StaticPinProvider(byte[]? pinBytes) => _pinBytes = pinBytes;

        public GetPinResult GetTokenPin(Pkcs11X509StoreInfo storeInfo, Pkcs11SlotInfo slotInfo, Pkcs11TokenInfo tokenInfo)
            => _pinBytes is null
                ? new GetPinResult(true, null!)
                : new GetPinResult(false, (byte[])_pinBytes.Clone());

        public GetPinResult GetKeyPin(
            Pkcs11X509StoreInfo storeInfo,
            Pkcs11SlotInfo slotInfo,
            Pkcs11TokenInfo tokenInfo,
            Pkcs11X509CertificateInfo certificateInfo)
            => _pinBytes is null
                ? new GetPinResult(true, null!)
                : new GetPinResult(false, (byte[])_pinBytes.Clone());
    }
}

public sealed record ValidatedPkcs11Certificate(
    string LibraryPath,
    string ExactCertificateAlias,
    string Subject,
    string Issuer,
    string SerialNumber,
    string Thumbprint,
    DateTime NotBeforeUtc,
    DateTime NotAfterUtc);
