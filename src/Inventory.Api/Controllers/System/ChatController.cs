using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Claims;
using System.Threading.Tasks;
using Inventory.Api.Services.Chat;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Paging = Inventory.Api.Services.Paging;
using Inventory.Api.Services;
using Inventory.Api.Infrastructure;

namespace Inventory.Api.Controllers;

[ApiController]
[Route("api/chat")]
[Authorize]
[RequirePermission("Chat", "View")]
public class ChatController : ControllerBase
{
    private readonly IChatService _chatService;
    private readonly ChatAttachmentService _files;

    public ChatController(IChatService chatService, ChatAttachmentService files)
    {
        _chatService = chatService;
        _files = files;
    }

    private int CurrentUserId =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;

    private string CurrentUserName =>
        User.Identity?.Name ?? "کاربر";

    /// <summary>فهرست گفتگوهای کاربر جاری</summary>
    [HttpGet("conversations")]
    public async Task<ActionResult<List<ChatConversationDto>>> GetConversations([FromQuery] string? search, [FromQuery] ChatTypeDto? type, [FromQuery] bool onlyUnread = false, [FromQuery] int skip = 0, [FromQuery] int? take = null)
        => Ok(await Paging.ResultAsync(
            pagination => _chatService.GetConversationsAsync(CurrentUserId, search, type, onlyUnread, pagination),
            skip, take));

    /// <summary>جزئیات یک گفتگو</summary>
    [HttpGet("conversations/{id}")]
    public async Task<ActionResult<ChatConversationDto>> GetConversation(int id)
    {
        var conv = await _chatService.GetConversationByIdAsync(CurrentUserId, id);
        return Ok(conv);
    }

    /// <summary>شروع یا دریافت گفتگوی خصوصی با یکی از کاربران نرم‌افزار</summary>
    [HttpPost("conversations/direct")]
    [RequirePermission("Chat", "Send")]
    public async Task<ActionResult<ChatConversationDto>> CreateDirect([FromBody] CreateDirectChatRequest req)
    {
        var conv = await _chatService.GetOrCreateDirectConversationAsync(CurrentUserId, CurrentUserName, req.TargetUserId);
        return Ok(conv);
    }

    /// <summary>ایجاد گروه جدید با کاربران انتخابی</summary>
    [HttpPost("conversations/group")]
    [RequirePermission("Chat", "Send")]
    public async Task<ActionResult<ChatConversationDto>> CreateGroup([FromBody] CreateGroupChatRequest req)
    {
        var conv = await _chatService.CreateGroupConversationAsync(CurrentUserId, CurrentUserName, req);
        return Ok(conv);
    }

    /// <summary>دریافت پیام‌های یک گفتگو با صفحه‌بندی</summary>
    [HttpGet("conversations/{id}/messages")]
    public async Task<ActionResult<List<ChatMessageDto>>> GetMessages(int id, [FromQuery] int? beforeId, [FromQuery] int pageSize = 50, [FromQuery] string? search = null, [FromQuery] int? skip = null, [FromQuery] int? take = null)
    {
        var messages = await _chatService.GetMessagesAsync(CurrentUserId, id, beforeId, Paging.ToPageSize(take, pageSize), search, skip ?? 0);
        return Ok(messages);
    }

    /// <summary>ارسال پیام جدید در گفتگو</summary>
    [HttpPost("conversations/{id}/messages")]
    [RequirePermission("Chat", "Send")]
    public async Task<ActionResult<ChatMessageDto>> SendMessage(int id, [FromBody] SendChatMessageRequest req)
    {
        req.ConversationId = id;
        var msg = await _chatService.SendMessageAsync(CurrentUserId, CurrentUserName, null, req);
        return Ok(msg);
    }

    /// <summary>فوروارد پیام (متن/فایل/رکورد) به گفتگویی که کاربر عضو آن است</summary>
    [HttpPost("messages/{id}/forward")]
    [RequirePermission("Chat", "Send")]
    public async Task<ActionResult<ChatMessageDto>> ForwardMessage(int id, [FromBody] ForwardChatMessageRequest req)
    {
        var msg = await _chatService.ForwardMessageAsync(CurrentUserId, CurrentUserName, null, id, req.ConversationId);
        return Ok(msg);
    }

    /// <summary>ویرایش پیام</summary>
    [HttpPut("messages/{id}")]
    [RequirePermission("Chat", "Send")]
    public async Task<ActionResult<ChatMessageDto>> EditMessage(int id, [FromBody] EditChatMessageRequest req)
    {
        var msg = await _chatService.EditMessageAsync(CurrentUserId, id, req.NewText);
        return Ok(msg);
    }

