using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RadisHr.Api.Data;
using RadisHr.Shared.Calculations;
using RadisHr.Shared.Contracts;
using RadisHr.Shared.Models;
using Paging = Inventory.Api.Services.Paging;

namespace RadisHr.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/hse")]
public class HseController : ControllerBase
{
    private readonly AppDbContext _db;
    public HseController(AppDbContext db) => _db = db;

    private string Actor => User.Identity?.Name ?? "نامشخص";
    private string ActorKey => User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? "";

    // ───────── تعاریف پایه ─────────
    [HttpGet("definitions")]
    public async Task<ActionResult<object>> Definitions()
    {
        var defs = await _db.HseDefinitions.AsNoTracking().OrderBy(d => d.Ordinal).ToListAsync();
        return new
        {
            incidentTypes = defs.Where(d => d.Group == "incidentTypes").Select(d => d.Title).ToList(),
            ppeTypes = defs.Where(d => d.Group == "ppeTypes").Select(d => d.Title).ToList(),
            extinguisherTypes = defs.Where(d => d.Group == "extinguisherTypes").Select(d => d.Title).ToList(),
            severities = new[] { "جزئی", "متوسط", "شدید", "فوتی" },
            shifts = new[] { "صبح", "شب", "عادی" },
            conditions = new[] { "نو", "تعویض", "امانی" },
            earlyReplacementReasons = new[]
            {
                "پارگی یا شکستگی در حین کار",
                "فرسودگی زودرس",
                "مفقود شدن",
                "تغییر سایز",
                "آسیب ناشی از حادثه",
                "سایر موارد"
            }
        };
    }

    /// <summary>فهرست خام تعاریف همراه با شناسه — برای حذف از رابط کاربری</summary>
    [HttpGet("definitions/all")]
    public async Task<ActionResult<List<HseDefinition>>> AllDefinitions([FromQuery] int skip = 0, [FromQuery] int? take = null) =>
        Ok(await Paging.ResultAsync(_db.HseDefinitions.AsNoTracking().OrderBy(d => d.Group).ThenBy(d => d.Ordinal), skip, take));

    [Authorize(Roles = "hse,hr,ceo")]
    [HttpPost("definitions")]
    public async Task<ActionResult<ApiMessage>> AddDefinition(HseDefinition definition)
    {
        if (await _db.HseDefinitions.AnyAsync(d => d.Group == definition.Group && d.Title == definition.Title))
            return Conflict(new ApiMessage(false, "این عنوان قبلاً ثبت شده است."));
        definition.Id = 0;
        definition.Ordinal = await _db.HseDefinitions.CountAsync(d => d.Group == definition.Group);
        _db.HseDefinitions.Add(definition);
        await _db.SaveChangesAsync();
        return new ApiMessage(true, "تعریف افزوده شد.");
    }

    [Authorize(Roles = "hse,hr,ceo")]
    [HttpDelete("definitions/{id:int}")]
    public async Task<ActionResult<ApiMessage>> DeleteDefinition(int id)
    {
        var definition = await _db.HseDefinitions.FindAsync(id);
        if (definition == null) return NotFound(new ApiMessage(false, "یافت نشد."));
        _db.HseDefinitions.Remove(definition);
        await _db.SaveChangesAsync();
        return new ApiMessage(true, "حذف شد.");
    }

    // ───────── حوادث ─────────
    [HttpGet("incidents")]
    public async Task<ActionResult<List<Incident>>> Incidents([FromQuery] string? status, [FromQuery] string? unit, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        var query = _db.Incidents.Include(i => i.CorrectiveActions).Include(i => i.MedicalDocuments)
            .AsNoTracking().AsQueryable();
        if (!string.IsNullOrEmpty(status)) query = query.Where(i => i.Status == status);
        if (!string.IsNullOrEmpty(unit)) query = query.Where(i => i.Unit == unit);
        return Ok(await Paging.ResultAsync(query.OrderByDescending(i => i.CreatedAt), skip, take));
    }

