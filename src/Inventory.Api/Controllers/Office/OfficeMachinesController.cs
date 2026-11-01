using System.Globalization;
using System.Net;
using Inventory.Api.Data;
using Inventory.Api.Services;
using Inventory.Api.Services.Office;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Paging = Inventory.Api.Services.Paging;

namespace Inventory.Api.Controllers;

// ======================================================================= DTOها

public class OfficeMachineSupplyDto
{
    public int Index { get; set; }
    public string Kind { get; set; } = "";
    public string? Color { get; set; }
    public string? Description { get; set; }
    public int Level { get; set; }
    public int MaxCapacity { get; set; }
    public int? Percent { get; set; }
}

public class OfficeMachineDto
{
    public int Id { get; set; }
    public string DeviceType { get; set; } = "Printer";
    public string? Brand { get; set; }
    public string Model { get; set; } = "";
    public string? SerialNumber { get; set; }
    public string? AssetTag { get; set; }
    public bool IsColor { get; set; }
    public string? Location { get; set; }
    public string? Department { get; set; }
    public int? ResponsibleUserId { get; set; }
    public string? ResponsibleName { get; set; }
    public int? CompanyId { get; set; }
    public string? CompanyName { get; set; }
    public int? AssignedUserId { get; set; }
    public string? AssignedUserName { get; set; }
    public DateTime? InstallDate { get; set; }
    public string Status { get; set; } = "InService";
    public bool IsActive { get; set; } = true;
    public DateTime? GoneDate { get; set; }
    public DateTime? ReturnDate { get; set; }
    public string? Notes { get; set; }
    // اتصال
    public string ConnectionType { get; set; } = "Network";
    public string? IpAddress { get; set; }
    public int? LinkedSystemInfoId { get; set; }
    public string? LinkedSystemLabel { get; set; }
    public string? MacAddress { get; set; }
    public string? Hostname { get; set; }
    public string? FirmwareVersion { get; set; }
    public string? SysDescr { get; set; }
    // قرارداد
    public DateTime? PurchaseDate { get; set; }
    public decimal? PurchasePrice { get; set; }
    public DateTime? WarrantyUntil { get; set; }
    public string? ServiceVendor { get; set; }
    public DateTime? ServiceContractUntil { get; set; }
    // SNMP (بدون اسرار)
    public bool SnmpEnabled { get; set; }
    public string SnmpVersion { get; set; } = "v2c";
    public int SnmpPort { get; set; } = 161;
    public bool HasCommunity { get; set; }
    public string? SnmpV3User { get; set; }
    public string SnmpV3AuthProtocol { get; set; } = "SHA1";
    public bool HasV3Auth { get; set; }
    public string SnmpV3PrivProtocol { get; set; } = "AES";
    public bool HasV3Priv { get; set; }
    public int PollIntervalMinutes { get; set; } = 10;
    public bool AlertsEnabled { get; set; } = true;
    public int SupplyLowThreshold { get; set; } = 15;
    // وضعیت زنده
    public DateTime? LastPollAt { get; set; }
    public DateTime? LastSeenOnlineAt { get; set; }
    public string OnlineState { get; set; } = "Unknown";
    public string? PrinterState { get; set; }
    public long? LastPageCount { get; set; }
    public int? LowestSupplyPercent { get; set; }
    public string? LastStatusText { get; set; }
    public string? LastErrorCodes { get; set; }
    public string? LastPollError { get; set; }
    public long? UptimeSeconds { get; set; }
    public DateTime CreatedAt { get; set; }
    // آمار
    public int RepairCount { get; set; }
    public int OpenRepairs { get; set; }
    public decimal TotalCost { get; set; }
    public int OpenEvents { get; set; }
    public int CriticalEvents { get; set; }
    public long Pages30d { get; set; }
    public List<OfficeMachineSupplyDto> Supplies { get; set; } = new();
    // پایش با ایجنت (چاپگر USB)
    /// <summary>Snmp | Agent | None</summary>
    public string MonitorSource { get; set; } = "None";
    public int? AgentPrinterId { get; set; }
    public string? AgentPrinterName { get; set; }
    public string? AgentHost { get; set; }
    public string? AgentDriver { get; set; }
    public string? AgentPort { get; set; }
    public DateTime? AgentLastReportAt { get; set; }
    public bool AgentPrinterPresent { get; set; }
    public bool AgentPageLog { get; set; }
    public int JobsCount { get; set; }
    public int JobsInError { get; set; }
    public int? OldestJobMinutes { get; set; }
}

/// <summary>ورودی ثبت/ویرایش؛ اسرار خالی یعنی «مقدار قبلی حفظ شود».</summary>
public class OfficeMachineSaveDto
{
    public string DeviceType { get; set; } = "Printer";
    public string? Brand { get; set; }
    public string Model { get; set; } = "";
    public string? SerialNumber { get; set; }
    public string? AssetTag { get; set; }
    public bool IsColor { get; set; }
    public string? Location { get; set; }
    public string? Department { get; set; }
    public int? ResponsibleUserId { get; set; }
    public int? CompanyId { get; set; }
    public int? AssignedUserId { get; set; }
    public DateTime? InstallDate { get; set; }
    public string Status { get; set; } = "InService";
    public string? Notes { get; set; }
    public string ConnectionType { get; set; } = "Network";
    public string? IpAddress { get; set; }
    public int? LinkedSystemInfoId { get; set; }
    public string? LinkedSystemLabel { get; set; }
    /// <summary>چاپگر محلی گزارش‌شده توسط ایجنت سیستم متصل (فقط دستگاه کابلی)</summary>
    public int? AgentPrinterId { get; set; }
    public string? MacAddress { get; set; }
    public DateTime? PurchaseDate { get; set; }
    public decimal? PurchasePrice { get; set; }
    public DateTime? WarrantyUntil { get; set; }
    public string? ServiceVendor { get; set; }
    public DateTime? ServiceContractUntil { get; set; }
    public bool SnmpEnabled { get; set; }
    public string SnmpVersion { get; set; } = "v2c";
    public int SnmpPort { get; set; } = 161;
    public string? SnmpCommunity { get; set; }
    public string? SnmpV3User { get; set; }
    public string SnmpV3AuthProtocol { get; set; } = "SHA1";
    public string? SnmpV3AuthPassword { get; set; }
    public string SnmpV3PrivProtocol { get; set; } = "AES";
    public string? SnmpV3PrivPassword { get; set; }
    public int PollIntervalMinutes { get; set; } = 10;
    public bool AlertsEnabled { get; set; } = true;
    public int SupplyLowThreshold { get; set; } = 15;
}

public class SnmpSettingsDto
{
    public int? MachineId { get; set; }
    public string? IpAddress { get; set; }
    public string Version { get; set; } = "v2c";
    public int Port { get; set; } = 161;
    public string? Community { get; set; }
    public string? V3User { get; set; }
    public string V3AuthProtocol { get; set; } = "SHA1";
    public string? V3AuthPassword { get; set; }
    public string V3PrivProtocol { get; set; } = "AES";
    public string? V3PrivPassword { get; set; }
}

public class DiscoverRequest : SnmpSettingsDto
{
    public string Range { get; set; } = "";
    public bool PrintersOnly { get; set; } = true;
}

public class DiscoverImportItem
{
    public string Ip { get; set; } = "";
    public string? Brand { get; set; }
    public string? Model { get; set; }
    public string? SerialNumber { get; set; }
    public string? SysName { get; set; }
    public string? SysLocation { get; set; }
}

public class DiscoverImportRequest : SnmpSettingsDto
{
    public List<DiscoverImportItem> Items { get; set; } = new();
    public bool EnableSnmp { get; set; } = true;
    public int PollIntervalMinutes { get; set; } = 10;
}

// ======================================================================= کنترلر

/// <summary>مرکز مدیریت ماشین‌های اداری: شناسنامه، پایش SNMP، رویدادها، کشف شبکه، تعمیرات و هزینه‌ها.</summary>
[ApiController]
[Route("api/[controller]")]
public class OfficeMachinesController : RbacControllerBase
{
    // ماژول مجوز عمداً «SystemUsers» است: منوی کلاینت همین صفحه را با پیشوند «SystemUsers.»
    // گیت می‌کند (MainLayout.razor) و آن را در بخش مدیریتی قرار می‌دهد؛ API و UI یک قاعده دارند.
    private const string Module = "SystemUsers";
    private static readonly string[] Statuses = { "InService", "InRepair", "Standby", "Retired" };
    private static readonly string[] DeviceTypes = { "Printer", "MFP", "Copier", "Scanner", "Fax", "Plotter", "LabelPrinter", "Other" };
    private static readonly string[] CostCategories = { "Toner", "Paper", "Service", "Part", "Contract", "Other" };

