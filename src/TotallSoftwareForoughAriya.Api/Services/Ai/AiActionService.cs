using System.Text.Json;
using Inventory.Api.Data;
using Inventory.Api.Services.FaAtt;
using Inventory.Api.Services.FaCom;
using Inventory.Api.Services.Treasury;
using Inventory.Api.Services.Office;
using Inventory.Shared;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Inventory.Api.Services.Ai;

// =====================================================================
// «اقدام با تأیید»: دستیار اقدام را به‌صورت پیش‌فاکتور ثبت می‌کند و فقط
// بعد از تأیید صریح همان کاربر (مثلاً با گفتن «تأیید») اجرا می‌شود.
// پیش‌فاکتورها ۱۵ دقیقه اعتبار دارند و فقط سازنده می‌تواند تأییدشان کند.
// =====================================================================

public class AiPendingActionInfo
{
    public int Id { get; set; }
    public string Action { get; set; } = "";
    public string Summary { get; set; } = "";
    public DateTime ExpiresAtUtc { get; set; }
}

public class AiActionService
{
    private readonly AppDbContext _db;
    private readonly AiOptions _options;
    private readonly IFaAttService _faAtt;
    private readonly IErjaService _erja;
    private readonly IPishnevisService _pishnevis;
    private readonly IFaComService _faCom;
    private readonly ITreasuryService _treasury;
    private readonly IMeetingMinutesService _minutes;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<AiActionService> _log;

    public AiActionService(AppDbContext db, IOptions<AiOptions> options, IFaAttService faAtt,
        IErjaService erja, IPishnevisService pishnevis, IFaComService faCom,
        ITreasuryService treasury, IMeetingMinutesService minutes,
        IHttpContextAccessor httpContextAccessor, ILogger<AiActionService> log)
    {
        _db = db;
        _options = options.Value;
        _faAtt = faAtt;
        _erja = erja;
        _pishnevis = pishnevis;
        _faCom = faCom;
        _treasury = treasury;
        _minutes = minutes;
        _httpContextAccessor = httpContextAccessor;
        _log = log;
    }

    /// <summary>ثبت پیش‌فاکتور جدید و برگرداندن آن (برای نمایش به کاربر).</summary>
    public async Task<AiPendingAction> CreateAsync(int userId, string action, string argsJson, string summary, CancellationToken ct)
    {
        await SweepExpiredAsync(userId, ct);
        var minutes = _options.PendingActionExpiryMinutes > 0 ? _options.PendingActionExpiryMinutes : 15;
        var entity = new AiPendingAction
        {
            UserId = userId,
            Action = action,
            ArgsJson = string.IsNullOrWhiteSpace(argsJson) ? "{}" : argsJson,
            Summary = summary,
            Status = 0,
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(minutes),
        };
        _db.AiPendingActions.Add(entity);
        await _db.SaveChangesAsync(ct);
        return entity;
    }

    /// <summary>پیش‌فاکتورهای باز کاربر (منقضی‌ها خودکار باطل می‌شوند).</summary>
    public async Task<List<AiPendingActionInfo>> ListPendingAsync(int userId, CancellationToken ct)
    {
        await SweepExpiredAsync(userId, ct);
        return await _db.AiPendingActions.AsNoTracking()
            .Where(a => a.UserId == userId && a.Status == 0)
            .OrderByDescending(a => a.Id)
            .Select(a => new AiPendingActionInfo
            {
                Id = a.Id,
                Action = a.Action,
                Summary = a.Summary,
                ExpiresAtUtc = a.ExpiresAtUtc,
            })
            .ToListAsync(ct);
    }

