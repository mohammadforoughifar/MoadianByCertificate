using Inventory.Api.Services;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Inventory.Api.Data;
using Paging = Inventory.Api.Services.Paging;

namespace Inventory.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SystemInfoController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly Hubs.DashboardBroadcaster _dash;
    private readonly IConfiguration _cfg;
    public SystemInfoController(AppDbContext db, Hubs.DashboardBroadcaster dash, IConfiguration cfg)
    {
        _db = db;
        _dash = dash;
        _cfg = cfg;
    }

    // ================= کلید مشترک ایجنت =================

    /// <summary>
    /// بررسی کلید مشترک ایجنت (هدر X-Agent-Key).
    /// اگر در تنظیمات Agent:Key مقدار داشته باشد، درخواست‌های ایجنت فقط با همان کلید پذیرفته
    /// می‌شوند (وگرنه 401). اگر خالی باشد، مسیرها مثل قبل باز می‌مانند تا نسخه‌های قدیمی ایجنت
    /// بدون تغییر کار کنند.
    /// </summary>
    private bool AgentKeyOk()
    {
        // مقدار خالی در appsettings نباید متغیر محیطی AGENT_KEY را بی‌اثر کند
        var expected = (string.IsNullOrWhiteSpace(_cfg["Agent:Key"]) ? Environment.GetEnvironmentVariable("AGENT_KEY") ?? "" : _cfg["Agent:Key"]!).Trim();
        if (expected.Length == 0) return true;
        var got = (Request.Headers["X-Agent-Key"].FirstOrDefault() ?? "").Trim();
        return got.Length == expected.Length &&
               string.Equals(got, expected, StringComparison.Ordinal);
    }

    private static readonly object AgentKeyError = new { message = "کلید ایجنت نامعتبر است (هدر X-Agent-Key)." };

    // ================= مدل‌های کمکی =================

    public class DiffItem
    {
        public string Field { get; set; } = "";
        public string? Old { get; set; }
        public string? New { get; set; }
        public bool Changed { get; set; }
    }

    public class PendingPayload
    {
        public string? Motherboard { get; set; }
        public string? Cpu { get; set; }
        public string? Ram { get; set; }
        public string? HardDisk { get; set; }
        public string? Graphics { get; set; }
        public string? Monitor { get; set; }
        public string? OsName { get; set; }
        public int TotalRamGb { get; set; }
        public string? DetailsJson { get; set; }
    }

    /// <summary>
    /// داده‌های ثبت دستی شناسنامهٔ یک سیستم. این مسیر مخصوص مدیر سامانه است و
    /// مستقل از گزارش خودکار ایجنت عمل می‌کند.
    /// </summary>
    public class ManualSystemInfoRequest
    {
        public string AgentId { get; set; } = "";
        public int CompanyId { get; set; }
        public int? DepartmentId { get; set; }
        public int? UserId { get; set; }
        public string? ComputerModel { get; set; }
        public string? Motherboard { get; set; }
        public string? Cpu { get; set; }
        public int TotalRamGb { get; set; }
        public string? Ram { get; set; }
        public string? HardDisk { get; set; }
        public string? Graphics { get; set; }
        public string? Monitor { get; set; }
        public string? OsName { get; set; }
        public string? Ip { get; set; }
    }

    private static readonly JsonSerializerOptions JOpt = new() { PropertyNameCaseInsensitive = true };

    // ================= دریافت ایجنت (upsert + مقایسه) =================

    /// <summary>
    /// دریافت گزارش سخت‌افزار از ایجنت — بدون توکن (ایجنت روی کلاینت‌ها توکن ندارد).
    /// ⚠️ این اکشن باید [AllowAnonymous] بماند؛ وگرنه DefaultAuthorizeConvention آن را
    /// پشت JWT می‌برد و همهٔ ایجنت‌ها 401 می‌گیرند و هیچ سیستمی در فهرست ثبت نمی‌شود.
    /// </summary>
    [AllowAnonymous]
    [HttpPost]
    public async Task<IActionResult> Post([FromBody] SystemInfo info)
    {
        if (!AgentKeyOk()) return Unauthorized(AgentKeyError);
        info.ReceivedAt = DateTime.Now;
        var existing = await _db.SystemInfos.FirstOrDefaultAsync(x => x.AgentId == info.AgentId);
        if (existing != null)
        {
            var payload = JsonSerializer.Serialize(new PendingPayload
            {
                Motherboard = info.Motherboard, Cpu = info.Cpu, Ram = info.Ram,
                HardDisk = info.HardDisk, Graphics = info.Graphics, Monitor = info.Monitor,
                OsName = info.OsName, TotalRamGb = info.TotalRamGb, DetailsJson = info.DetailsJson
            }, JOpt);

            var diffs = await BuildDiffFromDbAsync(existing, payload);

            if (diffs.All(d => !d.Changed))
            {
                // بدون تغییر — فقط تازه‌سازی زمان دریافت
                existing.ReceivedAt = DateTime.Now;
                await _db.SaveChangesAsync();
        _ = _dash.BroadcastAsync();
                return Ok(new { id = existing.Id, agent = existing.AgentId, updated = true, unchanged = true });
            }

            // تغییر دارد — به‌صورت «در انتظار مقایسه و تایید» ذخیره می‌شود
            existing.PendingPayloadJson = payload;
            existing.PendingReceivedAt = DateTime.Now;
            existing.ReceivedAt = DateTime.Now;
            await _db.SaveChangesAsync();
        _ = _dash.BroadcastAsync();
            return Ok(new
            {
                id = existing.Id,
                agent = existing.AgentId,
                updated = true,
                unchanged = false,
                pendingChanges = diffs.Count(d => d.Changed),
                message = "تغییرات سخت‌افزار تشخیص داده شد — در برنامه در انتظار تایید است."
            });
        }

        _db.SystemInfos.Add(info);
        await _db.SaveChangesAsync();
        SyncComponents(info);
        await _db.SaveChangesAsync();
        _ = _dash.BroadcastAsync();
        return Ok(new { id = info.Id, agent = info.AgentId, updated = false, firstRegistration = true });
    }

    // ================= ثبت دستی سیستم =================

    /// <summary>
    /// ثبت سیستم‌هایی که ایجنت روی آن‌ها اجرا نمی‌شود یا مشخصاتشان باید دستی وارد شود.
    /// برخلاف POST اصلی، این مسیر کلید ایجنت نمی‌خواهد و با احراز هویت عادی برنامه محافظت می‌شود.
    /// </summary>
    [HttpPost("manual")]
    public async Task<IActionResult> CreateManual([FromBody] ManualSystemInfoRequest request)
    {
        if (request == null) return BadRequest(new { message = "اطلاعات ثبت دستی ارسال نشده است." });

        var agentId = (request.AgentId ?? "").Trim();
        if (string.IsNullOrWhiteSpace(agentId))
            return BadRequest(new { message = "نام یا کد سیستم الزامی است." });
        if (request.CompanyId <= 0)
            return BadRequest(new { message = "انتخاب شرکت الزامی است." });

        var company = await _db.SystemCompanies.FirstOrDefaultAsync(c => c.Id == request.CompanyId && c.IsActive);
        if (company == null)
            return BadRequest(new { message = "شرکت انتخاب‌شده معتبر یا فعال نیست." });

        if (await _db.SystemInfos.AnyAsync(s => s.AgentId == agentId))
            return Conflict(new { message = "سیستمی با این نام یا کد قبلاً ثبت شده است." });

        if (request.DepartmentId is > 0)
        {
            var departmentOk = await _db.SystemDepartments.AnyAsync(d => d.Id == request.DepartmentId && d.IsActive && d.CompanyId == company.Id);
            if (!departmentOk)
                return BadRequest(new { message = "واحد انتخاب‌شده به شرکت انتخابی تعلق ندارد." });
        }

        SystemUser? user = null;
        if (request.UserId is > 0)
        {
            user = await _db.SystemUsers.FirstOrDefaultAsync(u => u.Id == request.UserId && u.IsActive);
            if (user == null)
                return BadRequest(new { message = "کاربر انتخاب‌شده معتبر یا فعال نیست." });
            if (user.CompanyId != company.Id)
                return BadRequest(new { message = "کاربر انتخاب‌شده به شرکت انتخابی تعلق ندارد." });
        }

        static string? TrimOrNull(string? value)
        {
            var cleaned = value?.Trim();
            return string.IsNullOrWhiteSpace(cleaned) ? null : cleaned;
        }

        var cpu = TrimOrNull(request.Cpu);
        var ram = TrimOrNull(request.Ram);
        var disk = TrimOrNull(request.HardDisk);
        var graphics = TrimOrNull(request.Graphics);
        var monitor = TrimOrNull(request.Monitor);
        var ip = TrimOrNull(request.Ip);
        var motherboard = TrimOrNull(request.Motherboard);
        var model = TrimOrNull(request.ComputerModel);
        var osName = TrimOrNull(request.OsName);

        // JSON ساختاریافته، تا صفحهٔ جزئیات و جدول‌های قطعات نیز برای ثبت دستی پر شوند.
        var details = new
        {
            board = motherboard,
            computerModel = model,
            cpus = string.IsNullOrWhiteSpace(cpu) ? Array.Empty<object>() : new object[] { new { name = cpu, cores = 0, threads = 0, clockGhz = 0 } },
            ramSticks = request.TotalRamGb > 0
                ? new object[] { new { slot = "ثبت دستی", capacityGb = request.TotalRamGb, type = ram ?? "", speedMhz = 0, manufacturer = "", partNumber = "", serialNumber = "" } }
                : Array.Empty<object>(),
            disks = string.IsNullOrWhiteSpace(disk) ? Array.Empty<object>() : new object[] { new { model = disk, sizeGb = 0, @interface = "", serialNumber = "" } },
            gpus = string.IsNullOrWhiteSpace(graphics) ? Array.Empty<object>() : new object[] { new { name = graphics, resolution = "" } },
            monitors = string.IsNullOrWhiteSpace(monitor) ? Array.Empty<object>() : new object[] { new { name = monitor, resolution = "", serialNumber = "", isPrimary = true } },
            netAdapters = string.IsNullOrWhiteSpace(ip) ? Array.Empty<object>() : new object[] { new { name = "ثبت دستی", description = "", type = "", macAddress = "", ipv4 = ip, gateway = "" } },
            volumes = Array.Empty<object>()
        };

        var info = new SystemInfo
        {
            AgentId = agentId,
            CompanyId = company.Id,
            DepartmentId = request.DepartmentId,
            UserId = user?.Id,
            Motherboard = motherboard,
            Cpu = cpu,
            Ram = request.TotalRamGb > 0 ? $"{request.TotalRamGb} GB" : ram,
            HardDisk = disk,
            Graphics = graphics,
            Monitor = monitor,
            OsName = osName,
            TotalRamGb = Math.Max(0, request.TotalRamGb),
            DetailsJson = JsonSerializer.Serialize(details),
            ReceivedAt = DateTime.Now,
            // اطلاعاتی که توسط مدیر وارد می‌شود، نیازمند چرخهٔ تأیید ایجنت نیست.
            IsApproved = true
        };

        _db.SystemInfos.Add(info);
        await _db.SaveChangesAsync();
        SyncComponents(info);

        if (user != null)
        {
            _db.SystemInfoUserHistories.Add(new SystemInfoUserHistory
            {
                SystemInfoId = info.Id,
                UserId = user.Id,
                UserName = $"{user.FirstName} {user.LastName}".Trim(),
                StaffNumber = user.StaffNumber,
                CompanyName = company.Name,
                FromAt = DateTime.Now
            });
        }

        await _db.SaveChangesAsync();
        _ = _dash.BroadcastAsync();
        return Ok(new { id = info.Id, message = "سیستم با موفقیت ثبت شد." });
    }

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        var pagination = new Paging.Request(skip, take);
        // لیست از جدول‌های قطعات می‌خواند (همان منبع صفحه‌ی جزئیات) — فیلدهای تخت فقط fallback
        var systems = await _db.SystemInfos.AsNoTracking().OrderByDescending(s => s.ReceivedAt).ThenBy(s => s.Id).ToPageListAsync(pagination);
        var ids = systems.Select(s => s.Id).ToList();

        var cpus = await _db.SystemCpus.AsNoTracking().Where(c => ids.Contains(c.SystemInfoId)).ToListAsync();
        var rams = await _db.SystemRams.AsNoTracking().Where(r => ids.Contains(r.SystemInfoId)).ToListAsync();
        var boards = await _db.SystemBoards.AsNoTracking().Where(b => ids.Contains(b.SystemInfoId)).ToListAsync();
        var nets = await _db.SystemNetAdapters.AsNoTracking().Where(n => ids.Contains(n.SystemInfoId)).ToListAsync();

        // امتیاز سلامت هر سیستم (0-100)
        var healths = await Services.SystemHealth.ComputeManyAsync(_db, ids);

        var result = systems
            .OrderByDescending(s => s.ReceivedAt)
            .Select(s =>
            {
                var ramSum = rams.Where(r => r.SystemInfoId == s.Id).Sum(r => r.CapacityGb);
                return new
                {
                    s.Id, s.AgentId, s.IsApproved, s.ReceivedAt, s.UserId,
                    s.CompanyId, s.DepartmentId,
                    s.PendingPayloadJson,
                    Cpu = cpus.FirstOrDefault(c => c.SystemInfoId == s.Id)?.Name ?? s.Cpu,
                    TotalRamGb = ramSum > 0 ? ramSum : s.TotalRamGb,
                    Ram = ramSum > 0 ? $"{ramSum} GB — {rams.Count(r => r.SystemInfoId == s.Id)} ماژول" : s.Ram,
                    Motherboard = boards.FirstOrDefault(b => b.SystemInfoId == s.Id)?.Board ?? s.Motherboard,
                    Ip = nets.FirstOrDefault(n => n.SystemInfoId == s.Id && !string.IsNullOrEmpty(n.Ipv4))?.Ipv4 ?? "",
                    s.OsName,
                    Health = healths.TryGetValue(s.Id, out var hr) ? hr.Score : (int?)null
                };
            }).ToList();

        return Ok(pagination.Result(result));
    }

    // ================= PDF شناسنامه سیستم =================

    [HttpGet("{id}/pdf")]
    public async Task<IActionResult> Pdf(int id)
    {
        try
        {
            var bytes = await Services.SystemInfoPdf.GenerateAsync(_db, id);
            if (bytes == null) return NotFound(new { message = "سیستم یافت نشد." });
            return File(bytes, "application/pdf", $"System-Identity-{id}.pdf");
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = "خطا در ساخت PDF: " + ex.Message });
        }
    }

    // ================= امتیاز سلامت سیستم (0-100) =================

    /// <summary>گزارش کامل سلامت یک سیستم: نمره + درجه + شکست امتیازها به تفکیک ۶ دسته.</summary>
    [HttpGet("health/{id}")]
    public async Task<IActionResult> Health(int id)
    {
        var report = await Services.SystemHealth.ComputeAsync(_db, id);
        return report == null ? NotFound(new { message = "سیستم یافت نشد." }) : Ok(report);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(int id)
    {
        var item = await _db.SystemInfos.FirstOrDefaultAsync(x => x.Id == id);
        return item == null ? NotFound() : Ok(item);
    }

    // ================= مقایسه (فرم دو ستونه) =================

    [HttpGet("changes/{id}")]
    public async Task<IActionResult> Changes(int id)
    {
        var item = await _db.SystemInfos.FirstOrDefaultAsync(x => x.Id == id);
        if (item == null) return NotFound(new { message = "سیستم یافت نشد." });

        if (string.IsNullOrWhiteSpace(item.PendingPayloadJson))
            return Ok(new { hasPending = false, diffs = Array.Empty<DiffItem>() });

        var diffs = await BuildDiffFromDbAsync(item, item.PendingPayloadJson);
        return Ok(new
        {
            hasPending = true,
            pendingReceivedAt = item.PendingReceivedAt,
            changeCount = diffs.Count(d => d.Changed),
            diffs
        });
    }

    [HttpPost("apply/{id}")]
    public async Task<IActionResult> Apply(int id)
    {
        var item = await _db.SystemInfos.FirstOrDefaultAsync(x => x.Id == id);
        if (item == null) return NotFound(new { message = "سیستم یافت نشد." });
        if (string.IsNullOrWhiteSpace(item.PendingPayloadJson))
            return BadRequest(new { message = "تغییر در انتظاری وجود ندارد." });

        var payload = JsonSerializer.Deserialize<PendingPayload>(item.PendingPayloadJson, JOpt);
        if (payload == null) return BadRequest(new { message = "داده‌ی در انتظار نامعتبر است." });

        var diffs = await BuildDiffFromDbAsync(item, item.PendingPayloadJson);

        // ۱) ثبت در تاریخچه
        _db.SystemInfoChangeLogs.Add(new SystemInfoChangeLog
        {
            SystemInfoId = item.Id,
            AgentId = item.AgentId,
            ChangedAt = DateTime.Now,
            ChangeCount = diffs.Count(d => d.Changed),
            ChangesJson = JsonSerializer.Serialize(diffs, JOpt)
        });

        // ۲) اعمال داده‌ی جدید
        item.Motherboard = payload.Motherboard;
        item.Cpu = payload.Cpu;
        item.Ram = payload.Ram;
        item.HardDisk = payload.HardDisk;
        item.Graphics = payload.Graphics;
        item.Monitor = payload.Monitor;
        item.OsName = payload.OsName;
        item.TotalRamGb = payload.TotalRamGb;
        item.DetailsJson = payload.DetailsJson;
        item.ReceivedAt = DateTime.Now;
        item.PendingPayloadJson = null;
        item.PendingReceivedAt = null;

        SyncComponents(item);
        await _db.SaveChangesAsync();
        _ = _dash.BroadcastAsync();
        return Ok(new { applied = true, changes = diffs.Count(d => d.Changed) });
    }

    [HttpPost("discard/{id}")]
    public async Task<IActionResult> Discard(int id)
    {
        var item = await _db.SystemInfos.FirstOrDefaultAsync(x => x.Id == id);
        if (item == null) return NotFound(new { message = "سیستم یافت نشد." });
        item.PendingPayloadJson = null;
        item.PendingReceivedAt = null;
        await _db.SaveChangesAsync();
        _ = _dash.BroadcastAsync();
        return Ok(new { discarded = true });
    }

    // ================= تاریخچه =================

    public class HistoryEntry
    {
        public int Id { get; set; }
        public DateTime ChangedAt { get; set; }
        public int ChangeCount { get; set; }
        public List<DiffItem> Diffs { get; set; } = new();
    }

    [HttpGet("history/{id}")]
    public async Task<IActionResult> History(int id, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        var pagination = new Paging.Request(skip, take);
        var logs = await _db.SystemInfoChangeLogs
            .Where(l => l.SystemInfoId == id)
            .OrderByDescending(l => l.ChangedAt).ThenBy(x => x.Id)
            .ToPageListAsync(pagination);

        var result = logs.Select(l => new HistoryEntry
        {
            Id = l.Id,
            ChangedAt = l.ChangedAt,
            ChangeCount = l.ChangeCount,
            Diffs = TryParseDiffs(l.ChangesJson)
        }).ToList();

        return Ok(pagination.Result(result));
    }

    private static List<DiffItem> TryParseDiffs(string json)
    {
        try { return JsonSerializer.Deserialize<List<DiffItem>>(json, JOpt) ?? new(); }
        catch { return new(); }
    }

    // ================= اختصاص کاربر =================

    [HttpPost("assign-user")]
    public async Task<IActionResult> AssignUser([FromBody] SystemInfoUserLink link)
    {
        var systemInfoId = link?.SystemInfoId ?? 0;
        var userId = link?.UserId ?? 0;

        var item = await _db.SystemInfos.FirstOrDefaultAsync(x => x.Id == systemInfoId);
        if (item == null) return NotFound(new { message = "سیستم یافت نشد." });

        var user = await _db.SystemUsers.FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null) return NotFound(new { message = "کاربر یافت نشد." });

        // ---------- تاریخچه کاربران: رکورد باز قبلی را ببند و رکورد جدید ثبت کن ----------
        if (item.UserId != user.Id)
        {
            var openLog = await _db.SystemInfoUserHistories
                .FirstOrDefaultAsync(h => h.SystemInfoId == item.Id && h.ToAt == null);
            if (openLog != null)
            {
                openLog.ToAt = DateTime.Now;
            }
            var companyName = user.CompanyId.HasValue
                ? await _db.SystemCompanies.AsNoTracking().Where(c => c.Id == user.CompanyId).Select(c => c.Name).FirstOrDefaultAsync()
                : null;
            _db.SystemInfoUserHistories.Add(new SystemInfoUserHistory
            {
                SystemInfoId = item.Id,
                UserId = user.Id,
                UserName = $"{user.FirstName} {user.LastName}".Trim(),
                StaffNumber = user.StaffNumber,
                CompanyName = companyName,
                FromAt = DateTime.Now
            });
        }

        item.UserId = user.Id;
        item.CompanyId = user.CompanyId ?? item.CompanyId;
        item.DepartmentId = user.DepartmentId;
        await _db.SaveChangesAsync();
        _ = _dash.BroadcastAsync();
        return Ok(new { linked = true, userId = user.Id, systemInfoId = item.Id });
    }

    // ================= تاریخچه کاربران سیستم =================

    public class UserHistoryItem
    {
        public int Id { get; set; }
        public int? UserId { get; set; }
        public string UserName { get; set; } = "";
        public string? StaffNumber { get; set; }
        public string? CompanyName { get; set; }
        public DateTime FromAt { get; set; }
        public DateTime? ToAt { get; set; }
    }

    [HttpGet("user-history/{id}")]
    public async Task<IActionResult> UserHistory(int id, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        var pagination = new Paging.Request(skip, take);
        var list = await _db.SystemInfoUserHistories
            .AsNoTracking()
            .Where(h => h.SystemInfoId == id)
            .OrderByDescending(h => h.FromAt).ThenBy(x => x.Id)
            .Select(h => new UserHistoryItem
            {
                Id = h.Id,
                UserId = h.UserId,
                UserName = h.UserName,
                StaffNumber = h.StaffNumber,
                CompanyName = h.CompanyName,
                FromAt = h.FromAt,
                ToAt = h.ToAt
            })
            .ToPageListAsync(pagination);
        return Ok(pagination.Result(list));
    }

    // ================= چک‌لیست تحویل دیجیتال با امضا =================

    public class HandoverRequest
    {
        public string FromUserName { get; set; } = "";
        public int ToUserId { get; set; }
        public string ToUserName { get; set; } = "";
        public List<ChecklistItemDto> Checklist { get; set; } = new();
        public string? SignatureDataUrl { get; set; }
        public string? Note { get; set; }
    }

    public class ChecklistItemDto
    {
        public string Key { get; set; } = "";
        public string Fa { get; set; } = "";
        public bool Checked { get; set; }
    }

    public class HandoverItem
    {
        public int Id { get; set; }
        public string FromUserName { get; set; } = "";
        public int? ToUserId { get; set; }
        public string ToUserName { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public bool IsCompleted { get; set; }
        public DateTime? CompletedAt { get; set; }
        public List<ChecklistItemDto> Checklist { get; set; } = new();
        public string? SignatureDataUrl { get; set; }
        public string? Note { get; set; }
    }

    [HttpPost("handover/{id}")]
    public async Task<IActionResult> CreateHandover(int id, [FromBody] HandoverRequest req)
    {
        var item = await _db.SystemInfos.FirstOrDefaultAsync(x => x.Id == id);
        if (item == null) return NotFound(new { message = "سیستم یافت نشد." });
        if (req == null || req.ToUserId <= 0)
            return BadRequest(new { message = "گیرنده‌ی تحویل را انتخاب کنید." });
        if (string.IsNullOrWhiteSpace(req.SignatureDataUrl))
            return BadRequest(new { message = "امضا الزامی است — در بوم امضا خط بکشید." });
        if (req.Checklist.Any(c => !c.Checked))
            return BadRequest(new { message = "همه‌ی آیتم‌های چک‌لیست را تیک بزنید." });

        var toUser = await _db.SystemUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Id == req.ToUserId);
        var handover = new SystemHandover
        {
            SystemInfoId = item.Id,
            FromUserName = string.IsNullOrWhiteSpace(req.FromUserName) ? "بدون مشخص‌کردن" : req.FromUserName.Trim(),
            ToUserId = req.ToUserId,
            ToUserName = string.IsNullOrWhiteSpace(req.ToUserName) ? (toUser != null ? $"{toUser.FirstName} {toUser.LastName}".Trim() : $"#{req.ToUserId}") : req.ToUserName.Trim(),
            CreatedAt = DateTime.Now,
            IsCompleted = true,
            CompletedAt = DateTime.Now,
            ChecklistJson = JsonSerializer.Serialize(req.Checklist, JOpt),
            SignatureDataUrl = req.SignatureDataUrl,
            Note = req.Note
        };
        _db.SystemHandovers.Add(handover);

        // اگر گیرنده کاربر فعال سیستم است، تخصیص را هم به‌روز کن (با ثبت در تاریخچه)
        if (toUser != null)
        {
            if (item.UserId != toUser.Id)
            {
                var openLog = await _db.SystemInfoUserHistories
                    .FirstOrDefaultAsync(h => h.SystemInfoId == item.Id && h.ToAt == null);
                if (openLog != null) openLog.ToAt = DateTime.Now;
                var companyName = toUser.CompanyId.HasValue
                    ? await _db.SystemCompanies.AsNoTracking().Where(c => c.Id == toUser.CompanyId).Select(c => c.Name).FirstOrDefaultAsync()
                    : null;
                _db.SystemInfoUserHistories.Add(new SystemInfoUserHistory
                {
                    SystemInfoId = item.Id,
                    UserId = toUser.Id,
                    UserName = $"{toUser.FirstName} {toUser.LastName}".Trim(),
                    StaffNumber = toUser.StaffNumber,
                    CompanyName = companyName,
                    FromAt = DateTime.Now
                });
            }
            item.UserId = toUser.Id;
            item.CompanyId = toUser.CompanyId ?? item.CompanyId;
            item.DepartmentId = toUser.DepartmentId;
        }

        await _db.SaveChangesAsync();
        _ = _dash.BroadcastAsync();
        return Ok(new { id = handover.Id, completed = true });
    }

    [HttpGet("handovers/{id}")]
    public async Task<IActionResult> Handovers(int id, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        var pagination = new Paging.Request(skip, take);
        var list = await _db.SystemHandovers
            .AsNoTracking()
            .Where(h => h.SystemInfoId == id)
            .OrderByDescending(h => h.CreatedAt).ThenBy(x => x.Id)
            .ToPageListAsync(pagination);
        var result = list.Select(h => new HandoverItem
        {
            Id = h.Id,
            FromUserName = h.FromUserName,
            ToUserId = h.ToUserId,
            ToUserName = h.ToUserName,
            CreatedAt = h.CreatedAt,
            IsCompleted = h.IsCompleted,
            CompletedAt = h.CompletedAt,
            SignatureDataUrl = h.SignatureDataUrl,
            Note = h.Note,
            Checklist = TryParseChecklist(h.ChecklistJson)
        }).ToList();
        return Ok(pagination.Result(result));
    }

    private static List<ChecklistItemDto> TryParseChecklist(string json)
    {
        try { return JsonSerializer.Deserialize<List<ChecklistItemDto>>(json, JOpt) ?? new(); }
        catch { return new(); }
    }

    // ================= دستور از راه دور (Remote Actions) =================

    public class CommandCreateRequest
    {
        public string Action { get; set; } = "";      // Reboot | Shutdown | Lock
        public string? ByUserName { get; set; }
    }

    public class CommandItem
    {
        public int Id { get; set; }
        public string Action { get; set; } = "";
        public string Status { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public string? ByUserName { get; set; }
        public DateTime? CompletedAt { get; set; }
        public string? Result { get; set; }
    }

    [HttpPost("commands/{id}")]
    public async Task<IActionResult> CreateCommand(int id, [FromBody] CommandCreateRequest req)
    {
        var item = await _db.SystemInfos.FirstOrDefaultAsync(x => x.Id == id);
        if (item == null) return NotFound(new { message = "سیستم یافت نشد." });
        if (req == null || !new[] { "Reboot", "Shutdown", "Lock" }.Contains(req.Action))
            return BadRequest(new { message = "عملیات نامعتبر است." });

        var cmd = new SystemRemoteCommand
        {
            SystemInfoId = item.Id,
            Action = req.Action,
            Status = "Pending",
            CreatedAt = DateTime.Now,
            ByUserName = req.ByUserName
        };
        _db.SystemRemoteCommands.Add(cmd);
        await _db.SaveChangesAsync();
        _ = _dash.BroadcastAsync();
        return Ok(new { id = cmd.Id, status = cmd.Status });
    }

    [HttpGet("commands/{id}")]
    public async Task<IActionResult> Commands(int id, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        var pagination = new Paging.Request(skip, take);
        var list = await _db.SystemRemoteCommands
            .AsNoTracking()
            .Where(c => c.SystemInfoId == id)
            .OrderByDescending(c => c.CreatedAt).ThenBy(x => x.Id)
            .ToPageListAsync(pagination, defaultCap: 20);
        return Ok(pagination.Result(list.Select(c => new CommandItem
        {
            Id = c.Id, Action = c.Action, Status = c.Status,
            CreatedAt = c.CreatedAt, ByUserName = c.ByUserName,
            CompletedAt = c.CompletedAt, Result = c.Result
        }).ToList()));
    }

    /// <summary>ایجنت: دریافت دستورهای در انتظار این سیستم.</summary>
    /// <summary>ایجنت: دریافت دستورهای در انتظار — بدون توکن (مثل ثبت گزارش).</summary>
    [AllowAnonymous]
    [HttpGet("agent-commands")]
    public async Task<IActionResult> AgentCommands([FromQuery] string agentId, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (!AgentKeyOk()) return Unauthorized(AgentKeyError);
        var pagination = new Paging.Request(skip, take);
        var pending = await _db.SystemRemoteCommands
            .AsNoTracking()
            .Where(c => c.Status == "Pending")
            .Join(_db.SystemInfos.AsNoTracking(),
                c => c.SystemInfoId, s => s.Id,
                (c, s) => new { c, s.AgentId })
            .Where(x => x.AgentId == agentId)
            .OrderBy(x => x.c.CreatedAt).ThenBy(x => x.c.Id)
            .Select(x => x.c)
            .ToPageListAsync(pagination);
        return Ok(pagination.Result(pending.Select(c => new { c.Id, c.Action, c.CreatedAt }).ToList()));
    }

    /// <summary>ایجنت: گزارش نتیجه‌ی اجرای دستور — بدون توکن.</summary>
    [AllowAnonymous]
    [HttpPost("commands/{cmdId}/result")]
    public async Task<IActionResult> CommandResult(int cmdId, [FromBody] CommandResultRequest req)
    {
        if (!AgentKeyOk()) return Unauthorized(AgentKeyError);
        var cmd = await _db.SystemRemoteCommands.FirstOrDefaultAsync(c => c.Id == cmdId);
        if (cmd == null) return NotFound(new { message = "دستور یافت نشد." });
        var ok = req != null && req.Ok;
        cmd.Status = ok ? "Completed" : "Failed";
        cmd.CompletedAt = DateTime.Now;
        cmd.Result = req?.Message;
        await _db.SaveChangesAsync();
        _ = _dash.BroadcastAsync();
        return Ok(new { done = true });
    }

    public class CommandResultRequest
    {
        public bool Ok { get; set; }
        public string? Message { get; set; }
    }

    [HttpPost("approve/{id}")]
    public async Task<IActionResult> Approve(int id)
    {
        var item = await _db.SystemInfos.FirstOrDefaultAsync(x => x.Id == id);
        if (item == null) return NotFound();
        item.IsApproved = true;
        await _db.SaveChangesAsync();
        _ = _dash.BroadcastAsync();
        return Ok(new { id, approved = true });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var item = await _db.SystemInfos.FirstOrDefaultAsync(x => x.Id == id);
        if (item == null) return NotFound();
        RemoveComponents(item.Id);
        _db.SystemInfos.Remove(item);
        await _db.SaveChangesAsync();
        _ = _dash.BroadcastAsync();
        return Ok();
    }

    // ================= قطعات به تفکیک جدول‌های مجزا =================

    [HttpGet("{id}/components")]
    public async Task<IActionResult> Components(int id)
    {
        if (!await _db.SystemInfos.AnyAsync(x => x.Id == id))
            return NotFound(new { message = "سیستم یافت نشد." });

        return Ok(new
        {
            board = await _db.SystemBoards.AsNoTracking().Where(x => x.SystemInfoId == id).FirstOrDefaultAsync(),
            cpus = await _db.SystemCpus.AsNoTracking().Where(x => x.SystemInfoId == id).ToListAsync(),
            rams = await _db.SystemRams.AsNoTracking().Where(x => x.SystemInfoId == id).ToListAsync(),
            disks = await _db.SystemDisks.AsNoTracking().Where(x => x.SystemInfoId == id).ToListAsync(),
            gpus = await _db.SystemGpus.AsNoTracking().Where(x => x.SystemInfoId == id).ToListAsync(),
            monitors = await _db.SystemMonitors.AsNoTracking().Where(x => x.SystemInfoId == id).ToListAsync(),
            netAdapters = await _db.SystemNetAdapters.AsNoTracking().Where(x => x.SystemInfoId == id).ToListAsync(),
            volumes = await _db.SystemVolumes.AsNoTracking().Where(x => x.SystemInfoId == id).ToListAsync()
        });
    }

    /// <summary>پر کردن جدول‌های قطعات از DetailsJson (حذف قبلی‌ها + درج جدید).</summary>
    private void SyncComponents(SystemInfo item)
    {
        RemoveComponents(item.Id);
        if (string.IsNullOrWhiteSpace(item.DetailsJson)) return;
        try
        {
            using var doc = JsonDocument.Parse(item.DetailsJson);
            var root = doc.RootElement;

            var boardName = RootStr(root, "board");
            var boardSerial = RootStr(root, "boardSerial");
            var computerModel = RootStr(root, "computerModel");
            if (boardName.Length > 0 || boardSerial.Length > 0 || computerModel.Length > 0)
                _db.SystemBoards.Add(new SystemBoard { SystemInfoId = item.Id, Board = boardName, BoardSerial = boardSerial, ComputerModel = computerModel });

            foreach (var e in EnumArray(root, "cpus"))
                _db.SystemCpus.Add(new SystemCpu { SystemInfoId = item.Id, Name = Str(e, "name"), Cores = Int(e, "cores"), Threads = Int(e, "threads"), ClockGhz = Dbl(e, "clockGhz") });

            foreach (var e in EnumArray(root, "ramSticks"))
                _db.SystemRams.Add(new SystemRam { SystemInfoId = item.Id, Slot = Str(e, "slot"), CapacityGb = Int(e, "capacityGb"), Type = Str(e, "type"), SpeedMhz = Int(e, "speedMhz"), Manufacturer = Str(e, "manufacturer"), PartNumber = Str(e, "partNumber"), SerialNumber = Str(e, "serialNumber") });

            foreach (var e in EnumArray(root, "disks"))
            {
                var smart = Str(e, "smart");
                _db.SystemDisks.Add(new SystemDisk
                {
                    SystemInfoId = item.Id,
                    Model = Str(e, "model"),
                    SizeGb = Int(e, "sizeGb"),
                    Interface = Str(e, "interface"),
                    SerialNumber = Str(e, "serialNumber"),
                    SmartStatus = smart.Length > 0 ? smart : null,
                    SmartUpdatedAt = smart.Length > 0 ? DateTime.Now : null
                });
            }

            foreach (var e in EnumArray(root, "gpus"))
                _db.SystemGpus.Add(new SystemGpu { SystemInfoId = item.Id, Name = Str(e, "name"), Resolution = Str(e, "resolution") });

            foreach (var e in EnumArray(root, "monitors"))
                _db.SystemMonitors.Add(new SystemMonitor { SystemInfoId = item.Id, Name = Str(e, "name"), Resolution = Str(e, "resolution"), SerialNumber = Str(e, "serialNumber"), IsPrimary = Bool(e, "isPrimary") });

            foreach (var e in EnumArray(root, "netAdapters"))
                _db.SystemNetAdapters.Add(new SystemNetAdapter { SystemInfoId = item.Id, Name = Str(e, "name"), Description = Str(e, "description"), Type = Str(e, "type"), MacAddress = Str(e, "macAddress"), Ipv4 = Str(e, "ipv4"), Gateway = Str(e, "gateway") });

            foreach (var e in EnumArray(root, "volumes"))
                _db.SystemVolumes.Add(new SystemVolume { SystemInfoId = item.Id, Letter = Str(e, "letter"), Label = Str(e, "label"), TotalGb = Int(e, "totalGb"), UsedGb = Int(e, "usedGb") });
        }
        catch { }
    }

    private void RemoveComponents(int systemInfoId)
    {
        _db.SystemBoards.RemoveRange(_db.SystemBoards.Where(x => x.SystemInfoId == systemInfoId));
        _db.SystemCpus.RemoveRange(_db.SystemCpus.Where(x => x.SystemInfoId == systemInfoId));
        _db.SystemRams.RemoveRange(_db.SystemRams.Where(x => x.SystemInfoId == systemInfoId));
        _db.SystemDisks.RemoveRange(_db.SystemDisks.Where(x => x.SystemInfoId == systemInfoId));
        _db.SystemGpus.RemoveRange(_db.SystemGpus.Where(x => x.SystemInfoId == systemInfoId));
        _db.SystemMonitors.RemoveRange(_db.SystemMonitors.Where(x => x.SystemInfoId == systemInfoId));
        _db.SystemNetAdapters.RemoveRange(_db.SystemNetAdapters.Where(x => x.SystemInfoId == systemInfoId));
        _db.SystemVolumes.RemoveRange(_db.SystemVolumes.Where(x => x.SystemInfoId == systemInfoId));
    }

    private static string RootStr(JsonElement root, string prop)
        => root.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "") : "";

    private static IEnumerable<JsonElement> EnumArray(JsonElement root, string prop)
    {
        if (root.TryGetProperty(prop, out var arr) && arr.ValueKind == JsonValueKind.Array)
            foreach (var e in arr.EnumerateArray())
                yield return e;
    }

    private static string Str(JsonElement e, string prop)
        => e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "") : "";

    private static int Int(JsonElement e, string prop)
        => e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : 0;

    private static double Dbl(JsonElement e, string prop)
        => e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d) ? d : 0;

    private static bool Bool(JsonElement e, string prop)
        => e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.True;

    // ================= موتور مقایسه =================

    /// <summary>
    /// مقایسه‌ی واقعی با بانک اطلاعاتی: سمت «قدیم» مستقیماً از جدول‌های SQL قطعات خوانده می‌شود
    /// (SystemCpus/SystemRams/SystemDisks/...) و سمت «جدید» از داده‌ی ارسالی ایجنت.
    /// اگر جدول‌ها برای رکورد قدیمی خالی باشند، از DetailsJson به‌عنوان fallback استفاده می‌شود.
    /// </summary>
    private async Task<List<DiffItem>> BuildDiffFromDbAsync(SystemInfo current, string pendingPayloadJson)
    {
        var id = current.Id;
        var cpus = await _db.SystemCpus.AsNoTracking().Where(x => x.SystemInfoId == id).ToListAsync();
        var rams = await _db.SystemRams.AsNoTracking().Where(x => x.SystemInfoId == id).ToListAsync();
        var disks = await _db.SystemDisks.AsNoTracking().Where(x => x.SystemInfoId == id).ToListAsync();
        var gpus = await _db.SystemGpus.AsNoTracking().Where(x => x.SystemInfoId == id).ToListAsync();
        var monitors = await _db.SystemMonitors.AsNoTracking().Where(x => x.SystemInfoId == id).ToListAsync();
        var nets = await _db.SystemNetAdapters.AsNoTracking().Where(x => x.SystemInfoId == id).ToListAsync();
        var board = await _db.SystemBoards.AsNoTracking().Where(x => x.SystemInfoId == id).FirstOrDefaultAsync();

        var hasTables = cpus.Count + rams.Count + disks.Count + gpus.Count + monitors.Count + nets.Count + (board != null ? 1 : 0) > 0;
        if (!hasTables) return BuildDiff(current, pendingPayloadJson); // رکورد قدیمی — مقایسه از JSON

        PendingPayload pending;
        try { pending = JsonSerializer.Deserialize<PendingPayload>(pendingPayloadJson, JOpt) ?? new PendingPayload(); }
        catch { pending = new PendingPayload(); }
        JsonDocument? newDoc = TryParse(pending.DetailsJson);

        var list = new List<DiffItem>();
        void Add(string field, string? oldV, string? newV)
        {
            oldV = Clean(oldV); newV = Clean(newV);
            list.Add(new DiffItem { Field = field, Old = oldV, New = newV, Changed = !string.Equals(oldV, newV, StringComparison.OrdinalIgnoreCase) });
        }

        Add("مادربرد", board?.Board, Arr(newDoc, "board", One));
        Add("سریال مادربرد", board?.BoardSerial, Arr(newDoc, "boardSerial", One));
        Add("مدل سیستم", board?.ComputerModel, Arr(newDoc, "computerModel", One));
        Add("پردازنده",
            cpus.Count > 0 ? string.Join(" + ", cpus.Select(c => c.Name)) : null,
            Joined(newDoc, "cpus", e => e.TryGetProperty("name", out var n) ? n.GetString() : null) ?? pending.Cpu);
        Add("رم",
            rams.Count > 0 ? string.Join(" + ", rams.Select(r => $"{r.CapacityGb}GB {r.Type}".Trim())) : null,
            Joined(newDoc, "ramSticks", e =>
            {
                var gb = e.TryGetProperty("capacityGb", out var g) ? g.GetInt32() : 0;
                var ty = e.TryGetProperty("type", out var t) ? t.GetString() : null;
                return $"{gb}GB {ty}".Trim();
            }) ?? pending.Ram);
        Add("هارد دیسک‌ها",
            disks.Count > 0 ? string.Join(" + ", disks.Select(d => $"{d.Model} ({d.SizeGb}GB)")) : null,
            Joined(newDoc, "disks", e =>
            {
                var m = e.TryGetProperty("model", out var x) ? x.GetString() : "";
                var gb = e.TryGetProperty("sizeGb", out var y) ? y.GetInt32() : 0;
                return $"{m} ({gb}GB)";
            }) ?? pending.HardDisk);
        Add("گرافیک",
            gpus.Count > 0 ? string.Join(" + ", gpus.Select(g => g.Name)) : null,
            Joined(newDoc, "gpus", e => e.TryGetProperty("name", out var n) ? n.GetString() : null) ?? pending.Graphics);
        Add("مانیتورها",
            monitors.Count > 0 ? string.Join(" + ", monitors.Select(m => m.Name)) : null,
            Joined(newDoc, "monitors", e => e.TryGetProperty("name", out var n) ? n.GetString() : null) ?? pending.Monitor);
        Add("کارت‌های شبکه",
            nets.Count > 0 ? string.Join(" + ", nets.Select(n => $"{n.Name} [{n.MacAddress}]")) : null,
            Joined(newDoc, "netAdapters", e =>
            {
                var n = e.TryGetProperty("name", out var x) ? x.GetString() : "";
                var m = e.TryGetProperty("macAddress", out var y) ? y.GetString() : "";
                return $"{n} [{m}]";
            }));
        Add("سیستم‌عامل", current.OsName, pending.OsName);

        newDoc?.Dispose();
        return list;
    }

    // ================= موتور مقایسه =================

    /// <summary>
    /// مقایسه‌ی اطلاعات فعلی سیستم با داده‌ی جدید ایجنت.
    /// فقط مشخصات شناسایی‌کننده‌ی سخت‌افزار مقایسه می‌شوند (حجم مصرفی درایو و IP به‌خاطر تغییر لحظه‌ای مقایسه نمی‌شوند).
    /// </summary>
    private static List<DiffItem> BuildDiff(SystemInfo current, string pendingPayloadJson)
    {
        PendingPayload pending;
        try { pending = JsonSerializer.Deserialize<PendingPayload>(pendingPayloadJson, JOpt) ?? new PendingPayload(); }
        catch { pending = new PendingPayload(); }

        JsonDocument? curDoc = TryParse(current.DetailsJson);
        JsonDocument? newDoc = TryParse(pending.DetailsJson);

        var list = new List<DiffItem>();

        void Add(string field, string? oldV, string? newV)
        {
            oldV = Clean(oldV);
            newV = Clean(newV);
            list.Add(new DiffItem
            {
                Field = field,
                Old = oldV,
                New = newV,
                Changed = !string.Equals(oldV, newV, StringComparison.OrdinalIgnoreCase)
            });
        }

        // ---- از DetailsJson ساختاریافته (با fallback روی فیلدهای تخت) ----
        Add("مادربرد", Arr(curDoc, "board", One), Arr(newDoc, "board", One) ?? pending.Motherboard);
        Add("مدل سیستم", Arr(curDoc, "computerModel", One), Arr(newDoc, "computerModel", One));
        Add("سریال مادربرد", Arr(curDoc, "boardSerial", One), Arr(newDoc, "boardSerial", One));

        Add("پردازنده",
            Joined(curDoc, "cpus", e => e.TryGetProperty("name", out var n) ? n.GetString() : null) ?? current.Cpu,
            Joined(newDoc, "cpus", e => e.TryGetProperty("name", out var n) ? n.GetString() : null) ?? pending.Cpu);

        Add("رم",
            Joined(curDoc, "ramSticks", e =>
            {
                var gb = e.TryGetProperty("capacityGb", out var g) ? g.GetInt32() : 0;
                var ty = e.TryGetProperty("type", out var t) ? t.GetString() : null;
                return $"{gb}GB {ty}".Trim();
            }) ?? current.Ram,
            Joined(newDoc, "ramSticks", e =>
            {
                var gb = e.TryGetProperty("capacityGb", out var g) ? g.GetInt32() : 0;
                var ty = e.TryGetProperty("type", out var t) ? t.GetString() : null;
                return $"{gb}GB {ty}".Trim();
            }) ?? pending.Ram);

        Add("هارد دیسک‌ها",
            Joined(curDoc, "disks", e =>
            {
                var m = e.TryGetProperty("model", out var x) ? x.GetString() : "";
                var gb = e.TryGetProperty("sizeGb", out var y) ? y.GetInt32() : 0;
                return $"{m} ({gb}GB)";
            }) ?? current.HardDisk,
            Joined(newDoc, "disks", e =>
            {
                var m = e.TryGetProperty("model", out var x) ? x.GetString() : "";
                var gb = e.TryGetProperty("sizeGb", out var y) ? y.GetInt32() : 0;
                return $"{m} ({gb}GB)";
            }) ?? pending.HardDisk);

        Add("گرافیک",
            Joined(curDoc, "gpus", e => e.TryGetProperty("name", out var n) ? n.GetString() : null) ?? current.Graphics,
            Joined(newDoc, "gpus", e => e.TryGetProperty("name", out var n) ? n.GetString() : null) ?? pending.Graphics);

        Add("مانیتورها",
            Joined(curDoc, "monitors", e => e.TryGetProperty("name", out var n) ? n.GetString() : null) ?? current.Monitor,
            Joined(newDoc, "monitors", e => e.TryGetProperty("name", out var n) ? n.GetString() : null) ?? pending.Monitor);

        // شبکه: نام + MAC (بدون IP — DHCP)
        Add("کارت‌های شبکه",
            Joined(curDoc, "netAdapters", e =>
            {
                var n = e.TryGetProperty("name", out var x) ? x.GetString() : "";
                var m = e.TryGetProperty("macAddress", out var y) ? y.GetString() : "";
                return $"{n} [{m}]";
            }),
            Joined(newDoc, "netAdapters", e =>
            {
                var n = e.TryGetProperty("name", out var x) ? x.GetString() : "";
                var m = e.TryGetProperty("macAddress", out var y) ? y.GetString() : "";
                return $"{n} [{m}]";
            }));

        Add("سیستم‌عامل", current.OsName, pending.OsName);

        curDoc?.Dispose();
        newDoc?.Dispose();
        return list;
    }

    private static string? Clean(string? s)
    {
        s = s?.Trim();
        return string.IsNullOrEmpty(s) ? null : s;
    }

    private static JsonDocument? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonDocument.Parse(json); }
        catch { return null; }
    }

    private static string One(JsonElement e) => e.ValueKind == JsonValueKind.String ? (e.GetString() ?? "") : e.ToString();

    /// <summary>خواندن یک مقدار رشته‌ای مستقیم از JSON</summary>
    private static string? Arr(JsonDocument? doc, string prop, Func<JsonElement, string> fmt)
    {
        if (doc == null) return null;
        if (doc.RootElement.TryGetProperty(prop, out var el) && el.ValueKind != JsonValueKind.Null)
            return fmt(el);
        return null;
    }

    /// <summary>جمع‌بندی یک آرایه از اشیا به یک رشته (مثل: نام همه‌ی هاردها)</summary>
    private static string? Joined(JsonDocument? doc, string prop, Func<JsonElement, string?> fmt)
    {
        if (doc == null) return null;
        if (!doc.RootElement.TryGetProperty(prop, out var arr) || arr.ValueKind != JsonValueKind.Array) return null;
        var parts = arr.EnumerateArray().Select(fmt).Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!.Trim());
        var joined = string.Join(" + ", parts);
        return joined.Length == 0 ? null : joined;
    }
}

public class SystemInfoUserLink
{
    public int SystemInfoId { get; set; }
    public int UserId { get; set; }
}
