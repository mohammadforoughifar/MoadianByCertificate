using Inventory.Api.Services;
using System.Text.Json;
using Inventory.Api.Data;
using Inventory.Api.Services.HrCore;
using Inventory.Shared;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services.HrReports;

public interface IHrReportService
{
    List<HrReportColumnDto> Meta(string entity);
    Task<HrReportResultDto> RunAsync(HrReportRunDto dto);
    Task<List<HrReportTemplateDto>> ListTemplatesAsync(int userId, Paging.Request? pagination = null);
    Task<HrReportTemplateDto> SaveTemplateAsync(int userId, HrReportTemplateSaveDto dto);
    Task DeleteTemplateAsync(int userId, int id);
}

/// <summary>گزارش‌ساز سفارشی HR: انتخاب موجودیت + ستون + فیلتر + قالب شخصی.</summary>
public class HrReportService : IHrReportService
{
    private readonly AppDbContext _db;

    public HrReportService(AppDbContext db) => _db = db;

    private static HrReportColumnDto Col(string key, string label, string type = "text",
        List<HrReportOptionDto>? options = null) => new()
        { Key = key, Label = label, Type = type, Options = options ?? new() };

    private static List<HrReportOptionDto> Opts(params (string v, string l)[] items)
        => items.Select(x => new HrReportOptionDto { Value = x.v, Label = x.l }).ToList();

    public List<HrReportColumnDto> Meta(string entity) => entity switch
    {
        "employee" => new()
        {
            Col("Code", "کد پرسنلی"), Col("FirstName", "نام"), Col("LastName", "نام خانوادگی"),
            Col("NationalCode", "کدملی"), Col("Mobile", "موبایل"), Col("HireDate", "تاریخ استخدام", "date"),
            Col("PostTitle", "سمت"), Col("OrgUnitName", "واحد"),
            Col("EmploymentType", "نوع همکاری", "enum", Opts(("0", "رسمی"), ("1", "قراردادی"), ("2", "پیمانی"), ("3", "ساعتی"), ("4", "مشاوره‌ای"))),
            Col("Status", "وضعیت", "enum", Opts(("0", "فعال"), ("1", "مرخصی بلندمدت"), ("2", "معلق"), ("3", "قطع همکاری"), ("4", "بازنشسته"))),
            Col("BaseSalary", "حقوق پایه", "number"),
        },
        "contract" => new()
        {
            Col("EmployeeName", "پرسنل"), Col("ContractNo", "شماره قرارداد"),
            Col("Type", "نوع", "enum", Opts(("0", "رسمی"), ("1", "قراردادی"), ("2", "پیمانی"), ("3", "ساعتی"), ("4", "مشاوره‌ای"))),
            Col("StartDate", "شروع", "date"), Col("EndDate", "پایان", "date"),
            Col("BaseSalary", "حقوق پایه", "number"), Col("JobTitle", "سمت"),
            Col("IsActive", "فعال", "bool"),
        },
        "decree" => new()
        {
            Col("EmployeeName", "پرسنل"), Col("DecreeNo", "شماره حکم"),
            Col("Type", "نوع حکم", "enum", Opts(("0", "استخدام"), ("1", "ارتقا"), ("2", "انتقال"), ("3", "تغییر حقوق"), ("4", "سایر"))),
            Col("EffectiveDate", "اجرا از", "date"), Col("NewPostTitle", "سمت جدید"),
            Col("NewBaseSalary", "حقوق جدید", "number"),
            Col("IsApplied", "اعمال‌شده", "bool"),
        },
        "leave" => new()
        {
            Col("EmployeeName", "پرسنل"), Col("LeaveType", "نوع مرخصی"),
            Col("FromDate", "از تاریخ", "date"), Col("ToDate", "تا تاریخ", "date"),
            Col("Days", "روز", "number"),
            Col("Status", "وضعیت", "enum", Opts(("0", "در انتظار"), ("1", "تأییدشده"), ("2", "ردشده"))),
        },
        "mission" => new()
        {
            Col("EmployeeName", "پرسنل"), Col("Destination", "مقصد"),
            Col("FromDate", "از تاریخ", "date"), Col("ToDate", "تا تاریخ", "date"),
            Col("Days", "روز", "number"),
            Col("Status", "وضعیت", "enum", Opts(("0", "در انتظار"), ("1", "تأییدشده"), ("2", "ردشده"))),
        },
        _ => throw new InvalidOperationException("موجودیت نامعتبر است.")
    };

