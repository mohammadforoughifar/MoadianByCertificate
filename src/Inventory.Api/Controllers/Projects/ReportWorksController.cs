using Inventory.Api.Data;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Paging = Inventory.Api.Services.Paging;

namespace Inventory.Api.Controllers;

/// <summary>گزارش‌های کار روی پروژه‌ها — ماژول دسترسی: ReportWorks</summary>
[Route("api/reportworks")]
public class ReportWorksController : RbacControllerBase
{
    private const string Module = "ReportWorks";

    public ReportWorksController(AppDbContext db) : base(db) { }

    private static string DisplayOf(User u)
    {
        var full = $"{u.FirstName} {u.LastName}".Trim();
        return string.IsNullOrWhiteSpace(full) ? u.Username : full;
    }

    /// <summary>زمان صرف‌شده خالص = (پایان − شروع) − (صبحانه + ناهار) — حداقل صفر</summary>
    public static TimeSpan CalcSpent(TimeOnly start, TimeOnly end, TimeOnly breakfast, TimeOnly lunch)
    {
        var total = end - start;
        if (total < TimeSpan.Zero) total += TimeSpan.FromDays(1); // شیفت شبانه
        var spent = total - breakfast.ToTimeSpan() - lunch.ToTimeSpan();
        return spent < TimeSpan.Zero ? TimeSpan.Zero : spent;
    }

    // =====================================================================
    //  فیلتر / مرتب‌سازی / صفحه‌بندی سمت سرور (لود سبک لیست گزارشات)
    // =====================================================================

    /// <summary>نرمال‌سازی متن فیلتر: ارقام فارسی/عربی → لاتین، ي/ك عربی → ی/ک فارسی</summary>
    private static string? NormFilter(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var t = Inventory.Shared.Fa.ToEn(s).Trim().Replace('\u064A', '\u06CC').Replace('\u0643', '\u06A9');
        return string.IsNullOrWhiteSpace(t) ? null : t;
    }

    /// <summary>متن (بخشی از) تاریخ شمسی → بازهٔ میلادی: «۱۴۰۵»، «۱۴۰۵/۰۶» یا «۱۴۰۵/۰۶/۲۵»</summary>
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

    /// <summary>
    /// متن ساعت («۸» یا «۸:۳۰») → بازهٔ دقیقه‌ای [از، تا).
    /// <para>
    /// <paramref name="maxHour"/> سقف ساعت مجاز است. برای ستون‌های «شروع/پایان» مقدار ۲۳ است،
    /// چون <c>TimeOnly.FromTimeSpan</c> برای ۲۴ ساعت و بیشتر استثنا می‌دهد و در نسخهٔ قبلی
    /// تایپ «۲۵» یا «۹۹» در کادر جستجو باعث خطای ۵۰۰ سرور (و پشت‌سرهم خطا در کلاینت) می‌شد.
    /// ورودی خارج از بازه نادیده گرفته می‌شود، نه اینکه درخواست را بشکند.
    /// </para>
    /// </summary>
    private static (int From, int To)? MinuteRange(string? text, int maxHour = 999)
    {
        var t = NormFilter(text);
        if (t is null) return null;
        var parts = t.Split(':');
        if (!int.TryParse(parts[0].Trim(), out var h) || h < 0 || h > maxHour) return null;
        if (parts.Length == 1 || string.IsNullOrWhiteSpace(parts[1]))
            return (h * 60, (h + 1) * 60);
        if (!int.TryParse(parts[1].Trim(), out var mi) || mi < 0 || mi > 59) return null;
        return (h * 60 + mi, h * 60 + mi + 1);
    }

