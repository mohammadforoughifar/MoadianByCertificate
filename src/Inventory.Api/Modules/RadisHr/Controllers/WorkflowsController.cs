using Inventory.Api.Services;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RadisHr.Api.Data;
using RadisHr.Api.Services;
using RadisHr.Shared.Contracts;
using RadisHr.Shared.Models;
using Paging = Inventory.Api.Services.Paging;

namespace RadisHr.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/workflows")]
public class WorkflowsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly PayrollService _payroll;

    public WorkflowsController(AppDbContext db, PayrollService payroll)
    {
        _db = db;
        _payroll = payroll;
    }

    private string Actor => User.Identity?.Name ?? "نامشخص";

    // ═════════ مساعده (نقش مالی) ═════════
    [HttpGet("advances")]
    public async Task<ActionResult<List<Advance>>> Advances([FromQuery] string? code, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        var query = _db.Advances.Include(a => a.Installments).AsNoTracking().AsQueryable();
        if (!string.IsNullOrEmpty(code)) query = query.Where(a => a.EmployeeCode == code);
        return Ok(await Paging.ResultAsync(query.OrderByDescending(a => a.CreatedAt), skip, take));
    }

    /// <summary>ثبت مساعدهٔ پرداخت‌شده — کاربر مالی فقط ثبت می‌کند</summary>
    [Authorize(Roles = "finance,accounting,ceo")]
    [HttpPost("advances")]
    public async Task<ActionResult<IdResponse>> CreateAdvance(Advance advance)
    {
        if (advance.Amount <= 0) return BadRequest(new ApiMessage(false, "مبلغ مساعده باید بزرگ‌تر از صفر باشد."));
        if (!await _db.Employees.AnyAsync(e => e.Code == advance.EmployeeCode))
            return BadRequest(new ApiMessage(false, "پرسنل یافت نشد."));

        advance.Id = 0;
        advance.Uid = Guid.NewGuid().ToString("N");
        advance.Deducted = 0;
        advance.SettlementMethod = "pending";
        advance.SettlementStatus = "تعیین تکلیف نشده";
        advance.CreatedBy = Actor;
        advance.Installments = new List<AdvanceInstallment>();

        _db.Advances.Add(advance);
        await _db.SaveChangesAsync();
        return new IdResponse(advance.Id, "مساعده ثبت شد و در انتظار تعیین روش تسویه است.");
    }

    /// <summary>
    /// تعیین روش تسویه: یکجا (full) یا اقساطی (installment، حداکثر ۶۰ قسط).
    /// اقساط از ماه شروع به‌صورت متوالی ساخته می‌شوند.
    /// </summary>
    [Authorize(Roles = "accounting,finance,ceo")]
    [HttpPost("advances/{id:int}/settlement")]
    public async Task<ActionResult<ApiMessage>> SetSettlement(int id, Advance input)
    {
        var advance = await _db.Advances.Include(a => a.Installments).FirstOrDefaultAsync(a => a.Id == id);
        if (advance == null) return NotFound(new ApiMessage(false, "مساعده یافت نشد."));

        _db.AdvanceInstallments.RemoveRange(advance.Installments);
        advance.SettlementMethod = input.SettlementMethod;

        if (input.SettlementMethod == "full")
        {
            advance.SettlementStartMonth = input.SettlementStartMonth;
            advance.SettlementCount = 1;
            advance.SettlementAmount = advance.Amount;
            advance.SettlementStatus = "تسویه یکجا";
            _db.AdvanceInstallments.Add(new AdvanceInstallment
            {
                AdvanceId = id, Month = input.SettlementStartMonth, Amount = advance.Amount
            });
        }
        else if (input.SettlementMethod == "installment")
        {
            var count = Math.Clamp(input.SettlementCount, 1, 60);
            var each = input.SettlementAmount > 0
                ? input.SettlementAmount
                : Math.Floor(advance.Amount / count);

            advance.SettlementStartMonth = input.SettlementStartMonth;
            advance.SettlementCount = count;
            advance.SettlementAmount = each;
            advance.SettlementStatus = $"اقساطی ({count} قسط)";

            var parts = input.SettlementStartMonth.Split('/');
            if (parts.Length != 2 || !int.TryParse(parts[0], out var year) || !int.TryParse(parts[1], out var month))
                return BadRequest(new ApiMessage(false, "ماه شروع تسویه نامعتبر است (نمونه: 1405/04)."));

            var remaining = advance.Amount;
            for (var i = 0; i < count; i++)
            {
                var amount = i == count - 1 ? remaining : each;
                remaining -= amount;
                _db.AdvanceInstallments.Add(new AdvanceInstallment
                {
                    AdvanceId = id, Month = $"{year}/{month:00}", Amount = amount
                });
                month++;
                if (month > 12) { month = 1; year++; }
            }
        }
        else
        {
            advance.SettlementStatus = "تعیین تکلیف نشده";
        }

        await _db.SaveChangesAsync();
        return new ApiMessage(true, "روش تسویه ثبت شد.");
    }

    [Authorize(Roles = "accounting,ceo")]
    [HttpPost("advances/installments/{id:int}/apply")]
    public async Task<ActionResult<ApiMessage>> ApplyInstallment(int id)
    {
        var installment = await _db.AdvanceInstallments.FindAsync(id);
        if (installment == null) return NotFound(new ApiMessage(false, "قسط یافت نشد."));
        if (installment.Applied) return BadRequest(new ApiMessage(false, "این قسط قبلاً اعمال شده است."));

        installment.Applied = true;
        installment.AppliedAt = DateTime.UtcNow;

        var advance = await _db.Advances.FindAsync(installment.AdvanceId);
        if (advance != null)
        {
            advance.Deducted += installment.Amount;
            if (advance.Deducted >= advance.Amount) advance.SettlementStatus = "تسویه کامل";
        }

        await _db.SaveChangesAsync();
        return new ApiMessage(true, "قسط اعمال شد.");
    }

    // ═════════ حسابداری: اصلاح، تأیید و تسویهٔ ماه ═════════
    [HttpGet("accounting/{month}")]
    public async Task<ActionResult<object>> AccountingMonth(string month)
    {
        var rows = await _payroll.MonthRowsAsync(month);
        var adjustments = await _db.AccountingAdjustments.AsNoTracking()
            .Where(a => a.Month == month).ToListAsync();
        var archive = await _db.AccountingArchives.AsNoTracking().FirstOrDefaultAsync(a => a.Month == month);
        var payments = await _db.PayrollPayments.AsNoTracking().Where(p => p.Month == month).ToListAsync();

        var byCode = adjustments.ToDictionary(a => a.EmployeeCode, a => a);
        var merged = rows.Select(r =>
        {
            byCode.TryGetValue(r.Code, out var adj);
            return new
            {
                row = r,
                adjustment = adj,
                finalNet = adj?.FinalNet ?? r.Net,
                paid = payments.Any(p => p.EmployeeCode == r.Code)
            };
        }).ToList();

        return new
        {
            month,
            status = archive?.Status ?? "در حال بررسی",
            locked = archive?.Locked ?? false,
            rows = merged,
            totalNet = merged.Sum(m => m.finalNet),
            employeeCount = merged.Count,
            archive
        };
    }

    /// <summary>اصلاح اقلام حقوق توسط حسابداری — درج دلیل الزامی است</summary>
    [Authorize(Roles = "accounting,ceo")]
    [HttpPost("accounting/adjust")]
    public async Task<ActionResult<ApiMessage>> Adjust(AccountingAdjustment input)
    {
        if (string.IsNullOrWhiteSpace(input.Reason))
            return BadRequest(new ApiMessage(false, "ثبت دلیل اصلاح الزامی است."));

        var archive = await _db.AccountingArchives.FirstOrDefaultAsync(a => a.Month == input.Month);
        if (archive?.Locked == true)
            return BadRequest(new ApiMessage(false, "این ماه قفل شده و قابل ویرایش نیست."));

        var row = await _db.PayrollRows.FirstOrDefaultAsync(r => r.Month == input.Month && r.Code == input.EmployeeCode);
        if (row == null) return NotFound(new ApiMessage(false, "ردیف حقوق این ماه یافت نشد."));

        // محاسبهٔ خالص نهایی پس از اصلاحات
        var gross = row.Gross
                  - row.OtPay + (input.Overtime ?? row.OtPay)
                  - row.Performance + (input.Performance ?? row.Performance)
                  + (input.Productivity ?? 0m)
                  + (input.OtherPayments ?? 0m);
        var shortfall = input.Shortfall ?? row.ShortfallPay;
        var insurance = input.Insurance ?? row.Insurance;
        var tax = input.Tax ?? row.Tax;
        input.FinalNet = Math.Max(0m, gross - shortfall - insurance - tax);

        var existing = await _db.AccountingAdjustments
            .FirstOrDefaultAsync(a => a.Month == input.Month && a.EmployeeCode == input.EmployeeCode);

        input.AdjustedBy = Actor;
        input.AdjustedAt = DateTime.UtcNow;

        if (existing == null) { input.Id = 0; _db.AccountingAdjustments.Add(input); }
        else { input.Id = existing.Id; _db.Entry(existing).CurrentValues.SetValues(input); }

        _db.CeoNotifications.Add(new AuditNotice
        {
            Uid = Guid.NewGuid().ToString("N"),
            Channel = "operational",
            Title = $"اصلاح حقوق {input.Month}",
            Message = $"اقلام حقوق پرسنل {input.EmployeeCode} اصلاح شد. دلیل: {input.Reason}",
            Actor = Actor
        });

        await _db.SaveChangesAsync();
        return new ApiMessage(true, $"اصلاح ثبت شد. خالص نهایی: {input.FinalNet:N0} ریال");
    }

    /// <summary>«تأیید ماه» — قفل کردن و آماده‌سازی مجوز پرداخت</summary>
    [Authorize(Roles = "accounting,ceo")]
    [HttpPost("accounting/{month}/approve")]
    public async Task<ActionResult<ApiMessage>> ApproveMonth(string month)
    {
        var rows = await _payroll.MonthRowsAsync(month);
        if (rows.Count == 0) return BadRequest(new ApiMessage(false, "برای این ماه ردیفی وجود ندارد."));

        var adjustments = (await _db.AccountingAdjustments.Where(a => a.Month == month).ToListAsync())
            .ToDictionary(a => a.EmployeeCode, a => a);

        var snapshot = rows.Select(r => new
        {
            r.Code, r.Name, r.Gross, r.Insurance, r.Tax,
            net = adjustments.TryGetValue(r.Code, out var a) ? a.FinalNet : r.Net
        }).ToList();

        var archive = await _db.AccountingArchives.FirstOrDefaultAsync(a => a.Month == month);
        if (archive == null)
        {
            archive = new AccountingArchive { Month = month };
            _db.AccountingArchives.Add(archive);
        }

        archive.Status = "تأییدشده";
        archive.Locked = true;
        archive.TotalNet = snapshot.Sum(s => s.net);
        archive.EmployeeCount = snapshot.Count;
        archive.ApprovedBy = Actor;
        archive.ApprovedAt = DateTime.UtcNow;
        archive.PayloadJson = JsonSerializer.Serialize(snapshot);

        await _db.SaveChangesAsync();
        return new ApiMessage(true, $"ماه {month} تأیید و قفل شد. مجموع خالص: {archive.TotalNet:N0} ریال");
    }

    /// <summary>«تسویه شد» — ثبت پرداخت نهایی</summary>
    [Authorize(Roles = "accounting,ceo")]
    [HttpPost("accounting/{month}/settle")]
    public async Task<ActionResult<ApiMessage>> SettleMonth(string month, [FromQuery] string? paidDate, [FromQuery] string? reference)
    {
        var archive = await _db.AccountingArchives.FirstOrDefaultAsync(a => a.Month == month);
        if (archive == null || archive.Status != "تأییدشده")
            return BadRequest(new ApiMessage(false, "ابتدا باید ماه تأیید شود."));

        var rows = await _payroll.MonthRowsAsync(month);
        var adjustments = (await _db.AccountingAdjustments.Where(a => a.Month == month).ToListAsync())
            .ToDictionary(a => a.EmployeeCode, a => a);
        var alreadyPaid = (await _db.PayrollPayments.Where(p => p.Month == month)
            .Select(p => p.EmployeeCode).ToListAsync()).ToHashSet();

        var count = 0;
        foreach (var row in rows)
        {
            if (alreadyPaid.Contains(row.Code)) continue;
            _db.PayrollPayments.Add(new PayrollPayment
            {
                Month = month,
                EmployeeCode = row.Code,
                Amount = adjustments.TryGetValue(row.Code, out var a) ? a.FinalNet : row.Net,
                PaidDate = paidDate ?? "",
                Reference = reference ?? "",
                PaidBy = Actor
            });
            count++;
        }

        archive.Status = "تسویه‌شده";
        archive.SettledBy = Actor;
        archive.SettledAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return new ApiMessage(true, $"تسویه ثبت شد؛ {count} پرداخت ایجاد شد.");
    }

    [HttpGet("payments")]
    public async Task<ActionResult<List<PayrollPayment>>> Payments([FromQuery] string? month, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        var query = _db.PayrollPayments.AsNoTracking().AsQueryable();
        if (!string.IsNullOrEmpty(month)) query = query.Where(p => p.Month == month);
        return Ok((await Paging.QueryAsync(query.OrderByDescending(p => p.CreatedAt), skip, take, 2000)).Result());
    }

    [HttpGet("archives")]
    public async Task<ActionResult<List<AccountingArchive>>> Archives([FromQuery] int skip = 0, [FromQuery] int? take = null) =>
        Ok(await Paging.ResultAsync(_db.AccountingArchives.AsNoTracking().OrderByDescending(a => a.Month), skip, take));

    // ═════════ اطلاعیه‌ها ═════════
    [HttpGet("announcements")]
    public async Task<ActionResult<List<Announcement>>> Announcements([FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        var pagination = new Paging.Request(skip, take);
        var role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? "";
        var now = DateTime.UtcNow;
        var all = await _db.Announcements
            .Where(a => a.Recipients.Any(r => (r.RoleKey == role || role == "ceo" || role == "hr")
                && (r.FirstSeenAt == null || r.FirstSeenAt.Value.AddDays(a.DurationDays) >= now)))
            .Include(a => a.Recipients).Include(a => a.Attachments)
            .AsNoTracking().OrderByDescending(a => a.CreatedAt).ThenBy(a => a.Id).ToPageListAsync(pagination);
        return Ok(pagination.Result(all));
    }

    [Authorize(Roles = "hr,ceo")]
    [HttpPost("announcements")]
    public async Task<ActionResult<IdResponse>> CreateAnnouncement(Announcement announcement)
    {
        if (string.IsNullOrWhiteSpace(announcement.Title))
            return BadRequest(new ApiMessage(false, "عنوان اطلاعیه الزامی است."));
        if (announcement.DurationDays is < 1 or > 365)
            return BadRequest(new ApiMessage(false, "مدت نمایش باید بین ۱ تا ۳۶۵ روز باشد."));
        if (announcement.Recipients.Count == 0)
            return BadRequest(new ApiMessage(false, "حداقل یک گیرنده انتخاب کنید."));

        announcement.Id = 0;
        announcement.Uid = Guid.NewGuid().ToString("N");
        announcement.CreatedBy = Actor;
        foreach (var r in announcement.Recipients) { r.Id = 0; r.FirstSeenAt = null; }
        foreach (var a in announcement.Attachments) { a.Id = 0; a.Uid = Guid.NewGuid().ToString("N"); }

        _db.Announcements.Add(announcement);
        await _db.SaveChangesAsync();
        return new IdResponse(announcement.Id, "اطلاعیه منتشر شد.");
    }

    [HttpPost("announcements/{id:int}/seen")]
    public async Task<ActionResult<ApiMessage>> MarkAnnouncementSeen(int id)
    {
        var role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? "";
        var recipient = await _db.AnnouncementRecipients
            .FirstOrDefaultAsync(r => r.AnnouncementId == id && r.RoleKey == role);
        if (recipient == null) return NotFound(new ApiMessage(false, "گیرنده یافت نشد."));

        recipient.FirstSeenAt ??= DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return new ApiMessage(true, "مشاهده ثبت شد.");
    }

    [Authorize(Roles = "hr,ceo")]
    [HttpDelete("announcements/{id:int}")]
    public async Task<ActionResult<ApiMessage>> DeleteAnnouncement(int id)
    {
        var announcement = await _db.Announcements.FindAsync(id);
        if (announcement == null) return NotFound(new ApiMessage(false, "یافت نشد."));
        _db.Announcements.Remove(announcement);
        await _db.SaveChangesAsync();
        return new ApiMessage(true, "اطلاعیه حذف شد.");
    }

    // ═════════ اعلان‌های ممیزی مدیرعامل ═════════
    [HttpGet("notices")]
    public async Task<ActionResult<List<AuditNotice>>> Notices([FromQuery] string? channel, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        var query = _db.CeoNotifications.AsNoTracking().AsQueryable();
        if (!string.IsNullOrEmpty(channel)) query = query.Where(n => n.Channel == channel);
        return Ok((await Paging.QueryAsync(query.OrderByDescending(n => n.CreatedAt), skip, take, 300)).Result());
    }

    [HttpPost("notices/{id:int}/seen")]
    public async Task<ActionResult<ApiMessage>> MarkNoticeSeen(int id)
    {
        var notice = await _db.CeoNotifications.FindAsync(id);
        if (notice == null) return NotFound(new ApiMessage(false, "یافت نشد."));
        notice.Seen = true;
        await _db.SaveChangesAsync();
        return new ApiMessage(true, "خوانده شد.");
    }
}
