using Inventory.Api.Data;
using Inventory.Shared;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services.ReportStudio;

// =====================================================================
//  منبع سطرها — ساخت IQueryable از روی جدول‌ها و جوین‌های انتخابی
//
//  رویکرد: هر ترکیبِ مجازِ جدول‌ها به یک پروجکشن تخت (Dictionary مانند)
//  تبدیل می‌شود. به‌جای Expression-building پیچیده و شکننده، از
//  پروجکشن صریح به RsRow استفاده می‌کنیم که:
//   • کاملاً توسط EF Core ترجمه می‌شود (بدون client-evaluation)
//   • تایپ‌سیف است و در زمان کامپایل بررسی می‌شود
//   • خطاهای زمان اجرا را عملاً حذف می‌کند
//
//  درس گرفته از نسخهٔ قبل: هرگز Select().Concat() — همیشه ریشه از
//  جدول هاب و استفاده از نویگیشن‌ها.
// =====================================================================

/// <summary>یک سطر تخت‌شده — کلید = شناسهٔ ستون در کاتالوگ.</summary>
public sealed class RsRow
{
    public Dictionary<string, object?> V { get; set; } = new();
}

public interface IRsRowSource
{
    /// <summary>سطرهای خام را بر اساس ترکیب جدول‌های انتخابی می‌سازد.</summary>
    Task<List<RsRow>> FetchAsync(RsQueryDto q, int hardLimit, RsRowScope scope,
        CancellationToken ct = default);
}

public sealed class RsRowSource : IRsRowSource
{
    private readonly AppDbContext _db;
    public RsRowSource(AppDbContext db) => _db = db;

    /// <summary>محدودهٔ دید کاربر جاری — در هر واکشی نامه اعمال می‌شود.</summary>
    private RsRowScope _scope = RsRowScope.All(0);

    private static string Key(IEnumerable<string> tables) =>
        string.Join("+", tables.Select(t => t.ToLowerInvariant()).OrderBy(t => t));

    public async Task<List<RsRow>> FetchAsync(RsQueryDto q, int hardLimit, RsRowScope scope,
        CancellationToken ct = default)
    {
        _scope = scope ?? RsRowScope.All(0);
        var tables = q.Tables.Select(t => t.TableKey).ToList();
        var shape = Key(tables);
        var leftJoin = q.Tables.Skip(1).All(t => t.JoinKind == RsJoinKind.Left);

        var lowered = tables.Select(t => t.ToLowerInvariant()).ToList();
        if (lowered.Count > 0 && lowered.All(IsWorkOrderTable))
            return await WorkOrdersShapeAsync(q, hardLimit, ct);

        return shape switch
        {
            "letter" => await LettersAsync(hardLimit, ct),
            "erja" => await ErjasAsync(hardLimit, ct),
            "erja+letter" => await LetterErjaAsync(leftJoin, hardLimit, ct),
            "outgoing" => await OutgoingAsync(hardLimit, ct),
            "incoming" => await IncomingAsync(hardLimit, ct),
            "erja+outgoing" => await OutgoingErjaAsync(leftJoin, hardLimit, ct),
            "erja+incoming" => await IncomingErjaAsync(leftJoin, hardLimit, ct),
            "invoice" => await InvoicesAsync(hardLimit, ct),
            "invoice+invoice_line" => await InvoiceLinesAsync(leftJoin, hardLimit, ct),
            "invoice+party" => await InvoicePartyAsync(leftJoin, hardLimit, ct),
            "invoice+invoice_line+party" => await InvoiceLinePartyAsync(leftJoin, hardLimit, ct),
            "invoice_line+invoice+product" => await InvoiceLineProductAsync(leftJoin, hardLimit, ct),
            "invoice+invoice_line+party+product" => await InvoiceFullAsync(leftJoin, hardLimit, ct),
            "party" => await PartiesAsync(hardLimit, ct),
            "product" => await ProductsAsync(hardLimit, ct),
            "stock" => await StocksAsync(hardLimit, ct),
            "document" => await DocumentsAsync(hardLimit, ct),
            "repair" => await RepairsAsync(hardLimit, ct),
            "project" => await ProjectsAsync(hardLimit, ct),
            "transaction" => await TransactionsAsync(hardLimit, ct),
            "expense" => await ExpensesAsync(hardLimit, ct),
            _ => throw new RsUnsupportedShapeException(tables)
        };
    }

    // =====================================================================
    //  اتوماسیون اداری
    // =====================================================================

    /// <summary>پایهٔ نامه‌ها — ریشه از LetterSources تا حذف منطقی رعایت شود.</summary>
    private IQueryable<LetterProj> LetterBase()
    {
        var now = DateTime.Now;
        var uid = _scope.UserId;
        var all = _scope.Unrestricted;

        return _db.LetterSources
            .Where(x => !x.IsDelete
                && (x.InnerLetter != null || x.OutgoingLetter != null || x.IncomingLetter != null))
            // ---------- امنیت سطح سطر ----------
            // بدون مجوز ViewAll، کاربر فقط نامه‌هایی را می‌بیند که ساخته
            // یا در گردش آن‌ها (فرستنده/گیرندهٔ ارجاع) نقش داشته است.
            .Where(x => all
                || (x.InnerLetter != null && x.InnerLetter.CreatorUserId == uid)
                || (x.OutgoingLetter != null && x.OutgoingLetter.CreatorUserId == uid)
                || (x.IncomingLetter != null && x.IncomingLetter.CreateUserId == uid)
                || x.Erjas.Any(e => !e.IsDelete
                        && (e.ReciverUserId == uid || e.SenderUserId == uid)))
            .Select(x => new LetterProj
            {
                Id = x.Id,
                Type = x.SourceType,
                No = x.InnerLetter != null ? x.InnerLetter.LetterNumber
                   : x.OutgoingLetter != null ? (x.OutgoingLetter.SadereNumber ?? x.OutgoingLetter.LetterNumber)
                   : x.IncomingLetter!.LetterNumber,
                Title = x.InnerLetter != null ? x.InnerLetter.Title
                      : x.OutgoingLetter != null ? x.OutgoingLetter.Title
                      : x.IncomingLetter!.Title,
                Date = x.InnerLetter != null ? x.InnerLetter.DateSabt
                     : x.OutgoingLetter != null ? x.OutgoingLetter.DateSabt
                     : x.IncomingLetter!.DateErsal,
                DateClosed = x.OutgoingLetter != null ? x.OutgoingLetter.DateSadere
                           : x.IncomingLetter != null ? (DateTime?)x.IncomingLetter.Date : null,
                CreatorId = x.InnerLetter != null ? x.InnerLetter.CreatorUserId
                          : x.OutgoingLetter != null ? x.OutgoingLetter.CreatorUserId
                          : x.IncomingLetter!.CreateUserId,
                ConfText = x.InnerLetter != null ? x.InnerLetter.Mahramanegi
                         : x.OutgoingLetter != null ? x.OutgoingLetter.Mahramanegi : null,
                ConfNum = x.IncomingLetter != null ? (int?)x.IncomingLetter.Mahramanegi : null,
                UrgText = x.InnerLetter != null ? x.InnerLetter.Foriat
                        : x.OutgoingLetter != null ? x.OutgoingLetter.Foriat : null,
                UrgNum = x.IncomingLetter != null ? (int?)x.IncomingLetter.Foriat : null,
                Party = x.OutgoingLetter != null ? x.OutgoingLetter.ReceiverOrganization
                      : x.IncomingLetter != null ? x.IncomingLetter.Ferestande : null,
                OutStatus = x.OutgoingLetter != null ? (int?)x.OutgoingLetter.Status : null,
                InBayegani = x.IncomingLetter != null && x.IncomingLetter.IsBayegani,
                Method = x.InnerLetter != null ? "اتوماسیون"
                       : x.OutgoingLetter != null ? x.OutgoingLetter.SendMethod
                       : x.IncomingLetter!.TypeErsal,
                Starred = x.InnerLetter != null ? x.InnerLetter.IsNeshan
                        : x.OutgoingLetter != null ? x.OutgoingLetter.IsNeshan
                        : x.IncomingLetter!.IsNeshan,
                ErjaCount = x.Erjas.Count(e => !e.IsDelete),
                DaysOpen = x.OutgoingLetter != null && x.OutgoingLetter.DateSadere != null
                    ? (x.OutgoingLetter.DateSadere.Value - x.OutgoingLetter.DateSabt).TotalDays
                    : x.InnerLetter != null ? (now - x.InnerLetter.DateSabt).TotalDays
                    : x.OutgoingLetter != null ? (now - x.OutgoingLetter.DateSabt).TotalDays
                    : (now - x.IncomingLetter!.DateErsal).TotalDays
            });
    }

    private sealed class LetterProj
    {
        public int Id; public int Type; public string? No; public string? Title;
        public DateTime Date; public DateTime? DateClosed; public int CreatorId;
        public string? ConfText; public int? ConfNum; public string? UrgText; public int? UrgNum;
        public string? Party; public int? OutStatus; public bool InBayegani;
        public string? Method; public bool Starred; public int ErjaCount; public double DaysOpen;
    }

