using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;

namespace Inventory.Api.Services.Ai;

// =====================================================================
// ایجنت «فروغ آریا» — حلقه گفتگو + ابزار + حافظه مکالمه.
// اگر مدل زبانی در دسترس نباشد، خودکار به حالت آفلاین (قطعی) می‌رود تا
// دستیار هیچ‌وقت کاملاً از کار نیفتد.
// =====================================================================

public interface IAiAgentService
{
    Task<AiChatResponse> ChatAsync(int userId, string userName, bool isAdmin,
        int? conversationId, string message, string channel, CancellationToken ct);

    /// <summary>تولید متن تک‌مرحله‌ای (برای هوش نامه‌ها) — در صورت قطعی مدل null.</summary>
    Task<string?> GenerateAsync(string systemPrompt, string userPrompt, CancellationToken ct);
}

public class AiAgentService : IAiAgentService
{
    private readonly AiOptions _options;
    private readonly IAiChatClient _chat;
    private readonly AiToolRegistry _tools;
    private readonly AiConversationService _conversations;
    private readonly AiFallbackRouter _fallback;
    private readonly IServiceProvider _services;
    private readonly ILogger<AiAgentService> _log;

    public AiAgentService(
        IOptions<AiOptions> options,
        IAiChatClient chat,
        AiToolRegistry tools,
        AiConversationService conversations,
        AiFallbackRouter fallback,
        IServiceProvider services,
        ILogger<AiAgentService> log)
    {
        _options = options.Value;
        _chat = chat;
        _tools = tools;
        _conversations = conversations;
        _fallback = fallback;
        _services = services;
        _log = log;
    }

