using System.Text;
using System.Text.Json;

namespace Inventory.Api.Services.Ai;

// =====================================================================
// هوش نامه‌ها (قابلیت‌های ۶ تا ۱۱ + پاسخ‌نویسی و پیشنهاد گیرنده):
// پیش‌نویس، خلاصه، پیشنهاد ارجاع، استخراج اقدام، دسته‌بندی، پیش‌نویس صورتجلسه،
// پیش‌نویس پاسخ با یک کلیک، پیشنهاد گیرنده هنگام نوشتن نامه.
// همه متدها اگر مدل زبانی در دسترس نباشد، خروجی قطعی (قاعده‌محور) می‌دهند.
// =====================================================================

public interface ILetterAiService
{
    Task<AiLetterDraftDto> DraftAsync(AiLetterDraftRequest req, CancellationToken ct);
    Task<AiLetterSummaryDto?> SummarizeAsync(int letterId, int userId, bool isAdmin, CancellationToken ct);
    Task<AiReferralSuggestResponse?> SuggestReferralAsync(int letterId, int userId, bool isAdmin, CancellationToken ct);
    Task<AiLetterTasksResponse?> ExtractTasksAsync(int letterId, int userId, bool isAdmin, CancellationToken ct);
    Task<AiLetterCategoryDto?> CategorizeAsync(int letterId, int userId, bool isAdmin, CancellationToken ct);
    Task<AiMinutesDraftDto> DraftMinutesAsync(AiMinutesDraftRequest req, CancellationToken ct);
    Task<AiLetterDraftDto?> DraftReplyAsync(int letterId, int userId, bool isAdmin, string? hint, CancellationToken ct);
    Task<AiReceiversSuggestResponse> SuggestReceiversAsync(AiReceiversSuggestRequest? req, int userId, CancellationToken ct);
}

public class LetterAiService : ILetterAiService
{
    private readonly IAiAgentService _agent;
    private readonly AiToolRegistry _tools;
    private readonly IServiceProvider _services;
    private readonly ILogger<LetterAiService> _log;

    public LetterAiService(IAiAgentService agent, AiToolRegistry tools, IServiceProvider services, ILogger<LetterAiService> log)
    {
        _agent = agent;
        _tools = tools;
        _services = services;
        _log = log;
    }

    // ==================== ۶) پیش‌نویس نامه ====================

    public async Task<AiLetterDraftDto> DraftAsync(AiLetterDraftRequest req, CancellationToken ct)
    {
        var topic = (req.Topic ?? "").Trim();
        if (topic.Length == 0) throw new ArgumentException("موضوع نامه را بنویسید.");
        var to = string.IsNullOrWhiteSpace(req.ToName) ? "" : $"گیرنده: {req.ToName}\n";
        var extra = string.IsNullOrWhiteSpace(req.ExtraContext) ? "" : $"توضیحات: {req.ExtraContext}\n";

        var gen = await _agent.GenerateAsync(
            "تو نویسنده نامه‌های اداری فارسی هستی. فقط JSON معتبر (بدون هیچ متن اضافه) با همین کلیدها بده: " +
            "{\"subject\": \"موضوع کوتاه نامه\", \"body\": \"متن کامل نامه با پاراگراف‌های جدا (با خط خالی)\"}. " +
            "متن باید رسمی، مؤدبانه و آماده ارسال باشد؛ جای امضا را «[نام و امضا]» بگذار.",
            $"نوع نامه: {req.Kind}\nلحن: {req.Tone}\n{to}{extra}موضوع/خواسته: {topic}",
            ct);
        var parsed = ParseJsonObject(gen);
        if (parsed != null && parsed.Value.TryGetProperty("body", out var body) && body.GetString() is { Length: > 0 } bodyText)
        {
            var subject = parsed.Value.TryGetProperty("subject", out var s) ? s.GetString() ?? topic : topic;
            return new AiLetterDraftDto { Subject = subject, BodyHtml = ToHtml(bodyText) };
        }
        return new AiLetterDraftDto { Subject = topic, BodyHtml = ToHtml(FallbackDraftBody(topic, req.Kind, req.ToName)), UsedFallback = true };
    }