    /// <summary>تأیید و اجرای اقدام (بدون شناسه = تازه‌ترین پیش‌فاکتور باز).</summary>
    public async Task<(bool ok, string message)> ConfirmAsync(int userId, string userName, int? actionId, CancellationToken ct)
    {
        await SweepExpiredAsync(userId, ct);
        var action = await FindPendingAsync(userId, actionId, ct);
        if (action == null)
            return (false, "پیش‌فاکتور بازی نداری. اول بگو چه کاری انجام بدهم (مثلاً «فردا مرخصی می‌خوام»).");

        // کنترل دسترسی ماژول مربوطه — دقیقاً مثل خود سامانه
        var (module, perm) = action.Action switch
        {
            "request_leave" => ("FaAtt", "Create"),
            "clock" => ("FaAtt", "Create"),
            "request_mission" => ("FaAtt", "Create"),
            "decide_leave" => ("FaAtt", "Read"),
            "decide_leave_bulk" => ("FaAtt", "Read"),
            "answer_referral" => ("InnerLetters", "Read"),
            "create_ticket" => ("FaCom", "Create"),
            "report_work" => ("ReportWorks", "Create"),
            "create_letter_draft" => ("InnerLetters", "Create"),
            "refer_letter" => ("InnerLetters", "Read"),
            "answer_ticket" => ("FaCom", "Create"),
            "register_cheque" => ("TrsCheques", "Create"),
            "create_minutes" => ("MeetingMinutes", "Create"),
            _ => ("", ""),
        };
        if (module == "")
            return (false, "نوع اقدام ناشناخته است.");
        var role = await _db.Users.AsNoTracking()
            .Where(u => u.Id == userId).Select(u => u.Role).FirstOrDefaultAsync(ct);
        if (!await AiAccessHelper.UserHasAsync(_db, userId, module, perm, role, ct))
            return (false, "به این اقدام دسترسی نداری. ⛔");

        try
        {
            var result = action.Action switch
            {
                "request_leave" => await ExecuteLeaveAsync(action, userId, userName, ct),
                "clock" => await ExecuteClockAsync(action, userId, userName, ct),
                "request_mission" => await ExecuteMissionAsync(action, userId, userName, ct),
                "decide_leave" => await ExecuteDecideLeaveAsync(action, userId, userName, role, ct),
                "decide_leave_bulk" => await ExecuteDecideLeaveBulkAsync(action, userId, userName, role, ct),
                "answer_referral" => await ExecuteAnswerReferralAsync(action, userId, userName, ct),
                "create_ticket" => await ExecuteTicketAsync(action, userId, ct),
                "report_work" => await ExecuteReportWorkAsync(action, userId, ct),
                "create_letter_draft" => await ExecuteLetterDraftAsync(action, userId, ct),
                "refer_letter" => await ExecuteReferLetterAsync(action, userId, userName, ct),
                "answer_ticket" => await ExecuteAnswerTicketAsync(action, userId, userName, role, ct),
                "register_cheque" => await ExecuteRegisterChequeAsync(action, userId, userName, ct),
                "create_minutes" => await ExecuteCreateMinutesAsync(action, userId, userName, ct),
                _ => throw new InvalidOperationException("نوع اقدام ناشناخته است."),
            };
            action.Status = 1;
            action.DecidedAtUtc = DateTime.UtcNow;
            action.ResultText = result;
            await _db.SaveChangesAsync(ct);
            return (true, "✅ انجام شد! " + result);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "اجرای اقدام {ActionId} ناموفق بود.", action.Id);
            action.Status = 2;
            action.DecidedAtUtc = DateTime.UtcNow;
            action.ResultText = "خطا: " + ex.Message;
            await _db.SaveChangesAsync(ct);
            return (false, "❌ اجرا نشد: " + ex.Message);
        }
    }

    /// <summary>لغو پیش‌فاکتور (بدون شناسه = تازه‌ترین).</summary>
    public async Task<(bool ok, string message)> CancelAsync(int userId, int? actionId, CancellationToken ct)
    {
        await SweepExpiredAsync(userId, ct);
        var action = await FindPendingAsync(userId, actionId, ct);
        if (action == null)
            return (false, "پیش‌فاکتور بازی نداری.");
        action.Status = 2;
        action.DecidedAtUtc = DateTime.UtcNow;
        action.ResultText = "لغو توسط کاربر";
        await _db.SaveChangesAsync(ct);
        return (true, $"پیش‌فاکتور «{action.Summary}» لغو شد. 🗑️");
    }

    // ---------------- اجرا ----------------

    private async Task<string> ExecuteLeaveAsync(AiPendingAction action, int userId, string userName, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(action.ArgsJson) ? "{}" : action.ArgsJson);
        var root = doc.RootElement;
        var typeId = root.TryGetProperty("leaveTypeId", out var t) && t.TryGetInt32(out var n) ? n : 0;
        var from = root.TryGetProperty("from", out var f) ? f.GetString() : null;
        var to = root.TryGetProperty("to", out var e) ? e.GetString() : null;
        var reason = root.TryGetProperty("reason", out var r) ? r.GetString() : null;
        if (typeId <= 0 || !DateTime.TryParse(from, out var fromDate) || !DateTime.TryParse(to, out var toDate))
            throw new InvalidOperationException("اطلاعات مرخصی ناقص است؛ دوباره بگو.");
        if (toDate < fromDate) (fromDate, toDate) = (toDate, fromDate);

        var empId = await _db.HrEmployees.AsNoTracking()
            .Where(x => x.SystemUserId == userId).Select(x => x.Id).FirstOrDefaultAsync(ct);
        if (empId == 0) throw new InvalidOperationException("پرونده پرسنلی برایت یافت نشد.");

        var saved = await _faAtt.RequestMyLeaveAsync(userId, userName, new FaAttLeaveSaveDto
        {
            EmployeeId = empId,
            LeaveTypeId = typeId,
            FromDate = fromDate.Date,
            ToDate = toDate.Date,
            Reason = string.IsNullOrWhiteSpace(reason) ? "ثبت با دستیار فروغ آریا" : reason.Trim(),
        });
        return $"مرخصی {saved.LeaveTypeName ?? ""} از {AiDateUtil.ToFaShort(saved.FromDate)} تا {AiDateUtil.ToFaShort(saved.ToDate)} ثبت شد و در انتظار تأیید است.";
    }

    private async Task<string> ExecuteClockAsync(AiPendingAction action, int userId, string userName, CancellationToken ct)
    {
        _ = ct;
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(action.ArgsJson) ? "{}" : action.ArgsJson);
        var type = doc.RootElement.TryGetProperty("type", out var t) && t.TryGetInt32(out var n) ? n : -1;
        if (type is not (0 or 1)) throw new InvalidOperationException("نوع ساعت‌زنی مشخص نیست.");
        var log = await _faAtt.ClockAsync(userId, userName, new FaAttClockSaveDto { Type = type });
        var when = log.Timestamp.ToString("HH:mm");
        return type == 0 ? $"ساعت ورود ({when}) ثبت شد. 👋" : $"ساعت خروج ({when}) ثبت شد. 👋";
    }

    private async Task<string> ExecuteMissionAsync(AiPendingAction action, int userId, string userName, CancellationToken ct)
    {
        _ = userName;
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(action.ArgsJson) ? "{}" : action.ArgsJson);
        var root = doc.RootElement;
        var from = root.TryGetProperty("from", out var f) ? f.GetString() : null;
        var to = root.TryGetProperty("to", out var e) ? e.GetString() : null;
        var dest = root.TryGetProperty("destination", out var d) ? d.GetString() : null;
        var reason = root.TryGetProperty("reason", out var r) ? r.GetString() : null;
        if (!DateTime.TryParse(from, out var fromDate) || !DateTime.TryParse(to, out var toDate)
            || string.IsNullOrWhiteSpace(dest))
            throw new InvalidOperationException("اطلاعات مأموریت ناقص است؛ مقصد و تاریخ را بگو.");
        if (toDate < fromDate) (fromDate, toDate) = (toDate, fromDate);

        var empId = await _db.HrEmployees.AsNoTracking()
            .Where(x => x.SystemUserId == userId).Select(x => x.Id).FirstOrDefaultAsync(ct);
        if (empId == 0) throw new InvalidOperationException("پرونده پرسنلی برایت یافت نشد.");

        var saved = await _faAtt.RequestMyMissionAsync(userId, new FaAttMissionSaveDto
        {
            EmployeeId = empId,
            FromDate = fromDate.Date,
            ToDate = toDate.Date,
            Destination = dest.Trim(),
            Reason = string.IsNullOrWhiteSpace(reason) ? "ثبت با دستیار فروغ آریا" : reason.Trim(),
        });
        return $"مأموریت {saved.Destination} از {AiDateUtil.ToFaShort(saved.FromDate)} تا {AiDateUtil.ToFaShort(saved.ToDate)} ثبت شد و در انتظار تأیید است.";
    }

    private async Task<string> ExecuteDecideLeaveAsync(AiPendingAction action, int userId, string userName, string? role, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(action.ArgsJson) ? "{}" : action.ArgsJson);
        var leaveId = doc.RootElement.TryGetProperty("leave_id", out var l) && l.TryGetInt32(out var n) ? n : 0;
        var approve = doc.RootElement.TryGetProperty("approve", out var a) && a.ValueKind == JsonValueKind.True;
        if (leaveId <= 0) throw new InvalidOperationException("مرخصی مشخص نیست.");
        var isHr = await AiAccessHelper.UserHasAsync(_db, userId, "FaAtt", "Manage", role, ct);
        var decided = await _faAtt.ManagerDecideAsync(leaveId, approve, userId, userName, isHr);
        var what = approve ? "تأیید" : "رد";
        return $"مرخصی {decided.EmployeeName ?? ""} ({decided.LeaveTypeName}) {what} شد. ✅";
    }

    private async Task<string> ExecuteDecideLeaveBulkAsync(AiPendingAction action, int userId, string userName, string? role, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(action.ArgsJson) ? "{}" : action.ArgsJson);
        var root = doc.RootElement;
        var ids = new List<int>();
        if (root.TryGetProperty("leave_ids", out var arr) && arr.ValueKind == JsonValueKind.Array)
            foreach (var el in arr.EnumerateArray())
                if (el.TryGetInt32(out var n) && n > 0 && !ids.Contains(n)) ids.Add(n);
        var approve = root.TryGetProperty("approve", out var a) && a.ValueKind == JsonValueKind.True;
        if (ids.Count == 0) throw new InvalidOperationException("مرخصی مشخص نیست.");
        var isHr = await AiAccessHelper.UserHasAsync(_db, userId, "FaAtt", "Manage", role, ct);
        var what = approve ? "تأیید" : "رد";
        var ok = new List<string>();
        var failed = new List<string>();
        foreach (var id in ids.Take(20))
        {
            try
            {
                var decided = await _faAtt.ManagerDecideAsync(id, approve, userId, userName, isHr);
                ok.Add(AiTextUtil.Truncate($"{decided.EmployeeName} ({decided.LeaveTypeName})", 40));
            }
            catch (Exception ex) { failed.Add($"#{id}: {ex.Message}"); }
        }
        if (ok.Count == 0)
            throw new InvalidOperationException(string.Join("؛ ", failed.Take(3)));
        var res = $"{what} گروهی انجام شد: {AiTextUtil.ToFaDigits(ok.Count.ToString())} موفق ✅ (" +
            string.Join("، ", ok.Take(3)) + (ok.Count > 3 ? $"، +{AiTextUtil.ToFaDigits((ok.Count - 3).ToString())} مورد دیگر" : "") + ")";
        if (failed.Count > 0)
            res += $" ⚠️ ناموفق: {string.Join("؛ ", failed.Take(3))}";
        return res;
    }

    private async Task<string> ExecuteAnswerReferralAsync(AiPendingAction action, int userId, string userName, CancellationToken ct)
    {
        _ = ct;
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(action.ArgsJson) ? "{}" : action.ArgsJson);
        var erjaId = doc.RootElement.TryGetProperty("erja_id", out var e) && e.TryGetInt32(out var n) ? n : 0;
        var decision = doc.RootElement.TryGetProperty("decision", out var d) && d.TryGetInt32(out var m) ? m : 0;
        var text = doc.RootElement.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
        if (erjaId <= 0) throw new InvalidOperationException("ارجاع مشخص نیست.");
        if (decision is < 0 or > 2) decision = 0;
        await _erja.AnswerAsync(erjaId, new AnswerErjaDto { Answer = text.Trim(), TypeTaeed = decision }, userId, userName);
        return decision switch
        {
            1 => "ارجاع تأیید شد. ✅",
            2 => "ارجاع رد شد.",
            _ => "پاسخ متنی روی ارجاع ثبت شد. ✍️",
        };
    }

    private async Task<string> ExecuteTicketAsync(AiPendingAction action, int userId, CancellationToken ct)
    {
        _ = ct;
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(action.ArgsJson) ? "{}" : action.ArgsJson);
        var subject = doc.RootElement.TryGetProperty("subject", out var s) ? s.GetString() ?? "" : "";
        var body = doc.RootElement.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
        var category = doc.RootElement.TryGetProperty("category", out var c) && c.TryGetInt32(out var n) ? n : 0;
        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(body))
            throw new InvalidOperationException("موضوع و شرح تیکت لازم است.");
        var saved = await _faCom.CreateTicketAsync(userId, new FaComTicketSaveDto
        {
            Subject = subject.Trim(),
            Body = body.Trim(),
            Category = Math.Clamp(category, 0, 5),
            Priority = 1,
        }, false);
        return $"تیکت «{saved.Subject}» با شماره {saved.Id} ثبت شد. 🎫";
    }

    private async Task<string> ExecuteReferLetterAsync(AiPendingAction action, int userId, string userName, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(action.ArgsJson) ? "{}" : action.ArgsJson);
        var root = doc.RootElement;
        var letterId = root.TryGetProperty("letter_id", out var l) && l.TryGetInt32(out var n) ? n : 0;
        var receiverId = root.TryGetProperty("receiver_id", out var r) && r.TryGetInt32(out var m) ? m : 0;
        var text = root.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
        DateTime? deadline = root.TryGetProperty("deadline", out var d) && DateTime.TryParse(d.GetString(), out var dd) ? dd.Date : null;
        if (letterId <= 0 || receiverId <= 0 || string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException("نامه، گیرنده و متن ارجاع لازم است.");
        var letter = await _db.InnerLetters.AsNoTracking()
            .Where(x => x.Id == letterId && !x.IsDelete)
            .Select(x => new { x.Title }).FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("نامه یافت نشد.");
        var receiver = await _db.Users.AsNoTracking()
            .Where(u => u.Id == receiverId && u.IsActive && u.Username != Data.AiSeeder.AiUsername)
            .Select(u => new { u.FirstName, u.LastName, u.Username }).FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("گیرنده معتبر نیست.");
        await _erja.AddErjaAsync(new AddErjaDto
        {
            LetterId = letterId,
            TextErja = text.Trim(),
            ReciversGirandegan = new List<int> { receiverId },
            DeadlineAnswer = deadline,
        }, userId, userName);
        var receiverName = ((receiver.FirstName ?? "") + " " + (receiver.LastName ?? "")).Trim();
        if (receiverName == "") receiverName = receiver.Username;
        return $"نامه «{letter.Title}» به {receiverName} ارجاع شد. 📨";
    }

    private async Task<string> ExecuteAnswerTicketAsync(AiPendingAction action, int userId, string userName, string? role, CancellationToken ct)
    {
        _ = ct;
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(action.ArgsJson) ? "{}" : action.ArgsJson);
        var root = doc.RootElement;
        var ticketId = root.TryGetProperty("ticket_id", out var t) && t.TryGetInt32(out var n) ? n : 0;
        var body = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
        if (ticketId <= 0 || string.IsNullOrWhiteSpace(body))
            throw new InvalidOperationException("تیکت و متن پاسخ لازم است.");
        var isHr = await AiAccessHelper.UserHasAsync(_db, userId, "FaCom", "Manage", role);
        var dto = new FaComReplySaveDto { TicketId = ticketId, Body = body.Trim() };
        var saved = isHr
            ? await _faCom.ReplyHrAsync(dto, userId, userName)
            : await _faCom.ReplyMyAsync(userId, userName, dto);
        return $"پاسخت در تیکت «{saved.Subject}» (شماره {saved.Id}) ثبت شد. 💬";
    }

    private async Task<string> ExecuteRegisterChequeAsync(AiPendingAction action, int userId, string userName, CancellationToken ct)
    {
        _ = userId;
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(action.ArgsJson) ? "{}" : action.ArgsJson);
        var root = doc.RootElement;
        var kind = root.TryGetProperty("kind", out var k) && k.TryGetInt32(out var kn) ? kn : 0;
        var number = root.TryGetProperty("number", out var n) ? n.GetString() ?? "" : "";
        var amount = root.TryGetProperty("amount", out var a) && a.TryGetDecimal(out var m) ? m : 0;
        DateTime? due = root.TryGetProperty("due_date", out var d) && DateTime.TryParse(d.GetString(), out var dd) ? dd.Date : null;
        var issue = root.TryGetProperty("issue_date", out var i) && DateTime.TryParse(i.GetString(), out var idd) ? idd.Date : DateTime.Today;
        var bank = root.TryGetProperty("bank", out var b) ? b.GetString() ?? "" : "";
        var owner = root.TryGetProperty("owner", out var o) ? o.GetString() ?? "" : "";
        var partyId = root.TryGetProperty("party_id", out var p) && p.TryGetInt32(out var pn) ? pn : 0;
        var accountId = root.TryGetProperty("account_id", out var ac) && ac.TryGetInt32(out var an) ? an : 0;
        var desc = root.TryGetProperty("description", out var ds) ? ds.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(number) || amount <= 0 || due == null)
            throw new InvalidOperationException("شماره، مبلغ و سررسید چک لازم است.");
        if (kind is < 0 or > 1) throw new InvalidOperationException("نوع چک نامعتبر است.");
        if (partyId > 0 && !await _db.Parties.AnyAsync(p => p.Id == partyId, ct))
            throw new InvalidOperationException("طرف حساب معتبر نیست.");
        if (kind == 1 && accountId <= 0)
            throw new InvalidOperationException("برای چک صادره، حساب بانکی لازم است.");
        if (accountId > 0 && !await _db.TrsAccounts.AnyAsync(a => a.Id == accountId && a.IsActive, ct))
            throw new InvalidOperationException("حساب بانکی معتبر نیست.");
        var saved = await _treasury.SaveChequeAsync(new Inventory.Shared.Dtos.TrsCheque
        {
            Kind = (ChequeKind)kind,
            Number = number.Trim(),
            Amount = amount,
            IssueDate = issue,
            DueDate = due.Value,
            BankName = bank == "" ? null : bank.Trim(),
            OwnerName = owner == "" ? null : owner.Trim(),
            PartyId = partyId > 0 ? partyId : null,
            TrsAccountId = accountId > 0 ? accountId : null,
            Description = desc == "" ? null : desc.Trim(),
        }, userName);
        var kindFa = kind == 1 ? "صادره" : "دریافتی";
        return $"چک {kindFa} شماره {saved.Number} به مبلغ {AiTextUtil.ToFaDigits(saved.Amount.ToString("#,##0"))} تومان ثبت شد. 🧾";
    }

    private async Task<string> ExecuteCreateMinutesAsync(AiPendingAction action, int userId, string userName, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(action.ArgsJson) ? "{}" : action.ArgsJson);
        var root = doc.RootElement;
        var title = root.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
        var meetingDate = root.TryGetProperty("meeting_date", out var md) && DateTime.TryParse(md.GetString(), out var mdt) ? mdt.Date : (DateTime?)null;
        if (title.Trim() == "" || meetingDate == null)
            throw new InvalidOperationException("اطلاعات صورتجلسه ناقص است؛ دوباره بگو.");
        var items = new List<SaveMinutesItemDto>();
        if (root.TryGetProperty("items", out var arr) && arr.ValueKind == JsonValueKind.Array)
            foreach (var el in arr.EnumerateArray().Take(30))
            {
                if (el.ValueKind != JsonValueKind.Object) continue;
                var text = el.TryGetProperty("text", out var x) ? x.GetString() ?? "" : "";
                if (text.Trim() == "") continue;
                var respId = el.TryGetProperty("responsible_id", out var r) && r.TryGetInt32(out var n) ? n : 0;
                var due = el.TryGetProperty("due", out var d) && DateTime.TryParse(d.GetString(), out var dd) ? dd.Date : (DateTime?)null;
                items.Add(new SaveMinutesItemDto
                {
                    Description = text.Trim(),
                    ResponsibleUserId = respId > 0 ? respId : null,
                    DueDate = due,
                });
            }
        if (items.Count == 0) throw new InvalidOperationException("دست‌کم یک بند لازم است.");
        static List<int> ReadIds(JsonElement root, string prop)
        {
            var ids = new List<int>();
            if (root.TryGetProperty(prop, out var a) && a.ValueKind == JsonValueKind.Array)
                foreach (var el in a.EnumerateArray())
                    if (el.TryGetInt32(out var v) && v > 0 && !ids.Contains(v)) ids.Add(v);
            return ids;
        }
        var attendees = ReadIds(root, "attendees");
        if (attendees.Count == 0) throw new InvalidOperationException("دست‌کم یک حاضر لازم است.");
        var companyId = await ActiveCompanyIdAsync(userId, ct);
        if (companyId is null)
            throw new InvalidOperationException("شرکت فعال انتخاب نشده است.");

        var id = await _minutes.SaveAsync(new SaveMinutesDto
        {
            Title = title.Trim(),
            MeetingDate = meetingDate.Value,
            AttendeeUserIds = attendees,
            AbsentUserIds = ReadIds(root, "absentees"),
            Items = items,
        }, userId, userName, companyId.Value);
        return $"صورتجلسه شماره {id} در وضعیت «در حال بررسی» ذخیره شد. 📝";
    }

    private async Task<int?> ActiveCompanyIdAsync(int userId, CancellationToken ct)
    {
        var header = _httpContextAccessor.HttpContext?.Request.Headers["X-Company-Id"].FirstOrDefault();
        if (!int.TryParse(header, out var companyId) || companyId <= 0) return null;
        return await _db.UserCompanyAccesses.AsNoTracking()
            .AnyAsync(x => x.UserId == userId && x.CompanyId == companyId && x.Company.IsActive, ct)
            ? companyId
            : null;
    }

    private async Task<string> ExecuteReportWorkAsync(AiPendingAction action, int userId, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(action.ArgsJson) ? "{}" : action.ArgsJson);
        var root = doc.RootElement;
        var projectId = root.TryGetProperty("project_id", out var p) && p.TryGetInt32(out var n) ? n : 0;
        var desc = root.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "";
        var date = root.TryGetProperty("date", out var dt) && DateTime.TryParse(dt.GetString(), out var dd) ? dd.Date : DateTime.Today;
        var start = root.TryGetProperty("start", out var s) && TimeOnly.TryParse(s.GetString(), out var st) ? st : new TimeOnly(8, 0);
        var end = root.TryGetProperty("end", out var e) && TimeOnly.TryParse(e.GetString(), out var en) ? en : new TimeOnly(17, 0);
        if (projectId <= 0 || string.IsNullOrWhiteSpace(desc))
            throw new InvalidOperationException("پروژه و شرح کار لازم است.");
        if (start == end) throw new InvalidOperationException("ساعت شروع و پایان نمی‌توانند یکسان باشند.");

        // همان قوانین کنترلر گزارش‌کار (ValidateAsync)
        var project = await _db.ProjectEntryExits.AsNoTracking()
            .Where(x => x.Id == projectId && !x.IsDelete)
            .Select(x => new { x.FlowStatus, x.ProjectName, x.CodeProject })
            .FirstOrDefaultAsync(ct);
        if (project == null) throw new InvalidOperationException("پروژه معتبر نیست.");
        if (project.FlowStatus == 0)
            throw new InvalidOperationException($"پروژه «{project.ProjectName}» هنوز در انتظار تأیید مدیر است؛ ثبت گزارش مجاز نیست.");
        if (project.FlowStatus == 2)
            throw new InvalidOperationException($"پروژه «{project.ProjectName}» رد شده است؛ ثبت گزارش مجاز نیست.");

        var code = project.CodeProject;
        var entity = new ReportWork
        {
            CodeProject = code,
            ReportDate = date,
            UserId = userId,
            WorkDescription = desc.Trim(),
            ProjectId = projectId,
            StartTime = start,
            EndTime = end,
            SpentTime = CalcSpent(start, end, TimeOnly.MinValue, TimeOnly.MinValue),
            CreatedAt = DateTime.Now,
        };
        _db.ReportWorks.Add(entity);
        await _db.SaveChangesAsync(ct);

        // به‌روزرسانی جمع ساعات پروژه (RecalcProjectTotalAsync)
        var proj = await _db.ProjectEntryExits.FirstOrDefaultAsync(x => x.Id == projectId, ct);
        if (proj != null)
        {
            var spans = await _db.ReportWorks
                .Where(r => r.ProjectId == projectId && !r.IsDelete)
                .Select(r => r.SpentTime)
                .ToListAsync(ct);
            proj.TotalSpentTime = TimeSpan.FromTicks(spans.Sum(s => s.Ticks));
            await _db.SaveChangesAsync(ct);
        }
        return $"گزارش‌کار پروژه «{project.ProjectName}» برای {AiDateUtil.ToFaShort(date)} ثبت شد. 📋";
    }

    private async Task<string> ExecuteLetterDraftAsync(AiPendingAction action, int userId, CancellationToken ct)
    {
        _ = ct;
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(action.ArgsJson) ? "{}" : action.ArgsJson);
        var title = doc.RootElement.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
        var text = doc.RootElement.TryGetProperty("text", out var x) ? x.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(title)) throw new InvalidOperationException("عنوان نامه لازم است.");
        var id = await _pishnevis.AddAsync(new PishnevisDto { Title = title.Trim(), Text = text }, userId);
        return $"پیش‌نویس نامه «{title.Trim()}» ذخیره شد (شماره {id})؛ از کارتابل ادامه‌اش بده. ✉️";
    }

    /// <summary>همان فرمول ReportWorksController.CalcSpent (کپی عمدی — منطق ساده و پایدار).</summary>
    private static TimeSpan CalcSpent(TimeOnly start, TimeOnly end, TimeOnly breakfast, TimeOnly lunch)
    {
        var total = end - start;
        if (total < TimeSpan.Zero) total += TimeSpan.FromDays(1);
        var spent = total - breakfast.ToTimeSpan() - lunch.ToTimeSpan();
        return spent < TimeSpan.Zero ? TimeSpan.Zero : spent;
    }

    // ---------------- کمکی ----------------

    private async Task<AiPendingAction?> FindPendingAsync(int userId, int? actionId, CancellationToken ct)
    {
        var q = _db.AiPendingActions.Where(a => a.UserId == userId && a.Status == 0);
        if (actionId is > 0) return await q.FirstOrDefaultAsync(a => a.Id == actionId, ct);
        return await q.OrderByDescending(a => a.Id).FirstOrDefaultAsync(ct);
    }

    private async Task SweepExpiredAsync(int userId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        await _db.AiPendingActions
            .Where(a => a.UserId == userId && a.Status == 0 && a.ExpiresAtUtc <= now)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.Status, 2)
                .SetProperty(a => a.DecidedAtUtc, now)
                .SetProperty(a => a.ResultText, "منقضی شد"), ct);
    }
}

