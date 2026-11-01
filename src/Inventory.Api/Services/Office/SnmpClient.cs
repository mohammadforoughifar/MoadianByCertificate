using System.Net;
using System.Net.Sockets;
using Lextm.SharpSnmpLib;
using Lextm.SharpSnmpLib.Messaging;
using Lextm.SharpSnmpLib.Security;

namespace Inventory.Api.Services.Office;

/// <summary>مشخصات اتصال SNMP به یک دستگاه (اسرار به‌صورت متن ساده و فقط در حافظه).</summary>
public sealed class SnmpTarget
{
    public string Host { get; init; } = "";
    public int Port { get; init; } = 161;
    /// <summary>v1 | v2c | v3</summary>
    public string Version { get; init; } = "v2c";
    public string? Community { get; init; }
    public string? V3User { get; init; }
    /// <summary>None | MD5 | SHA1 | SHA256</summary>
    public string V3AuthProtocol { get; init; } = "SHA1";
    public string? V3AuthPassword { get; init; }
    /// <summary>None | DES | AES</summary>
    public string V3PrivProtocol { get; init; } = "AES";
    public string? V3PrivPassword { get; init; }
    public int TimeoutMs { get; init; } = 2000;
    public int Retries { get; init; } = 1;
}

/// <summary>خطای قابل‌نمایش به کاربر در ارتباط SNMP.</summary>
public sealed class SnmpQueryException : Exception
{
    public bool IsTimeout { get; }
    /// <summary>میزبان زنده است ولی پورت UDP SNMP بسته است (ICMP Port Unreachable ← در ویندوز ConnectionReset).</summary>
    public bool IsPortClosed { get; }
    public SnmpQueryException(string message, bool isTimeout = false, Exception? inner = null, bool isPortClosed = false) : base(message, inner)
    {
        IsTimeout = isTimeout;
        IsPortClosed = isPortClosed;
    }
}

/// <summary>
/// نشست SNMP فقط‌خواندنی (GET / WALK) روی Lextm.SharpSnmpLib — پشتیبانی v1، v2c و v3 (authNoPriv/authPriv).
/// هیچ عملیات SET در این کلاس وجود ندارد.
/// متدها همگام (blocking) هستند؛ فراخواننده باید آن‌ها را داخل Task.Run اجرا کند.
/// </summary>
public sealed class SnmpSession
{
    private readonly SnmpTarget _t;
    private readonly IPEndPoint _endpoint;
    private readonly VersionCode _version;
    private readonly OctetString _community;
    private IPrivacyProvider? _privacy;
    private ISnmpMessage? _report;

    public SnmpSession(SnmpTarget target)
    {
        _t = target;
        _endpoint = new IPEndPoint(ResolveHost(target.Host), target.Port is > 0 and < 65536 ? target.Port : 161);
        _version = target.Version switch { "v1" => VersionCode.V1, "v3" => VersionCode.V3, _ => VersionCode.V2 };
        _community = new OctetString(_version == VersionCode.V3 ? (target.V3User ?? "") : (target.Community ?? "public"));
        if (_version == VersionCode.V3 && string.IsNullOrWhiteSpace(target.V3User))
            throw new SnmpQueryException("برای SNMPv3 نام کاربری الزامی است.");
    }

    public string Endpoint => _endpoint.ToString();