    private readonly OfficeMachineMonitor _monitor;
    private readonly OfficeMachineMonitorWorker _worker;

    public OfficeMachinesController(AppDbContext db, OfficeMachineMonitor monitor, OfficeMachineMonitorWorker worker) : base(db)
    {
        _monitor = monitor;
        _worker = worker;
    }

    // ================================================================= فهرست / داشبورد

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string? q, [FromQuery] bool includeRetired = true, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        var pagination = new Paging.Request(skip, take);
        var query = Db.OfficeMachines.AsNoTracking().AsQueryable();
        if (!includeRetired) query = query.Where(m => m.Status != "Retired");
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim().ToUpper();
            query = query.Where(m => m.Model.ToUpper().Contains(term)
                || (m.SerialNumber ?? "").ToUpper().Contains(term)
                || (m.Location ?? "").ToUpper().Contains(term)
                || (m.Department ?? "").ToUpper().Contains(term)
                || (m.Brand ?? "").ToUpper().Contains(term)
                || (m.AssetTag ?? "").ToUpper().Contains(term)
                || (m.IpAddress ?? "").Contains(term)
                || Db.SystemUsers.Any(u => u.Id == m.AssignedUserId && (u.FirstName + " " + u.LastName + " " + u.StaffNumber).ToUpper().Contains(term))
                || Db.SystemCompanies.Any(c => c.Id == m.CompanyId && c.Name.ToUpper().Contains(term)));
        }
        var machines = await query.OrderBy(m => m.Id).ToPageListAsync(pagination);
        return Ok(pagination.Result(await ToDtosAsync(machines)));
    }

    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard()
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        var list = await Db.OfficeMachines.AsNoTracking().Where(m => m.Status != "Retired")
            .Select(m => new
            {
                m.Id, m.OnlineState, m.Status, m.LowestSupplyPercent, m.SupplyLowThreshold, m.WarrantyUntil, m.ServiceContractUntil,
                Monitored = (m.ConnectionType == "Network" && m.SnmpEnabled) || (m.ConnectionType == "Cable" && m.AgentPrinterId != null),
                ByAgent = m.ConnectionType == "Cable" && m.AgentPrinterId != null
            })
            .ToListAsync();
        var ids = list.Select(x => x.Id).ToList();
        var openEvents = await Db.OfficeMachineEvents.AsNoTracking().Where(e => !e.IsResolved && ids.Contains(e.MachineId))
            .GroupBy(e => e.Severity).Select(g => new { g.Key, Count = g.Count() }).ToListAsync();
        var pages = await PagesInWindowAsync(ids, 30);
        var soon = DateTime.Now.AddDays(30);
        return Ok(new
        {
            Total = list.Count,
            Monitored = list.Count(x => x.Monitored),
            MonitoredByAgent = list.Count(x => x.ByAgent),
            UnlinkedAgentPrinters = await Db.OfficeAgentPrinters.CountAsync(p => (p.Kind == "Usb" || p.Kind == "Local") && p.IsPresent && !p.IsIgnored
                && !Db.OfficeMachines.Any(m => m.AgentPrinterId == p.Id)),
            Online = list.Count(x => x.Monitored && x.OnlineState == "Online"),
            Warning = list.Count(x => x.Monitored && x.OnlineState == "Warning"),
            Error = list.Count(x => x.Monitored && x.OnlineState == "Error"),
            Offline = list.Count(x => x.Monitored && x.OnlineState == "Offline"),
            LowSupply = list.Count(x => x.LowestSupplyPercent != null && x.LowestSupplyPercent <= x.SupplyLowThreshold),
            InRepair = list.Count(x => x.Status == "InRepair"),
            OpenEvents = openEvents.Sum(x => x.Count),
            CriticalEvents = openEvents.Where(x => x.Key == "Critical").Sum(x => x.Count),
            Pages30d = pages.Values.Sum(),
            WarrantyExpiring = list.Count(x => x.WarrantyUntil != null && x.WarrantyUntil <= soon && x.WarrantyUntil >= DateTime.Now.Date),
            ContractExpiring = list.Count(x => x.ServiceContractUntil != null && x.ServiceContractUntil <= soon && x.ServiceContractUntil >= DateTime.Now.Date),
            Retired = await Db.OfficeMachines.CountAsync(m => m.Status == "Retired"),
        });
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Detail(int id)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        var m = await Db.OfficeMachines.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (m == null) return NotFound(new { message = "دستگاه یافت نشد." });
        var dto = (await ToDtosAsync(new List<OfficeMachine> { m })).First();
        var trays = await Db.OfficeMachineTrays.AsNoTracking().Where(t => t.MachineId == id).OrderBy(t => t.TrayIndex)
            .Select(t => new { t.TrayIndex, t.Name, t.MediaName, t.CurrentLevel, t.MaxCapacity, t.Percent, t.UpdatedAt }).ToListAsync();
        var events = await Db.OfficeMachineEvents.AsNoTracking().Where(e => e.MachineId == id)
            .OrderBy(e => e.IsResolved).ThenByDescending(e => e.LastSeenAt).Take(60).ToListAsync();
        var since = DateTime.Now.Date.AddMonths(-13);
        var counters = await Db.OfficeMachineCounters.AsNoTracking().Where(c => c.MachineId == id && c.Day >= since)
            .OrderBy(c => c.Day).Select(c => new { c.Day, c.TotalPages }).ToListAsync();

        // تیراژ ماهانه (ماه شمسی) = بیشینهٔ شمارندهٔ ماه − بیشینهٔ ماه قبل (یا کمینهٔ همان ماه برای اولین ماه)
        var pc = new PersianCalendar();
        var monthly = new List<object>();
        long? prevMax = null;
        foreach (var g in counters.GroupBy(c => (pc.GetYear(c.Day), pc.GetMonth(c.Day))))
        {
            var max = g.Max(x => x.TotalPages);
            var baseline = prevMax ?? g.Min(x => x.TotalPages);
            monthly.Add(new { Year = g.Key.Item1, Month = g.Key.Item2, Pages = Math.Max(0, max - baseline) });
            prevMax = max;
        }
        var daily = new List<object>();
        for (var i = 1; i < counters.Count; i++)
            if (counters[i].Day >= DateTime.Now.Date.AddDays(-30))
                daily.Add(new { counters[i].Day, Pages = Math.Max(0, counters[i].TotalPages - counters[i - 1].TotalPages) });

        return Ok(new { Machine = dto, Trays = trays, Events = events, Monthly = monthly.TakeLast(12), Daily = daily });
    }

    private async Task<List<OfficeMachineDto>> ToDtosAsync(List<OfficeMachine> machines)
    {
        var ids = machines.Select(m => m.Id).ToList();
        var repairs = await Db.OfficeMachineRepairs.AsNoTracking().Where(r => ids.Contains(r.MachineId))
            .Select(r => new { r.MachineId, r.Cost, r.Fixed, r.GoneDate, r.ReturnDate }).ToListAsync();
        var costs = await Db.OfficeMachineCosts.AsNoTracking().Where(c => ids.Contains(c.MachineId))
            .Select(c => new { c.MachineId, c.Amount }).ToListAsync();
        var events = await Db.OfficeMachineEvents.AsNoTracking().Where(e => !e.IsResolved && ids.Contains(e.MachineId))
            .Select(e => new { e.MachineId, e.Severity }).ToListAsync();
        var supplies = await Db.OfficeMachineSupplies.AsNoTracking().Where(s => ids.Contains(s.MachineId))
            .OrderBy(s => s.SupplyIndex).ToListAsync();
        var userIds = machines.Where(m => m.ResponsibleUserId != null).Select(m => m.ResponsibleUserId!.Value).Distinct().ToList();
        var users = await Db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.Username, u.FirstName, u.LastName }).ToListAsync();
        var assignedIds = machines.Where(m => m.AssignedUserId != null).Select(m => m.AssignedUserId!.Value).Distinct().ToList();
        var assigned = await Db.SystemUsers.AsNoTracking().Where(u => assignedIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FirstName, u.LastName }).ToListAsync();
        var companyIds = machines.Where(m => m.CompanyId != null).Select(m => m.CompanyId!.Value).Distinct().ToList();
        var companies = await Db.SystemCompanies.AsNoTracking().Where(c => companyIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Name }).ToListAsync();
        var pages = await PagesInWindowAsync(ids, 30);
        var agentIds = machines.Where(m => m.AgentPrinterId != null).Select(m => m.AgentPrinterId!.Value).Distinct().ToList();
        var agentPrinters = agentIds.Count == 0 ? new List<OfficeAgentPrinter>()
            : await Db.OfficeAgentPrinters.AsNoTracking().Where(p => agentIds.Contains(p.Id)).ToListAsync();

        return machines.Select(m =>
        {
            var ap = m.AgentPrinterId == null ? null : agentPrinters.FirstOrDefault(p => p.Id == m.AgentPrinterId);
            var source = MonitorSourceOf(m);
            var u = users.FirstOrDefault(x => x.Id == m.ResponsibleUserId);
            var uname = u == null ? null : ((u.FirstName + " " + u.LastName).Trim() is { Length: > 0 } n ? n : u.Username);
            return new OfficeMachineDto
            {
                Id = m.Id, DeviceType = m.DeviceType, Brand = m.Brand, Model = m.Model, SerialNumber = m.SerialNumber,
                AssetTag = m.AssetTag, IsColor = m.IsColor, Location = m.Location, Department = m.Department,
                ResponsibleUserId = m.ResponsibleUserId, ResponsibleName = uname, InstallDate = m.InstallDate,
                CompanyId = m.CompanyId, CompanyName = companies.FirstOrDefault(c => c.Id == m.CompanyId)?.Name,
                AssignedUserId = m.AssignedUserId,
                AssignedUserName = assigned.FirstOrDefault(x => x.Id == m.AssignedUserId) is { } au ? $"{au.FirstName} {au.LastName}".Trim() : null,
                Status = m.Status, IsActive = m.IsActive, GoneDate = m.GoneDate, ReturnDate = m.ReturnDate, Notes = m.Notes,
                ConnectionType = m.ConnectionType, IpAddress = m.IpAddress, LinkedSystemInfoId = m.LinkedSystemInfoId,
                LinkedSystemLabel = m.LinkedSystemLabel, MacAddress = m.MacAddress, Hostname = m.Hostname,
                FirmwareVersion = m.FirmwareVersion, SysDescr = m.SysDescr,
                PurchaseDate = m.PurchaseDate, PurchasePrice = m.PurchasePrice, WarrantyUntil = m.WarrantyUntil,
                ServiceVendor = m.ServiceVendor, ServiceContractUntil = m.ServiceContractUntil,
                SnmpEnabled = m.SnmpEnabled, SnmpVersion = m.SnmpVersion, SnmpPort = m.SnmpPort,
                HasCommunity = m.SnmpCommunityCipher != null, SnmpV3User = m.SnmpV3User,
                SnmpV3AuthProtocol = m.SnmpV3AuthProtocol, HasV3Auth = m.SnmpV3AuthCipher != null,
                SnmpV3PrivProtocol = m.SnmpV3PrivProtocol, HasV3Priv = m.SnmpV3PrivCipher != null,
                PollIntervalMinutes = m.PollIntervalMinutes, AlertsEnabled = m.AlertsEnabled, SupplyLowThreshold = m.SupplyLowThreshold,
                LastPollAt = m.LastPollAt, LastSeenOnlineAt = m.LastSeenOnlineAt,
                OnlineState = source == "None" ? "NotMonitored" : m.OnlineState,
                PrinterState = m.PrinterState, LastPageCount = m.LastPageCount, LowestSupplyPercent = m.LowestSupplyPercent,
                LastStatusText = m.LastStatusText, LastErrorCodes = m.LastErrorCodes, LastPollError = m.LastPollError,
                UptimeSeconds = m.UptimeSeconds, CreatedAt = m.CreatedAt,
                RepairCount = repairs.Count(r => r.MachineId == m.Id),
                OpenRepairs = repairs.Count(r => r.MachineId == m.Id && (!r.Fixed || (r.GoneDate != null && r.ReturnDate == null))),
                TotalCost = costs.Where(c => c.MachineId == m.Id).Sum(c => c.Amount) + repairs.Where(r => r.MachineId == m.Id).Sum(r => r.Cost),
                OpenEvents = events.Count(e => e.MachineId == m.Id),
                CriticalEvents = events.Count(e => e.MachineId == m.Id && e.Severity == "Critical"),
                Pages30d = pages.GetValueOrDefault(m.Id),
                Supplies = supplies.Where(s => s.MachineId == m.Id).Select(s => new OfficeMachineSupplyDto
                {
                    Index = s.SupplyIndex, Kind = s.Kind, Color = s.Color, Description = s.Description,
                    Level = s.Level, MaxCapacity = s.MaxCapacity, Percent = s.Percent
                }).ToList(),
                MonitorSource = source,
                AgentPrinterId = m.AgentPrinterId, AgentPrinterName = ap?.PrinterName, AgentHost = ap?.HostName ?? ap?.AgentId,
                AgentDriver = ap?.DriverName, AgentPort = ap?.PortName, AgentLastReportAt = ap?.LastReportAt,
                AgentPrinterPresent = ap?.IsPresent ?? false, AgentPageLog = ap?.AgentCumulativePages != null,
                JobsCount = ap?.JobsCount ?? 0, JobsInError = ap?.JobsInError ?? 0, OldestJobMinutes = ap?.OldestJobMinutes,
            };
        }).ToList();
    }

    private static string MonitorSourceOf(OfficeMachine m) =>
        m.ConnectionType == "Network" && m.SnmpEnabled ? "Snmp"
        : m.ConnectionType == "Cable" && m.AgentPrinterId != null ? "Agent" : "None";

    /// <summary>تیراژ هر دستگاه در N روز اخیر (اختلاف بیشینه و کمینهٔ شمارنده در بازه + آخرین مقدار قبل از بازه).</summary>
    private async Task<Dictionary<int, long>> PagesInWindowAsync(List<int> ids, int days)
    {
        var from = DateTime.Now.Date.AddDays(-days);
        var rows = await Db.OfficeMachineCounters.AsNoTracking()
            .Where(c => ids.Contains(c.MachineId) && c.Day >= from.AddDays(-7))
            .Select(c => new { c.MachineId, c.Day, c.TotalPages }).ToListAsync();
        return rows.GroupBy(r => r.MachineId).ToDictionary(g => g.Key, g =>
        {
            var inside = g.Where(x => x.Day >= from).ToList();
            if (inside.Count == 0) return 0L;
            var before = g.Where(x => x.Day < from).OrderByDescending(x => x.Day).FirstOrDefault();
            var baseline = before?.TotalPages ?? inside.Min(x => x.TotalPages);
            return Math.Max(0, inside.Max(x => x.TotalPages) - baseline);
        });
    }

    // ================================================================= ثبت / ویرایش / حذف

    [HttpPost]
    public async Task<IActionResult> Post([FromBody] OfficeMachineSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Module, "Create") is { } forbid) return forbid;
        var m = new OfficeMachine { CreatedAt = DateTime.Now };
        if (await ApplyAsync(m, dto, isNew: true) is { } error) return BadRequest(new { message = error });
        Db.OfficeMachines.Add(m);
        await Db.SaveChangesAsync();
        if (m.SnmpEnabled) _ = BackgroundPollAsync(m.Id);
        if (m.AgentPrinterId != null) await _monitor.ApplyAgentLinkNowAsync(m.Id);
        return Ok(new { id = m.Id });
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Put(int id, [FromBody] OfficeMachineSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Module, "Update") is { } forbid) return forbid;
        var m = await Db.OfficeMachines.FirstOrDefaultAsync(x => x.Id == id);
        if (m == null) return NotFound(new { message = "دستگاه یافت نشد." });
        var oldIp = m.IpAddress; var oldEnabled = m.SnmpEnabled; var oldSource = MonitorSourceOf(m); var oldAgent = m.AgentPrinterId;
        if (await ApplyAsync(m, dto, isNew: false) is { } error) return BadRequest(new { message = error });
        m.UpdatedAt = DateTime.Now;
        var source = MonitorSourceOf(m);
        if (source == "None" || source != oldSource || (source == "Agent" && oldAgent != m.AgentPrinterId))
        {
            // منبع پایش خاموش/عوض شد ← رویدادهای باز پایش قبلی بسته می‌شوند تا هشدار کهنه نماند
            await CloseOpenPollEventsAsync(m.Id, source == "None" ? "غیرفعال‌شدن پایش" : "تغییر منبع پایش");
            m.OnlineState = source == "None" ? "NotMonitored" : "Unknown";
            if (oldSource == "Snmp" && source != "Snmp")
            {
                m.LowestSupplyPercent = null;
                Db.OfficeMachineSupplies.RemoveRange(Db.OfficeMachineSupplies.Where(x => x.MachineId == m.Id));
                Db.OfficeMachineTrays.RemoveRange(Db.OfficeMachineTrays.Where(x => x.MachineId == m.Id));
            }
        }
        await Db.SaveChangesAsync();
        if (m.SnmpEnabled && (!oldEnabled || oldIp != m.IpAddress)) _ = BackgroundPollAsync(m.Id);
        if (source == "Agent" && oldAgent != m.AgentPrinterId) await _monitor.ApplyAgentLinkNowAsync(m.Id);
        return Ok();
    }

    private async Task<string?> ApplyAsync(OfficeMachine m, OfficeMachineSaveDto d, bool isNew)
    {
        static string? N(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        if (string.IsNullOrWhiteSpace(d.Model)) return "مدل الزامی است.";
        var conn = d.ConnectionType == "Cable" ? "Cable" : "Network";
        var ip = conn == "Network" ? N(d.IpAddress) : null;
        if (conn == "Network")
        {
            if (ip == null) return "برای دستگاه شبکه‌ای، آدرس IP را وارد کنید.";
            if (!IPAddress.TryParse(ip, out _) && Uri.CheckHostName(ip) == UriHostNameType.Unknown) return "آدرس IP/نام میزبان معتبر نیست.";
            var dup = await Db.OfficeMachines.AsNoTracking()
                .Where(x => x.Id != m.Id && x.IpAddress == ip && x.Status != "Retired" && x.ConnectionType == "Network")
                .Select(x => x.Model).FirstOrDefaultAsync();
            if (dup != null) return $"این IP قبلاً برای دستگاه «{dup}» ثبت شده است.";
        }
        if (conn == "Cable" && d.LinkedSystemInfoId is not > 0) return "برای دستگاه کابلی، سیستم متصل را انتخاب کنید.";
        int? agentPrinterId = null;
        if (conn == "Cable" && d.AgentPrinterId is > 0)
        {
            var ap = await Db.OfficeAgentPrinters.AsNoTracking().Where(p => p.Id == d.AgentPrinterId)
                .Select(p => new { p.Id, p.SystemInfoId, p.PrinterName }).FirstOrDefaultAsync();
            if (ap == null) return "چاپگر انتخاب‌شدهٔ ایجنت یافت نشد.";
            if (ap.SystemInfoId != d.LinkedSystemInfoId) return "چاپگر انتخاب‌شده متعلق به سیستم متصل این دستگاه نیست.";
            var other = await Db.OfficeMachines.AsNoTracking().Where(x => x.Id != m.Id && x.AgentPrinterId == ap.Id)
                .Select(x => x.Model).FirstOrDefaultAsync();
            if (other != null) return $"چاپگر «{ap.PrinterName}» قبلاً به دستگاه «{other}» لینک شده است.";
            agentPrinterId = ap.Id;
        }
        if (d.SnmpEnabled && conn != "Network") return "پایش SNMP فقط برای دستگاه‌های شبکه‌ای ممکن است.";
        var version = d.SnmpVersion is "v1" or "v3" ? d.SnmpVersion : "v2c";
        if (d.SnmpEnabled && version == "v3" && string.IsNullOrWhiteSpace(d.SnmpV3User) && string.IsNullOrWhiteSpace(m.SnmpV3User))
            return "برای SNMPv3 نام کاربری الزامی است.";

        m.DeviceType = DeviceTypes.Contains(d.DeviceType) ? d.DeviceType : "Other";
        m.Brand = Trunc(N(d.Brand), 80);
        m.Model = Trunc(d.Model.Trim(), 150)!;
        m.SerialNumber = Trunc(N(d.SerialNumber), 150);
        m.AssetTag = Trunc(N(d.AssetTag), 80);
        m.IsColor = d.IsColor;
        m.Location = Trunc(N(d.Location), 250);
        m.Department = Trunc(N(d.Department), 150);
        m.ResponsibleUserId = d.ResponsibleUserId is > 0 ? d.ResponsibleUserId : null;

        // اختصاص به شرکت و کاربر (کاربران مدیریت سیستم). اگر فقط کاربر انتخاب شود، شرکتِ او ثبت می‌شود.
        int? companyId = d.CompanyId is > 0 ? d.CompanyId : null;
        int? assignedUserId = d.AssignedUserId is > 0 ? d.AssignedUserId : null;
        if (companyId != null && !await Db.SystemCompanies.AnyAsync(c => c.Id == companyId))
            return "شرکت انتخاب‌شده یافت نشد.";
        if (assignedUserId != null)
        {
            var su = await Db.SystemUsers.AsNoTracking().Where(u => u.Id == assignedUserId)
                .Select(u => new { u.CompanyId, Name = (u.FirstName + " " + u.LastName).Trim() }).FirstOrDefaultAsync();
            if (su == null) return "کاربر انتخاب‌شده یافت نشد.";
            if (companyId == null) companyId = su.CompanyId;
            else if (su.CompanyId != null && su.CompanyId != companyId) return $"«{su.Name}» کاربر این شرکت نیست.";
        }
        m.CompanyId = companyId;
        m.AssignedUserId = assignedUserId;
        m.InstallDate = d.InstallDate;
        m.Status = Statuses.Contains(d.Status) ? d.Status : "InService";
        m.IsActive = m.Status is "InService" or "InRepair";
        m.Notes = Trunc(N(d.Notes), 500);
        m.ConnectionType = conn;
        m.IpAddress = ip;
        m.LinkedSystemInfoId = conn == "Cable" ? d.LinkedSystemInfoId : null;
        m.LinkedSystemLabel = conn == "Cable" ? Trunc(N(d.LinkedSystemLabel), 250) : null;
        m.AgentPrinterId = agentPrinterId;
        if (N(d.MacAddress) is { } mac) m.MacAddress = Trunc(mac.ToUpperInvariant(), 30);
        m.PurchaseDate = d.PurchaseDate;
        m.PurchasePrice = d.PurchasePrice is > 0 ? d.PurchasePrice : null;
        m.WarrantyUntil = d.WarrantyUntil;
        m.ServiceVendor = Trunc(N(d.ServiceVendor), 150);
        m.ServiceContractUntil = d.ServiceContractUntil;

        m.SnmpEnabled = d.SnmpEnabled && conn == "Network";
        m.SnmpVersion = version;
        m.SnmpPort = d.SnmpPort is > 0 and < 65536 ? d.SnmpPort : 161;
        if (!string.IsNullOrEmpty(d.SnmpCommunity)) m.SnmpCommunityCipher = _monitor.Encrypt(d.SnmpCommunity);
        else if (isNew && m.SnmpEnabled && version != "v3") m.SnmpCommunityCipher = _monitor.Encrypt("public");
        if (N(d.SnmpV3User) is { } user) m.SnmpV3User = Trunc(user, 100);
        m.SnmpV3AuthProtocol = d.SnmpV3AuthProtocol is "None" or "MD5" or "SHA256" ? d.SnmpV3AuthProtocol : "SHA1";
        m.SnmpV3PrivProtocol = d.SnmpV3PrivProtocol is "None" or "DES" ? d.SnmpV3PrivProtocol : "AES";
        if (!string.IsNullOrEmpty(d.SnmpV3AuthPassword)) m.SnmpV3AuthCipher = _monitor.Encrypt(d.SnmpV3AuthPassword);
        if (!string.IsNullOrEmpty(d.SnmpV3PrivPassword)) m.SnmpV3PrivCipher = _monitor.Encrypt(d.SnmpV3PrivPassword);
        m.PollIntervalMinutes = Math.Clamp(d.PollIntervalMinutes, 2, 1440);
        m.AlertsEnabled = d.AlertsEnabled;
        m.SupplyLowThreshold = Math.Clamp(d.SupplyLowThreshold, 1, 90);
        if (!m.SnmpEnabled) m.OnlineState = "NotMonitored";
        else if (m.OnlineState == "NotMonitored") m.OnlineState = "Unknown";
        return null;
    }

    /// <summary>
    /// حذف: پیش‌فرض «خارج از رده» (Soft) تا سابقهٔ تعمیر و هزینه حفظ شود.
    /// با hard=true رکورد و تمام سوابق آن برای همیشه پاک می‌شود.
    /// </summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, [FromQuery] bool hard = false)
    {
        if (await ForbiddenUnlessAsync(Module, "Delete") is { } forbid) return forbid;
        var item = await Db.OfficeMachines.FirstOrDefaultAsync(x => x.Id == id);
        if (item == null) return NotFound();
        if (!hard)
        {
            item.Status = "Retired"; item.IsActive = false; item.UpdatedAt = DateTime.Now;
            await CloseOpenPollEventsAsync(id, "خارج از رده شدن دستگاه");
            await Db.SaveChangesAsync();
            return Ok(new { retired = true });
        }
        Db.OfficeMachineRepairs.RemoveRange(Db.OfficeMachineRepairs.Where(r => r.MachineId == id));
        Db.OfficeMachineCosts.RemoveRange(Db.OfficeMachineCosts.Where(c => c.MachineId == id));
        Db.OfficeMachineSupplies.RemoveRange(Db.OfficeMachineSupplies.Where(c => c.MachineId == id));
        Db.OfficeMachineTrays.RemoveRange(Db.OfficeMachineTrays.Where(c => c.MachineId == id));
        Db.OfficeMachineCounters.RemoveRange(Db.OfficeMachineCounters.Where(c => c.MachineId == id));
        Db.OfficeMachineEvents.RemoveRange(Db.OfficeMachineEvents.Where(c => c.MachineId == id));
        Db.OfficeMachines.Remove(item);
        await Db.SaveChangesAsync();
        return Ok(new { deleted = true });
    }

    [HttpPost("{id:int}/restore")]
    public async Task<IActionResult> Restore(int id)
    {
        if (await ForbiddenUnlessAsync(Module, "Update") is { } forbid) return forbid;
        var item = await Db.OfficeMachines.FirstOrDefaultAsync(x => x.Id == id);
        if (item == null) return NotFound();
        item.Status = "InService"; item.IsActive = true; item.UpdatedAt = DateTime.Now;
        await Db.SaveChangesAsync();
        return Ok();
    }

    private async Task CloseOpenPollEventsAsync(int machineId, string reason)
    {
        var open = await Db.OfficeMachineEvents.Where(e => e.MachineId == machineId && !e.IsResolved).ToListAsync();
        foreach (var e in open) { e.IsResolved = true; e.ResolvedAt = DateTime.Now; e.ResolvedByName = reason; }
    }

    // ================================================================= پایش / تست / کشف

    [HttpPost("{id:int}/poll")]
    public async Task<IActionResult> Poll(int id, CancellationToken ct)
    {
        if (await ForbiddenUnlessAsync(Module, "Update") is { } forbid) return forbid;
        var agentInfo = await Db.OfficeMachines.AsNoTracking().Where(m => m.Id == id && m.ConnectionType == "Cable" && m.AgentPrinterId != null)
            .Select(m => new { m.AgentPrinterId }).FirstOrDefaultAsync(ct);
        if (agentInfo != null)
        {
            // دستگاه USB: داده از ایجنت می‌آید (هر ۵ دقیقه)؛ اینجا فقط آخرین گزارش دوباره ارزیابی می‌شود
            await _monitor.ApplyAgentLinkNowAsync(id, ct);
            var st = await Db.OfficeMachines.AsNoTracking().Where(m => m.Id == id).Select(m => new { m.OnlineState, m.LastPollError }).FirstAsync(ct);
            var last = await Db.OfficeAgentPrinters.AsNoTracking().Where(p => p.Id == agentInfo.AgentPrinterId).Select(p => p.LastReportAt).FirstOrDefaultAsync(ct);
            var ok = st.OnlineState is "Online" or "Warning" or "Error";
            return Ok(new
            {
                Success = ok,
                Error = ok ? null : (st.LastPollError ?? "ایجنت سیستم متصل گزارش تازه‌ای نفرستاده است."),
                State = st.OnlineState, NewEvents = 0, ResolvedEvents = 0, Source = "Agent", LastReportAt = last
            });
        }
        var r = await _monitor.PollAsync(id, ct);
        return Ok(new { r.Success, r.Error, r.State, r.NewEvents, r.ResolvedEvents });
    }

    [HttpPost("poll-all")]
    public async Task<IActionResult> PollAll()
    {
        if (await ForbiddenUnlessAsync(Module, "Update") is { } forbid) return forbid;
        _ = Task.Run(() => _worker.RunOnceAsync(force: true, CancellationToken.None));
        var count = await Db.OfficeMachines.CountAsync(m => m.SnmpEnabled && m.Status != "Retired" && m.ConnectionType == "Network");
        return Ok(new { queued = count });
    }

    /// <summary>تست اتصال SNMP قبل از ذخیره — اطلاعات دستگاه برای تکمیل خودکار فرم برگردانده می‌شود.</summary>
    [HttpPost("snmp/test")]
    public async Task<IActionResult> TestSnmp([FromBody] SnmpSettingsDto s, CancellationToken ct)
    {
        if (await ForbiddenUnlessAnyAsync(Module, "Create", "Update") is { } forbid) return forbid;
        if (string.IsNullOrWhiteSpace(s.IpAddress)) return BadRequest(new { message = "آدرس IP را وارد کنید." });
        var target = await BuildTargetAsync(s);
        try
        {
            var (snap, error) = await OfficeMachineMonitor.TryReadAsync(target, full: true, ct);
            if (snap == null) return Ok(new { ok = false, message = error?.Message ?? "دستگاه پاسخ نداد." });
            return Ok(new
            {
                ok = true,
                snap.Brand, snap.Model, snap.SerialNumber, snap.MacAddress, snap.SysName, snap.SysLocation, snap.SysDescr,
                snap.FirmwareVersion, snap.IsPrinter, snap.IsColor, snap.PageCount, snap.PrinterState, snap.DeviceStatus,
                snap.DisplayText, snap.UptimeSeconds,
                ErrorFlags = snap.ErrorFlags.Select(f => new { Code = f, Title = OfficeMachineMonitor.FlagTitle(f) }),
                Supplies = snap.Supplies.Select(x => new { x.Index, x.Kind, x.Color, x.Description, x.Percent }),
                Trays = snap.Trays.Select(x => new { x.Index, x.Name, x.CurrentLevel, x.MaxCapacity, x.Percent }),
            });
        }
        catch (SnmpQueryException ex) { return Ok(new { ok = false, message = ex.Message }); }
        catch (Exception ex) { return Ok(new { ok = false, message = "خطا در تست SNMP: " + ex.Message }); }
    }

    [HttpPost("discover")]
    public async Task<IActionResult> Discover([FromBody] DiscoverRequest req, CancellationToken ct)
    {
        if (await ForbiddenUnlessAsync(Module, "Create") is { } forbid) return forbid;
        List<IPAddress> ips;
        try { ips = OfficeMachineMonitor.ParseRange(req.Range, 1024); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        req.IpAddress = "0.0.0.0";
        var template = await BuildTargetAsync(req);
        var list = await _monitor.DiscoverAsync(ips, template, req.PrintersOnly, ct);
        return Ok(new { scanned = ips.Count, items = list });
    }

    [HttpPost("discover/import")]
    public async Task<IActionResult> DiscoverImport([FromBody] DiscoverImportRequest req)
    {
        if (await ForbiddenUnlessAsync(Module, "Create") is { } forbid) return forbid;
        if (req.Items.Count == 0) return BadRequest(new { message = "هیچ دستگاهی انتخاب نشده است." });
        var version = req.Version is "v1" or "v3" ? req.Version : "v2c";
        var existingIps = await Db.OfficeMachines.AsNoTracking().Where(m => m.IpAddress != null && m.Status != "Retired")
            .Select(m => m.IpAddress!).ToListAsync();
        var created = new List<OfficeMachine>();
        foreach (var it in req.Items.Where(i => IPAddress.TryParse(i.Ip, out _)).DistinctBy(i => i.Ip))
        {
            if (existingIps.Contains(it.Ip)) continue;
            var m = new OfficeMachine
            {
                DeviceType = "Printer", Brand = Trunc(it.Brand, 80), Model = Trunc(string.IsNullOrWhiteSpace(it.Model) ? $"دستگاه {it.Ip}" : it.Model, 150)!,
                SerialNumber = Trunc(it.SerialNumber, 150), Hostname = Trunc(it.SysName, 100), Location = Trunc(it.SysLocation, 250),
                ConnectionType = "Network", IpAddress = it.Ip, InstallDate = DateTime.Now.Date, CreatedAt = DateTime.Now,
                SnmpEnabled = req.EnableSnmp, SnmpVersion = version, SnmpPort = req.Port is > 0 and < 65536 ? req.Port : 161,
                SnmpCommunityCipher = version != "v3" ? _monitor.Encrypt(string.IsNullOrEmpty(req.Community) ? "public" : req.Community) : null,
                SnmpV3User = version == "v3" ? Trunc(req.V3User, 100) : null,
                SnmpV3AuthProtocol = req.V3AuthProtocol is "None" or "MD5" or "SHA256" ? req.V3AuthProtocol : "SHA1",
                SnmpV3AuthCipher = version == "v3" ? _monitor.Encrypt(req.V3AuthPassword) : null,
                SnmpV3PrivProtocol = req.V3PrivProtocol is "None" or "DES" ? req.V3PrivProtocol : "AES",
                SnmpV3PrivCipher = version == "v3" ? _monitor.Encrypt(req.V3PrivPassword) : null,
                PollIntervalMinutes = Math.Clamp(req.PollIntervalMinutes, 2, 1440),
                OnlineState = req.EnableSnmp ? "Unknown" : "NotMonitored",
                Notes = "افزوده‌شده با کشف خودکار شبکه"
            };
            Db.OfficeMachines.Add(m);
            created.Add(m);
        }
        await Db.SaveChangesAsync();
        if (req.EnableSnmp && created.Count > 0) _ = Task.Run(() => _worker.RunOnceAsync(force: false, CancellationToken.None));
        return Ok(new { created = created.Count, skipped = req.Items.Count - created.Count });
    }

    private async Task<SnmpTarget> BuildTargetAsync(SnmpSettingsDto s)
    {
        OfficeMachine? stored = null;
        if (s.MachineId is > 0) stored = await Db.OfficeMachines.AsNoTracking().FirstOrDefaultAsync(m => m.Id == s.MachineId);
        string? Pick(string? input, string? cipher) => !string.IsNullOrEmpty(input) ? input : _monitor.Decrypt(cipher);
        return new SnmpTarget
        {
            Host = (s.IpAddress ?? "").Trim(),
            Port = s.Port is > 0 and < 65536 ? s.Port : 161,
            Version = s.Version is "v1" or "v3" ? s.Version : "v2c",
            Community = Pick(s.Community, stored?.SnmpCommunityCipher) ?? "public",
            V3User = !string.IsNullOrWhiteSpace(s.V3User) ? s.V3User.Trim() : stored?.SnmpV3User,
            V3AuthProtocol = s.V3AuthProtocol,
            V3AuthPassword = Pick(s.V3AuthPassword, stored?.SnmpV3AuthCipher),
            V3PrivProtocol = s.V3PrivProtocol,
            V3PrivPassword = Pick(s.V3PrivPassword, stored?.SnmpV3PrivCipher),
            TimeoutMs = _monitor.TimeoutMs,
            Retries = _monitor.Retries,
        };
    }

    private Task BackgroundPollAsync(int id)
    {
        var services = HttpContext.RequestServices.GetRequiredService<IServiceScopeFactory>();
        return Task.Run(async () =>
        {
            try
            {
                using var scope = services.CreateScope();
                await scope.ServiceProvider.GetRequiredService<OfficeMachineMonitor>().PollAsync(id);
            }
            catch { /* نتیجه در وضعیت دستگاه ثبت می‌شود */ }
        });
    }

    // ================================================================= چاپگرهای گزارش‌شده توسط ایجنت

    /// <summary>
    /// چاپگرهایی که ایجنت سیستم‌ها گزارش کرده‌اند. برای فرم (systemId) و پنجرهٔ «کشف از ایجنت».
    /// به‌طور پیش‌فرض چاپگرهای مجازی (PDF/XPS/Fax) و اتصال به چاپگر اشتراکی سیستم‌های دیگر حذف می‌شوند.
    /// </summary>
    [HttpGet("agent-printers")]
    public async Task<IActionResult> AgentPrinters([FromQuery] int? systemId, [FromQuery] bool includeVirtual = false, [FromQuery] bool includeIgnored = false)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        var q = Db.OfficeAgentPrinters.AsNoTracking().AsQueryable();
        if (systemId is > 0) q = q.Where(p => p.SystemInfoId == systemId);
        if (!includeVirtual) q = q.Where(p => p.Kind != "Virtual" && p.Kind != "Connection");
        if (!includeIgnored) q = q.Where(p => !p.IsIgnored);
        var rows = await q.OrderBy(p => p.HostName).ThenBy(p => p.PrinterName).Take(2000).ToListAsync();

        var ids = rows.Select(r => r.Id).ToList();
        var linked = await Db.OfficeMachines.AsNoTracking().Where(m => m.AgentPrinterId != null && ids.Contains(m.AgentPrinterId.Value))
            .Select(m => new { m.Id, m.Model, AgentPrinterId = m.AgentPrinterId!.Value }).ToListAsync();
        var ips = rows.Where(r => r.PortIp != null).Select(r => r.PortIp!).Distinct().ToList();
        var byIp = await Db.OfficeMachines.AsNoTracking().Where(m => m.ConnectionType == "Network" && m.IpAddress != null && ips.Contains(m.IpAddress) && m.Status != "Retired")
            .Select(m => new { m.Id, m.Model, m.IpAddress }).ToListAsync();
        var sysIds = rows.Where(r => r.SystemInfoId != null).Select(r => r.SystemInfoId!.Value).Distinct().ToList();
        var systems = await Db.SystemInfos.AsNoTracking().Where(x => sysIds.Contains(x.Id))
            .Select(x => new
            {
                x.Id, x.AgentId,
                Owner = Db.SystemUsers.Where(u => u.Id == x.UserId).Select(u => (u.FirstName + " " + u.LastName).Trim()).FirstOrDefault()
            }).ToListAsync();
        var staleCut = DateTime.Now.AddMinutes(-_monitor.AgentStaleMinutes);

        return Ok(rows.Select(r =>
        {
            var lm = linked.FirstOrDefault(x => x.AgentPrinterId == r.Id);
            var ipm = r.PortIp == null ? null : byIp.FirstOrDefault(x => x.IpAddress == r.PortIp);
            var sys = systems.FirstOrDefault(x => x.Id == r.SystemInfoId);
            return new
            {
                r.Id, r.AgentId, r.SystemInfoId, SystemLabel = sys?.AgentId, SystemOwner = sys?.Owner, r.HostName,
                r.PrinterName, r.DriverName, r.PortName, r.Kind, r.PortIp, r.IsDefault, r.IsShared, r.Location,
                r.Status, StatusTitle = OfficeMachineMonitor.AgentStatusTitle(r.Status), r.ErrorCodes,
                r.JobsCount, r.JobsInError, r.OldestJobMinutes, r.PagesLogged, PageLog = r.AgentCumulativePages != null,
                r.IsPresent, r.IsIgnored, r.FirstSeenAt, r.LastSeenAt, r.LastReportAt, Stale = r.LastReportAt < staleCut,
                LinkedMachineId = lm?.Id, LinkedMachineModel = lm?.Model,
                NetworkMachineId = ipm?.Id, NetworkMachineModel = ipm?.Model,
            };
        }));
    }

    public class AgentImportRequest
    {
        public List<int> Ids { get; set; } = new();
        /// <summary>برای چاپگرهای پورت شبکه: ثبت به‌صورت دستگاه شبکه‌ای با پایش SNMP</summary>
        public bool EnableSnmpForNetwork { get; set; } = true;
        public string? Community { get; set; }
    }

    /// <summary>
    /// ثبت گروهی چاپگرهای کشف‌شده توسط ایجنت:
    ///   USB/LPT ← دستگاه «کابلی» لینک‌شده به همان سیستم و همان صف چاپ (پایش با ایجنت)
    ///   پورت شبکه (IP) ← دستگاه «شبکه‌ای» با پایش SNMP
    /// </summary>
    [HttpPost("agent-printers/import")]
    public async Task<IActionResult> ImportAgentPrinters([FromBody] AgentImportRequest req)
    {
        if (await ForbiddenUnlessAsync(Module, "Create") is { } forbid) return forbid;
        var ids = (req.Ids ?? new()).Distinct().Take(200).ToList();
        if (ids.Count == 0) return BadRequest(new { message = "چاپگری انتخاب نشده است." });
        var rows = await Db.OfficeAgentPrinters.AsNoTracking().Where(p => ids.Contains(p.Id)).ToListAsync();
        var sysIds = rows.Where(r => r.SystemInfoId != null).Select(r => r.SystemInfoId!.Value).Distinct().ToList();
        var systems = await Db.SystemInfos.AsNoTracking().Where(x => sysIds.Contains(x.Id))
            .Select(x => new
            {
                x.Id, x.AgentId, x.UserId, x.CompanyId,
                Owner = Db.SystemUsers.Where(u => u.Id == x.UserId).Select(u => (u.FirstName + " " + u.LastName).Trim()).FirstOrDefault()
            }).ToListAsync();

        var created = new List<OfficeMachine>();
        var skipped = new List<string>();
        foreach (var r in rows)
        {
            if (await Db.OfficeMachines.AnyAsync(m => m.AgentPrinterId == r.Id)) { skipped.Add($"«{r.PrinterName}» قبلاً لینک شده است."); continue; }
            var (brand, model) = SplitDriver(r.DriverName, r.PrinterName);
            var m = new OfficeMachine
            {
                CreatedAt = DateTime.Now, DeviceType = "Printer", Brand = Trunc(brand, 80), Model = Trunc(model, 150)!,
                Location = Trunc(r.Location, 250), Status = "InService", IsActive = true, AlertsEnabled = true,
                Notes = Trunc($"ثبت خودکار از ایجنت سیستم {r.HostName ?? r.AgentId}", 500),
            };
            if (OfficeMachineMonitor.IsPhysicalLocal(r.Kind))
            {
                var sys = systems.FirstOrDefault(x => x.Id == r.SystemInfoId);
                if (sys == null) { skipped.Add($"«{r.PrinterName}»: سیستم میزبان هنوز در فهرست سیستم‌ها ثبت نشده (گزارش سخت‌افزار ایجنت را بررسی کنید)."); continue; }
                m.ConnectionType = "Cable";
                m.LinkedSystemInfoId = sys.Id;
                m.LinkedSystemLabel = Trunc(string.IsNullOrWhiteSpace(sys.Owner) ? sys.AgentId : $"{sys.AgentId} — {sys.Owner}", 250);
                m.AgentPrinterId = r.Id;
                m.OnlineState = "Unknown";
                // چاپگر USB معمولاً مال همان کسی است که سیستم به او اختصاص دارد
                m.AssignedUserId = sys.UserId is > 0 ? sys.UserId : null;
                m.CompanyId = sys.CompanyId is > 0 ? sys.CompanyId : null;
            }
            else if (r.Kind == "Network" && r.PortIp != null)
            {
                if (await Db.OfficeMachines.AnyAsync(x => x.ConnectionType == "Network" && x.IpAddress == r.PortIp && x.Status != "Retired"))
                { skipped.Add($"«{r.PrinterName}»: IP {r.PortIp} قبلاً ثبت شده است."); continue; }
                m.ConnectionType = "Network";
                m.IpAddress = r.PortIp;
                m.SnmpEnabled = req.EnableSnmpForNetwork;
                m.SnmpVersion = "v2c";
                m.SnmpPort = 161;
                if (m.SnmpEnabled) m.SnmpCommunityCipher = _monitor.Encrypt(string.IsNullOrWhiteSpace(req.Community) ? "public" : req.Community.Trim());
                m.OnlineState = m.SnmpEnabled ? "Unknown" : "NotMonitored";
            }
            else { skipped.Add($"«{r.PrinterName}»: نوع پورت ({r.PortName}) قابل ثبت خودکار نیست."); continue; }
            Db.OfficeMachines.Add(m);
            created.Add(m);
        }
        await Db.SaveChangesAsync();
        foreach (var m in created)
        {
            if (m.AgentPrinterId != null) await _monitor.ApplyAgentLinkNowAsync(m.Id);
            else if (m.SnmpEnabled) _ = BackgroundPollAsync(m.Id);
        }
        return Ok(new { created = created.Count, skipped });
    }

    public class AgentIgnoreRequest { public bool Ignored { get; set; } = true; }

    /// <summary>پنهان کردن چاپگری که نباید ثبت شود (مثلاً چاپگر شخصی یا موقت).</summary>
    [HttpPost("agent-printers/{pid:int}/ignore")]
    public async Task<IActionResult> IgnoreAgentPrinter(int pid, [FromBody] AgentIgnoreRequest req)
    {
        if (await ForbiddenUnlessAsync(Module, "Update") is { } forbid) return forbid;
        var row = await Db.OfficeAgentPrinters.FirstOrDefaultAsync(p => p.Id == pid);
        if (row == null) return NotFound(new { message = "چاپگر یافت نشد." });
        row.IsIgnored = req.Ignored;
        await Db.SaveChangesAsync();
        return Ok();
    }

    /// <summary>حذف رکورد چاپگر ایجنت (مثلاً سیستم جمع‌آوری شده). دستگاه لینک‌شده «بدون پایش» می‌شود.</summary>
    [HttpDelete("agent-printers/{pid:int}")]
    public async Task<IActionResult> DeleteAgentPrinter(int pid)
    {
        if (await ForbiddenUnlessAsync(Module, "Delete") is { } forbid) return forbid;
        var row = await Db.OfficeAgentPrinters.FirstOrDefaultAsync(p => p.Id == pid);
        if (row == null) return NotFound(new { message = "چاپگر یافت نشد." });
        foreach (var m in await Db.OfficeMachines.Where(m => m.AgentPrinterId == pid).ToListAsync())
        {
            m.AgentPrinterId = null;
            m.OnlineState = "NotMonitored";
            await CloseOpenPollEventsAsync(m.Id, "حذف چاپگر ایجنت");
        }
        Db.OfficeAgentPrinters.Remove(row);
        await Db.SaveChangesAsync();
        return Ok();
    }

    private static readonly string[] KnownBrands =
        { "HP", "Canon", "Kyocera", "Ricoh", "Sharp", "Brother", "Xerox", "Konica Minolta", "Epson", "Lexmark", "Samsung", "Toshiba", "Pantum", "Panasonic", "OKI", "Dell", "Zebra", "TSC", "Godex", "Bixolon" };

    /// <summary>«HP LaserJet Pro M404 PCL 6» ← (HP, LaserJet Pro M404). پسوندهای زبان درایور (PCL/PS/UFR/…) حذف می‌شوند.</summary>
    internal static (string? Brand, string Model) SplitDriver(string? driver, string printerName)
    {
        // درایورهای عمومی (Canon Generic Plus، HP Universal، Kyocera Classic Universal، Microsoft IPP Class) مدل واقعی را نمی‌گویند ← نام صف چاپ
        var generic = string.IsNullOrWhiteSpace(driver) || System.Text.RegularExpressions.Regex.IsMatch(driver, @"\b(Generic|Universal|IPP Class|Class Driver|WSD Print)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var text = generic ? printerName.Trim() : driver!.Trim();
        text = System.Text.RegularExpressions.Regex.Replace(text,
            @"\s+(PCL\s*-?\s*6|PCL\s*-?\s*XL|PCL\s*5[ce]?|PCL|PS3?|PostScript|UFR\s*II(\s*LT)?|UFRII(\s*LT)?|KX|GDI|XPS|V4|Class Driver|Series|\(.*?\))\b.*$",
            "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
        if (text.Length == 0) text = printerName;
        if (text.StartsWith("Hewlett-Packard ", StringComparison.OrdinalIgnoreCase)) text = "HP " + text[16..];
        var brand = KnownBrands.FirstOrDefault(b => text.StartsWith(b + " ", StringComparison.OrdinalIgnoreCase));
        var model = brand == null ? text : text[(brand.Length + 1)..].Trim();
        return (brand, model.Length == 0 ? text : model);
    }

    // ================================================================= رویدادها

    [HttpGet("events")]
    public async Task<IActionResult> Events([FromQuery] bool openOnly = true, [FromQuery] int? machineId = null, [FromQuery] int take = 200)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        var q = Db.OfficeMachineEvents.AsNoTracking().AsQueryable();
        if (openOnly) q = q.Where(e => !e.IsResolved);
        if (machineId is > 0) q = q.Where(e => e.MachineId == machineId);
        var rows = await q.OrderBy(e => e.IsResolved).ThenByDescending(e => e.Severity == "Critical").ThenByDescending(e => e.LastSeenAt)
            .Take(Math.Clamp(take, 1, 1000))
            .Join(Db.OfficeMachines, e => e.MachineId, m => m.Id, (e, m) => new
            {
                e.Id, e.MachineId, e.Code, e.Severity, e.Message, e.Source, e.OpenedAt, e.LastSeenAt,
                e.IsResolved, e.ResolvedAt, e.ResolvedByName, m.Model, m.Location, m.IpAddress
            }).ToListAsync();
        return Ok(rows);
    }

    [HttpPost("events/{eventId:long}/resolve")]
    public async Task<IActionResult> ResolveEvent(long eventId)
    {
        if (await ForbiddenUnlessAsync(Module, "Update") is { } forbid) return forbid;
        var e = await Db.OfficeMachineEvents.FirstOrDefaultAsync(x => x.Id == eventId);
        if (e == null) return NotFound();
        if (!e.IsResolved)
        {
            e.IsResolved = true; e.ResolvedAt = DateTime.Now;
            e.ResolvedByName = Trunc(string.IsNullOrWhiteSpace(MyUsername) ? "کاربر" : MyUsername, 200);
            await Db.SaveChangesAsync();
        }
        return Ok();
    }

    // ================================================================= Lookupها

    /// <summary>سیستم‌های تاییدشده برای انتخاب در ماشین کابلی — با نام مالک و IP.</summary>
    [HttpGet("systems-lookup")]
    public async Task<IActionResult> SystemsLookup([FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        return Ok(await Paging.ResultAsync(Db.SystemInfos.AsNoTracking().Where(s => s.IsApproved)
            .Select(s => new
            {
                s.Id,
                Label = s.AgentId ?? "",
                Ip = Db.SystemNetAdapters.Where(n => n.SystemInfoId == s.Id && n.Ipv4 != "")
                    .Select(n => n.Ipv4).FirstOrDefault() ?? "",
                Owner = Db.SystemUsers.Where(u => u.Id == s.UserId)
                    .Select(u => (u.FirstName + " " + u.LastName).Trim()).FirstOrDefault() ?? "",
                s.UserId, s.CompanyId
            }), skip, take));
    }

    /// <summary>کاربران فعال برای انتخاب «مسئول دستگاه» (مقصد اعلان هشدار).</summary>
    [HttpGet("users-lookup")]
    public async Task<IActionResult> UsersLookup()
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        var users = await Db.Users.AsNoTracking().Where(u => u.IsActive)
            .OrderBy(u => u.Username)
            .Select(u => new { u.Id, u.Username, u.FirstName, u.LastName }).ToListAsync();
        return Ok(users.Select(u => new
        {
            u.Id,
            Name = string.IsNullOrWhiteSpace((u.FirstName + " " + u.LastName).Trim()) ? u.Username : (u.FirstName + " " + u.LastName).Trim(),
            u.Username
        }));
    }

    // ================= تعمیرات =================

    [HttpGet("{id:int}/repairs")]
    public async Task<IActionResult> Repairs(int id, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        return Ok(await Paging.ResultAsync(Db.OfficeMachineRepairs.AsNoTracking()
            .Where(r => r.MachineId == id)
            .OrderByDescending(r => r.RepairDate), skip, take));
    }

    [HttpPost("{id:int}/repairs")]
    public async Task<IActionResult> AddRepair(int id, [FromBody] OfficeMachineRepair r)
    {
        if (await ForbiddenUnlessAsync(Module, "Create") is { } forbid) return forbid;
        if (string.IsNullOrWhiteSpace(r.Problem))
            return BadRequest(new { message = "شرح ایراد الزامی است." });
        if (!await Db.OfficeMachines.AnyAsync(m => m.Id == id))
            return NotFound(new { message = "ماشین یافت نشد." });
        r.Id = 0;
        r.MachineId = id;
        r.RepairDate = DateTime.Now;
        Db.OfficeMachineRepairs.Add(r);
        await Db.SaveChangesAsync();
        await SyncRepairStatusAsync(id);
        return Ok(new { id = r.Id });
    }

    [HttpPut("repairs/{repairId:int}")]
    public async Task<IActionResult> UpdateRepair(int repairId, [FromBody] OfficeMachineRepair r)
    {
        if (await ForbiddenUnlessAsync(Module, "Update") is { } forbid) return forbid;
        var item = await Db.OfficeMachineRepairs.FirstOrDefaultAsync(x => x.Id == repairId);
        if (item == null) return NotFound();
        if (string.IsNullOrWhiteSpace(r.Problem))
            return BadRequest(new { message = "شرح ایراد الزامی است." });
        item.GoneDate = r.GoneDate;
        item.ReturnDate = r.ReturnDate;
        item.Problem = r.Problem;
        item.PerformedWork = r.PerformedWork;
        item.Cost = r.Cost;
        item.Fixed = r.Fixed;
        await Db.SaveChangesAsync();
        await SyncRepairStatusAsync(item.MachineId);
        return Ok();
    }

    [HttpDelete("repairs/{repairId:int}")]
    public async Task<IActionResult> DeleteRepair(int repairId)
    {
        if (await ForbiddenUnlessAsync(Module, "Delete") is { } forbid) return forbid;
        var r = await Db.OfficeMachineRepairs.FirstOrDefaultAsync(x => x.Id == repairId);
        if (r == null) return NotFound();
        Db.OfficeMachineRepairs.Remove(r);
        await Db.SaveChangesAsync();
        await SyncRepairStatusAsync(r.MachineId);
        return Ok();
    }

    /// <summary>
    /// تعمیرِ «رفته و برنگشته» ⇒ وضعیت دستگاه «در تعمیر»؛ وقتی همه برگشتند ⇒ «در حال کار».
    /// وضعیت‌های Standby/Retired دست نمی‌خورند.
    /// </summary>
    private async Task SyncRepairStatusAsync(int machineId)
    {
        var m = await Db.OfficeMachines.FirstOrDefaultAsync(x => x.Id == machineId);
        if (m == null || m.Status is "Standby" or "Retired") return;
        var away = await Db.OfficeMachineRepairs.AnyAsync(r => r.MachineId == machineId && r.GoneDate != null && r.ReturnDate == null);
        var target = away ? "InRepair" : "InService";
        if (m.Status == target) return;
        m.Status = target; m.IsActive = true; m.UpdatedAt = DateTime.Now;
        await Db.SaveChangesAsync();
    }

    // ================= هزینه‌ها =================

    [HttpGet("{id:int}/costs")]
    public async Task<IActionResult> Costs(int id, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        return Ok(await Paging.ResultAsync(Db.OfficeMachineCosts.AsNoTracking()
            .Where(c => c.MachineId == id)
            .OrderByDescending(c => c.CostDate), skip, take));
    }

    [HttpPost("{id:int}/costs")]
    public async Task<IActionResult> AddCost(int id, [FromBody] OfficeMachineCost c)
    {
        if (await ForbiddenUnlessAsync(Module, "Create") is { } forbid) return forbid;
        if (c.Amount <= 0)
            return BadRequest(new { message = "مبلغ باید بزرگ‌تر از صفر باشد." });
        if (string.IsNullOrWhiteSpace(c.Title))
            return BadRequest(new { message = "عنوان هزینه الزامی است." });
        if (!await Db.OfficeMachines.AnyAsync(m => m.Id == id))
            return NotFound(new { message = "ماشین یافت نشد." });
        c.Id = 0;
        c.MachineId = id;
        c.Title = Trunc(c.Title.Trim(), 300)!;
        c.Category = CostCategories.Contains(c.Category) ? c.Category : "Other";
        if (c.CostDate == default) c.CostDate = DateTime.Now;
        Db.OfficeMachineCosts.Add(c);
        await Db.SaveChangesAsync();
        return Ok(new { id = c.Id });
    }

    [HttpPut("costs/{costId:int}")]
    public async Task<IActionResult> UpdateCost(int costId, [FromBody] OfficeMachineCost c)
    {
        if (await ForbiddenUnlessAsync(Module, "Update") is { } forbid) return forbid;
        var item = await Db.OfficeMachineCosts.FirstOrDefaultAsync(x => x.Id == costId);
        if (item == null) return NotFound();
        if (c.Amount <= 0) return BadRequest(new { message = "مبلغ باید بزرگ‌تر از صفر باشد." });
        if (string.IsNullOrWhiteSpace(c.Title)) return BadRequest(new { message = "عنوان هزینه الزامی است." });
        item.Title = Trunc(c.Title.Trim(), 300)!;
        item.Amount = c.Amount;
        item.Category = CostCategories.Contains(c.Category) ? c.Category : "Other";
        if (c.CostDate != default) item.CostDate = c.CostDate;
        await Db.SaveChangesAsync();
        return Ok();
    }

    [HttpDelete("costs/{costId:int}")]
    public async Task<IActionResult> DeleteCost(int costId)
    {
        if (await ForbiddenUnlessAsync(Module, "Delete") is { } forbid) return forbid;
        var c = await Db.OfficeMachineCosts.FirstOrDefaultAsync(x => x.Id == costId);
        if (c == null) return NotFound();
        Db.OfficeMachineCosts.Remove(c);
        await Db.SaveChangesAsync();
        return Ok();
    }

    private static string? Trunc(string? s, int max) => s is null ? null : s.Length <= max ? s : s[..max];
}
