using System.Security.Claims;
using Inventory.Api.Data;
using Inventory.Api.Services;
using Inventory.Api.Services.Core;
using Inventory.Api.Services.ItAssets;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Controllers;

/// <summary>
/// مدیریت آی‌پی (IPAM) — رنج‌های شبکه به تفکیک شرکت با نمای شبکه‌ای:
///   سفید = خالی | سبز = ثبت‌شده در شناسنامه‌ها | زرد = اشغال در شبکه ولی بدون شناسنامه.
/// مجوزها: Ipam.View / Create / Update / Delete / Scan
/// </summary>
[ApiController]
[Route("api/ipam")]
public sealed class IpamController : ControllerBase
{
    public static readonly string[] ReservedTypes = { "Server", "Switch", "Router", "Firewall", "AccessPoint", "Printer", "Camera", "Pc", "Phone", "Other" };

    private readonly AppDbContext _db;
    private readonly IEffectivePermissions _perms;
    private readonly IpamScanner _scanner;

    public IpamController(AppDbContext db, IEffectivePermissions perms, IpamScanner scanner)
    {
        _db = db;
        _perms = perms;
        _scanner = scanner;
    }

    private int UserId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;
    private bool IsAdmin => User.IsInRole("Admin");

    private HashSet<string>? _permCache;
    private async Task<bool> HasAsync(string action)
    {
        _permCache ??= await _perms.GetAsync(User);
        return _permCache.Contains("Ipam." + action);
    }

    /// <summary>شرکت‌های مجاز کاربر — همان منطق انتخاب شرکت فعال (CompanyContextController).</summary>
    private async Task<List<int>> AllowedCompanyIdsAsync()
    {
        var ids = await _db.UserCompanyAccesses.AsNoTracking()
            .Where(x => x.UserId == UserId && x.Company.IsActive)
            .Select(x => x.CompanyId).ToListAsync();
        if (ids.Count > 0) return ids;

        var username = User.FindFirstValue(ClaimTypes.Name) ?? "";
        var legacy = await _db.SystemUsers.AsNoTracking()
            .Where(u => u.Username == username && u.CompanyId != null)
            .Select(u => u.CompanyId!.Value).FirstOrDefaultAsync();
        if (legacy > 0 && await _db.SystemCompanies.AnyAsync(c => c.Id == legacy && c.IsActive))
            return new List<int> { legacy };

        if (IsAdmin)
            return await _db.SystemCompanies.AsNoTracking().Where(c => c.IsActive).Select(c => c.Id).ToListAsync();
        return new List<int>();
    }