    private async Task<Dictionary<int, string>> UserNamesAsync(IEnumerable<int> ids, CancellationToken ct)
    {
        var list = ids.Where(i => i > 0).Distinct().ToList();
        if (list.Count == 0) return new();
        return await _db.Users.Where(u => list.Contains(u.Id))
            .Select(u => new { u.Id, N = ((u.FirstName ?? "") + " " + (u.LastName ?? "")).Trim(), u.Username })
            .ToDictionaryAsync(u => u.Id, u => string.IsNullOrWhiteSpace(u.N) ? u.Username : u.N, ct);
    }

    private static string ConfFa(string? text, int? num) =>
        num is not null
            ? num switch { 0 => "عادی", 1 => "محرمانه", 2 => "خیلی محرمانه", _ => "سرّی" }
            : string.IsNullOrWhiteSpace(text) ? "عادی" : text.Trim();

    private static string UrgFa(string? text, int? num) =>
        num is not null
            ? num switch { 0 => "عادی", 1 => "فوری", 2 => "خیلی فوری", _ => "آنی" }
            : string.IsNullOrWhiteSpace(text) ? "عادی" : text.Trim();

    private static string StatusFa(int type, int? outStatus, bool bayegani) => type switch
    {
        1 => "ثبت‌شده",
        2 => outStatus switch
        {
            0 => "پیش‌نویس", 1 => "در گردش تایید", 2 => "تایید شده", 3 => "صادر شده", _ => "نامشخص"
        },
        _ => bayegani ? "بایگانی‌شده" : "در جریان"
    };

    private static void FillLetter(RsRow r, LetterProj p, Dictionary<int, string> users)
    {
        r.V["letter.id"] = p.Id;
        r.V["letter.type"] = p.Type;
        r.V["letter.no"] = p.No;
        r.V["letter.title"] = p.Title;
        r.V["letter.date"] = p.Date;
        r.V["letter.date_closed"] = p.DateClosed;
        r.V["letter.creator"] = users.GetValueOrDefault(p.CreatorId);
        r.V["letter.status"] = StatusFa(p.Type, p.OutStatus, p.InBayegani);
        r.V["letter.conf"] = ConfFa(p.ConfText, p.ConfNum);
        r.V["letter.urg"] = UrgFa(p.UrgText, p.UrgNum);
        r.V["letter.party"] = p.Party;
        r.V["letter.method"] = p.Method;
        r.V["letter.starred"] = p.Starred;
        r.V["letter.archived"] = p.InBayegani;
        r.V["letter.erja_count"] = p.ErjaCount;
        r.V["letter.days_open"] = Math.Round(p.DaysOpen, 1);
        r.V["letter.count"] = 1;
    }

    private async Task<List<RsRow>> LettersAsync(int limit, CancellationToken ct)
    {
        var raw = await LetterBase().Take(limit).ToListAsync(ct);
        var users = await UserNamesAsync(raw.Select(x => x.CreatorId), ct);
        return raw.Select(p => { var r = new RsRow(); FillLetter(r, p, users); return r; }).ToList();
    }

    private sealed class ErjaProj
    {
        public int Id; public int SourceId; public int SenderId; public int ReceiverId;
        public DateTime Date; public string Type = ""; public int Taeed;
        public bool IsRead; public string Answer = ""; public DateTime? Deadline; public string Note = "";
    }

    private IQueryable<ErjaProj> ErjaBase()
    {
        var uid = _scope.UserId;
        var all = _scope.Unrestricted;
        return _db.Erjas
            .Where(e => !e.IsDelete)
            // ارجاع‌های دیگران دیده نمی‌شود مگر با مجوز ViewAll
            .Where(e => all || e.ReciverUserId == uid || e.SenderUserId == uid)
            .Select(e => new ErjaProj
        {
            Id = e.ErjaId,
            SourceId = e.SourceId,
            SenderId = e.SenderUserId,
            ReceiverId = e.ReciverUserId,
            Date = e.Date,
            Type = e.Type,
            Taeed = e.TypeTaeed,
            IsRead = e.IsRead,
            Answer = e.Answer,
            Deadline = e.MohlatPasokh,
            Note = e.MatnErja
        });
    }

    private static void FillErja(RsRow r, ErjaProj e, Dictionary<int, string> users, DateTime now)
    {
        var answered = !string.IsNullOrEmpty(e.Answer);
        r.V["erja.id"] = e.Id;
        r.V["erja.sender"] = users.GetValueOrDefault(e.SenderId);
        r.V["erja.receiver"] = users.GetValueOrDefault(e.ReceiverId);
        r.V["erja.date"] = e.Date;
        r.V["erja.type"] = e.Type;
        r.V["erja.taeed"] = e.Taeed;
        r.V["erja.is_read"] = e.IsRead;
        r.V["erja.answered"] = answered;
        r.V["erja.overdue"] = e.Deadline != null && e.Deadline < now && !answered;
        r.V["erja.deadline"] = e.Deadline;
        r.V["erja.note"] = e.Note;
        r.V["erja.count"] = 1;
    }

    private async Task<List<RsRow>> ErjasAsync(int limit, CancellationToken ct)
    {
        var now = DateTime.Now;
        var raw = await ErjaBase().Take(limit).ToListAsync(ct);
        var users = await UserNamesAsync(raw.SelectMany(x => new[] { x.SenderId, x.ReceiverId }), ct);
        return raw.Select(e => { var r = new RsRow(); FillErja(r, e, users, now); return r; }).ToList();
    }

    private async Task<List<RsRow>> LetterErjaAsync(bool left, int limit, CancellationToken ct)
    {
        var now = DateTime.Now;
        var letters = await LetterBase().Take(limit).ToListAsync(ct);
        var ids = letters.Select(l => l.Id).ToList();
        var erjas = await ErjaBase().Where(e => ids.Contains(e.SourceId)).ToListAsync(ct);

        var users = await UserNamesAsync(
            letters.Select(l => l.CreatorId)
                   .Concat(erjas.SelectMany(e => new[] { e.SenderId, e.ReceiverId })), ct);

        var byLetter = erjas.GroupBy(e => e.SourceId).ToDictionary(g => g.Key, g => g.ToList());
        var rows = new List<RsRow>();
        foreach (var l in letters)
        {
            var mine = byLetter.GetValueOrDefault(l.Id);
            if (mine is null || mine.Count == 0)
            {
                if (!left) continue;                 // Inner join: نامهٔ بدون ارجاع حذف می‌شود
                var r = new RsRow();
                FillLetter(r, l, users);
                rows.Add(r);
                continue;
            }
            foreach (var e in mine)
            {
                var r = new RsRow();
                FillLetter(r, l, users);
                FillErja(r, e, users, now);
                rows.Add(r);
                if (rows.Count >= limit) return rows;
            }
        }
        return rows;
    }

    // ---------------- نامه صادره ----------------
    private sealed class OutProj
    {
        public int Id; public string? No; public string? SadereNo; public int Number;
        public string? Title; public DateTime DateSabt; public DateTime? DateSadere;
        public int CreatorId; public int Status; public string? RecvOrg; public string? RecvName;
        public string? RecvTitle; public string? Method; public string? Tracking; public string? Deliverer;
        public string? DestReg; public string? DestEmail; public string? ExtRef; public string? CopyTo;
        public string? Conf; public string? Urg; public bool Dabir; public DateTime? DateDabir;
        public bool Starred; public int SourceId;
    }

    private IQueryable<OutProj> OutgoingBase()
    {
        var uid = _scope.UserId; var all = _scope.Unrestricted;
        return _db.LetterSources
            .Where(x => !x.IsDelete && x.OutgoingLetter != null)
            .Where(x => all || x.OutgoingLetter!.CreatorUserId == uid
                || x.Erjas.Any(e => !e.IsDelete && (e.ReciverUserId == uid || e.SenderUserId == uid)))
            .Select(x => new OutProj
            {
                Id = x.OutgoingLetter!.Id,
                SourceId = x.Id,
                No = x.OutgoingLetter.LetterNumber,
                SadereNo = x.OutgoingLetter.SadereNumber,
                Number = x.OutgoingLetter.Number,
                Title = x.OutgoingLetter.Title,
                DateSabt = x.OutgoingLetter.DateSabt,
                DateSadere = x.OutgoingLetter.DateSadere,
                CreatorId = x.OutgoingLetter.CreatorUserId,
                Status = x.OutgoingLetter.Status,
                RecvOrg = x.OutgoingLetter.ReceiverOrganization,
                RecvName = x.OutgoingLetter.ReceiverName,
                RecvTitle = x.OutgoingLetter.ReceiverTitle,
                Method = x.OutgoingLetter.SendMethod,
                Tracking = x.OutgoingLetter.TrackingCode,
                Deliverer = x.OutgoingLetter.DelivererName,
                DestReg = x.OutgoingLetter.DestRegNumber,
                DestEmail = x.OutgoingLetter.DestEmail,
                ExtRef = x.OutgoingLetter.ExternalRefNumber,
                CopyTo = x.OutgoingLetter.CopyTo,
                Conf = x.OutgoingLetter.Mahramanegi,
                Urg = x.OutgoingLetter.Foriat,
                Dabir = x.OutgoingLetter.DabirkhaneSabt,
                DateDabir = x.OutgoingLetter.DateDabirkhane,
                Starred = x.OutgoingLetter.IsNeshan
            });
    }

