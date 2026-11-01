using System.Security.Claims;
using System.Text.Json;
using Inventory.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Inventory.Api.Controllers;

/// <summary>
/// سمت «شعبه» — نرم‌افزارِ نصب‌شده در شرکتِ مشتری که درخواست‌های کاربرانش را به سرورِ مرکزیِ
/// واحدِ آی‌تی می‌فرستد.
/// کاربران این نصب در سرورِ مرکزی کاربر نیستند؛ هویتشان با یک «کلید پایدارِ کاربر» منتقل می‌شود.
/// آینهٔ درخواست‌های ارسالی در دیتابیسِ همین نصب نگه‌داری می‌شود (نه در مرورگر) تا با تغییر
/// دستگاه از بین نرود و بین همهٔ کاربران مشترک باشد.
/// </summary>
[ApiController]
[Route("api/itremote")]
[Authorize]
public class ItRemoteController : ControllerBase
{
    /// <summary>نام بخش تنظیمات: آدرس سرور مرکزیِ پیشنهادی به نصب‌های شرکت‌ها</summary>
    public const string CentralUrlKey = "It:CentralServerUrl";

    private readonly AppDbContext _db;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<ItRemoteController> _log;
    private readonly IConfiguration _cfg;

    public ItRemoteController(AppDbContext db, IHttpClientFactory httpFactory,
        ILogger<ItRemoteController> log, IConfiguration cfg)
    {
        _db = db; _httpFactory = httpFactory; _log = log; _cfg = cfg;
    }

    /// <summary>آدرس سرور مرکزی که در appsettings تعریف شده (پیش‌فرضِ پیشنهادی نصب‌های شرکت).</summary>
    private string? DefaultServerUrl
    {
        get
        {
            var v = _cfg[CentralUrlKey];
            return string.IsNullOrWhiteSpace(v) ? null : v.Trim().TrimEnd('/');
        }
    }