    /// <summary>ثبت اولیهٔ حادثه توسط واحد اداری — شماره پرونده HSE-{سال}-{0001}</summary>
    [Authorize(Roles = "hr,hse,ceo")]
    [HttpPost("incidents")]
    public async Task<ActionResult<IdResponse>> CreateIncident(Incident incident)
    {
        var year = incident.Date.Length >= 4 && int.TryParse(incident.Date[..4], out var y)
            ? y : PersianCalendarUtil.Today().Jy;
        var count = await _db.Incidents.CountAsync(i => i.CaseNo.StartsWith($"HSE-{year}-"));

        incident.Id = 0;
        incident.Uid = Guid.NewGuid().ToString("N");
        incident.CaseNo = $"HSE-{year}-{count + 1:0000}";
        incident.Status = "ثبت اولیه";
        incident.CreatedBy = Actor;
        incident.CorrectiveActions = new List<CorrectiveAction>();
        incident.MedicalDocuments = new List<MedicalDocument>();

        _db.Incidents.Add(incident);
        await _db.SaveChangesAsync();
        return new IdResponse(incident.Id, $"حادثه با شماره پرونده {incident.CaseNo} ثبت شد.");
    }

    /// <summary>افزودن اقدام اصلاحی توسط HSE — وضعیت پرونده به «اقدامات اصلاحی» تغییر می‌کند</summary>
    [Authorize(Roles = "hse,ceo")]
    [HttpPost("incidents/{id:int}/actions")]
    public async Task<ActionResult<ApiMessage>> AddAction(int id, CorrectiveAction action)
    {
        var incident = await _db.Incidents.FindAsync(id);
        if (incident == null) return NotFound(new ApiMessage(false, "پرونده یافت نشد."));

        action.Id = 0;
        action.IncidentId = id;
        action.Uid = Guid.NewGuid().ToString("N");
        if (string.IsNullOrEmpty(action.Status)) action.Status = "باز";
        _db.CorrectiveActions.Add(action);

        incident.Status = "اقدامات اصلاحی";
        await _db.SaveChangesAsync();
        return new ApiMessage(true, "اقدام اصلاحی ثبت شد.");
    }

    [Authorize(Roles = "hse,ceo")]
    [HttpPut("incidents/{id:int}")]
    public async Task<ActionResult<ApiMessage>> UpdateIncident(int id, Incident input)
    {
        var incident = await _db.Incidents.FindAsync(id);
        if (incident == null) return NotFound(new ApiMessage(false, "پرونده یافت نشد."));

        incident.RootCause = input.RootCause;
        incident.LostDays = input.LostDays;
        incident.MedicalNotes = input.MedicalNotes;
        incident.LeaveRequired = input.LeaveRequired;
        incident.LeaveType = input.LeaveType;
        incident.Status = string.IsNullOrEmpty(input.Status) ? incident.Status : input.Status;
        await _db.SaveChangesAsync();
        return new ApiMessage(true, "پرونده به‌روزرسانی شد.");
    }

    /// <summary>بایگانی مدرک پزشکی — غیرقابل حذف و ویرایش (immutable)</summary>
    [Authorize(Roles = "hse,hr,ceo")]
    [HttpPost("incidents/{id:int}/documents")]
    public async Task<ActionResult<ApiMessage>> AddDocument(int id, MedicalDocument document)
    {
        if (!await _db.Incidents.AnyAsync(i => i.Id == id))
            return NotFound(new ApiMessage(false, "پرونده یافت نشد."));

        document.Id = 0;
        document.IncidentId = id;
        document.Uid = Guid.NewGuid().ToString("N");
        document.ArchivedBy = Actor;
        document.ArchivedAt = DateTime.UtcNow;
        document.Immutable = true;
        _db.MedicalDocuments.Add(document);
        await _db.SaveChangesAsync();
        return new ApiMessage(true, "مدرک پزشکی بایگانی شد (غیرقابل حذف).");
    }

    // ───────── تجهیزات حفاظت فردی ─────────
    [HttpGet("ppe")]
    public async Task<ActionResult<List<PpeDelivery>>> Ppe([FromQuery] string? code, [FromQuery] string? pending, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        var query = _db.PpeDeliveries.AsNoTracking().AsQueryable();
        if (!string.IsNullOrEmpty(code)) query = query.Where(p => p.EmployeeCode == code);
        if (pending == "hse") query = query.Where(p => p.HseApproval == "در انتظار");
        if (pending == "production") query = query.Where(p => p.ProductionApproval == "در انتظار");
        return Ok(await Paging.ResultAsync(query.OrderByDescending(p => p.CreatedAt), skip, take));
    }

