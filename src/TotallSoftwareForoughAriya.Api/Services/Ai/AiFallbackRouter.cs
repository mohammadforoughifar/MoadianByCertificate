using System.Text;
using System.Text.Json;

namespace Inventory.Api.Services.Ai;

// =====================================================================
// مسیریاب آفلاین (قطعی) — وقتی مدل زبانی در دسترس نیست، پرتکرارترین
// سؤال‌ها را با الگویابی ساده جواب می‌دهد تا دستیار هیچ‌وقت سکوت نکند.
// خروجی‌ها همان قالب متنی پاسخ عادی‌اند (با لینک و لیست).
// =====================================================================
public class AiFallbackRouter
{
    private readonly AiToolRegistry _tools;

    public AiFallbackRouter(AiToolRegistry tools) => _tools = tools;

    /// <summary>اگر سؤال شناخته‌شده بود جواب می‌دهد، وگرنه null.</summary>
    public async Task<string?> TryAnswerAsync(string message, AiToolContext ctx)
    {
        var q = AiTextUtil.NormalizeFa(message);

        if (IsGreeting(q)) return Greeting(ctx.UserName);

        // تأیید/لغو پیش‌فاکتور اقدام — حتی در حالت آفلاین
        if (q is "تأیید" or "تایید" or "باشه" or "اوکی" or "انجام بده" or "آره" or "اره" or "بله")
            return FormatConfirm(await _tools.ExecuteAsync("confirm_action", "{}", ctx));
        if (q is "لغو" or "کنسل" or "نه" or "پشیمان شدم" or "پشیمون شدم")
            return FormatConfirm(await _tools.ExecuteAsync("cancel_action", "{}", ctx));
        if (ContainsAny(q, "کی هستی", "تو کی", "معرفی", "چه کار", "چیکار", "کمک", "راهنما", "help"))
            return HelpText();
        if (ContainsAny(q, "ساعت چنده", "تاریخ امروز", "امروز چه روزی", "امروز چندمه"))
            return $"امروز **{AiDateUtil.TodayFa()}** است. 📅";

        // حقوق و فیش (قبل از مرخصی چون «حقوق» گاهی با مرخصی می‌آید؟ نه — ترتیب مهم نیست، جدا هستند)
        if (ContainsAny(q, "فیش", "حقوق", "دستمزد", "خالص پرداختی")
            || (ContainsAny(q, "درآمد", "درامد") && ContainsAny(q, "من", "خودم")))
        {
            var (year, month) = ExtractYearMonth(q);
            var args = "{}";
            if (year != null || month != null)
                args = JsonSerializer.Serialize(new { year, month });
            return FormatPayslip(await _tools.ExecuteAsync("my_payslip", args, ctx));
        }
        if (ContainsAny(q, "وام", "مساعده"))
            return FormatLoans(await _tools.ExecuteAsync("my_loans", "{}", ctx));
        if (ContainsAny(q, "ماموریت", "مأموریت"))
            return FormatMissions(await _tools.ExecuteAsync("my_missions", "{\"limit\":5}", ctx));
        if (ContainsAny(q, "مرخصی", "مرخصى"))
        {
            // «تأیید مرخصی ۱۲» / «رد مرخصی ۱۲» — ساخت پیش‌فاکتور حتی در حالت آفلاین
            var decide = ParseDecideLeave(q);
            if (decide != null)
                return FormatProforma(await _tools.ExecuteAsync("decide_leave",
                    JsonSerializer.Serialize(new { leave_id = decide.Value.id, approve = decide.Value.approve }), ctx));
            if (ContainsAny(q, "تأیید", "تاييد", "تصویب", "در انتظار", "تیم", "نیروها"))
                return FormatApprovals(await _tools.ExecuteAsync("pending_approvals", "{\"limit\":10}", ctx));
            if (ContainsAny(q, "مانده", "باقی", "باقیمانده", "چقدر", "چند روز", "دارم"))
                return FormatBalances(await _tools.ExecuteAsync("my_leave_balance", "{}", ctx));
            return FormatLeaves(await _tools.ExecuteAsync("my_leaves", "{\"limit\":5}", ctx));
        }
        if (ContainsAny(q, "حضور", "ورود", "خروج", "تاخیر", "تأخیر", "کارکرد", "شیفت", "ساعت زنی", "ساعت‌زنی"))
            return FormatAttendance(await _tools.ExecuteAsync("my_attendance_today", "{}", ctx));
        if (ContainsAny(q, "نامه", "کارتابل", "خوانده نشده", "خوانده‌نشده", "ارجاع"))
        {
            if (ContainsAny(q, "بی پاسخ", "منتظر پاسخ", "پاسخ نداده", "جواب نداده", "پاسخ بدهم", "پاسخ بدم"))
                return FormatReferrals(await _tools.ExecuteAsync("my_referrals_pending", "{\"limit\":10}", ctx));
            return await FormatLettersAsync(ctx);
        }

        // هشدارهای امروز (قبل از هوش مدیریتی — چون «هشدار چک» شامل «چک» است)
        if (ContainsAny(q, "هشدار", "اعلان", "آلارم"))
            return FormatAlerts(await _tools.ExecuteAsync("my_alerts", "{}", ctx));

        // ---------- هوش مدیریتی آفلاین (فقط خواندنی) ----------
        // نقدینگی اول — چون «بانک» و «موجودی» با چک و انبار مشترک‌اند
        if (ContainsAny(q, "صندوق", "نقدینگی", "تنخواه")
            || (ContainsAny(q, "بانک") && !ContainsAny(q, "چک")))
            return FormatCash(await _tools.ExecuteAsync("cash_status",
                JsonSerializer.Serialize(new { period = DetectPeriod(q) }), ctx));
        if (ContainsAny(q, "چک"))
            return FormatCheques(await _tools.ExecuteAsync("cheques_due",
                JsonSerializer.Serialize(new { days = DetectChequeDays(q) }), ctx));
        if (ContainsAny(q, "بدهکار", "بدهی", "مطالبات", "دریافتنی", "طلبکار"))
        {
            // «بدهی من» یعنی وام/مساعده خود کاربر، نه بدهکاران شرکت
            if (ContainsAny(q, "بدهی") && ContainsAny(q, "من", "خودم", "وام"))
                return FormatLoans(await _tools.ExecuteAsync("my_loans", "{}", ctx));
            return FormatDebtors(await _tools.ExecuteAsync("top_debtors", "{\"limit\":10}", ctx));
        }
        if (ContainsAny(q, "فروش", "فاکتور", "سود", "درآمد", "درامد", "خرید")
            && !ContainsAny(q, "چطور", "چگونه", "چجوری", "نحوه", "کجا", "آموزش", "ثبت"))
        {
            if (ContainsAny(q, "آخرین", "اخیر", "جدیدترین"))
                return FormatInvoices(await _tools.ExecuteAsync("recent_invoices", "{\"limit\":5}", ctx));
            return FormatSales(await _tools.ExecuteAsync("sales_summary",
                JsonSerializer.Serialize(new { period = DetectPeriod(q) }), ctx));
        }
        if (ContainsAny(q, "انبار", "کالا", "موجودی"))
        {
            var stockSearch = ExtractStockSearch(q);
            return FormatStock(await _tools.ExecuteAsync("stock_status",
                JsonSerializer.Serialize(new { search = stockSearch, limit = 10 }), ctx), stockSearch != null);
        }

        // راهنمای «چطور...؟» با جستجوی کلیدواژه‌ای (بدون نیاز به مدل)
        if (ContainsAny(q, "چطور", "چگونه", "چجوری", "نحوه", "کجا", "آموزش", "ثبت", "چیکار کنم"))
            return await FormatGuideAsync(q, ctx);

        return null;
    }

