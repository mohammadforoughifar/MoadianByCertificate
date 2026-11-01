using Inventory.Api.Data;
using Inventory.Api.Services;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Paging = Inventory.Api.Services.Paging;

namespace Inventory.Api.Controllers;

/// <summary>ورود و خروج پروژه‌ها — ماژول دسترسی: Projects</summary>
[Route("api/projects")]
public class ProjectsController : RbacControllerBase
{
    private const string Module = "Projects";

    public ProjectsController(AppDbContext db) : base(db) { }

    /// <summary>نمایش نام کاربر: نام و نام خانوادگی، در غیر این صورت نام کاربری</summary>
    private static string DisplayOf(User u)
    {
        var full = $"{u.FirstName} {u.LastName}".Trim();
        return string.IsNullOrWhiteSpace(full) ? u.Username : full;
    }

    /// <summary>آیتم‌های کمبو: کاربران لاگین، کارفرماها، انواع فاکتور، پروژه‌ها.
    /// برای کاربران ماژول پروژه یا گزارش کار آزاد است.</summary>
    /// <param name="withProjects">
    /// اگر false باشد، فهرست کامل پروژه‌ها (Projects / ReportableProjects / BaseProjects) در پاسخ
    /// نمی‌آید و صفحه باید از اندپوینت <c>api/projects/lookup</c> (جستجوی سمت سرور) استفاده کند.
    /// </param>
    [HttpGet("lookups")]
    public async Task<IActionResult> Lookups([FromQuery] bool withProjects = true)
    {
        var allowed = await HasAsync(Module, "Read") || await HasAsync("ReportWorks", "Read")
                      || await HasAsync("Karfarmas", "Read") || await HasAsync("TypeFactors", "Read");
        if (!allowed) return StatusCode(403, new { message = "شما به این بخش دسترسی ندارید." });

        var rawUsers = await Db.Users.AsNoTracking().Where(u => u.IsActive)
            .OrderBy(u => u.FirstName).ThenBy(u => u.LastName)
            .ToListAsync();
        var users = rawUsers.Select(u => new LookupItem { Id = u.Id, Name = DisplayOf(u) }).ToList();

        // اپراتورها: همهٔ کاربران (فعال + غیرفعال) — کاربران قدیمیِ مهاجرت‌شده غیرفعال‌اند ولی گزارش‌های زیادی دارند
        var rawOperators = await Db.Users.AsNoTracking()
            .OrderByDescending(u => u.IsActive).ThenBy(u => u.FirstName).ThenBy(u => u.LastName)
            .ToListAsync();
        var operators = rawOperators.Select(u => new LookupItem { Id = u.Id, Name = DisplayOf(u) + (u.IsActive ? "" : " (غیرفعال)") }).ToList();

        var karfarmas = await Db.KarFarmas.AsNoTracking().Where(k => !k.IsDelete)
            .OrderBy(k => k.Name)
            .Select(k => new LookupItem { Id = k.Id, Name = k.Name })
            .ToListAsync();

        var typeFactors = await Db.TypeFactors.AsNoTracking().Where(t => !t.IsDelete)
            .OrderBy(t => t.Name)
            .Select(t => new LookupItem { Id = t.Id, Name = t.Name })
            .ToListAsync();

        if (!withProjects)
            return Ok(new ProjectLookups
            {
                Users = users,
                Operators = operators,
                KarFarmas = karfarmas,
                TypeFactors = typeFactors
            });

        // کمبوی پروژه: نام پروژه + کد پروژه (نه سریال — سریال مستقل از کد است)
        var projects = await Db.ProjectEntryExits.AsNoTracking().Where(p => !p.IsDelete)
            .OrderByDescending(p => p.Id)
            .Select(p => new LookupItem { Id = p.Id, Name = p.ProjectName + " (کد " + p.CodeProject + ")" })
            .ToListAsync();

        // کمبوی «پروژه» در فرم گزارش کار: فقط پروژه‌های تاییدشدهٔ مدیر
        // (۱=در انتظار کارشناسی، ۳=نهایی). پروژه‌های در انتظار تایید (۰) و ردشده (۲) قابل گزارش‌دهی نیستند.
        var reportableProjects = await Db.ProjectEntryExits.AsNoTracking()
            .Where(p => !p.IsDelete && (p.FlowStatus == 1 || p.FlowStatus == 3))
            .OrderByDescending(p => p.Id)
            .Select(p => new LookupItem { Id = p.Id, Name = p.ProjectName + " (کد " + p.CodeProject + ")" })
            .ToListAsync();

        // کمبوی «پروژه برگشتی» در فرم: فقط پروژه‌هایی که RE نیستند (برگشتی نمی‌تواند برگشتیِ برگشتی باشد)
        var baseProjects = await Db.ProjectEntryExits.AsNoTracking()
            .Where(p => !p.IsDelete && p.ReturnProjectId <= 0)
            .OrderByDescending(p => p.Id)
            .Select(p => new LookupItem { Id = p.Id, Name = p.ProjectName + " (کد " + p.CodeProject + ")" })
            .ToListAsync();

        return Ok(new ProjectLookups
        {
            Users = users,
            Operators = operators,
            KarFarmas = karfarmas,
            TypeFactors = typeFactors,
            Projects = projects,
            BaseProjects = baseProjects,
            ReportableProjects = reportableProjects
        });
    }

    /// <summary>
    /// جستجوی سمت سرور پروژه‌ها برای کمبوها — <b>فقط همان تعداد لازم</b> از دیتابیس خوانده می‌شود.
    /// <para>
    /// چرا لازم است: کمبوی «همه پروژه‌ها» پیش‌تر کل جدول پروژه‌ها را یک‌جا می‌گرفت؛ با
    /// چند هزار پروژه، هر بار باز کردن صفحه یک payload سنگین و فهرست بلند در کلاینت
    /// می‌ساخت. اکنون با تایپ هر عبارت، فقط ۲۰ نتیجهٔ منطبق از سرور می‌آید.
    /// </para>
    /// <param name="q">عبارت جستجو در نام یا کد پروژه (ارقام فارسی هم پذیرفته می‌شود).</param>
    /// <param name="take">تعداد نتیجه (پیش‌فرض ۲۰، حداکثر ۵۰).</param>
    /// <param name="reportable">فقط پروژه‌های قابل گزارش (تأییدشدهٔ مدیر: ۱=در انتظار کارشناسی، ۳=نهایی).</param>
    /// </summary>
    [HttpGet("lookup")]
    public async Task<IActionResult> ProjectLookup([FromQuery] string? q = null, [FromQuery] int take = 20,
        [FromQuery] bool reportable = false)
    {
        var allowed = await HasAsync(Module, "Read") || await HasAsync("ReportWorks", "Read");
        if (!allowed) return StatusCode(403, new { message = "شما به این بخش دسترسی ندارید." });

        take = take <= 0 ? 20 : Math.Min(take, 50);

        var query = Db.ProjectEntryExits.AsNoTracking().Where(p => !p.IsDelete);
        if (reportable) query = query.Where(p => p.FlowStatus == 1 || p.FlowStatus == 3);

        var term = Inventory.Shared.Fa.ToEn((q ?? "").Trim());
        if (term.Length > 0)
            query = query.Where(p => p.ProjectName.Contains(term) || p.CodeProject.Contains(term));

        var items = await query.OrderByDescending(p => p.Id).Take(take)
            .Select(p => new LookupItem { Id = p.Id, Name = p.ProjectName + " (کد " + p.CodeProject + ")" })
            .ToListAsync();

        return Ok(items);
    }

    /// <summary>کد پروژه پیشنهادی بعدی = بزرگ‌ترین کد عددی + ۱ (خودکار — فقط نمایشی)</summary>
    [HttpGet("next-serial")]
    public async Task<IActionResult> NextSerial()
    {
        if (await ForbiddenUnlessAsync(Module, "Create") is { } forbid) return forbid;
        return Ok(new { next = await NextCodeAsync() });
    }

