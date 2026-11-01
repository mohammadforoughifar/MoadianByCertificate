using Inventory.Api.Data;
using Inventory.Api.Services;
using Inventory.Api.Services.Ai;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Inventory.Api.Controllers;

/// <summary>
/// هوش مصنوعی فروغ آریا — دستیار مرکزی (۱ تا ۵)، هوش نامه‌ها (۶ تا ۱۱ + پاسخ‌نویسی و پیشنهاد گیرنده)،
/// منشی کارمندی (۲۲) و اقدام‌های اجرایی با تأیید (مرخصی، مأموریت، ارجاع، تیکت، گزارش‌کار، پیش‌نویس نامه).
/// ماژول دسترسی: AiAssistant (Use) + مجوز ماژول مربوطه برای هوش نامه‌ها.
/// </summary>
[Route("api/ai")]
public class AiController : RbacControllerBase
{
    private const string Module = "AiAssistant";

    private readonly IAiAgentService _agent;
    private readonly ILetterAiService _letters;
    private readonly AiConversationService _conversations;
    private readonly IAiChatClient _chat;
    private readonly AiBriefingService _briefing;
    private readonly AiDigestService _digest;
    private readonly AiFeedbackService _feedback;
    private readonly AiAuditService _audit;
    private readonly AiReportService _reports;
    private readonly IMessengerService _messenger;
    private readonly AiOptions _options;

    public AiController(
        AppDbContext db,
        IAiAgentService agent,
        ILetterAiService letters,
        AiConversationService conversations,
        IAiChatClient chat,
        AiBriefingService briefing,
        AiDigestService digest,
        AiFeedbackService feedback,
        AiAuditService audit,
        AiReportService reports,
        IMessengerService messenger,
        IOptions<AiOptions> options)
        : base(db)
    {
        _agent = agent;
        _letters = letters;
        _conversations = conversations;
        _chat = chat;
        _briefing = briefing;
        _digest = digest;
        _feedback = feedback;
        _audit = audit;
        _reports = reports;
        _messenger = messenger;
        _options = options.Value;
    }

    /// <summary>وضعیت دستیار و اتصال به مدل.</summary>
    [HttpGet("health")]
    public async Task<IActionResult> Health(CancellationToken ct)
    {
        if (await ForbiddenUnlessAsync(Module, "Use") is { } forbidden) return forbidden;
        var dto = new AiHealthDto
        {
            Enabled = _options.Enabled,
            ChatModel = _options.ChatModel,
            // وضعیت «تبدیل ویس پیام‌رسان به متن»: برای نمایش در تنظیمات/عیب‌یابی
            TranscriptionConfigured = _options.TranscriptionReady,
            TranscriptionModel = string.IsNullOrWhiteSpace(_options.TranscriptionModel) ? null : _options.TranscriptionModel
        };
        if (_options.Enabled)
        {
            var (ok, error) = await _chat.CheckHealthAsync(ct);
            dto.ModelReachable = ok;
            dto.Error = error;
        }
        return Ok(dto);
    }

    /// <summary>گفتگو با دستیار.</summary>
    [HttpPost("chat")]
    public async Task<IActionResult> Chat([FromBody] AiChatRequest req, CancellationToken ct)
    {
        if (await ForbiddenUnlessAsync(Module, "Use") is { } forbidden) return forbidden;
        var isAdmin = await HasAsync("InnerLetters", "ViewAll");
        var displayName = string.IsNullOrWhiteSpace(MyUsername) ? "همکار" : MyUsername;
        // نام نمایشی واقعی از پروفایل
        var me = await Db.Users.FindAsync(new object?[] { MyUserId }, ct);
        if (me != null)
        {
            var full = ((me.FirstName ?? "") + " " + (me.LastName ?? "")).Trim();
            if (full != "") displayName = full;
        }
        var result = await _agent.ChatAsync(MyUserId, displayName, isAdmin, req.ConversationId, req.Message, "web", ct);
        return Ok(result);
    }

