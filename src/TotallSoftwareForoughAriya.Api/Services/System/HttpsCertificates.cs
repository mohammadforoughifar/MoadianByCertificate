using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace Inventory.Api.Services;

/// <summary>
/// راه‌اندازی HTTPS داخلی برای سامانه.
///
/// چرا لازم است؟ مرورگرها (Chrome/Edge/Firefox) «اعلان سیستمی» و Service Worker را فقط در
/// «زمینهٔ امن» (Secure Context) فعال می‌کنند: HTTPS معتبر یا localhost. روی http://192.168.x.x اعلان
/// سیستمی به هیچ عنوان فعال نمی‌شود. این سرویس یک «مرجع صدور گواهی (CA)» داخلی و یک گواهی سرور
/// برای همهٔ آی‌پی‌های شبکهٔ محلی می‌سازد تا با نصب فایل CA روی گوشی/رایانه‌ها، نشانی
/// https://<آی‌پی>:<پورت> معتبر شود و اعلان‌ها فعال گردند.
///
/// خروجی‌ها در پوشهٔ دادهٔ پایدار ذخیره می‌شوند:
///   ca.crt      → فایل گواهی مرجع (برای نصب روی گوشی/ویندوز/iOS)
///   server.pfx  → گواهی سرور (برای Kestrel و در صورت نیاز برای IIS)
///   meta.json   → آی‌پی‌ها/نام‌های داخل گواهی، اثر انگشت و رمز فایل pfx
/// </summary>
public sealed partial class HttpsCertificates
{
    public const int DefaultPort = 5443;

    private readonly string _dir;
    private readonly List<string> _log = new();

    public bool Enabled { get; private init; }
    public int Port { get; private init; }
    public X509Certificate2? Certificate { get; private init; }
    public string DataDir => _dir;
    public IReadOnlyList<string> Messages => _log;
    public string CaPath => Path.Combine(_dir, "ca.crt");
    private string CaKeyPath => Path.Combine(_dir, "ca.key");
    public string PfxPath => Path.Combine(_dir, "server.pfx");

    private HttpsCertificates(string dir, bool enabled, int port, X509Certificate2? cert)
    {
        _dir = dir;
        Enabled = enabled;
        Port = port;
        Certificate = cert;
    }

    private void Note(string message) => _log.Add(message);

    /// <summary>آماده‌سازی گواهی‌ها پیش از ساخت وب‌هاست (چون Kestrel در زمان راه‌اندازی به گواهی نیاز دارد).</summary>
    public static HttpsCertificates Prepare(IHostEnvironment env, IConfiguration cfg, bool skipPreparation = false)
    {
        var enabled = cfg.GetValue<bool?>("Https:Enabled") ?? true;
        var portText = Environment.GetEnvironmentVariable("HTTPS_PORT") ?? cfg["Https:Port"];
        var port = int.TryParse(portText, out var p) && p is > 0 and < 65536 ? p : DefaultPort;
        // EF/maintenance must not touch certificates, probe ports, or create certificate directories.
        if (skipPreparation) return new HttpsCertificates("", false, port, null);
        var dir = ResolveDataDir(env, cfg);
        var result = new HttpsCertificates(dir, enabled, port, null);

        if (!enabled)
        {
            result.Note($"HTTPS داخلی خاموش است (Https:Enabled=false). تنظیم Https:Enabled=true برای فعال‌سازی اعلان سیستمی روی شبکهٔ محلی.");
            return result;
        }

        try
        {
            var cert = LoadOrCreate(result, cfg);
            var listening = IsPortFree(port);
            if (!listening)
            {
                result.Note($"پورت {port} در حال استفاده است؛ HTTPS داخلی فعال نشد. پورت دیگری با Https:Port یا متغیر HTTPS_PORT تنظیم کنید.");
                var busy = new HttpsCertificates(dir, false, port, null);
                busy._log.AddRange(result._log);
                return busy;
            }
            var ready = new HttpsCertificates(dir, true, port, cert);
            ready._log.AddRange(result._log); // پیام‌های ساخت گواهی در کنسول هم دیده شوند
            ready.ConfigureExtras(cfg);        // گواهی عمومی (Let's Encrypt)، پورت‌های اضافه، هدایت HTTP → HTTPS
            return ready;
        }
        catch (Exception ex)
        {
            result.Note($"ساخت/بارگذاری گواهی HTTPS ناموفق بود: {ex.Message}");
            return result;
        }
    }