    /// <summary>بزرگ‌ترین کد عددی (کدهای REn نادیده گرفته می‌شوند) + ۱ — حذف‌شده‌ها هم لحاظ می‌شوند تا کد تکراری صادر نشود</summary>
    private async Task<string> NextCodeAsync()
    {
        var codes = await Db.ProjectEntryExits.AsNoTracking()
            .Where(p => !p.CodeProject.StartsWith("RE"))
            .Select(p => p.CodeProject)
            .ToListAsync();
        var maxCode = codes.Select(c => int.TryParse(c, out var n) ? n : 0).DefaultIfEmpty(0).Max();
        return (maxCode + 1).ToString();
    }

    /// <summary>خروجی اکسل یک پروژه: شیت «اطلاعات پروژه» + شیت «گزارش‌های کار»</summary>
    [HttpGet("{id:int}/export")]
    public async Task<IActionResult> Export(int id)
    {
        if (await ForbiddenUnlessAsync(Module, "Export") is { } forbid) return forbid;
        var showFactor = await HasAsync(Module, "ViewFactor");

        var p = await Db.ProjectEntryExits.AsNoTracking()
            .Include(x => x.KarFarma)
            .Include(x => x.TypeFactor)
            .Include(x => x.User)
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDelete);
        if (p is null) return NotFound(new { message = "پروژه پیدا نشد." });

        var reports = await Db.ReportWorks.AsNoTracking()
            .Include(r => r.User)
            .Include(r => r.Operator)
            .Where(r => r.ProjectId == id && !r.IsDelete)
            .OrderBy(r => r.ReportDate)
            .ToListAsync();

        using var wb = new ClosedXML.Excel.XLWorkbook();
        var ws = wb.Worksheets.Add("اطلاعات پروژه");
        ws.RightToLeft = true;
        var row = 1;
        void Field(string title, string? value)
        {
            ws.Cell(row, 1).Value = title;
            ws.Cell(row, 1).Style.Font.Bold = true;
            ws.Cell(row, 2).Value = value ?? "—";
            row++;
        }

        Field("کد پروژه", p.CodeProject);
        Field("شماره سریال", p.SerialNumber);
        Field("نام پروژه", p.ProjectName);
        Field("کارفرما", p.KarFarma?.Name);
        if (showFactor) Field("نوع فاکتور", p.TypeFactor?.Name);
        Field("کاربر", p.User is null ? null : DisplayOf(p.User));
        Field("تحویل گیرنده", p.ProjectReceiver);
        if (showFactor) Field("شماره فاکتور", p.FactorNumber);
        Field("قبض خروج", p.GhabzExit);
        Field("کارشناسی اولیه", p.KarshenasiAvalie);
        Field("عدد برگشتی", p.ReturnProjectId > 0 ? $"RE{p.ReturnProjectId}" : "—");
        Field("تاریخ ورود", Shamsi(p.EntryDate));
        Field("تاریخ خروج", Shamsi(p.ExitDate));
        Field("تاریخ خروج موقت", Shamsi(p.TemporaryExitDate));
        Field("تاریخ پرونده (تحویل پرونده)", Shamsi(p.FileDate));
        Field("تاریخ تحویل", Shamsi(p.DeliveryDate));
        Field("تاریخ ثبت پروژه", Shamsi(p.ProjectRegistrationDate));
        Field("تاریخ موردنیاز مشتری", Shamsi(p.CustomerRequiredDate));
        Field("پوشه", p.IsFolder == true ? "دارد" : "ندارد");
        Field("کل زمان مصرفی", $"{(int)p.TotalSpentTime.TotalHours}:{p.TotalSpentTime.Minutes:00} ساعت");
        Field("شرح", p.Description);
        ws.Columns(1, 2).AdjustToContents();