    private static string FallbackDraftBody(string topic, string kind, string? toName)
    {
        var dear = string.IsNullOrWhiteSpace(toName) ? "مدیر محترم" : toName.Trim();
        return $"با سلام و احترام،\n\nاحتراماً به استحضار {dear} می‌رساند: {topic}.\n\n" +
               $"خواهشمند است دستور فرمایید در این خصوص بررسی و اقدام لازم صورت گیرد.\n\nبا تشکر\n[نام و امضا]";
    }

    // ==================== پاسخ‌نویسی با یک کلیک ====================

    public async Task<AiLetterDraftDto?> DraftReplyAsync(int letterId, int userId, bool isAdmin, string? hint, CancellationToken ct)
    {
        var data = await LoadLetterContextAsync(letterId, userId, isAdmin, ct);
        if (data == null) return null;
        var hintLine = string.IsNullOrWhiteSpace(hint) ? "" : $"خواسته کاربر از پاسخ: {hint.Trim()}\n";

        var gen = await _agent.GenerateAsync(
            "تو نویسنده پاسخ نامه‌های اداری فارسی هستی. فقط JSON معتبر (بدون هیچ متن اضافه) با همین کلیدها بده: " +
            "{\"subject\": \"موضوع پاسخ (کوتاه، با پیشوند «در پاسخ به: ...»)\", \"body\": \"متن کامل پاسخ\"}. " +
            "قواعد: پاراگراف اول با «عطف به نامه شماره ... مورخ ...» شروع شود؛ به خواسته اصلی نامه جواب بده؛ " +
            "لحن رسمی و مؤدبانه؛ اگر خواسته کاربر با متن نامه ناسازگار بود خواسته کاربر ملاک است؛ جای امضا «[نام و امضا]».",
            $"{hintLine}{data.PromptText}",
            ct);
        var parsed = ParseJsonObject(gen);
        if (parsed != null && parsed.Value.TryGetProperty("body", out var body) && body.GetString() is { Length: > 0 } bodyText)
        {
            var subject = parsed.Value.TryGetProperty("subject", out var s) && s.GetString() is { Length: > 0 } st
                ? st : $"در پاسخ به: {data.Title}";
            return new AiLetterDraftDto { Subject = subject, BodyHtml = ToHtml(bodyText) };
        }
        return new AiLetterDraftDto
        {
            Subject = $"در پاسخ به: {data.Title}",
            BodyHtml = ToHtml(FallbackReplyBody(data)),
            UsedFallback = true,
        };
    }

    private static string FallbackReplyBody(LetterContext data) =>
        $"با سلام و احترام،\n\nعطف به نامه شماره {data.Number} مورخ {AiDateUtil.ToFaShort(data.Date)} با موضوع «{data.Title}»، " +
        $"به استحضار می‌رساند موضوع در دست بررسی است و نتیجه متعاقباً اعلام می‌گردد.\n\nبا تشکر\n[نام و امضا]";

    // ==================== ۷) خلاصه نامه + وضعیت گردش ====================

    public async Task<AiLetterSummaryDto?> SummarizeAsync(int letterId, int userId, bool isAdmin, CancellationToken ct)
    {
        var data = await LoadLetterContextAsync(letterId, userId, isAdmin, ct);
        if (data == null) return null;

        var gen = await _agent.GenerateAsync(
            "تو خلاصه‌کننده نامه‌های اداری فارسی هستی. فقط JSON معتبر بده (بدون متن اضافه): " +
            "{\"summary\": \"خلاصه ۳ تا ۶ خطی: اصل موضوع، خواسته اصلی، نکات مهم\", " +
            "\"status\": \"یک خط: الان نامه دست کیست و آخرین اقدام چه بوده\"}.",
            data.PromptText, ct);
        var parsed = ParseJsonObject(gen);
        if (parsed != null && parsed.Value.TryGetProperty("summary", out var s) && s.GetString() is { Length: > 0 } summary)
        {
            var status = parsed.Value.TryGetProperty("status", out var st) ? st.GetString() ?? "" : "";
            return new AiLetterSummaryDto { LetterId = letterId, Summary = summary, CurrentStatus = status };
        }
        return new AiLetterSummaryDto
        {
            LetterId = letterId,
            Summary = $"«{data.Title}» از {data.Sender} — {AiTextUtil.Truncate(data.PlainText, 400)}",
            CurrentStatus = data.FallbackStatus,
            UsedFallback = true,
        };
    }

