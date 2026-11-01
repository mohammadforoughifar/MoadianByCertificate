using Inventory.Api.Data;
using Inventory.Api.Services;
using Inventory.Api.Services.Office;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.IO.Compression;

namespace Inventory.Api.Controllers;

/// <summary>
/// کنترلر مدیریت کامل نامه‌های وارده (Incoming Letters Subsystem)
/// </summary>
[ApiController]
[Route("api/incoming-letters")]
[Authorize]
public class IncomingLettersController : RbacControllerBase
{
    private const string Module = "IncomingLetters";

    private async Task<bool> IsAdminAsync() => await HasAsync(Module, "Delete");

    // ثبت نامه وارده و رزرو شماره فقط از مسیر دبیرخانه انجام می‌شود.
    private async Task<bool> HasDabirkhaneAsync() =>
        await HasAsync(Module, "Dabirkhane") || await IsAdminAsync();

    /// <summary>
    /// آیا کاربر جاری مجاز به دیدن «پیوست‌های این نامهٔ وارده» است؟
    ///
    /// چرا لازم است؟ مجوز عمومی «IncomingLetters.Read» فقط یعنی کاربر به *ماژول*
    /// نامه وارده دسترسی دارد، نه به *هر* نامهٔ آن. بدون این بررسی، endpointهای
    /// پیوست با پیمایش شناسه (attId / letterId) هر فایلی را لو می‌دادند — از جمله
    /// پیوست نامه‌های Mahramanegi=3 (سرّی).
    ///
    /// قاعده دقیقاً همان قاعدهٔ «جزئیات نامه» در IncomingLetterService.GetDetailAsync
    /// و «درخت گردش» در ErjaService.GetGardeshTreeAsync است تا هر سه یک سطح محافظت
    /// داشته باشند:  سازندهٔ نامه | حاضر در گردش (ارجاع/هامش) | مدیر
    ///
    /// نامهٔ حذف‌شده یا ناموجود → false (نه استثنا)، تا endpoint همان 404 قبلی را بدهد.
    /// </summary>
    private async Task<bool> CanAccessLetterAsync(int letterId)
    {
        if (letterId <= 0) return false;

        var creatorUserId = await Db.IncomingLetters.AsNoTracking()
            .Where(x => x.Id == letterId && !x.IsDelete)
            .Select(x => (int?)x.CreateUserId)
            .FirstOrDefaultAsync();
        if (creatorUserId == null) return false;

        if (creatorUserId.Value == MyUserId) return true;
        if (await IsAdminAsync()) return true;

        return await Db.Erjas.AnyAsync(e =>
            e.SourceId == letterId && e.ReciverUserId == MyUserId && !e.IsDelete);
    }

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
    private readonly IIncomingLetterService _letters;
    private readonly IErjaService _erja;
    private readonly ILetterGroupService _groups;
    private readonly IArchiveService _archive;
    private readonly FileStore _store;

    public IncomingLettersController(IIncomingLetterService letters, IErjaService erja, ILetterGroupService groups, IArchiveService archive, FileStore store, AppDbContext db)
        : base(db)
    {
        _letters = letters;
        _erja = erja;
        _groups = groups;
        _archive = archive;
        _store = store;
    }

    [HttpGet("inbox")]
    public async Task<IActionResult> GetInbox([FromQuery] string? search, [FromQuery] bool? unreadOnly, [FromQuery] int page = 1, [FromQuery] int pageSize = 15, [FromQuery] bool? starredOnly = null, [FromQuery] int? skip = null, [FromQuery] int? take = null,
        [FromQuery] bool? attachOnly = null, [FromQuery] bool? workOrderOnly = null)
    {
        page = Paging.ToPage(skip, take, page);
        pageSize = Paging.ToPageSize(take, pageSize);
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        return Ok(await _letters.GetInboxAsync(MyUserId, search, unreadOnly, page, pageSize, starredOnly, attachOnly, workOrderOnly));
    }

