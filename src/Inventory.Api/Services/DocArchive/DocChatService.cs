using System.Text;
using Inventory.Api.Data;
using Inventory.Api.Services.Ai;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Inventory.Api.Services.DocArchive;

// =====================================================================
// گفتگو با سند — پرسش آزاد دربارهٔ متن استخراج‌شدهٔ یک مدرک.
// مسیر اصلی: مدل زبانی لوکال (Ollama با قرارداد OpenAI) با زمینهٔ بازیابی‌شده (RAG).
// مسیر پشتیبان: اگر مدل در دسترس نبود، نزدیک‌ترین جمله‌های متن سند برگردانده می‌شود.
// دیده‌بانی: فقط کاربرانی که حداقل دسترسی «مشاهده» روی مدرک دارند.
// =====================================================================

public interface IDocChatService
{
    Task<DocChatResponse> AskAsync(int userId, bool isManager, int documentId, DocChatRequest req, CancellationToken ct);
}

public class DocChatService : IDocChatService
{
    private const int ChunkSize = 700;
    private const int MaxChunks = 6;

    private static readonly HashSet<string> Stop = new()
    {
        "و","در","را","به","از","که","است","چه","چند","کجا","آیا","با","برای","روی","این","آن","ها","یک","می","اینکه","چطور","لطفا","بگو","بگویید"
    };

    private readonly AppDbContext _db;
    private readonly IDocAccessService _access;
    private readonly IAiChatClient _chat;
    private readonly AiOptions _ai;
    private readonly ILogger<DocChatService> _log;

    public DocChatService(AppDbContext db, IDocAccessService access, IAiChatClient chat,
        IOptions<AiOptions> ai, ILogger<DocChatService> log)
    {
        _db = db; _access = access; _chat = chat; _ai = ai.Value; _log = log;
    }

    public async Task<DocChatResponse> AskAsync(int userId, bool isManager, int documentId, DocChatRequest req, CancellationToken ct)
    {
        var (level, _) = await _access.DocumentAccessAsync(userId, isManager, documentId);
        if (level == DocAccessLevel.None)
            throw new InvalidOperationException("به این مدرک دسترسی ندارید.");

        var doc = await _db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == documentId, ct)
            ?? throw new InvalidOperationException("مدرک یافت نشد.");

        var texts = await _db.DocExtractedTexts.AsNoTracking()
            .Where(x => x.DocumentId == documentId)
            .Select(x => new { x.FileName, x.NormalizedText, x.ExtractedText })
            .ToListAsync(ct);

        var usable = texts
            .Select(t => (t.FileName, Body: string.IsNullOrWhiteSpace(t.NormalizedText) ? t.ExtractedText : t.NormalizedText))
            .Where(t => !string.IsNullOrWhiteSpace(t.Body))
            .ToList();

        if (usable.Count == 0)
            return new DocChatResponse
            {
                HasText = false,
                Mode = "notext",
                Answer = "متن این سند هنوز استخراج نشده است. در همین صفحه، بخش «متن استخراج‌شده» دکمهٔ «اجرای مجدد OCR / استخراج متن» را بزنید؛ پس از پایان، دوباره گفتگو کنید."
            };

        // ---------- تکه‌سازی متن ----------
        var chunks = new List<(string File, string Text)>();
        foreach (var t in usable)
            foreach (var c in Chunk(t.Body!))
                chunks.Add((t.FileName, c));

        // ---------- امتیازدهی به تکه‌ها نسبت به پرسش ----------
        var qTokens = Expand(Tokens(req.Question));
        var scored = chunks
            .Select(c => (c, Score(c.Text, qTokens)))
            .OrderByDescending(x => x.Item2)
            .ToList();

        var top = scored.Take(MaxChunks).Select(x => x.c).ToList();
        var sources = top.Select(t => t.File).Distinct().ToList();
        var context = string.Join("\n\n---\n\n", top.Select(t => t.Text));