    /// <summary>فهرست گفتگوهای من.</summary>
    [HttpGet("conversations")]
    public async Task<IActionResult> Conversations([FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Module, "Use") is { } forbidden) return forbidden;
        return Ok(await Paging.ResultAsync(async pagination => await _conversations.ListAsync(MyUserId, pagination: pagination), skip, take));
    }

    /// <summary>یک گفتگو با پیام‌ها.</summary>
    [HttpGet("conversations/{id:int}")]
    public async Task<IActionResult> Conversation(int id)
    {
        if (await ForbiddenUnlessAsync(Module, "Use") is { } forbidden) return forbidden;
        var conv = await _conversations.GetAsync(MyUserId, id);
        return conv == null ? NotFound(new { message = "گفتگو یافت نشد." }) : Ok(conv);
    }

    /// <summary>حذف (بایگانی) گفتگو.</summary>
    [HttpDelete("conversations/{id:int}")]
    public async Task<IActionResult> DeleteConversation(int id)
    {
        if (await ForbiddenUnlessAsync(Module, "Use") is { } forbidden) return forbidden;
        await _conversations.DeleteAsync(MyUserId, id);
        return Ok(new { ok = true });
    }

    // ==================== هوش نامه‌ها ====================

    /// <summary>۶) پیش‌نویس هوشمند نامه.</summary>
    [HttpPost("letters/draft")]
    public async Task<IActionResult> DraftLetter([FromBody] AiLetterDraftRequest req, CancellationToken ct)
    {
        if (await ForbiddenUnlessAsync(Module, "Use") is { } forbidden) return forbidden;
        if (await ForbiddenUnlessAsync("InnerLetters", "Create") is { } noCreate) return noCreate;
        return Ok(await _letters.DraftAsync(req, ct));
    }

    /// <summary>۷) خلاصه نامه + وضعیت گردش.</summary>
    [HttpPost("letters/{id:int}/summarize")]
    public async Task<IActionResult> SummarizeLetter(int id, CancellationToken ct)
    {
        if (await ForbiddenUnlessAsync(Module, "Use") is { } forbidden) return forbidden;
        if (await ForbiddenUnlessAsync("InnerLetters", "Read") is { } noRead) return noRead;
        var result = await _letters.SummarizeAsync(id, MyUserId, await HasAsync("InnerLetters", "ViewAll"), ct);
        return result == null ? NotFound(new { message = "نامه یافت نشد یا دسترسی ندارید." }) : Ok(result);
    }

    /// <summary>۸) پیشنهاد ارجاع هوشمند.</summary>
    [HttpPost("letters/{id:int}/suggest-referral")]
    public async Task<IActionResult> SuggestReferral(int id, CancellationToken ct)
    {
        if (await ForbiddenUnlessAsync(Module, "Use") is { } forbidden) return forbidden;
        if (await ForbiddenUnlessAsync("InnerLetters", "Read") is { } noRead) return noRead;
        var result = await _letters.SuggestReferralAsync(id, MyUserId, await HasAsync("InnerLetters", "ViewAll"), ct);
        return result == null ? NotFound(new { message = "نامه یافت نشد یا دسترسی ندارید." }) : Ok(result);
    }

    /// <summary>۹) استخراج اقدام‌ها و مهلت‌ها از نامه.</summary>
    [HttpPost("letters/{id:int}/extract-tasks")]
    public async Task<IActionResult> ExtractTasks(int id, CancellationToken ct)
    {
        if (await ForbiddenUnlessAsync(Module, "Use") is { } forbidden) return forbidden;
        if (await ForbiddenUnlessAsync("InnerLetters", "Read") is { } noRead) return noRead;
        var result = await _letters.ExtractTasksAsync(id, MyUserId, await HasAsync("InnerLetters", "ViewAll"), ct);
        return result == null ? NotFound(new { message = "نامه یافت نشد یا دسترسی ندارید." }) : Ok(result);
    }

    /// <summary>۱۱) دسته‌بندی و اولویت نامه.</summary>
    [HttpPost("letters/{id:int}/categorize")]
    public async Task<IActionResult> CategorizeLetter(int id, CancellationToken ct)
    {
        if (await ForbiddenUnlessAsync(Module, "Use") is { } forbidden) return forbidden;
        if (await ForbiddenUnlessAsync("InnerLetters", "Read") is { } noRead) return noRead;
        var result = await _letters.CategorizeAsync(id, MyUserId, await HasAsync("InnerLetters", "ViewAll"), ct);
        return result == null ? NotFound(new { message = "نامه یافت نشد یا دسترسی ندارید." }) : Ok(result);
    }

    /// <summary>پاسخ‌نویسی هوشمند: پیش‌نویس پاسخ آماده برای باز شدن در فرم نامه.</summary>
    [HttpPost("letters/{id:int}/draft-reply")]
    public async Task<IActionResult> DraftReply(int id, [FromBody] AiReplyHintRequest? req, CancellationToken ct)
    {
        if (await ForbiddenUnlessAsync(Module, "Use") is { } forbidden) return forbidden;
        if (await ForbiddenUnlessAsync("InnerLetters", "Read") is { } noRead) return noRead;
        var result = await _letters.DraftReplyAsync(id, MyUserId, await HasAsync("InnerLetters", "ViewAll"), req?.Hint, ct);
        return result == null ? NotFound(new { message = "نامه یافت نشد یا دسترسی ندارید." }) : Ok(result);
    }

    /// <summary>پیشنهاد گیرنده هنگام نوشتن نامه جدید (از روی موضوع و متن).</summary>
    [HttpPost("letters/suggest-receivers")]
    public async Task<IActionResult> SuggestReceivers([FromBody] AiReceiversSuggestRequest? req, CancellationToken ct)
    {
        if (await ForbiddenUnlessAsync(Module, "Use") is { } forbidden) return forbidden;
        if (await ForbiddenUnlessAsync("InnerLetters", "Create") is { } noCreate) return noCreate;
        return Ok(await _letters.SuggestReceiversAsync(req, MyUserId, ct));
    }

    /// <summary>دانلود اکسل گزارش ساخته‌شده در گفتگو (فقط سازنده، تا ۱۵ دقیقه).</summary>
    [HttpGet("reports/{id}/excel")]
    public async Task<IActionResult> DownloadReportExcel(string id, CancellationToken ct)
    {
        _ = ct;
        if (await ForbiddenUnlessAsync(Module, "Use") is { } forbidden) return forbidden;
        var spec = _reports.TryGetSpec(MyUserId, id);
        if (spec == null)
            return NotFound(new { message = "گزارش یافت نشد یا منقضی شده است؛ دوباره از دستیار بخواه." });
        var bytes = Services.Export.ExcelWriter.Build(spec);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"{spec.FileBaseName ?? "gozaresh"}.xlsx");
    }

    /// <summary>۱۰) پیش‌نویس صورتجلسه از متن خام.</summary>
    [HttpPost("minutes/draft")]
    public async Task<IActionResult> DraftMinutes([FromBody] AiMinutesDraftRequest req, CancellationToken ct)
    {
        if (await ForbiddenUnlessAsync(Module, "Use") is { } forbidden) return forbidden;
        if (await ForbiddenUnlessAnyAsync("MeetingMinutes", "View", "Create", "Update") is { } noAccess) return noAccess;
        return Ok(await _letters.DraftMinutesAsync(req, ct));
    }

    // ==================== گزارش صبحگاهی ====================

    /// <summary>پیش‌نمایش گزارش صبحگاهی خودم (همان متنی که در بله می‌آید).</summary>
    [HttpGet("briefing/preview")]
    public async Task<IActionResult> BriefingPreview(CancellationToken ct)
    {
        if (await ForbiddenUnlessAsync(Module, "Use") is { } forbidden) return forbidden;
        var text = await _briefing.BuildBriefingAsync(MyUserId, await MyDisplayNameAsync(ct), ct);
        return Ok(new { briefing = text });
    }

    /// <summary>ارسال فوری گزارش صبحگاهی به همه کاربران واجد شرایط (مدیر — برای تست).</summary>
    [HttpPost("briefing/send-now")]
    public async Task<IActionResult> BriefingSendNow(CancellationToken ct)
    {
        if (await ForbiddenUnlessAsync(Module, "Manage") is { } forbidden) return forbidden;
        var sent = await _briefing.SendToAllAsync(_messenger, ct);
        return Ok(new { sent });
    }

    // ==================== خلاصه هفتگی ====================

    /// <summary>پیش‌نمایش خلاصه هفته (مدیران — همان متنی که در پیام‌رسان می‌آید).</summary>
    [HttpGet("digest/preview")]
    public async Task<IActionResult> DigestPreview(CancellationToken ct)
    {
        if (await ForbiddenUnlessAsync(Module, "Use") is { } forbidden) return forbidden;
        if (await ForbiddenUnlessAsync("FaAtt", "Manage") is { } noManage) return noManage;
        var text = await _digest.BuildDigestAsync(MyUserId, await MyDisplayNameAsync(ct), ct);
        return Ok(new { digest = text });
    }

    /// <summary>ارسال فوری خلاصه هفته به مدیران واجد شرایط (مدیر — برای تست).</summary>
    [HttpPost("digest/send-now")]
    public async Task<IActionResult> DigestSendNow(CancellationToken ct)
    {
        if (await ForbiddenUnlessAsync(Module, "Manage") is { } forbidden) return forbidden;
        var sent = await _digest.SendToAllAsync(_messenger, ct);
        return Ok(new { sent });
    }

    /// <summary>ثبت امتیاز به یک پاسخ دستیار (§۲۴).</summary>
    [HttpPost("feedback/submit")]
    public async Task<IActionResult> FeedbackSubmit([FromBody] AiFeedbackSubmitRequest req, CancellationToken ct)
    {
        if (await ForbiddenUnlessAsync(Module, "Use") is { } forbidden) return forbidden;
        if (req == null || req.MessageId <= 0) return BadRequest("شناسه پیام لازم است.");
        var (ok, error, removed) = await _feedback.SubmitAsync(MyUserId, req.MessageId, req.Rating, req.Reason, req.Comment, ct);
        if (!ok) return BadRequest(error);
        return Ok(new { removed });
    }

    /// <summary>آمار بازخوردها (مدیر).</summary>
    [HttpGet("feedback/stats")]
    public async Task<IActionResult> FeedbackStats([FromQuery] int days = 14, CancellationToken ct = default)
    {
        if (await ForbiddenUnlessAsync(Module, "Manage") is { } forbidden) return forbidden;
        return Ok(await _feedback.StatsAsync(days, ct));
    }

    /// <summary>فهرست بازخوردها برای بازبینی (مدیر).</summary>
    [HttpGet("feedback/list")]
    public async Task<IActionResult> FeedbackList([FromQuery] int? rating, [FromQuery] int skip = 0, [FromQuery] int take = 20, CancellationToken ct = default)
    {
        if (await ForbiddenUnlessAsync(Module, "Manage") is { } forbidden) return forbidden;
        return Ok(await _feedback.ListAsync(rating, skip, take, ct));
    }

    /// <summary>آمار حسابرسی دستیار (مدیر، §۲۵).</summary>
    [HttpGet("audit/stats")]
    public async Task<IActionResult> AuditStats([FromQuery] int days = 14, CancellationToken ct = default)
    {
        if (await ForbiddenUnlessAsync(Module, "Manage") is { } forbidden) return forbidden;
        return Ok(await _audit.StatsAsync(days, ct));
    }

    /// <summary>فهرست نوبت‌های چت برای بازبینی (مدیر).</summary>
    [HttpGet("audit/list")]
    public async Task<IActionResult> AuditList([FromQuery] int? userId, [FromQuery] bool? success, [FromQuery] string? search, [FromQuery] int skip = 0, [FromQuery] int take = 20, CancellationToken ct = default)
    {
        if (await ForbiddenUnlessAsync(Module, "Manage") is { } forbidden) return forbidden;
        return Ok(await _audit.ListAsync(userId, success, search, skip, take, ct));
    }

    private async Task<string> MyDisplayNameAsync(CancellationToken ct)
    {
        var displayName = string.IsNullOrWhiteSpace(MyUsername) ? "همکار" : MyUsername;
        var me = await Db.Users.FindAsync(new object?[] { MyUserId }, ct);
        if (me != null)
        {
            var full = ((me.FirstName ?? "") + " " + (me.LastName ?? "")).Trim();
            if (full != "") displayName = full;
        }
        return displayName;
    }
}