    private static IPAddress ResolveHost(string host)
    {
        host = (host ?? "").Trim();
        if (IPAddress.TryParse(host, out var ip)) return ip;
        try
        {
            var addrs = Dns.GetHostAddresses(host);
            var v4 = addrs.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork) ?? addrs.FirstOrDefault();
            if (v4 != null) return v4;
        }
        catch { /* پایین‌تر خطای خوانا می‌دهیم */ }
        throw new SnmpQueryException($"آدرس «{host}» معتبر نیست یا قابل resolve نیست.");
    }

    // ------------------------------------------------------------------ v3

    private void EnsureV3()
    {
        if (_version != VersionCode.V3 || _privacy != null) return;

        IAuthenticationProvider auth = (_t.V3AuthProtocol ?? "").ToUpperInvariant() switch
        {
            "NONE" or "" => DefaultAuthenticationProvider.Instance,
            "MD5" => new MD5AuthenticationProvider(new OctetString(_t.V3AuthPassword ?? "")),
            "SHA256" => new SHA256AuthenticationProvider(new OctetString(_t.V3AuthPassword ?? "")),
            _ => new SHA1AuthenticationProvider(new OctetString(_t.V3AuthPassword ?? "")),
        };
        if (auth is DefaultAuthenticationProvider)
            _privacy = DefaultPrivacyProvider.DefaultPair;
        else
            _privacy = (_t.V3PrivProtocol ?? "").ToUpperInvariant() switch
            {
                "DES" => new DESPrivacyProvider(new OctetString(_t.V3PrivPassword ?? ""), auth),
                "AES" => new AESPrivacyProvider(new OctetString(_t.V3PrivPassword ?? ""), auth),
                _ => new DefaultPrivacyProvider(auth),
            };

        // کشف engineId/boots/time دستگاه (Report) — پیش‌نیاز هر درخواست v3
        _report = WithRetry(() => Messenger.GetNextDiscovery(SnmpType.GetRequestPdu).GetResponse(_t.TimeoutMs, _endpoint));
    }

    // ------------------------------------------------------------------ GET

    /// <summary>
    /// GET چند OID. OIDهای ناموجود (noSuchObject/noSuchInstance/noSuchName) در خروجی نیستند.
    /// </summary>
    public Dictionary<string, ISnmpData> Get(IEnumerable<string> oids)
    {
        var list = oids.Distinct().ToList();
        var result = new Dictionary<string, ISnmpData>();
        if (list.Count == 0) return result;
        try
        {
            foreach (var v in GetRaw(list)) Add(result, v);
        }
        catch (ErrorException) when (list.Count > 1)
        {
            // v1: یک OID ناموجود کل درخواست را با noSuchName رد می‌کند ← تک‌تک
            foreach (var oid in list)
            {
                try { foreach (var v in GetRaw(new List<string> { oid })) Add(result, v); }
                catch (ErrorException) { }
            }
        }
        return result;

        static void Add(Dictionary<string, ISnmpData> d, Variable v)
        {
            var type = v.Data.TypeCode;
            if (type is SnmpType.NoSuchObject or SnmpType.NoSuchInstance or SnmpType.EndOfMibView or SnmpType.Null) return;
            d[v.Id.ToString()] = v.Data;
        }
    }

    public ISnmpData? Get(string oid) => Get(new[] { oid }).TryGetValue(oid, out var d) ? d : null;

    private IList<Variable> GetRaw(List<string> oids)
    {
        var vars = oids.Select(o => new Variable(new ObjectIdentifier(o))).ToList();
        if (_version != VersionCode.V3)
            return WithRetry(() => Messenger.Get(_version, _endpoint, _community, vars, _t.TimeoutMs));

        EnsureV3();
        return WithRetry(() =>
        {
            var request = new GetRequestMessage(VersionCode.V3, Messenger.NextMessageId, Messenger.NextRequestId,
                _community, OctetString.Empty, vars, _privacy!, Messenger.MaxMessageSize, _report!);
            var reply = request.GetResponse(_t.TimeoutMs, _endpoint);
            if (reply is ReportMessage)
                throw new SnmpQueryException(DescribeReport(reply));
            var pdu = reply.Pdu();
            if (pdu.ErrorStatus.ToInt32() != 0)
                throw new ErrorException(pdu.ErrorStatus.ToErrorCode().ToString());
            return pdu.Variables;
        });
    }

    // ------------------------------------------------------------------ WALK

    /// <summary>پیمایش یک زیردرخت؛ حداکثر <paramref name="maxRows"/> ردیف.</summary>
    public List<Variable> Walk(string rootOid, int maxRows = 2000)
    {
        var table = new ObjectIdentifier(rootOid);
        var list = new List<Variable>();
        try
        {
            if (_version == VersionCode.V1)
                WithRetry(() => { list.Clear(); return Messenger.Walk(_version, _endpoint, _community, table, list, _t.TimeoutMs, WalkMode.WithinSubtree); });
            else
            {
                EnsureV3();
                WithRetry(() =>
                {
                    list.Clear();
                    return Messenger.BulkWalk(_version, _endpoint, _community, OctetString.Empty, table, list,
                        _t.TimeoutMs, 10, WalkMode.WithinSubtree, _privacy, _report);
                });
            }
        }
        catch (ErrorException) { /* جدول پشتیبانی نمی‌شود */ }
        catch (SnmpQueryException ex) when (!ex.IsTimeout) { }

        var prefix = rootOid + ".";
        return list.Where(v => v.Id.ToString().StartsWith(prefix, StringComparison.Ordinal)
                               && v.Data.TypeCode is not (SnmpType.NoSuchObject or SnmpType.NoSuchInstance or SnmpType.EndOfMibView))
                   .Take(maxRows).ToList();
    }

    // ------------------------------------------------------------------ helpers

    private T WithRetry<T>(Func<T> action)
    {
        var attempts = Math.Clamp(_t.Retries, 0, 3) + 1;
        for (var i = 1; ; i++)
        {
            try { return action(); }
            catch (Lextm.SharpSnmpLib.Messaging.TimeoutException ex)
            {
                if (i >= attempts)
                    throw new SnmpQueryException($"دستگاه {_endpoint} در {_t.TimeoutMs}ms پاسخ نداد (SNMP خاموش است، community/کاربر اشتباه است یا فایروال UDP {_endpoint.Port} را بسته).", true, ex);
            }
            catch (SocketException ex) when (ex.SocketErrorCode is SocketError.ConnectionReset or SocketError.ConnectionRefused)
            {
                // پاسخ قطعی است (نه گم‌شدن بسته) ← تکرار بی‌فایده است
                throw new SnmpQueryException(
                    $"پورت UDP {_endpoint.Port} روی {_endpoint.Address} بسته است — دستگاه روشن است ولی سرویس SNMP ندارد یا SNMP آن غیرفعال است (پاسخ ICMP Port Unreachable). SNMP را در تنظیمات وب دستگاه فعال کنید.",
                    true, ex, isPortClosed: true);
            }
            catch (SocketException ex)
            {
                if (i >= attempts)
                    throw new SnmpQueryException($"خطای شبکه در اتصال به {_endpoint}: {ex.Message}", true, ex);
            }
            catch (DecryptionException ex)
            {
                throw new SnmpQueryException("رمزگشایی پاسخ SNMPv3 ناموفق بود — رمز Privacy یا نوع آن (AES/DES) را بررسی کنید.", false, ex);
            }
        }
    }

    private static string DescribeReport(ISnmpMessage report)
    {
        var oid = report.Pdu().Variables.FirstOrDefault()?.Id.ToString() ?? "";
        return oid switch
        {
            "1.3.6.1.6.3.15.1.1.3.0" => "SNMPv3: نام کاربری روی دستگاه تعریف نشده است (unknownUserName).",
            "1.3.6.1.6.3.15.1.1.5.0" => "SNMPv3: رمز Authentication یا نوع آن اشتباه است (wrongDigest).",
            "1.3.6.1.6.3.15.1.1.1.0" => "SNMPv3: سطح امنیت پشتیبانی نمی‌شود (unsupportedSecLevel).",
            "1.3.6.1.6.3.15.1.1.6.0" => "SNMPv3: رمزگشایی ناموفق — رمز Privacy را بررسی کنید.",
            "1.3.6.1.6.3.15.1.1.2.0" => "SNMPv3: پنجرهٔ زمانی نامعتبر (notInTimeWindow) — دوباره تلاش کنید.",
            _ => $"SNMPv3: دستگاه درخواست را رد کرد ({oid})."
        };
    }
}
