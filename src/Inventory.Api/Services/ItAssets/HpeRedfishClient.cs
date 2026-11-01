using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Inventory.Api.Services.ItAssets;

/// <summary>اعتبارنامه‌ای که فقط در حافظه و هنگام فراخوانی Redfish استفاده می‌شود.</summary>
public sealed record RedfishCredentials(string Username, string Password);

/// <summary>خلاصهٔ غیرمحرمانهٔ خوانده‌شده از iLO/Redfish.</summary>
public sealed class RedfishSnapshot
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    /// <summary>CertUntrusted | CertChanged | Auth | Network | Timeout | Protocol — برای تصمیم UI؛ متن خطا همچنان در Error است.</summary>
    public string? ErrorCode { get; init; }
    /// <summary>گواهی‌ای که iLO ارائه داد (فقط وقتی اتصال به‌خاطر گواهی رد شده باشد).</summary>
    public IloCertificateInfo? Certificate { get; init; }
    public string Health { get; init; } = "Unknown";
    public string? PowerState { get; init; }
    public string? Manufacturer { get; init; }
    public string? Model { get; init; }
    public string? SerialNumber { get; init; }
    public string? AssetTag { get; init; }
    public string? BiosVersion { get; init; }
    public string? FirmwareVersion { get; init; }
    public string? IloVersion { get; init; }
    /// <summary>فقط دادهٔ ساخت‌یافتهٔ غیرمحرمانه؛ پاسخ خام Redfish نگه‌داری نمی‌شود.</summary>
    public IReadOnlyList<RedfishHardwareComponent> Components { get; init; } = Array.Empty<RedfishHardwareComponent>();
    public IReadOnlyList<RedfishHardwareEvent> Events { get; init; } = Array.Empty<RedfishHardwareEvent>();
}

public sealed class RedfishHardwareComponent
{
    public string Type { get; init; } = "";
    public string Key { get; init; } = "";
    public string Name { get; init; } = "";
    public string Health { get; init; } = "Unknown";
    public string? State { get; init; }
    public string? Reading { get; init; }
    public string? Model { get; init; }
    public string? SerialNumber { get; init; }
    public string? Message { get; init; }
}

public sealed class RedfishHardwareEvent
{
    public string Key { get; init; } = "";
    public string Severity { get; init; } = "Unknown";
    public string Message { get; init; } = "";
    public string? Origin { get; init; }
    public DateTime? OccurredAt { get; init; }
}

/// <summary>مشخصات غیرمحرمانهٔ گواهی TLS ارائه‌شده توسط iLO.</summary>
public sealed class IloCertificateInfo
{
    /// <summary>SHA-256 کل گواهی (DER) به‌صورت هگز بزرگ، بدون جداکننده.</summary>
    public string Thumbprint { get; init; } = "";
    public string Subject { get; init; } = "";
    public string Issuer { get; init; } = "";
    public DateTime NotBefore { get; init; }
    public DateTime NotAfter { get; init; }
    /// <summary>زنجیرهٔ گواهی و نام میزبان از نظر سیستم‌عامل سرور برنامه معتبر است.</summary>
    public bool SystemTrusted { get; init; }
    public bool SelfSigned { get; init; }
}