    private int MyUserId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var v) ? v : 0;
    private string MyName => User.FindFirstValue(ClaimTypes.Name) ?? "";

    // ================== تنظیم اتصال به سرور مرکزی ==================

    private async Task<ItRemoteConnection> ConnAsync(bool create = false)
    {
        var c = await _db.ItRemoteConnections.FirstOrDefaultAsync();
        if (c == null && create)
        {
            c = new ItRemoteConnection { Id = 1 };
            _db.ItRemoteConnections.Add(c);
            await _db.SaveChangesAsync();
        }
        return c ?? new ItRemoteConnection { Id = 1 };
    }

    /// <summary>
    /// وضعیت اتصال — کلید پوشیده‌شده برگردانده می‌شود (نه خودِ کلید).
    /// اگر آدرسی ذخیره نشده باشد، آدرس سرور مرکزیِ تعریف‌شده در تنظیماتِ همین سرور
    /// به‌عنوان مقدار پیشنهادی برگردانده می‌شود تا کاربر شرکت آن را نبیند و وارد نکند.
    /// </summary>
    [HttpGet("connection")]
    public async Task<IActionResult> GetConnection()
    {
        var c = await ConnAsync();
        return Ok(new
        {
            ServerUrl = c.ServerUrl ?? DefaultServerUrl,
            c.CompanyCode,
            c.CompanyName,
            HasKey = !string.IsNullOrWhiteSpace(c.ApiKey),
            KeyHint = string.IsNullOrWhiteSpace(c.ApiKey) ? null : Mask(c.ApiKey!),
            c.Enabled,
            c.UpdatedAt,
            IsSaved = !string.IsNullOrWhiteSpace(c.ServerUrl),
            DefaultServerUrl,
        });
    }

    [HttpPut("connection")]
    public async Task<IActionResult> SaveConnection([FromBody] ConnectionDto dto)
    {
        var c = await ConnAsync(create: true);
        if (!string.IsNullOrWhiteSpace(dto.ServerUrl))
        {
            var url = dto.ServerUrl.Trim().TrimEnd('/');
            if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || (u.Scheme != "http" && u.Scheme != "https"))
                return BadRequest(new { message = "آدرس سرور مرکزی معتبر نیست (باید با http:// یا https:// شروع شود)." });
            c.ServerUrl = url;
        }
        if (dto.ClearUrl) c.ServerUrl = null;
        // اگر آدرس خالی است ولی کاربر می‌خواهد وصل بماند، آدرس مرکزیِ تعریف‌شده در تنظیمات را می‌گیرد
        if (string.IsNullOrWhiteSpace(c.ServerUrl) && !dto.ClearUrl
            && !string.IsNullOrWhiteSpace(dto.ApiKey) && !string.IsNullOrWhiteSpace(DefaultServerUrl))
            c.ServerUrl = DefaultServerUrl;

        if (dto.CompanyCode != null) c.CompanyCode = dto.CompanyCode.Trim().ToUpperInvariant();
        if (dto.CompanyName != null) c.CompanyName = dto.CompanyName.Trim();
        if (!string.IsNullOrWhiteSpace(dto.ApiKey)) c.ApiKey = dto.ApiKey.Trim();
        if (dto.ClearKey) c.ApiKey = null;
        c.Enabled = dto.Enabled;
        c.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync();
        return Ok(new { ok = true, hasKey = !string.IsNullOrWhiteSpace(c.ApiKey) });
    }

    public class ConnectionDto
    {
        public string? ServerUrl { get; set; }
        public string? CompanyCode { get; set; }
        public string? CompanyName { get; set; }
        public string? ApiKey { get; set; }
        public bool Enabled { get; set; } = true;
        public bool ClearUrl { get; set; }
        public bool ClearKey { get; set; }
    }

    private static string Mask(string key) => key.Length <= 10 ? key[..4] : key[..10] + "…" + key[^4..];

    /// <summary>تست اتصال به سرور مرکزی با کلیدِ ذخیره‌شده (یک درخواست نمایشی ساخته نمی‌شود).</summary>
    [HttpPost("connection/test")]
    public async Task<IActionResult> TestConnection()
    {
        var c = await ConnAsync();
        if (string.IsNullOrWhiteSpace(c.ServerUrl) || string.IsNullOrWhiteSpace(c.ApiKey))
            return BadRequest(new { message = "آدرس سرور یا کلید API تنظیم نشده است." });
        try
        {
            var probe = new HttpRequestMessage(HttpMethod.Get, $"{c.ServerUrl}/api/itrequests/ping");
            probe.Headers.Add("X-It-Api-Key", c.ApiKey);
            if (!string.IsNullOrWhiteSpace(c.CompanyCode)) probe.Headers.Add("X-It-Company-Code", c.CompanyCode);
            var resp = await _httpFactory.CreateClient().SendAsync(probe);
            var body = await resp.Content.ReadAsStringAsync();
            return Ok(new { ok = resp.IsSuccessStatusCode, status = (int)resp.StatusCode, body = body.Length > 500 ? body[..500] : body });
        }
        catch (Exception ex)
        {
            return Ok(new { ok = false, status = 0, error = ex.Message });
        }
    }

    // ================== کلید پایدار کاربر ==================

    /// <summary>کلید پایدارِ کاربر جاری — یک‌بار ساخته و در دیتابیسِ محلی نگه‌داری می‌شود.</summary>
    [HttpGet("my-key")]
    public async Task<IActionResult> MyKey()
    {
        var k = await _db.ItRemoteUserKeys.FirstOrDefaultAsync(x => x.UserId == MyUserId);
        if (k == null)
        {
            k = new ItRemoteUserKey { UserId = MyUserId, Key = Guid.NewGuid().ToString("N") };
            _db.ItRemoteUserKeys.Add(k);
            await _db.SaveChangesAsync();
        }
        return Ok(new { k.Key });
    }

    // ================== ارسال درخواست به سرور مرکزی ==================

    public class SendRequestDto
    {
        public string? SystemLabel { get; set; }
        public string? LocalSystemRef { get; set; }
        public string RequestType { get; set; } = "Hardware";
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public string? Phone { get; set; }
        public string? Email { get; set; }
        /// <summary>فایل‌ها به‌صورت base64 (نام + محتوا) — برای ارسال یک‌جا با فرم</summary>
        public List<AttachDto> Attachments { get; set; } = new();
    }

    public class AttachDto
    {
        public string FileName { get; set; } = "";
        public string ContentType { get; set; } = "application/octet-stream";
        public string Base64 { get; set; } = "";
    }

    /// <summary>
    /// ثبت درخواست در سرور مرکزی. شناسهٔ یکتای محلی (ExternalId) قبل از ارسال ساخته می‌شود تا
    /// تلاش مجدد (یا قطعی‌شدن اینترنت) درخواست تکراری در سرور مرکزی نسازد.
    /// </summary>
    [HttpPost("requests")]
    public async Task<IActionResult> Send([FromBody] SendRequestDto dto)
    {
        var conn = await ConnAsync();
        if (!conn.Enabled) return BadRequest(new { message = "ارسال به سرور مرکزی غیرفعال است." });
        if (string.IsNullOrWhiteSpace(conn.ServerUrl) || string.IsNullOrWhiteSpace(conn.ApiKey))
            return BadRequest(new { message = "ابتدا آدرس سرور مرکزی و کلید API را در تنظیمات وارد کنید." });
        if (string.IsNullOrWhiteSpace(dto.Title)) return BadRequest(new { message = "موضوع درخواست الزامی است." });
        if (dto.Title.Trim().Length > 200) return BadRequest(new { message = "موضوع درخواست حداکثر ۲۰۰ کاراکتر است." });
        if ((dto.Description ?? "").Length > 1800) return BadRequest(new { message = "شرح درخواست حداکثر ۱۸۰۰ کاراکتر است." });

        var keyRow = await _db.ItRemoteUserKeys.FirstOrDefaultAsync(x => x.UserId == MyUserId);
        if (keyRow == null)
        {
            keyRow = new ItRemoteUserKey { UserId = MyUserId, Key = Guid.NewGuid().ToString("N") };
            _db.ItRemoteUserKeys.Add(keyRow);
            await _db.SaveChangesAsync();
        }

        // رکورد محلی (فهرست کاربر حتی وقتی سرور مرکزی در دسترس نیست حفظ می‌شود)
        var local = new ItRemoteRequest
        {
            ExternalId = Guid.NewGuid().ToString("N"),
            RequesterKey = keyRow.Key,
            LocalUserId = MyUserId,
            RequesterName = MyName,
            SystemLabel = dto.SystemLabel,
            RequestType = dto.RequestType is "Software" or "Network" or "Telecom" ? dto.RequestType : "Hardware",
            Title = dto.Title.Trim(),
            Description = dto.Description ?? "",
        };
        _db.ItRemoteRequests.Add(local);
        await _db.SaveChangesAsync();

        var result = await PushAsync(conn, local, dto.Phone, dto.Email, dto.Attachments, dto.LocalSystemRef);
        return Ok(new
        {
            local.Id, local.ExternalId, local.RemoteNumber,
            sent = result.Ok, error = result.Error, duplicate = result.Duplicate
        });
    }

    /// <summary>تلاش مجدد برای درخواستی که قبلاً به‌خاطر قطعی ارسال نشده بود.</summary>
    [HttpPost("requests/{id:int}/retry")]
    public async Task<IActionResult> Retry(int id)
    {
        var conn = await ConnAsync();
        var local = await _db.ItRemoteRequests.FirstOrDefaultAsync(x => x.Id == id && x.LocalUserId == MyUserId);
        if (local == null) return NotFound();
        var r = await PushAsync(conn, local, null, null, new List<AttachDto>(), null);
        return Ok(new { ok = r.Ok, local.RemoteNumber, error = r.Error });
    }

    private async Task<(bool Ok, string? Error, bool Duplicate)> PushAsync(
        ItRemoteConnection conn, ItRemoteRequest local, string? phone, string? email,
        List<AttachDto> attachments, string? localSystemRef)
    {
        var payload = new
        {
            companyCode = conn.CompanyCode,
            requesterKey = local.RequesterKey,
            externalId = local.ExternalId,
            requesterName = local.RequesterName,
            phone,
            email,
            systemLabel = local.SystemLabel,
            localSystemRef,
            requestType = local.RequestType,
            title = local.Title,
            description = local.Description,
        };

        try
        {
            var client = _httpFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(30);
            var req = new HttpRequestMessage(HttpMethod.Post, $"{conn.ServerUrl}/api/itrequests/external")
            {
                Content = new StringContent(
                    System.Text.Json.JsonSerializer.Serialize(payload,
                        new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }),
                    System.Text.Encoding.UTF8, "application/json")
            };
            req.Headers.Add("X-It-Api-Key", conn.ApiKey);
            if (!string.IsNullOrWhiteSpace(conn.CompanyCode)) req.Headers.Add("X-It-Company-Code", conn.CompanyCode);

            var resp = await client.SendAsync(req);
            var body = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
            {
                var msg = ExtractMessage(body) ?? $"خطای سرور مرکزی ({(int)resp.StatusCode})";
                local.LastError = msg;
                await _db.SaveChangesAsync();
                return (false, msg, false);
            }

            using var doc = System.Text.Json.JsonDocument.Parse(body);
            local.RemoteNumber = doc.RootElement.TryGetProperty("number", out var n) ? n.GetString() : null;
            local.TrackToken = doc.RootElement.TryGetProperty("trackToken", out var t) ? t.GetString() : null;
            local.Status = "New";
            local.LastError = null;
            local.SyncedAt = DateTime.Now;
            await _db.SaveChangesAsync();

            // ---------- پیوست‌ها (مرحله‌ی دوم) ----------
            if (attachments is { Count: > 0 } && !string.IsNullOrEmpty(local.RemoteNumber))
            {
                foreach (var a in attachments)
                {
                    if (string.IsNullOrWhiteSpace(a.Base64) || string.IsNullOrWhiteSpace(a.FileName)) continue;
                    byte[] bytes;
                    try { bytes = Convert.FromBase64String(a.Base64); }
                    catch { continue; }
                    if (bytes.Length == 0 || bytes.Length > 10 * 1024 * 1024) continue;

                    using var ms = new MemoryStream(bytes);
                    var up = new HttpRequestMessage(HttpMethod.Post,
                        $"{conn.ServerUrl}/api/itrequests/external/{Uri.EscapeDataString(local.RemoteNumber)}/attachments")
                    {
                        Content = new MultipartFormDataContent
                        {
                            { new StreamContent(ms), "file", Path.GetFileName(a.FileName) }
                        }
                    };
                    up.Headers.Add("X-It-Api-Key", conn.ApiKey);
                    if (!string.IsNullOrWhiteSpace(conn.CompanyCode)) up.Headers.Add("X-It-Company-Code", conn.CompanyCode);
                    var upResp = await client.SendAsync(up);
                    if (!upResp.IsSuccessStatusCode)
                    {
                        local.LastError = $"پیوست «{a.FileName}» ارسال نشد: {await upResp.Content.ReadAsStringAsync()}";
                        await _db.SaveChangesAsync();
                    }
                }
            }

            return (true, null, doc.RootElement.TryGetProperty("duplicate", out var d) && d.GetBoolean());
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "ارسال درخواست به سرور مرکزی ناموفق بود.");
            local.LastError = "ارتباط با سرور مرکزی برقرار نشد: " + ex.Message;
            await _db.SaveChangesAsync();
            return (false, local.LastError, false);
        }
    }

    private static string? ExtractMessage(string json)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("message", out var m) ? m.GetString() : null;
        }
        catch { return null; }
    }

    // ================== «درخواست‌های من» (از دیتابیس محلی) ==================

    /// <summary>همهٔ درخواست‌های این کاربر در این نصب (نه فقط مرورگر/دستگاه فعلی).</summary>
    [HttpGet("requests")]
    public async Task<IActionResult> MyRemoteRequests([FromQuery] bool allUsers = false, [FromQuery] bool pendingSyncOnly = false)
    {
        var q = _db.ItRemoteRequests.AsNoTracking();
        if (!allUsers) q = q.Where(r => r.LocalUserId == MyUserId);
        if (pendingSyncOnly) q = q.Where(r => r.RemoteNumber == null || r.RemoteNumber == "");
        var list = await q.OrderByDescending(r => r.Id).Take(500).ToListAsync();
        return Ok(list.Select(r => new
        {
            r.Id, r.RemoteNumber, r.ExternalId, r.RequesterName, r.SystemLabel,
            r.RequestType, r.Title, r.Description, r.Status, r.FinalResponse,
            r.CreatedAt, ApprovedAt = r.ManagerApprovedAt, r.CompletedAt, r.SyncedAt, r.LastError,
            trackToken = r.TrackToken,
            notSent = string.IsNullOrEmpty(r.RemoteNumber),
        }));
    }

    /// <summary>همگام‌سازی وضعیت‌ها از سرور مرکزی (یک درخواست شبکه به‌ازای کل دسته).</summary>
    [HttpPost("sync")]
    public async Task<IActionResult> Sync()
    {
        var conn = await ConnAsync();
        if (string.IsNullOrWhiteSpace(conn.ServerUrl) || string.IsNullOrWhiteSpace(conn.ApiKey))
            return BadRequest(new { message = "اتصال به سرور مرکزی تنظیم نشده است." });

        var keyRow = await _db.ItRemoteUserKeys.FirstOrDefaultAsync(x => x.UserId == MyUserId);
        if (keyRow == null)
        {
            keyRow = new ItRemoteUserKey { UserId = MyUserId, Key = Guid.NewGuid().ToString("N") };
            _db.ItRemoteUserKeys.Add(keyRow);
            await _db.SaveChangesAsync();
        }

        try
        {
            var client = _httpFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(30);
            var req = new HttpRequestMessage(HttpMethod.Get,
                $"{conn.ServerUrl}/api/itrequests/external/mine?requesterKey={Uri.EscapeDataString(keyRow.Key)}");
            req.Headers.Add("X-It-Api-Key", conn.ApiKey);
            if (!string.IsNullOrWhiteSpace(conn.CompanyCode)) req.Headers.Add("X-It-Company-Code", conn.CompanyCode);
            var resp = await client.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
                return StatusCode((int)resp.StatusCode, new { message = ExtractMessage(await resp.Content.ReadAsStringAsync()) });

            using var doc = System.Text.Json.JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            if (!doc.RootElement.TryGetProperty("items", out var items)) items = doc.RootElement;

            var map = new Dictionary<string, JsonElement>();
            foreach (var it in items.EnumerateArray())
            {
                var num = it.TryGetProperty("number", out var v) ? v.GetString() : null;
                if (!string.IsNullOrEmpty(num)) map[num] = it;
            }

            var mine = await _db.ItRemoteRequests.Where(r => r.LocalUserId == MyUserId).ToListAsync();
            var now = DateTime.Now;
            var updated = 0;
            foreach (var m in mine)
            {
                if (string.IsNullOrEmpty(m.RemoteNumber) || !map.TryGetValue(m.RemoteNumber, out var it)) continue;
                m.Status = it.TryGetProperty("status", out var st) ? st.GetString() ?? m.Status : m.Status;
                m.FinalResponse = it.TryGetProperty("finalResponse", out var fr) ? fr.GetString() : m.FinalResponse;
                m.ManagerApprovedAt = it.TryGetProperty("approvedAt", out var ap) && ap.ValueKind == JsonValueKind.String
                    ? it.GetProperty("approvedAt").GetDateTime() : m.ManagerApprovedAt;
                m.CompletedAt = it.TryGetProperty("completedAt", out var cp) && cp.ValueKind == JsonValueKind.String
                    ? it.GetProperty("completedAt").GetDateTime() : m.CompletedAt;
                m.SyncedAt = now;
                updated++;
            }
            await _db.SaveChangesAsync();
            return Ok(new { ok = true, updated });
        }
        catch (Exception ex)
        {
            return Ok(new { ok = false, error = ex.Message });
        }
    }

    /// <summary>تأیید نهاییِ تکمیل توسط کاربر (از طریق سرور مرکزی).</summary>
    [HttpPost("requests/{id:int}/complete")]
    public async Task<IActionResult> Complete(int id)
    {
        var conn = await ConnAsync();
        var local = await _db.ItRemoteRequests.FirstOrDefaultAsync(x => x.Id == id && x.LocalUserId == MyUserId);
        if (local == null) return NotFound();
        if (string.IsNullOrEmpty(local.RemoteNumber)) return BadRequest(new { message = "این درخواست هنوز به سرور مرکزی ارسال نشده است." });
        if (local.Status != "ManagerApproved") return BadRequest(new { message = "این درخواست هنوز پاسخ نهایی نگرفته است." });

        try
        {
            var client = _httpFactory.CreateClient();
            var url = $"{conn.ServerUrl}/api/itrequests/track/complete?number={Uri.EscapeDataString(local.RemoteNumber)}"
                    + (string.IsNullOrEmpty(local.TrackToken) ? "" : $"&token={Uri.EscapeDataString(local.TrackToken)}");
            var resp = await client.PostAsync(url, null);
            if (!resp.IsSuccessStatusCode)
                return StatusCode((int)resp.StatusCode, new { message = ExtractMessage(await resp.Content.ReadAsStringAsync()) });

            local.Status = "Completed";
            local.CompletedAt = DateTime.Now;
            local.SyncedAt = DateTime.Now;
            await _db.SaveChangesAsync();
            return Ok(new { ok = true });
        }
        catch (Exception ex)
        {
            return Ok(new { ok = false, error = ex.Message });
        }
    }
}