    [HttpGet("archive")]
    public async Task<IActionResult> GetArchive([FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 15, [FromQuery] int? skip = null, [FromQuery] int? take = null)
    {
        page = Paging.ToPage(skip, take, page);
        pageSize = Paging.ToPageSize(take, pageSize);
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        return Ok(await _letters.GetArchiveAsync(MyUserId, search, page, pageSize));
    }

    [HttpGet("sent")]
    public async Task<IActionResult> GetSent([FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 15, [FromQuery] int? skip = null, [FromQuery] int? take = null,
        [FromQuery] bool? starredOnly = null, [FromQuery] bool? attachOnly = null)
    {
        page = Paging.ToPage(skip, take, page);
        pageSize = Paging.ToPageSize(take, pageSize);
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        return Ok(await _letters.GetSentAsync(MyUserId, search, page, pageSize, starredOnly, attachOnly));
    }

    [HttpGet("stats")]
    public async Task<IActionResult> GetStats()
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        return Ok(await _letters.GetStatsAsync(MyUserId));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetDetail(int id)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        try
        {
            // پرچمِ «دبیرخانه» هم پاس می‌شود تا همکارِ دبیرخانه بتواند نامهٔ ثبت‌شده توسط
            // دیگری را (حتی پس از رویتِ گیرندگان) ویرایش کند.
            var detail = await _letters.GetDetailAsync(id, MyUserId, await IsAdminAsync(), await HasDabirkhaneAsync());
            if (detail == null) return NotFound(new { message = "نامه وارده یافت نشد." });
            return Ok(detail);
        }
        catch (UnauthorizedAccessException ex)
        {
            // کاربر در گردش این نامه نیست — هم‌راستا با Edit و Delete همین کنترلر
            return StatusCode(403, new { message = ex.Message });
        }
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] AddIncomingLetterDto dto)
    {
        if (!await HasDabirkhaneAsync()) return StatusCode(403, new { message = "ثبت نامه وارده فقط از طریق دبیرخانه مجاز است." });
        try
        {
            var id = await _letters.AddAsync(dto, MyUserId);
            return Ok(new { id, message = "نامه وارده با موفقیت ثبت شد." });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message, detail = "ثبت نامه وارده انجام نشد؛ اسکیمای دیتابیس و اطلاعات اجباری فرم را بررسی کنید." });
        }
    }

    /// <summary>
    /// نهایی‌سازی نامهٔ وارده پس از اتمام آپلود پیوست‌ها و ارسال اعلان گیرندگان اولیه.
    /// کلاینت این endpoint را فقط وقتی صدا می‌زند که ثبت نامه و «همهٔ» آپلودها موفق
    /// شده باشند؛ اگر یک آپلود خطا بدهد، Finalize و اعلان انجام نمی‌شود.
    /// فراخوان دوبارهٔ آن idempotent است (اعلان تکراری ساخته نمی‌شود).
    /// </summary>
    [HttpPost("{id:int}/finalize")]
    public async Task<IActionResult> Finalize(int id)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        try
        {
            var notified = await _letters.FinalizeAsync(id, MyUserId, await IsAdminAsync());
            return Ok(new
            {
                notified,
                message = notified > 0
                    ? $"نامه نهایی شد و اعلان برای {notified} گیرنده ارسال شد."
                    : "اعلان تازه‌ای ارسال نشد (قبلاً ارسال شده یا گیرندهٔ اولیه‌ای وجود ندارد)."
            });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Edit(int id, [FromBody] EditIncomingLetterDto dto)
    {
        // ویرایش با مجوز ثبت یا دبیرخانهٔ همین ماژول مجاز است؛ خودِ سرویس هم دوباره
        // بررسی می‌کند که کاربر ثبت‌کننده، مدیر یا کاربرِ دبیرخانه باشد.
        if (!await HasAsync(Module, "Create") && !await HasAsync(Module, "Dabirkhane"))
            return StatusCode(403, new { message = "شما به این بخش دسترسی ندارید." });
        try
        {
            var notified = await _letters.EditAsync(id, dto, MyUserId, await IsAdminAsync(), await HasDabirkhaneAsync());
            return Ok(new { message = "نامه وارده ویرایش شد.", notified });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        if (await ForbiddenUnlessAsync(Module, "Delete") is { } forbid) return forbid;
        try
        {
            await _letters.DeleteAsync(id, MyUserId, await IsAdminAsync());
            return Ok(new { message = "نامه وارده حذف شد." });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { message = ex.Message });
        }
    }

    [HttpPost("erja")]
    public async Task<IActionResult> Erja([FromBody] AddErjaDto dto)
    {
        if (await ForbiddenUnlessAsync(Module, "Erja") is { } forbid) return forbid;
        // مسئول دبیرخانه می‌تواند نامه را به خودش هم ارجاع دهد (علاوه بر دیگران).
        await _erja.AddErjaAsync(dto, MyUserId, await MyDisplayNameAsync(), await HasDabirkhaneAsync());
        return Ok(new { message = "ارجاع نامه انجام شد." });
    }

    [HttpGet("{id:int}/gardesh")]
    public async Task<IActionResult> Gardesh(int id, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        return Ok(await Paging.ResultAsync(async pagination => await _erja.GetGardeshTreeAsync(id, MyUserId, await IsAdminAsync(), pagination), skip, take));
    }

    [HttpPost("erja/{erjaId:int}/answer")]
    public async Task<IActionResult> AnswerErja(int erjaId, [FromBody] AnswerErjaDto dto)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        try
        {
            await _erja.AnswerAsync(erjaId, dto, MyUserId, await MyDisplayNameAsync());
            return Ok(new { message = "پاسخ ارجاع ثبت شد." });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message, detail = "پاسخ ثبت نشد؛ بررسی کنید ارجاع برای کاربر جاری باشد و متن پاسخ خالی نباشد." });
        }
    }

    [HttpPost("erja/{erjaId:int}/read")]
    public async Task<IActionResult> MarkRead(int erjaId)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        await _erja.MarkReadAsync(erjaId, MyUserId);
        return Ok(new { message = "نامه خوانده شد." });
    }

    [HttpPost("erja/{erjaId:int}/neshan")]
    public async Task<IActionResult> ToggleNeshan(int erjaId)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        var isNeshan = await _erja.ToggleNeshanAsync(erjaId, MyUserId);
        return Ok(new { isNeshan });
    }

    [HttpPost("{id:int}/neshan")]
    public async Task<IActionResult> ToggleLetterNeshan(int id)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        // پیش‌تر هیچ بررسی‌ای نبود: هر کاربری با مجوز «مشاهدهٔ ماژول» می‌توانست با
        // پیمایش شناسه، نشانِ نامهٔ دیگران را عوض کند.
        if (!await CanAccessLetterAsync(id))
            return StatusCode(403, new { message = "شما در گردش این نامه نیستید." });
        try
        {
            var isNeshan = await _letters.ToggleLetterNeshanAsync(id, MyUserId);
            return Ok(new { isNeshan });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { message = ex.Message });
        }
    }

    [HttpPost("{id:int}/bayegani")]
    public async Task<IActionResult> ToggleLetterBayegani(int id)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        var letter = await Db.IncomingLetters.FirstOrDefaultAsync(x => x.Id == id && !x.IsDelete);
        if (letter == null) return NotFound(new { message = "نامه وارده پیدا نشد." });
        if (letter.CreateUserId != MyUserId && !await IsAdminAsync() && !await HasDabirkhaneAsync()) return StatusCode(403, new { message = "فقط ثبت‌کننده، دبیرخانه یا مدیر می‌تواند نامه را بایگانی کند." });
        letter.IsBayegani = !letter.IsBayegani;

        // رکورد درخت بایگانی هم هم‌زمان ساخته/حذف می‌شود؛ وگرنه نامه از دریافتی خارج
        // می‌شود ولی در ریل «بایگانی» (که از درخت خوانده می‌شود) هیچ اثری نمی‌گذارد.
        // کلید مشترک IncomingLetter با LetterSource همان Id است (SourceId وجود ندارد)
        var myErja = await Db.Erjas.AsNoTracking()
            .Where(e => e.SourceId == letter.Id && e.ReciverUserId == MyUserId && !e.IsDelete)
            .OrderByDescending(e => e.ErjaId)
            .Select(e => new { e.ErjaId })
            .FirstOrDefaultAsync();
        if (myErja != null)
        {
            var row = await Db.LetterBayeganis.FirstOrDefaultAsync(b =>
                b.ErjaId == myErja.ErjaId && b.UserId == MyUserId && !b.IsFolder && !b.IsDelete);
            if (letter.IsBayegani)
            {
                if (row == null)
                    Db.LetterBayeganis.Add(new LetterBayegani
                    {
                        Title = (letter.Title ?? "نامه").Length > 200 ? letter.Title![..200] : (letter.Title ?? "نامه"),
                        ErjaId = myErja.ErjaId,
                        // شناسهٔ نامه هم ثبت می‌شود تا هنگام خروج از بایگانی، گره درختی
                        // با اطمینان پیدا و بسته شود (پیش‌تر فقط ErjaId ذخیره می‌شد).
                        LetterId = letter.Id,
                        ParentId = 0,
                        UserId = MyUserId,
                        TypeBayegani = 1,
                        IsFolder = false,
                        IsDelete = false
                    });
            }
            else if (row != null)
            {
                row.IsDelete = true;
            }

            // وضعیت ارجاع هم همان ما بماند تا فهرست دریافتی/بایگانی هم‌خوان باشد
            var erja = await Db.Erjas.FirstOrDefaultAsync(e => e.ErjaId == myErja.ErjaId);
            if (erja != null) erja.IsBayegani = letter.IsBayegani;
        }

        // خروج از بایگانی: هر گره درختیِ همین نامه/ارجاع برای همین کاربر بسته می‌شود.
        // پیش‌تر فقط رکوردی که با ErjaId ساخته شده بود بسته می‌شد و گره‌های کلیدخورده با
        // شناسهٔ نامه (ثبت‌کننده/دبیرخانه) در درخت می‌ماندند — نامه از «دریافتی/ارسالی»
        // بیرون می‌رفت ولی در بایگانی می‌ماند (یا برعکس، گم می‌شد).
        if (!letter.IsBayegani)
        {
            var letterNodes = await Db.LetterBayeganis
                .Where(b => b.UserId == MyUserId && !b.IsFolder && !b.IsDelete && b.LetterId == letter.Id)
                .ToListAsync();
            foreach (var node in letterNodes) node.IsDelete = true;
        }

        await Db.SaveChangesAsync();
        return Ok(new { isBayegani = letter.IsBayegani });
    }

    /// <summary>
    /// خروج کامل نامهٔ وارده از بایگانی کاربر — گره درختی و همهٔ پرچم‌های بایگانیِ
    /// همین کاربر برای این نامه آزاد می‌شود تا نامه بی‌چون‌وچرا به فهرست برگردد.
    /// </summary>
    [HttpPost("{id:int}/unarchive")]
    public async Task<IActionResult> UnarchiveLetter(int id, [FromServices] IArchiveService archive)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        var closed = await archive.UnarchiveIncomingLetterAsync(id, MyUserId);
        return Ok(new { isBayegani = false, closedNodes = closed });
    }

    [HttpPost("erja/{erjaId:int}/bayegani")]
    public async Task<IActionResult> ToggleBayegani(int erjaId)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        var isBayegani = await _erja.ToggleBayeganiAsync(erjaId, MyUserId);
        return Ok(new { isBayegani });
    }

    public class BatchBayeganiDto { public List<int> ErjaIds { get; set; } = new(); public string? Description { get; set; } }

    [HttpPost("erja/batch-bayegani")]
    public async Task<IActionResult> BatchBayegani([FromBody] BatchBayeganiDto dto)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        var count = await _erja.BatchBayeganiAsync(dto.ErjaIds, MyUserId, dto.Description);
        return Ok(new { count, message = $"{count} نامه با موفقیت بایگانی شد." });
    }

    // ==================== گیرندگان، گروه‌ها و عملگرهای ارجاع ====================
    // فرم‌های «ثبت نامه وارده» و «ارجاع نامه وارده» همان کمبوی مشترک
    // (LetterPickCombo) نامه داخلی/صادره را به کار می‌برند و به این سه
    // سرویس نیاز دارند؛ نبودشان باعث خطای کامپایل سمت کلاینت شده بود.

    /// <summary>لیست کاربران فعال برای انتخاب گیرنده ارجاع</summary>
    [HttpGet("recivers")]
    public async Task<IActionResult> Recivers([FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        // مسئول دبیرخانه خودش هم در فهرست گیرندگان دیده می‌شود تا بتواند به خودش
        // ارجاع دهد؛ برای بقیهٔ کاربران مثل قبل حذف می‌شود.
        var canSelf = await HasDabirkhaneAsync();
        var meId = MyUserId;
        const string selfSuffix = " (خودم — دبیرخانه)";
        var users = Db.Users.AsNoTracking()
            .Where(u => u.IsActive && (u.Id != meId || canSelf))
            .OrderBy(u => u.FirstName).ThenBy(u => u.Username)
            .Select(u => new LetterReciverDto
            {
                UserId = u.Id,
                FullName = (string.IsNullOrEmpty(u.FirstName + u.LastName)
                    ? u.Username
                    : (u.FirstName + " " + u.LastName).Trim())
                    + (u.Id == meId ? selfSuffix : "")
            });
        return Ok(await Paging.ResultAsync(users, skip, take));
    }

    /// <summary>گروه‌های گیرندگان فعال — برای انتخاب گروهی در ارجاع</summary>
    [HttpGet("groups")]
    public async Task<IActionResult> Groups([FromQuery] bool withMembers = true, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        return Ok(await Paging.ResultAsync(async pagination => await _groups.GetAllAsync(withMembers, pagination: pagination), skip, take));
    }

    /// <summary>عملگرهای ارجاع (جهت اقدام، جهت اطلاع و …)</summary>
    [HttpGet("amalgars")]
    public async Task<IActionResult> Amalgars([FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        // باگ B10: این سه endpoint یک گروه‌اند (recivers / groups / amalgars) ولی فقط
        // دوتای اول گارد RBAC داشتند. این یکی بدون هیچ بررسی بود، پس هر کاربر
        // لاگین‌کرده — حتی بدون مجوز نامه وارده — می‌توانست فهرست عملگرهای ارجاع
        // (ساختار چارت سازمانی) را بخواند.
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        return Ok(await Paging.ResultAsync(async pagination => await _erja.GetAmalgarsAsync(pagination: pagination), skip, take));
    }

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

    [HttpGet("{id:int}/attachments")]
    public async Task<IActionResult> Attachments(int id, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        var pagination = new Paging.Request(skip, take);
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        // مجوز ماژول به‌تنهایی کافی نیست — باید در گردشِ همین نامه هم باشد
        if (!await CanAccessLetterAsync(id))
            return StatusCode(403, new { message = "شما در گردش این نامه نیستید و اجازهٔ دیدن پیوست‌های آن را ندارید." });

        // ⚠ ستون Data (بایت‌های فایل) عمداً select نمی‌شود؛ فقط طول آن در SQL حساب
        // می‌شود. قبلاً فهرست پیوست‌ها همهٔ فایل‌های یک نامه را به حافظه می‌آورد.
        var rows = await Db.AppAttachments.AsNoTracking()
            .Where(a => a.Module == Module && a.RefId == id)
            .OrderBy(a => a.Id)
            .Select(a => new
            {
                a.Id, a.FileName, a.ContentType, a.FilePath,
                DataLength = a.Data == null ? 0 : a.Data.Length,
                a.UploaderName, a.UploaderUserId, a.UploadedAt
            })
            .ToPageListAsync(pagination);

        var list = rows.Select(a => new LetterAttachmentDto
        {
            Id = a.Id,
            FileName = a.FileName,
            ContentType = a.ContentType,
            Size = a.FilePath is not null ? _store.Size(a.FilePath) : (long)a.DataLength,
            UploaderName = a.UploaderName,
            UploaderUserId = a.UploaderUserId,
            UploadedAt = a.UploadedAt
        }).ToList();
        return Ok(pagination.Result(list));
    }

    [HttpPost("{id:int}/attachments")]
    [DisableRequestSizeLimit]
    public async Task<IActionResult> UploadAttachment(int id, IFormFile file)
    {
        if (!await CanAccessLetterAsync(id)) return StatusCode(403, new { message = "فقط افراد حاضر در گردش نامه می‌توانند پیوست اضافه کنند." });
        if (file == null || file.Length <= 0)
            return BadRequest(new { message = "فایل خالی است." });
        if (file.Length > 20 * 1024 * 1024)
            return BadRequest(new { message = "حداکثر حجم هر فایل ۲۰ مگابایت است." });

        // سقف مجموع پیوست‌های یک نامه: ۱۰۰ مگابایت — با احتساب پیوست‌های قبلیِ همین نامه.
        // حجم پیوست‌های قبلی بدون خواندن بایت‌ها محاسبه می‌شود (فقط طول ستون در SQL).
        var existingSize = (await Db.AppAttachments.AsNoTracking()
                .Where(a => a.Module == Module && a.RefId == id)
                .Select(a => new { a.FilePath, DataLength = a.Data == null ? 0 : a.Data.Length })
                .ToListAsync())
            .Sum(a => a.FilePath is not null ? _store.Size(a.FilePath) : (long)a.DataLength);
        if (existingSize + file.Length > 100L * 1024 * 1024)
            return BadRequest(new { message = "حداکثر حجم مجموع پیوست‌های هر نامه ۱۰۰ مگابایت است." });

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        ms.Position = 0;

        // ذخیره فیزیکی زیر wwwroot/uploads/IncomingLetters/{id}/{guid}_{name}
        var relPath = await _store.SaveAsync("IncomingLetters", id, ms, file.FileName);

        var att = new AppAttachment
        {
            Module = "IncomingLetters",
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

    [HttpGet("attachments/{attId:int}/download")]
    public async Task<IActionResult> DownloadAttachment(int attId)
    {
        // باگ قبلی: این endpoint «هیچ» بررسی نداشت — نه مجوز ماژول، نه مالکیت.
        // با پیمایش attId هر پیوستی (از جمله پیوست نامهٔ سرّی) قابل دانلود بود (IDOR).
        // نمونهٔ همسان در EmailController.DownloadAttachment این گارد را دارد.
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;

        var a = await Db.AppAttachments.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == attId && x.Module == Module);
        if (a == null) return NotFound();

        // دسترسی به «فایل» از دسترسی به «نامه» مشتق می‌شود، نه از بارگذارندهٔ فایل
        if (!await CanAccessLetterAsync(a.RefId))
            return StatusCode(403, new { message = "شما در گردش این نامه نیستید و اجازهٔ دانلود پیوست را ندارید." });

        var bytes = (a.FilePath is not null ? _store.ReadBytes(a.FilePath) : null) ?? (a.Data is { Length: > 0 } ? a.Data : null);
        if (bytes == null) return NotFound(new { message = "فایل پیوست پیدا نشد." });
        return File(bytes, a.ContentType, a.FileName);
    }

    /// <summary>دانلود کلی همهٔ پیوست‌های نامه وارده به صورت فایل ZIP</summary>
    [HttpGet("{id:int}/attachments/download-zip")]
    public async Task<IActionResult> DownloadAttachmentsZip(int id)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        if (!await CanAccessLetterAsync(id))
            return StatusCode(403, new { message = "شما در گردش این نامه نیستید." });

        // فهرست بدون ستون Data؛ فایلِ رکوردهای قدیمی (که داخل دیتابیس است) تک‌تک
        // و فقط هنگام نوشتن در ZIP خوانده می‌شود — پیش‌تر همهٔ بایت‌ها یک‌جا در
        // حافظه بار می‌شدند و همان‌ها دوباره داخل ZIP کپی می‌شدند.
        var attachments = await Db.AppAttachments.AsNoTracking()
            .Where(a => a.Module == Module && a.RefId == id)
            .OrderBy(a => a.Id)
            .Select(a => new { a.Id, a.FileName, a.ContentType, a.FilePath })
            .ToListAsync();

        if (attachments.Count == 0)
            return NotFound(new { message = "پیوستی یافت نشد." });

        using var ms = new MemoryStream();
        // نام تکراری در ZIP: اگر دو پیوست همنام باشند، ورودی دوم با پسوند شماره‌دار
        // نوشته می‌شود؛ وگرنه بعضی برنامه‌های فشرده‌ساز هنگام استخراج یکی را حذف می‌کنند.
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            foreach (var att in attachments)
            {
                var bytes = att.FilePath is not null
                    ? _store.ReadBytes(att.FilePath)
                    : await Db.AppAttachments.AsNoTracking()
                        .Where(x => x.Id == att.Id)
                        .Select(x => x.Data)
                        .FirstOrDefaultAsync();
                if (bytes is not { Length: > 0 }) continue;

                var entryName = string.IsNullOrWhiteSpace(att.FileName) ? $"attachment-{att.Id}" : att.FileName;
                if (!usedNames.Add(entryName))
                {
                    var dot = entryName.LastIndexOf('.');
                    var stem = dot > 0 ? entryName[..dot] : entryName;
                    var ext = dot > 0 ? entryName[dot..] : "";
                    var n = 2;
                    while (!usedNames.Add($"{stem} ({n}){ext}")) n++;
                    entryName = $"{stem} ({n}){ext}";
                }

                var entry = zip.CreateEntry(entryName);
                using var entryStream = entry.Open();
                await entryStream.WriteAsync(bytes);
            }
        }

        ms.Position = 0;
        return File(ms.ToArray(), "application/zip", $"incoming-letter-{id}-attachments.zip");
    }

    [HttpDelete("attachments/{attId:int}")]
    public async Task<IActionResult> DeleteAttachment(int attId)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;

        var a = await Db.AppAttachments.FirstOrDefaultAsync(x => x.Id == attId && x.Module == Module);
        if (a == null) return NotFound();

        // پیش از قاعدهٔ «بارگذارنده یا مدیر»، کاربر باید اساساً به این نامه دسترسی داشته باشد
        if (!await CanAccessLetterAsync(a.RefId))
            return StatusCode(403, new { message = "شما در گردش این نامه نیستید و اجازهٔ حذف پیوست را ندارید." });

        if (a.UploaderUserId != MyUserId && !await IsAdminAsync())
            return StatusCode(403, new { message = "فقط بارگذارنده یا مدیر می‌تواند پیوست را حذف کند." });

        _store.Delete(a.FilePath);
        Db.AppAttachments.Remove(a);
        await Db.SaveChangesAsync();
        return Ok(new { message = "پیوست حذف شد." });
    }

    [HttpGet("pick")]
    public async Task<IActionResult> Pick([FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 15, [FromQuery] int? skip = null, [FromQuery] int? take = null)
    {
        page = Paging.ToPage(skip, take, page);
        pageSize = Paging.ToPageSize(take, pageSize);
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        return Ok(await _letters.PickListAsync(MyUserId, search, page, pageSize));
    }

    [HttpGet("reservations")]
    public async Task<IActionResult> GetReservations([FromQuery] int typeForm = 3, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        return Ok(await Paging.ResultAsync(async pagination => await _letters.GetReservationsAsync(MyUserId, typeForm, pagination: pagination), skip, take));
    }

    [HttpPost("reserve-number")]
    public async Task<IActionResult> ReserveNumber([FromBody] ReserveNumberRequestDto dto)
    {
        if (!await HasDabirkhaneAsync()) return StatusCode(403, new { message = "رزرو شماره نامه فقط برای دبیرخانه مجاز است." });
        var list = await _letters.ReserveNumberAsync(MyUserId, dto.TypeForm, dto.Count);
        return Ok(list);
    }
}