        // ---------- مسیر اصلی: مدل زبانی ----------
        if (_ai.Enabled)
        {
            try
            {
                var (ok, _) = await _chat.CheckHealthAsync(ct);
                if (ok)
                {
                    var msgs = new List<AiChatMessage>
                    {
                        new() { Role = "system", Content = SystemPrompt(doc.Title) }
                    };
                    foreach (var h in req.History.TakeLast(6))
                        msgs.Add(new AiChatMessage { Role = h.Role == "assistant" ? "assistant" : "user", Content = h.Text });
                    msgs.Add(new AiChatMessage
                    {
                        Role = "user",
                        Content = $"متن سند «{doc.Title}»:\n{context}\n\nپرسش کاربر: {req.Question}"
                    });

                    var res = await _chat.ChatAsync(msgs, null, ct);
                    if (!string.IsNullOrWhiteSpace(res.Content))
                        return new DocChatResponse
                        {
                            Mode = "ai", HasText = true,
                            Answer = res.Content.Trim(),
                            Sources = sources
                        };
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning("گفتگوی هوشمند با مدل ناموفق بود؛ پاسخ از متن سند ساخته می‌شود. ({Msg})", ex.Message);
            }
        }

        // ---------- مسیر پشتیبان: بازیابی نزدیک‌ترین جمله‌ها ----------
        var sentences = top
            .SelectMany(t => SplitSentences(t.Text))
            .Distinct()
            .Select(s => (s, SentScore(s, qTokens)))
            .Where(x => x.Item2 > 0)
            .OrderByDescending(x => x.Item2)
            .Take(5)
            .Select(x => x.s)
            .ToList();

        string answer;
        if (sentences.Count > 0)
        {
            answer = "مدل زبانی در دسترس نیست؛ نزدیک‌ترین بخش‌های متن سند به پرسش شما:\n• " +
                     string.Join("\n• ", sentences);
        }
        else
        {
            var head = string.Join(" ", top.Take(2).Select(t => t.Text));
            if (head.Length > 600) head = head[..600] + "…";
            answer = "بخش دقیقی مرتبط با پرسش پیدا نشد. آغاز متن سند:\n" + head;
        }

        return new DocChatResponse { Mode = "retrieval", HasText = true, Answer = answer, Sources = sources };
    }

    // ------------------------------------------------------------------

    private static string SystemPrompt(string title) =>
        $"تو دستیار اسناد «فروغ آریا» هستی و فقط دربارهٔ سند «{title}» پاسخ می‌دهی.\n" +
        "قواعد: ۱) فقط بر اساس «متن سند» که به تو داده شده جواب بده و چیزی از خودت اضافه نکن. " +
        "۲) اگر جواب در متن نیست، صریح بگو «در متن سند نیست». " +
        "۳) فارسی، کوتاه و مرتب جواب بده؛ مبلغ‌ها و تاریخ‌ها را دقیقاً مانند متن سند بنویس. " +
        "۴) اگر خلاصه خواسته شد، مهم‌ترین نکته‌ها را در چند جمله بگو.";

    private static IEnumerable<string> Chunk(string text)
    {
        text = text.Replace("\r", "").Trim();
        if (text.Length == 0) yield break;
        if (text.Length <= ChunkSize) { yield return text; yield break; }

        var start = 0;
        while (start < text.Length)
        {
            var end = Math.Min(start + ChunkSize, text.Length);
            if (end < text.Length)
            {
                var cut = text.LastIndexOfAny(new[] { '\n', '.', '۔', '!', '?' }, end, Math.Min(end - start, 250));
                if (cut > start + ChunkSize / 2) end = cut + 1;
            }
            yield return text[start..end].Trim();
            if (end >= text.Length) yield break;
            start = end;
        }
    }

    /// <summary>هم‌معنی‌های پرتکرار فارسی تا بازیابی، پرسش‌های کلی را هم خوب جواب بدهد.</summary>
    private static List<string> Expand(List<string> tokens)
    {
        var extra = new List<string>();
        if (tokens.Any(t => t is "طرفین" or "طرف" or "کیستند" or "نام" or "اسم"))
            extra.AddRange(new[] { "موجر", "مستاجر", "نمایندگی" });
        if (tokens.Any(t => t is "مبلغ" or "ارقام" or "قیمت" or "هزینه"))
            extra.AddRange(new[] { "ریال", "مبلغ" });
        if (tokens.Any(t => t is "تاریخ" or "مهلت" or "زمان"))
            extra.AddRange(new[] { "تاریخ", "مهلت" });
        if (tokens.Any(t => t is "خلاصه"))
            extra.AddRange(new[] { "قرارداد", "سند" });
        return tokens.Concat(extra).Distinct().ToList();
    }

    private static List<string> Tokens(string s) =>
        (s ?? "").ToLower()
            .Split(new[] { ' ', '،', '،', '.', '?', '!', '؟', ':', '؛', '\n', '\t', '(', ')', '"', '\'' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length >= 3 && !Stop.Contains(t))
            .Distinct()
            .ToList();

    private static double Score(string chunk, List<string> qTokens)
    {
        if (qTokens.Count == 0) return 0.01;
        var hit = qTokens.Count(t => chunk.Contains(t, StringComparison.Ordinal));
        return (double)hit / qTokens.Count;
    }

    private static IEnumerable<string> SplitSentences(string text) =>
        text.Split(new[] { '\n', '.', '!', '?', '۔', '؟' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length >= 12 && s.Length <= 400);

    private static int SentScore(string sentence, List<string> qTokens) =>
        qTokens.Count(t => sentence.Contains(t, StringComparison.Ordinal));
}
