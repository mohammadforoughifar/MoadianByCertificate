using Inventory.Api.Data;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Controllers.DocArchive;

/// <summary>
/// ================== تنظیمات آرشیو اسناد — شماره‌گذار خودکار کد مدرک ==================
/// فقط مدیر آرشیو (مجوز DocArchive.Manage). فعال‌سازی در تنظیمات؛ هنگام ساخت مدرک،
/// اگر کد خالی بماند، کد به‌صورت خودکار و اتمیک از شمارنده تخصیص می‌یابد.
/// «اجرای یک‌باره» کد مدارک موجودِ ناسازگار با الگو را بر اساس الگو بازشمارش می‌کند (فقط یک‌بار).
/// </summary>
[ApiController]
[Route("api/doc-archive/settings")]
public class DocSettingsController : RbacControllerBase
{
    private const string Mod = "DocArchive";

    public DocSettingsController(AppDbContext db) : base(db) { }

    private Task<bool> IsManagerAsync() => HasAsync(Mod, "Manage");

    private async Task<DocCodeSettings> GetOrCreateRowAsync()
    {
        var st = await Db.DocCodeSettings.FirstOrDefaultAsync(s => s.Id == 1);
        if (st == null)
        {
            st = new DocCodeSettings { Id = 1 };
            Db.DocCodeSettings.Add(st);
            await Db.SaveChangesAsync();
        }
        return st;
    }

    private static DocCodeSettingsDto ToDto(DocCodeSettings s) => new()
    {
        Enabled = s.Enabled,
        Prefix = s.Prefix,
        Padding = s.Padding,
        NextNumber = s.NextNumber,
        BackfillRanAt = s.BackfillRanAt,
        BackfillRanByName = s.BackfillRanByName,
        BackfillAssignedCount = s.BackfillAssignedCount
    };

    [HttpGet("code-numbering")]
    public async Task<IActionResult> Get()
    {
        if (!await IsManagerAsync())
            return StatusCode(403, new { message = "تنظیمات آرشیو فقط برای مدیر آرشیو است." });
        return Ok(ToDto(await GetOrCreateRowAsync()));
    }

    [HttpPut("code-numbering")]
    public async Task<IActionResult> Save([FromBody] DocCodeSettingsDto dto)
    {
        if (!await IsManagerAsync())
            return StatusCode(403, new { message = "تنظیمات آرشیو فقط برای مدیر آرشیو است." });

        var st = await GetOrCreateRowAsync();
        st.Enabled = dto.Enabled;
        var prefix = (dto.Prefix ?? "").Trim();
        st.Prefix = prefix.Length <= 10 ? prefix : prefix[..10];
        st.Padding = Math.Clamp(dto.Padding, 3, 10);
        st.NextNumber = Math.Max(1, dto.NextNumber);
        await Db.SaveChangesAsync();
        return Ok(ToDto(st));
    }

    /// <summary>
    /// اجرای یک‌باره بازشمارش — فقط یک‌بار در کل عمر برنامه قابل اجراست.
    /// کد مدارکی که با الگوی فعلی (پیشوند + رقم) سازگار نیستند، با کدهای خودکار جدید جایگزین می‌شود;
    /// کدهای سازگار دست‌نخورده می‌مانند و هر جایگزینی در رخ‌نامه همان مدرک ثبت می‌شود.
    /// </summary>
    [HttpPost("code-numbering/run-once")]
    public async Task<IActionResult> RunOnce()
    {
        if (!await IsManagerAsync())
            return StatusCode(403, new { message = "تنظیمات آرشیو فقط برای مدیر آرشیو است." });

        var st = await GetOrCreateRowAsync();
        if (!st.Enabled)
            return BadRequest(new { message = "ابتدا شماره‌گذار خودکار را فعال و ذخیره کنید." });
        if (st.BackfillRanAt != null)
            return Conflict(new DocCodeBackfillResultDto
            {
                Assigned = 0,
                Message = $"اجرای یک‌باره قبلاً در {st.BackfillRanAt:g} توسط «{st.BackfillRanByName}» انجام شده است ({st.BackfillAssignedCount} مدرک)."
            });

        var prefix = (st.Prefix ?? "").Trim();
        var pad = Math.Clamp(st.Padding, 3, 10);

        bool MatchesPattern(string code) =>
            code.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && code.Length > prefix.Length
            && code[prefix.Length..].All(char.IsDigit);

        var docs = await Db.Documents.Where(d => !d.IsDeleted).OrderBy(d => d.Id).ToListAsync();
        var usedCodes = docs.Where(d => MatchesPattern(d.Code))
            .Select(d => d.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var n = st.NextNumber;
        // پرش اولیه از روی کدهای الگودار موجود
        while (usedCodes.Contains(prefix + n.ToString().PadLeft(pad, '0'))) n++;

        var assigned = 0;
        foreach (var d in docs)
        {
            if (MatchesPattern(d.Code)) continue;

            var candidate = prefix + n.ToString().PadLeft(pad, '0');
            while (usedCodes.Contains(candidate))
            {
                n++;
                candidate = prefix + n.ToString().PadLeft(pad, '0');
            }

            var oldCode = d.Code;
            d.Code = candidate;
            usedCodes.Add(candidate);
            n++;
            assigned++;

            Db.DocumentLogs.Add(new DocumentLog
            {
                DocumentId = d.Id,
                Action = "CodeRenumber",
                Detail = $"بازشمارش یک‌باره شماره‌گذار خودکار: کد «{oldCode}» → «{candidate}»",
                UserId = MyUserId,
                UserName = MyUsername
            });
        }

        st.NextNumber = n;
        st.BackfillRanAt = DateTime.Now;
        st.BackfillRanByName = MyUsername;
        st.BackfillAssignedCount = assigned;
        await Db.SaveChangesAsync();

        return Ok(new DocCodeBackfillResultDto
        {
            Assigned = assigned,
            Message = assigned == 0
                ? "همه مدارک از قبل دارای کد منطبق با الگو بودند؛ موردی بازشمارش نشد."
                : $"{assigned} مدرک با کد جدید بازشمارش شد. شماره بعدی شمارنده: {n}"
        });
    }
}