    private static X509Certificate2 LoadOrCreate(HttpsCertificates target, IConfiguration cfg)
    {
        var dir = target._dir;
        Directory.CreateDirectory(dir);
        var metaPath = Path.Combine(dir, "meta.json");
        var currentHosts = CollectHosts(cfg); // شامل Https:Hosts (دامنه/آی‌پی عمومی که روی کارت شبکه نیست)

        Meta? meta = null;
        try { if (File.Exists(metaPath)) meta = JsonSerializer.Deserialize<Meta>(File.ReadAllText(metaPath)); } catch { }

        var caExists = File.Exists(target.CaPath);
        var pfxExists = File.Exists(target.PfxPath);
        var needsNewServerCert = meta == null || !caExists || !pfxExists
                                 || !currentHosts.SequenceEqual(meta.Hosts)
                                 || meta.ServerNotAfterUtc <= DateTime.UtcNow.AddDays(30);

        if (needsNewServerCert)
        {
            // Old releases persisted only ca.crt. Its private key cannot be recovered from a certificate.
            // Preserve a working legacy server until renewal is necessary; then rotate explicitly with a warning.
            var reuseCa = caExists && File.Exists(target.CaKeyPath);
            using var existingCa = reuseCa ? X509Certificate2.CreateFromPemFile(target.CaPath, target.CaKeyPath) : null;
            reuseCa = existingCa is not null && existingCa.HasPrivateKey
                && existingCa.NotAfter.ToUniversalTime() > DateTime.UtcNow.AddDays(31);
            using var ca = reuseCa ? null : CreateRootCa();
            var issuer = reuseCa ? existingCa! : ca!;
            if (!reuseCa)
            {
                if (caExists)
                {
                    var backup = target.CaPath + "." + Guid.NewGuid().ToString("N") + ".bak";
                    File.Copy(target.CaPath, backup);
                    target.Note("کلید خصوصی CA قبلی موجود نیست یا CA در آستانهٔ انقضا است؛ CA جدید ساخته شد. فایل ca.crt جدید را روی دستگاه‌ها دوباره نصب کنید.");
                }
                SaveCaKey(target.CaKeyPath, issuer);
                SavePem(target.CaPath, issuer);
            }

            var hostList = CollectHosts(cfg);
            var (serverCertificate, pfxPassword) = CreateServerCertificate(issuer, hostList);
            using var server = serverCertificate;
            File.WriteAllBytes(target.PfxPath, server.Export(X509ContentType.Pfx, pfxPassword));

            meta = new Meta
            {
                Hosts = hostList,
                CreatedAtUtc = DateTime.UtcNow,
                ServerNotAfterUtc = server.NotAfter.ToUniversalTime(),
                CaNotAfterUtc = issuer.NotAfter.ToUniversalTime(),
                PfxPassword = pfxPassword,
                CaFingerprint = Fingerprint(issuer),
                ServerFingerprint = Fingerprint(server)
            };
            File.WriteAllText(metaPath, JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true }));
            target.Note(reuseCa
                ? "گواهی سرور برای آی‌پی‌های فعلی شبکه بازسازی شد (فایل CA تغییر نکرده و نصب‌های قبلی معتبر می‌مانند)."
                : "مرجع صدور گواهی (CA) و گواهی سرور ساخته شد.");
        }

#pragma warning disable SYSLIB0057 // جایگزین جدید فقط در .NET 9 موجود است
        var loaded = new X509Certificate2(File.ReadAllBytes(target.PfxPath), meta!.PfxPassword,
            OperatingSystem.IsWindows() ? X509KeyStorageFlags.Exportable : X509KeyStorageFlags.EphemeralKeySet | X509KeyStorageFlags.Exportable);