    /// <summary>حذف پیام</summary>
    [HttpDelete("messages/{id}")]
    [RequirePermission("Chat", "Manage")]
    public async Task<IActionResult> DeleteMessage(int id)
    {
        await _chatService.DeleteMessageAsync(CurrentUserId, id);
        return Ok(new { message = "پیام با موفقیت حذف شد." });
    }

    /// <summary>ثبت یا تغییر واکنش ایموجی</summary>
    [HttpPost("messages/{id}/react")]
    [RequirePermission("Chat", "Send")]
    public async Task<ActionResult<Dictionary<string, List<ChatReactionUserDto>>>> React(int id, [FromBody] ReactChatMessageRequest req)
    {
        var reactions = await _chatService.ToggleReactionAsync(CurrentUserId, CurrentUserName, id, req.Emoji);
        return Ok(reactions);
    }

    /// <summary>پین یا آن‌پین کردن پیام در گفتگو</summary>
    [HttpPost("messages/{id}/pin")]
    [RequirePermission("Chat", "Manage")]
    public async Task<ActionResult<ChatMessageDto>> TogglePinMessage(int id)
    {
        var msg = await _chatService.TogglePinMessageAsync(CurrentUserId, id);
        return Ok(msg);
    }

    /// <summary>علامت‌گذاری گفتگو به عنوان خوانده شده</summary>
    [HttpPost("conversations/{id}/read")]
    [RequirePermission("Chat", "Send")]
    public async Task<IActionResult> MarkRead(int id, [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] MarkChatReadRequest? request = null)
    {
        await _chatService.MarkConversationAsReadAsync(CurrentUserId, id, request?.LastReadMessageId);
        return Ok(new { success = true });
    }

    /// <summary>پین یا آن‌پین کردن گفتگو در لیست دیالوگ‌ها</summary>
    [HttpPost("conversations/{id}/pin")]
    [RequirePermission("Chat", "Send")]
    public async Task<IActionResult> TogglePinConversation(int id)
    {
        await _chatService.TogglePinConversationAsync(CurrentUserId, id);
        return Ok(new { success = true });
    }

    /// <summary>بی‌صدا کردن یا فعال‌سازی صدای گفتگو</summary>
    [HttpPost("conversations/{id}/mute")]
    [RequirePermission("Chat", "Send")]
    public async Task<IActionResult> ToggleMuteConversation(int id)
    {
        await _chatService.ToggleMuteConversationAsync(CurrentUserId, id);
        return Ok(new { success = true });
    }

    /// <summary>فهرست اعضای گروه</summary>
    [HttpGet("conversations/{id}/members")]
    public async Task<ActionResult<List<ChatMemberDto>>> GetMembers(int id, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        var pagination = new Paging.Request(skip, take);
        var members = await _chatService.GetGroupMembersAsync(CurrentUserId, id, pagination);
        return Ok(pagination.Result(members));
    }

    /// <summary>افزودن عضو جدید به گروه</summary>
    [HttpPost("conversations/{id}/members")]
    [RequirePermission("Chat", "Manage")]
    public async Task<IActionResult> AddMembers(int id, [FromBody] List<int> userIds)
    {
        await _chatService.AddMembersToGroupAsync(CurrentUserId, CurrentUserName, id, userIds);
        return Ok(new { success = true });
    }

    /// <summary>حذف عضو از گروه یا خروج</summary>
    [HttpDelete("conversations/{id}/members/{userId}")]
    [RequirePermission("Chat", "Manage")]
    public async Task<IActionResult> RemoveMember(int id, int userId)
    {
        await _chatService.RemoveMemberFromGroupAsync(CurrentUserId, CurrentUserName, id, userId);
        return Ok(new { success = true });
    }

    /// <summary>فهرست تمامی کاربران نرم‌افزار جهت شروع چت</summary>
    [HttpGet("users")]
    public async Task<ActionResult<List<ChatUserDto>>> GetUsers([FromQuery] string? search, [FromQuery] int skip = 0, [FromQuery] int? take = null)
        => Ok(await Paging.ResultAsync(
            pagination => _chatService.GetSoftwareUsersForChatAsync(CurrentUserId, search, pagination),
            skip, take));

    /// <summary>خلاصه اعلان‌های چت برای هدر نرم‌افزار</summary>
    [HttpGet("summary")]
    public async Task<ActionResult<ChatSummaryDto>> GetSummary()
    {
        var summary = await _chatService.GetChatSummaryAsync(CurrentUserId);
        return Ok(summary);
    }