    /// <summary>ثبت تحویل PPE توسط انباردار — نیازمند تأیید دوگانهٔ HSE و مدیر تولید</summary>
    [Authorize(Roles = "warehouse,hse,hr,ceo")]
    [HttpPost("ppe")]
    public async Task<ActionResult<IdResponse>> CreatePpe(PpeDelivery delivery)
    {
        delivery.Id = 0;
        delivery.Uid = Guid.NewGuid().ToString("N");
        delivery.HseApproval = "در انتظار";
        delivery.ProductionApproval = "در انتظار";
        delivery.CreatedBy = Actor;
        _db.PpeDeliveries.Add(delivery);

        // تعویض زودهنگام → اطلاع‌رسانی به HSE
        if (delivery.EarlyReplacement)
        {
            _db.HseNotifications.Add(new HseNotification
            {
                Uid = Guid.NewGuid().ToString("N"),
                PpeId = delivery.Uid,
                Reason = delivery.EarlyReplacementReason,
                Details = delivery.Notes,
                Message = $"تعویض زودهنگام {delivery.EquipmentType} برای پرسنل {delivery.EmployeeCode} — علت: {delivery.EarlyReplacementReason}"
            });
        }

        await _db.SaveChangesAsync();
        return new IdResponse(delivery.Id, "تحویل تجهیزات ثبت شد و در انتظار تأیید است.");
    }

    [Authorize(Roles = "hse,production,ceo")]
    [HttpPost("ppe/{id:int}/approve")]
    public async Task<ActionResult<ApiMessage>> ApprovePpe(int id, [FromQuery] bool approve)
    {
        var delivery = await _db.PpeDeliveries.FindAsync(id);
        if (delivery == null) return NotFound(new ApiMessage(false, "رکورد یافت نشد."));

        var decision = approve ? "تأیید" : "رد";
        if (ActorKey == "production")
        {
            delivery.ProductionApproval = decision;
            delivery.ProductionApprovedBy = Actor;
        }
        else
        {
            delivery.HseApproval = decision;
            delivery.HseApprovedBy = Actor;
        }

        await _db.SaveChangesAsync();
        var complete = delivery.HseApproval == "تأیید" && delivery.ProductionApproval == "تأیید";
        return new ApiMessage(true, complete
            ? "هر دو تأیید ثبت شد؛ رکورد تکمیل است."
            : $"تصمیم «{decision}» ثبت شد؛ در انتظار تأیید طرف دیگر.");
    }

    // ───────── ماتریس تجهیزات مجاز ─────────
    [HttpGet("ppe-authorizations")]
    public async Task<ActionResult<List<PpeAuthorization>>> PpeAuthorizations([FromQuery] int skip = 0, [FromQuery] int? take = null) =>
        Ok(await Paging.ResultAsync(_db.PpeAuthorizations.AsNoTracking().OrderBy(a => a.StationKey), skip, take));

    [Authorize(Roles = "hse,hr,ceo")]
    [HttpPut("ppe-authorizations")]
    public async Task<ActionResult<ApiMessage>> SetPpeAuthorizations(
        [FromQuery] string stationKey, [FromBody] List<string> equipmentTypes)
    {
        var existing = await _db.PpeAuthorizations.Where(a => a.StationKey == stationKey).ToListAsync();
        _db.PpeAuthorizations.RemoveRange(existing);
        foreach (var type in equipmentTypes.Distinct())
            _db.PpeAuthorizations.Add(new PpeAuthorization { StationKey = stationKey, EquipmentType = type });
        await _db.SaveChangesAsync();
        return new ApiMessage(true, "ماتریس تجهیزات مجاز ذخیره شد.");
    }

    // ───────── کپسول‌های آتش‌نشانی ─────────
    [HttpGet("extinguishers")]
    public async Task<ActionResult<List<Extinguisher>>> Extinguishers([FromQuery] int skip = 0, [FromQuery] int? take = null) =>
        Ok(await Paging.ResultAsync(_db.Extinguishers.AsNoTracking().OrderBy(e => e.ExpiryDate), skip, take));

