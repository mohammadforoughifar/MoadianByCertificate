using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Inventory.Api.Data;
using Inventory.Api.Services.Ai;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Inventory.Api.Services;

/// <summary>نتیجه‌ی ارسال یک اعلان به پیام‌رسان‌ها (برای نمایش وضعیت در «ارسال آزمایشی»).</summary>
public record MessengerSendResult(bool BaleSent, string? BaleError, bool EitaaSent, string? EitaaError, bool Linked)
{
    public bool AnySent => BaleSent || EitaaSent;
}

/// <summary>
/// ================== سرویس پیام‌رسان‌های ایرانی (بله + ایتا) ==================
///  • بله:  ربات ساخته‌شده در @botfather (ble.ir) — کاربر ربات را /start می‌کند و شماره‌اش را
///          به اشتراک می‌گذارد؛ شماره با موبایل کاربران تطبیق داده می‌شود و شناسه چت ذخیره می‌گردد.
///          ارسال با متد sendMessage روی tapi.bale.ai انجام می‌شود (سازگار با Bot API تلگرام).
///  • ایتا: برنامه‌ی ثبت‌شده در سامانه توسعه‌دهندگان ایتا (developer.eitaa.com / پنل ایتایار).
///          کاربر با بازکردن «برنامه» در ایتا و وارد کردن کد اتصال، شناسه عددی ایتای خود را
///          به سامانه می‌دهد (اعتبارسنجی initData با HMAC-SHA256 و توکن برنامه).
///          ارسال با متد app/sendMessage روی eitaayar.ir انجام می‌شود:
///          POST https://eitaayar.ir/api/app/sendMessage  { token, chat_id, text }
///  • شماره معرف سامانه در امضای هر پیام می‌آید.
/// </summary>
public interface IMessengerService
{
    /// <summary>ارسال پیام به کاربر در بله و ایتا (هر کدام که متصل است). خطاها برگردانده می‌شوند و بی‌صدا نمی‌مانند.</summary>
    Task<MessengerSendResult> SendToUserAsync(int userId, string title, string? body);
    /// <summary>ارسال فایل اکسل به بله کاربر لینک‌شده (ایتا فایل نمی‌گیرد).</summary>
    Task<bool> SendExcelToUserAsync(int userId, string caption, byte[] bytes, string fileName);

    /// <summary>همگام‌سازی دستی بله (دکمه‌ی صفحه‌ی کاربران): خواندن آپدیت‌ها، پاسخ به /start و تطبیق شماره‌ها.</summary>
    Task<(int matched, string message)> SyncBaleAsync();

    /// <summary>یک دور خواندن آپدیت‌های بله از offset داده‌شده — خروجی: تعداد اتصال‌های جدید و بزرگ‌ترین update_id.</summary>
    Task<(int linked, long maxUpdateId, string note)> PollBaleAsync(long offset);

    /// <summary>ارسال پیام آزمایشی به کاربر جاری — خروجی: وضعیت هر پیام‌رسان برای نمایش به کاربر/مدیر.</summary>
    Task<MessengerSendResult> SendTestAsync(int userId);

    /// <summary>اعتبارسنجی initData برنامک ایتا و اتصال کاربر (با کد یکبارمصرف) — شناسه چت ایتا ذخیره می‌شود.</summary>
    Task<(bool ok, string message)> LinkEitaaAsync(string code, string initData);
}

public class MessengerService : IMessengerService
{
    private readonly AppDbContext _db;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IMessengerLinkCodes _codes;
    private readonly ILogger<MessengerService> _log;
    private readonly AiOptions _aiOptions;
    private readonly IAiAgentService _agent;
    private readonly AiReportService _reports;

    public MessengerService(AppDbContext db, IHttpClientFactory httpFactory, IMessengerLinkCodes codes, ILogger<MessengerService> log, IOptions<AiOptions> aiOptions, IAiAgentService agent, AiReportService reports)
    {
        _db = db;
        _httpFactory = httpFactory;
        _codes = codes;
        _log = log;
        _aiOptions = aiOptions.Value;
        _agent = agent;
        _reports = reports;
    }