    /// <summary>بارگذاری فایل برای گفتگوی مشخص (فقط اعضا)، حداکثر ۵۰ MiB.</summary>
    [HttpPost("conversations/{id:int}/attachments")]
    [RequirePermission("Chat", "Send")]
    [DisableRequestSizeLimit]
    [RequestFormLimits(MultipartBodyLengthLimit = long.MaxValue)]
    public async Task<IActionResult> UploadAttachment(int id, [FromForm] IFormFile? file)
    {
        if (file == null) return BadRequest(new { message = "فایلی انتخاب نشده است." });
        return Ok(await _files.UploadAsync(CurrentUserId, id, file, HttpContext.RequestAborted));
    }

    /// <summary>سازگاری با کلاینت قدیمی؛ فایل بدون گفتگو فقط در اختیار بارگذار می‌ماند.</summary>
    [HttpPost("upload")]
    [RequirePermission("Chat", "Send")]
    [DisableRequestSizeLimit]
    [RequestFormLimits(MultipartBodyLengthLimit = long.MaxValue)]
    public async Task<IActionResult> UploadLegacy([FromForm] IFormFile? file, [FromQuery] int? conversationId)
    {
        if (file == null) return BadRequest(new { message = "فایلی انتخاب نشده است." });
        return Ok(await _files.UploadAsync(CurrentUserId, conversationId, file, HttpContext.RequestAborted));
    }

    /// <summary>
    /// توکن کوتاه برای لینک‌های دانلود/پیش‌نمایش (تگ‌های a/img/audio/video هدر Authorization ندارند).
    /// JWT کامل به‌دلیل طول (≈۱۰KB) در URL جا نمی‌شود.
    /// </summary>
    [HttpGet("file-token")]
    public ActionResult<object> GetFileToken()
    {
        var (token, exp) = ChatFileToken.Create(CurrentUserId);
        return Ok(new { token, expiresAt = exp });
    }

    [HttpGet("messages/{id:int}/download")]
    public Task<IActionResult> DownloadMessageFile(int id) =>
        FileResponseAsync(() => _files.GetMessageFileAsync(CurrentUserId, id), false);

    [HttpGet("messages/{id:int}/preview")]
    public Task<IActionResult> PreviewMessageFile(int id) =>
        FileResponseAsync(() => _files.GetMessageFileAsync(CurrentUserId, id), true);

    [HttpGet("attachments/{id:guid}/download")]
    public Task<IActionResult> DownloadAttachment(Guid id) =>
        FileResponseAsync(() => _files.GetAttachmentAsync(CurrentUserId, id), false);

    /// <summary>
    /// تبدیل پیام صوتی به متن (رونویسی). با <c>?force=true</c> رونویسی قبلی دوباره انجام می‌شود.
    /// خطاها با پیام فارسیِ قابل‌نمایش برمی‌گردند تا کاربر بداند مشکل تنظیمات است یا فایل.
    /// </summary>
    [HttpPost("messages/{id:int}/transcribe")]
    public async Task<ActionResult<ChatMessageDto>> TranscribeMessage(int id, [FromQuery] bool force = false, CancellationToken ct = default)
    {
        try
        {
            return Ok(await _chatService.TranscribeVoiceMessageAsync(CurrentUserId, id, force, ct));
        }
        catch (UnauthorizedAccessException ex) { return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message }); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    /// <summary>ابزار تشخیصی فایل‌های پیام‌رسان (فقط مدیر): مسیر مؤثر ذخیره، دسترسی نوشتن، شمارش فایل‌ها
    /// و نمونهٔ آخرین پیوست‌ها همراه با اینکه در کدام مسیر موجودند. برای یافتن علت
    /// «فایل پیوست روی سرور یافت نشد.» بدون دسترسی به سرور.
    /// </summary>
    [HttpGet("attachments/diagnose")]
    [RequirePermission("Chat", "Manage")]
    public async Task<ActionResult<object>> DiagnoseAttachments([FromServices] Inventory.Api.Data.AppDbContext db)
    {
        if (!await db.Users.AsNoTracking().AnyAsync(u => u.Id == CurrentUserId && u.IsActive && u.Role == "Admin"))
            return Forbid();
        return Ok(await _files.DiagnoseAsync());
    }

    private async Task<IActionResult> FileResponseAsync(Func<Task<ChatFileResult>> resolve, bool preview)
    {
        try
        {
            var file = await resolve();
            Response.Headers.CacheControl = "private, no-store";
            Response.Headers["X-Content-Type-Options"] = "nosniff";
            Response.Headers["Referrer-Policy"] = "no-referrer";
            Response.Headers["Content-Security-Policy"] = "sandbox; default-src 'none'";
            var inline = preview && ChatAttachmentService.MessageTypeFor(file.ContentType) != ChatMessageTypeDto.File;
            return PhysicalFile(file.Path, file.ContentType, inline ? null : file.FileName, enableRangeProcessing: true);
        }
        catch (UnauthorizedAccessException)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "دسترسی به فایل این گفتگو مجاز نیست." });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }
}