    private record Row(Dictionary<string, string> Text, Dictionary<string, double> Num, Dictionary<string, DateTime> Dates);

    public async Task<HrReportResultDto> RunAsync(HrReportRunDto dto)
    {
        var meta = Meta(dto.Entity);
        var cols = dto.Columns.Count == 0 ? meta.Select(c => c.Key).ToList()
            : dto.Columns.Where(c => meta.Any(m => m.Key == c)).Distinct().ToList();
        if (cols.Count == 0) throw new InvalidOperationException("ستونی انتخاب نشده است.");
        var take = Math.Clamp(dto.Take, 1, 2000);

        var rows = dto.Entity switch
        {
            "employee" => await EmployeeRowsAsync(),
            "contract" => await ContractRowsAsync(),
            "decree" => await DecreeRowsAsync(),
            "leave" => await LeaveRowsAsync(),
            "mission" => await MissionRowsAsync(),
            _ => throw new InvalidOperationException("موجودیت نامعتبر است.")
        };

        foreach (var f in dto.Filters.Where(f => !string.IsNullOrWhiteSpace(f.Value)))
            rows = ApplyFilter(rows, meta, f).ToList();

        var total = rows.Count;
        var page = rows.Take(take).ToList();
        return new HrReportResultDto
        {
            Columns = meta.Where(m => cols.Contains(m.Key)).ToList(),
            Rows = page.Select(r => cols.Select(c =>
                r.Text.TryGetValue(c, out var v) ? v : "").ToList()).ToList(),
            Total = total,
            Truncated = total > page.Count
        };
    }

    private static IEnumerable<Row> ApplyFilter(IEnumerable<Row> rows, List<HrReportColumnDto> meta, HrReportFilterDto f)
    {
        var col = meta.FirstOrDefault(m => m.Key == f.Field);
        if (col == null) return rows;
        var val = (f.Value ?? "").Trim();
        return col.Type switch
        {
            "number" when double.TryParse(val, out var n) => rows.Where(r =>
                r.Num.TryGetValue(f.Field, out var v) && f.Op switch
                {
                    "eq" => v == n, "gte" => v >= n, "lte" => v <= n, _ => v.ToString().Contains(val)
                }),
            "date" when DateTime.TryParse(val, out var d) => rows.Where(r =>
                r.Dates.TryGetValue(f.Field, out var v) && f.Op switch
                {
                    "eq" => v.Date == d.Date, "gte" => v.Date >= d.Date, "lte" => v.Date <= d.Date, _ => true
                }),
            "enum" or "bool" => rows.Where(r =>
                r.Text.TryGetValue(f.Field, out var v) &&
                (v == val || RawOf(r, f.Field) == val)),
            _ => rows.Where(r =>
                r.Text.TryGetValue(f.Field, out var v) &&
                v.Contains(val, StringComparison.OrdinalIgnoreCase))
        };
    }

    private static string RawOf(Row r, string field)
        => r.Num.TryGetValue(field, out var v) ? v.ToString("0") : "";

    private static Row R() => new(new Dictionary<string, string>(), new Dictionary<string, double>(), new Dictionary<string, DateTime>());

    private static void SetText(Row r, string k, string? v) => r.Text[k] = v ?? "";
    private static void SetNum(Row r, string k, double v) { r.Num[k] = v; r.Text[k] = Fa.Digits(v.ToString("#,0.##")); }
    private static void SetDate(Row r, string k, DateTime? v)
    {
        if (v == null) r.Text[k] = "";
        else { r.Dates[k] = v.Value.Date; r.Text[k] = PersianDate.ToShortFa(v.Value); }
    }
    private static void SetEnum(Row r, string k, int v, string label) { r.Num[k] = v; r.Text[k] = label; }
    private static void SetBool(Row r, string k, bool v) { r.Num[k] = v ? 1 : 0; r.Text[k] = v ? "بله" : "خیر"; }