    [Authorize(Roles = "hse,warehouse,ceo")]
    [HttpPost("extinguishers")]
    public async Task<ActionResult<IdResponse>> AddExtinguisher(Extinguisher extinguisher)
    {
        extinguisher.Id = 0;
        extinguisher.Uid = Guid.NewGuid().ToString("N");
        extinguisher.CreatedBy = Actor;
        _db.Extinguishers.Add(extinguisher);
        await _db.SaveChangesAsync();
        return new IdResponse(extinguisher.Id, "کپسول ثبت شد.");
    }

    [Authorize(Roles = "hse,ceo")]
    [HttpDelete("extinguishers/{id:int}")]
    public async Task<ActionResult<ApiMessage>> DeleteExtinguisher(int id)
    {
        var item = await _db.Extinguishers.FindAsync(id);
        if (item == null) return NotFound(new ApiMessage(false, "یافت نشد."));
        _db.Extinguishers.Remove(item);
        await _db.SaveChangesAsync();
        return new ApiMessage(true, "حذف شد.");
    }

    // ───────── اعلان‌های HSE ─────────
    [HttpGet("notifications")]
    public async Task<ActionResult<List<HseNotification>>> Notifications([FromQuery] int skip = 0, [FromQuery] int? take = null) =>
        Ok((await Paging.QueryAsync(_db.HseNotifications.AsNoTracking().OrderByDescending(n => n.CreatedAt), skip, take, 200)).Result());

    [Authorize(Roles = "hse,ceo")]
    [HttpPost("notifications/{id:int}/seen")]
    public async Task<ActionResult<ApiMessage>> MarkSeen(int id)
    {
        var notification = await _db.HseNotifications.FindAsync(id);
        if (notification == null) return NotFound(new ApiMessage(false, "یافت نشد."));
        notification.Seen = true;
        await _db.SaveChangesAsync();
        return new ApiMessage(true, "خوانده شد.");
    }

    // ───────── گزارش‌ها ─────────
    [HttpGet("reports")]
    public async Task<ActionResult<object>> Reports([FromQuery] int? year)
    {
        var incidents = await _db.Incidents.AsNoTracking().ToListAsync();
        if (year.HasValue) incidents = incidents.Where(i => i.Date.StartsWith(year.Value.ToString())).ToList();

        var ppe = await _db.PpeDeliveries.AsNoTracking().ToListAsync();
        var extinguishers = await _db.Extinguishers.AsNoTracking().ToListAsync();
        var today = PersianCalendarUtil.TodayKey();

        return new
        {
            totalIncidents = incidents.Count,
            lostDays = incidents.Sum(i => i.LostDays),
            bySeverity = incidents.GroupBy(i => i.Severity)
                .Select(g => new { severity = g.Key, count = g.Count() }).ToList(),
            byType = incidents.GroupBy(i => i.Type)
                .Select(g => new { type = g.Key, count = g.Count() }).OrderByDescending(x => x.count).ToList(),
            byUnit = incidents.GroupBy(i => i.Unit)
                .Select(g => new { unit = g.Key, count = g.Count() }).OrderByDescending(x => x.count).ToList(),
            byMonth = incidents.Where(i => i.Date.Length >= 7).GroupBy(i => i.Date[..7])
                .Select(g => new { month = g.Key, count = g.Count() }).OrderBy(x => x.month).ToList(),
            openActions = await _db.CorrectiveActions.CountAsync(a => a.Status == "باز"),
            ppeDeliveries = ppe.Count,
            ppePendingApproval = ppe.Count(p => p.HseApproval == "در انتظار" || p.ProductionApproval == "در انتظار"),
            earlyReplacements = ppe.Count(p => p.EarlyReplacement),
            extinguishersTotal = extinguishers.Count,
            extinguishersExpired = extinguishers.Count(e =>
                !string.IsNullOrEmpty(e.ExpiryDate) && string.CompareOrdinal(e.ExpiryDate, today) < 0)
        };
    }
}
