using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Server.Kestrel.Https;

namespace Inventory.Api.Services;

/// <summary>
/// HTTPS هم‌زمان برای «داخل شبکه» و «بیرون از شبکه».
///
///   • داخل شبکه (http://192.168.x.x) ← گواهی داخلی سامانه (CA خودِ نرم‌افزار؛ یک‌بار نصب ca.crt روی هر دستگاه).
///   • بیرون از شبکه با دامنهٔ واقعی (erp.example.com) ← گواهی عمومی معتبر (مثلاً Let's Encrypt با win-acme)
///     که روی همهٔ گوشی‌ها بدون نصب چیزی معتبر است.
///
/// انتخاب گواهی در لحظهٔ اتصال و بر اساس SNI (نامی که مرورگر درخواست کرده) انجام می‌شود:
/// اگر نام درخواستی در گواهی عمومی باشد همان ارسال می‌شود، وگرنه گواهی داخلی.
/// فایل گواهی عمومی هر بار که عوض شود (تمدید خودکار Let's Encrypt) بدون ری‌استارت دوباره خوانده می‌شود.
///
/// تنظیمات (appsettings.json یا متغیر محیطی با «__»؛ مثلاً Https__PublicCertificate__Path):
///   Https:ExtraPorts                 پورت‌های HTTPS اضافه با کاما؛ مثلاً "443"
///   Https:PublicPort                 پورتی که کاربران بیرونی می‌بینند (برای هدایت خودکار)؛ پیش‌فرض = Https:Port
///   Https:RedirectHttp               true = بازکردن صفحه با http به https هدایت شود (پیش‌فرض false)
///   Https:PublicCertificate:Path     مسیر فایل pfx گواهی عمومی؛ خالی = &lt;DataDir&gt;\public\public.pfx
///   Https:PublicCertificate:Password رمز pfx؛ خالی = خواندن از فایل «public.pfx.pwd» کنار همان فایل
/// </summary>
public sealed partial class HttpsCertificates
{
    private static readonly TimeSpan PublicRecheckInterval = TimeSpan.FromSeconds(30);

    private readonly object _publicLock = new();
    private string _publicPath = "";
    private string? _publicPasswordConfigured;
    private PublicCert? _public;
    private DateTime _publicStamp = DateTime.MinValue;
    private DateTime _publicLastCheck = DateTime.MinValue;
    private string _publicError = "";
    private SslStreamCertificateContext? _internalContext;

    public IReadOnlyList<int> ExtraPorts { get; private set; } = Array.Empty<int>();
    public bool RedirectHttp { get; private set; }
    public int PublicPort { get; private set; }

    /// <summary>همهٔ پورت‌های HTTPS که باید شنیده شوند (پورت اصلی + اضافه‌ها).</summary>
    public IEnumerable<int> AllPorts => new[] { Port }.Concat(ExtraPorts).Distinct();

    private sealed record PublicCert(X509Certificate2 Leaf, SslStreamCertificateContext Context, IReadOnlyList<string> Names);