    private static void FillOutgoing(RsRow r, OutProj o, Dictionary<int, string> users)
    {
        r.V["outgoing.id"] = o.Id;
        r.V["outgoing.no"] = o.No;
        r.V["outgoing.sadere_no"] = o.SadereNo;
        r.V["outgoing.number"] = o.Number;
        r.V["outgoing.title"] = o.Title;
        r.V["outgoing.date_sabt"] = o.DateSabt;
        r.V["outgoing.date_sadere"] = o.DateSadere;
        r.V["outgoing.creator"] = users.GetValueOrDefault(o.CreatorId);
        r.V["outgoing.status"] = o.Status;
        r.V["outgoing.receiver_org"] = o.RecvOrg;
        r.V["outgoing.receiver_name"] = o.RecvName;
        r.V["outgoing.receiver_title"] = o.RecvTitle;
        r.V["outgoing.method"] = o.Method;
        r.V["outgoing.tracking"] = o.Tracking;
        r.V["outgoing.deliverer"] = o.Deliverer;
        r.V["outgoing.dest_reg_no"] = o.DestReg;
        r.V["outgoing.dest_email"] = o.DestEmail;
        r.V["outgoing.ext_ref"] = o.ExtRef;
        r.V["outgoing.copy_to"] = o.CopyTo;
        r.V["outgoing.conf"] = string.IsNullOrWhiteSpace(o.Conf) ? "عادی" : o.Conf;
        r.V["outgoing.urg"] = string.IsNullOrWhiteSpace(o.Urg) ? "عادی" : o.Urg;
        r.V["outgoing.dabirkhane"] = o.Dabir;
        r.V["outgoing.date_dabirkhane"] = o.DateDabir;
        r.V["outgoing.starred"] = o.Starred;
        r.V["outgoing.days_to_issue"] = o.DateSadere is null
            ? null : (object)Math.Round((o.DateSadere.Value - o.DateSabt).TotalDays, 1);
        r.V["outgoing.count"] = 1;
    }

    private async Task<List<RsRow>> OutgoingAsync(int limit, CancellationToken ct)
    {
        var raw = await OutgoingBase().Take(limit).ToListAsync(ct);
        var users = await UserNamesAsync(raw.Select(x => x.CreatorId), ct);
        return raw.Select(o => { var r = new RsRow(); FillOutgoing(r, o, users); return r; }).ToList();
    }

    private async Task<List<RsRow>> OutgoingErjaAsync(bool left, int limit, CancellationToken ct)
    {
        var now = DateTime.Now;
        var outs = await OutgoingBase().Take(limit).ToListAsync(ct);
        var ids = outs.Select(o => o.SourceId).ToList();
        var erjas = await ErjaBase().Where(e => ids.Contains(e.SourceId)).ToListAsync(ct);
        var users = await UserNamesAsync(outs.Select(o => o.CreatorId)
            .Concat(erjas.SelectMany(e => new[] { e.SenderId, e.ReceiverId })), ct);
        var byId = erjas.GroupBy(e => e.SourceId).ToDictionary(g => g.Key, g => g.ToList());

        var rows = new List<RsRow>();
        foreach (var o in outs)
        {
            var mine = byId.GetValueOrDefault(o.SourceId);
            if (mine is null || mine.Count == 0)
            {
                if (!left) continue;
                var r0 = new RsRow(); FillOutgoing(r0, o, users); rows.Add(r0); continue;
            }
            foreach (var e in mine)
            {
                var r = new RsRow(); FillOutgoing(r, o, users); FillErja(r, e, users, now);
                rows.Add(r);
                if (rows.Count >= limit) return rows;
            }
        }
        return rows;
    }

    // ---------------- نامه وارده ----------------
    private sealed class IncProj
    {
        public int Id; public string? No; public int NumberSabt; public string? NumberVarede;
        public string? Title; public DateTime Date; public DateTime DateErsal; public int CreatorId;
        public string? Sender; public string? Method; public string? DeliveryName;
        public int Conf; public int Urg; public bool Archived; public bool Starred;
        public string? Description; public int SourceId;
    }

    private IQueryable<IncProj> IncomingBase()
    {
        var uid = _scope.UserId; var all = _scope.Unrestricted;
        return _db.LetterSources
            .Where(x => !x.IsDelete && x.IncomingLetter != null)
            .Where(x => all || x.IncomingLetter!.CreateUserId == uid
                || x.Erjas.Any(e => !e.IsDelete && (e.ReciverUserId == uid || e.SenderUserId == uid)))
            .Select(x => new IncProj
            {
                Id = x.IncomingLetter!.Id,
                SourceId = x.Id,
                No = x.IncomingLetter.LetterNumber,
                NumberSabt = x.IncomingLetter.NumberSabt,
                NumberVarede = x.IncomingLetter.NumberLetterVarede,
                Title = x.IncomingLetter.Title,
                Date = x.IncomingLetter.Date,
                DateErsal = x.IncomingLetter.DateErsal,
                CreatorId = x.IncomingLetter.CreateUserId,
                Sender = x.IncomingLetter.Ferestande,
                Method = x.IncomingLetter.TypeErsal,
                DeliveryName = x.IncomingLetter.DeliveryName,
                Conf = x.IncomingLetter.Mahramanegi,
                Urg = x.IncomingLetter.Foriat,
                Archived = x.IncomingLetter.IsBayegani,
                Starred = x.IncomingLetter.IsNeshan,
                Description = x.IncomingLetter.Description
            });
    }

    private static void FillIncoming(RsRow r, IncProj i, Dictionary<int, string> users, DateTime now)
    {
        r.V["incoming.id"] = i.Id;
        r.V["incoming.no"] = i.No;
        r.V["incoming.number_sabt"] = i.NumberSabt;
        r.V["incoming.number_varede"] = i.NumberVarede;
        r.V["incoming.title"] = i.Title;
        r.V["incoming.date"] = i.Date;
        r.V["incoming.date_ersal"] = i.DateErsal;
        r.V["incoming.creator"] = users.GetValueOrDefault(i.CreatorId);
        r.V["incoming.sender"] = i.Sender;
        r.V["incoming.method"] = i.Method;
        r.V["incoming.delivery_name"] = i.DeliveryName;
        r.V["incoming.conf"] = i.Conf;
        r.V["incoming.urg"] = i.Urg;
        r.V["incoming.archived"] = i.Archived;
        r.V["incoming.starred"] = i.Starred;
        r.V["incoming.description"] = i.Description;
        r.V["incoming.days_open"] = Math.Round((now - i.DateErsal).TotalDays, 1);
        r.V["incoming.count"] = 1;
    }

    private async Task<List<RsRow>> IncomingAsync(int limit, CancellationToken ct)
    {
        var now = DateTime.Now;
        var raw = await IncomingBase().Take(limit).ToListAsync(ct);
        var users = await UserNamesAsync(raw.Select(x => x.CreatorId), ct);
        return raw.Select(i => { var r = new RsRow(); FillIncoming(r, i, users, now); return r; }).ToList();
    }

    private async Task<List<RsRow>> IncomingErjaAsync(bool left, int limit, CancellationToken ct)
    {
        var now = DateTime.Now;
        var incs = await IncomingBase().Take(limit).ToListAsync(ct);
        var ids = incs.Select(i => i.SourceId).ToList();
        var erjas = await ErjaBase().Where(e => ids.Contains(e.SourceId)).ToListAsync(ct);
        var users = await UserNamesAsync(incs.Select(i => i.CreatorId)
            .Concat(erjas.SelectMany(e => new[] { e.SenderId, e.ReceiverId })), ct);
        var byId = erjas.GroupBy(e => e.SourceId).ToDictionary(g => g.Key, g => g.ToList());

        var rows = new List<RsRow>();
        foreach (var i in incs)
        {
            var mine = byId.GetValueOrDefault(i.SourceId);
            if (mine is null || mine.Count == 0)
            {
                if (!left) continue;
                var r0 = new RsRow(); FillIncoming(r0, i, users, now); rows.Add(r0); continue;
            }
            foreach (var e in mine)
            {
                var r = new RsRow(); FillIncoming(r, i, users, now); FillErja(r, e, users, now);
                rows.Add(r);
                if (rows.Count >= limit) return rows;
            }
        }
        return rows;
    }

    // ---------------- فروش و خرید ----------------


    // =====================================================================
    //  فروش و خرید
    // =====================================================================

    private sealed class InvProj
    {
        public int Id; public int Number; public int Kind; public int Status;
        public DateTime Date; public DateTime? Due; public string? PartyName; public int? PartyId;
        public string? Warehouse; public decimal Gross; public decimal Discount;
        public decimal Vat; public decimal Net;
    }