// =====================================================================
// کمک‌متن مرخصی: تطبیق نام نوع مرخصی + فهم تاریخ‌های فارسی
// (امروز، فردا، پس‌فردا، نام روز هفته، ۱۴۰۴/۰۷/۰۵ یا 2026-09-27)
// =====================================================================

public static class AiLeaveHelper
{
    /// <summary>پیدا کردن نوع مرخصی از روی نام (تقریبی)؛ پیش‌فرض: استحقاقی.</summary>
    public static async Task<(int id, string name)?> MatchLeaveTypeAsync(IFaAttService fa, string? wanted)
    {
        var types = (await fa.ListLeaveTypesAsync()).Where(t => t.IsActive).ToList();
        if (types.Count == 0) return null;
        var w = AiTextUtil.NormalizeFa(wanted);
        if (w != "")
        {
            var hit = types.FirstOrDefault(t => AiTextUtil.NormalizeFa(t.Name) == w)
                ?? types.FirstOrDefault(t => AiTextUtil.NormalizeFa(t.Name).Contains(w))
                ?? types.FirstOrDefault(t => w.Contains(AiTextUtil.NormalizeFa(t.Name)));
            if (hit != null) return (hit.Id, hit.Name);
        }
        var def = types.FirstOrDefault(t => AiTextUtil.NormalizeFa(t.Name).Contains("استحقاقی")) ?? types[0];
        return (def.Id, def.Name);
    }