    private async Task<List<Row>> EmployeeRowsAsync()
    {
        var emps = await _db.HrEmployees.AsNoTracking().OrderBy(e => e.Code).Take(5000).ToListAsync();
        var unitIds = emps.Where(e => e.OrgUnitId != null).Select(e => e.OrgUnitId!.Value).Distinct().ToList();
        var units = unitIds.Count == 0 ? new Dictionary<int, string>()
            : await _db.HrOrgUnits.AsNoTracking().Where(u => unitIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.Name);
        var list = new List<Row>();
        foreach (var e in emps)
        {
            var r = R();
            SetText(r, "Code", e.Code); SetText(r, "FirstName", e.FirstName); SetText(r, "LastName", e.LastName);
            SetText(r, "NationalCode", e.NationalCode == null ? "" : Fa.Digits(e.NationalCode));
            SetText(r, "Mobile", e.Mobile == null ? "" : Fa.Digits(e.Mobile));
            SetDate(r, "HireDate", e.HireDate);
            SetText(r, "PostTitle", e.PostTitle);
            SetText(r, "OrgUnitName", e.OrgUnitId != null && units.TryGetValue(e.OrgUnitId.Value, out var n) ? n : "");
            SetEnum(r, "EmploymentType", (int)e.EmploymentType, HrCoreTexts.EmploymentType((int)e.EmploymentType));
            SetEnum(r, "Status", (int)e.Status, HrCoreTexts.EmployeeStatus((int)e.Status));
            SetNum(r, "BaseSalary", (double)e.BaseSalary);
            list.Add(r);
        }
        return list;
    }

    private async Task<List<Row>> ContractRowsAsync()
    {
        var rows = await _db.HrContracts.AsNoTracking().OrderByDescending(c => c.StartDate).Take(5000).ToListAsync();
        var names = await EmpNamesAsync(rows.Select(c => c.EmployeeId));
        var list = new List<Row>();
        foreach (var c in rows)
        {
            var r = R();
            SetText(r, "EmployeeName", names.TryGetValue(c.EmployeeId, out var n) ? n : "");
            SetText(r, "ContractNo", c.ContractNo);
            SetEnum(r, "Type", (int)c.Type, HrCoreTexts.EmploymentType((int)c.Type));
            SetDate(r, "StartDate", c.StartDate); SetDate(r, "EndDate", c.EndDate);
            SetNum(r, "BaseSalary", (double)c.BaseSalary);
            SetText(r, "JobTitle", c.JobTitle);
            SetBool(r, "IsActive", c.IsActive);
            list.Add(r);
        }
        return list;
    }

    private async Task<List<Row>> DecreeRowsAsync()
    {
        var rows = await _db.HrDecrees.AsNoTracking().OrderByDescending(d => d.EffectiveDate).Take(5000).ToListAsync();
        var names = await EmpNamesAsync(rows.Select(d => d.EmployeeId));
        var list = new List<Row>();
        foreach (var d in rows)
        {
            var r = R();
            SetText(r, "EmployeeName", names.TryGetValue(d.EmployeeId, out var n) ? n : "");
            SetText(r, "DecreeNo", d.DecreeNo);
            SetEnum(r, "Type", (int)d.Type, HrCoreTexts.DecreeType((int)d.Type));
            SetDate(r, "EffectiveDate", d.EffectiveDate);
            SetText(r, "NewPostTitle", d.NewPostTitle);
            if (d.NewBaseSalary != null) SetNum(r, "NewBaseSalary", (double)d.NewBaseSalary.Value);
            else r.Text["NewBaseSalary"] = "";
            SetBool(r, "IsApplied", d.IsApplied);
            list.Add(r);
        }
        return list;
    }