#pragma warning restore SYSLIB0057
        target.Note($"گواهی سرور آماده است؛ نام‌ها/آی‌پی‌های گواهی: {string.Join(" ، ", meta.Hosts)}");
        return loaded;
    }

    /// <summary>ساخت/بازسازی گواهی‌ها با دستور مدیر (مثلاً پس از تغییر آی‌پی سرور در شبکه).</summary>
    public static HttpsCertificates Regenerate(IHostEnvironment env, IConfiguration cfg, bool newCa)
    {
        var dir = ResolveDataDir(env, cfg);
        if (newCa)
        {
            foreach (var file in new[] { "ca.crt", "ca.key", "server.pfx", "meta.json" })
            {
                var path = Path.Combine(dir, file);
                try { if (File.Exists(path)) File.Delete(path); } catch { }
            }
        }
        else
        {
            var meta = Path.Combine(dir, "meta.json");
            try { if (File.Exists(meta)) File.Delete(meta); } catch { }
        }
        return Prepare(env, cfg);
    }

    /// <summary>مشخصات گواهی برای نمایش/صدور در IIS.</summary>
    public HttpsFacts Describe(IHostEnvironment env, IConfiguration cfg)
    {
        var meta = ReadMeta();
        return new HttpsFacts
        {
            Enabled = Enabled,
            Port = Port,
            DataDir = _dir,
            Hosts = meta?.Hosts ?? new List<string>(),
            CaFingerprint = meta?.CaFingerprint ?? "",
            ServerFingerprint = meta?.ServerFingerprint ?? "",
            ServerNotAfterUtc = meta?.ServerNotAfterUtc,
            CaNotAfterUtc = meta?.CaNotAfterUtc,
            PfxPassword = meta?.PfxPassword ?? "",
            HasCaFile = File.Exists(CaPath),
            HasPfxFile = File.Exists(PfxPath),
            ExtraPorts = ExtraPorts.ToList(),
            RedirectHttp = RedirectHttp,
            PublicPort = PublicPort,
            PublicCertificate = DescribePublic(),
            Messages = _log.ToList()
        };
    }

    public Meta? ReadMeta()
    {
        try
        {
            var path = Path.Combine(_dir, "meta.json");
            return File.Exists(path) ? JsonSerializer.Deserialize<Meta>(File.ReadAllText(path)) : null;
        }
        catch { return null; }
    }

    // ============================ ساخت گواهی ============================

    private static X509Certificate2 CreateRootCa()
    {
        using var key = RSA.Create(3072);
        var req = new CertificateRequest("CN=Totall Local Certificate Authority, O=Totall ERP", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        req.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(req.PublicKey, false));
        var ca = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(10));
        return ca;
    }

    private static (X509Certificate2 Server, string Password) CreateServerCertificate(X509Certificate2 ca, List<string> hosts)
    {
        using var key = RSA.Create(2048);
        var primaryName = hosts.FirstOrDefault(h => !IPAddress.TryParse(h, out _)) ?? "totall-local";
        var req = new CertificateRequest($"CN={primaryName}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));
        req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.1") }, false)); // serverAuth
        req.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(req.PublicKey, false));

        var san = new SubjectAlternativeNameBuilder();
        var hasDns = false;
        foreach (var host in hosts)
        {
            if (IPAddress.TryParse(host, out var ip)) san.AddIpAddress(ip);
            else { san.AddDnsName(host); hasDns = true; }
        }
        if (!hasDns) san.AddDnsName("localhost");
        req.CertificateExtensions.Add(san.Build());

        var serial = RandomNumberGenerator.GetBytes(16);
        var notAfter = DateTimeOffset.UtcNow.AddYears(3);
        var caExpiry = new DateTimeOffset(ca.NotAfter.ToUniversalTime()).AddMinutes(-1);
        if (notAfter > caExpiry) notAfter = caExpiry;
        using var signed = req.Create(ca, DateTimeOffset.UtcNow.AddDays(-1), notAfter, serial);
        var server = signed.CopyWithPrivateKey(key);
        var password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).Replace("+", "").Replace("/", "").Replace("=", "");
        return (server, password);
    }

    private static void SaveCaKey(string path, X509Certificate2 ca)
    {
        using var key = ca.GetRSAPrivateKey()
            ?? throw new InvalidOperationException("کلید خصوصی CA در دسترس نیست.");
        // Restrict Unix permissions at creation, not after writing the private key.
        // On Windows the data directory must be ACL-restricted to the service identity/admins.
        var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        using (var stream = new FileStream(path, options))
        using (var writer = new StreamWriter(stream))
            writer.Write(key.ExportPkcs8PrivateKeyPem());
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    private static void SavePem(string path, X509Certificate2 cert)
    {
        var pem = new string(PemEncoding.Write("CERTIFICATE", cert.RawData));
        File.WriteAllText(path, pem);
        if (!OperatingSystem.IsWindows())
        {
            try { File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead); } catch { }
        }
    }

    public static string Fingerprint(X509Certificate2 cert)
        => string.Join(":", cert.GetCertHash(HashAlgorithmName.SHA256).Select(b => b.ToString("X2")));

    // ============================ محیط و پورت ============================

    /// <summary>نام‌ها/آی‌پی‌های داخل گواهی: نام دستگاه، localhost و همهٔ آی‌پی‌های شبکهٔ محلی + موارد دلخواه تنظیمات.</summary>
    public static List<string> CollectHosts(IConfiguration? cfg)
    {
        var hosts = new List<string>();
        void Add(string? value)
        {
            value = value?.Trim();
            if (!string.IsNullOrWhiteSpace(value) && !hosts.Contains(value, StringComparer.OrdinalIgnoreCase)) hosts.Add(value);
        }

        Add("localhost");
        try { Add(Dns.GetHostName()); } catch { }
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (var uni in nic.GetIPProperties().UnicastAddresses)
                {
                    if (uni.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    Add(uni.Address.ToString());
                }
            }
        }
        catch { }
        foreach (var extra in (cfg?["Https:Hosts"] ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) Add(extra);
        return hosts;
    }

    private static string ResolveDataDir(IHostEnvironment env, IConfiguration cfg)
    {
        var candidates = new List<string>();
        var configured = Environment.GetEnvironmentVariable("HTTPS_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(configured)) candidates.Add(configured!);
        var configDir = cfg["Https:DataDir"];
        if (!string.IsNullOrWhiteSpace(configDir)) candidates.Add(configDir!);
        if (OperatingSystem.IsWindows()) candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Totall", "https"));
        else candidates.Add("/var/lib/totall/https");
        candidates.Add(Path.Combine(env.ContentRootPath, "App_Data", "https"));

        foreach (var candidate in candidates)
        {
            try
            {
                Directory.CreateDirectory(candidate);
                var probe = Path.Combine(candidate, ".write-test");
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                return candidate;
            }
            catch { }
        }
        return Path.Combine(env.ContentRootPath, "App_Data", "https");
    }

    private static bool IsPortFree(int port)
    {
        try
        {
            var listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            listener.Stop();
            return true;
        }
        catch { return false; }
    }

    /// <summary>محتوای فایل CA برای دانلود (با نام ca.crt).</summary>
    public string? ReadCaPem() => File.Exists(CaPath) ? File.ReadAllText(CaPath) : null;

    /// <summary>فایل pfx به‌صورت Base64 برای درون‌ریزی در IIS.</summary>
    public string? ReadPfxBase64() => File.Exists(PfxPath) ? Convert.ToBase64String(File.ReadAllBytes(PfxPath)) : null;

    public sealed class Meta
    {
        public List<string> Hosts { get; set; } = new();
        public DateTime CreatedAtUtc { get; set; }
        public DateTime ServerNotAfterUtc { get; set; }
        public DateTime CaNotAfterUtc { get; set; }
        public string PfxPassword { get; set; } = "";
        public string CaFingerprint { get; set; } = "";
        public string ServerFingerprint { get; set; } = "";
    }
}

/// <summary>خلاصهٔ وضعیت HTTPS برای نمایش در تنظیمات.</summary>
public sealed class HttpsFacts
{
    public bool Enabled { get; set; }
    public int Port { get; set; }
    public string DataDir { get; set; } = "";
    public List<string> Hosts { get; set; } = new();
    public string CaFingerprint { get; set; } = "";
    public string ServerFingerprint { get; set; } = "";
    public DateTime? ServerNotAfterUtc { get; set; }
    public DateTime? CaNotAfterUtc { get; set; }
    public string PfxPassword { get; set; } = "";
    public bool HasCaFile { get; set; }
    public bool HasPfxFile { get; set; }
    public List<int> ExtraPorts { get; set; } = new();
    public bool RedirectHttp { get; set; }
    public int PublicPort { get; set; }
    public PublicCertificateFacts PublicCertificate { get; set; } = new();
    public List<string> Messages { get; set; } = new();
}