    /// <summary>اعمال فیلترهای بالای صفحه + جستجوی سرستون‌ها</summary>
    private static IQueryable<ReportWork> ApplyFilters(IQueryable<ReportWork> query, ReportWorkListQuery q)
    {
        if (q.ProjectId is > 0) query = query.Where(r => r.ProjectId == q.ProjectId);
        if (q.UserId is > 0) query = query.Where(r => r.UserId == q.UserId);
        if (q.OperatorId is > 0) query = query.Where(r => r.OperatorId == q.OperatorId);
        if (q.From is not null)
        {
            var f = q.From.Value.Date;
            query = query.Where(r => r.ReportDate >= f);
        }
        if (q.To is not null)
        {
            var t = q.To.Value.Date.AddDays(1);
            query = query.Where(r => r.ReportDate < t);
        }

        var rDate = JalaliRange(q.FDate);
        if (rDate is not null)
        {
            var f = rDate.Value.From; var t = rDate.Value.To;
            query = query.Where(r => r.ReportDate >= f && r.ReportDate < t);
        }

        var fCode = NormFilter(q.FCode);
        if (fCode is not null) query = query.Where(r => r.CodeProject.Contains(fCode));

        var fOperator = NormFilter(q.FOperator);
        if (fOperator is not null)
            query = query.Where(r => r.Operator != null &&
                ((r.Operator.FirstName + " " + r.Operator.LastName).Contains(fOperator) || r.Operator.Username.Contains(fOperator)));

        var fProject = NormFilter(q.FProject);
        if (fProject is not null) query = query.Where(r => r.Project != null && r.Project.ProjectName.Contains(fProject));

        var fUser = NormFilter(q.FUser);
        if (fUser is not null)
            query = query.Where(r => r.User != null &&
                ((r.User.FirstName + " " + r.User.LastName).Contains(fUser) || r.User.Username.Contains(fUser)));

        var rStart = MinuteRange(q.FStart, maxHour: 23);
        if (rStart is not null)
        {
            var f = TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(rStart.Value.From));
            query = query.Where(r => r.StartTime >= f);
            if (rStart.Value.To < 24 * 60)
            {
                var t = TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(rStart.Value.To));
                query = query.Where(r => r.StartTime < t);
            }
        }
        var rEnd = MinuteRange(q.FEnd, maxHour: 23);
        if (rEnd is not null)
        {
            var f = TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(rEnd.Value.From));
            query = query.Where(r => r.EndTime >= f);
            if (rEnd.Value.To < 24 * 60)
            {
                var t = TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(rEnd.Value.To));
                query = query.Where(r => r.EndTime < t);
            }
        }
        var rRest = MinuteRange(q.FRest, maxHour: 48);
        if (rRest is not null)
        {
            var f = rRest.Value.From; var t = rRest.Value.To;
            query = query.Where(r =>
                (r.BreakfastTime.Hour * 60 + r.BreakfastTime.Minute + r.LunchTime.Hour * 60 + r.LunchTime.Minute) >= f &&
                (r.BreakfastTime.Hour * 60 + r.BreakfastTime.Minute + r.LunchTime.Hour * 60 + r.LunchTime.Minute) < t);
        }
        var rSpent = MinuteRange(q.FSpent, maxHour: 48);
        if (rSpent is not null)
        {
            var f = TimeSpan.FromMinutes(rSpent.Value.From);
            var t = TimeSpan.FromMinutes(rSpent.Value.To);
            query = query.Where(r => r.SpentTime >= f && r.SpentTime < t);
        }

        var fDesc = NormFilter(q.FDesc);
        if (fDesc is not null) query = query.Where(r => r.WorkDescription.Contains(fDesc));

        return query;
    }

    /// <summary>مرتب‌سازی سمت سرور بر اساس کلید سرستون</summary>
    private static IQueryable<ReportWork> ApplySort(IQueryable<ReportWork> query, string? sort, bool desc)
        => (sort ?? "date") switch
        {
            "code" => desc ? query.OrderByDescending(r => r.CodeProject.Length).ThenByDescending(r => r.CodeProject)
                           : query.OrderBy(r => r.CodeProject.Length).ThenBy(r => r.CodeProject),
            "project" => desc ? query.OrderByDescending(r => r.Project!.ProjectName) : query.OrderBy(r => r.Project!.ProjectName),
            "operator" => desc ? query.OrderByDescending(r => r.Operator!.FirstName).ThenByDescending(r => r.Operator!.LastName)
                               : query.OrderBy(r => r.Operator!.FirstName).ThenBy(r => r.Operator!.LastName),
            "user" => desc ? query.OrderByDescending(r => r.User!.FirstName).ThenByDescending(r => r.User!.LastName)
                           : query.OrderBy(r => r.User!.FirstName).ThenBy(r => r.User!.LastName),
            "start" => desc ? query.OrderByDescending(r => r.StartTime) : query.OrderBy(r => r.StartTime),
            "end" => desc ? query.OrderByDescending(r => r.EndTime) : query.OrderBy(r => r.EndTime),
            "rest" => desc
                ? query.OrderByDescending(r => r.BreakfastTime.Hour * 60 + r.BreakfastTime.Minute + r.LunchTime.Hour * 60 + r.LunchTime.Minute)
                : query.OrderBy(r => r.BreakfastTime.Hour * 60 + r.BreakfastTime.Minute + r.LunchTime.Hour * 60 + r.LunchTime.Minute),
            "spent" => desc ? query.OrderByDescending(r => r.SpentTime) : query.OrderBy(r => r.SpentTime),
            _ => desc ? query.OrderByDescending(r => r.ReportDate).ThenByDescending(r => r.Id)
                      : query.OrderBy(r => r.ReportDate).ThenBy(r => r.Id)
        };

    /// <summary>
    /// لیست گزارش‌های کار — فیلتر ستونی، مرتب‌سازی و صفحه‌بندی سمت سرور.
    /// خروجی: PagedResult (ردیف‌های همان صفحه + تعداد کل + جمع ساعات کل فیلتر).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] ReportWorkListQuery q, [FromQuery] int? skip = null, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;

        var filtered = ApplyFilters(Db.ReportWorks.AsNoTracking().Where(r => !r.IsDelete), q);

        // ---------------------------------------------------------------
        //  صفحه‌بندی کاملاً سمت سرور است: از جدول فقط «یک صفحه» خوانده می‌شود.
        //  (۱) تعداد کل ردیف‌های منطبق — COUNT در دیتابیس
        //  (۲) جمع ساعت خالص کل ردیف‌های منطبق — تجمیع جریانی (Stream) بدون ساخت لیست
        //  (۳) شناسه‌های همان صفحه — ORDER BY ... OFFSET/FETCH
        //  (۴) فقط ردیف‌های همان صفحه با Include
        //  پیش‌تر جمع ساعت‌ها با ToListAsync خوانده می‌شد؛ با ۸۰۰۰ گزارش، برای هر جستجو
        //  هزاران مقدار در حافظه ساخته می‌شد. اکنون حافظه ثابت (O(1)) و انتقال سبک است.
        // ---------------------------------------------------------------
        var total = await filtered.CountAsync();

        var spentTicks = 0L;
        await foreach (var ts in filtered.Select(r => r.SpentTime).AsAsyncEnumerable())
            spentTicks += ts.Ticks;

        var page = q.Page < 1 ? 1 : q.Page;
        var pageSize = q.PageSize;
        // ورودی یکپارچه skip/take — بر page/pageSize اولویت دارد
        page = Paging.ToPage(skip, take, page);
        pageSize = Paging.ToPageSize(take, pageSize);
        var idQuery = ApplySort(filtered, q.Sort, q.Desc).Select(r => r.Id);
        if (pageSize > 0)
        {
            var pageCount = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
            if (page > pageCount) page = pageCount;
            idQuery = idQuery.Skip((page - 1) * pageSize).Take(pageSize);
        }
        var ids = await idQuery.ToListAsync();

        var rows = await Db.ReportWorks.AsNoTracking()
            .Include(r => r.Project)
            .Include(r => r.User)
            .Include(r => r.Operator)
            .Where(r => ids.Contains(r.Id))
            .ToListAsync();
        var map = rows.ToDictionary(r => r.Id);
        var ordered = ids.Where(map.ContainsKey).Select(id => map[id]).ToList();

        return Ok(new PagedResult<ReportWorkDto>
        {
            Items = ordered.Select(ToDto).ToList(),
            Total = total,
            Page = page,
            PageSize = pageSize > 0 ? pageSize : Math.Max(total, 1),
            SumTicks = spentTicks
        });
    }

    /// <summary>
    /// خروجی اکسل گزارش‌های کار — <b>بر اساس همان فیلترهای جاری لیست</b>
    /// (اگر فیلتری اعمال نشده باشد، همهٔ گزارش‌ها).
    /// </summary>
    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] ReportWorkListQuery q)
    {
        if (await ForbiddenUnlessAsync(Module, "Export") is { } forbid) return forbid;

        var filtered = ApplyFilters(Db.ReportWorks.AsNoTracking().Where(r => !r.IsDelete), q);
        var list = await ApplySort(filtered, q.Sort, q.Desc)
            .Include(r => r.Project)
            .Include(r => r.User)
            .Include(r => r.Operator)
            .ToListAsync();

        using var wb = new ClosedXML.Excel.XLWorkbook();
        var ws = wb.Worksheets.Add("گزارش‌های کار");
        ws.RightToLeft = true;

        var headers = new[] { "ردیف", "تاریخ", "کد پروژه", "پروژه", "کاربر", "اپراتور", "شروع", "پایان", "استراحت", "ساعت خالص", "شرح کار" };
        for (var i = 0; i < headers.Length; i++)
        {
            var c = ws.Cell(1, i + 1);
            c.Value = headers[i];
            c.Style.Font.Bold = true;
            c.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#FEF3C7");
            c.Style.Border.BottomBorder = ClosedXML.Excel.XLBorderStyleValues.Thin;
        }
        ws.SheetView.FreezeRows(1);

        var row = 2;
        var totalTicks = 0L;
        foreach (var r in list)
        {
            var rest = r.BreakfastTime.ToTimeSpan() + r.LunchTime.ToTimeSpan();
            ws.Cell(row, 1).Value = row - 1;
            ws.Cell(row, 2).Value = Inventory.Shared.PersianDate.ToShort(r.ReportDate);
            ws.Cell(row, 3).Value = !string.IsNullOrEmpty(r.CodeProject) ? r.CodeProject : (r.Project?.CodeProject ?? "");
            ws.Cell(row, 4).Value = r.Project?.ProjectName ?? "";
            ws.Cell(row, 5).Value = r.User is null ? "" : DisplayOf(r.User);
            ws.Cell(row, 6).Value = r.Operator is null ? "" : DisplayOf(r.Operator);
            ws.Cell(row, 7).Value = r.StartTime.ToString("HH:mm");
            ws.Cell(row, 8).Value = r.EndTime.ToString("HH:mm");
            ws.Cell(row, 9).Value = $"{(int)rest.TotalHours}:{rest.Minutes:00}";
            ws.Cell(row, 10).Value = $"{(int)r.SpentTime.TotalHours}:{r.SpentTime.Minutes:00}";
            ws.Cell(row, 11).Value = r.WorkDescription;
            totalTicks += r.SpentTime.Ticks;
            row++;
        }

        var sum = TimeSpan.FromTicks(totalTicks);
        ws.Cell(row, 1).Value = $"جمع کل — {list.Count} گزارش";
        ws.Cell(row, 1).Style.Font.Bold = true;
        ws.Cell(row, 10).Value = $"{(int)sum.TotalHours}:{sum.Minutes:00}";
        ws.Cell(row, 10).Style.Font.Bold = true;
        ws.Range(row, 1, row, headers.Length).Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#FEF3C7");
        ws.Columns(1, headers.Length).AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return File(ms.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"report_works_{DateTime.Now:yyyyMMdd_HHmm}.xlsx");
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        var r = await Db.ReportWorks.AsNoTracking()
            .Include(x => x.Project)
            .Include(x => x.User)
            .Include(x => x.Operator)
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDelete);
        return r is null ? NotFound(new { message = "گزارش کار پیدا نشد." }) : Ok(ToDto(r));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] ReportWorkDto dto)
    {
        if (await ForbiddenUnlessAsync(Module, "Create") is { } forbid) return forbid;
        var err = await ValidateAsync(dto);
        if (err is not null) return err;

        var entity = new ReportWork();
        Map(dto, entity);
        // گزارش بر اساس «کد پروژه» ذخیره می‌شود نه فقط Id (کپی موقع ثبت)
        entity.CodeProject = await CodeOfProjectAsync(dto.ProjectId);
        // کاربر گزارش‌دهنده همیشه کاربر لاگین فعلی است (تو بک‌اند)
        entity.UserId = MyUserId;
        entity.CreatedAt = DateTime.Now;

        Db.ReportWorks.Add(entity);
        await Db.SaveChangesAsync();
        await RecalcProjectTotalAsync(entity.ProjectId);
        return Ok(new { id = entity.Id, spentTime = entity.SpentTime, codeProject = entity.CodeProject });
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] ReportWorkDto dto)
    {
        if (await ForbiddenUnlessAsync(Module, "Update") is { } forbid) return forbid;
        var err = await ValidateAsync(dto);
        if (err is not null) return err;

        var entity = await Db.ReportWorks.FirstOrDefaultAsync(r => r.Id == id && !r.IsDelete);
        if (entity is null) return NotFound(new { message = "گزارش کار پیدا نشد." });

        var oldProjectId = entity.ProjectId;
        var originUserId = entity.UserId; // ثبت‌کننده اصلی حفظ می‌شود
        Map(dto, entity);
        entity.UserId = originUserId;
        // اگر پروژه عوض شد، کد پروژه هم به‌روزرسانی شود
        if (oldProjectId != entity.ProjectId)
            entity.CodeProject = await CodeOfProjectAsync(entity.ProjectId);
        await Db.SaveChangesAsync();

        await RecalcProjectTotalAsync(entity.ProjectId);
        if (oldProjectId != entity.ProjectId)
            await RecalcProjectTotalAsync(oldProjectId);
        return Ok(new { id = entity.Id, spentTime = entity.SpentTime });
    }

    /// <summary>حذف نرم</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        if (await ForbiddenUnlessAsync(Module, "Delete") is { } forbid) return forbid;

        var entity = await Db.ReportWorks.FirstOrDefaultAsync(r => r.Id == id && !r.IsDelete);
        if (entity is null) return NotFound(new { message = "گزارش کار پیدا نشد." });

        entity.IsDelete = true;
        await Db.SaveChangesAsync();
        await RecalcProjectTotalAsync(entity.ProjectId);
        return Ok(new { ok = true });
    }

    // ==================== کمکی ====================

    /// <summary>کد پروژه از روی شناسه (مثل «RE1-2001» یا «2001»)</summary>
    private async Task<string> CodeOfProjectAsync(int projectId)
    {
        var code = await Db.ProjectEntryExits.AsNoTracking()
            .Where(p => p.Id == projectId)
            .Select(p => p.CodeProject)
            .FirstOrDefaultAsync();
        return code ?? "";
    }

    private async Task<IActionResult?> ValidateAsync(ReportWorkDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.WorkDescription))
            return BadRequest(new { message = "شرح کار الزامی است." });

        var project = await Db.ProjectEntryExits.AsNoTracking()
            .Where(p => p.Id == dto.ProjectId && !p.IsDelete)
            .Select(p => new { p.FlowStatus, p.ProjectName, p.CodeProject })
            .FirstOrDefaultAsync();
        if (project is null)
            return BadRequest(new { message = "پروژه انتخاب نشده یا معتبر نیست." });

        // ---------- قانون گردش‌کار: فقط روی پروژهٔ تاییدشده می‌توان گزارش کار ثبت کرد ----------
        // ۰ = در انتظار تایید مدیر → هنوز کار شروع نشده، ثبت گزارش مجاز نیست
        // ۲ = رد شده توسط مدیر    → پروژه متوقف است، ثبت گزارش مجاز نیست
        // ۱ (در انتظار کارشناسی) و ۳ (نهایی) → مجاز
        if (project.FlowStatus == 0)
            return BadRequest(new
            {
                message = $"پروژهٔ «{project.ProjectName}» (کد {project.CodeProject}) هنوز در کارتابل مدیر و «در انتظار تایید» است — " +
                          "تا زمانی که مدیر آن را تایید نکند امکان ثبت گزارش کار وجود ندارد."
            });
        if (project.FlowStatus == 2)
            return BadRequest(new
            {
                message = $"پروژهٔ «{project.ProjectName}» (کد {project.CodeProject}) توسط مدیر «رد» شده است — " +
                          "برای ثبت گزارش کار ابتدا باید پروژه اصلاح و دوباره برای تایید مدیر ارسال شود."
            });

        if (dto.EndTime == dto.StartTime)
            return BadRequest(new { message = "ساعت شروع و پایان نمی‌توانند یکسان باشند." });

        // اپراتور (انجام‌دهندهٔ کار) اختیاری است ولی اگر داده شد باید کاربر معتبر باشد
        if (dto.OperatorId is > 0 && !await Db.Users.AnyAsync(u => u.Id == dto.OperatorId))
            return BadRequest(new { message = "اپراتور انتخاب‌شده در لیست کاربران وجود ندارد." });
        return null;
    }

    private static void Map(ReportWorkDto dto, ReportWork e)
    {
        e.ReportDate = dto.ReportDate.Date;
        e.UserId = dto.UserId;
        e.OperatorId = dto.OperatorId is > 0 ? dto.OperatorId : null;
        e.WorkDescription = dto.WorkDescription.Trim();
        e.ProjectId = dto.ProjectId;
        e.StartTime = dto.StartTime;
        e.EndTime = dto.EndTime;
        e.BreakfastTime = dto.BreakfastTime;
        e.LunchTime = dto.LunchTime;
        e.SpentTime = CalcSpent(dto.StartTime, dto.EndTime, dto.BreakfastTime, dto.LunchTime);
    }

    /// <summary>جمع ساعات پروژه از روی گزارش‌های فعال به‌روزرسانی می‌شود</summary>
    private async Task RecalcProjectTotalAsync(int projectId)
    {
        var project = await Db.ProjectEntryExits.FirstOrDefaultAsync(p => p.Id == projectId);
        if (project is null) return;
        // جمع در حافظه — EF نمی‌تواند Sum روی TimeSpan را به SQL ترجمه کند
        var spans = await Db.ReportWorks
            .Where(r => r.ProjectId == projectId && !r.IsDelete)
            .Select(r => r.SpentTime)
            .ToListAsync();
        project.TotalSpentTime = TimeSpan.FromTicks(spans.Sum(s => s.Ticks));
        await Db.SaveChangesAsync();
    }

    private static ReportWorkDto ToDto(ReportWork r) => new()
    {
        Id = r.Id,
        ReportDate = r.ReportDate,
        UserId = r.UserId,
        OperatorId = r.OperatorId,
        WorkDescription = r.WorkDescription,
        ProjectId = r.ProjectId,
        // اولویت با کد ذخیره‌شده روی گزارش (اسنادی)؛ برای داده‌های خیلی قدیمی از خود پروژه
        CodeProject = !string.IsNullOrEmpty(r.CodeProject) ? r.CodeProject : (r.Project?.CodeProject ?? ""),
        StartTime = r.StartTime,
        EndTime = r.EndTime,
        BreakfastTime = r.BreakfastTime,
        LunchTime = r.LunchTime,
        SpentTime = r.SpentTime,
        ProjectName = r.Project is null ? null : r.Project.ProjectName,
        UserName = r.User is null ? null : DisplayOf(r.User),
        OperatorName = r.Operator is null ? null : DisplayOf(r.Operator)
    };
}