    // ==================== ۸) پیشنهاد ارجاع ====================

    public async Task<AiReferralSuggestResponse?> SuggestReferralAsync(int letterId, int userId, bool isAdmin, CancellationToken ct)
    {
        var data = await LoadLetterContextAsync(letterId, userId, isAdmin, ct);
        if (data == null) return null;
        var (list, usedFallback) = await SuggestPeopleAsync(data.Title, data.PlainText, userId, ct);
        return new AiReferralSuggestResponse { LetterId = letterId, Suggestions = list, UsedFallback = usedFallback };
    }

    // ==================== پیشنهاد گیرنده هنگام نوشتن نامه ====================

    public async Task<AiReceiversSuggestResponse> SuggestReceiversAsync(AiReceiversSuggestRequest? req, int userId, CancellationToken ct)
    {
        var title = (req?.Title ?? "").Trim();
        var plain = AiTextUtil.StripHtml(req?.Text);
        if (title == "" && plain == "") throw new ArgumentException("اول موضوع یا متن نامه را بنویسید.");
        var (list, usedFallback) = await SuggestPeopleAsync(title, plain, userId, ct);
        return new AiReceiversSuggestResponse { Suggestions = list, UsedFallback = usedFallback };
    }

    /// <summary>موتور مشترک پیشنهاد همکار از روی موضوع+متن (هم ارجاع، هم گیرنده نامه جدید).</summary>
    private async Task<(List<AiReferralSuggestionDto> list, bool usedFallback)> SuggestPeopleAsync(
        string title, string plainText, int userId, CancellationToken ct)
    {
        var ctx = ToolCtx(userId, ct);
        var usersJson = await _tools.ExecuteAsync("users_lookup", "{\"search\":\"\"}", ctx);
        var candidates = ParseUsers(usersJson);

        var gen = await _agent.GenerateAsync(
            "تو کارشناس ارجاع نامه‌های اداری هستی. با توجه به موضوع نامه، از فهرست همکاران «حداکثر ۳ نفر» مناسب برای ارجاع را انتخاب کن. " +
            "فقط JSON معتبر بده (بدون متن اضافه): {\"suggestions\": [{\"user_id\": 12, \"reason\": \"دلیل کوتاه فارسی\"}]}. " +
            "user_id باید دقیقاً از فهرست باشد. اگر کسی مناسب نیست آرایه خالی بده.",
            $"موضوع: {title}\nمتن: {AiTextUtil.Truncate(plainText, 2500)}\n\nهمکاران:\n{AiTextUtil.Truncate(usersJson, 6000)}",
            ct);
        var parsed = ParseJsonObject(gen);
        if (parsed != null && parsed.Value.TryGetProperty("suggestions", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            var byId = candidates.ToDictionary(c => c.Id);
            var list = new List<AiReferralSuggestionDto>();
            foreach (var s in arr.EnumerateArray().Take(3))
            {
                var id = s.TryGetProperty("user_id", out var u) && u.TryGetInt32(out var n) ? n : 0;
                if (id > 0 && byId.TryGetValue(id, out var c))
                    list.Add(new AiReferralSuggestionDto
                    {
                        UserId = id,
                        Name = c.Name,
                        Department = c.Dept,
                        Reason = s.TryGetProperty("reason", out var r) ? r.GetString() ?? "" : "",
                    });
            }
            if (list.Count > 0)
                return (list, false);
        }
        // حالت قطعی: تطبیق موضوعی واحد سازمانی
        return (FallbackReferral(plainText + " " + title, candidates), true);
    }

    private record Candidate(int Id, string Name, string? Dept);

    private static List<Candidate> ParseUsers(string json)
    {
        var list = new List<Candidate>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return list;
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                var id = e.TryGetProperty("شناسه", out var i) && i.TryGetInt32(out var n) ? n : 0;
                var name = e.TryGetProperty("نام", out var nm) ? nm.GetString() ?? "" : "";
                var dept = e.TryGetProperty("واحد", out var d) ? d.GetString() : null;
                if (id > 0 && name != "") list.Add(new Candidate(id, name, dept));
            }
        }
        catch { /* نادیده */ }
        return list;
    }

    private static List<AiReferralSuggestionDto> FallbackReferral(string text, List<Candidate> candidates)
    {
        var tokens = AiTextUtil.Tokens(text);
        return candidates
            .Select(c => new
            {
                C = c,
                Score = AiTextUtil.Tokens(c.Name + " " + (c.Dept ?? "")).Count(t => t.Length >= 3 && tokens.Any(q => q.Contains(t) || t.Contains(q))),
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Take(3)
            .Select(x => new AiReferralSuggestionDto
            {
                UserId = x.C.Id,
                Name = x.C.Name,
                Department = x.C.Dept,
                Reason = $"تطبیق موضوعی با {(x.C.Dept != null ? $"واحد «{x.C.Dept}»" : "نام همکار")} (پیشنهاد خودکار)",
            })
            .ToList();
    }

    // ==================== ۹) استخراج اقدام‌ها و مهلت‌ها ====================

    public async Task<AiLetterTasksResponse?> ExtractTasksAsync(int letterId, int userId, bool isAdmin, CancellationToken ct)
    {
        var data = await LoadLetterContextAsync(letterId, userId, isAdmin, ct);
        if (data == null) return null;

        var gen = await _agent.GenerateAsync(
            $"تو استخراج‌کننده اقدام‌ها از نامه فارسی هستی. امروز {AiDateUtil.TodayFa()} است. " +
            "کارها، دستورها و مهلت‌ها را پیدا کن و فقط JSON معتبر بده (بدون متن اضافه): " +
            "{\"tasks\": [{\"action\": \"شرح کار\", \"assignee\": \"نام مسئول اگر در متن بود وگرنه خالی\", " +
            "\"deadline_text\": \"متن مهلت اگر بود\", \"deadline_date\": \"تاریخ میلادی YYYY-MM-DD اگر قابل تشخیص بود وگرنه null\"}]}. " +
            "اگر کاری در نامه نیست آرایه خالی بده.",
            $"موضوع: {data.Title}\nمتن: {AiTextUtil.Truncate(data.PlainText, 3000)}",
            ct);
        var parsed = ParseJsonObject(gen);
        if (parsed != null && parsed.Value.TryGetProperty("tasks", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            var tasks = new List<AiLetterTaskDto>();
            foreach (var t in arr.EnumerateArray().Take(10))
            {
                var action = t.TryGetProperty("action", out var a) ? a.GetString() ?? "" : "";
                if (string.IsNullOrWhiteSpace(action)) continue;
                DateTime? deadline = null;
                if (t.TryGetProperty("deadline_date", out var dd) && dd.ValueKind == JsonValueKind.String
                    && DateTime.TryParse(dd.GetString(), out var parsedDate))
                    deadline = parsedDate;
                tasks.Add(new AiLetterTaskDto
                {
                    Action = action,
                    AssigneeHint = t.TryGetProperty("assignee", out var asg) ? asg.GetString() : null,
                    DeadlineText = t.TryGetProperty("deadline_text", out var dt) ? dt.GetString() : null,
                    DeadlineDate = deadline,
                });
            }
            return new AiLetterTasksResponse { LetterId = letterId, Tasks = tasks };
        }
        return new AiLetterTasksResponse { LetterId = letterId, Tasks = FallbackTasks(data.PlainText), UsedFallback = true };
    }

    private static List<AiLetterTaskDto> FallbackTasks(string text)
    {
        // حالت قطعی: جمله‌هایی که بوی دستور/مهلت می‌دهند
        var tasks = new List<AiLetterTaskDto>();
        var sentences = text.Split(new[] { '\n', '。', '.', '؛' }, StringSplitOptions.RemoveEmptyEntries);
        string[] cues = { "لطفا", "لطفاً", "خواهشمند", "مقرر", "ضروری است", "لازم است", "دستور فرمایید", "اقدام", "مهلت", "حداکثر", "تا تاریخ", "موظف" };
        var dateRx = new System.Text.RegularExpressions.Regex(@"14\d\d/\d{1,2}/\d{1,2}");
        foreach (var s in sentences)
        {
            var t = s.Trim();
            if (t.Length is < 10 or > 300) continue;
            var norm = AiTextUtil.NormalizeFa(t);
            if (!cues.Any(c => norm.Contains(AiTextUtil.NormalizeFa(c)))) continue;
            var dm = dateRx.Match(t);
            tasks.Add(new AiLetterTaskDto
            {
                Action = t,
                DeadlineText = dm.Success ? dm.Value : null,
            });
            if (tasks.Count >= 5) break;
        }
        return tasks;
    }

    // ==================== ۱۱) دسته‌بندی و اولویت ====================

    public async Task<AiLetterCategoryDto?> CategorizeAsync(int letterId, int userId, bool isAdmin, CancellationToken ct)
    {
        var data = await LoadLetterContextAsync(letterId, userId, isAdmin, ct);
        if (data == null) return null;

        var gen = await _agent.GenerateAsync(
            "تو دسته‌بند نامه‌های اداری فارسی هستی. فقط JSON معتبر بده (بدون متن اضافه): " +
            "{\"category\": \"یکی از: مالی، منابع انسانی، بازرگانی و انبار، فناوری اطلاعات، اداری، حقوقی، تولید، فروش، سایر\", " +
            "\"priority\": عدد ۱ (کم) تا ۵ (خیلی فوری), \"tags\": [\"حداکثر ۴ برچسب فارسی کوتاه\"]}.",
            $"موضوع: {data.Title}\nفوریت ثبت‌شده: {data.Priority}\nمتن: {AiTextUtil.Truncate(data.PlainText, 2500)}",
            ct);
        var parsed = ParseJsonObject(gen);
        if (parsed != null && parsed.Value.TryGetProperty("category", out var c) && c.GetString() is { Length: > 0 } category)
        {
            var prio = parsed.Value.TryGetProperty("priority", out var p) && p.TryGetInt32(out var n) ? Math.Clamp(n, 1, 5) : 3;
            var tags = new List<string>();
            if (parsed.Value.TryGetProperty("tags", out var tg) && tg.ValueKind == JsonValueKind.Array)
                tags = tg.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x != "").Take(4).ToList();
            return new AiLetterCategoryDto { LetterId = letterId, Category = category, Priority = prio, Tags = tags };
        }
        var (fbCat, fbPrio) = FallbackCategory(data.Title + " " + data.PlainText, data.Priority);
        return new AiLetterCategoryDto { LetterId = letterId, Category = fbCat, Priority = fbPrio, Tags = new(), UsedFallback = true };
    }

    private static (string category, int priority) FallbackCategory(string text, string foriat)
    {
        var t = AiTextUtil.NormalizeFa(text);

        static bool HasAny(string text, params string[] words) =>
            words.Any(w => text.Contains(AiTextUtil.NormalizeFa(w)));
        string cat = "اداری";
        if (HasAny(t, "فاکتور", "پرداخت", "بودجه", "حساب", "مالیات", "چک", "حقوق")) cat = "مالی";
        else if (HasAny(t, "مرخصی", "استخدام", "حقوق", "پرسنل", "کارمند", "بیمه", "قرارداد کار")) cat = "منابع انسانی";
        else if (HasAny(t, "خرید", "انبار", "کالا", "موجودی", "فروش", "مشتری", "تامین کننده")) cat = "بازرگانی و انبار";
        else if (HasAny(t, "سیستم", "سرور", "نرم افزار", "کامپیوتر", "شبکه", "خرابی", "آی تی")) cat = "فناوری اطلاعات";
        else if (HasAny(t, "دادگاه", "شکایت", "وکالت", "قرارداد")) cat = "حقوقی";
        var prio = AiTextUtil.NormalizeFa(foriat) switch
        {
            var f when f.Contains("خیلی فوری") || f.Contains("آنی") => 5,
            var f when f.Contains("فوری") => 4,
            _ => t.Contains("فوری") || t.Contains("سریع") || t.Contains("عجله") ? 4 : 3,
        };
        return (cat, prio);
    }

    // ==================== ۱۰) پیش‌نویس صورتجلسه ====================

    public async Task<AiMinutesDraftDto> DraftMinutesAsync(AiMinutesDraftRequest req, CancellationToken ct)
    {
        var raw = (req.RawText ?? "").Trim();
        if (raw.Length < 10) throw new ArgumentException("متن جلسه خیلی کوتاه است.");
        var gen = await _agent.GenerateAsync(
            "تو تنظیم‌کننده صورتجلسه فارسی هستی. از متن خام جلسه، خلاصه و قلم‌های مصوبه/اقدام/اطلاع را استخراج کن. " +
            "فقط JSON معتبر بده (بدون متن اضافه): {\"title\": \"عنوان جلسه\", \"summary\": \"خلاصه ۲-۳ خطی\", " +
            "\"items\": [{\"text\": \"متن قلم\", \"kind\": \"مصوبه یا اقدام یا اطلاع\", \"responsible\": \"مسئول اگر مشخص بود\", \"deadline\": \"مهلت اگر بود\"}]}.",
            $"عنوان پیشنهادی: {req.Title}\n\nمتن جلسه:\n{AiTextUtil.Truncate(raw, 6000)}",
            ct);
        var parsed = ParseJsonObject(gen);
        if (parsed != null && parsed.Value.TryGetProperty("items", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            var items = new List<AiMinutesItemDto>();
            foreach (var i in arr.EnumerateArray().Take(20))
            {
                var text = i.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
                if (string.IsNullOrWhiteSpace(text)) continue;
                var kind = i.TryGetProperty("kind", out var k) ? k.GetString() ?? "مصوبه" : "مصوبه";
                if (kind is not ("مصوبه" or "اقدام" or "اطلاع")) kind = "مصوبه";
                items.Add(new AiMinutesItemDto
                {
                    Text = text,
                    Kind = kind,
                    Responsible = i.TryGetProperty("responsible", out var r) ? r.GetString() : null,
                    DeadlineText = i.TryGetProperty("deadline", out var d) ? d.GetString() : null,
                });
            }
            if (items.Count > 0)
                return new AiMinutesDraftDto
                {
                    Title = parsed.Value.TryGetProperty("title", out var tt) ? tt.GetString() ?? req.Title ?? "صورتجلسه" : req.Title ?? "صورتجلسه",
                    Summary = parsed.Value.TryGetProperty("summary", out var ss) ? ss.GetString() ?? "" : "",
                    Items = items,
                };
        }
        // حالت قطعی: هر خط معنادار = یک قلم
        var fbItems = raw.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim().TrimStart('-', '*', '•', '۱', '۲', '۳', '۴', '۵', '۶', '۷', '۸', '۹', '۰', '.', ')', ':').Trim())
            .Where(l => l.Length >= 5)
            .Take(20)
            .Select(l => new AiMinutesItemDto { Text = l, Kind = "مصوبه" })
            .ToList();
        return new AiMinutesDraftDto
        {
            Title = string.IsNullOrWhiteSpace(req.Title) ? "صورتجلسه" : req.Title.Trim(),
            Summary = "",
            Items = fbItems,
            UsedFallback = true,
        };
    }

    // ==================== ابزارهای مشترک ====================

    private record LetterContext(string Number, string Title, string Sender, string PlainText, string Priority, DateTime Date, string PromptText, string FallbackStatus);

    private async Task<LetterContext?> LoadLetterContextAsync(int letterId, int userId, bool isAdmin, CancellationToken ct)
    {
        var letters = _services.GetRequiredService<IInnerLetterService>();
        var erja = _services.GetRequiredService<IErjaService>();
        var d = await letters.GetDetailAsync(letterId, userId, isAdmin);
        if (d == null) return null;
        var plain = AiTextUtil.StripHtml(d.Text);
        var chainText = "";
        var fallbackStatus = "بدون ارجاع ثبت‌شده.";
        try
        {
            var tree = await erja.GetGardeshTreeAsync(letterId, userId, isAdmin);
            var nodes = tree.Take(15).ToList();
            if (nodes.Count > 0)
            {
                var sb = new StringBuilder();
                foreach (var n in nodes)
                    sb.AppendLine($"- {n.Sender} ← {n.Reciver} ({AiDateUtil.ToFaShort(n.Date)}): {AiTextUtil.Truncate(AiTextUtil.StripHtml(n.MatnErja), 200)}");
                chainText = sb.ToString();
                var last = nodes[^1];
                fallbackStatus = $"آخرین ارجاع: {last.Sender} به {last.Reciver} در {AiDateUtil.ToFaShort(last.Date)}.";
            }
        }
        catch (Exception ex) { _log.LogDebug(ex, "خواندن گردش نامه {LetterId} ناموفق بود.", letterId); }

        var prompt = $"شماره: {d.LetterNumber}\nعنوان: {d.Title}\nفرستنده: {d.SenderName}\n" +
                     $"تاریخ: {AiDateUtil.ToFaShort(d.DateSabt)}\nفوریت: {d.Foriat} — محرمانگی: {d.Mahramanegi}\n" +
                     $"متن:\n{AiTextUtil.Truncate(plain, 4000)}\n" +
                     (chainText != "" ? $"\nگردش ارجاع‌ها:\n{chainText}" : "");
        return new LetterContext(d.LetterNumber, d.Title, d.SenderName, plain, d.Foriat, d.DateSabt, prompt, fallbackStatus);
    }

    private AiToolContext ToolCtx(int userId, CancellationToken ct) => new()
    {
        UserId = userId,
        Services = _services,
        CancellationToken = ct,
    };

    private static JsonElement? ParseJsonObject(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        try
        {
            // حذف قاب markdown و نویز اطراف JSON
            var t = text.Trim();
            var fence = t.IndexOf("```", StringComparison.Ordinal);
            if (fence >= 0)
            {
                var end = t.IndexOf("```", fence + 3, StringComparison.Ordinal);
                if (end > fence) t = t.Substring(fence + 3, end - fence - 3);
                t = t.Trim();
                if (t.StartsWith("json", StringComparison.OrdinalIgnoreCase)) t = t[4..].Trim();
            }
            var start = t.IndexOf('{');
            var endBrace = t.LastIndexOf('}');
            if (start < 0 || endBrace <= start) return null;
            using var doc = JsonDocument.Parse(t.Substring(start, endBrace - start + 1));
            return doc.RootElement.Clone();
        }
        catch
        {
            return null;
        }
    }

    private static string ToHtml(string plainText)
    {
        var sb = new StringBuilder();
        foreach (var para in plainText.Split(new[] { "\n\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            var p = para.Trim();
            if (p.Length == 0) continue;
            sb.Append("<p>").Append(System.Net.WebUtility.HtmlEncode(p).Replace("\n", "<br>")).AppendLine("</p>");
        }
        return sb.ToString();
    }
}