    private void ConfigureExtras(IConfiguration cfg)
    {
        // پورت‌های اضافه (مثلاً 443 برای دسترسی بیرونی بدون نوشتن شماره پورت)
        var extras = new List<int>();
        foreach (var part in (cfg["Https:ExtraPorts"] ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!int.TryParse(part, out var extra) || extra is <= 0 or >= 65536 || extra == Port || extras.Contains(extra)) continue;
            if (!IsPortFree(extra)) { Note($"پورت HTTPS اضافهٔ {extra} در حال استفاده است و نادیده گرفته شد (مثلاً IIS یا Skype آن را گرفته است)."); continue; }
            extras.Add(extra);
        }
        ExtraPorts = extras;

        PublicPort = int.TryParse(cfg["Https:PublicPort"], out var pp) && pp is > 0 and < 65536 ? pp : Port;
        RedirectHttp = cfg.GetValue<bool?>("Https:RedirectHttp") ?? false;

        var path = cfg["Https:PublicCertificate:Path"];
        _publicPath = string.IsNullOrWhiteSpace(path) ? Path.Combine(DataDir, "public", "public.pfx") : path.Trim();
        _publicPasswordConfigured = cfg["Https:PublicCertificate:Password"];

        var pub = GetPublic(force: true);
        if (pub != null)
            Note($"گواهی عمومی بارگذاری شد ({string.Join(" ، ", pub.Names)}) — اعتبار تا {pub.Leaf.NotAfter:yyyy-MM-dd}.");
        else if (!string.IsNullOrEmpty(_publicError))
            Note(_publicError);
        else
            Note($"گواهی عمومی (دامنه) یافت نشد؛ فقط گواهی داخلی استفاده می‌شود. برای دسترسی بیرونی بدون نصب گواهی: tools\\https\\setup-https.ps1 -Domain ... -LetsEncrypt");

        if (ExtraPorts.Count > 0) Note($"پورت‌های HTTPS اضافه: {string.Join(", ", ExtraPorts)}");
        if (RedirectHttp) Note("هدایت خودکار HTTP → HTTPS فعال است (به‌جز /https-setup و /ca.crt).");
    }

    /// <summary>گزینه‌های TLS برای Kestrel: انتخاب گواهی بر اساس نام درخواستی (SNI).</summary>
    public TlsHandshakeCallbackOptions CreateTlsOptions() => new()
    {
        HandshakeTimeout = TimeSpan.FromSeconds(10),
        OnConnection = ctx =>
        {
            var context = SelectContext(ctx.ClientHelloInfo.ServerName);
            return ValueTask.FromResult(new SslServerAuthenticationOptions
            {
                ServerCertificateContext = context,
                ClientCertificateRequired = false
            });
        }
    };

    private SslStreamCertificateContext SelectContext(string? serverName)
    {
        if (IsPublicHost(serverName)) return _public!.Context;
        return _internalContext ??= SslStreamCertificateContext.Create(Certificate!, additionalCertificates: null, offline: true);
    }