/// <summary>
/// کلاینت فقط-خواندنی Redfish برای HPE iLO. درخواست‌ها فقط روی HTTPS، بدون دنبال‌کردن redirect
/// و فقط به همان origin ثبت‌شده ارسال می‌شوند تا اعتبارنامه به مقصد دیگری نرود.
/// </summary>
public sealed class HpeRedfishClient
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);

    // IHttpClientFactory فقط برای سازگاری DI نگه داشته شده است؛ هر فراخوانی handler خودش را دارد
    // تا بررسی گواهی بتواند به اثر انگشت تأییدشدهٔ همان سرور مقید شود.
    public HpeRedfishClient(IHttpClientFactory clients) { _ = clients; }

    /// <summary>اعتبارنامه + HttpClient مقید به گواهی همان iLO برای یک بار خواندن.</summary>
    private sealed record Session(RedfishCredentials Credentials, HttpClient Http);

    /// <summary>
    /// تصمیم دربارهٔ گواهی iLO: معتبر از نظر سیستم‌عامل ← قبول؛ در غیر این صورت فقط اگر
    /// SHA-256 آن با اثر انگشت تأییدشدهٔ کاربر برابر باشد. گواهی دیده‌شده برای نمایش نگه داشته می‌شود.
    /// </summary>
    private sealed class CertificateGate
    {
        private readonly string? _pinned;
        public CertificateGate(string? pinned) => _pinned = NormalizeThumbprint(pinned);
        public IloCertificateInfo? Seen { get; private set; }
        public string? Rejected { get; private set; }

        public bool Validate(X509Certificate? cert, SslPolicyErrors errors)
        {
            if (cert is null) { Rejected = "CertUntrusted"; return false; }
            Seen = Describe(cert, errors);
            if (errors == SslPolicyErrors.None) return true;
            if (_pinned is not null && string.Equals(_pinned, Seen.Thumbprint, StringComparison.Ordinal)) return true;
            Rejected = _pinned is null ? "CertUntrusted" : "CertChanged";
            return false;
        }
    }

    public static string? NormalizeThumbprint(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var hex = new string(value.Where(Uri.IsHexDigit).ToArray()).ToUpperInvariant();
        return hex.Length == 64 ? hex : null;
    }

    private static IloCertificateInfo Describe(X509Certificate cert, SslPolicyErrors errors)
    {
        using var x = new X509Certificate2(cert);
        return new IloCertificateInfo
        {
            Thumbprint = Convert.ToHexString(SHA256.HashData(x.RawData)),
            Subject = x.Subject,
            Issuer = x.Issuer,
            NotBefore = x.NotBefore,
            NotAfter = x.NotAfter,
            SystemTrusted = errors == SslPolicyErrors.None,
            SelfSigned = string.Equals(x.Subject, x.Issuer, StringComparison.Ordinal)
        };
    }

    private static (HttpClient Client, SocketsHttpHandler Handler) CreateHttp(CertificateGate gate)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            ConnectTimeout = ConnectTimeout,
            PooledConnectionLifetime = TimeSpan.FromMinutes(1),
            UseProxy = false
        };
        handler.SslOptions.RemoteCertificateValidationCallback = (_, cert, _, errors) => gate.Validate(cert, errors);
        return (new HttpClient(handler, disposeHandler: false) { Timeout = RequestTimeout }, handler);
    }

    /// <summary>
    /// فقط دست‌دهی TLS با iLO را انجام می‌دهد و گواهی را برمی‌گرداند. هیچ درخواست HTTP و هیچ اعتبارنامه‌ای ارسال نمی‌شود.
    /// </summary>
    public async Task<(IloCertificateInfo? Certificate, string? Error)> ProbeCertificateAsync(string address, int port, CancellationToken ct = default)
    {
        Uri baseUri;
        try { baseUri = BuildBaseUri(address, port); }
        catch (InvalidOperationException ex) { return (null, ex.Message); }

        IloCertificateInfo? seen = null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(ConnectTimeout + TimeSpan.FromSeconds(5));
        try
        {
            using var tcp = new TcpClient();
            await tcp.ConnectAsync(baseUri.IdnHost, baseUri.Port, timeout.Token);
            await using var ssl = new SslStream(tcp.GetStream(), leaveInnerStreamOpen: false);
            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = baseUri.IdnHost,
                RemoteCertificateValidationCallback = (_, cert, _, errors) =>
                {
                    if (cert is not null) seen = Describe(cert, errors);
                    return cert is not null; // فقط برای خواندن گواهی؛ بلافاصله بسته می‌شود.
                }
            }, timeout.Token);
            return seen is null ? (null, "iLO گواهی TLS ارائه نکرد.") : (seen, null);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return (null, TimeoutMessage);
        }
        catch (Exception ex) when (ex is SocketException or IOException or AuthenticationException)
        {
            return (seen, seen is null ? ClassifyTransport(ex) : null);
        }
    }

    private const string TimeoutMessage = "از iLO پاسخی نیامد. دسترسی شبکه از سرور برنامه به iLO (پورت و فایروال) را بررسی کنید.";

    /// <param name="includeHardwareDetails">خواندن محدود قطعات/رخدادها؛ برای پایش و دریافت Inventory استفاده شود، نه تست سریع اتصال.</param>
    /// <param name="pinnedThumbprint">اثر انگشت SHA-256 گواهی تأییدشده برای این iLO (اختیاری).</param>
    public async Task<RedfishSnapshot> GetSnapshotAsync(string address, int port, RedfishCredentials credentials, CancellationToken ct = default,
        bool includeHardwareDetails = false, string? pinnedThumbprint = null)
    {
        var gate = new CertificateGate(pinnedThumbprint);
        var (http, handler) = CreateHttp(gate);
        using var handlerScope = handler;
        using var httpScope = http;
        var session = new Session(credentials, http);
        try
        {
            var baseUri = BuildBaseUri(address, port);
            using var root = await GetJsonAsync(baseUri, new Uri(baseUri, "redfish/v1/"), session, ct);
            var systemsLink = GetOdataLink(root.RootElement, "Systems", baseUri)
                ?? throw new InvalidOperationException("منبع Systems در پاسخ Redfish پیدا نشد.");
            using var systems = await GetJsonAsync(baseUri, systemsLink, session, ct);
            var systemLink = GetFirstMemberLink(systems.RootElement, baseUri)
                ?? throw new InvalidOperationException("هیچ System قابل مدیریتی در Redfish پیدا نشد.");
            using var system = await GetJsonAsync(baseUri, systemLink, session, ct);

            string? firmware = null;
            var managersLink = GetOdataLink(root.RootElement, "Managers", baseUri);
            if (managersLink is not null)
            {
                try
                {
                    using var managers = await GetJsonAsync(baseUri, managersLink, session, ct);
                    var managerLink = GetFirstMemberLink(managers.RootElement, baseUri);
                    if (managerLink is not null)
                    {
                        using var manager = await GetJsonAsync(baseUri, managerLink, session, ct);
                        firmware = ReadString(manager.RootElement, "FirmwareVersion");
                    }
                }
                // نبودن Managers نباید Health/System را از دسترس خارج کند.
                catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or JsonException)
                {
                    firmware = null;
                }
            }

            var systemNode = system.RootElement;
            IReadOnlyList<RedfishHardwareComponent> components = Array.Empty<RedfishHardwareComponent>();
            IReadOnlyList<RedfishHardwareEvent> events = Array.Empty<RedfishHardwareEvent>();
            if (includeHardwareDetails)
            {
                // جزئیات اختیاری‌اند: نبودن یک resource در نسل‌های مختلف iLO نباید Health اصلی را ناموفق کند.
                try { components = await ReadHardwareComponentsAsync(baseUri, root.RootElement, systemNode, session, ct); }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
                catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or JsonException) { }
                try { events = await ReadRecentEventsAsync(baseUri, root.RootElement, session, ct); }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
                catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or JsonException) { }
            }

            return new RedfishSnapshot
            {
                Success = true,
                Health = ReadNestedString(systemNode, "Status", "HealthRollup")
                         ?? ReadNestedString(systemNode, "Status", "Health")
                         ?? "Unknown",
                PowerState = ReadString(systemNode, "PowerState"),
                Manufacturer = ReadString(systemNode, "Manufacturer"),
                Model = ReadString(systemNode, "Model"),
                SerialNumber = ReadString(systemNode, "SerialNumber"),
                AssetTag = ReadString(systemNode, "AssetTag"),
                BiosVersion = ReadString(systemNode, "BiosVersion"),
                FirmwareVersion = firmware,
                // FirmwareVersion منیجر iLO، نسخهٔ واقعی iLO است؛ RedfishVersion فقط نسخهٔ پروتکل است.
                IloVersion = firmware,
                Components = components,
                Events = events
            };
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new RedfishSnapshot { Error = TimeoutMessage, ErrorCode = "Timeout" };
        }
        catch (OperationCanceledException)
        {
            return new RedfishSnapshot { Error = "درخواست لغو شد.", ErrorCode = "Timeout" };
        }
        catch (HttpRequestException ex)
        {
            return ToSafeError(ex, gate);
        }
        catch (JsonException)
        {
            return new RedfishSnapshot { Error = "پاسخ iLO معتبر نیست یا Redfish در این آدرس فعال نیست.", ErrorCode = "Protocol" };
        }
        catch (Exception ex) when (ex is UriFormatException or InvalidOperationException)
        {
            return new RedfishSnapshot { Error = ex.Message };
        }
    }

    private async Task<IReadOnlyList<RedfishHardwareComponent>> ReadHardwareComponentsAsync(Uri origin, JsonElement root,
        JsonElement system, Session credentials, CancellationToken ct)
    {
        var components = new List<RedfishHardwareComponent>();

        // هر resource اختیاری جداگانه محافظت می‌شود؛ نبودن Thermal نباید Memory/Storage را حذف کند.
        try
        {
            // Chassis منابع Power و Thermal را در Redfish استاندارد نگه‌داری می‌کند.
            var chassisCollection = GetOdataLink(root, "Chassis", origin);
            if (chassisCollection is not null)
            {
                using var chassisMembers = await GetJsonAsync(origin, chassisCollection, credentials, ct);
                var chassisLink = GetFirstMemberLink(chassisMembers.RootElement, origin);
                if (chassisLink is not null)
                {
                    using var chassis = await GetJsonAsync(origin, chassisLink, credentials, ct);
                    AddComponent(components, "Chassis", chassis.RootElement);
                    await ReadPowerAndThermalAsync(origin, chassis.RootElement, credentials, components, ct);
                }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or JsonException) { }

        // سقف منابع مانع فشار غیرقابل‌پیش‌بینی روی کنترلر مدیریت می‌شود.
        try { await ReadSystemResourceComponentsAsync(origin, system, "Memory", "Memory", 16, credentials, components, ct); }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or JsonException) { }
        try { await ReadSystemResourceComponentsAsync(origin, system, "Processors", "Processor", 8, credentials, components, ct); }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or JsonException) { }
        try { await ReadStorageComponentsAsync(origin, system, credentials, components, ct); }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or JsonException) { }
        return components.Take(128).ToList();
    }

    private async Task ReadPowerAndThermalAsync(Uri origin, JsonElement chassis, Session credentials,
        List<RedfishHardwareComponent> components, CancellationToken ct)
    {
        var powerLink = GetOdataLink(chassis, "Power", origin);
        if (powerLink is not null)
        {
            using var power = await GetJsonAsync(origin, powerLink, credentials, ct);
            if (power.RootElement.TryGetProperty("PowerSupplies", out var supplies) && supplies.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in supplies.EnumerateArray().Take(16))
                    AddComponent(components, "PowerSupply", item, ReadNumber(item, "LastPowerOutputWatts", "W")
                        ?? ReadNumber(item, "PowerCapacityWatts", "W"));
            }
        }

        var thermalLink = GetOdataLink(chassis, "Thermal", origin);
        if (thermalLink is null) return;
        using var thermal = await GetJsonAsync(origin, thermalLink, credentials, ct);
        if (thermal.RootElement.TryGetProperty("Fans", out var fans) && fans.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in fans.EnumerateArray().Take(32))
                AddComponent(components, "Fan", item, ReadNumber(item, "Reading", ReadString(item, "ReadingUnits") ?? ""));
        }
        if (thermal.RootElement.TryGetProperty("Temperatures", out var temperatures) && temperatures.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in temperatures.EnumerateArray().Take(32))
                AddComponent(components, "Temperature", item, ReadNumber(item, "ReadingCelsius", "°C")
                    ?? ReadNumber(item, "Reading", ReadString(item, "ReadingUnits") ?? ""));
        }
    }

    private async Task ReadSystemResourceComponentsAsync(Uri origin, JsonElement system, string property, string type,
        int max, Session credentials, List<RedfishHardwareComponent> components, CancellationToken ct)
    {
        var collectionLink = GetOdataLink(system, property, origin);
        if (collectionLink is null) return;
        using var collection = await GetJsonAsync(origin, collectionLink, credentials, ct);
        foreach (var link in GetMemberLinks(collection.RootElement, origin, max))
        {
            using var resource = await GetJsonAsync(origin, link, credentials, ct);
            var reading = type == "Memory" ? ReadNumber(resource.RootElement, "CapacityMiB", "MiB")
                : ReadNumber(resource.RootElement, "TotalCores", "cores");
            AddComponent(components, type, resource.RootElement, reading);
        }
    }

    private async Task ReadStorageComponentsAsync(Uri origin, JsonElement system, Session credentials,
        List<RedfishHardwareComponent> components, CancellationToken ct)
    {
        var storageCollection = GetOdataLink(system, "Storage", origin);
        if (storageCollection is null) return;
        using var storageMembers = await GetJsonAsync(origin, storageCollection, credentials, ct);
        var remainingDrives = 16;
        foreach (var storageLink in GetMemberLinks(storageMembers.RootElement, origin, 4))
        {
            if (remainingDrives <= 0) break;
            using var storage = await GetJsonAsync(origin, storageLink, credentials, ct);
            AddComponent(components, "Storage", storage.RootElement);
            var driveLinks = GetPropertyLinks(storage.RootElement, "Drives", origin, remainingDrives);
            remainingDrives -= driveLinks.Count;
            foreach (var driveLink in driveLinks)
            {
                using var drive = await GetJsonAsync(origin, driveLink, credentials, ct);
                AddComponent(components, "Disk", drive.RootElement, ReadBytes(drive.RootElement, "CapacityBytes"));
            }
        }
    }

    private async Task<IReadOnlyList<RedfishHardwareEvent>> ReadRecentEventsAsync(Uri origin, JsonElement root,
        Session credentials, CancellationToken ct)
    {
        var managerCollection = GetOdataLink(root, "Managers", origin);
        if (managerCollection is null) return Array.Empty<RedfishHardwareEvent>();
        using var managers = await GetJsonAsync(origin, managerCollection, credentials, ct);
        var managerLink = GetFirstMemberLink(managers.RootElement, origin);
        if (managerLink is null) return Array.Empty<RedfishHardwareEvent>();
        using var manager = await GetJsonAsync(origin, managerLink, credentials, ct);
        var managerKey = ReadString(manager.RootElement, "Id") ?? "manager";
        var logsLink = GetOdataLink(manager.RootElement, "LogServices", origin);
        if (logsLink is null) return Array.Empty<RedfishHardwareEvent>();
        using var logs = await GetJsonAsync(origin, logsLink, credentials, ct);
        var logLink = GetFirstMemberLink(logs.RootElement, origin);
        if (logLink is null) return Array.Empty<RedfishHardwareEvent>();
        using var log = await GetJsonAsync(origin, logLink, credentials, ct);
        var entriesLink = GetOdataLink(log.RootElement, "Entries", origin);
        if (entriesLink is null) return Array.Empty<RedfishHardwareEvent>();
        var entriesTop = new UriBuilder(entriesLink) { Query = "$top=15" }.Uri;
        using var entries = await GetJsonAsync(origin, entriesTop, credentials, ct);

        var result = new List<RedfishHardwareEvent>();
        if (!entries.RootElement.TryGetProperty("Members", out var members) || members.ValueKind != JsonValueKind.Array)
            return result;
        foreach (var member in members.EnumerateArray().Take(15))
        {
            if (!string.IsNullOrWhiteSpace(ReadString(member, "Message")))
                AddEvent(result, managerKey, member);
            else if (ReadLink(member, origin) is { } entryLink)
            {
                using var entry = await GetJsonAsync(origin, entryLink, credentials, ct);
                AddEvent(result, managerKey, entry.RootElement);
            }
        }
        return result;
    }

    private static void AddComponent(List<RedfishHardwareComponent> items, string type, JsonElement source, string? reading = null)
    {
        var name = ReadString(source, "Name") ?? ReadString(source, "MemberId") ?? ReadString(source, "Id") ?? type;
        var key = ReadString(source, "Id") ?? ReadString(source, "MemberId") ?? name;
        var health = ReadNestedString(source, "Status", "HealthRollup")
            ?? ReadNestedString(source, "Status", "Health") ?? "Unknown";
        items.Add(new RedfishHardwareComponent
        {
            Type = type, Key = Limit(key, 200) ?? type, Name = Limit(name, 200) ?? type,
            Health = Limit(health, 20) ?? "Unknown", State = Limit(ReadNestedString(source, "Status", "State"), 40),
            Reading = Limit(reading, 100), Model = Limit(ReadString(source, "Model") ?? ReadString(source, "PartNumber"), 150),
            SerialNumber = Limit(ReadString(source, "SerialNumber"), 100),
            Message = Limit(ReadString(source, "Status") ?? ReadString(source, "StatusMessage"), 500)
        });
    }

    private static void AddEvent(List<RedfishHardwareEvent> items, string managerKey, JsonElement source)
    {
        var message = Limit(ReadString(source, "Message"), 2000);
        if (string.IsNullOrWhiteSpace(message)) return;
        var id = ReadString(source, "Id") ?? $"{ReadString(source, "Created")}|{message}";
        DateTime? occurred = DateTime.TryParse(ReadString(source, "Created"), out var parsed) ? parsed : null;
        items.Add(new RedfishHardwareEvent
        {
            Key = Limit($"{managerKey}:{id}", 200) ?? managerKey,
            Severity = Limit(ReadString(source, "Severity") ?? ReadString(source, "EventType"), 30) ?? "Unknown",
            Message = message, Origin = Limit(ReadNestedString(source, "OriginOfCondition", "@odata.id"), 500), OccurredAt = occurred
        });
    }

    private static List<Uri> GetMemberLinks(JsonElement collection, Uri origin, int max)
    {
        if (!collection.TryGetProperty("Members", out var members) || members.ValueKind != JsonValueKind.Array) return new();
        return members.EnumerateArray().Select(item => ReadLink(item, origin)).Where(link => link is not null)
            .Select(link => link!).Take(max).ToList();
    }

    private static List<Uri> GetPropertyLinks(JsonElement source, string property, Uri origin, int max)
    {
        if (!source.TryGetProperty(property, out var values)) return new();
        if (values.ValueKind != JsonValueKind.Array) return ReadLink(values, origin) is { } link ? new List<Uri> { link } : new();
        return values.EnumerateArray().Select(item => ReadLink(item, origin)).Where(link => link is not null)
            .Select(link => link!).Take(max).ToList();
    }

    private static string? ReadNumber(JsonElement source, string property, string unit)
    {
        if (!source.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Number) return null;
        return $"{value.GetRawText()} {unit}".Trim();
    }

    private static string? ReadBytes(JsonElement source, string property)
    {
        if (!source.TryGetProperty(property, out var value) || !value.TryGetInt64(out var bytes) || bytes <= 0) return null;
        return $"{Math.Round(bytes / 1073741824d, 1)} GiB";
    }

    private static string? Limit(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Trim();
        return value.Length <= max ? value : value[..max];
    }

    private async Task<JsonDocument> GetJsonAsync(Uri origin, Uri endpoint, Session credentials, CancellationToken ct)
    {
        EnsureSameOrigin(origin, endpoint);
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("OData-Version", "4.0");
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{credentials.Credentials.Username}:{credentials.Credentials.Password}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);

        using var response = await credentials.Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            var status = (int)response.StatusCode;
            var friendly = response.StatusCode switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "نام کاربری یا رمز iLO معتبر نیست، یا این حساب دسترسی Redfish ندارد.",
                HttpStatusCode.NotFound => "Redfish روی iLO پیدا نشد.",
                _ => $"iLO پاسخ HTTP {status} برگرداند."
            };
            throw new HttpRequestException(friendly, null, response.StatusCode);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
    }

    private static Uri BuildBaseUri(string address, int port)
    {
        if (string.IsNullOrWhiteSpace(address)) throw new InvalidOperationException("آدرس iLO وارد نشده است.");
        if (port is < 1 or > 65535) throw new InvalidOperationException("پورت iLO نامعتبر است.");

        var text = address.Trim();
        if (!text.Contains("://", StringComparison.Ordinal)) text = "https://" + text;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var parsed) || parsed.Host.Length == 0)
            throw new InvalidOperationException("آدرس iLO معتبر نیست.");
        if (!string.IsNullOrEmpty(parsed.UserInfo))
            throw new InvalidOperationException("آدرس iLO نباید شامل نام کاربری یا رمز باشد.");
        if (!string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("برای امنیت، اتصال iLO فقط با HTTPS مجاز است.");

        var builder = new UriBuilder(parsed)
        {
            Scheme = Uri.UriSchemeHttps,
            Port = port,
            Path = "/",
            Query = string.Empty,
            Fragment = string.Empty
        };
        return builder.Uri;
    }

    private static void EnsureSameOrigin(Uri origin, Uri target)
    {
        if (!string.Equals(origin.Scheme, target.Scheme, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(origin.Host, target.Host, StringComparison.OrdinalIgnoreCase)
            || origin.Port != target.Port)
            throw new InvalidOperationException("لینک Redfish به مقصدی خارج از iLO ثبت‌شده اشاره می‌کند و اجرا نشد.");
    }

    private static Uri? GetOdataLink(JsonElement source, string name, Uri origin)
    {
        if (!source.TryGetProperty(name, out var node)) return null;
        return ReadLink(node, origin);
    }

    private static Uri? GetFirstMemberLink(JsonElement collection, Uri origin)
    {
        if (!collection.TryGetProperty("Members", out var members) || members.ValueKind != JsonValueKind.Array) return null;
        foreach (var item in members.EnumerateArray())
        {
            var link = ReadLink(item, origin);
            if (link is not null) return link;
        }
        return null;
    }

    private static Uri? ReadLink(JsonElement node, Uri origin)
    {
        var raw = node.ValueKind == JsonValueKind.String ? node.GetString()
            : ReadString(node, "@odata.id");
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var uri = new Uri(origin, raw);
        EnsureSameOrigin(origin, uri);
        return uri;
    }

    private static string? ReadString(JsonElement node, string property) =>
        node.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? ReadNestedString(JsonElement node, string objectProperty, string valueProperty) =>
        node.TryGetProperty(objectProperty, out var child) && child.ValueKind == JsonValueKind.Object
            ? ReadString(child, valueProperty)
            : null;

    private static RedfishSnapshot ToSafeError(HttpRequestException ex, CertificateGate gate)
    {
        if (gate.Rejected == "CertUntrusted")
            return new RedfishSnapshot
            {
                ErrorCode = "CertUntrusted", Certificate = gate.Seen,
                Error = "گواهی TLS این iLO هنوز تأیید نشده است. گواهی را بررسی و تأیید کنید."
            };
        if (gate.Rejected == "CertChanged")
            return new RedfishSnapshot
            {
                ErrorCode = "CertChanged", Certificate = gate.Seen,
                Error = "گواهی TLS این iLO با گواهی تأییدشده فرق دارد. اگر گواهی iLO را عوض نکرده‌اید، اتصال را بررسی کنید."
            };
        if (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            return new RedfishSnapshot { ErrorCode = "Auth", Error = "نام کاربری یا رمز iLO معتبر نیست، یا این حساب دسترسی Redfish ندارد." };
        if (ex.StatusCode == HttpStatusCode.NotFound)
            return new RedfishSnapshot { ErrorCode = "Protocol", Error = "Redfish روی iLO پیدا نشد. نسخهٔ iLO (حداقل iLO 4 v2.30) را بررسی کنید." };
        if (ex.StatusCode is not null)
            return new RedfishSnapshot { ErrorCode = "Protocol", Error = ex.Message };
        return new RedfishSnapshot { ErrorCode = "Network", Error = ClassifyTransport(ex) };
    }

    /// <summary>دلیل واقعی خطای شبکه/TLS را از زنجیرهٔ InnerException بیرون می‌کشد.</summary>
    private static string ClassifyTransport(Exception ex)
    {
        for (Exception? e = ex; e is not null; e = e.InnerException)
        {
            if (e is SocketException se)
                return se.SocketErrorCode switch
                {
                    SocketError.ConnectionRefused => "iLO اتصال را رد کرد. پورت iLO را بررسی کنید.",
                    SocketError.HostNotFound or SocketError.NoData or SocketError.TryAgain => "نام iLO پیدا نشد. آدرس یا DNS را بررسی کنید.",
                    SocketError.TimedOut => TimeoutMessage,
                    SocketError.NetworkUnreachable or SocketError.HostUnreachable => "مسیر شبکه از سرور برنامه تا iLO وجود ندارد.",
                    SocketError.ConnectionReset or SocketError.ConnectionAborted => "iLO اتصال را قطع کرد.",
                    _ => $"اتصال شبکه به iLO برقرار نشد ({se.SocketErrorCode})."
                };
            if (e is TimeoutException) return TimeoutMessage;
            if (e is AuthenticationException) return "دست‌دهی TLS با iLO ناموفق بود. نسخهٔ TLS یا تنظیمات امنیتی iLO را بررسی کنید.";
        }
        return "اتصال به iLO برقرار نشد.";
    }
}