    // ---------------- قالب‌بندی پاسخ‌ها ----------------

    private static string FormatConfirm(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var msg = Prop(doc.RootElement, "message");
            return string.IsNullOrWhiteSpace(msg) ? "انجام شد." : msg;
        }
        catch { return "انجام شد."; }
    }

    private static string FormatBalances(string json)
    {
        var items = ParseArray(json);
        if (items.Count == 0) return "مانده مرخصی‌ای برایت ثبت نشده. 🤷 از واحد منابع انسانی پیگیری کن.";
        var sb = new StringBuilder("**مانده مرخصی‌ات:**\n");
        foreach (var it in items)
            sb.AppendLine($"- {Prop(it, "نوع")}: **{FaNum(Prop(it, "مانده"))} روز** (استحقاق {FaNum(Prop(it, "استحقاق"))}، استفاده‌شده {FaNum(Prop(it, "استفاده_شده"))})");
        sb.AppendLine().Append("درخواست جدید از [مرخصی‌های من](/fa-att/my-leaves)");
        return sb.ToString();
    }

    private static string FormatLeaves(string json)
    {
        var items = ParseArray(json);
        if (items.Count == 0) return "درخواستی ثبت نکردی. از [مرخصی‌های من](/fa-att/my-leaves) می‌تونی ثبت کنی. 📝";
        var sb = new StringBuilder("**مرخصی‌های اخیرت:**\n");
        foreach (var it in items)
            sb.AppendLine($"- {Prop(it, "نوع")}: {FaNum(Prop(it, "از"))} تا {FaNum(Prop(it, "تا"))} — {Prop(it, "وضعیت")}");
        sb.AppendLine().Append("جزئیات در [مرخصی‌های من](/fa-att/my-leaves)");
        return sb.ToString();
    }

    private static string FormatAttendance(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object) return "امروز رکورد حضوری نداری.";
            var sb = new StringBuilder($"**حضور امروزت ({FaNum(Prop(r, "تاریخ"))}):**\n");
            sb.AppendLine($"- شیفت: {Prop(r, "شیفت")}");
            sb.AppendLine($"- ورود: {FaNum(Prop(r, "ورود"))} — خروج: {FaNum(Prop(r, "خروج"))}");
            sb.AppendLine($"- کارکرد: {FaNum(Prop(r, "کارکرد_دقیقه"))} دقیقه — تأخیر: {FaNum(Prop(r, "تأخیر_دقیقه"))} دقیقه");
            if (Prop(r, "ناقص") == "True") sb.AppendLine("⚠️ رکورد امروزت ناقصه (ورود یا خروج ثبت نشده).");
            sb.Append("از [ساعت‌زنی](/fa-att/clock) می‌تونی ورود/خروج ثبت کنی.");
            return sb.ToString();
        }
        catch { return "نتونستم وضعیت حضورت رو بخونم. 😕"; }
    }

    private static string FormatPayslip(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            if (r.TryGetProperty("error", out _))
                return "فیشی برای این دوره پیدا نکردم. 🤷 دوره دیگری را بگو (مثلاً «فیش خرداد»).";
            var sb = new StringBuilder($"**فیش حقوقی {FaNum(Prop(r, "دوره"))}:**\n");
            sb.AppendLine($"- ناخالص: **{FaMoney(Prop(r, "ناخالص"))}**");
            sb.AppendLine($"- کسورات: {FaMoney(Prop(r, "کسورات"))} (مالیات {FaMoney(Prop(r, "مالیات"))} + بیمه {FaMoney(Prop(r, "بیمه"))})");
            sb.AppendLine($"- 💰 خالص پرداختی: **{FaMoney(Prop(r, "خالص_پرداختی"))}**");
            sb.AppendLine($"- وضعیت: {(Prop(r, "پرداخت_شده") == "True" ? "پرداخت شده ✅" : "پرداخت نشده ⏳")}");
            sb.Append("ریز اقلام در [فیش من](/fa-pay/my)");
            return sb.ToString();
        }
        catch { return "نتونستم فیشت رو بخونم. 😕"; }
    }

    private static string FormatLoans(string json)
    {
        var items = ParseArray(json);
        if (items.Count == 0) return "وام فعالی به نامت ثبت نیست. 🎉";
        var sb = new StringBuilder("**وام‌هایت:**\n");
        foreach (var it in items)
            sb.AppendLine($"- {Prop(it, "عنوان")}: مبلغ {FaMoney(Prop(it, "مبلغ_کل"))}، قسط {FaMoney(Prop(it, "مبلغ_قسط"))}، مانده **{FaMoney(Prop(it, "مانده"))}**");
        return sb.ToString();
    }

    private static string FormatMissions(string json)
    {
        var items = ParseArray(json);
        if (items.Count == 0) return "مأموریتی نداری. 🧳";
        var sb = new StringBuilder("**مأموریت‌هایت:**\n");
        foreach (var it in items)
            sb.AppendLine($"- {Prop(it, "مقصد")}: {FaNum(Prop(it, "از"))} تا {FaNum(Prop(it, "تا"))} — {Prop(it, "وضعیت")}");
        return sb.ToString();
    }

    private static string FormatProforma(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            if (r.TryGetProperty("error", out _))
                return Prop(r, "message");
            return $"📝 **پیش‌فاکتور:** {Prop(r, "summary")}\nبرای اجرا بنویس: **تأیید** (یا «لغو» برای انصراف)";
        }
        catch { return "نتونستم پیش‌فاکتور بسازم. 😕"; }
    }

    private static string FormatApprovals(string json)
    {
        var items = ParseArray(json);
        if (items.Count == 0) return "مرخصی در انتظار تأییدی نداری. 🎉";
        var sb = new StringBuilder("**مرخصی‌های در انتظار تأییدت:**\n");
        foreach (var it in items)
        {
            sb.AppendLine($"- **شناسه {FaNum(Prop(it, "شناسه"))}** — {Prop(it, "کارمند")}: {Prop(it, "نوع")}، {FaNum(Prop(it, "از"))} تا {FaNum(Prop(it, "تا"))}" +
                          (Prop(it, "دلیل") == "" ? "" : $" (دلیل: {Prop(it, "دلیل")})"));
        }
        sb.AppendLine().Append("برای تصمیم بنویس مثلاً: «تأیید مرخصی ۱۲» یا «رد مرخصی ۱۲»");
        return sb.ToString();
    }

    private static string FormatReferrals(string json)
    {
        var items = ParseArray(json);
        if (items.Count == 0) return "ارجاع بی‌پاسخی نداری. 🎉";
        var sb = new StringBuilder("**ارجاع‌های بی‌پاسخت:**\n");
        foreach (var it in items)
        {
            sb.AppendLine($"- **{Prop(it, "عنوان")}** (از {Prop(it, "فرستنده")} — {FaNum(Prop(it, "تاریخ"))})" +
                          (Prop(it, "پاراف") == "" ? "" : $"\n  پاراف: {Prop(it, "پاراف")}") +
                          (Prop(it, "مهلت") == "" ? "" : $" ⏰ مهلت: {FaNum(Prop(it, "مهلت"))}"));
        }
        sb.AppendLine().Append("باز کردن [کارتابل نامه‌ها](/letters)");
        return sb.ToString();
    }

    private static (int id, bool approve)? ParseDecideLeave(string q)
    {
        // «تأیید مرخصی ۱۲» یا «رد مرخصی ۱۲» (ارقام فارسی هم قبول)
        var m = System.Text.RegularExpressions.Regex.Match(AiTextUtil.ToEnDigits(q),
            @"(تأیید|تاييد|تایید|رد)\s+(مرخصی\s+)?(\d+)");
        if (!m.Success || !int.TryParse(m.Groups[3].Value, out var id) || id <= 0) return null;
        return (id, m.Groups[1].Value != "رد");
    }

    // ---------------- قالب‌بندهای هوش مدیریتی ----------------

    /// <summary>اگر خروجی ابزار خطاست (مثلاً عدم دسترسی)، پیامش را برمی‌گرداند.</summary>
    private static string? ErrorOf(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            if (r.ValueKind == JsonValueKind.Object && r.TryGetProperty("error", out _))
                return Prop(r, "message");
        }
        catch { /* خطا نیست */ }
        return null;
    }

    internal static string FormatSales(string json)
    {
        if (ErrorOf(json) is { } err) return err;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            var sb = new StringBuilder($"**فروش و خرید {Prop(r, "دوره")}:**\n");
            sb.AppendLine($"- 🛒 فروش: **{FaMoney(Prop(r, "فروش_جمع"))}** ({FaNum(Prop(r, "فروش_تعداد"))} فاکتور)");
            sb.AppendLine($"- 📦 خرید: {FaMoney(Prop(r, "خرید_جمع"))} — برگشتی فروش: {FaMoney(Prop(r, "برگشتی_فروش"))}");
            sb.AppendLine($"- 📈 سود ناخالص تقریبی: **{FaMoney(Prop(r, "سود_ناخالص_تقریبی"))}**");
            sb.AppendLine($"- 💳 دریافتنی: {FaMoney(Prop(r, "دریافتنی"))} — پرداختنی: {FaMoney(Prop(r, "پرداختنی"))}");
            if (Prop(r, "پیش_نویس_باز") is string drafts && drafts != "" && drafts != "0")
                sb.AppendLine($"- 📝 {FaNum(drafts)} فاکتور پیش‌نویس باز داری.");
            var prods = ParseArray(json, "پرفروش_ترین_کالاها");
            if (prods.Count > 0)
            {
                sb.AppendLine("\n**پرفروش‌ترین کالاها:**");
                foreach (var p in prods.Take(5))
                    sb.AppendLine($"- {Prop(p, "عنوان")}: {FaMoney(Prop(p, "مبلغ"))}");
            }
            var parties = ParseArray(json, "بزرگ_ترین_طرف_ها");
            if (parties.Count > 0)
            {
                sb.AppendLine("\n**بزرگ‌ترین طرف‌ها:**");
                foreach (var p in parties.Take(5))
                    sb.AppendLine($"- {Prop(p, "عنوان")}: {FaMoney(Prop(p, "مبلغ"))}");
            }
            sb.Append("\nجزئیات در [فاکتورها](/fac/invoices)");
            return sb.ToString();
        }
        catch { return "نتونستم گزارش فروش رو بخونم. 😕"; }
    }

    internal static string FormatInvoices(string json)
    {
        if (ErrorOf(json) is { } err) return err;
        var items = ParseArray(json);
        if (items.Count == 0) return "فاکتور قطعی‌شده‌ای پیدا نکردم.";
        var sb = new StringBuilder("**آخرین فاکتورها:**\n");
        foreach (var it in items)
            sb.AppendLine($"- فاکتور {Prop(it, "نوع")} شماره **{FaNum(Prop(it, "شماره"))}** — {Prop(it, "طرف")} — {FaMoney(Prop(it, "مبلغ"))} ({Prop(it, "تسویه")}، {FaNum(Prop(it, "تاریخ"))})");
        sb.Append("\nهمه در [فاکتورها](/fac/invoices)");
        return sb.ToString();
    }

    internal static string FormatStock(string json, bool searched)
    {
        if (ErrorOf(json) is { } err) return err;
        var items = ParseArray(json);
        if (items.Count == 0)
            return searched ? "کالایی با این نام/کد پیدا نکردم. 🤷" : "کالای زیر نقطه سفارش نداری؛ انبار سالمه. 🎉";
        var sb = new StringBuilder(searched ? "**موجودی:**\n" : "**کالاهای زیر نقطه سفارش:**\n");
        foreach (var it in items)
            sb.AppendLine($"- {Prop(it, "کالا")} ({Prop(it, "انبار")}): **{FaNum(Prop(it, "موجودی"))} {Prop(it, "واحد")}**" +
                          (Prop(it, "وضعیت") == "کمبود" ? " ⚠️ کمبود" : ""));
        sb.Append("\nجزئیات در [موجودی انبار](/inv/stock)");
        return sb.ToString();
    }

    private static string FormatAlerts(string json)
    {
        if (ErrorOf(json) is { } err) return err;
        var items = ParseArray(json, "alerts");
        if (items.Count == 0) return "هشدار فعالی نداری؛ همه‌چیز آرام است! 🎉";
        var sb = new StringBuilder("**هشدارهای امروز:**\n");
        foreach (var it in items)
        {
            sb.AppendLine($"\n{Prop(it, "icon")} **{Prop(it, "title")}:**");
            if (it.ValueKind == JsonValueKind.Object
                && it.TryGetProperty("lines", out var lines)
                && lines.ValueKind == JsonValueKind.Array)
                foreach (var l in lines.EnumerateArray())
                    sb.AppendLine($"- {(l.ValueKind == JsonValueKind.String ? l.GetString() : l.GetRawText())}");
            var link = Prop(it, "link");
            if (link != "") sb.AppendLine($"[{Prop(it, "link_text")}]({link})");
        }
        return sb.ToString().Trim();
    }

    internal static string FormatCheques(string json)
    {
        if (ErrorOf(json) is { } err) return err;
        var items = ParseArray(json, "چک_ها");
        if (items.Count == 0) return "چک بازی نزدیک سررسید نیست. 🎉";
        var sb = new StringBuilder("**چک‌های نزدیک سررسید:**\n");
        foreach (var it in items)
            sb.AppendLine($"- چک {Prop(it, "نوع")} شماره {FaNum(Prop(it, "شماره"))} — {Prop(it, "طرف")} — **{FaMoney(Prop(it, "مبلغ"))}** — سررسید {FaNum(Prop(it, "سررسید"))} ({Prop(it, "وضعیت")})");
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            sb.AppendLine($"\nجمع دریافتی: {FaMoney(Prop(r, "جمع_دریافتی"))} — جمع صادره: {FaMoney(Prop(r, "جمع_صادره"))}");
        }
        catch { /* جمع اختیاری */ }
        sb.Append("مدیریت در [چک‌ها](/trs/cheques)");
        return sb.ToString();
    }

    internal static string FormatDebtors(string json)
    {
        if (ErrorOf(json) is { } err) return err;
        var items = ParseArray(json);
        if (items.Count == 0) return "بدهکار بازی نداری. 🎉";
        var sb = new StringBuilder("**بدهکاران بزرگ (جمع فاکتور نسیه منهای برگشتی):**\n");
        foreach (var it in items)
            sb.AppendLine($"- {Prop(it, "طرف")}: **{FaMoney(Prop(it, "جمع_نسیه"))}** ({FaNum(Prop(it, "تعداد_فاکتور"))} فاکتور)");
        sb.Append("\nجزئیات در [فاکتورها](/fac/invoices)");
        return sb.ToString();
    }

    internal static string FormatCash(string json)
    {
        if (ErrorOf(json) is { } err) return err;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            var sb = new StringBuilder("**وضعیت نقدینگی:**\n");
            sb.AppendLine($"- 💰 جمع کل: **{FaMoney(Prop(r, "جمع_کل"))}** (صندوق {FaMoney(Prop(r, "صندوق"))} + بانک {FaMoney(Prop(r, "بانک"))})");
            var accs = ParseArray(json, "حساب_ها");
            foreach (var a in accs.Take(8))
                sb.AppendLine($"- {Prop(a, "نام")} ({Prop(a, "نوع")}): {FaMoney(Prop(a, "مانده"))}");
            sb.AppendLine($"\nگردش {Prop(r, "دوره")}: دریافت {FaMoney(Prop(r, "دریافت_دوره"))} — پرداخت {FaMoney(Prop(r, "پرداخت_دوره"))}");
            sb.Append("جزئیات در [صندوق و بانک](/trs/accounts)");
            return sb.ToString();
        }
        catch { return "نتونستم وضعیت صندوق رو بخونم. 😕"; }
    }

    private static string DetectPeriod(string q)
    {
        if (ContainsAny(q, "امسال", "ام سال")) return "امسال";
        if (ContainsAny(q, "ماه گذشته", "ماه قبل", "ماه پیش")) return "ماه گذشته";
        if (ContainsAny(q, "ماه", "ماهانه")) return "این ماه";
        if (ContainsAny(q, "هفته")) return "این هفته";
        if (ContainsAny(q, "امروز")) return "امروز";
        return "این ماه"; // پیش‌فرض مدیریتی
    }

    private static int DetectChequeDays(string q)
    {
        if (ContainsAny(q, "امروز")) return 0;
        if (ContainsAny(q, "فردا", "پس فردا", "پس‌فردا")) return 2;
        if (ContainsAny(q, "هفته")) return 7;
        if (ContainsAny(q, "ماه")) return 30;
        return 7;
    }

    private static string? ExtractStockSearch(string q)
    {
        string[] stop = { "موجودی", "انبار", "کالا", "کالای", "چقدر", "چند", "تعداد", "را", "رو", "است",
            "هست", "چیه", "چیست", "بگو", "ببین", "نشان", "وضعیت", "کمبود", "زیر", "نقطه", "سفارش" };
        var words = q.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Trim('؟', '?', '.', '،', ':'))
            .Where(w => w.Length >= 2 && !stop.Contains(w))
            .ToList();
        return words.Count == 0 ? null : string.Join(" ", words);
    }

    private async Task<string> FormatLettersAsync(AiToolContext ctx)
    {
        string statsJson = await _tools.ExecuteAsync("my_letters_stats", "{}", ctx);
        string inboxJson = await _tools.ExecuteAsync("my_letters_inbox", "{\"unread_only\":true,\"limit\":5}", ctx);
        var sb = new StringBuilder();
        try
        {
            using var doc = JsonDocument.Parse(statsJson);
            var r = doc.RootElement;
            sb.AppendLine($"📩 **{FaNum(Prop(r, "خوانده_نشده"))} نامه خوانده‌نشده** داری (از {FaNum(Prop(r, "کل_وارده"))} نامه وارده).");
            if (Prop(r, "مهلت_نزدیک") is string dl && dl != "0" && dl != "")
                sb.AppendLine($"⏰ {FaNum(dl)} ارجاع مهلت نزدیک داری!");
        }
        catch { /* رد شو */ }
        var items = ParseArray(inboxJson, "نامه_ها");
        if (items.Count > 0)
        {
            sb.AppendLine("\n**تازه‌ترین خوانده‌نشده‌ها:**");
            foreach (var it in items)
                sb.AppendLine($"- {Prop(it, "عنوان")} (از {Prop(it, "فرستنده")} — {FaNum(Prop(it, "تاریخ"))})");
        }
        sb.Append("\nباز کردن [کارتابل نامه‌ها](/letters)");
        return sb.ToString();
    }

    private async Task<string> FormatGuideAsync(string query, AiToolContext ctx)
    {
        var json = await _tools.ExecuteAsync("guide_search", JsonSerializer.Serialize(new { query }), ctx);
        var items = ParseArray(json);
        if (items.Count == 0) return "برای این موضوع راهنمایی پیدا نکردم. 😕 سؤالت رو کوتاه‌تر بپرس، مثلاً «ثبت مرخصی» یا «فاکتور فروش».";
        var sb = new StringBuilder();
        foreach (var it in items.Take(2))
        {
            var title = Prop(it, "Title");
            var content = Prop(it, "content");
            var link = Prop(it, "link");
            sb.AppendLine($"**{title}**");
            sb.AppendLine(content);
            if (!string.IsNullOrWhiteSpace(link)) sb.AppendLine($"باز کردن [{title}]({link})");
            sb.AppendLine();
        }
        return sb.ToString().Trim();
    }

    // ---------------- ابزارهای کمکی ----------------

    private static bool IsGreeting(string q) =>
        q is "سلام" or "درود" or "هی" or "صبح بخیر" or "عصر بخیر" or "شب بخیر"
        || q.StartsWith("سلام ") || q is "خداحافظ" or "خدانگهدار" or "ممنون" or "مرسی" or "تشکر";

    private static string Greeting(string userName) =>
        $"سلام {userName}! 👋 من **فروغ آریا** هستم. بپرس: «مانده مرخصی»، «فیش حقوقی»، «نامه‌های خوانده‌نشده» یا «چطور ...؟»";

    private static string HelpText() =>
        "من **فروغ آریا**، دستیار هوشمند سامانه‌ام 🤖\n\n" +
        "**اطلاعات خودت:** مانده مرخصی، فیش حقوقی، حضور امروز، وام‌ها، مأموریت‌ها، نامه‌های خوانده‌نشده\n" +
        "**مدیران:** «مرخصی‌های در انتظار تأیید» و «تأیید/رد مرخصی شماره…» + «ارجاع‌های بی‌پاسخ»\n" +
        "**هوش مدیریتی:** فروش دوره، آخرین فاکتورها، موجودی انبار، چک‌های نزدیک سررسید، بدهکاران، موجودی صندوق (با دسترسی همان بخش)\n" +
        "**هشدارها:** بپرس «هشدارهای امروز» — کمبود انبار، چک برگشتی، پیش‌فاکتور قدیمی، تیکت جدید، قرارداد رو به اتمام\n" +
        "**راهنما:** بپرس «چطور ...؟» — مثلاً «چطور فاکتور ثبت کنم؟»\n" +
        "**نامه‌ها:** داخل کارتابل، دکمه‌های خلاصه، پیشنهاد ارجاع و پیش‌نویس هوشمند کمکت می‌کنن.";

    private static bool ContainsAny(string q, params string[] parts) =>
        parts.Any(p => q.Contains(AiTextUtil.NormalizeFa(p)));

    private static (int? year, int? month) ExtractYearMonth(string q)
    {
        int? month = null;
        string[] months = { "فروردین", "اردیبهشت", "خرداد", "تیر", "مرداد", "شهریور", "مهر", "آبان", "آذر", "دی", "بهمن", "اسفند" };
        for (var i = 0; i < months.Length; i++)
            if (q.Contains(months[i])) { month = i + 1; break; }
        int? year = null;
        var m = System.Text.RegularExpressions.Regex.Match(q, @"14\d\d");
        if (m.Success && int.TryParse(m.Value, out var y)) year = y;
        return (year, month);
    }

    private static List<JsonElement> ParseArray(string json, string? prop = null)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (prop != null)
            {
                if (!root.TryGetProperty(prop, out var arr) || arr.ValueKind != JsonValueKind.Array) return new();
                return arr.EnumerateArray().Select(e => e.Clone()).ToList();
            }
            if (root.ValueKind != JsonValueKind.Array) return new();
            return root.EnumerateArray().Select(e => e.Clone()).ToList();
        }
        catch { return new(); }
    }

    private static string Prop(JsonElement el, string name)
    {
        if (el.ValueKind != JsonValueKind.Object) return "";
        if (!el.TryGetProperty(name, out var v)) return "";
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString() ?? "",
            JsonValueKind.Number => v.GetRawText(),
            JsonValueKind.True => "True",
            JsonValueKind.False => "False",
            JsonValueKind.Null => "",
            _ => v.GetRawText(),
        };
    }

    private static string FaNum(string s)
    {
        if (string.IsNullOrWhiteSpace(s) || s is "—" or "") return "—";
        return ToFaDigits(s);
    }

    private static string FaMoney(string s)
    {
        if (double.TryParse(s, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var d))
            return ToFaDigits(d.ToString("#,##0")) + " تومان";
        return FaNum(s);
    }

    private static string ToFaDigits(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
            sb.Append(c is >= '0' and <= '9' ? (char)('۰' + (c - '0')) : c);
        return sb.ToString();
    }
}