    private async Task<string> MyNameAsync()
    {
        var me = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == UserId);
        return me is null ? (User.FindFirstValue(ClaimTypes.Name) ?? "") : UserDisplay.Name(me);
    }

    private async Task<IpSubnet?> FindSubnetAsync(int id)
    {
        var s = await _db.IpSubnets.FirstOrDefaultAsync(x => x.Id == id);
        if (s is null) return null;
        var allowed = await AllowedCompanyIdsAsync();
        return allowed.Contains(s.CompanyId) ? s : null;
    }

    private static string? Clean(string? s, int max)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim();
        return s.Length > max ? s[..max] : s;
    }

    // ================================================================== شرکت‌ها

    [HttpGet("companies")]
    public async Task<IActionResult> Companies()
    {
        if (!await HasAsync("View")) return Forbid();
        var ids = await AllowedCompanyIdsAsync();
        var list = await _db.SystemCompanies.AsNoTracking()
            .Where(c => ids.Contains(c.Id))
            .OrderBy(c => c.Name)
            .Select(c => new { c.Id, c.Name, Count = _db.IpSubnets.Count(x => x.CompanyId == c.Id) })
            .ToListAsync();
        return Ok(list);
    }

    // ================================================================== رنج‌ها

    /// <summary>فهرست رنج‌های یک شرکت با آمار (کل/ثبت‌شده/اشغال بدون شناسنامه/خالی).</summary>
    [HttpGet("subnets")]
    public async Task<IActionResult> Subnets([FromQuery] int companyId)
    {
        if (!await HasAsync("View")) return Forbid();
        if (!(await AllowedCompanyIdsAsync()).Contains(companyId))
            return BadRequest(new { message = "به این شرکت دسترسی ندارید یا شرکت انتخاب نشده است." });

        var subnets = await _db.IpSubnets.AsNoTracking().Where(s => s.CompanyId == companyId)
            .OrderBy(s => s.NetworkNum).ThenBy(s => s.Prefix).ToListAsync();
        var ids = subnets.Select(s => s.Id).ToList();
        var alive = await _db.IpAddressEntries.AsNoTracking()
            .Where(e => ids.Contains(e.SubnetId) && e.IsAlive)
            .Select(e => new { e.SubnetId, e.IpNum }).ToListAsync();
        var registry = await IpamRegistry.BuildAsync(_db, companyId);

        var result = subnets.Select(s =>
        {
            var (first, last) = IpamTools.HostRange(s.NetworkNum, s.Prefix);
            var total = (int)(last - first + 1);
            var reg = registry.Keys.Count(k => k >= first && k <= last);
            var aliveSet = alive.Where(a => a.SubnetId == s.Id && a.IpNum >= first && a.IpNum <= last).Select(a => a.IpNum).ToHashSet();
            var unregistered = aliveSet.Count(n => !registry.ContainsKey(n));
            return new
            {
                s.Id, s.CompanyId, s.Name, s.Cidr, s.Prefix, s.Gateway, s.Vlan, s.Location, s.DhcpRange,
                s.AutoScanMinutes, s.LastScanAt,
                Total = total, Registered = reg, Unregistered = unregistered,
                Free = Math.Max(0, total - reg - unregistered),
                Scanning = _scanner.IsRunning(s.Id)
            };
        });
        return Ok(result);
    }

    public sealed class SubnetDto
    {
        public int CompanyId { get; set; }
        public string Name { get; set; } = "";
        public string Cidr { get; set; } = "";
        public string? Gateway { get; set; }
        public int? Vlan { get; set; }
        public string? DnsServers { get; set; }
        public string? DhcpRange { get; set; }
        public string? Location { get; set; }
        public string? Description { get; set; }
        public int AutoScanMinutes { get; set; }
    }

    private async Task<(string? Error, long Network, int Prefix)> ValidateAsync(SubnetDto d, int? id)
    {
        if (string.IsNullOrWhiteSpace(d.Name)) return ("نام رنج را وارد کنید.", 0, 0);
        if (!IpamTools.TryParseCidr(d.Cidr, out var net, out var prefix, out var err)) return (err, 0, 0);
        if (!string.IsNullOrWhiteSpace(d.Gateway))
        {
            if (!IpamTools.TryParseIp(d.Gateway, out var gw)) return ("آدرس گیت‌وی نامعتبر است.", 0, 0);
            var (f, l) = IpamTools.HostRange(net, prefix);
            if (gw < f || gw > l) return ("گیت‌وی باید داخل همین رنج باشد.", 0, 0);
        }
        if (d.Vlan is < 1 or > 4094) return ("شمارهٔ VLAN باید بین ۱ تا ۴۰۹۴ باشد.", 0, 0);
        if (d.AutoScanMinutes != 0 && (d.AutoScanMinutes < 5 || d.AutoScanMinutes > 1440))
            return ("فاصلهٔ اسکن خودکار باید بین ۵ تا ۱۴۴۰ دقیقه باشد (۰ = خاموش).", 0, 0);

        // هم‌پوشانی با رنج‌های دیگر همان شرکت
        var bcast = IpamTools.Broadcast(net, prefix);
        var others = await _db.IpSubnets.AsNoTracking()
            .Where(s => s.CompanyId == d.CompanyId && (id == null || s.Id != id))
            .Select(s => new { s.Name, s.Cidr, s.NetworkNum, s.Prefix }).ToListAsync();
        var clash = others.FirstOrDefault(o => o.NetworkNum <= bcast && IpamTools.Broadcast(o.NetworkNum, o.Prefix) >= net);
        if (clash != null) return ($"این رنج با «{clash.Name}» ({clash.Cidr}) هم‌پوشانی دارد.", 0, 0);
        return (null, net, prefix);
    }

    private static void Apply(IpSubnet s, SubnetDto d, long net, int prefix)
    {
        s.CompanyId = d.CompanyId;
        s.Name = Clean(d.Name, 150)!;
        s.NetworkNum = net;
        s.Prefix = prefix;
        s.Cidr = IpamTools.Cidr(net, prefix);
        s.Gateway = IpamTools.TryParseIp(d.Gateway, out var gw) ? IpamTools.ToIp(gw) : null;
        s.Vlan = d.Vlan;
        s.DnsServers = Clean(d.DnsServers, 200);
        s.DhcpRange = Clean(d.DhcpRange, 80);
        s.Location = Clean(d.Location, 250);
        s.Description = Clean(d.Description, 1000);
        s.AutoScanMinutes = d.AutoScanMinutes;
    }

    [HttpPost("subnets")]
    public async Task<IActionResult> CreateSubnet([FromBody] SubnetDto d)
    {
        if (!await HasAsync("Create")) return Forbid();
        if (!(await AllowedCompanyIdsAsync()).Contains(d.CompanyId)) return BadRequest(new { message = "به این شرکت دسترسی ندارید." });
        var (err, net, prefix) = await ValidateAsync(d, null);
        if (err != null) return BadRequest(new { message = err });

        var s = new IpSubnet { CreatedAt = DateTime.Now, UpdatedAt = DateTime.Now, UpdatedByName = await MyNameAsync() };
        Apply(s, d, net, prefix);
        _db.IpSubnets.Add(s);
        await _db.SaveChangesAsync();
        return Ok(new { id = s.Id });
    }

    [HttpPut("subnets/{id:int}")]
    public async Task<IActionResult> UpdateSubnet(int id, [FromBody] SubnetDto d)
    {
        if (!await HasAsync("Update")) return Forbid();
        var s = await FindSubnetAsync(id);
        if (s is null) return NotFound(new { message = "رنج پیدا نشد." });
        if (!(await AllowedCompanyIdsAsync()).Contains(d.CompanyId)) return BadRequest(new { message = "به شرکت مقصد دسترسی ندارید." });
        var (err, net, prefix) = await ValidateAsync(d, id);
        if (err != null) return BadRequest(new { message = err });

        var rangeChanged = s.NetworkNum != net || s.Prefix != prefix;
        Apply(s, d, net, prefix);
        s.UpdatedAt = DateTime.Now;
        s.UpdatedByName = await MyNameAsync();
        if (rangeChanged)
        {
            // ردیف‌های خارج از رنج جدید حذف می‌شوند (اسکن/رزروی که دیگر به این رنج تعلق ندارد)
            var outside = await _db.IpAddressEntries.Where(e => e.SubnetId == id).ToListAsync();
            _db.IpAddressEntries.RemoveRange(outside.Where(e => !IpamTools.Contains(net, prefix, e.IpNum)));
            s.LastScanAt = null;
        }
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    [HttpDelete("subnets/{id:int}")]
    public async Task<IActionResult> DeleteSubnet(int id)
    {
        if (!await HasAsync("Delete")) return Forbid();
        var s = await FindSubnetAsync(id);
        if (s is null) return NotFound(new { message = "رنج پیدا نشد." });
        if (_scanner.IsRunning(id)) return BadRequest(new { message = "این رنج در حال اسکن است؛ کمی بعد دوباره امتحان کنید." });
        _db.IpAddressEntries.RemoveRange(await _db.IpAddressEntries.Where(e => e.SubnetId == id).ToListAsync());
        _db.IpSubnets.Remove(s);
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    // ================================================================== نقشهٔ آی‌پی‌ها

    /// <summary>
    /// نقشهٔ کامل یک رنج. فقط آی‌پی‌های «غیرخالی» یا قبلاً دیده‌شده برگردانده می‌شوند؛ بقیه در کلاینت سفید رسم می‌شوند.
    /// state: registered (سبز) | unregistered (زرد) | free (سفید، ولی قبلاً دیده شده)
    /// </summary>
    [HttpGet("subnets/{id:int}/map")]
    public async Task<IActionResult> Map(int id)
    {
        if (!await HasAsync("View")) return Forbid();
        var s = await FindSubnetAsync(id);
        if (s is null) return NotFound(new { message = "رنج پیدا نشد." });

        var (first, last) = IpamTools.HostRange(s.NetworkNum, s.Prefix);
        var registry = await IpamRegistry.BuildAsync(_db, s.CompanyId);
        var entries = await _db.IpAddressEntries.AsNoTracking().Where(e => e.SubnetId == id).ToDictionaryAsync(e => e.IpNum);

        // MAC → صاحب (برای راهنمایی: «این دستگاه با آی‌پی دیگری در شناسنامه ثبت شده»)
        var macOwners = registry.SelectMany(kv => kv.Value.Where(o => o.Mac != null).Select(o => (o.Mac!, Ip: kv.Key, Owner: o)))
            .GroupBy(x => x.Item1).ToDictionary(g => g.Key, g => g.First());

        var nums = registry.Keys.Where(k => k >= first && k <= last)
            .Concat(entries.Keys.Where(k => k >= first && k <= last))
            .Distinct().OrderBy(k => k);

        int reg = 0, regOnline = 0, unreg = 0;
        var hosts = new List<object>();
        foreach (var n in nums)
        {
            registry.TryGetValue(n, out var owners);
            entries.TryGetValue(n, out var e);
            var alive = e?.IsAlive == true;
            var isReg = owners is { Count: > 0 };
            var state = isReg ? "registered" : alive ? "unregistered" : "free";
            if (isReg) { reg++; if (alive) regOnline++; }
            else if (alive) unreg++;

            string? macHint = null;
            if (!isReg && e?.Mac != null && macOwners.TryGetValue(e.Mac, out var mo))
                macHint = $"این MAC متعلق به «{mo.Owner.Title}» است که در شناسنامه با آی‌پی {IpamTools.ToIp(mo.Ip)} ثبت شده.";
            string? macMismatch = null;
            if (isReg && e?.Mac != null)
            {
                var known = owners!.Where(o => o.Mac != null).Select(o => o.Mac).ToList();
                if (known.Count > 0 && !known.Contains(e.Mac))
                    macMismatch = $"MAC دیده‌شده ({e.Mac}) با MAC ثبت‌شده در شناسنامه ({string.Join("، ", known)}) فرق دارد — احتمال تداخل آی‌پی.";
            }

            hosts.Add(new
            {
                Ip = IpamTools.ToIp(n), Num = n, State = state, Alive = alive,
                e?.LastSeenAt, e?.FirstSeenAt, e?.LastCheckedAt, e?.Mac, e?.Hostname, e?.Method,
                Owners = owners ?? new List<IpOwner>(),
                Conflict = owners != null && owners.Select(o => o.Source + ":" + o.RefId).Distinct().Count() > 1,
                Reserved = e is { IsReserved: true }
                    ? new { Title = e.ReservedTitle, Type = e.ReservedType, Owner = e.ReservedOwner, e.Note, e.UpdatedAt, e.UpdatedByName }
                    : null,
                MacHint = macHint, MacMismatch = macMismatch
            });
        }

        var total = (int)(last - first + 1);
        var progress = _scanner.GetProgress(id);
        return Ok(new
        {
            Subnet = new
            {
                s.Id, s.CompanyId, s.Name, s.Cidr, s.NetworkNum, s.Prefix, s.Gateway, s.Vlan, s.DnsServers, s.DhcpRange,
                s.Location, s.Description, s.AutoScanMinutes, s.LastScanAt, s.LastScanAliveCount, s.LastScanDurationMs,
                s.UpdatedAt, s.UpdatedByName,
                Broadcast = IpamTools.ToIp(IpamTools.Broadcast(s.NetworkNum, s.Prefix)),
                Mask = IpamTools.ToIp(IpamTools.MaskOf(s.Prefix))
            },
            Stats = new { Total = total, Registered = reg, RegisteredOnline = regOnline, RegisteredOffline = reg - regOnline, Unregistered = unreg, Free = Math.Max(0, total - reg - unreg) },
            Hosts = hosts,
            Scan = ScanDto(progress)
        });
    }

    // ================================================================== اسکن

    private static object? ScanDto(IpamScanProgress? p) => p is null ? null : new
    {
        p.Running, p.Progress, p.Total, Scanned = p.Scanned, Found = p.Found, p.Phase,
        p.StartedBy, p.StartedAt, p.FinishedAt, p.Error
    };

    [HttpPost("subnets/{id:int}/scan")]
    public async Task<IActionResult> StartScan(int id)
    {
        if (!await HasAsync("Scan")) return Forbid();
        var s = await FindSubnetAsync(id);
        if (s is null) return NotFound(new { message = "رنج پیدا نشد." });
        var started = _scanner.TryStart(id, await MyNameAsync());
        return Ok(new { started, scan = ScanDto(_scanner.GetProgress(id)) });
    }

    [HttpGet("subnets/{id:int}/scan")]
    public async Task<IActionResult> ScanStatus(int id)
    {
        if (!await HasAsync("View")) return Forbid();
        var s = await FindSubnetAsync(id);
        if (s is null) return NotFound(new { message = "رنج پیدا نشد." });
        return Ok(ScanDto(_scanner.GetProgress(id)) ?? new { Running = false });
    }

    public sealed class IpDto { public string Ip { get; set; } = ""; }

    /// <summary>بررسی فوری یک آی‌پی و ذخیرهٔ نتیجه.</summary>
    [HttpPost("subnets/{id:int}/probe")]
    public async Task<IActionResult> Probe(int id, [FromBody] IpDto d)
    {
        if (!await HasAsync("Scan")) return Forbid();
        var s = await FindSubnetAsync(id);
        if (s is null) return NotFound(new { message = "رنج پیدا نشد." });
        var (err, n) = CheckHostIp(s, d.Ip);
        if (err != null) return BadRequest(new { message = err });

        var ip = IpamTools.ToIp(n);
        var r = await _scanner.ProbeOneAsync(ip);
        var e = await _db.IpAddressEntries.FirstOrDefaultAsync(x => x.SubnetId == id && x.IpNum == n);
        var now = DateTime.Now;
        if (e is null && r.Alive)
        {
            e = new IpAddressEntry { SubnetId = id, Ip = ip, IpNum = n, FirstSeenAt = now };
            _db.IpAddressEntries.Add(e);
        }
        if (e != null)
        {
            e.IsAlive = r.Alive;
            e.LastCheckedAt = now;
            if (r.Alive) { e.LastSeenAt = now; e.FirstSeenAt ??= now; e.Method = r.Method; }
            if (r.Mac != null) e.Mac = r.Mac;
            if (r.Hostname != null) e.Hostname = r.Hostname.Length > 200 ? r.Hostname[..200] : r.Hostname;
            await _db.SaveChangesAsync();
        }
        return Ok(new { r.Alive, r.Method, r.Mac, r.Hostname });
    }

    // ================================================================== رزرو دستی

    private static (string? Error, long Num) CheckHostIp(IpSubnet s, string? ipText)
    {
        if (!IpamTools.TryParseIp(ipText, out var n)) return ("آی‌پی نامعتبر است.", 0);
        var (first, last) = IpamTools.HostRange(s.NetworkNum, s.Prefix);
        if (n < first || n > last) return ("این آی‌پی داخل رنج قابل استفاده نیست (آدرس شبکه/برادکست یا خارج از رنج).", 0);
        return (null, n);
    }

    public sealed class ReserveDto
    {
        public string Ip { get; set; } = "";
        public string? Title { get; set; }
        public string? Type { get; set; }
        public string? Owner { get; set; }
        public string? Note { get; set; }
    }

    [HttpPut("subnets/{id:int}/reserve")]
    public async Task<IActionResult> Reserve(int id, [FromBody] ReserveDto d)
    {
        if (!await HasAsync("Update")) return Forbid();
        var s = await FindSubnetAsync(id);
        if (s is null) return NotFound(new { message = "رنج پیدا نشد." });
        var (err, n) = CheckHostIp(s, d.Ip);
        if (err != null) return BadRequest(new { message = err });
        if (string.IsNullOrWhiteSpace(d.Title)) return BadRequest(new { message = "عنوان دستگاه/کاربرد را وارد کنید." });
        if (d.Type != null && !ReservedTypes.Contains(d.Type)) return BadRequest(new { message = "نوع دستگاه نامعتبر است." });

        var e = await _db.IpAddressEntries.FirstOrDefaultAsync(x => x.SubnetId == id && x.IpNum == n);
        if (e is null)
        {
            e = new IpAddressEntry { SubnetId = id, Ip = IpamTools.ToIp(n), IpNum = n };
            _db.IpAddressEntries.Add(e);
        }
        e.IsReserved = true;
        e.ReservedTitle = Clean(d.Title, 200);
        e.ReservedType = d.Type;
        e.ReservedOwner = Clean(d.Owner, 200);
        e.Note = Clean(d.Note, 500);
        e.UpdatedAt = DateTime.Now;
        e.UpdatedByName = await MyNameAsync();
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    [HttpDelete("subnets/{id:int}/reserve")]
    public async Task<IActionResult> Unreserve(int id, [FromQuery] string ip)
    {
        if (!await HasAsync("Update")) return Forbid();
        var s = await FindSubnetAsync(id);
        if (s is null) return NotFound(new { message = "رنج پیدا نشد." });
        if (!IpamTools.TryParseIp(ip, out var n)) return BadRequest(new { message = "آی‌پی نامعتبر است." });
        var e = await _db.IpAddressEntries.FirstOrDefaultAsync(x => x.SubnetId == id && x.IpNum == n);
        if (e is null || !e.IsReserved) return Ok(new { ok = true });

        e.IsReserved = false;
        e.ReservedTitle = e.ReservedType = e.ReservedOwner = e.Note = null;
        e.UpdatedAt = DateTime.Now;
        e.UpdatedByName = await MyNameAsync();
        // اگر هیچ اطلاعات اسکنی ندارد، ردیف را کامل حذف کن
        if (e.LastSeenAt == null) _db.IpAddressEntries.Remove(e);
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }
}