    /// <summary>فهم تاریخ فارسی/میلادی به تاریخ میلادی (ساعت ۰۰:۰۰). ناموفق = null.</summary>
    public static DateTime? ParseFaDate(string? text, DateTime today)
    {
        var t = AiTextUtil.NormalizeFa(text).Replace("پس فردا", "پس‌فردا");
        if (t == "") return null;
        today = today.Date;

        if (t is "امروز") return today;
        if (t is "فردا") return today.AddDays(1);
        if (t is "پس‌فردا" or "پس فردا") return today.AddDays(2);

        // نام روز هفته → نزدیک‌ترین آینده (اگر امروز همان روز است، امروز)
        var dow = t switch
        {
            "شنبه" => (DayOfWeek?)DayOfWeek.Saturday,
            "یکشنبه" or "یک شنبه" => DayOfWeek.Sunday,
            "دوشنبه" or "دو شنبه" => DayOfWeek.Monday,
            "سه‌شنبه" or "سه شنبه" => DayOfWeek.Tuesday,
            "چهارشنبه" or "چهار شنبه" => DayOfWeek.Wednesday,
            "پنجشنبه" or "پنج شنبه" => DayOfWeek.Thursday,
            "جمعه" => DayOfWeek.Friday,
            _ => null,
        };
        if (dow != null)
        {
            var delta = ((int)dow.Value - (int)today.DayOfWeek + 7) % 7;
            return today.AddDays(delta);
        }

        // عددی: 1404/07/05 یا 2026-09-27 (ارقام فارسی هم قبول)
        var digits = ToEnDigits(t);
        var m = System.Text.RegularExpressions.Regex.Match(digits, @"(\d{4})\s*[/\-]\s*(\d{1,2})\s*[/\-]\s*(\d{1,2})");
        if (!m.Success) return null;
        var y = int.Parse(m.Groups[1].Value);
        var mo = int.Parse(m.Groups[2].Value);
        var d = int.Parse(m.Groups[3].Value);
        try
        {
            if (y is >= 1300 and <= 1500)
                return new System.Globalization.PersianCalendar().ToDateTime(y, mo, d, 0, 0, 0, 0);
            return new DateTime(y, mo, d);
        }
        catch { return null; }
    }

    private static string ToEnDigits(string s)
    {
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (var c in s)
            sb.Append(c switch
            {
                >= '۰' and <= '۹' => (char)('0' + (c - '۰')),
                >= '٠' and <= '٩' => (char)('0' + (c - '٠')),
                _ => c,
            });
        return sb.ToString();
    }
}