    private IQueryable<InvProj> InvoiceBase() =>
        _db.FacInvoices.Select(i => new InvProj
        {
            Id = i.Id,
            Number = i.Number,
            Kind = (int)i.Kind,
            Status = (int)i.Status,
            Date = i.Date,
            Due = i.DueDate,
            PartyId = i.PartyId,
            PartyName = i.Party != null ? i.Party.Name : null,
            Warehouse = i.Warehouse != null ? i.Warehouse.Name : null,
            Gross = i.TotalGross,
            Discount = i.TotalLineDiscount + i.InvoiceDiscount,
            Vat = i.TotalVat,
            Net = i.TotalNet
        });

    private static void FillInvoice(RsRow r, InvProj i)
    {
        r.V["invoice.id"] = i.Id;
        r.V["invoice.number"] = i.Number;
        r.V["invoice.kind"] = i.Kind;
        r.V["invoice.status"] = i.Status;
        r.V["invoice.date"] = i.Date;
        r.V["invoice.due"] = i.Due;
        r.V["invoice.party"] = i.PartyName;
        r.V["invoice.warehouse"] = i.Warehouse;
        r.V["invoice.gross"] = i.Gross;
        r.V["invoice.discount"] = i.Discount;
        r.V["invoice.vat"] = i.Vat;
        r.V["invoice.net"] = i.Net;
        r.V["invoice.count"] = 1;
    }

    private async Task<List<RsRow>> InvoicesAsync(int limit, CancellationToken ct)
    {
        var raw = await InvoiceBase().Take(limit).ToListAsync(ct);
        return raw.Select(i => { var r = new RsRow(); FillInvoice(r, i); return r; }).ToList();
    }

    private sealed class LineProj
    {
        public int InvoiceId; public int ProductId; public string? Product; public string? Code;
        public string? Category; public string? Unit; public decimal Qty; public decimal Price;
        public decimal Discount; public decimal Total;
    }

    private IQueryable<LineProj> LineBase() =>
        _db.FacInvoiceLines.Select(l => new LineProj
        {
            InvoiceId = l.InvoiceId,
            ProductId = l.ProductId,
            Product = l.Product != null ? l.Product.Name : null,
            Code = l.Product != null ? l.Product.Code : null,
            Category = l.Product != null ? l.Product.Category : null,
            Unit = l.Product != null ? l.Product.Unit : null,
            Qty = l.Quantity,
            Price = l.UnitPrice,
            Discount = l.Discount,
            Total = l.Total
        });

    private static void FillLine(RsRow r, LineProj l)
    {
        r.V["invoice_line.product"] = l.Product;
        r.V["invoice_line.product_code"] = l.Code;
        r.V["invoice_line.category"] = l.Category;
        r.V["invoice_line.unit"] = l.Unit;
        r.V["invoice_line.qty"] = l.Qty;
        r.V["invoice_line.price"] = l.Price;
        r.V["invoice_line.discount"] = l.Discount;
        r.V["invoice_line.total"] = l.Total;
        r.V["invoice_line.count"] = 1;
    }

    private async Task<List<(InvProj Inv, LineProj? Line)>> InvLinePairsAsync(
        bool left, int limit, CancellationToken ct)
    {
        var invs = await InvoiceBase().Take(limit).ToListAsync(ct);
        var ids = invs.Select(i => i.Id).ToList();
        var lines = await LineBase().Where(l => ids.Contains(l.InvoiceId)).ToListAsync(ct);
        var byInv = lines.GroupBy(l => l.InvoiceId).ToDictionary(g => g.Key, g => g.ToList());

        var pairs = new List<(InvProj, LineProj?)>();
        foreach (var i in invs)
        {
            var mine = byInv.GetValueOrDefault(i.Id);
            if (mine is null || mine.Count == 0)
            {
                if (left) pairs.Add((i, null));
                continue;
            }
            foreach (var l in mine)
            {
                pairs.Add((i, l));
                if (pairs.Count >= limit) return pairs;
            }
        }
        return pairs;
    }

    private async Task<List<RsRow>> InvoiceLinesAsync(bool left, int limit, CancellationToken ct)
    {
        var pairs = await InvLinePairsAsync(left, limit, ct);
        return pairs.Select(p =>
        {
            var r = new RsRow();
            FillInvoice(r, p.Inv);
            if (p.Line is not null) FillLine(r, p.Line);
            return r;
        }).ToList();
    }

    private sealed class PartyProj
    {
        public int Id; public string Name = ""; public string? Mobile;
        public string? Phone; public bool IsActive;
    }

    private IQueryable<PartyProj> PartyBase() =>
        _db.Parties.Select(p => new PartyProj
        {
            Id = p.Id, Name = p.Name, Mobile = p.Mobile, Phone = p.Phone, IsActive = p.IsActive
        });

    private static void FillParty(RsRow r, PartyProj p)
    {
        r.V["party.id"] = p.Id;
        r.V["party.name"] = p.Name;
        r.V["party.mobile"] = p.Mobile;
        r.V["party.phone"] = p.Phone;
        r.V["party.is_active"] = p.IsActive;
        r.V["party.count"] = 1;
    }

    private async Task<List<RsRow>> PartiesAsync(int limit, CancellationToken ct)
    {
        var raw = await PartyBase().Take(limit).ToListAsync(ct);
        return raw.Select(p => { var r = new RsRow(); FillParty(r, p); return r; }).ToList();
    }

    private sealed class ProductProj
    {
        public int Id; public string Name = ""; public string Code = ""; public string? Category;
        public string Unit = ""; public decimal Sale; public decimal Purchase; public bool IsActive;
    }

    private IQueryable<ProductProj> ProductBase() =>
        _db.Products.Select(p => new ProductProj
        {
            Id = p.Id, Name = p.Name, Code = p.Code, Category = p.Category,
            Unit = p.Unit, Sale = p.SalePrice, Purchase = p.PurchasePrice, IsActive = p.IsActive
        });

    private static void FillProduct(RsRow r, ProductProj p)
    {
        r.V["product.id"] = p.Id;
        r.V["product.name"] = p.Name;
        r.V["product.code"] = p.Code;
        r.V["product.category"] = p.Category;
        r.V["product.unit"] = p.Unit;
        r.V["product.sale_price"] = p.Sale;
        r.V["product.purchase_price"] = p.Purchase;
        r.V["product.is_active"] = p.IsActive;
        r.V["product.count"] = 1;
    }

    private async Task<List<RsRow>> ProductsAsync(int limit, CancellationToken ct)
    {
        var raw = await ProductBase().Take(limit).ToListAsync(ct);
        return raw.Select(p => { var r = new RsRow(); FillProduct(r, p); return r; }).ToList();
    }