        var wr = wb.Worksheets.Add("گزارش‌های کار");
        wr.RightToLeft = true;
        wr.Cell(1, 1).Value = "تاریخ";
        wr.Cell(1, 2).Value = "ثبت‌کننده";
        wr.Cell(1, 3).Value = "اپراتور";
        wr.Cell(1, 4).Value = "شروع";
        wr.Cell(1, 5).Value = "پایان";
        wr.Cell(1, 6).Value = "صبحانه";
        wr.Cell(1, 7).Value = "ناهار";
        wr.Cell(1, 8).Value = "ساعت خالص";
        wr.Cell(1, 9).Value = "شرح کار";
        wr.Range(1, 1, 1, 9).Style.Font.Bold = true;
        var r = 2;
        foreach (var x in reports)
        {
            wr.Cell(r, 1).Value = Inventory.Shared.PersianDate.ToShort(x.ReportDate);
            wr.Cell(r, 2).Value = x.User is null ? "" : DisplayOf(x.User);
            wr.Cell(r, 3).Value = x.Operator is null ? "" : DisplayOf(x.Operator);
            wr.Cell(r, 4).Value = x.StartTime.ToString("HH:mm");
            wr.Cell(r, 5).Value = x.EndTime.ToString("HH:mm");
            wr.Cell(r, 6).Value = x.BreakfastTime.ToString("HH:mm");
            wr.Cell(r, 7).Value = x.LunchTime.ToString("HH:mm");
            wr.Cell(r, 8).Value = $"{(int)x.SpentTime.TotalHours}:{x.SpentTime.Minutes:00}";
            wr.Cell(r, 9).Value = x.WorkDescription;
            r++;
        }
        wr.Cell(r, 7).Value = "جمع:";
        wr.Cell(r, 7).Style.Font.Bold = true;
        wr.Cell(r, 8).Value = $"{(int)p.TotalSpentTime.TotalHours}:{p.TotalSpentTime.Minutes:00}";
        wr.Cell(r, 8).Style.Font.Bold = true;
        wr.Columns(1, 9).AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        var fileName = $"project_{p.Id}_{p.SerialNumber}.xlsx";
        return File(ms.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileName);
    }

    /// <summary>
    /// خروجی اکسل پروژه‌ها — <b>دقیقاً بر اساس فیلترهای جاری لیست</b> (اگر فیلتری اعمال نشده باشد، همهٔ پروژه‌ها).
    /// هر پروژه یک سطر + سطر جمع ساعات (ستون‌های فاکتور فقط با مجوز رویت).
    /// </summary>
    [HttpGet("export")]
    public async Task<IActionResult> ExportAll([FromQuery] ProjectListQuery q)
    {
        if (await ForbiddenUnlessAsync(Module, "Export") is { } forbid) return forbid;
        var showFactor = await HasAsync(Module, "ViewFactor");

        var filtered = ApplyFilters(Db.ProjectEntryExits.AsNoTracking().Where(p => !p.IsDelete), q, showFactor);
        var ids = await ApplySort(filtered, q.Sort, q.Desc).Select(p => p.Id).ToListAsync();
        var projects = await LoadRowsAsync(ids);

        using var wb = new ClosedXML.Excel.XLWorkbook();
        var ws = wb.Worksheets.Add("کل پروژه‌ها");
        ws.RightToLeft = true;

        // ---------- سرستون‌ها ----------
        var headers = new List<string>
        {
            "کد پروژه", "نام پروژه", "شماره سریال", "کارفرما", "تحویل گیرنده"
        };
        if (showFactor) { headers.Add("نوع فاکتور"); headers.Add("شماره فاکتور"); }
        headers.AddRange(new[]
        {
            "تاریخ ورود", "تاریخ خروج", "تاریخ خروج موقت", "تاریخ ثبت پروژه", "تاریخ نیاز مشتری",
            "تاریخ تحویل", "تاریخ پرونده", "کارشناسی اولیه", "قبض خروج", "برگشتی",
            "تعداد گزارش کار", "کل زمان مصرفی (ساعت)", "پوشه", "ثبت‌کننده", "شرح"
        });

        for (var i = 0; i < headers.Count; i++)
        {
            var c = ws.Cell(1, i + 1);
            c.Value = headers[i];
            c.Style.Font.Bold = true;
            c.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#EEF2FF");
            c.Style.Border.BottomBorder = ClosedXML.Excel.XLBorderStyleValues.Thin;
        }
        ws.SheetView.FreezeRows(1);

        // ---------- ردیف‌ها ----------
        var row = 2;
        var totalTicks = 0L;
        foreach (var p in projects)
        {
            var col = 1;
            ws.Cell(row, col++).Value = p.CodeProject;
            ws.Cell(row, col++).Value = p.ProjectName;
            ws.Cell(row, col++).Value = p.SerialNumber;
            ws.Cell(row, col++).Value = p.KarFarma?.Name ?? "";
            ws.Cell(row, col++).Value = p.ProjectReceiver;
            if (showFactor)
            {
                ws.Cell(row, col++).Value = p.TypeFactor?.Name ?? "";
                ws.Cell(row, col++).Value = p.FactorNumber ?? "";
            }
            ws.Cell(row, col++).Value = Shamsi(p.EntryDate) ?? "";
            ws.Cell(row, col++).Value = Shamsi(p.ExitDate) ?? "";
            ws.Cell(row, col++).Value = Shamsi(p.TemporaryExitDate) ?? "";
            ws.Cell(row, col++).Value = Shamsi(p.ProjectRegistrationDate) ?? "";
            ws.Cell(row, col++).Value = Shamsi(p.CustomerRequiredDate) ?? "";
            ws.Cell(row, col++).Value = Shamsi(p.DeliveryDate) ?? "";
            ws.Cell(row, col++).Value = Shamsi(p.FileDate) ?? "";
            ws.Cell(row, col++).Value = p.KarshenasiAvalie ?? "";
            ws.Cell(row, col++).Value = p.GhabzExit ?? "";
            ws.Cell(row, col++).Value = p.ReturnProjectId > 0 ? $"RE{p.ReturnProjectId}" : "—";
            ws.Cell(row, col++).Value = p.ReportWorks.Count;
            ws.Cell(row, col++).Value = $"{(int)p.TotalSpentTime.TotalHours}:{p.TotalSpentTime.Minutes:00}";
            ws.Cell(row, col++).Value = p.IsFolder == true ? "دارد" : "ندارد";
            ws.Cell(row, col++).Value = p.User is null ? "" : DisplayOf(p.User);
            ws.Cell(row, col++).Value = p.Description ?? "";
            totalTicks += p.TotalSpentTime.Ticks;
            row++;
        }

        // ---------- سطر جمع ----------
        var total = TimeSpan.FromTicks(totalTicks);
        var totalCol = headers.IndexOf("کل زمان مصرفی (ساعت)") + 1;
        ws.Cell(row, 1).Value = $"جمع کل — {projects.Count} پروژه";
        ws.Cell(row, 1).Style.Font.Bold = true;
        ws.Cell(row, totalCol).Value = $"{(int)total.TotalHours}:{total.Minutes:00}";
        ws.Cell(row, totalCol).Style.Font.Bold = true;
        ws.Range(row, 1, row, headers.Count).Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#FEF3C7");

        ws.Columns(1, headers.Count).AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        var fileName = $"projects_all_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";
        return File(ms.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileName);
    }

    private static string? Shamsi(DateTime? dt) =>
        dt is null ? null : Inventory.Shared.PersianDate.ToShort(dt.Value);

    // =====================================================================
    //  فیلتر/مرتب‌سازی/صفحه‌بندی سمت سرور
    //  (لیست ۱۰۰۰+ رکوردی دیگر یکجا به کلاینت فرستاده نمی‌شود — هر بار فقط یک صفحه)
    // =====================================================================

    /// <summary>نرمال‌سازی متن فیلتر: ارقام فارسی/عربی → لاتین، ي/ك عربی → ی/ک فارسی</summary>
    private static string? NormFilter(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var t = Inventory.Shared.Fa.ToEn(s).Trim().Replace('\u064A', '\u06CC').Replace('\u0643', '\u06A9');
        return string.IsNullOrWhiteSpace(t) ? null : t;
    }

    // ================== فیلتر «پروژه‌های بدون داده» ==================
    // کاربر با تایپ «-» (یا واژه‌های خل / خالی / null / empty) در فیلتر ستون‌های تاریخِ اختیاری
    // (و شماره فاکتور / نوع فاکتور / کارشناسی) پروژه‌هایی را می‌بیند که آن مقدار در آن‌ها
    // ثبت نشده است — مثلاً «تاریخ خروج ندارند» یعنی هنوز از مجموعه خارج نشده‌اند.
    private static readonly string[] EmptyFilterTokens =
    { "-", "—", "خل", "خالی", "بدون مقدار", "بدون داده", "فاقد", "ندارد", "null", "empty" };

    /// <summary>آیا متنِ تایپ‌شده در فیلتر ستون به معنی «رکوردهای بدون مقدار» است؟</summary>
    private static bool IsEmptyFilter(string? text)
    {
        var t = NormFilter(text);
        if (t is null) return false;
        return EmptyFilterTokens.Contains(t.Trim().ToLowerInvariant());
    }

    /// <summary>
    /// تبدیل متن (بخشی از) تاریخ شمسی به بازهٔ میلادی — «۱۴۰۵» یک سال، «۱۴۰۵/۰۶» یک ماه و «۱۴۰۵/۰۶/۲۵» یک روز.
    /// اگر متن قابل تفسیر نباشد null برمی‌گردد (فیلتر نادیده گرفته می‌شود).
    /// </summary>
    private static (DateTime From, DateTime To)? JalaliRange(string? text)
    {
        var t = NormFilter(text);
        if (t is null) return null;
        t = t.Replace('-', '/').Replace('.', '/');
        var parts = t.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return null;
        if (!int.TryParse(parts[0], out var y) || y < 1200 || y > 1700) return null;

        DateTime from, to;
        if (parts.Length == 1)
        {
            from = Inventory.Shared.PersianDate.ToGregorian(y, 1, 1);
            to = Inventory.Shared.PersianDate.ToGregorian(y + 1, 1, 1);
        }
        else
        {
            if (!int.TryParse(parts[1], out var m) || m < 1 || m > 12) return null;
            if (parts.Length == 2)
            {
                from = Inventory.Shared.PersianDate.ToGregorian(y, m, 1);
                var ny = m == 12 ? y + 1 : y;
                var nm = m == 12 ? 1 : m + 1;
                to = Inventory.Shared.PersianDate.ToGregorian(ny, nm, 1);
            }
            else
            {
                if (!int.TryParse(parts[2], out var d) || d < 1 || d > 31) return null;
                from = Inventory.Shared.PersianDate.ToGregorian(y, m, d);
                to = from == DateTime.MinValue ? DateTime.MinValue : from.AddDays(1);
            }
        }
        if (from == DateTime.MinValue || to == DateTime.MinValue) return null;
        return (from, to);
    }

    /// <summary>تبدیل متن ساعت («۲» یا «۲:۳۰») به بازهٔ زمانی برای فیلتر ستون‌های ساعتی</summary>
    private static (TimeSpan From, TimeSpan To)? SpanRange(string? text)
    {
        var t = NormFilter(text);
        if (t is null) return null;
        var parts = t.Split(':');
        if (!int.TryParse(parts[0].Trim(), out var h) || h < 0 || h > 999) return null;
        if (parts.Length == 1 || string.IsNullOrWhiteSpace(parts[1]))
            return (TimeSpan.FromHours(h), TimeSpan.FromHours(h + 1));
        if (!int.TryParse(parts[1].Trim(), out var mi) || mi < 0 || mi > 59) return null;
        var from = new TimeSpan(h, mi, 0);
        return (from, from.Add(TimeSpan.FromMinutes(1)));
    }

    /// <summary>اعمال همهٔ فیلترها (بالای صفحه + جستجوی سرستون‌ها) روی کوئری</summary>
    private static IQueryable<ProjectEntryExit> ApplyFilters(
        IQueryable<ProjectEntryExit> query, ProjectListQuery q, bool showFactor)
    {
        var search = NormFilter(q.Search);
        if (search is not null)
            query = query.Where(p =>
                p.ProjectName.Contains(search) ||
                p.CodeProject.Contains(search) ||
                p.SerialNumber.Contains(search) ||
                p.ProjectReceiver.Contains(search) ||
                (showFactor && p.FactorNumber != null && p.FactorNumber.Contains(search)) ||
                (p.Description != null && p.Description.Contains(search)));

        if (q.KarfarmaId is > 0) query = query.Where(p => p.KarFarmaId == q.KarfarmaId);
        if (q.TypeFactorId is > 0) query = query.Where(p => p.FactorTypeId == q.TypeFactorId);
        if (q.UserId is > 0) query = query.Where(p => p.UserId == q.UserId);
        if (q.Returned == true) query = query.Where(p => p.ReturnProjectId > 0);

        var fCode = NormFilter(q.FCode);
        if (fCode is not null) query = query.Where(p => p.CodeProject.Contains(fCode));

        var fName = NormFilter(q.FName);
        if (fName is not null) query = query.Where(p => p.ProjectName.Contains(fName));

        var fSerial = NormFilter(q.FSerial);
        if (fSerial is not null) query = query.Where(p => p.SerialNumber.Contains(fSerial));
        // «ندارد» بر «دارد» اولویت دارد؛ اگر هیچ‌کدام انتخاب نشده باشد («همه») شرطی اعمال نمی‌شود
        if (q.EmptySerial) query = query.Where(p => p.SerialNumber == null || p.SerialNumber == "");
        else if (q.HasSerial) query = query.Where(p => p.SerialNumber != null && p.SerialNumber != "");

        var fKarfarma = NormFilter(q.FKarfarma);
        if (fKarfarma is not null) query = query.Where(p => p.KarFarma != null && p.KarFarma.Name.Contains(fKarfarma));

        if (showFactor)
        {
            // «-» در فیلتر شماره فاکتور = فقط پروژه‌هایی که هنوز فاکتور ندارند
            if (IsEmptyFilter(q.FFactor)) query = query.Where(p => p.FactorNumber == null || p.FactorNumber == "");
            else
            {
                var fFactor = NormFilter(q.FFactor);
                if (fFactor is not null) query = query.Where(p => p.FactorNumber != null && p.FactorNumber.Contains(fFactor));
            }

            // «-» در فیلتر نوع فاکتور = فقط پروژه‌هایی که نوع فاکتور برایشان تعیین نشده
            if (IsEmptyFilter(q.FFactorType)) query = query.Where(p => p.FactorTypeId == null);
            else
            {
                var fFactorType = NormFilter(q.FFactorType);
                if (fFactorType is not null) query = query.Where(p => p.TypeFactor != null && p.TypeFactor.Name.Contains(fFactorType));
            }
        }

        if (q.EmptyFactor) query = query.Where(p => p.FactorNumber == null || p.FactorNumber == "");
        else if (q.HasFactor) query = query.Where(p => p.FactorNumber != null && p.FactorNumber != "");
        if (q.EmptyFactorType) query = query.Where(p => p.FactorTypeId == null);
        else if (q.HasFactorType) query = query.Where(p => p.FactorTypeId != null);

        // «-» در فیلتر کارشناسی اولیه = فقط پروژه‌هایی که کارشناسی ثبت نشده دارد
        if (IsEmptyFilter(q.FKarshenasi))
        {
            query = query.Where(p => p.KarshenasiAvalie == null || p.KarshenasiAvalie == "");
        }
        else
        {
            var fKarshenasi = NormFilter(q.FKarshenasi);
            if (fKarshenasi is not null) query = query.Where(p => p.KarshenasiAvalie != null && p.KarshenasiAvalie.Contains(fKarshenasi));
        }
        if (q.EmptyKarshenasi) query = query.Where(p => p.KarshenasiAvalie == null || p.KarshenasiAvalie == "");
        else if (q.HasKarshenasi) query = query.Where(p => p.KarshenasiAvalie != null && p.KarshenasiAvalie != "");

        // ----- تاریخ‌های اختیاری: تایپ «-» یعنی فقط پروژه‌هایی که این تاریخ را ندارند -----
        // در فیلتر سرستون‌های تاریخ، تایپ «-» (یا خالی/ندارد/null) یعنی «این تاریخ را ندارد»
        if (q.EmptyEntry || IsEmptyFilter(q.FEntry))
        {
            query = query.Where(p => p.EntryDate == null);
        }
        else
        {
            if (q.HasEntry) query = query.Where(p => p.EntryDate != null);
            var rEntry = JalaliRange(q.FEntry);
            if (rEntry is not null)
            {
                var f = rEntry.Value.From; var t = rEntry.Value.To;
                query = query.Where(p => p.EntryDate >= f && p.EntryDate < t);
            }
        }

        if (q.EmptyExit || IsEmptyFilter(q.FExit))
        {
            query = query.Where(p => p.ExitDate == null);
        }
        else
        {
            if (q.HasExit) query = query.Where(p => p.ExitDate != null);
            var rExit = JalaliRange(q.FExit);
            if (rExit is not null)
            {
                var f = rExit.Value.From; var t = rExit.Value.To;
                query = query.Where(p => p.ExitDate >= f && p.ExitDate < t);
            }
        }

        if (!q.EmptyEntry && !IsEmptyFilter(q.FEntry) && (q.EntryFrom.HasValue || q.EntryTo.HasValue))
        {
            if (q.EntryFrom.HasValue) query = query.Where(p => p.EntryDate >= q.EntryFrom.Value.Date);
            if (q.EntryTo.HasValue) query = query.Where(p => p.EntryDate < q.EntryTo.Value.Date.AddDays(1));
        }
        if (!q.EmptyExit && !IsEmptyFilter(q.FExit) && (q.ExitFrom.HasValue || q.ExitTo.HasValue))
        {
            if (q.ExitFrom.HasValue) query = query.Where(p => p.ExitDate >= q.ExitFrom.Value.Date);
            if (q.ExitTo.HasValue) query = query.Where(p => p.ExitDate < q.ExitTo.Value.Date.AddDays(1));
        }

        if (q.EmptyTemporaryExit)
        {
            query = query.Where(p => p.TemporaryExitDate == null);
        }
        else if (q.HasTemporaryExit || q.TemporaryExitFrom.HasValue || q.TemporaryExitTo.HasValue)
        {
            if (q.HasTemporaryExit) query = query.Where(p => p.TemporaryExitDate != null);
            if (q.TemporaryExitFrom.HasValue) query = query.Where(p => p.TemporaryExitDate >= q.TemporaryExitFrom.Value.Date);
            if (q.TemporaryExitTo.HasValue) query = query.Where(p => p.TemporaryExitDate < q.TemporaryExitTo.Value.Date.AddDays(1));
        }

        if (q.EmptyDelivery)
        {
            query = query.Where(p => p.DeliveryDate == null);
        }
        else if (q.HasDelivery || q.DeliveryFrom.HasValue || q.DeliveryTo.HasValue)
        {
            if (q.HasDelivery) query = query.Where(p => p.DeliveryDate != null);
            if (q.DeliveryFrom.HasValue) query = query.Where(p => p.DeliveryDate >= q.DeliveryFrom.Value.Date);
            if (q.DeliveryTo.HasValue) query = query.Where(p => p.DeliveryDate < q.DeliveryTo.Value.Date.AddDays(1));
        }

        if (q.EmptyFile)
        {
            query = query.Where(p => p.FileDate == null);
        }
        else if (q.HasFile || q.FileFrom.HasValue || q.FileTo.HasValue)
        {
            if (q.HasFile) query = query.Where(p => p.FileDate != null);
            if (q.FileFrom.HasValue) query = query.Where(p => p.FileDate >= q.FileFrom.Value.Date);
            if (q.FileTo.HasValue) query = query.Where(p => p.FileDate < q.FileTo.Value.Date.AddDays(1));
        }

        if (q.EmptyRegistration || IsEmptyFilter(q.FSabt))
        {
            query = query.Where(p => p.ProjectRegistrationDate == null);
        }
        else
        {
            if (q.HasRegistration) query = query.Where(p => p.ProjectRegistrationDate != null);
            var rSabt = JalaliRange(q.FSabt);
            if (rSabt is not null)
            {
                var f = rSabt.Value.From; var t = rSabt.Value.To;
                query = query.Where(p => p.ProjectRegistrationDate >= f && p.ProjectRegistrationDate < t);
            }
        }

        if (q.EmptyCustomerNeed || IsEmptyFilter(q.FNeed))
        {
            query = query.Where(p => p.CustomerRequiredDate == null);
        }
        else
        {
            if (q.HasCustomerNeed) query = query.Where(p => p.CustomerRequiredDate != null);
            var rNeed = JalaliRange(q.FNeed);
            if (rNeed is not null)
            {
                var f = rNeed.Value.From; var t = rNeed.Value.To;
                query = query.Where(p => p.CustomerRequiredDate >= f && p.CustomerRequiredDate < t);
            }
        }

        if (!q.EmptyRegistration && !IsEmptyFilter(q.FSabt) && (q.RegistrationFrom.HasValue || q.RegistrationTo.HasValue))
        {
            if (q.RegistrationFrom.HasValue) query = query.Where(p => p.ProjectRegistrationDate >= q.RegistrationFrom.Value.Date);
            if (q.RegistrationTo.HasValue) query = query.Where(p => p.ProjectRegistrationDate < q.RegistrationTo.Value.Date.AddDays(1));
        }
        if (!q.EmptyCustomerNeed && !IsEmptyFilter(q.FNeed) && (q.CustomerNeedFrom.HasValue || q.CustomerNeedTo.HasValue))
        {
            if (q.CustomerNeedFrom.HasValue) query = query.Where(p => p.CustomerRequiredDate >= q.CustomerNeedFrom.Value.Date);
            if (q.CustomerNeedTo.HasValue) query = query.Where(p => p.CustomerRequiredDate < q.CustomerNeedTo.Value.Date.AddDays(1));
        }

        var rSpent = SpanRange(q.FSpent);
        if (rSpent is not null)
        {
            var f = rSpent.Value.From; var t = rSpent.Value.To;
            query = query.Where(p => p.TotalSpentTime >= f && p.TotalSpentTime < t);
        }

        if (q.FFolder == "y") query = query.Where(p => p.IsFolder == true);
        else if (q.FFolder == "n") query = query.Where(p => p.IsFolder != true);

        if (int.TryParse(q.FStatus, out var status)) query = query.Where(p => p.FlowStatus == status);

        return query;
    }

    /// <summary>مرتب‌سازی سمت سرور بر اساس کلید سرستون</summary>
    private static IQueryable<ProjectEntryExit> ApplySort(IQueryable<ProjectEntryExit> query, string? sort, bool desc)
        => (sort ?? "id") switch
        {
            // کدها و سریال‌های عددی: اول بر اساس طول تا ترتیب عددی حفظ شود
            "code" => desc ? query.OrderByDescending(p => p.CodeProject.Length).ThenByDescending(p => p.CodeProject)
                           : query.OrderBy(p => p.CodeProject.Length).ThenBy(p => p.CodeProject),
            "serial" => desc ? query.OrderByDescending(p => p.SerialNumber.Length).ThenByDescending(p => p.SerialNumber)
                             : query.OrderBy(p => p.SerialNumber.Length).ThenBy(p => p.SerialNumber),
            "name" => desc ? query.OrderByDescending(p => p.ProjectName) : query.OrderBy(p => p.ProjectName),
            "karfarma" => desc ? query.OrderByDescending(p => p.KarFarma!.Name) : query.OrderBy(p => p.KarFarma!.Name),
            "entry" => desc ? query.OrderByDescending(p => p.EntryDate) : query.OrderBy(p => p.EntryDate),
            "exit" => desc ? query.OrderByDescending(p => p.ExitDate) : query.OrderBy(p => p.ExitDate),
            "factor" => desc ? query.OrderByDescending(p => p.FactorNumber) : query.OrderBy(p => p.FactorNumber),
            "factortype" => desc ? query.OrderByDescending(p => p.TypeFactor!.Name) : query.OrderBy(p => p.TypeFactor!.Name),
            "karshenasi" => desc ? query.OrderByDescending(p => p.KarshenasiAvalie) : query.OrderBy(p => p.KarshenasiAvalie),
            "sabt" => desc ? query.OrderByDescending(p => p.ProjectRegistrationDate) : query.OrderBy(p => p.ProjectRegistrationDate),
            "need" => desc ? query.OrderByDescending(p => p.CustomerRequiredDate) : query.OrderBy(p => p.CustomerRequiredDate),
            "spent" => desc ? query.OrderByDescending(p => p.TotalSpentTime) : query.OrderBy(p => p.TotalSpentTime),
            "folder" => desc ? query.OrderByDescending(p => p.IsFolder) : query.OrderBy(p => p.IsFolder),
            "status" => desc ? query.OrderByDescending(p => p.FlowStatus) : query.OrderBy(p => p.FlowStatus),
            _ => desc ? query.OrderByDescending(p => p.Id) : query.OrderBy(p => p.Id)
        };

    /// <summary>واکشی کامل ردیف‌های یک صفحه (با جوین‌ها) و حفظ ترتیب شناسه‌ها</summary>
    private async Task<List<ProjectEntryExit>> LoadRowsAsync(List<int> ids)
    {
        if (ids.Count == 0) return new List<ProjectEntryExit>();
        var rows = await Db.ProjectEntryExits.AsNoTracking()
            .Include(p => p.KarFarma)
            .Include(p => p.TypeFactor)
            .Include(p => p.User)
            .Include(p => p.Attaches.Where(a => !a.IsDelete))
            .Include(p => p.ReportWorks.Where(r => !r.IsDelete))
            .AsSplitQuery()
            .Where(p => ids.Contains(p.Id))
            .ToListAsync();
        var map = rows.ToDictionary(p => p.Id);
        return ids.Where(map.ContainsKey).Select(id => map[id]).ToList();
    }

    /// <summary>
    /// لیست ورود/خروج پروژه‌ها — فیلتر، مرتب‌سازی و صفحه‌بندی کاملاً سمت سرور.
    /// خروجی: فقط ردیف‌های همان صفحه + تعداد کل (PagedResult).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] ProjectListQuery q, [FromQuery] int? skip = null, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;

        // رویت ستون‌های فاکتور فقط با مجوز Projects.ViewFactor
        var showFactor = await HasAsync(Module, "ViewFactor");

        var filtered = ApplyFilters(Db.ProjectEntryExits.AsNoTracking().Where(p => !p.IsDelete), q, showFactor);
        var total = await filtered.CountAsync();

        var page = q.Page < 1 ? 1 : q.Page;
        var pageSize = q.PageSize;
        // ورودی یکپارچه skip/take — بر page/pageSize اولویت دارد
        page = Paging.ToPage(skip, take, page);
        pageSize = Paging.ToPageSize(take, pageSize);
        var idQuery = ApplySort(filtered, q.Sort, q.Desc).Select(p => p.Id);
        if (pageSize > 0)
        {
            var pageCount = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
            if (page > pageCount) page = pageCount;
            idQuery = idQuery.Skip((page - 1) * pageSize).Take(pageSize);
        }

        var rows = await LoadRowsAsync(await idQuery.ToListAsync());
        return Ok(new PagedResult<ProjectEntryExitDto>
        {
            Items = rows.Select(p => ToDto(p, showFactor)).ToList(),
            Total = total,
            Page = page,
            PageSize = pageSize > 0 ? pageSize : Math.Max(total, 1)
        });
    }

    /// <summary>کدهای برگشتی ثبت‌شدهٔ یک پروژه مبدأ + اطلاعات پایهٔ آن (برای فرم پروژه برگشتی)</summary>
    [HttpGet("{id:int}/returns")]
    public async Task<IActionResult> Returns(int id, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;

        var parent = await Db.ProjectEntryExits.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id && !p.IsDelete);
        if (parent is null) return NotFound(new { message = "پروژه پیدا نشد." });

        var suffix = "-" + parent.CodeProject;
        var pagination = new Paging.Request(skip, take);
        var codes = await Db.ProjectEntryExits.AsNoTracking()
            .Where(p => !p.IsDelete && p.ReturnProjectId > 0 && p.CodeProject.EndsWith(suffix))
            .OrderBy(p => p.ReturnProjectId).ThenBy(p => p.Id)
            .Select(p => p.CodeProject)
            .ToPageListAsync(pagination);

        if (Paging.Requested(skip, take))
            return Ok(new
            {
                parentCode = parent.CodeProject,
                parentName = parent.ProjectName,
                parentReceiver = parent.ProjectReceiver,
                karFarmaId = parent.KarFarmaId,
                total = pagination.Total,
                codes
            });
        return Ok(new
        {
            parentCode = parent.CodeProject,
            parentName = parent.ProjectName,
            parentReceiver = parent.ProjectReceiver,
            karFarmaId = parent.KarFarmaId,
            codes
        });
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        var p = await Db.ProjectEntryExits.AsNoTracking()
            .Include(x => x.KarFarma)
            .Include(x => x.TypeFactor)
            .Include(x => x.User)
            .Include(x => x.Attaches.Where(a => !a.IsDelete))
            .Include(x => x.ReportWorks.Where(r => !r.IsDelete))
            .AsSplitQuery()
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDelete);
        if (p is null) return NotFound(new { message = "پروژه پیدا نشد." });
        var showFactor = await HasAsync(Module, "ViewFactor");
        return Ok(ToDto(p, showFactor));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] ProjectEntryExitDto dto, [FromServices] Hubs.INotifyService notify)
    {
        if (await ForbiddenUnlessAsync(Module, "Create") is { } forbid) return forbid;
        var err = await ValidateAsync(dto);
        if (err is not null) return err;

        var entity = new ProjectEntryExit();
        Map(dto, entity);

        // ==================== کد پروژه (خودکار) ====================
        // برگشتی: عدد برگشتی (دستی کاربر) + کد پروژه مبدأ → «REn-کد مبدأ»
        if (dto.ReturnProjectId > 0 || dto.ReturnOfProjectId > 0)
        {
            if (dto.ReturnProjectId <= 0)
                return BadRequest(new { message = "عدد برگشتی را وارد کنید (حداقل ۱)." });
            if (dto.ReturnProjectId > 9999)
                return BadRequest(new { message = "عدد برگشتی معتبر نیست (حداکثر ۹۹۹۹)." });
            if (dto.ReturnOfProjectId <= 0)
                return BadRequest(new { message = "پروژه مبدأ (برگشتی) را انتخاب کنید." });

            var parent = await Db.ProjectEntryExits.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == dto.ReturnOfProjectId && !x.IsDelete);
            if (parent is null)
                return BadRequest(new { message = "پروژه مبدأ (برگشتی) پیدا نشد." });

            var code = $"RE{dto.ReturnProjectId}-{parent.CodeProject}";
            if (await Db.ProjectEntryExits.AnyAsync(x => !x.IsDelete && x.CodeProject == code))
                return BadRequest(new { message = $"کد پروژه «{code}» قبلاً ثبت شده است. عدد برگشتی دیگری وارد کنید." });

            entity.CodeProject = code;
            entity.ReturnProjectId = dto.ReturnProjectId;
        }
        else
        {
            entity.CodeProject = await NextCodeAsync();
            entity.ReturnProjectId = 0;
        }

        // اطلاعات فاکتور فقط از فرم مجزای فاکتور ثبت می‌شود (بعداً تکمیل می‌گردد)
        entity.FactorTypeId = null;
        entity.FactorNumber = null;
        // کاربر ثبت‌کننده همیشه کاربر لاگین فعلی است (تو بک‌اند — اعتماد به کلاینت نمی‌کنیم)
        entity.UserId = MyUserId;
        entity.CreatedAt = DateTime.Now;
        entity.TotalSpentTime = TimeSpan.Zero;
        // گردش‌کار: هر پروژهٔ تازه‌ثبت‌شده اول به کارتابل مدیر می‌رود (درخواست کاربر)
        entity.FlowStatus = 0;

        Db.ProjectEntryExits.Add(entity);
        await Db.SaveChangesAsync();

        // زنگ اعلان برای مدیران کارتابل + اعلام زنده (SignalR) برای رفرش لیست‌ها
        await NotifyProjectEventAsync(notify, "Manager",
            "پروژه جدید ثبت شد",
            $"«{entity.ProjectName}» (کد {entity.CodeProject}) ثبت شد و در کارتابل مدیر قرار گرفت.",
            $"/project-cartable?queue=manager");

        return Ok(new { id = entity.Id, codeProject = entity.CodeProject });
    }

    /// <summary>اعلان زنگ به کاربران دارای مجوزِ اکشنِ کارتابل + پخش بلادرنگ «projects» برای رفرش لیست‌ها (SignalR)</summary>
    private async Task NotifyProjectEventAsync(Hubs.INotifyService? notify, string cartableAction, string title, string body, string link)
    {
        try
        {
            if (notify is not null)
            {
                var targets = await UsersWithPermissionAsync("ProjectCartable", cartableAction, excludeSelf: true);
                await notify.SendManyAsync(targets, title, body, DisplayNameOf(MyUserId), "مدیریت پروژه‌ها", link);
                await notify.BroadcastChangedAsync("projects");
            }
        }
        catch { /* اعلان نباید مانع عملیات اصلی شود */ }
    }

    /// <summary>نام نمایشی ثبت‌کننده (از دیتابیس)</summary>
    private string DisplayNameOf(int userId)
    {
        var u = Db.Users.Find(userId);
        if (u is null) return MyUsername;
        var full = $"{u.FirstName} {u.LastName}".Trim();
        return string.IsNullOrWhiteSpace(full) ? u.Username : full;
    }

    /// <summary>
    /// ویرایش پروژه.
    /// اگر کاربر خودش «مدیر کارتابل پروژه» (ProjectCartable.Manager) باشد تغییر مستقیم اعمال می‌شود؛
    /// در غیر این صورت یک «درخواست ویرایش» ساخته می‌شود و تا تایید مدیر هیچ تغییری روی پروژه اعمال نمی‌گردد.
    /// </summary>
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] ProjectEntryExitDto dto, [FromServices] Hubs.INotifyService notify)
    {
        if (await ForbiddenUnlessAsync(Module, "Update") is { } forbid) return forbid;
        var err = await ValidateAsync(dto, id);
        if (err is not null) return err;

        var entity = await Db.ProjectEntryExits.FirstOrDefaultAsync(p => p.Id == id && !p.IsDelete);
        if (entity is null) return NotFound(new { message = "پروژه پیدا نشد." });

        // ==================== نیاز به تایید مدیر ====================
        if (!await IsProjectManagerAsync())
        {
            var summary = BuildEditSummary(entity, dto);
            if (string.IsNullOrWhiteSpace(summary))
                return Ok(new { id = entity.Id, pending = false, message = "تغییری برای ثبت وجود نداشت." });

            if (await Db.ProjectChangeRequests.AnyAsync(c => c.ProjectId == id && c.Kind == 1 && c.Status == 0))
                return BadRequest(new { message = "برای این پروژه یک «درخواست ویرایش» در انتظار تایید مدیر وجود دارد؛ تا تعیین تکلیف آن، ویرایش جدید ممکن نیست." });

            Db.ProjectChangeRequests.Add(new ProjectChangeRequest
            {
                ProjectId = id,
                Kind = 1,
                Status = 0,
                PayloadJson = System.Text.Json.JsonSerializer.Serialize(dto),
                Summary = summary,
                RequestedById = MyUserId,
                RequestedAt = DateTime.Now
            });
            await Db.SaveChangesAsync();

            await NotifyProjectEventAsync(notify, "Manager",
                "درخواست ویرایش پروژه",
                $"«{entity.ProjectName}» (کد {entity.CodeProject}) — درخواست ویرایش در انتظار تایید شماست.",
                "/project-cartable?queue=changes");

            return Ok(new
            {
                id = entity.Id,
                pending = true,
                message = "درخواست ویرایش برای تایید مدیر ارسال شد — پس از تایید مدیر اعمال می‌شود."
            });
        }

        var originUserId = entity.UserId; // ثبت‌کننده اصلی حفظ می‌شود
        Map(dto, entity); // Map اطلاعات فاکتور و کد پروژه/عدد برگشتی را دست نمی‌زند
        entity.UserId = originUserId;
        await Db.SaveChangesAsync();
        try { await notify.BroadcastChangedAsync("projects"); } catch { }
        return Ok(new { id = entity.Id, pending = false });
    }

    /// <summary>آیا کاربر جاری خودش مدیر کارتابل پروژه است؟ (تغییرات او بدون انتظار اعمال می‌شود)</summary>
    private Task<bool> IsProjectManagerAsync() => HasAsync("ProjectCartable", "Manager");

    /// <summary>خلاصهٔ خوانای تفاوت مقادیر فعلی پروژه با مقادیر درخواستی (برای نمایش به مدیر)</summary>
    internal static string BuildEditSummary(ProjectEntryExit e, ProjectEntryExitDto d)
    {
        var lines = new List<string>();
        void Cmp(string title, string? oldV, string? newV)
        {
            var o = (oldV ?? "").Trim();
            var n = (newV ?? "").Trim();
            if (o != n) lines.Add($"{title}: «{(o.Length == 0 ? "—" : o)}» ← «{(n.Length == 0 ? "—" : n)}»");
        }
        void CmpDate(string title, DateTime? oldV, DateTime? newV)
            => Cmp(title,
                oldV is null ? null : Inventory.Shared.PersianDate.ToShort(oldV.Value),
                newV is null ? null : Inventory.Shared.PersianDate.ToShort(newV.Value));

        Cmp("نام پروژه", e.ProjectName, d.ProjectName);
        Cmp("شماره سریال", e.SerialNumber, d.SerialNumber);
        Cmp("تحویل گیرنده", e.ProjectReceiver, d.ProjectReceiver);
        Cmp("قبض خروج", e.GhabzExit, d.GhabzExit);
        Cmp("کارشناسی اولیه", e.KarshenasiAvalie, d.KarshenasiAvalie);
        Cmp("شرح", e.Description, d.Description);
        if (e.KarFarmaId != d.KarFarmaId) lines.Add("کارفرما تغییر کرده است.");
        if ((e.IsFolder == true) != (d.IsFolder == true))
            lines.Add($"پوشه: «{(e.IsFolder == true ? "دارد" : "ندارد")}» ← «{(d.IsFolder == true ? "دارد" : "ندارد")}»");
        CmpDate("تاریخ ورود", e.EntryDate, d.EntryDate);
        CmpDate("تاریخ خروج", e.ExitDate, d.ExitDate);
        CmpDate("تاریخ خروج موقت", e.TemporaryExitDate, d.TemporaryExitDate);
        CmpDate("تاریخ ثبت پروژه", e.ProjectRegistrationDate, d.ProjectRegistrationDate);
        CmpDate("تاریخ نیاز مشتری", e.CustomerRequiredDate, d.CustomerRequiredDate);
        CmpDate("تاریخ تحویل", e.DeliveryDate, d.DeliveryDate);
        CmpDate("تاریخ پرونده", e.FileDate, d.FileDate);

        var text = string.Join(" | ", lines);
        return text.Length > 1900 ? text.Substring(0, 1900) + "…" : text;
    }

    /// <summary>ثبت/ویرایش اطلاعات فاکتور پروژه (شماره + نوع فاکتور) — فرم مجزا از منوی سطر</summary>
    [HttpPut("{id:int}/factor")]
    public async Task<IActionResult> UpdateFactor(int id, [FromBody] ProjectFactorDto dto)
    {
        if (await ForbiddenUnlessAsync(Module, "Update") is { } forbid) return forbid;

        var entity = await Db.ProjectEntryExits.FirstOrDefaultAsync(p => p.Id == id && !p.IsDelete);
        if (entity is null) return NotFound(new { message = "پروژه پیدا نشد." });

        if (dto.FactorTypeId <= 0 || !await Db.TypeFactors.AnyAsync(t => t.Id == dto.FactorTypeId && !t.IsDelete))
            return BadRequest(new { message = "نوع فاکتور انتخاب نشده یا معتبر نیست." });

        entity.FactorTypeId = dto.FactorTypeId;
        var fn = dto.FactorNumber?.Trim();
        entity.FactorNumber = string.IsNullOrWhiteSpace(fn) ? null : fn;
        await Db.SaveChangesAsync();
        return Ok(new { id = entity.Id });
    }

    /// <summary>
    /// ثبت/ویرایش تاریخ‌های چرخهٔ پروژه (خروج، خروج موقت، نیاز مشتری، تحویل پروژه، تحویل پرونده)
    /// — فرم مجزا از منوی سطر، دقیقاً مثل فرم فاکتور. هر فیلد خالی یعنی «ثبت نشده» و پاک می‌شود.
    /// </summary>
    [HttpPut("{id:int}/dates")]
    public async Task<IActionResult> UpdateDates(int id, [FromBody] ProjectDatesDto dto, [FromServices] Hubs.INotifyService notify)
    {
        var canUpdateAllDates = await HasAsync(Module, "Update");
        var canUpdateExitDate = await HasAsync(Module, "UpdateExitDate");
        if (!canUpdateAllDates && !canUpdateExitDate)
            return StatusCode(403, new { message = "مجوز ثبت تاریخ‌های پروژه را ندارید." });

        var entity = await Db.ProjectEntryExits.FirstOrDefaultAsync(p => p.Id == id && !p.IsDelete);
        if (entity is null) return NotFound(new { message = "پروژه پیدا نشد." });

        // کاربر دارای UpdateExitDate فقط مجاز به تغییر/پاک‌کردن تاریخ خروج است.
        if (!canUpdateAllDates &&
            (dto.TemporaryExitDate != entity.TemporaryExitDate ||
             dto.CustomerRequiredDate != entity.CustomerRequiredDate ||
             dto.DeliveryDate != entity.DeliveryDate ||
             dto.FileDate != entity.FileDate))
            return StatusCode(403, new { message = "این کاربر فقط مجاز به ثبت تاریخ خروج است." });

        // ---------- اعتبارسنجی منطقی ترتیب تاریخ‌ها (پیام‌های فارسی) ----------
        var entry = entity.EntryDate;
        if (entry is not null)
        {
            var entryFa = Inventory.Shared.PersianDate.ToShortFa(entry.Value);
            if (dto.ExitDate is not null && dto.ExitDate < entry)
                return BadRequest(new { message = $"تاریخ خروج نمی‌تواند قبل از تاریخ ورود ({entryFa}) باشد." });
            if (dto.TemporaryExitDate is not null && dto.TemporaryExitDate < entry)
                return BadRequest(new { message = $"تاریخ خروج موقت نمی‌تواند قبل از تاریخ ورود ({entryFa}) باشد." });
            if (dto.DeliveryDate is not null && dto.DeliveryDate < entry)
                return BadRequest(new { message = $"تاریخ تحویل پروژه نمی‌تواند قبل از تاریخ ورود ({entryFa}) باشد." });
            if (dto.FileDate is not null && dto.FileDate < entry)
                return BadRequest(new { message = $"تاریخ تحویل پرونده نمی‌تواند قبل از تاریخ ورود ({entryFa}) باشد." });
        }

        entity.ExitDate = dto.ExitDate?.Date;
        entity.TemporaryExitDate = dto.TemporaryExitDate?.Date;
        entity.CustomerRequiredDate = dto.CustomerRequiredDate?.Date;
        entity.DeliveryDate = dto.DeliveryDate?.Date;
        entity.FileDate = dto.FileDate?.Date;

        await Db.SaveChangesAsync();
        try { await notify.BroadcastChangedAsync("projects"); } catch { }
        return Ok(new { id = entity.Id });
    }

    /// <summary>
    /// حذف پروژه.
    /// مدیر کارتابل پروژه: حذف نرم فوری (به‌همراه گزارش‌های کار و پیوست‌ها).
    /// سایر کاربران: «درخواست حذف» ساخته می‌شود و فقط با تایید مدیر انجام می‌گیرد.
    /// </summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, [FromServices] Services.IProjectFileProtection protect, [FromServices] Hubs.INotifyService notify)
    {
        if (await ForbiddenUnlessAsync(Module, "Delete") is { } forbid) return forbid;

        var entity = await Db.ProjectEntryExits
            .Include(p => p.Attaches.Where(a => !a.IsDelete))
            .FirstOrDefaultAsync(p => p.Id == id && !p.IsDelete);
        if (entity is null) return NotFound(new { message = "پروژه پیدا نشد." });

        // ==================== نیاز به تایید مدیر ====================
        if (!await IsProjectManagerAsync())
        {
            if (await Db.ProjectChangeRequests.AnyAsync(c => c.ProjectId == id && c.Kind == 2 && c.Status == 0))
                return BadRequest(new { message = "برای این پروژه یک «درخواست حذف» در انتظار تایید مدیر وجود دارد." });

            Db.ProjectChangeRequests.Add(new ProjectChangeRequest
            {
                ProjectId = id,
                Kind = 2,
                Status = 0,
                Summary = $"حذف پروژهٔ «{entity.ProjectName}» (کد {entity.CodeProject}) به‌همراه گزارش‌های کار و پیوست‌های آن",
                RequestedById = MyUserId,
                RequestedAt = DateTime.Now
            });
            await Db.SaveChangesAsync();

            await NotifyProjectEventAsync(notify, "Manager",
                "درخواست حذف پروژه",
                $"«{entity.ProjectName}» (کد {entity.CodeProject}) — درخواست حذف در انتظار تایید شماست.",
                "/project-cartable?queue=changes");

            return Ok(new
            {
                ok = true,
                pending = true,
                message = "درخواست حذف برای تایید مدیر ارسال شد — پروژه تا تایید مدیر حذف نمی‌شود."
            });
        }

        await ApplyDeleteAsync(Db, entity, protect);
        try { await notify.BroadcastChangedAsync("projects"); } catch { }
        return Ok(new { ok = true, pending = false });
    }

    /// <summary>حذف نرم پروژه + گزارش‌های کار + پیوست‌ها (مشترک بین حذف مستقیم مدیر و تایید درخواست حذف)</summary>
    internal static async Task ApplyDeleteAsync(AppDbContext db, ProjectEntryExit entity, Services.IProjectFileProtection protect)
    {
        entity.IsDelete = true;

        var reports = await db.ReportWorks.Where(r => r.ProjectId == entity.Id && !r.IsDelete).ToListAsync();
        foreach (var r in reports) r.IsDelete = true;

        var attaches = await db.ProjectAttaches.Where(a => a.ProjectId == entity.Id && !a.IsDelete).ToListAsync();
        foreach (var a in attaches)
        {
            a.IsDelete = true;
            try { protect.Delete(a.StoredFileName); } catch { /* فایل شاید از قبل نباشد */ }
        }

        await db.SaveChangesAsync();
    }

    // ==================== کمکی ====================

    /// <summary>
    /// اعتبارسنجی پروژه — «شماره سریال» و «تحویل گیرنده» اختیاری هستند (اجبار برداشته شد)؛
    /// فقط اگر سریال وارد شده باشد، تکراری‌نبودن آن بررسی می‌شود.
    /// </summary>
    private async Task<IActionResult?> ValidateAsync(ProjectEntryExitDto dto, int excludeId = 0)
    {
        if (string.IsNullOrWhiteSpace(dto.ProjectName))
            return BadRequest(new { message = "نام پروژه الزامی است." });

        var serial = (dto.SerialNumber ?? "").Trim();
        if (serial.Length > 0)
        {
            var dup = await Db.ProjectEntryExits.AnyAsync(p => p.Id != excludeId && !p.IsDelete && p.SerialNumber == serial);
            if (dup)
                return BadRequest(new { message = "این شماره سریال قبلاً برای پروژه دیگری ثبت شده است." });
        }

        if (!await Db.KarFarmas.AnyAsync(k => k.Id == dto.KarFarmaId && !k.IsDelete))
            return BadRequest(new { message = "کارفرما انتخاب نشده یا معتبر نیست." });
        return null;
    }

    internal static void Map(ProjectEntryExitDto dto, ProjectEntryExit e)
    {
        e.SerialNumber = (dto.SerialNumber ?? "").Trim();
        e.ProjectName = (dto.ProjectName ?? "").Trim();
        // نکته: CodeProject و ReturnProjectId اینجا نیستند — کد فقط موقع ایجاد صادر می‌شود و بعداً تغییر نمی‌کند
        e.GhabzExit = dto.GhabzExit?.Trim();
        // نکته: FactorNumber و FactorTypeId عمداً اینجا نیستند — فقط از UpdateFactor تغییر می‌کنند
        e.KarshenasiAvalie = dto.KarshenasiAvalie?.Trim();
        e.ProjectReceiver = (dto.ProjectReceiver ?? "").Trim();
        e.Description = dto.Description?.Trim();
        e.KarFarmaId = dto.KarFarmaId;
        e.UserId = dto.UserId;
        e.ExitDate = dto.ExitDate;
        e.EntryDate = dto.EntryDate;
        e.FileDate = dto.FileDate;
        e.DeliveryDate = dto.DeliveryDate;
        e.TemporaryExitDate = dto.TemporaryExitDate;
        e.ProjectRegistrationDate = dto.ProjectRegistrationDate;
        e.CustomerRequiredDate = dto.CustomerRequiredDate;
        e.IsFolder = dto.IsFolder;
    }

    /// <param name="showFactor">رویت شماره/نوع فاکتور (مجوز Projects.ViewFactor)</param>
    private static ProjectEntryExitDto ToDto(ProjectEntryExit p, bool showFactor) => new()
    {
        Id = p.Id,
        CodeProject = p.CodeProject,
        ReturnProjectId = p.ReturnProjectId,
        SerialNumber = p.SerialNumber,
        ProjectName = p.ProjectName,
        GhabzExit = p.GhabzExit,
        KarshenasiAvalie = p.KarshenasiAvalie,
        ProjectReceiver = p.ProjectReceiver,
        Description = p.Description,
        KarFarmaId = p.KarFarmaId,
        UserId = p.UserId,
        ExitDate = p.ExitDate,
        EntryDate = p.EntryDate,
        FileDate = p.FileDate,
        DeliveryDate = p.DeliveryDate,
        TemporaryExitDate = p.TemporaryExitDate,
        ProjectRegistrationDate = p.ProjectRegistrationDate,
        CustomerRequiredDate = p.CustomerRequiredDate,
        IsFolder = p.IsFolder,
        TotalSpentTime = p.TotalSpentTime,
        KarFarmaName = p.KarFarma?.Name,
        UserName = p.User is null ? null : DisplayOf(p.User),
        AttachCount = p.Attaches.Count,
        ReportWorkCount = p.ReportWorks.Count,
        // فیلدهای فاکتور فقط با مجوز رویت فاکتور برمی‌گردند
        FactorNumber = showFactor ? p.FactorNumber : null,
        FactorTypeId = showFactor ? p.FactorTypeId : null,
        FactorTypeName = showFactor ? p.TypeFactor?.Name : null,
        // گردش‌کار کارتابل
        FlowStatus = p.FlowStatus,
        ManagerNote = p.ManagerNote,
        ExpertNote = p.ExpertNote,
        ManagerActionAt = p.ManagerActionAt,
        ExpertActionAt = p.ExpertActionAt
    };
}