    public async Task<string?> GenerateAsync(string systemPrompt, string userPrompt, CancellationToken ct)
    {
        if (!_options.Enabled) return null;
        try
        {
            var result = await _chat.ChatAsync(new List<AiChatMessage>
            {
                new() { Role = "system", Content = systemPrompt },
                new() { Role = "user", Content = userPrompt },
            }, null, ct);
            var text = (result.Content ?? "").Trim();
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "تولید متن ناموفق بود؛ حالت آفلاین.");
            return null;
        }
    }

    public async Task<AiChatResponse> ChatAsync(int userId, string userName, bool isAdmin,
        int? conversationId, string message, string channel, CancellationToken ct)
    {
        message = (message ?? "").Trim();
        if (message.Length > 4000) message = message[..4000];
        if (string.IsNullOrWhiteSpace(message))
            return new AiChatResponse { ConversationId = conversationId ?? 0, Reply = "پیامت خالیه! 😊 یه چیزی بنویس تا کمکت کنم." };

        if (!_options.Enabled)
            return new AiChatResponse
            {
                ConversationId = conversationId ?? 0,
                Reply = "دستیار هوشمند فعلاً توسط مدیر سیستم خاموش شده است. ⛔",
                UsedFallback = true,
            };

        var conv = await _conversations.GetOrCreateAsync(userId, channel, conversationId);
        var sw = Stopwatch.StartNew();

        // سقف مصرف روزانه
        if (_options.MaxMessagesPerUserPerDay > 0)
        {
            var today = await _conversations.CountTodayAsync(userId);
            if (today >= _options.MaxMessagesPerUserPerDay)
            {
                var limitMsg = "به سقف پیام روزانه‌ات رسیدی! ⏳ فردا دوباره در خدمتم. (اگر لازم داری، مدیر سیستم می‌تونه سقف رو بیشتر کنه)";
                await _conversations.AddMessageAsync(conv.Id, "user", message);
                var limitId = await _conversations.AddMessageAsync(conv.Id, "assistant", limitMsg, null, true);
                await _services.GetRequiredService<AiAuditService>().LogAsync(userId, conv.Id, channel, message, null, false, false, "سقف پیام روزانه", sw.ElapsedMilliseconds, ct);
                return new AiChatResponse { ConversationId = conv.Id, MessageId = limitId, Reply = limitMsg, UsedFallback = true };
            }
        }

        // تاریخچه برای حافظه مکالمه (قبل از ذخیره پیام جدید)
        var existing = await _conversations.GetAsync(userId, conv.Id);
        var history = (existing?.Messages ?? new()).TakeLast(_options.MaxHistoryMessages).ToList();
        var isFirst = history.Count == 0;

        await _conversations.AddMessageAsync(conv.Id, "user", message);
        if (isFirst) await _conversations.AutoTitleAsync(conv.Id, message);

        var ctx = new AiToolContext
        {
            UserId = userId,
            UserName = userName,
            IsAdmin = isAdmin,
            Services = _services,
            CancellationToken = ct,
        };

        try
        {
            var (reply, used, attachments) = await RunAgentLoopAsync(history, message, userName, ctx, ct);
            var msgId = await _conversations.AddMessageAsync(conv.Id, "assistant", reply,
                used.Count > 0 ? string.Join(",", used.Distinct()) : null, false);
            await _services.GetRequiredService<AiAuditService>().LogAsync(userId, conv.Id, channel, message,
                used.Count > 0 ? string.Join(",", used.Distinct()) : null, true, false, null, sw.ElapsedMilliseconds, ct);
            return new AiChatResponse
            {
                ConversationId = conv.Id,
                MessageId = msgId,
                Reply = reply,
                ToolsUsed = used.Distinct().ToList(),
                Attachments = attachments,
            };
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "حلقه ایجنت ناموفق بود؛ حالت آفلاین برای کاربر {UserId}", userId);
            var offline = await _fallback.TryAnswerAsync(message, ctx);
            var reply = offline ?? "مغز متفکرم (مدل زبانی) فعلاً در دسترس نیست 😔 ولی من هنوز اینجام! " +
                "می‌تونی این‌ها رو بپرسی: «مانده مرخصی»، «فیش حقوقی»، «حضور امروز»، «نامه‌های خوانده‌نشده»، «مرخصی‌های در انتظار تأیید»، «فروش امروز» — یا بعداً دوباره تلاش کن.";
            var offId = await _conversations.AddMessageAsync(conv.Id, "assistant", reply, "offline", true);
            await _services.GetRequiredService<AiAuditService>().LogAsync(userId, conv.Id, channel, message, "offline", true, true, null, sw.ElapsedMilliseconds, ct);
            return new AiChatResponse { ConversationId = conv.Id, MessageId = offId, Reply = reply, UsedFallback = true };
        }
    }

    private async Task<(string reply, List<string> used, List<AiChatAttachment> attachments)> RunAgentLoopAsync(
        List<AiMessageDto> history, string message, string userName, AiToolContext ctx, CancellationToken ct)
    {
        var messages = new List<AiChatMessage> { new() { Role = "system", Content = BuildSystemPrompt(userName) } };
        messages.AddRange(history.Select(h => new AiChatMessage
        {
            Role = h.Role == "assistant" ? "assistant" : "user",
            Content = AiTextUtil.Truncate(h.Content, 2000),
        }));
        messages.Add(new AiChatMessage { Role = "user", Content = message });

        var schemas = _tools.Schemas();
        var used = new List<string>();
        var attachments = new List<AiChatAttachment>();

        for (var i = 0; i < _options.MaxToolIterations; i++)
        {
            var result = await _chat.ChatAsync(messages, schemas, ct);
            if (result.ToolCalls.Count == 0)
            {
                var text = (result.Content ?? "").Trim();
                if (string.IsNullOrWhiteSpace(text))
                    text = "متأسفم، نتونستم جواب بدم. 😕 سؤالت رو یه جور دیگه بپرس.";
                return (CleanReply(text), used, attachments);
            }
            messages.Add(new AiChatMessage { Role = "assistant", Content = result.Content, ToolCalls = result.ToolCalls });
            foreach (var call in result.ToolCalls)
            {
                used.Add(call.Name);
                var output = await _tools.ExecuteAsync(call.Name, call.ArgumentsJson, ctx);
                if (call.Name == "build_report")
                    TryExtractAttachment(output, attachments);
                messages.Add(new AiChatMessage
                {
                    Role = "tool",
                    ToolCallId = call.Id,
                    Content = AiTextUtil.Truncate(output, 6000),
                });
            }
        }

        // اگر مدل در حلقه ابزار گیر کرد، یک پاسخ نهایی بدون ابزار بخواه
        var final = await _chat.ChatAsync(
            messages.Concat(new[] { new AiChatMessage
            {
                Role = "user",
                Content = "با همین اطلاعاتی که به دست آوردی، جواب نهایی فارسی و خلاصه بده. اگر داده‌ای پیدا نشد صادقانه بگو.",
            } }).ToList(), null, ct);
        return (CleanReply((final.Content ?? "").Trim()), used, attachments);
    }

    private static string BuildSystemPrompt(string userName) => $"""
        تو «فروغ آریا» هستی — دستیار هوشمند فارسی سامانه یکپارچه سازمان (نرم‌افزار فروغ آریا).
        امروز {AiDateUtil.TodayFa()} است. داری با «{userName}» حرف می‌زنی.

        قوانین مهم:
        ۱) همیشه فارسی، صمیمی، کوتاه و کاربردی جواب بده. از ایموجی کم استفاده کن.
        ۲) هرگز عدد یا اطلاعات واقعی را از خودت نساز! برای هر داده واقعی (مرخصی، فیش، حضور، نامه، وام، راهنما، فروش، موجودی، چک، مطالبات، نقدینگی، هشدارها، یادآورها، صورتجلسه) حتماً ابزار مناسب را صدا بزن.
        ۳) ابزارها فقط اطلاعات خود همین کاربر را می‌دهند؛ اگر چیزی درباره شخص دیگری پرسید، مؤدبانه بگو فقط به اطلاعات خودش دسترسی داری.
        ۴) اگر ابزار خطای دسترسی (access_denied) داد، بگو «به این مورد دسترسی نداری» و راهنمایی کن از چه کسی پیگیری کند.
        ۵) وقتی ابزار لینک صفحه برگرداند، آن را دقیقاً به همین شکل در جواب بگذار: [متن](مسیر) — مثلاً [مرخصی‌های من](/fa-att/my-leaves)
        ۶) اعداد و مبالغ را در جدول یا لیست مرتب بده و تاریخ‌ها را همان‌طور که ابزار داده (شمسی) بنویس.
        ۷) اگر سؤال ربطی به سامانه و کار سازمانی ندارد، خیلی کوتاه جواب بده و برگرد به کارت.
        ۸) جواب‌های طولانی را با تیتر و لیست خوانا کن. حداکثر در حد نیاز توضیح بده.
        ۹) اقدام اجرایی فقط با این ابزارها و به‌صورت پیش‌فاکتور ثبت می‌شود — هرگز مستقیم اجرا نکن: request_leave (مرخصی)، clock (ساعت‌زنی)، request_mission (مأموریت)، decide_leave (تأیید/رد مرخصی)، answer_referral (پاسخ به ارجاع)، create_ticket (تیکت)، report_work (گزارش‌کار)، create_letter_draft (پیش‌نویس نامه)، refer_letter (ارجاع نامه به همکار)، answer_ticket (پاسخ به تیکت)، register_cheque (ثبت چک دریافتی/صادره)، decide_leave_bulk (تأیید/رد گروهی مرخصی‌ها)، create_minutes (ثبت صورتجلسه جدید).
        ۱۰) بعد از ساخت پیش‌فاکتور، خلاصه‌اش را نشان بده و بنویس: «برای تأیید بنویس: تأیید» (یا «لغو» برای انصراف).
        ۱۱) ابزار confirm_action را فقط وقتی صدا بزن که کاربر در همین گفتگو صراحتاً تأیید کرد (تأیید، باشه، اوکی، انجام بده). اگر مطمئن نیستی، بپرس.
        ۱۲) برای «تأیید مرخصی‌ها» اول pending_approvals را ببین و فهرست شماره‌دار نشان بده؛ بعد به‌ازای هر موردی که کاربر گفت، decide_leave بساز؛ اگر گفت «همه» یا چند مورد را یکجا خواست، با decide_leave_bulk فقط یک پیش‌فاکتور گروهی بساز (حداکثر ۲۰؛ بیشتر شد بگو در چند بسته). برای «ارجاع‌های بی‌پاسخ» اول my_referrals_pending را ببین؛ بعد answer_referral.
        ۱۳) در report_work اگر ابزار خطای ambiguous_project داد، نامزدها را با شناسه نشان بده و بپرس کدام؛ اگر no_project داد، نام دقیق‌تر بخواه.
        ۱۴) برای سؤال‌های مدیریتی فقط از ابزارهای فقط-خواندنی استفاده کن: sales_summary (فروش/خرید/سود دوره)، recent_invoices (آخرین فاکتورها)، stock_status (موجودی کالا یا کمبودها)، cheques_due (چک‌های نزدیک سررسید)، top_debtors (بدهکاران)، cash_status (صندوق و بانک)، my_alerts (هشدارهای امروز: کمبود انبار، چک برگشتی، پیش‌فاکتور قدیمی، تیکت جدید، قرارداد رو به اتمام، اقدام صورتجلسه)، weekly_digest (خلاصه ۷ روز گذشته برای مدیر: فروش، HR، تیکت، ارجاع، چک، گزارش‌کار — فقط با مجوز FaAtt.Manage؛ اگر کاربر مدیر نبود، بگو این گزارش فقط برای مدیران است). مبالغ را با جداکننده هزارگان + «تومان» بنویس و لینک صفحه مرتبط را بگذار: [فاکتورها](/fac/invoices) — [موجودی انبار](/inv/stock) — [چک‌ها](/trs/cheques) — [صندوق و بانک](/trs/accounts).
        ۱۵) وقتی کاربر «گزارش» خواست، گفت «به تفکیک ...»، یا جدول کامل/فایل اکسل لازم داشت، از build_report استفاده کن (نه ابزارهای خلاصه). جدول پیش‌نمایش را دقیقاً به همین شکل در جواب بگذار: یک جدول مارک‌داون (| ستون | ... | + خط | --- |) با همان ستون‌ها و حداکثر ۱۰ سطر اول + سطر جمع در انتها؛ بعد بنویس فایل اکسل کامل (N سطر) با دکمه «دانلود اکسل» زیر همین پیام. شناسه گزارش (report_id) را هرگز نشان نده.
        ۱۶) برای هر سؤال درباره داده سامانه که ابزار آماده‌اش را نداری (پرسنل، قراردادها، مرخصی‌ها، حضور، تیکت‌ها، کاربران، طرف‌حساب‌ها، کالاها، فاکتورها، چک‌ها، اسناد خزانه/حسابداری، سطرهای فاکتور و سند، نامه‌ها، ارجاع‌ها، پروژه‌ها، گزارش‌کارها، انبارها، مأموریت‌ها، وام‌ها، مانده مرخصی، کدینگ حساب‌ها، صندوق و بانک‌ها، صورتجلسه‌ها، بندهای صورتجلسه) از explore_data استفاده کن؛ اگر موجودیت را نمی‌شناسی اول data_catalog. فیلترها را فارسی بده (مثلاًKind=فروش،Status=برگشتی؛ تاریخ دقیق شمسی مثل ۱۴۰۴/۰۷/۰۵ یا «امروز»/«دیروز»؛ برای بازه نسبی مثل «این ماه»، «هفته گذشته»، «امسال» حتماً op=period). برای «به تفکیک» از group_by + agg استفاده کن. فقط وقتی کاربر فایل/اکسل/«همه» خواست excel=true بگذار (وگرنه جدول همان‌جا کافی است). خروجی را مثل قانون ۱۵ جدول مارک‌داون (حداکثر ۱۰ سطر) نشان بده و report_id را هرگز ننویس.
        ۱۷) یادآور شخصی: set_reminder (زمان فارسی آزاد مثل «فردا ساعت ۹»، «هر روز ساعت ۸ صبح»، «۲۰ دقیقه دیگه» + متن یادآوری؛ بدون نیاز به تأیید — ثبت همان انجام است)، my_reminders (فهرست فعال‌ها با شناسه)، cancel_reminder (لغو با شناسه). ارسال سر وقت در بله/ایتا انجام می‌شود؛ اگر ابزار گفت حساب لینک نیست، راهنمای لینک را بده ولی ثبت را انجام بده.
        ۱۸) صورتجلسه: draft_minutes (پیش‌نویس از متن خام جلسه: بندها با نوع مصوبه/اقدام/اطلاع + مسئول و مهلت پیشنهادی — فقط پیش‌نمایش، ذخیره نمی‌کند)، create_minutes (ثبت با تأیید؛ بندها از draft_minutes، تاریخ جلسه شمسی، حاضران حداقل یک نفر با شناسه عددی — نام را اول با users_lookup به شناسه تبدیل کن)، my_minutes_actions (بندهای باز به مسئولیت خودش + لینک). نوع بند فقط در پیش‌نمایش است و ذخیره نمی‌شود.
        ۱۹) گزارش زمان‌بندی‌شده: schedule_report (بدون نیاز به تأیید؛ kind ‏bi برای یکی از ۶ خلاصه آماده با period نسبی مثل «این هفته» — متن فقط و بدون اکسل؛ kind ‏explore برای هر سؤال روی ۲۶ موجودیت با فیلتر/گروه‌بندی — با excel=true فایل هم در بله می‌فرستد؛ اگر کاربر اکسل خواست حتماً explore)، my_schedules (فهرست با نوبت بعدی)، cancel_schedule (لغو با شناسه). دوره فارسی: «هر روز ساعت ۸»، «هر شنبه ساعت ۸»، «اول هر ماه ساعت ۹»، «آخر هر ماه». برای period در bi و کاوش، همیشه بازه نسبی بگذار تا هر دوره تازه حساب شود (نه تاریخ ثابت). ارسال در بله/ایتا؛ بدون لینک، معلق می‌ماند.
        ۲۰) جستجوی معنایی: search_docs برای «نامه/تیکت درباره X» با query (و scope اختیاری: letter/نامه، ticket/تیکت) — فقط اسناد قابل‌مشاهده خود کاربر برمی‌گردد و نامه محرمانه/سری و حذف‌شده هرگز؛ اگر نتیجه خالی بود، scope را عوض کن یا عبارت را ساده‌تر بگو. reindex_docs فقط مدیر سیستم (اگر کاربر عادی خواست، بگو به مدیر بگو). نتیجه را با عنوان، درصد شباهت و لینک نشان بده.
        ۲۱) هشدار شرطی: create_alert (بدون نیاز به تأیید) با entity + فیلترهای کاوش + op (بیشتر/کمتر/حداقل/حداکثر/برابر) + مقدار آستانه؛ agg پیش‌فرض تعداد است و برای جمع/میانگین agg_field لازم است. فقط در گذار به «برقرار» خبر می‌دهد (نه هر ۵ دقیقه)؛ موقع ثبت، مقدار فعلی نمایش داده می‌شود. my_alert_rules (فهرست با وضعیت)، delete_alert (حذف با شناسه). بدون لینک بله/ایتا خبری نمی‌رسد.
        ۲۲) بازخورد پاسخ‌ها: feedback_stats فقط مدیر سیستم (اگر کاربر عادی خواست، بگو به مدیر بگو) — آمار رضایت، دلیل‌های نارضایتی و ابزارهای دخیل در پاسخ‌های ضعیف؛ فهرست جزئی در صفحه «بازبینی بازخوردها» (/ai-feedback). هرگز از کاربر در چت نخواه که امتیاز بدهد؛ دکمه‌ها زیر هر پاسخ هستند.
        ۲۳) حسابرسی: audit_stats فقط مدیر سیستم (اگر کاربر عادی خواست، بگو به مدیر بگو) — آمار نوبت‌ها، موفقیت، ابزارها و کاربران پرکاربرد و اقدام‌های تأییدی؛ فهرست جزئی در صفحه «حسابرسی دستیار» (/ai-audit).
        """;

    /// <summary>استخراج پیوست اکسل از خروجی build_report (شناسه گزارش → دکمه دانلود زیر پیام).</summary>
    private static void TryExtractAttachment(string output, List<AiChatAttachment> attachments)
    {
        try
        {
            using var doc = JsonDocument.Parse(output);
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object) return;
            if (!r.TryGetProperty("report_id", out var id) || id.GetString() is not { Length: > 0 } reportId) return;
            if (attachments.Any(a => a.ReportId == reportId)) return;
            attachments.Add(new AiChatAttachment
            {
                ReportId = reportId,
                Title = r.TryGetProperty("title", out var t) ? t.GetString() ?? "گزارش" : "گزارش",
                TotalRows = r.TryGetProperty("total_rows", out var n) && n.TryGetInt32(out var c) ? c : 0,
            });
        }
        catch { /* پیوست اختیاری است */ }
    }

    private static string CleanReply(string text)
    {
        // حذف اکوی احتمالی JSON ابزار از انتهای پاسخ مدل‌های کوچک
        var idx = text.IndexOf("{\"", StringComparison.Ordinal);
        if (idx > 100) text = text[..idx].Trim();
        return text;
    }
}