    private async Task<List<Row>> LeaveRowsAsync()
    {
        var rows = await _db.FaAttLeaves.AsNoTracking().OrderByDescending(l => l.FromDate).Take(10000).ToListAsync();
        var names = await EmpNamesAsync(rows.Select(l => l.EmployeeId));
        var typeIds = rows.Select(l => l.LeaveTypeId).Distinct().ToList();
        var types = typeIds.Count == 0 ? new Dictionary<int, string>()
            : await _db.FaAttLeaveTypes.AsNoTracking().Where(x => typeIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.Name);
        var list = new List<Row>();
        foreach (var l in rows)
        {
            var r = R();
            SetText(r, "EmployeeName", names.TryGetValue(l.EmployeeId, out var n) ? n : "");
            SetText(r, "LeaveType", types.TryGetValue(l.LeaveTypeId, out var t) ? t : "");
            SetDate(r, "FromDate", l.FromDate); SetDate(r, "ToDate", l.ToDate);
            var days = l.HoursPerDay != null
                ? l.HoursPerDay.Value / 8.0 * Math.Max(1, (l.ToDate.Date - l.FromDate.Date).Days + 1)
                : Math.Max(1, (l.ToDate.Date - l.FromDate.Date).Days + 1);
            SetNum(r, "Days", Math.Round(days, 2));
            var st = l.Status switch { FaAttRequestStatus.Approved => 1, FaAttRequestStatus.Rejected => 2, _ => 0 };
            SetEnum(r, "Status", st, st == 1 ? "تأییدشده" : st == 2 ? "ردشده" : "در انتظار");
            list.Add(r);
        }
        return list;
    }

    private async Task<List<Row>> MissionRowsAsync()
    {
        var rows = await _db.FaAttMissions.AsNoTracking().OrderByDescending(m => m.FromDate).Take(10000).ToListAsync();
        var names = await EmpNamesAsync(rows.Select(m => m.EmployeeId));
        var list = new List<Row>();
        foreach (var m in rows)
        {
            var r = R();
            SetText(r, "EmployeeName", names.TryGetValue(m.EmployeeId, out var n) ? n : "");
            SetText(r, "Destination", m.Destination);
            SetDate(r, "FromDate", m.FromDate); SetDate(r, "ToDate", m.ToDate);
            SetNum(r, "Days", Math.Max(1, (m.ToDate.Date - m.FromDate.Date).Days + 1));
            var st = m.Status switch { FaAttRequestStatus.Approved => 1, FaAttRequestStatus.Rejected => 2, _ => 0 };
            SetEnum(r, "Status", st, st == 1 ? "تأییدشده" : st == 2 ? "ردشده" : "در انتظار");
            list.Add(r);
        }
        return list;
    }

    private async Task<Dictionary<int, string>> EmpNamesAsync(IEnumerable<int> ids)
    {
        var list = ids.Distinct().ToList();
        if (list.Count == 0) return new();
        return await _db.HrEmployees.AsNoTracking().Where(e => list.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => e.FirstName + " " + e.LastName);
    }

    // ==================== قالب‌های شخصی ====================

    public async Task<List<HrReportTemplateDto>> ListTemplatesAsync(int userId, Paging.Request? pagination = null)
    {
        var rows = await _db.HrReportTemplates.AsNoTracking()
            .Where(x => x.UserId == userId).OrderBy(x => x.Name).ThenBy(x => x.Id).ToPageListAsync(pagination, defaultCap: 100);
        return rows.Select(MapTemplate).ToList();
    }

    private static HrReportTemplateDto MapTemplate(HrReportTemplate x) => new()
    {
        Id = x.Id, Name = x.Name, Entity = x.Entity, CreatedAt = x.CreatedAt,
        Columns = JsonSerializer.Deserialize<List<string>>(x.ColumnsJson) ?? new(),
        Filters = JsonSerializer.Deserialize<List<HrReportFilterDto>>(x.FiltersJson) ?? new()
    };

    public async Task<HrReportTemplateDto> SaveTemplateAsync(int userId, HrReportTemplateSaveDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name)) throw new InvalidOperationException("نام قالب خالی است.");
        Meta(dto.Entity);
        var x = new HrReportTemplate
        {
            UserId = userId, Name = dto.Name.Trim(), Entity = dto.Entity,
            ColumnsJson = JsonSerializer.Serialize(dto.Columns),
            FiltersJson = JsonSerializer.Serialize(dto.Filters)
        };
        _db.HrReportTemplates.Add(x);
        await _db.SaveChangesAsync();
        return MapTemplate(x);
    }

    public async Task DeleteTemplateAsync(int userId, int id)
    {
        var x = await _db.HrReportTemplates.FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId)
            ?? throw new InvalidOperationException("قالب یافت نشد.");
        _db.HrReportTemplates.Remove(x);
        await _db.SaveChangesAsync();
    }
}