    private async Task<Dictionary<int, PartyProj>> PartyMapAsync(IEnumerable<int> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        if (list.Count == 0) return new();
        return await PartyBase().Where(p => list.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
    }

    private async Task<Dictionary<int, ProductProj>> ProductMapAsync(IEnumerable<int> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        if (list.Count == 0) return new();
        return await ProductBase().Where(p => list.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
    }

    private async Task<List<RsRow>> InvoicePartyAsync(bool left, int limit, CancellationToken ct)
    {
        var invs = await InvoiceBase().Take(limit).ToListAsync(ct);
        var parties = await PartyMapAsync(invs.Where(i => i.PartyId != null).Select(i => i.PartyId!.Value), ct);
        var rows = new List<RsRow>();
        foreach (var i in invs)
        {
            PartyProj? pp = i.PartyId != null ? parties.GetValueOrDefault(i.PartyId.Value) : null;
            if (pp is null && !left) continue;
            var r = new RsRow();
            FillInvoice(r, i);
            if (pp is not null) FillParty(r, pp);
            rows.Add(r);
        }
        return rows;
    }

    private async Task<List<RsRow>> InvoiceLinePartyAsync(bool left, int limit, CancellationToken ct)
    {
        var pairs = await InvLinePairsAsync(left, limit, ct);
        var parties = await PartyMapAsync(
            pairs.Where(p => p.Inv.PartyId != null).Select(p => p.Inv.PartyId!.Value), ct);

        var rows = new List<RsRow>();
        foreach (var (inv, line) in pairs)
        {
            PartyProj? pp = inv.PartyId != null ? parties.GetValueOrDefault(inv.PartyId.Value) : null;
            if (pp is null && !left) continue;
            var r = new RsRow();
            FillInvoice(r, inv);
            if (line is not null) FillLine(r, line);
            if (pp is not null) FillParty(r, pp);
            rows.Add(r);
        }
        return rows;
    }

    private async Task<List<RsRow>> InvoiceLineProductAsync(bool left, int limit, CancellationToken ct)
    {
        var pairs = await InvLinePairsAsync(left, limit, ct);
        var products = await ProductMapAsync(
            pairs.Where(p => p.Line != null).Select(p => p.Line!.ProductId), ct);

        var rows = new List<RsRow>();
        foreach (var (inv, line) in pairs)
        {
            ProductProj? pr = line != null ? products.GetValueOrDefault(line.ProductId) : null;
            if (pr is null && !left) continue;
            var r = new RsRow();
            FillInvoice(r, inv);
            if (line is not null) FillLine(r, line);
            if (pr is not null) FillProduct(r, pr);
            rows.Add(r);
        }
        return rows;
    }

    private async Task<List<RsRow>> InvoiceFullAsync(bool left, int limit, CancellationToken ct)
    {
        var pairs = await InvLinePairsAsync(left, limit, ct);
        var parties = await PartyMapAsync(
            pairs.Where(p => p.Inv.PartyId != null).Select(p => p.Inv.PartyId!.Value), ct);
        var products = await ProductMapAsync(
            pairs.Where(p => p.Line != null).Select(p => p.Line!.ProductId), ct);

        var rows = new List<RsRow>();
        foreach (var (inv, line) in pairs)
        {
            var r = new RsRow();
            FillInvoice(r, inv);
            if (line is not null) FillLine(r, line);
            if (inv.PartyId != null && parties.TryGetValue(inv.PartyId.Value, out var pp)) FillParty(r, pp);
            if (line != null && products.TryGetValue(line.ProductId, out var pr)) FillProduct(r, pr);
            rows.Add(r);
        }
        return rows;
    }

    // =====================================================================
    //  انبار و موجودی
    // =====================================================================
    private async Task<List<RsRow>> StocksAsync(int limit, CancellationToken ct)
    {
        var raw = await _db.Stocks
            .OrderBy(x => x.WarehouseId).ThenBy(x => x.ProductId)
            .Take(limit)
            .Select(x => new { x.Id, x.ProductId, x.WarehouseId, x.Quantity, x.AvgCost })
            .ToListAsync(ct);

        var prodIds = raw.Select(x => x.ProductId).Distinct().ToList();
        var whIds = raw.Select(x => x.WarehouseId).Distinct().ToList();

        var prods = prodIds.Count == 0
            ? new Dictionary<int, Inventory.Api.Data.Product>()
            : await _db.Products.Where(p => prodIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var whs = whIds.Count == 0
            ? new Dictionary<int, Inventory.Api.Data.Warehouse>()
            : await _db.Warehouses.Where(w => whIds.Contains(w.Id)).ToDictionaryAsync(w => w.Id, ct);

        return raw.Select(x =>
        {
            var prod = prods.GetValueOrDefault(x.ProductId);
            var r = new RsRow();
            r.V["stock.id"] = x.Id;
            r.V["stock.product"] = prod?.Name ?? "";
            r.V["stock.code"] = prod?.Code ?? "";
            r.V["stock.warehouse"] = whs.GetValueOrDefault(x.WarehouseId)?.Name ?? "";
            r.V["stock.quantity"] = x.Quantity;
            r.V["stock.avg_cost"] = x.AvgCost;
            r.V["stock.value"] = x.Quantity * x.AvgCost;
            r.V["stock.count"] = 1;
            return r;
        }).ToList();
    }

    // =====================================================================
    //  آرشیو اسناد و مدارک
    // =====================================================================
    private async Task<List<RsRow>> DocumentsAsync(int limit, CancellationToken ct)
    {
        var raw = await _db.Documents
            .Where(d => !d.IsDeleted)
            .OrderBy(d => d.Code)
            .Take(limit)
            .Select(d => new
            {
                d.Id, d.Code, d.Title, d.FolderId, d.CustomerCode,
                d.ExpireDate, d.IsPublic, d.RequireDownloadConfirm
            })
            .ToListAsync(ct);

        var folderIds = raw.Select(x => x.FolderId).Distinct().ToList();
        var folders = folderIds.Count == 0
            ? new Dictionary<int, DocFolder>()
            : await _db.DocFolders.Where(f => folderIds.Contains(f.Id)).ToDictionaryAsync(f => f.Id, ct);

        return raw.Select(x =>
        {
            var r = new RsRow();
            r.V["document.id"] = x.Id;
            r.V["document.code"] = x.Code;
            r.V["document.title"] = x.Title;
            r.V["document.folder"] = folders.GetValueOrDefault(x.FolderId)?.Name ?? "";
            r.V["document.customer_code"] = x.CustomerCode ?? "";
            r.V["document.expire_date"] = x.ExpireDate;
            r.V["document.is_public"] = x.IsPublic;
            r.V["document.require_confirm"] = x.RequireDownloadConfirm;
            r.V["document.count"] = 1;
            return r;
        }).ToList();
    }

    // =====================================================================
    //  تعمیرات
    // =====================================================================
    private static string RepairStatusFa(RepairStatus s) => s switch
    {
        RepairStatus.Received => "پذیرش شده",
        RepairStatus.InProgress => "در حال تعمیر",
        RepairStatus.Ready => "آماده تحویل",
        RepairStatus.Delivered => "تحویل شده",
        RepairStatus.Cancelled => "لغو شده",
        _ => "نامشخص"
    };

    private async Task<List<RsRow>> RepairsAsync(int limit, CancellationToken ct)
    {
        var raw = await _db.RepairOrders
            .OrderByDescending(x => x.ReceivedAt)
            .Take(limit)
            .Select(x => new
            {
                x.Id, x.Number, x.PartyId, x.TechnicianId, x.DeviceType,
                x.DeviceModel, x.Status, x.ReceivedAt, x.DeliveredAt, x.QuotedPrice
            })
            .ToListAsync(ct);

        var partyIds = raw.Select(x => x.PartyId).Distinct().ToList();
        var techIds = raw.Where(x => x.TechnicianId != null)
                         .Select(x => x.TechnicianId!.Value).Distinct().ToList();

        var parties = partyIds.Count == 0
            ? new Dictionary<int, Inventory.Api.Data.Party>()
            : await _db.Parties.Where(p => partyIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var techs = techIds.Count == 0
            ? new Dictionary<int, Inventory.Api.Data.Technician>()
            : await _db.Technicians.Where(t => techIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, ct);

        return raw.Select(x =>
        {
            var r = new RsRow();
            r.V["repair.id"] = x.Id;
            r.V["repair.number"] = x.Number;
            r.V["repair.party"] = parties.GetValueOrDefault(x.PartyId)?.Name ?? "";
            r.V["repair.device_type"] = x.DeviceType;
            r.V["repair.device_model"] = x.DeviceModel ?? "";
            r.V["repair.technician"] = x.TechnicianId != null
                ? techs.GetValueOrDefault(x.TechnicianId.Value)?.Name ?? "" : "";
            r.V["repair.status"] = RepairStatusFa(x.Status);
            r.V["repair.received_at"] = x.ReceivedAt;
            r.V["repair.delivered_at"] = x.DeliveredAt;
            r.V["repair.quoted_price"] = x.QuotedPrice;
            r.V["repair.count"] = 1;
            return r;
        }).ToList();
    }

    // =====================================================================
    //  پروژه‌ها
    // =====================================================================
    private async Task<List<RsRow>> ProjectsAsync(int limit, CancellationToken ct)
    {
        var raw = await _db.ProjectEntryExits
            .OrderByDescending(x => x.Id)
            .Take(limit)
            .Select(x => new
            {
                x.Id, x.CodeProject, x.ProjectName, x.ProjectReceiver,
                x.SerialNumber, x.ExitDate, x.EntryDate
            })
            .ToListAsync(ct);

        return raw.Select(x =>
        {
            var r = new RsRow();
            r.V["project.id"] = x.Id;
            r.V["project.code"] = x.CodeProject;
            r.V["project.name"] = x.ProjectName;
            r.V["project.receiver"] = x.ProjectReceiver;
            r.V["project.serial"] = x.SerialNumber;
            r.V["project.exit_date"] = x.ExitDate;
            r.V["project.entry_date"] = x.EntryDate;
            r.V["project.count"] = 1;
            return r;
        }).ToList();
    }

    // =====================================================================
    //  مالی و خزانه‌داری
    // =====================================================================
    private static string TransactionTypeFa(TransactionType t) => t switch
    {
        TransactionType.Initial => "موجودی اول دوره",
        TransactionType.Purchase => "رسید خرید",
        TransactionType.Sale => "حواله فروش",
        TransactionType.Adjustment => "تعدیل",
        _ => "نامشخص"
    };

    private static string PaymentMethodFa(PaymentMethod m) => m switch
    {
        PaymentMethod.Cash => "نقدی",
        PaymentMethod.Credit => "نسیه",
        PaymentMethod.Cheque => "چک",
        PaymentMethod.Installment => "اقساطی",
        PaymentMethod.Mixed => "ترکیبی",
        _ => "نامشخص"
    };

    private async Task<List<RsRow>> TransactionsAsync(int limit, CancellationToken ct)
    {
        var raw = await _db.Transactions
            .OrderByDescending(x => x.Date)
            .Take(limit)
            .Select(x => new
            {
                x.Id, x.Number, x.Type, x.Date, x.PartyId,
                x.Amount, x.SettledAmount, x.PaymentMethod, x.DueDate
            })
            .ToListAsync(ct);

        var partyIds = raw.Where(x => x.PartyId != null)
                          .Select(x => x.PartyId!.Value).Distinct().ToList();
        var parties = partyIds.Count == 0
            ? new Dictionary<int, Inventory.Api.Data.Party>()
            : await _db.Parties.Where(p => partyIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);

        return raw.Select(x =>
        {
            var r = new RsRow();
            r.V["transaction.id"] = x.Id;
            r.V["transaction.number"] = x.Number;
            r.V["transaction.type"] = TransactionTypeFa(x.Type);
            r.V["transaction.date"] = x.Date;
            r.V["transaction.party"] = x.PartyId != null
                ? parties.GetValueOrDefault(x.PartyId.Value)?.Name ?? "" : "";
            r.V["transaction.payment_method"] = PaymentMethodFa(x.PaymentMethod);
            r.V["transaction.amount"] = x.Amount;
            r.V["transaction.settled_amount"] = x.SettledAmount;
            r.V["transaction.due_date"] = x.DueDate;
            r.V["transaction.count"] = 1;
            return r;
        }).ToList();
    }

    private async Task<List<RsRow>> ExpensesAsync(int limit, CancellationToken ct)
    {
        var raw = await _db.Expenses
            .OrderByDescending(x => x.Date)
            .Take(limit)
            .Select(x => new { x.Id, x.Number, x.CategoryId, x.Amount, x.Date, x.Payee, x.PayType })
            .ToListAsync(ct);

        var catIds = raw.Select(x => x.CategoryId).Distinct().ToList();
        var cats = catIds.Count == 0
            ? new Dictionary<int, ExpenseCategory>()
            : await _db.ExpenseCategories.Where(c => catIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);

        return raw.Select(x =>
        {
            var r = new RsRow();
            r.V["expense.id"] = x.Id;
            r.V["expense.number"] = x.Number;
            r.V["expense.category"] = cats.GetValueOrDefault(x.CategoryId)?.Name ?? "";
            r.V["expense.payee"] = x.Payee ?? "";
            r.V["expense.pay_type"] = x.PayType == CashType.Cash ? "نقد" : "غیرنقد";
            r.V["expense.date"] = x.Date;
            r.V["expense.amount"] = x.Amount;
            r.V["expense.count"] = 1;
            return r;
        }).ToList();
    }

    // =====================================================================
    //  دستور کار
    // =====================================================================

    private static bool IsWorkOrderTable(string key) => key is
        "workorder" or "wo_assignee" or "wo_checklist" or "wo_comment" or "wo_log" or "wo_attachment";

    private IQueryable<WorkOrder> VisibleWorkOrders()
    {
        var q = _db.WorkOrders.AsNoTracking();
        if (_scope.CanSeeAllWorkOrders) return q;
        var uid = _scope.UserId;
        return q.Where(w => w.OwnerUserId == uid
            || _db.WorkOrderAssignees.Any(a => a.OrderId == w.Id && a.UserId == uid));
    }

    private sealed class WoSnap
    {
        public int Id; public string Number = ""; public string Title = ""; public string Description = "";
        public string OwnerName = ""; public int OwnerUserId; public DateTime DueAt; public string Status = "Open";
        public int Priority; public int Recurrence; public DateTime CreatedAt; public DateTime? ClosedAt;
        public string? CloseNote; public int ExtensionCount; public string? Tags;
        public string? SourceModule; public int? SourceId; public int? ParentOrderId;
    }

    private sealed class WoAsgSnap
    {
        public int Id; public int OrderId; public int UserId; public string Name = "";
        public DateTime? SeenAt; public DateTime? RepliedAt; public bool? Done;
        public string? ReplyText; public string? OwnerDecision; public string? OwnerDecisionNote;
    }

    private sealed class WoChkSnap
    {
        public int Id; public int OrderId; public string Text = ""; public int SortOrder;
        public bool IsDone; public string? DoneByName; public DateTime? DoneAt;
    }

    private sealed class WoCmtSnap
    {
        public int Id; public int OrderId; public string AuthorName = ""; public string Text = "";
        public int? ReplyToId; public bool IsDeleted; public DateTime CreatedAt;
    }

    private sealed class WoLogSnap
    {
        public int Id; public int OrderId; public string ActorName = ""; public string Action = "";
        public string? Text; public DateTime CreatedAt;
    }

    private sealed class WoAttSnap
    {
        public int Id; public int OrderId; public string FileName = ""; public string ContentType = "";
        public string UploaderName = ""; public DateTime UploadedAt;
    }

    private async Task<List<RsRow>> WorkOrdersShapeAsync(RsQueryDto q, int limit, CancellationToken ct)
    {
        var wanted = q.Tables.Select(t => t.TableKey.ToLowerInvariant()).ToHashSet();
        var inner = q.Tables
            .Where(t => !string.IsNullOrWhiteSpace(t.JoinKey))
            .Where(t => t.JoinKind != RsJoinKind.Left)
            .Select(t => t.TableKey.ToLowerInvariant())
            .ToHashSet();

        var wantOrder = wanted.Contains("workorder");
        var childKeys = wanted.Where(t => t != "workorder").ToList();

        // جدول فرزند به‌تنهایی: از خودِ فرزند شروع می‌کنیم تا سطرهای قدیمی جا نمانند.
        if (!wantOrder && childKeys.Count == 1)
            return await WorkOrderChildOnlyAsync(childKeys[0], limit, ct);

        var orders = await VisibleWorkOrders()
            .OrderByDescending(w => w.Id)
            .Take(limit)
            .Select(w => new WoSnap
            {
                Id = w.Id, Number = w.Number, Title = w.Title, Description = w.Description,
                OwnerName = w.OwnerName, OwnerUserId = w.OwnerUserId, DueAt = w.DueAt, Status = w.Status,
                Priority = w.Priority, Recurrence = w.Recurrence, CreatedAt = w.CreatedAt, ClosedAt = w.ClosedAt,
                CloseNote = w.CloseNote, ExtensionCount = w.ExtensionCount, Tags = w.Tags,
                SourceModule = w.SourceModule, SourceId = w.SourceId, ParentOrderId = w.ParentOrderId
            })
            .ToListAsync(ct);

        return await ComposeWorkOrdersAsync(orders, wanted, inner, limit, ct);
    }

    private async Task<List<RsRow>> WorkOrderChildOnlyAsync(string child, int limit, CancellationToken ct)
    {
        var visible = VisibleWorkOrders().Select(w => w.Id);
        var rows = new List<RsRow>();
        var now = DateTime.Now;

        if (child == "wo_assignee")
        {
            var asgs = await _db.WorkOrderAssignees.AsNoTracking()
                .Where(a => visible.Contains(a.OrderId))
                .OrderByDescending(a => a.Id).Take(limit)
                .Select(a => new WoAsgSnap
                {
                    Id = a.Id, OrderId = a.OrderId, UserId = a.UserId, Name = a.Name,
                    SeenAt = a.SeenAt, RepliedAt = a.RepliedAt, Done = a.Done,
                    ReplyText = a.ReplyText, OwnerDecision = a.OwnerDecision, OwnerDecisionNote = a.OwnerDecisionNote
                }).ToListAsync(ct);
            var orders = await OrderMapAsync(asgs.Select(a => a.OrderId), ct);
            foreach (var a in asgs)
            {
                var r = new RsRow();
                FillAssignee(r, a, orders.GetValueOrDefault(a.OrderId), now);
                rows.Add(r);
            }
            return rows;
        }

        if (child == "wo_checklist")
        {
            var items = await _db.WorkOrderChecklistItems.AsNoTracking()
                .Where(c => visible.Contains(c.OrderId))
                .OrderByDescending(c => c.OrderId).ThenBy(c => c.SortOrder).Take(limit)
                .Select(c => new WoChkSnap
                {
                    Id = c.Id, OrderId = c.OrderId, Text = c.Text, SortOrder = c.SortOrder,
                    IsDone = c.IsDone, DoneByName = c.DoneByName, DoneAt = c.DoneAt
                }).ToListAsync(ct);
            var orders = await OrderMapAsync(items.Select(c => c.OrderId), ct);
            foreach (var c in items)
            {
                var r = new RsRow();
                FillChecklist(r, c, orders.GetValueOrDefault(c.OrderId));
                rows.Add(r);
            }
            return rows;
        }

        if (child == "wo_comment")
        {
            var items = await _db.WorkOrderComments.AsNoTracking()
                .Where(c => visible.Contains(c.OrderId))
                .OrderByDescending(c => c.Id).Take(limit)
                .Select(c => new WoCmtSnap
                {
                    Id = c.Id, OrderId = c.OrderId, AuthorName = c.AuthorName, Text = c.Text,
                    ReplyToId = c.ReplyToId, IsDeleted = c.IsDeleted, CreatedAt = c.CreatedAt
                }).ToListAsync(ct);
            var orders = await OrderMapAsync(items.Select(c => c.OrderId), ct);
            foreach (var c in items)
            {
                var r = new RsRow();
                FillComment(r, c, orders.GetValueOrDefault(c.OrderId));
                rows.Add(r);
            }
            return rows;
        }

        if (child == "wo_log")
        {
            var items = await _db.WorkOrderLogs.AsNoTracking()
                .Where(c => visible.Contains(c.OrderId))
                .OrderByDescending(c => c.Id).Take(limit)
                .Select(c => new WoLogSnap
                {
                    Id = c.Id, OrderId = c.OrderId, ActorName = c.ActorName, Action = c.Action,
                    Text = c.Text, CreatedAt = c.CreatedAt
                }).ToListAsync(ct);
            var orders = await OrderMapAsync(items.Select(c => c.OrderId), ct);
            foreach (var c in items)
            {
                var r = new RsRow();
                FillLog(r, c, orders.GetValueOrDefault(c.OrderId));
                rows.Add(r);
            }
            return rows;
        }

        var atts = await _db.AppAttachments.AsNoTracking()
            .Where(c => c.Module == "WorkOrders" && visible.Contains(c.RefId))
            .OrderByDescending(c => c.Id).Take(limit)
            .Select(c => new WoAttSnap
            {
                Id = c.Id, OrderId = c.RefId, FileName = c.FileName, ContentType = c.ContentType,
                UploaderName = c.UploaderName, UploadedAt = c.UploadedAt
            }).ToListAsync(ct);
        var attOrders = await OrderMapAsync(atts.Select(c => c.OrderId), ct);
        foreach (var c in atts)
        {
            var r = new RsRow();
            FillAttachment(r, c, attOrders.GetValueOrDefault(c.OrderId));
            rows.Add(r);
        }
        return rows;
    }

    private async Task<Dictionary<int, WoSnap>> OrderMapAsync(IEnumerable<int> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        if (list.Count == 0) return new();
        var rows = await _db.WorkOrders.AsNoTracking()
            .Where(w => list.Contains(w.Id))
            .Select(w => new WoSnap
            {
                Id = w.Id, Number = w.Number, Title = w.Title, OwnerName = w.OwnerName,
                DueAt = w.DueAt, Status = w.Status, ParentOrderId = w.ParentOrderId
            }).ToListAsync(ct);
        return rows.ToDictionary(w => w.Id);
    }

    private async Task<List<RsRow>> ComposeWorkOrdersAsync(
        List<WoSnap> orders, HashSet<string> wanted, HashSet<string> inner, int limit, CancellationToken ct)
    {
        var ids = orders.Select(o => o.Id).ToList();
        var now = DateTime.Now;
        var parentIds = orders.Where(o => o.ParentOrderId != null).Select(o => o.ParentOrderId!.Value).Distinct().ToList();
        var parents = parentIds.Count == 0
            ? new Dictionary<int, string>()
            : await _db.WorkOrders.AsNoTracking().Where(w => parentIds.Contains(w.Id))
                .Select(w => new { w.Id, w.Number }).ToDictionaryAsync(w => w.Id, w => w.Number, ct);

        var asgs = await _db.WorkOrderAssignees.AsNoTracking()
            .Where(a => ids.Contains(a.OrderId))
            .Select(a => new WoAsgSnap
            {
                Id = a.Id, OrderId = a.OrderId, UserId = a.UserId, Name = a.Name,
                SeenAt = a.SeenAt, RepliedAt = a.RepliedAt, Done = a.Done,
                ReplyText = a.ReplyText, OwnerDecision = a.OwnerDecision, OwnerDecisionNote = a.OwnerDecisionNote
            }).ToListAsync(ct);
        var asgBy = asgs.GroupBy(a => a.OrderId).ToDictionary(g => g.Key, g => g.ToList());

        var checks = wanted.Contains("wo_checklist") || wanted.Contains("workorder")
            ? await _db.WorkOrderChecklistItems.AsNoTracking()
                .Where(c => ids.Contains(c.OrderId))
                .Select(c => new WoChkSnap
                {
                    Id = c.Id, OrderId = c.OrderId, Text = c.Text, SortOrder = c.SortOrder,
                    IsDone = c.IsDone, DoneByName = c.DoneByName, DoneAt = c.DoneAt
                }).ToListAsync(ct)
            : new List<WoChkSnap>();
        var chkBy = checks.GroupBy(c => c.OrderId).ToDictionary(g => g.Key, g => g.OrderBy(x => x.SortOrder).ToList());

        var comments = wanted.Contains("wo_comment") || wanted.Contains("workorder")
            ? await _db.WorkOrderComments.AsNoTracking()
                .Where(c => ids.Contains(c.OrderId))
                .Select(c => new WoCmtSnap
                {
                    Id = c.Id, OrderId = c.OrderId, AuthorName = c.AuthorName, Text = c.Text,
                    ReplyToId = c.ReplyToId, IsDeleted = c.IsDeleted, CreatedAt = c.CreatedAt
                }).ToListAsync(ct)
            : new List<WoCmtSnap>();
        var cmtBy = comments.GroupBy(c => c.OrderId).ToDictionary(g => g.Key, g => g.ToList());

        var logs = wanted.Contains("wo_log")
            ? await _db.WorkOrderLogs.AsNoTracking()
                .Where(c => ids.Contains(c.OrderId))
                .Select(c => new WoLogSnap
                {
                    Id = c.Id, OrderId = c.OrderId, ActorName = c.ActorName, Action = c.Action,
                    Text = c.Text, CreatedAt = c.CreatedAt
                }).ToListAsync(ct)
            : new List<WoLogSnap>();
        var logBy = logs.GroupBy(c => c.OrderId).ToDictionary(g => g.Key, g => g.ToList());

        var atts = wanted.Contains("wo_attachment") || wanted.Contains("workorder")
            ? await _db.AppAttachments.AsNoTracking()
                .Where(c => c.Module == "WorkOrders" && ids.Contains(c.RefId))
                .Select(c => new WoAttSnap
                {
                    Id = c.Id, OrderId = c.RefId, FileName = c.FileName, ContentType = c.ContentType,
                    UploaderName = c.UploaderName, UploadedAt = c.UploadedAt
                }).ToListAsync(ct)
            : new List<WoAttSnap>();
        var attBy = atts.GroupBy(c => c.OrderId).ToDictionary(g => g.Key, g => g.ToList());

        var childOrder = new[] { "wo_assignee", "wo_checklist", "wo_comment", "wo_log", "wo_attachment" }
            .Where(wanted.Contains).ToList();

        if (orders.Count == 0) return new();

        var rows = new List<RsRow>();
        foreach (var o in orders)
        {
            var dims = new List<List<object?>>();
            var drop = false;
            foreach (var key in childOrder)
            {
                List<object?> items = key switch
                {
                    "wo_assignee" => (asgBy.GetValueOrDefault(o.Id) ?? new()).Cast<object?>().ToList(),
                    "wo_checklist" => (chkBy.GetValueOrDefault(o.Id) ?? new()).Cast<object?>().ToList(),
                    "wo_comment" => (cmtBy.GetValueOrDefault(o.Id) ?? new()).Cast<object?>().ToList(),
                    "wo_log" => (logBy.GetValueOrDefault(o.Id) ?? new()).Cast<object?>().ToList(),
                    _ => (attBy.GetValueOrDefault(o.Id) ?? new()).Cast<object?>().ToList()
                };
                if (items.Count == 0)
                {
                    if (inner.Contains(key)) { drop = true; break; }
                    dims.Add(new List<object?> { null });
                }
                else dims.Add(items);
            }
            if (drop) continue;

            foreach (var combo in Cartesian(dims))
            {
                var r = new RsRow();
                if (wanted.Contains("workorder"))
                    FillWorkOrder(r, o, parents, asgBy.GetValueOrDefault(o.Id), chkBy.GetValueOrDefault(o.Id),
                        cmtBy.GetValueOrDefault(o.Id), attBy.GetValueOrDefault(o.Id), now);
                for (var i = 0; i < childOrder.Count; i++)
                {
                    if (combo[i] is null) continue;
                    switch (childOrder[i])
                    {
                        case "wo_assignee": FillAssignee(r, (WoAsgSnap)combo[i]!, o, now); break;
                        case "wo_checklist": FillChecklist(r, (WoChkSnap)combo[i]!, o); break;
                        case "wo_comment": FillComment(r, (WoCmtSnap)combo[i]!, o); break;
                        case "wo_log": FillLog(r, (WoLogSnap)combo[i]!, o); break;
                        case "wo_attachment": FillAttachment(r, (WoAttSnap)combo[i]!, o); break;
                    }
                }
                rows.Add(r);
                if (rows.Count >= limit) return rows;
            }
        }
        return rows;
    }

    private static IEnumerable<object?[]> Cartesian(List<List<object?>> dims)
    {
        if (dims.Count == 0)
        {
            yield return Array.Empty<object?>();
            yield break;
        }
        var idx = new int[dims.Count];
        while (true)
        {
            var row = new object?[dims.Count];
            for (var i = 0; i < dims.Count; i++) row[i] = dims[i][idx[i]];
            yield return row;
            var k = dims.Count - 1;
            while (k >= 0)
            {
                idx[k]++;
                if (idx[k] < dims[k].Count) break;
                idx[k] = 0;
                k--;
            }
            if (k < 0) yield break;
        }
    }

    private static void FillWorkOrder(RsRow r, WoSnap o, Dictionary<int, string> parents,
        List<WoAsgSnap>? asgs, List<WoChkSnap>? checks, List<WoCmtSnap>? comments, List<WoAttSnap>? atts, DateTime now)
    {
        asgs ??= new();
        checks ??= new();
        comments ??= new();
        atts ??= new();
        var chkDone = checks.Count(c => c.IsDone);
        var asgDone = asgs.Count(a => a.Done == true);
        var progress = checks.Count > 0
            ? (int)Math.Round(chkDone * 100.0 / checks.Count)
            : asgs.Count > 0 ? (int)Math.Round(asgDone * 100.0 / asgs.Count)
            : o.Status == "Closed" ? 100 : 0;

        r.V["workorder.id"] = o.Id;
        r.V["workorder.number"] = o.Number;
        r.V["workorder.title"] = o.Title;
        r.V["workorder.description"] = PlainText(o.Description);
        r.V["workorder.owner"] = o.OwnerName;
        r.V["workorder.status"] = string.IsNullOrWhiteSpace(o.Status) ? "Open" : o.Status;
        r.V["workorder.priority"] = o.Priority;
        r.V["workorder.recurrence"] = o.Recurrence;
        r.V["workorder.outcome"] = OutcomeOf(o, asgs, now);
        r.V["workorder.due_at"] = o.DueAt;
        r.V["workorder.created_at"] = o.CreatedAt;
        r.V["workorder.closed_at"] = o.ClosedAt;
        r.V["workorder.close_note"] = o.CloseNote ?? "";
        r.V["workorder.extension_count"] = o.ExtensionCount;
        r.V["workorder.tags"] = TagsFa(o.Tags);
        r.V["workorder.source"] = SourceFa(o.SourceModule);
        r.V["workorder.source_id"] = o.SourceId;
        r.V["workorder.parent_no"] = o.ParentOrderId is int pid && parents.TryGetValue(pid, out var pn) ? pn : "";
        r.V["workorder.assignees"] = string.Join("، ", asgs.Select(a => a.Name).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct());
        r.V["workorder.assignee_count"] = asgs.Count;
        r.V["workorder.checklist_done"] = chkDone;
        r.V["workorder.checklist_total"] = checks.Count;
        r.V["workorder.progress"] = progress;
        r.V["workorder.comment_count"] = comments.Count(c => !c.IsDeleted);
        r.V["workorder.attachment_count"] = atts.Count;
        r.V["workorder.days_left"] = Math.Round((decimal)((o.DueAt - now).TotalDays), 1);
        r.V["workorder.overdue"] = o.Status == "Open" && o.DueAt < now;
        r.V["workorder.count"] = 1;
    }

    private static void FillAssignee(RsRow r, WoAsgSnap a, WoSnap? o, DateTime now)
    {
        r.V["wo_assignee.id"] = a.Id;
        r.V["wo_assignee.order_id"] = a.OrderId;
        r.V["wo_assignee.order_no"] = o?.Number ?? "";
        r.V["wo_assignee.order_title"] = o?.Title ?? "";
        r.V["wo_assignee.order_status"] = o?.Status ?? "";
        r.V["wo_assignee.order_due"] = o?.DueAt;
        r.V["wo_assignee.owner"] = o?.OwnerName ?? "";
        r.V["wo_assignee.user"] = a.Name;
        r.V["wo_assignee.seen_at"] = a.SeenAt;
        r.V["wo_assignee.replied_at"] = a.RepliedAt;
        r.V["wo_assignee.result"] = a.Done == true ? "done" : a.Done == false ? "notdone" : "pending";
        r.V["wo_assignee.reply"] = a.ReplyText ?? "";
        r.V["wo_assignee.decision"] = string.IsNullOrWhiteSpace(a.OwnerDecision) ? "none" : a.OwnerDecision;
        r.V["wo_assignee.decision_note"] = a.OwnerDecisionNote ?? "";
        var due = o?.DueAt;
        r.V["wo_assignee.late"] = due != null && (
            (a.RepliedAt != null && a.RepliedAt > due) ||
            (a.RepliedAt == null && due < now && (o?.Status == "Open")));
        r.V["wo_assignee.count"] = 1;
    }

    private static void FillChecklist(RsRow r, WoChkSnap c, WoSnap? o)
    {
        r.V["wo_checklist.id"] = c.Id;
        r.V["wo_checklist.order_id"] = c.OrderId;
        r.V["wo_checklist.order_no"] = o?.Number ?? "";
        r.V["wo_checklist.order_title"] = o?.Title ?? "";
        r.V["wo_checklist.text"] = c.Text;
        r.V["wo_checklist.sort"] = c.SortOrder;
        r.V["wo_checklist.is_done"] = c.IsDone;
        r.V["wo_checklist.done_by"] = c.DoneByName ?? "";
        r.V["wo_checklist.done_at"] = c.DoneAt;
        r.V["wo_checklist.count"] = 1;
    }

    private static void FillComment(RsRow r, WoCmtSnap c, WoSnap? o)
    {
        r.V["wo_comment.id"] = c.Id;
        r.V["wo_comment.order_id"] = c.OrderId;
        r.V["wo_comment.order_no"] = o?.Number ?? "";
        r.V["wo_comment.order_title"] = o?.Title ?? "";
        r.V["wo_comment.author"] = c.AuthorName;
        r.V["wo_comment.text"] = c.IsDeleted ? "" : c.Text;
        r.V["wo_comment.created_at"] = c.CreatedAt;
        r.V["wo_comment.is_reply"] = c.ReplyToId != null;
        r.V["wo_comment.is_deleted"] = c.IsDeleted;
        r.V["wo_comment.count"] = 1;
    }

    private static void FillLog(RsRow r, WoLogSnap c, WoSnap? o)
    {
        r.V["wo_log.id"] = c.Id;
        r.V["wo_log.order_id"] = c.OrderId;
        r.V["wo_log.order_no"] = o?.Number ?? "";
        r.V["wo_log.order_title"] = o?.Title ?? "";
        r.V["wo_log.actor"] = c.ActorName;
        r.V["wo_log.action"] = c.Action;
        r.V["wo_log.text"] = c.Text ?? "";
        r.V["wo_log.created_at"] = c.CreatedAt;
        r.V["wo_log.count"] = 1;
    }

    private static void FillAttachment(RsRow r, WoAttSnap c, WoSnap? o)
    {
        r.V["wo_attachment.id"] = c.Id;
        r.V["wo_attachment.order_id"] = c.OrderId;
        r.V["wo_attachment.order_no"] = o?.Number ?? "";
        r.V["wo_attachment.order_title"] = o?.Title ?? "";
        r.V["wo_attachment.file_name"] = c.FileName;
        r.V["wo_attachment.content_type"] = c.ContentType;
        r.V["wo_attachment.uploader"] = c.UploaderName;
        r.V["wo_attachment.uploaded_at"] = c.UploadedAt;
        r.V["wo_attachment.count"] = 1;
    }

    private static string OutcomeOf(WoSnap o, List<WoAsgSnap> asgs, DateTime now)
    {
        var allDone = asgs.Count > 0 && asgs.All(a => a.Done == true);
        if (allDone)
        {
            var last = asgs.Max(a => a.RepliedAt) ?? DateTime.MaxValue;
            return last <= o.DueAt ? "ontime" : "latedone";
        }
        if (o.Status == "Closed") return "closednodone";
        if (o.DueAt < now) return "late";
        return "open";
    }

    private static string SourceFa(string? module) => (module ?? "").Trim() switch
    {
        "InnerLetter" => "نامه داخلی",
        "OutgoingLetter" => "نامه صادره",
        "IncomingLetter" => "نامه وارده",
        "MeetingMinutes" => "صورتجلسه",
        "MeetingMinutesItem" => "بند صورتجلسه",
        "Document" => "مدرک آرشیو",
        "" => "",
        var other => other
    };

    private static string TagsFa(string? tags)
    {
        if (string.IsNullOrWhiteSpace(tags)) return "";
        return string.Join("، ", tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private static string PlainText(string? html, int max = 400)
    {
        if (string.IsNullOrWhiteSpace(html)) return "";
        var sb = new System.Text.StringBuilder(html.Length);
        var tag = false;
        foreach (var ch in html)
        {
            if (ch == '<') { tag = true; continue; }
            if (ch == '>')
            {
                tag = false;
                if (sb.Length > 0 && sb[^1] != ' ') sb.Append(' ');
                continue;
            }
            if (!tag) sb.Append(ch);
        }
        var s = System.Net.WebUtility.HtmlDecode(sb.ToString()).Replace('\n', ' ').Replace('\r', ' ').Trim();
        while (s.Contains("  ")) s = s.Replace("  ", " ");
        return s.Length <= max ? s : s[..max] + "…";
    }
}

/// <summary>ترکیب جدول‌ها پشتیبانی نمی‌شود — با پیام راهنما به کاربر.</summary>
public sealed class RsUnsupportedShapeException : Exception
{
    public RsUnsupportedShapeException(IEnumerable<string> tables)
        : base("این ترکیب از جدول‌ها هنوز پشتیبانی نمی‌شود: " +
               string.Join(" + ", tables.Select(t => RsSchemaCatalog.FindTable(t)?.Title ?? t)) +
               ". لطفاً جدول‌ها را طبق مسیرهای جوین پیشنهادی انتخاب کنید.")
    { }
}