    /// <summary>آیا این نام میزبان با گواهی عمومی (دامنه) سرو می‌شود؟</summary>
    public bool IsPublicHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host) || IPAddress.TryParse(host, out _)) return false;
        var pub = GetPublic(force: false);
        return pub != null && pub.Names.Any(n => NameMatches(n, host));
    }

    /// <summary>آیا برای این نام میزبان گواهی معتبری داریم (عمومی یا داخلی)؟ برای هدایت امن HTTP → HTTPS.</summary>
    public bool HasCertificateFor(string host)
    {
        if (IsPublicHost(host)) return true;
        var hosts = ReadMeta()?.Hosts ?? new List<string>();
        return hosts.Any(h => h.Equals(host, StringComparison.OrdinalIgnoreCase));
    }

    private static bool NameMatches(string certName, string host)
    {
        host = host.TrimEnd('.');
        if (certName.Equals(host, StringComparison.OrdinalIgnoreCase)) return true;
        if (certName.StartsWith("*.", StringComparison.Ordinal))
        {
            var dot = host.IndexOf('.');
            return dot > 0 && host[(dot + 1)..].Equals(certName[2..], StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    private PublicCert? GetPublic(bool force)
    {
        var now = DateTime.UtcNow;
        if (!force && now - _publicLastCheck < PublicRecheckInterval) return _public;

        lock (_publicLock)
        {
            if (!force && now - _publicLastCheck < PublicRecheckInterval) return _public;
            _publicLastCheck = now;
            try
            {
                if (string.IsNullOrEmpty(_publicPath) || !File.Exists(_publicPath))
                {
                    _public = null;
                    _publicStamp = DateTime.MinValue;
                    return null;
                }

                var stamp = File.GetLastWriteTimeUtc(_publicPath);
                if (_public != null && stamp == _publicStamp) return _public;

                var loaded = LoadPublic(_publicPath, ResolvePublicPassword());
                _public = loaded;
                _publicStamp = stamp;
                _publicError = "";
                Console.WriteLine($"[HTTPS] گواهی عمومی (دوباره) خوانده شد: {string.Join(", ", loaded.Names)} — اعتبار تا {loaded.Leaf.NotAfter:yyyy-MM-dd}");
            }
            catch (Exception ex)
            {
                // گواهی قبلی (اگر بود) حفظ می‌شود تا تمدید ناقص، سایت را از کار نیندازد.
                _publicError = $"بارگذاری گواهی عمومی از «{_publicPath}» ناموفق بود: {ex.Message}";
                Console.WriteLine($"[HTTPS] {_publicError}");
            }
            return _public;
        }
    }

    private string? ResolvePublicPassword()
    {
        if (!string.IsNullOrEmpty(_publicPasswordConfigured)) return _publicPasswordConfigured;
        var pwdFile = _publicPath + ".pwd";
        return File.Exists(pwdFile) ? File.ReadAllText(pwdFile).Trim() : null;
    }

    private static PublicCert LoadPublic(string path, string? password)
    {
        var flags = OperatingSystem.IsWindows()
            ? X509KeyStorageFlags.Exportable
            : X509KeyStorageFlags.EphemeralKeySet | X509KeyStorageFlags.Exportable;

        var all = new X509Certificate2Collection();
#pragma warning disable SYSLIB0057 // جایگزین جدید فقط در .NET 9 موجود است
        all.Import(File.ReadAllBytes(path), password, flags);
#pragma warning restore SYSLIB0057

        var leaf = all.Cast<X509Certificate2>().FirstOrDefault(c => c.HasPrivateKey)
                   ?? throw new InvalidOperationException("فایل pfx کلید خصوصی ندارد.");
        var chain = new X509Certificate2Collection();
        foreach (var c in all) if (!ReferenceEquals(c, leaf) && c.Subject != c.Issuer) chain.Add(c); // گواهی‌های میانی (بدون ریشه)

        if (leaf.NotAfter.ToUniversalTime() < DateTime.UtcNow)
            Console.WriteLine($"[HTTPS] هشدار: گواهی عمومی منقضی شده است ({leaf.NotAfter:yyyy-MM-dd}). تمدید خودکار win-acme را بررسی کنید.");

        var names = new List<string>();
        foreach (var ext in leaf.Extensions)
            if (ext is X509SubjectAlternativeNameExtension san)
                foreach (var dns in san.EnumerateDnsNames())
                    if (!names.Contains(dns, StringComparer.OrdinalIgnoreCase)) names.Add(dns);
        var cn = leaf.GetNameInfo(X509NameType.DnsName, false);
        if (!string.IsNullOrWhiteSpace(cn) && !names.Contains(cn, StringComparer.OrdinalIgnoreCase)) names.Add(cn);
        if (names.Count == 0) throw new InvalidOperationException("در گواهی عمومی هیچ نام دامنه‌ای پیدا نشد.");

        var context = SslStreamCertificateContext.Create(leaf, chain, offline: true);
        return new PublicCert(leaf, context, names);
    }

    private PublicCertificateFacts DescribePublic()
    {
        var pub = GetPublic(force: false);
        return new PublicCertificateFacts
        {
            Path = _publicPath,
            Loaded = pub != null,
            Names = pub?.Names.ToList() ?? new List<string>(),
            NotAfterUtc = pub?.Leaf.NotAfter.ToUniversalTime(),
            Issuer = pub?.Leaf.Issuer ?? "",
            Fingerprint = pub != null ? Fingerprint(pub.Leaf) : "",
            Error = _publicError
        };
    }
}

public sealed class PublicCertificateFacts
{
    public string Path { get; set; } = "";
    public bool Loaded { get; set; }
    public List<string> Names { get; set; } = new();
    public DateTime? NotAfterUtc { get; set; }
    public string Issuer { get; set; } = "";
    public string Fingerprint { get; set; } = "";
    public string Error { get; set; } = "";
}