    private async Task<(string? bale, string? eitaa, string sender)> TokensAsync()
    {
        var st = await _db.AppSettings.AsNoTracking().FirstOrDefaultAsync();
        return (st?.BaleBotToken?.Trim(), st?.EitaaToken?.Trim(), st?.MessengerSenderNumber ?? "09111189771");
    }

    private static string ComposeText(string title, string? body, string sender)
        => $"🔔 {title}\n{body}\n———\n📱 سامانه انبار و فروش — {sender}";

    // ============================================================ ارسال
    public async Task<MessengerSendResult> SendToUserAsync(int userId, string title, string? body)
    {
        try
        {
            var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null) return new MessengerSendResult(false, "کاربر یافت نشد.", false, null, false);

            var (baleToken, eitaaToken, sender) = await TokensAsync();
            var text = ComposeText(title, body, sender);
            var http = _httpFactory.CreateClient("messenger");

            string? baleError = null, eitaaError = null;
            var baleSent = false;
            var eitaaSent = false;

            // ---------- بله ----------
            if (!string.IsNullOrWhiteSpace(user.BaleChatId))
            {
                if (string.IsNullOrWhiteSpace(baleToken)) baleError = "توکن ربات بله در تنظیمات وارد نشده است.";
                else
                {
                    var (ok, err) = await PostJsonAsync(http, $"https://tapi.bale.ai/bot{baleToken}/sendMessage",
                        new { chat_id = BaleChatIdValue(user.BaleChatId!), text });
                    baleSent = ok;
                    if (!ok) baleError = err;
                }
            }

            // ---------- ایتا (برنامه‌ی رسمی ایتا / ایتایار) ----------
            if (!string.IsNullOrWhiteSpace(user.EitaaChatId))
            {
                if (string.IsNullOrWhiteSpace(eitaaToken)) eitaaError = "توکن برنامه ایتا در تنظیمات وارد نشده است.";
                else
                {
                    var (ok, err) = await PostJsonAsync(http, "https://eitaayar.ir/api/app/sendMessage",
                        new { token = eitaaToken, chat_id = EitaaChatIdValue(user.EitaaChatId!), text });
                    eitaaSent = ok;
                    if (!ok) eitaaError = err;
                }
            }

            var linked = !string.IsNullOrWhiteSpace(user.BaleChatId) || !string.IsNullOrWhiteSpace(user.EitaaChatId);
            return new MessengerSendResult(baleSent, baleError, eitaaSent, eitaaError, linked);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "ارسال پیام‌رسان برای کاربر {UserId} ناموفق بود", userId);
            return new MessengerSendResult(false, ex.Message, false, null, false);
        }
    }

    public async Task<bool> SendExcelToUserAsync(int userId, string caption, byte[] bytes, string fileName)
    {
        try
        {
            var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null || string.IsNullOrWhiteSpace(user.BaleChatId)) return false;
            var baleToken = (await TokensAsync()).bale;
            if (string.IsNullOrWhiteSpace(baleToken)) return false;
            var http = _httpFactory.CreateClient("messenger");
            await SendDocumentAsync(http, baleToken, user.BaleChatId!, bytes, fileName, caption ?? "");
            return true;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "ارسال اکسل برای کاربر {UserId} ناموفق بود.", userId);
            return false;
        }
    }

    public Task<MessengerSendResult> SendTestAsync(int userId)
        => SendToUserAsync(userId, "پیام آزمایشی", $"اتصال پیام‌رسان‌ها برقرار است ✅{Environment.NewLine}اگر این پیام را می‌بینید، اعلان‌های سامانه برای شما به این پیام‌رسان ارسال می‌شود.");

    private static object BaleChatIdValue(string chatId) => long.TryParse(chatId, out var n) ? n : chatId;
    private static object EitaaChatIdValue(string chatId) => long.TryParse(chatId, out var n) ? n : chatId;

    /// <summary>ارسال JSON و خواندن فیلد ok (هر دو سرویس بله و ایتایار همین ساختار را دارند).</summary>
    private static async Task<(bool ok, string? error)> PostJsonAsync(HttpClient http, string url, object payload)
    {
        try
        {
            var json = JsonSerializer.Serialize(payload);
            using var resp = await http.PostAsync(url, new StringContent(json, Encoding.UTF8, "application/json"));
            var bodyText = await resp.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(bodyText))
                return (resp.IsSuccessStatusCode, resp.IsSuccessStatusCode ? null : $"پاسخ خالی از سرور ({(int)resp.StatusCode}).");

            try
            {
                using var doc = JsonDocument.Parse(bodyText);
                var root = doc.RootElement;
                var ok = root.TryGetProperty("ok", out var okEl) && okEl.ValueKind == JsonValueKind.True;
                if (ok) return (true, null);

                var reason = root.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString()
                    : root.TryGetProperty("result", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString()
                    : root.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString()
                    : null;
                var code = root.TryGetProperty("error_code", out var ec) ? ec.ToString() : null;
                var error = $"{(string.IsNullOrWhiteSpace(reason) ? "خطای نامشخص" : reason)}{(string.IsNullOrWhiteSpace(code) ? "" : $" (کد {code})")}";
                return (false, error);
            }
            catch (JsonException)
            {
                return (resp.IsSuccessStatusCode, resp.IsSuccessStatusCode ? null : bodyText.Trim());
            }
        }
        catch (Exception ex)
        {
            return (false, "ارتباط برقرار نشد: " + ex.Message);
        }
    }

    // ============================================================ بله: دریافت آپدیت‌ها
    public async Task<(int linked, long maxUpdateId, string note)> PollBaleAsync(long offset)
    {
        var (baleToken, _, _) = await TokensAsync();
        if (string.IsNullOrWhiteSpace(baleToken)) return (0, 0, "توکن ربات بله در تنظیمات وارد نشده است.");

        var http = _httpFactory.CreateClient("messenger");
        var url = $"https://tapi.bale.ai/bot{baleToken}/getUpdates?limit=100&timeout=0" + (offset > 0 ? $"&offset={offset}" : "");

        HttpResponseMessage resp;
        try { resp = await http.GetAsync(url); }
        catch (Exception ex) { return (0, 0, "ارتباط با سرور بله برقرار نشد: " + ex.Message); }

        using (resp)
        {
            var bodyText = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
                return (0, 0, $"سرور بله پاسخ {(int)resp.StatusCode} داد.");

            JsonDocument doc;
            try { doc = JsonDocument.Parse(bodyText); }
            catch { return (0, 0, "پاسخ نامعتبر از سرور بله."); }

            using (doc)
            {
                var root = doc.RootElement;
                if (root.TryGetProperty("ok", out var okEl) && okEl.ValueKind == JsonValueKind.False)
                {
                    var d = root.TryGetProperty("description", out var de) ? de.GetString() : null;
                    return (0, 0, "بله درخواست را نپذیرفت: " + (d ?? "خطای نامشخص"));
                }
                if (!root.TryGetProperty("result", out var results) || results.ValueKind != JsonValueKind.Array)
                    return (0, 0, "پاسخ نامعتبر از سرور بله.");

                // phone → chatId از مخاطبین به‌اشتراک‌گذاشته‌شده + پاسخ به /start
                var phoneToChat = new Dictionary<string, string>();
                var startChats = new List<string>();
                var helpChats = new List<string>();
                var aiIncoming = new List<(string chatId, string text)>();
                long maxId = 0;
                var updates = 0;

                foreach (var upd in results.EnumerateArray())
                {
                    updates++;
                    if (upd.TryGetProperty("update_id", out var uidEl) && uidEl.TryGetInt64(out var uid) && uid > maxId) maxId = uid;
                    if (!upd.TryGetProperty("message", out var msg)) continue;

                    var chatId = msg.TryGetProperty("chat", out var chat) && chat.TryGetProperty("id", out var cid)
                        ? cid.GetRawText().Trim('"') : null;
                    if (string.IsNullOrWhiteSpace(chatId)) continue;

                    if (msg.TryGetProperty("contact", out var contact))
                    {
                        var phone = contact.TryGetProperty("phone_number", out var ph) ? ph.GetString() : null;
                        if (!string.IsNullOrWhiteSpace(phone)) phoneToChat[Normalize(phone)] = chatId!;
                        continue;
                    }

                    var text = msg.TryGetProperty("text", out var tx) ? (tx.GetString() ?? "").Trim() : "";
                    if (text.StartsWith("/start", StringComparison.OrdinalIgnoreCase)) startChats.Add(chatId!);
                    else if (text.Equals("/help", StringComparison.OrdinalIgnoreCase)) helpChats.Add(chatId!);
                    else if (text.Length > 0) aiIncoming.Add((chatId!, text));
                }

                var linked = 0;
                if (phoneToChat.Count > 0)
                {
                    var users = await _db.Users.Where(u => u.Mobile != null && u.Mobile != "").ToListAsync();
                    foreach (var u in users)
                    {
                        if (!phoneToChat.TryGetValue(Normalize(u.Mobile!), out var chatId)) continue;
                        if (u.BaleChatId == chatId) continue;
                        u.BaleChatId = chatId;
                        linked++;
                        await ReplyAsync(http, baleToken, chatId, "✅ شماره شما با موفقیت به سامانه متصل شد.\nاز این پس اعلان‌های سامانه در همین گفتگو برای شما ارسال می‌شود.");
                    }
                    if (linked > 0) await _db.SaveChangesAsync();
                }

                // کاربرانی که ربات را استارت کرده‌اند اما شماره نداده‌اند: درخواست اشتراک شماره
                foreach (var chatId in startChats.Distinct())
                {
                    var known = await _db.Users.AsNoTracking().AnyAsync(u => u.BaleChatId == chatId);
                    if (known)
                    {
                        // کاربر لینک‌شده: معرفی دستیار فروغ آریا
                        await ReplyAsync(http, baleToken, chatId, BaleIntroText);
                        continue;
                    }
                    await ReplyAsync(http, baleToken, chatId,
                        "سلام 👋\nبرای دریافت اعلان‌های سامانه، لطفاً شماره تلفن خود را با دکمه‌ی زیر ارسال کنید.",
                        new { keyboard = new[] { new[] { new { text = "📱 ارسال شماره من", request_contact = true } } }, resize_keyboard = true, one_time_keyboard = true });
                }

                // راهنمای دستیار + پاسخ فروغ آریا به پیام‌های متنی (فقط کاربران لینک‌شده و مجاز)
                foreach (var chatId in helpChats.Distinct())
                    await ReplyAsync(http, baleToken, chatId, BaleIntroText);
                await ReplyAiAsync(http, baleToken, aiIncoming);

                // تأیید آپدیت‌ها (تا دفعه‌ی بعد مجدداً پردازش نشوند)
                if (maxId > 0)
                {
                    try
                    {
                        using var confirm = await http.GetAsync($"https://tapi.bale.ai/bot{baleToken}/getUpdates?limit=1&timeout=0&offset={maxId + 1}");
                        _ = await confirm.Content.ReadAsStringAsync();
                    }
                    catch { /* تأیید ناموفق مهم نیست؛ دفعه‌ی بعد دوباره تلاش می‌شود */ }
                }

                var note = updates == 0 ? "پیام تازه‌ای از کاربران دریافت نشد." : $"تعداد پیام‌های دریافتی: {updates}.";
                return (linked, maxId, note);
            }
        }
    }

    private const string BaleIntroText =
        "سلام! 👋 من «فروغ آریا»، دستیار هوشمند سامانه‌ام.\n\n" +
        "می‌تونی ازم بپرسی:\n" +
        "• مانده مرخصی‌ام چقدره؟\n" +
        "• فیش حقوقیم چقدره؟\n" +
        "• وضعیت حضور امروزم؟\n" +
        "• نامه خوانده‌نشده دارم؟\n" +
        "• چطور فاکتور ثبت کنم؟\n" +
        "• گزارش فروش این ماه (با فایل اکسل 📥)\n\n" +
        "فقط بنویس! 🤖";

    /// <summary>پاسخ دستیار فروغ آریا به پیام‌های بله: تایپینگ، متن تمیز، چندپیامی، فایل اکسل (§۱۸).</summary>
    private async Task ReplyAiAsync(HttpClient http, string token, List<(string chatId, string text)> incoming)
    {
        if (!_aiOptions.Enabled || !_aiOptions.BaleEnabled) return;
        foreach (var (chatId, text) in incoming.DistinctBy(x => x.chatId + "\n" + x.text).Take(10))
        {
            try
            {
                var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.BaleChatId == chatId);
                if (user == null)
                {
                    // غریبه در چت خصوصی: دعوت به اتصال (در گروه‌ها سکوت)
                    if (long.TryParse(chatId, out var cid) && cid > 0)
                        await ReplyAsync(http, token, chatId,
                            "سلام 👋\nبرای استفاده از دستیار هوشمند، اول /start را بزن و شماره‌ات را متصل کن.");
                    continue;
                }
                if (!user.IsActive) continue;
                if (!await AiAccessHelper.UserHasAsync(_db, user.Id, "AiAssistant", "Use", user.Role))
                {
                    await ReplyAsync(http, token, chatId,
                        "به دستیار هوشمند دسترسی نداری؛ از مدیر سامانه بخواه دسترسی دستیار (AiAssistant) را برایت فعال کند.");
                    continue;
                }
                var name = ((user.FirstName ?? "") + " " + (user.LastName ?? "")).Trim();
                if (name == "") name = user.Username;

                // نمایش «در حال نوشتن…» تا آمدن جواب مدل
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(4));
                using var typingCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
                var typingTask = TypingLoopAsync(http, token, chatId, typingCts.Token);
                AiChatResponse result;
                try
                {
                    result = await _agent.ChatAsync(user.Id, name, false, null, text, "bale", cts.Token);
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "پاسخ هوش مصنوعی در بله ناموفق بود.");
                    await ReplyAsync(http, token, chatId,
                        "نتونستم جوابت را آماده کنم 😔\nیک بار دیگر بفرست؛ اگر درست نشد از چت وب سامانه استفاده کن.");
                    continue;
                }
                finally
                {
                    typingCts.Cancel();
                    try { await typingTask; } catch { /* تمام */ }
                }

                var plain = AiMessengerFormat.ToPlain(result.Reply);
                if (plain == "") plain = "پاسخی آماده نشد؛ سوالت را واضح‌تر بپرس. 🤖";
                foreach (var part in AiMessengerFormat.SplitMessage(plain))
                    await ReplyAsync(http, token, chatId, part);

                // فایل‌های اکسل گزارش → سند بله
                foreach (var att in (result.Attachments ?? new()).Where(a => a.Kind == "excel").Take(3))
                {
                    try
                    {
                        var spec = _reports.TryGetSpec(user.Id, att.ReportId);
                        if (spec == null) continue;
                        var bytes = Inventory.Api.Services.Export.ExcelWriter.Build(spec);
                        await SendDocumentAsync(http, token, chatId, bytes,
                            $"{spec.FileBaseName ?? "gozaresh"}.xlsx", $"📥 {att.Title}");
                    }
                    catch (Exception ex)
                    {
                        _log.LogWarning(ex, "ارسال فایل اکسل در بله ناموفق بود.");
                    }
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "پردازش پیام بله ناموفق بود.");
            }
        }
    }

    /// <summary>حلقه «در حال نوشتن…» تا لغو توکن.</summary>
    private static async Task TypingLoopAsync(HttpClient http, string token, string chatId, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var payload = JsonSerializer.Serialize(new { chat_id = BaleChatIdValue(chatId), action = "typing" });
                    using var _ = await http.PostAsync($"https://tapi.bale.ai/bot{token}/sendChatAction",
                        new StringContent(payload, Encoding.UTF8, "application/json"), ct);
                }
                catch { /* یک دور ناموفق مهم نیست */ }
                await Task.Delay(TimeSpan.FromSeconds(4), ct);
            }
        }
        catch (OperationCanceledException) { /* پایان طبیعی */ }
        catch { /* خطای دیگر هم مهم نیست */ }
    }

    /// <summary>ارسال فایل (اکسل گزارش) به چت بله.</summary>
    private async Task SendDocumentAsync(HttpClient http, string token, string chatId,
        byte[] bytes, string fileName, string caption)
    {
        try
        {
            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(BaleChatIdValue(chatId).ToString() ?? chatId), "chat_id");
            var file = new ByteArrayContent(bytes);
            file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
            form.Add(file, "document", fileName);
            if (caption.Length > 1000) caption = caption[..1000];
            form.Add(new StringContent(caption, Encoding.UTF8), "caption");
            using var resp = await http.PostAsync($"https://tapi.bale.ai/bot{token}/sendDocument", form);
            _ = await resp.Content.ReadAsStringAsync();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "ارسال سند در بله ناموفق بود.");
        }
    }

    public async Task<(int matched, string message)> SyncBaleAsync()
    {
        var baleToken = (await TokensAsync()).bale;
        if (string.IsNullOrWhiteSpace(baleToken))
            return (0, "توکن ربات بله در تنظیمات وارد نشده است.");

        var (linked, _, note) = await PollBaleAsync(0);
        var message = linked > 0
            ? $"{linked} کاربر با موفقیت به بله متصل شد."
            : "کاربر جدیدی برای اتصال پیدا نشد — کاربران باید ربات را در بله استارت کنند و شماره خود را (با دکمه‌ی «ارسال شماره من») بفرستند.";
        return (linked, $"{message} {note}");
    }

    private static async Task ReplyAsync(HttpClient http, string token, string chatId, string text, object? replyMarkup = null)
    {
        try
        {
            var payload = replyMarkup == null
                ? JsonSerializer.Serialize(new { chat_id = BaleChatIdValue(chatId), text })
                : JsonSerializer.Serialize(new { chat_id = BaleChatIdValue(chatId), text, reply_markup = replyMarkup });
            using var _ = await http.PostAsync($"https://tapi.bale.ai/bot{token}/sendMessage", new StringContent(payload, Encoding.UTF8, "application/json"));
        }
        catch { /* پاسخ خودکار اختیاری است */ }
    }

    /// <summary>نرمال‌سازی شماره: 09xx و +989xx و 989xx یکسان می‌شوند.</summary>
    private static string Normalize(string phone)
    {
        var p = new string(phone.Where(char.IsDigit).ToArray());
        if (p.StartsWith("98") && p.Length >= 12) p = "0" + p[2..];
        if (!p.StartsWith("0")) p = "0" + p;
        return p;
    }

    // ============================================================ ایتا: اتصال کاربر
    public async Task<(bool ok, string message)> LinkEitaaAsync(string code, string initData)
    {
        if (string.IsNullOrWhiteSpace(initData))
            return (false, "داده‌ی ایتا دریافت نشد؛ این صفحه باید از داخل برنامه‌ی ایتا باز شود.");

        var (_, eitaaToken, _) = await TokensAsync();
        if (string.IsNullOrWhiteSpace(eitaaToken))
            return (false, "توکن برنامه ایتا در تنظیمات سامانه وارد نشده است.");

        if (!TryReadInitData(eitaaToken, initData, out var eitaaUserId, out var error))
            return (false, error);

        if (!_codes.TryTake(code, out var userId))
            return (false, "کد اتصال نامعتبر یا منقضی شده است؛ از سامانه کد تازه بگیرید.");

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null) return (false, "کاربری که کد برای او ساخته شده پیدا نشد.");

        user.EitaaChatId = eitaaUserId.ToString();
        await _db.SaveChangesAsync();
        return (true, "✅ اتصال ایتا انجام شد. از این پس اعلان‌های سامانه در ایتا برای شما ارسال می‌شود.");
    }

    /// <summary>
    /// اعتبارسنجی initData برنامک ایتا مطابق مستندات رسمی:
    /// data_check_string = اتصال «کلید=مقدار»های مرتب‌شده (بدون hash) با \n
    /// secret = HMAC_SHA256(key: "WebAppData", message: app_token) و امضا = HMAC_SHA256(secret, data_check_string)
    /// </summary>
    public static bool TryReadInitData(string appToken, string initData, out long eitaaUserId, out string error)
    {
        eitaaUserId = 0;
        error = "";

        var pairs = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in initData.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var idx = part.IndexOf('=');
            if (idx <= 0) continue;
            var key = part[..idx];
            var value = WebUtility.UrlDecode(part[(idx + 1)..]);
            pairs[key] = value;
        }

        if (!pairs.TryGetValue("hash", out var hash) || string.IsNullOrWhiteSpace(hash))
        {
            error = "امضای داده‌های ایتا (hash) یافت نشد.";
            return false;
        }

        var dataCheckString = string.Join("\n", pairs
            .Where(kv => !string.Equals(kv.Key, "hash", StringComparison.Ordinal))
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => $"{kv.Key}={kv.Value}"));

        using var keyHmac = new HMACSHA256(Encoding.UTF8.GetBytes("WebAppData"));
        var secret = keyHmac.ComputeHash(Encoding.UTF8.GetBytes(appToken));
        using var sigHmac = new HMACSHA256(secret);
        var computed = Convert.ToHexString(sigHmac.ComputeHash(Encoding.UTF8.GetBytes(dataCheckString))).ToLowerInvariant();

        var expected = hash.Trim().ToLowerInvariant();
        if (computed.Length != expected.Length ||
            !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(computed), Encoding.UTF8.GetBytes(expected)))
        {
            error = "امضای داده‌های ایتا معتبر نیست؛ مطمئن شوید توکن برنامه در تنظیمات همان برنامه‌ای است که این صفحه در آن باز شده.";
            return false;
        }

        if (pairs.TryGetValue("auth_date", out var authDate) && long.TryParse(authDate, out var ts))
        {
            var when = DateTimeOffset.FromUnixTimeSeconds(ts);
            if (DateTimeOffset.UtcNow - when > TimeSpan.FromMinutes(30))
            {
                error = "این داده‌ها منقضی شده‌اند؛ برنامه را در ایتا ببندید و دوباره باز کنید.";
                return false;
            }
        }

        if (pairs.TryGetValue("user", out var userJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(userJson);
                if (doc.RootElement.TryGetProperty("id", out var idEl))
                {
                    if (idEl.ValueKind == JsonValueKind.Number) eitaaUserId = idEl.GetInt64();
                    else if (idEl.ValueKind == JsonValueKind.String && long.TryParse(idEl.GetString(), out var idStr)) eitaaUserId = idStr;
                }
            }
            catch { /* پایین بررسی می‌شود */ }
        }

        if (eitaaUserId <= 0)
        {
            error = "شناسه کاربر ایتا در داده‌های دریافتی پیدا نشد.";
            return false;
        }
        return true;
    }
}
