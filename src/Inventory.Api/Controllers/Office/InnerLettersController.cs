using Inventory.Api.Data;
using Inventory.Api.Services;
using Inventory.Api.Services.Office;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.IO.Compression;

namespace Inventory.Api.Controllers;

/// <summary>
/// اتوماسیون اداری — کارتابل نامه داخلی — ماژول دسترسی: InnerLetters
/// </summary>
[Route("api/letters")]
public class InnerLettersController : RbacControllerBase
{
    private const string Module = "InnerLetters";

    private static string PersianError(Exception ex, string fallback)
    {
        var message = ex.GetBaseException().Message?.Trim();
        return !string.IsNullOrWhiteSpace(message) && message.Any(c => c >= 0x0600 && c <= 0x06FF)
            ? message : fallback;
    }

    private readonly IInnerLetterService _letters;
    private readonly IErjaService _erja;
    private readonly IPishnevisService _pishnevis;
    private readonly ILetterGroupService _groups;
    private readonly IArchiveService _archive;
    private readonly FileStore _store;
    private readonly IInnerLetterPrintService _print;

    public InnerLettersController(AppDbContext db, IInnerLetterService letters, IErjaService erja, IPishnevisService pishnevis, ILetterGroupService groups, IArchiveService archive, FileStore store, IInnerLetterPrintService print)
        : base(db)
    {
        _letters = letters;
        _erja = erja;
        _pishnevis = pishnevis;
        _groups = groups;
        _archive = archive;
        _store = store;
        _print = print;
    }

    private async Task<bool> IsAdminAsync() => await HasAsync(Module, "Delete");

    [HttpGet("{id:int}/print")]
    public async Task<IActionResult> Print(int id, [FromQuery] string size = "A4", [FromQuery] bool withHamesh = false)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        if (size is not ("A4" or "A5")) return BadRequest(new { message = "سایز چاپ فقط A4 یا A5 است." });
        try
        {
            var pdf = await _print.GeneratePdfAsync(id, size, MyUserId, HttpContext.RequestAborted, withHamesh);
            var variant = withHamesh ? "with-hamesh" : "without-hamesh";
            return pdf == null ? NotFound(new { message = "نامه پیدا نشد یا دسترسی چاپ ندارید." })
                : File(pdf, "application/pdf", $"inner-letter-{id}-{size}-{variant}.pdf");
        }
        catch (UnauthorizedAccessException ex) { return StatusCode(403, new { message = ex.Message }); }
        catch (InnerLetterPrintException ex) { return StatusCode(500, new { message = ex.Message }); }
    }

    /// <summary>نام نمایشی کاربر جاری (نام و نام خانوادگی، وگرنه نام کاربری)</summary>
    private async Task<string> MyDisplayNameAsync()
    {
        var u = await Db.Users.AsNoTracking()
            .Where(x => x.Id == MyUserId)
            .Select(x => new { x.FirstName, x.LastName, x.Username })
            .FirstOrDefaultAsync();
        if (u == null) return MyUsername;
        var full = $"{u.FirstName} {u.LastName}".Trim();
        return string.IsNullOrWhiteSpace(full) ? u.Username : full;
    }

    // ==================== کارتابل ====================

    /// <summary>صندوق وارده کاربر جاری</summary>
    [HttpGet("inbox")]
    public async Task<IActionResult> Inbox([FromQuery] string? search, [FromQuery] bool? unreadOnly, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] int? skip = null, [FromQuery] int? take = null,
        [FromQuery] bool? starredOnly = null, [FromQuery] bool? attachOnly = null, [FromQuery] bool? deadlineOnly = null)
    {
        page = Paging.ToPage(skip, take, page);
        pageSize = Paging.ToPageSize(take, pageSize);
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        // فیلترهای چیپ کارتابل (نشان‌شده/پیوست‌دار/مهلت‌دار) سمت سرور اعمال می‌شوند
        return Ok(await _letters.GetInboxAsync(MyUserId, search, unreadOnly, page, pageSize, starredOnly, attachOnly, deadlineOnly));
    }

    /// <summary>پوشه بایگانی کاربر جاری</summary>
    [HttpGet("archive")]
    public async Task<IActionResult> Archive([FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] int? skip = null, [FromQuery] int? take = null)
    {
        page = Paging.ToPage(skip, take, page);
        pageSize = Paging.ToPageSize(take, pageSize);
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        return Ok(await _letters.GetArchiveAsync(MyUserId, search, page, pageSize));
    }

    /// <summary>نامه‌های ارسالی کاربر جاری</summary>
    [HttpGet("sent")]
    public async Task<IActionResult> Sent([FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] int? skip = null, [FromQuery] int? take = null,
        [FromQuery] bool? starredOnly = null, [FromQuery] bool? attachOnly = null)
    {
        page = Paging.ToPage(skip, take, page);
        pageSize = Paging.ToPageSize(take, pageSize);
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        return Ok(await _letters.GetSentAsync(MyUserId, search, page, pageSize, starredOnly, attachOnly));
    }

    /// <summary>آمار کارتابل (شمارنده نخوانده‌ها و…)</summary>
    [HttpGet("stats")]
    public async Task<IActionResult> Stats()
    {
        if (!await HasAsync(Module, "Read")) return Ok(new LetterCartableStatsDto());
        return Ok(await _letters.GetStatsAsync(MyUserId));
    }

    /// <summary>جزئیات نامه — فقط فرستنده/گیرندگان/مدیر</summary>
    [HttpGet("{id:int}")]
    public async Task<IActionResult> Detail(int id)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        try
        {
            var dto = await _letters.GetDetailAsync(id, MyUserId, await IsAdminAsync());
            return dto is null
                ? NotFound(new { message = "نامه پیدا نشد یا شما در گردش آن نیستید." })
                : Ok(dto);
        }
        catch (Exception ex) { return BadRequest(new { message = PersianError(ex, "جزئیات نامه قابل دریافت نیست.") }); }
    }

    /// <summary>ثبت و ارسال نامه داخلی جدید</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] AddInnerLetterDto dto)
    {
        if (await ForbiddenUnlessAsync(Module, "Create") is { } forbid) return forbid;
        try
        {
            var id = await _letters.AddInnerLetterAsync(dto, MyUserId, await MyDisplayNameAsync());
            return Ok(new { id, message = "نامه با موفقیت ارسال شد." });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return BadRequest(new { message = PersianError(ex, "ثبت نامه انجام نشد؛ اطلاعات واردشده را بررسی کنید.") });
        }
    }

    /// <summary>ویرایش نامه — فرستنده تا قبل از خوانده‌شدن توسط هر گیرنده (مدیر: همیشه)</summary>
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Edit(int id, [FromBody] EditInnerLetterDto dto)
    {
        var isAdmin = await IsAdminAsync();
        if (!isAdmin && !await HasAsync(Module, "Create"))
            return StatusCode(403, new { message = "شما به این بخش دسترسی ندارید." });
        try
        {
            await _letters.EditAsync(id, dto, MyUserId, isAdmin);
            return Ok(new { message = "نامه ویرایش شد." });
        }
        catch (Exception ex) { return BadRequest(new { message = PersianError(ex, "ویرایش نامه انجام نشد؛ اطلاعات واردشده را بررسی کنید.") }); }
    }

    /// <summary>حذف نرم نامه — فرستنده (تا قبل از خوانده‌شدن) یا مدیر</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var isAdmin = await IsAdminAsync();
        if (!isAdmin && !await HasAsync(Module, "Create"))
            return StatusCode(403, new { message = "شما به این بخش دسترسی ندارید." });
        try
        {
            await _letters.DeleteAsync(id, MyUserId, isAdmin);
            return Ok(new { message = "نامه حذف شد." });
        }
        catch (Exception ex) { return BadRequest(new { message = PersianError(ex, "حذف نامه انجام نشد؛ نامه یا دسترسی خود را بررسی کنید.") }); }
    }

    /// <summary>لیست انتخاب نامه برای عطف/پیرو</summary>
    [HttpGet("pick")]
    public async Task<IActionResult> Pick([FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] int? skip = null, [FromQuery] int? take = null)
    {
        page = Paging.ToPage(skip, take, page);
        pageSize = Paging.ToPageSize(take, pageSize);
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        return Ok(await _letters.PickListAsync(MyUserId, search, page, pageSize));
    }

    // ==================== گردش / ارجاع ====================

    /// <summary>درخت گردش کامل نامه</summary>
    [HttpGet("{id:int}/gardesh")]
    public async Task<IActionResult> Gardesh(int id, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        try { return Ok(await Paging.ResultAsync(async pagination => await _erja.GetGardeshTreeAsync(id, MyUserId, await IsAdminAsync(), pagination), skip, take)); }
        catch (Exception ex) { return BadRequest(new { message = PersianError(ex, "گردش نامه قابل دریافت نیست.") }); }
    }

    /// <summary>ارجاع نامه به کاربر(ان) دیگر</summary>
    [HttpPost("erja")]
    public async Task<IActionResult> AddErja([FromBody] AddErjaDto dto)
    {
        if (await ForbiddenUnlessAsync(Module, "Erja") is { } forbid) return forbid;
        await _erja.AddErjaAsync(dto, MyUserId, await MyDisplayNameAsync());
        return Ok(new { message = "ارجاع با موفقیت ثبت شد." });
    }

    /// <summary>پاسخ/اقدام روی ارجاع (+ تایید/رد)</summary>
    [HttpPost("erja/{erjaId:int}/answer")]
    public async Task<IActionResult> Answer(int erjaId, [FromBody] AnswerErjaDto dto)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        try
        {
            await _erja.AnswerAsync(erjaId, dto, MyUserId, await MyDisplayNameAsync());
            return Ok(new { message = "پاسخ ثبت شد." });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = PersianError(ex, "پاسخ ثبت نشد؛ فقط گیرنده ارجاع می‌تواند پاسخ دهد.") });
        }
    }

    /// <summary>ثبت خوانده‌شدن ارجاع</summary>
    [HttpPost("erja/{erjaId:int}/read")]
    public async Task<IActionResult> MarkRead(int erjaId)
    {
        await _erja.MarkReadAsync(erjaId, MyUserId);
        return Ok();
    }

    /// <summary>نشان‌کردن/برداشتن نشان (ستاره) — نامه دریافتی (روی ارجاع کاربر)</summary>
    [HttpPost("erja/{erjaId:int}/neshan")]
    public async Task<IActionResult> ToggleNeshan(int erjaId)
    {
        var isNeshan = await _erja.ToggleNeshanAsync(erjaId, MyUserId);
        return Ok(new { isNeshan });
    }

    /// <summary>نشان‌کردن/برداشتن نشان (ستاره) — نامه ارسالی (روی خود نامه، توسط فرستنده)</summary>
    [HttpPost("{id:int}/neshan")]
    public async Task<IActionResult> ToggleLetterNeshan(int id)
    {
        var isNeshan = await _letters.ToggleLetterNeshanAsync(id, MyUserId);
        return Ok(new { isNeshan });
    }

    /// <summary>بایگانی / خروج از بایگانی نامه دریافتی</summary>
    [HttpPost("erja/{erjaId:int}/bayegani")]
    public async Task<IActionResult> ToggleBayegani(int erjaId)
    {
        var isBayegani = await _erja.ToggleBayeganiAsync(erjaId, MyUserId);
        return Ok(new { isBayegani });
    }

    /// <summary>بایگانی گروهی نامه‌های دریافتی</summary>
    [HttpPost("erja/batch-bayegani")]
    public async Task<IActionResult> BatchBayegani([FromBody] BatchBayeganiRequest dto)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        if (dto == null || dto.ErjaIds == null || dto.ErjaIds.Count == 0)
            return BadRequest(new { message = "هیچ نامه‌ای برای بایگانی انتخاب نشده است." });

        var count = await _erja.BatchBayeganiAsync(dto.ErjaIds, MyUserId, dto.Description);
        return Ok(new { count, message = $"{count} نامه با موفقیت بایگانی شدند." });
    }

    // ==================== بایگانی درختی ====================

    /// <summary>درخت کامل بایگانی کاربر جاری (پوشه‌ها + نامه‌ها)</summary>
    [HttpGet("bayegani/tree")]
    public async Task<IActionResult> BayeganiTree([FromQuery] int skip = 0, [FromQuery] int? take = null)
        => Ok(await Paging.ResultAsync(pagination => _archive.GetTreeAsync(MyUserId, pagination), skip, take));

    /// <summary>ایجاد دسته اصلی بایگانی (ریشه)</summary>
    [HttpPost("bayegani/main-category")]
    public async Task<IActionResult> AddMainCategory([FromBody] SaveBayeganiFolderDto dto)
        => Ok(await _archive.AddMainCategoryAsync(MyUserId, dto));

    /// <summary>ایجاد زیرپوشه بایگانی</summary>
    [HttpPost("bayegani/sub-category")]
    public async Task<IActionResult> AddSubCategory([FromBody] SaveBayeganiFolderDto dto)
        => Ok(await _archive.AddSubCategoryAsync(MyUserId, dto));

    /// <summary>ویرایش عنوان پوشه بایگانی</summary>
    [HttpPut("bayegani/folder/{id:int}")]
    public async Task<IActionResult> EditBayeganiFolder(int id, [FromBody] SaveBayeganiFolderDto dto)
        => Ok(await _archive.EditFolderAsync(id, MyUserId, dto));

    /// <summary>جابجایی پوشه بایگانی به والد جدید (0 = ریشه)</summary>
    [HttpPost("bayegani/move-folder/{id:int}")]
    public async Task<IActionResult> MoveBayeganiFolder(int id, [FromQuery] int newParentId)
        => Ok(await _archive.MoveFolderAsync(id, newParentId, MyUserId));

    /// <summary>بایگانی یک یا چند نامه در پوشه انتخابی</summary>
    [HttpPost("bayegani/letters")]
    public async Task<IActionResult> ArchiveLetters([FromBody] ArchiveLettersDto dto)
    {
        await _archive.AddLettersToArchiveAsync(MyUserId, dto);
        return Ok();
    }

    /// <summary>جابجایی نامه بایگانی‌شده به پوشه دیگر</summary>
    [HttpPost("bayegani/move-letter/{id:int}")]
    public async Task<IActionResult> MoveBayeganiLetter(int id, [FromQuery] int newParentId)
        => Ok(await _archive.MoveLetterAsync(id, newParentId, MyUserId));

    /// <summary>حذف پوشه خالی / خروج نامه از بایگانی</summary>
    [HttpDelete("bayegani/{id:int}")]
    public async Task<IActionResult> DeleteBayegani(int id)
    {
        await _archive.DeleteAsync(id, MyUserId);
        return Ok();
    }

    /// <summary>خروج نامه از بایگانی بر اساس شناسه ارجاع</summary>
    [HttpPost("bayegani/unarchive/{erjaId:int}")]
    public async Task<IActionResult> UnarchiveByErja(int erjaId)
    {
        await _archive.UnarchiveByErjaAsync(erjaId, MyUserId);
        return Ok();
    }

    /// <summary>خروج نامه ارسالی از بایگانی بر اساس شناسه نامه (مسیر فرستنده)</summary>
    [HttpPost("bayegani/unarchive-letter/{letterId:int}")]
    public async Task<IActionResult> UnarchiveByLetter(int letterId)
    {
        await _archive.UnarchiveByLetterAsync(letterId, MyUserId);
        return Ok();
    }

    /// <summary>لیست عملگرهای ارجاع</summary>
    [HttpGet("amalgars")]
    public async Task<IActionResult> Amalgars([FromQuery] int skip = 0, [FromQuery] int? take = null) => Ok(await Paging.ResultAsync(async pagination => await _erja.GetAmalgarsAsync(pagination: pagination), skip, take));

    /// <summary>افزودن عملگر ارجاع از داخل فرم ارجاع (دکمهٔ «+»)</summary>
    [HttpPost("amalgars")]
    public async Task<IActionResult> CreateAmalgar([FromBody] CreateAmalgarDto dto)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        try
        {
            var created = await _erja.CreateAmalgarAsync(dto?.Title ?? "", dto?.TaeedEmza);
            return Ok(created);
        }
        catch (Exception ex) { return BadRequest(new { message = ex.Message }); }
    }

    // ==================== پیش‌نویس ====================

    [HttpGet("pishnevis")]
    public async Task<IActionResult> PishnevisList([FromQuery] string? search, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        return Ok(await Paging.ResultAsync(async pagination => await _pishnevis.GetAllAsync(MyUserId, search, pagination: pagination), skip, take));
    }

    [HttpGet("pishnevis/{id:int}")]
    public async Task<IActionResult> PishnevisGet(int id)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        var p = await _pishnevis.GetByIdAsync(id, MyUserId);
        return p is null ? NotFound(new { message = "پیش‌نویس پیدا نشد." }) : Ok(p);
    }

    [HttpPost("pishnevis")]
    public async Task<IActionResult> PishnevisSave([FromBody] PishnevisDto dto)
    {
        if (await ForbiddenUnlessAsync(Module, "Create") is { } forbid) return forbid;
        if (dto.PishnevisId > 0)
        {
            await _pishnevis.EditAsync(dto, MyUserId);
            return Ok(new { id = dto.PishnevisId, message = "پیش‌نویس ویرایش شد." });
        }
        var id = await _pishnevis.AddAsync(dto, MyUserId);
        return Ok(new { id, message = "پیش‌نویس ذخیره شد." });
    }

    [HttpDelete("pishnevis/{id:int}")]
    public async Task<IActionResult> PishnevisDelete(int id)
    {
        if (await ForbiddenUnlessAsync(Module, "Create") is { } forbid) return forbid;
        await _pishnevis.DeleteAsync(id, MyUserId);
        return Ok(new { message = "پیش‌نویس حذف شد." });
    }

    // ==================== گیرندگان (کاربران فعال) ====================

    /// <summary>لیست کاربران فعال برای انتخاب گیرنده</summary>
    [HttpGet("recivers")]
    public async Task<IActionResult> Recivers([FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        var pagination = new Paging.Request(skip, take);
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        var users = await Db.Users.AsNoTracking()
            .Where(u => u.IsActive && u.Id != MyUserId)
            .OrderBy(u => u.FirstName).ThenBy(u => u.Username).ThenBy(x => x.Id)
            .Select(u => new LetterReciverDto
            {
                UserId = u.Id,
                FullName = string.IsNullOrEmpty(u.FirstName + u.LastName)
                    ? u.Username
                    : (u.FirstName + " " + u.LastName).Trim()
            })
            .ToPageListAsync(pagination);
        return Ok(pagination.Result(users));
    }

    // ==================== گروه‌های گیرندگان (پورت Groups کارفرما) ====================

    /// <summary>لیست گروه‌های فعال — با اعضا (برای نمایش در کمبوی گروهی)</summary>
    [HttpGet("groups")]
    public async Task<IActionResult> Groups([FromQuery] bool withMembers = true, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        return Ok(await Paging.ResultAsync(async pagination => await _groups.GetAllAsync(withMembers, pagination: pagination), skip, take));
    }

    /// <summary>ایجاد/ویرایش گروه گیرندگان</summary>
    [HttpPost("groups")]
    public async Task<IActionResult> SaveGroup([FromBody] SaveLetterGroupDto dto)
    {
        if (await ForbiddenUnlessAsync(Module, "Create") is { } forbid) return forbid;
        var id = await _groups.SaveAsync(dto, MyUserId);
        return Ok(new { id, message = "گروه ذخیره شد." });
    }

    /// <summary>حذف نرم گروه — سازنده یا مدیر</summary>
    [HttpDelete("groups/{id:int}")]
    public async Task<IActionResult> DeleteGroup(int id)
    {
        var isAdmin = await IsAdminAsync();
        if (!isAdmin && !await HasAsync(Module, "Create"))
            return StatusCode(403, new { message = "شما به این بخش دسترسی ندارید." });
        await _groups.DeleteAsync(id, MyUserId, isAdmin);
        return Ok(new { message = "گروه حذف شد." });
    }

    private static readonly HashSet<string> AllowedAttachmentExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".pdf", ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".txt", ".rtf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".zip" };

    private static bool HasValidSignature(byte[] bytes, string extension)
    {
        if (bytes.Length == 0) return false;
        if (extension is ".txt" or ".rtf") return true;
        if (extension == ".pdf") return bytes.AsSpan().StartsWith("%PDF"u8);
        if (extension is ".png") return bytes.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        if (extension is ".jpg" or ".jpeg") return bytes.AsSpan().StartsWith(new byte[] { 255, 216, 255 });
        if (extension == ".gif") return bytes.AsSpan().StartsWith("GIF"u8);
        if (extension == ".webp") return bytes.Length >= 12 && bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) && bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8);
        if (extension == ".bmp") return bytes.AsSpan().StartsWith("BM"u8);
        if (extension is ".doc" or ".xls" or ".ppt") return bytes.AsSpan().StartsWith(new byte[] { 208, 207, 17, 224, 161, 177, 26, 225 });
        if (extension is ".docx" or ".xlsx" or ".pptx" or ".zip") return bytes.AsSpan().StartsWith(new byte[] { 80, 75, 3, 4 });
        return false;
    }

    // ==================== پیوست‌ها (AppAttachment، Module="InnerLetters") ====================

    private async Task<bool> InFlowAsync(int letterId)
    {
        var letter = await Db.InnerLetters.AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == letterId && !l.IsDelete);
        if (letter == null) return false;
        if (letter.CreatorUserId == MyUserId) return true;
        return await Db.Erjas.AnyAsync(e => e.SourceId == letterId && e.ReciverUserId == MyUserId && !e.IsDelete);
    }

    /// <summary>لیست پیوست‌های نامه — فقط افراد در گردش یا مدیر</summary>
    [HttpGet("{id:int}/attachments")]
    public async Task<IActionResult> Attachments(int id, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        var pagination = new Paging.Request(skip, take);
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        if (!await InFlowAsync(id) && !await IsAdminAsync())
            return StatusCode(403, new { message = "شما در گردش این نامه نیستید." });

        var rows = await Db.AppAttachments.AsNoTracking()
            .Where(a => a.Module == Module && a.RefId == id)
            .OrderBy(a => a.Id)
            .Select(a => new { a.Id, a.FileName, a.ContentType, a.FilePath, a.Data, a.UploaderName, a.UploaderUserId, a.UploadedAt })
            .ToPageListAsync(pagination);

        var list = rows.Select(a => new LetterAttachmentDto
        {
            Id = a.Id,
            FileName = a.FileName,
            ContentType = a.ContentType,
            Size = a.FilePath is not null ? _store.Size(a.FilePath) : (long)a.Data.Length,
            UploaderName = a.UploaderName,
            UploaderUserId = a.UploaderUserId,
            UploadedAt = a.UploadedAt
        }).ToList();
        return Ok(pagination.Result(list));
    }

    /// <summary>
    /// آیا کاربر جاری اجازه افزودن پیوست به این نامه را دارد؟
    /// قاعده: «هر کاربر حاضر در گردش نامه» (فرستنده یا هر گیرنده‌ای در ارجاع‌ها) یا مدیر — هم‌راستا با نامهٔ وارده و صادره.
    /// </summary>
    private async Task<bool> CanAttachAsync(int letterId)
    {
        return await InFlowAsync(letterId) || await IsAdminAsync();
    }

    /// <summary>
    /// آپلود پیوست — بدون محدودیت تعداد؛ هر فایل حداکثر ۲۰ مگابایت؛ فایل خالی رد می‌شود.
    /// هر کاربر حاضر در گردش نامه (فرستنده/گیرنده) یا مدیر می‌تواند پیوست اضافه کند.
    /// فایل‌ها در wwwroot/uploads/innerletter/{letterId} ذخیره می‌شوند و در DB فقط اطلاعات + مسیر (FilePath) ثبت می‌شود.
    /// </summary>
    [HttpPost("{id:int}/attachments")]
    [DisableRequestSizeLimit]
    public async Task<IActionResult> UploadAttachment(int id, IFormFile file)
    {
        // Read (نه Create): گیرندهٔ در گردش معمولاً فقط مجوز خواندن ماژول را دارد
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        if (!await CanAttachAsync(id))
            return StatusCode(403, new { message = "شما در گردش این نامه نیستید و اجازه افزودن پیوست ندارید." });

        if (file == null || file.Length <= 0)
            return BadRequest(new { message = "فایل خالی است و قابل بارگذاری نیست." });
        if (file.Length > 20 * 1024 * 1024)
            return BadRequest(new { message = "حداکثر حجم هر فایل ۲۰ مگابایت است." });
        // محدودیت پسوند فایل و بررسی امضای محتوا برداشته شد — هر نوع فایلی مجاز است
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        ms.Position = 0;

        // فایل روی دیسک در wwwroot/uploads/office/innerletter/{letterId} ذخیره می‌شود
        var relPath = await _store.SaveAsync(Module, id, ms, file.FileName);

        var att = new AppAttachment
        {
            Module = Module,
            RefId = id,
            FileName = Path.GetFileName(file.FileName),
            ContentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType,
            FilePath = relPath,
            Data = Array.Empty<byte>(),
            UploaderName = await MyDisplayNameAsync(),
            UploaderUserId = MyUserId
        };
        Db.AppAttachments.Add(att);
        await Db.SaveChangesAsync();
        return Ok(new { id = att.Id, message = "پیوست بارگذاری شد." });
    }

    /// <summary>دانلود پیوست — فقط افراد در گردش یا مدیر</summary>
    [HttpGet("attachments/{attId:int}/download")]
    public async Task<IActionResult> DownloadAttachment(int attId)
    {
        var a = await Db.AppAttachments.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == attId && (x.Module == Module || x.Module == "Pishnevis"));
        if (a == null) return NotFound();
        if (a.Module == "Pishnevis")
        {
            // پیش‌نویس فقط برای صاحبش
            var owns = await Db.PishnevisLetters.AnyAsync(p => p.PishnevisId == a.RefId && p.UserId == MyUserId);
            if (!owns && !await IsAdminAsync())
                return StatusCode(403, new { message = "پیش‌نویس متعلق به شما نیست." });
        }
        else if (!await InFlowAsync(a.RefId) && !await IsAdminAsync())
        {
            var hasReadPerm = await ForbiddenUnlessAsync(Module, "Read") == null;
            if (!hasReadPerm)
                return StatusCode(403, new { message = "شما در گردش این نامه نیستید." });
        }

        // اول از دیسک خوانده می‌شود؛ در صورت نبود، به blob قدیمی در DB برمی‌گردد
        var bytes = _store.ReadBytes(a.FilePath) ?? (a.Data is { Length: > 0 } ? a.Data : null);
        if (bytes is null) return NotFound(new { message = "فایل در دسترس نیست." });
        return File(bytes, a.ContentType, a.FileName);
    }

    /// <summary>دانلود کلی همهٔ پیوست‌های نامه به صورت فایل ZIP — فقط برای نامهٔ دارای بیش از یک پیوست</summary>
    [HttpGet("{id:int}/attachments/download-zip")]
    public async Task<IActionResult> DownloadAttachmentsZip(int id)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        if (!await InFlowAsync(id) && !await IsAdminAsync())
            return StatusCode(403, new { message = "شما در گردش این نامه نیستید." });

        var attachments = await Db.AppAttachments.AsNoTracking()
            .Where(a => a.Module == Module && a.RefId == id)
            .OrderBy(a => a.Id)
            .Select(a => new { a.Id, a.FileName, a.ContentType, a.FilePath, a.Data })
            .ToListAsync();

        if (attachments.Count == 0)
            return NotFound(new { message = "پیوستی یافت نشد." });

        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            foreach (var att in attachments)
            {
                var bytes = _store.ReadBytes(att.FilePath) ?? (att.Data is { Length: > 0 } ? att.Data : null);
                if (bytes is null) continue;
                var entry = zip.CreateEntry(att.FileName);
                using var entryStream = entry.Open();
                await entryStream.WriteAsync(bytes);
            }
        }

        ms.Position = 0;
        return File(ms.ToArray(), "application/zip", $"inner-letter-{id}-attachments.zip");
    }

    /// <summary>لیست پیوست‌های پیش‌نویس کاربر جاری</summary>
    [HttpGet("pishnevis/{id:int}/attachments")]
    public async Task<IActionResult> PishnevisAttachments(int id, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        var pagination = new Paging.Request(skip, take);
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        var owns = await Db.PishnevisLetters.AnyAsync(p => p.PishnevisId == id && p.UserId == MyUserId && !p.IsDelete);
        if (!owns) return StatusCode(403, new { message = "پیش‌نویس متعلق به شما نیست." });

        var rows = await Db.AppAttachments.AsNoTracking()
            .Where(a => a.Module == "Pishnevis" && a.RefId == id)
            .OrderBy(a => a.Id)
            .Select(a => new { a.Id, a.FileName, a.ContentType, a.FilePath, a.Data, a.UploaderName, a.UploaderUserId, a.UploadedAt })
            .ToPageListAsync(pagination);

        var list = rows.Select(a => new LetterAttachmentDto
        {
            Id = a.Id, FileName = a.FileName, ContentType = a.ContentType,
            Size = a.FilePath is not null ? _store.Size(a.FilePath) : (long)a.Data.Length, UploaderName = a.UploaderName,
            UploaderUserId = a.UploaderUserId, UploadedAt = a.UploadedAt
        }).ToList();
        return Ok(pagination.Result(list));
    }

    /// <summary>
    /// آپلود پیوست روی پیش‌نویس — همان قواعد ۲۰ مگابایت/فایل خالی.
    /// فایل‌ها در wwwroot/uploads/innerletter/pishnevis/{pishnevisId} ذخیره می‌شوند.
    /// </summary>
    [HttpPost("pishnevis/{id:int}/attachments")]
    [DisableRequestSizeLimit]
    public async Task<IActionResult> UploadPishnevisAttachment(int id, IFormFile file)
    {
        if (await ForbiddenUnlessAsync(Module, "Create") is { } forbid) return forbid;
        var owns = await Db.PishnevisLetters.AnyAsync(p => p.PishnevisId == id && p.UserId == MyUserId && !p.IsDelete);
        if (!owns) return StatusCode(403, new { message = "پیش‌نویس متعلق به شما نیست." });

        if (file == null || file.Length <= 0)
            return BadRequest(new { message = "فایل خالی است و قابل بارگذاری نیست." });
        if (file.Length > 20 * 1024 * 1024)
            return BadRequest(new { message = "حداکثر حجم هر فایل ۲۰ مگابایت است." });
        // محدودیت پسوند فایل و بررسی امضای محتوا برداشته شد — هر نوع فایلی مجاز است
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        ms.Position = 0;

        // فایل پیش‌نویس زیر wwwroot/uploads/office/innerletter/pishnevis/{pishnevisId} ذخیره می‌شود
        var relPath = await _store.SaveAsync(Module, id, ms, file.FileName, "pishnevis");

        Db.AppAttachments.Add(new AppAttachment
        {
            Module = "Pishnevis",
            RefId = id,
            FileName = Path.GetFileName(file.FileName),
            ContentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType,
            FilePath = relPath,
            Data = Array.Empty<byte>(),
            UploaderName = await MyDisplayNameAsync(),
            UploaderUserId = MyUserId
        });
        await Db.SaveChangesAsync();
        return Ok(new { message = "پیوست بارگذاری شد." });
    }

    /// <summary>حذف پیوست — فقط آپلودکننده یا مدیر</summary>
    [HttpDelete("attachments/{attId:int}")]
    public async Task<IActionResult> DeleteAttachment(int attId)
    {
        var a = await Db.AppAttachments.FirstOrDefaultAsync(x => x.Id == attId && (x.Module == Module || x.Module == "Pishnevis"));
        if (a == null) return NotFound();
        if (a.UploaderUserId != MyUserId && !await IsAdminAsync())
            return StatusCode(403, new { message = "فقط بارگذارنده یا مدیر می‌تواند پیوست را حذف کند." });

        // فایل روی دیسک هم حذف می‌شود
        _store.Delete(a.FilePath);
        Db.AppAttachments.Remove(a);
        await Db.SaveChangesAsync();
        return Ok(new { message = "پیوست حذف شد." });
    }
}
