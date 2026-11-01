using System.Globalization;
using System.Security.Claims;
using Inventory.Api.Data;
using Inventory.Api.Hubs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Inventory.Api.Services;
using Inventory.Api.Services.ItAssets;
using Inventory.Shared;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Controllers;

/// <summary>دستور کار — ایجاد برای خود/دیگران، پاسخ، تایید/رد، تمدید (تا ۵ بار)، بستن و بایگانی.</summary>
[ApiController]
[Route("api/workorders")]
[Authorize]
public class WorkOrdersController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly INotifyService _notify;
    private readonly FileStore _store;
    private readonly IWorkOrderSmsService _sms;
    public WorkOrdersController(AppDbContext db, INotifyService notify, FileStore store, IWorkOrderSmsService sms)
    { _db = db; _notify = notify; _store = store; _sms = sms; }

    /// <summary>
    /// حافظهٔ کوتاه‌مدتِ «ثبت‌های تازه» برای جلوگیری از دستور کار تکراری.
    /// کلید = شناسهٔ درخواست (ClientRequestId) یا اثرانگشت محتوا؛ مقدار = شناسهٔ دستور کار و زمان.
    /// فقط چند ثانیه معتبر است تا ثبت عمدیِ دومین دستور کارِ مشابه بلاک نشود.
    /// </summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (int OrderId, string Number, DateTime At)> RecentCreates = new();

    /// <summary>پنجرهٔ زمانی یکسان‌سازی ثبت — دو کلیک پشت‌سرهم قطعاً داخل این بازه‌اند.</summary>
    private static readonly TimeSpan DuplicateWindow = TimeSpan.FromSeconds(12);

    /// <summary>اثرانگشت محتواییِ درخواست — برای مواردی که کلاینت ClientRequestId نفرستد.</summary>
    private static string CreateFingerprint(int userId, CreateDto dto, int companyId)
    {
        var assignees = string.Join(",", dto.AssigneeUserIds.Distinct().OrderBy(x => x));
        var raw = string.Join("|", userId, companyId, dto.Title.Trim(), (dto.Description ?? "").Trim(),
            dto.DueAt.ToString("O"), dto.Priority, dto.Recurrence, assignees,
            dto.SourceModule ?? "", dto.SourceId?.ToString() ?? "", dto.ParentOrderId?.ToString() ?? "",
            dto.ProjectId?.ToString() ?? "", dto.Weight);
        using var sha = System.Security.Cryptography.SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(raw)));
    }

    private void RememberCreate(string key, int orderId, string number)
    {
        // پاک‌سازی ورودی‌های منقضی — بدون تایمر و بدون نشتی حافظه
        var now = DateTime.UtcNow;
        foreach (var kv in RecentCreates)
            if (now - kv.Value.At > DuplicateWindow) RecentCreates.TryRemove(kv.Key, out _);
        RecentCreates[key] = (orderId, number, now);
    }

    private int MyUserId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var v) ? v : 0;
    private string MyUsername => User.FindFirstValue(ClaimTypes.Name) ?? "";
    private bool IsLegacyAdmin => User.IsInRole("Admin");

    // کلید زمینهٔ شرکت در HttpContext — همان کلیدی که میان‌افزار شرکت فعال (در صورت فعال بودن) پر می‌کند.
    private const string CompanyItemKey = "ActiveCompanyId";
    private const string CompanyHeaderName = "X-Company-Id";

    /// <summary>
    /// شناسهٔ شرکت فعال — ابتدا نتیجهٔ میان‌افزار شرکت فعال (که هدر درخواستی را با دسترسی‌های
    /// کاربر اعتبارسنجی می‌کند) و در نبود آن، بررسی صریح هدر؛ مدیر قدیمی بدون ردیف دسترسی هم پذیرفته می‌شود.
    /// </summary>
    private async Task<int?> ActiveCompanyIdAsync()
    {
        if (HttpContext.Items[CompanyItemKey] is int resolved)
            return resolved > 0 ? resolved : null;

        if (!int.TryParse(Request.Headers[CompanyHeaderName].FirstOrDefault(), out var id) || id <= 0)
            return null;
        return await _db.UserCompanyAccesses.AnyAsync(x => x.UserId == MyUserId && x.CompanyId == id) || IsLegacyAdmin ? id : null;
    }

    private async Task<string> MyDisplayNameAsync()
    {
        var me = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == MyUserId);
        var n = UserDisplay.Name(me);
        return string.IsNullOrWhiteSpace(n) ? MyUsername : n;
    }

    private async Task<bool> HasAsync(string action)
    {
        var hasRoles = await _db.UserRoles.AnyAsync(ur => ur.UserId == MyUserId && _db.Roles.Any(r => r.Id == ur.RoleId && r.IsActive));
        if (!hasRoles)
        {
            var legacy = User.FindFirstValue(ClaimTypes.Role);
            if (legacy == "Admin") return true;
            if (legacy == "Operator") return action is "Create" or "View";
            return false;
        }
        return await _db.UserRoles.Where(ur => ur.UserId == MyUserId && _db.Roles.Any(r => r.Id == ur.RoleId && r.IsActive))
            .Join(_db.RolePermissions, ur => ur.RoleId, rp => rp.RoleId, (ur, rp) => rp.PermissionId)
            .Join(_db.Permissions, pid => pid, p => p.Id, (pid, p) => p)
            .AnyAsync(p => p.Module == "WorkOrders" && p.Action == action);
    }

    private void Log(int orderId, string action, string? text) =>
        _db.WorkOrderLogs.Add(new WorkOrderLog { OrderId = orderId, ActorName = MyUsername, Action = action, Text = text });

    /// <summary>
    /// هستهٔ «افزودن افراد به گردش واقعی نامه» — بدون بررسی مجوز؛ بررسی مجوز وظیفهٔ فراخوان است.
    /// برای هر کاربری که هنوز در گردش نامه نیست، یک ردیف ارجاع (Erja) ساخته می‌شود تا از
    /// فهرست دستور کار روی لینک نامه، به کارتابل همان نامه دسترسی داشته باشد.
    /// خروجی: تعداد افرادی که واقعاً به گردش اضافه شدند.
    /// </summary>
    private async Task<int> EnsureLetterFlowForUsersAsync(int sourceId, IEnumerable<int> userIds,
        int senderUserId, string orderTitle, string orderNumber)
    {
        if (sourceId <= 0) return 0;
        var targets = (userIds ?? Enumerable.Empty<int>()).Where(x => x > 0).Distinct().ToList();
        if (targets.Count == 0) return 0;

        var isLetter = await _db.LetterSources.AsNoTracking()
            .AnyAsync(x => x.Id == sourceId && !x.IsDelete && x.SourceType >= 1 && x.SourceType <= 3);
        if (!isLetter) return 0;

        // ---- دو استثنا که نباید در «دریافتی» ردیف بسازند ----
        // ۱) خودِ کاربر جاری (سازندهٔ دستور کار / ارجاع‌دهنده): ساختن یا گرفتنِ دستور کار
        //    روی یک نامه، نباید همان نامه را دوباره در صندوق دریافتی خودش بگذارد؛
        //    حتی اگر دستور کار را به خودش واگذار کند. این ردیف قبلاً ساخته می‌شد و
        //    منجر به «نامه در دریافتی من ظاهر شد در حالی که به خودم ارجاع نداده‌ام» بود.
        // ۲) ثبت‌کننده/ایجادکنندهٔ همان نامه: طبق قاعدهٔ کارتابل، ثبت‌کننده نامهٔ خودش را
        //    در «دریافتی» نمی‌بیند مگر با ارجاع صریح (مسیر ارجاعِ کارتابل، نه این مسیر).
        targets = targets.Where(u => u != MyUserId).ToList();
        if (targets.Count == 0) return 0;

        var letterCreatorId = await _db.LetterSources.AsNoTracking()
            .Where(x => x.Id == sourceId)
            .Select(x => x.SourceType == 1
                ? (x.InnerLetter != null ? x.InnerLetter.CreatorUserId : (int?)null)
                : x.SourceType == 2
                    ? (x.OutgoingLetter != null ? x.OutgoingLetter.CreatorUserId : (int?)null)
                    : (x.IncomingLetter != null ? x.IncomingLetter.CreateUserId : (int?)null))
            .FirstOrDefaultAsync();
        if (letterCreatorId is > 0)
            targets = targets.Where(u => u != letterCreatorId.Value).ToList();
        if (targets.Count == 0) return 0;

        var existing = await _db.Erjas
            .Where(x => x.SourceId == sourceId && !x.IsDelete && targets.Contains(x.ReciverUserId))
            .Select(x => x.ReciverUserId).Distinct().ToListAsync();
        var newTargets = targets.Except(existing).ToList();
        if (newTargets.Count == 0) return 0;

        var text = $"دسترسی به نامه از طریق دستورکار «{orderTitle}» ({orderNumber}) ایجاد شد.";
        foreach (var userId in newTargets)
        {
            _db.Erjas.Add(new Erja
            {
                SourceId = sourceId,
                SenderUserId = senderUserId,
                ReciverUserId = userId,
                Date = DateTime.Now,
                Type = "ارجاع",
                MatnErja = text,
                AmalgarId = 1,
                ShowMassage = true
            });
        }

        // اعلان «نامهٔ جدید در کارتابل شما» — عمداً فقط برای افرادی که در این لحظه به گردش
        // نامه اضافه شدند (newTargets)؛ کسانی که از قبل در گردش بودند اعلان تکراری نمی‌گیرند.
        await NotifyNewLetterAccessAsync(sourceId, newTargets, orderTitle, orderNumber, senderUserId);

        return newTargets.Count;
    }

    /// <summary>
    /// اعلان «نامهٔ جدید در کارتابل شما» برای افرادی که تازه به گردش نامه اضافه شده‌اند.
    /// لینک اعلان همان مسیر کارتابلِ درستِ همان نوع نامه است تا با یک کلیک باز شود.
    /// </summary>
    private async Task NotifyNewLetterAccessAsync(int sourceId, List<int> userIds,
        string orderTitle, string orderNumber, int senderUserId)
    {
        if (userIds.Count == 0) return;

        var letter = await _db.LetterSources.AsNoTracking()
            .Where(x => x.Id == sourceId && !x.IsDelete)
            .Select(x => new
            {
                x.SourceType,
                InnerTitle = x.InnerLetter != null ? x.InnerLetter.Title : null,
                InnerNumber = x.InnerLetter != null ? x.InnerLetter.LetterNumber : null,
                OutTitle = x.OutgoingLetter != null ? x.OutgoingLetter.Title : null,
                OutNumber = x.OutgoingLetter != null ? x.OutgoingLetter.SadereNumber : null,
                IncTitle = x.IncomingLetter != null ? x.IncomingLetter.Title : null,
                IncNumber = x.IncomingLetter != null ? x.IncomingLetter.LetterNumber : null
            })
            .FirstOrDefaultAsync();
        if (letter == null) return;

        var (formName, route, number, title) = letter.SourceType switch
        {
            1 => ("نامه داخلی", "/letters", letter.InnerNumber, letter.InnerTitle),
            2 => ("نامه صادره", "/outgoing-letters", letter.OutNumber, letter.OutTitle),
            3 => ("نامه وارده", "/incoming-letters", letter.IncNumber, letter.IncTitle),
            _ => ("نامه", "/letters", null, null)
        };

        var senderName = await _db.Users.AsNoTracking()
            .Where(u => u.Id == senderUserId)
            .Select(u => string.IsNullOrWhiteSpace((u.FirstName ?? "") + (u.LastName ?? ""))
                ? u.Username : (u.FirstName + " " + u.LastName).Trim())
            .FirstOrDefaultAsync() ?? "دستور کار";

        var numberTag = string.IsNullOrWhiteSpace(number) ? "" : $"{number} — ";
        var titleTag = string.IsNullOrWhiteSpace(title) ? "" : $"«{title}»";
        var body = $"{numberTag}{titleTag} — دسترسی از طریق دستورکار «{orderTitle}» ({orderNumber})";

        foreach (var uid in userIds.Where(id => id > 0 && id != senderUserId).Distinct())
            await _notify.SendAsync(uid, "نامهٔ جدید در کارتابل شما 📨", body, senderName, formName,
                $"{route}?open={sourceId}");
    }

    /// <summary>
    /// گیرندگانِ یک دستورکارِ متصل به نامه را به گردش همان نامه اضافه می‌کند.
    /// برای مسیرهای «ویرایش دستور کار» و «افزودن نفر به گردش» که مبدأ از خودِ رکورد خوانده می‌شود.
    /// </summary>
    private Task<int> SyncOrderAssigneesToLetterFlowAsync(WorkOrder order, IEnumerable<int> userIds)
    {
        if (order.SourceId is not > 0) return Task.FromResult(0);
        if (!IsLetterModuleOrUnknown(order.SourceModule)) return Task.FromResult(0);
        return EnsureLetterFlowForUsersAsync(order.SourceId.Value, userIds, MyUserId, order.Title, order.Number);
    }

    /// <summary>
    /// افراد جدید دستورکار را به گردش واقعی نامه اضافه می‌کند. این کار عمداً در API
    /// انجام می‌شود تا ایجاد دستورکار از هر کلاینتی، دسترسی ناقص به نامه نسازد.
    /// </summary>
    /// <param name="requireRequestorInFlow">
    /// true = سازندهٔ دستورکار باید ایجادکننده یا گیرندهٔ نامه باشد (مسیر «ساخت از نامه»).
    /// false = مبدأ از دستور والد به ارث رسیده و پیوند از قبل برقرار است (زیر-دستور).
    /// </param>
    private async Task<int> AddNewLetterWorkOrderAssigneesToFlowAsync(CreateDto dto, WorkOrder order, bool requireRequestorInFlow = true)
    {
        if (dto.SourceId is not > 0) return 0;

        // ماژولِ صریحِ غیرنامه (مدرک/صورتجلسه) اصلاً به گردشِ نامه ربطی ندارد
        if (!IsLetterModuleOrUnknown(dto.SourceModule)) return 0;

        // نامهٔ مبدأ مستقل از نامِ ماژولِ ارسالیِ کلاینت و از روی شناسه پیدا می‌شود:
        // جدولِ LetterSources کلیدِ مشترکِ هر سه نوع نامه است و SourceType آن نوعِ
        // واقعیِ نامه را می‌گوید. پیش‌تر، نامِ ماژول ملاک بود و اگر کلاینت نامِ اشتباه
        // (یا نامِ قدیمی/خالی) می‌فرستاد، یا دستورکار ساخته نمی‌شد یا با نوعِ نادرست
        // ذخیره می‌شد و لینکِ «نامه» به کارتابلِ اشتباه می‌رفت.
        var source = await _db.LetterSources.AsNoTracking()
            .Include(x => x.InnerLetter)
            .Include(x => x.OutgoingLetter)
            .Include(x => x.IncomingLetter)
            .FirstOrDefaultAsync(x => x.Id == dto.SourceId.Value && !x.IsDelete && x.SourceType >= 1 && x.SourceType <= 3);

        if (source == null)
        {
            // مبدأ یک نامه نیست و نامِ ماژول هم نامه را نشان نمی‌دهد — کاری لازم نیست.
            if (ModuleToLetterType(dto.SourceModule) is null) return 0;
            throw new InvalidOperationException("نامهٔ مبدأ برای ایجاد گردش دستورکار پیدا نشد.");
        }

        var sourceType = source.SourceType;

        var creatorId = sourceType == 1 ? source.InnerLetter?.CreatorUserId
                     : sourceType == 2 ? source.OutgoingLetter?.CreatorUserId
                     : source.IncomingLetter?.CreateUserId;
        if (requireRequestorInFlow)
        {
            var mayUseLetter = creatorId == MyUserId || await _db.Erjas.AnyAsync(e =>
                e.SourceId == source.Id && e.ReciverUserId == MyUserId && !e.IsDelete);
            if (!mayUseLetter && !IsLegacyAdmin)
                throw new InvalidOperationException("فقط ایجادکننده یا گیرندهٔ حاضر در گردش نامه می‌تواند از این نامه دستورکار بسازد.");
        }

        // مقدار کاننیکال از نوعِ واقعیِ LetterSource تعیین می‌شود تا اشتباهِ کلاینت
        // یا داده‌های قدیمی باعث جابه‌جاییِ فیلترِ نوعِ نامه و اشتباه در لینک نشود.
        order.SourceModule = LetterModuleName(sourceType) ?? order.SourceModule;

        return await EnsureLetterFlowForUsersAsync(source.Id, dto.AssigneeUserIds.Where(x => x != MyUserId),
            MyUserId, order.Title, order.Number);
    }

    // ================== دسترسی‌ها و لیست افراد مجاز ==================
    [HttpGet("my-access")]
    public async Task<IActionResult> MyAccess() => Ok(new
    {
        userId = MyUserId,
        canView = await HasAsync("View"),
        canCreate = await HasAsync("Create"),
        canAssignOthers = await HasAsync("AssignOthers"),
        canDelete = await HasAsync("Delete"),
        smsConfigured = _sms.IsConfigured
    });

    /// <summary>افرادی که کاربر جاری می‌تواند به آن‌ها دستور کار بدهد (بند ۶).</summary>
    [HttpGet("targets")]
    public async Task<IActionResult> Targets([FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        var canOthers = await HasAsync("AssignOthers");
        var query = _db.Users.AsNoTracking().Where(u => u.Id == MyUserId ||
            (canOthers && u.IsActive && (IsLegacyAdmin || _db.WorkOrderAllowedAssignees
                .Any(a => a.OwnerUserId == MyUserId && a.TargetUserId == u.Id))));
        var pagination = new Paging.Request(skip, take);
        var users = await query.OrderBy(u => u.Id != MyUserId).ThenBy(u => u.Id).ToPageListAsync(pagination);
        return Ok(pagination.Result(users.Select(u => new
        {
            u.Id,
            Username = u.Id == MyUserId
                ? (string.IsNullOrWhiteSpace((u.FirstName + " " + u.LastName).Trim()) ? MyUsername : (u.FirstName + " " + u.LastName).Trim()) + " (خودم)"
                : (string.IsNullOrWhiteSpace((u.FirstName + " " + u.LastName).Trim()) ? u.Username : (u.FirstName + " " + u.LastName).Trim() + $" ({u.Username})")
        })));

    }

    /// <summary>لیست مجاز یک کاربر (پیکربندی — فقط مدیر).</summary>
    [HttpGet("allowed/{userId:int}")]
    public async Task<IActionResult> GetAllowed(int userId, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (!IsLegacyAdmin && !await HasAsync("AssignOthers")) return Forbid();
        return Ok(await Paging.ResultAsync(_db.WorkOrderAllowedAssignees.Where(a => a.OwnerUserId == userId)
            .Select(a => a.TargetUserId), skip, take));
    }

    [HttpPost("allowed/{userId:int}")]
    public async Task<IActionResult> SetAllowed(int userId, [FromBody] List<int> targetIds)
    {
        if (!IsLegacyAdmin) return Forbid();
        var old = await _db.WorkOrderAllowedAssignees.Where(a => a.OwnerUserId == userId).ToListAsync();
        _db.WorkOrderAllowedAssignees.RemoveRange(old);
        foreach (var t in targetIds.Distinct().Where(t => t != userId))
            _db.WorkOrderAllowedAssignees.Add(new WorkOrderAllowedAssignee { OwnerUserId = userId, TargetUserId = t });
        await _db.SaveChangesAsync();
        return Ok();
    }

    // ================== ایجاد دستور کار ==================
    public class CreateDto
    {
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public DateTime DueAt { get; set; }
        public List<int> AssigneeUserIds { get; set; } = new();
        /// <summary>ارسال پیامک اطلاع‌رسانی به شماره موبایل گیرندگان پس از ثبت موفق دستور کار.</summary>
        public bool SendSms { get; set; }

        /// <summary>اولویت: 0=کم | 1=عادی | 2=بالا | 3=فوری (پیش‌فرض: عادی).</summary>
        public int Priority { get; set; } = WorkOrderPriority.Normal;

        /// <summary>تکرار: 0=بدون تکرار | 1=روزانه | 2=هفتگی | 3=ماهانه.</summary>
        public int Recurrence { get; set; } = WorkOrderRecurrence.None;

        /// <summary>آیتم‌های چک‌لیست زیرکار (اختیاری) — به ترتیب لیست ذخیره می‌شوند.</summary>
        public List<string> ChecklistItems { get; set; } = new();

        /// <summary>ماژول مبدأ — مثلاً "InnerLetter" برای نامه داخلی (اختیاری).</summary>
        public string? SourceModule { get; set; }
        public int? SourceId { get; set; }
        /// <summary>شناسهٔ رکورد والد مبدأ (مثلاً شناسهٔ صورتجلسه برای بند) — اختیاری.</summary>
        public int? SourceRefId { get; set; }

        /// <summary>برچسب‌ها (اختیاری) — حداکثر ۵ برچسب، هر یک تا ۳۰ حرف.</summary>
        public List<string> Tags { get; set; } = new();

        /// <summary>
        /// ارجاع زنجیره‌ای (اختیاری) — شناسهٔ دستور والد. فقط گیرندهٔ دستور والد (تا وقتی باز است)
        /// می‌تواند بخشی از کار را به‌صورت زیر-دستور به نفر بعدی ارجاع دهد.
        /// </summary>
        public int? ParentOrderId { get; set; }

        /// <summary>پروژه کاری متصل (اختیاری) — باید باز و متعلق به شرکت فعال باشد.</summary>
        public int? ProjectId { get; set; }

        /// <summary>وزن دستور در پیشرفت پروژه: ۰ تا ۱۰ (۰ = مستثنی از محاسبه)؛ پیش‌فرض ۱.</summary>
        public int Weight { get; set; } = 1;

        /// <summary>
        /// کلید یکتای هر بار پر کردن فرم (اختیاری). کلاینت هنگام باز شدن فرم یک GUID می‌سازد و
        /// پس از ثبت موفق آن را عوض می‌کند. اگر همان کلید دوباره برسد (دوبار کلیک، ارسال مجدد
        /// مرورگر، شبکهٔ کند) دستور کار دوم ساخته نمی‌شود و همان دستور کار قبلی برگردانده می‌شود.
        /// </summary>
        public string? ClientRequestId { get; set; }
    }

    /// <summary>
    /// نرمال‌سازی برچسب‌ها: حذف تکراری/خالی، برش طول، سقف ۵ عدد؛
    /// خروجی به شکل ",کارگاه,برق," ذخیره می‌شود تا فیلتر دقیق LIKE ممکن باشد.
    /// </summary>
    private static string? NormalizeTags(IEnumerable<string>? tags)
    {
        if (tags == null) return null;
        var list = tags.Select(t => (t ?? "").Trim().Replace(",", "،"))
            .Where(t => t.Length > 0)
            .Select(t => t.Length > 30 ? t[..30] : t)
            .Distinct()
            .Take(5)
            .ToList();
        return list.Count == 0 ? null : "," + string.Join(",", list) + ",";
    }

    /// <summary>تبدیل رشتهٔ ذخیره‌شده به لیست برچسب‌ها برای خروجی API.</summary>
    private static List<string> TagsToList(string? tags) =>
        string.IsNullOrWhiteSpace(tags)
            ? new List<string>()
            : tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateDto dto)
    {
        if (!await HasAsync("Create")) return Forbid();
        var companyId = await ActiveCompanyIdAsync();
        if (companyId is null) return BadRequest(new { message = "شرکت فعال انتخاب نشده است." });
        await using var tx = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        if (string.IsNullOrWhiteSpace(dto.Title))
            return BadRequest(new { message = "عنوان دستور کار را وارد کنید." });
        if (dto.AssigneeUserIds.Count == 0)
            return BadRequest(new { message = "حداقل یک نفر را انتخاب کنید." });
        // بند ۱: تاریخ و ساعت مقرر نباید قبل از زمان ثبت باشد
        if (dto.DueAt <= DateTime.Now)
            return BadRequest(new { message = "تاریخ و ساعت مقرر نمی‌تواند قبل از زمان ثبت باشد." });
        if (!WorkOrderPriority.IsValid(dto.Priority))
            return BadRequest(new { message = "اولویت انتخابی نامعتبر است." });
        if (!WorkOrderRecurrence.IsValid(dto.Recurrence))
            return BadRequest(new { message = "الگوی تکرار نامعتبر است." });

        // بند ۵ و ۶: بدون مجوز «به دیگران»، فقط خودش | با مجوز، فقط لیست مجاز
        var others = dto.AssigneeUserIds.Where(id => id != MyUserId).Distinct().ToList();
        if (others.Count > 0)
        {
            if (!await HasAsync("AssignOthers"))
                return BadRequest(new { message = "شما فقط مجاز به ثبت دستور کار برای خودتان هستید." });
            if (!IsLegacyAdmin)
            {
                var allowed = await _db.WorkOrderAllowedAssignees.Where(a => a.OwnerUserId == MyUserId)
                    .Select(a => a.TargetUserId).ToListAsync();
                var illegal = others.Where(id => !allowed.Contains(id)).ToList();
                if (illegal.Count > 0)
                    return BadRequest(new { message = "برخی افراد انتخابی در لیست مجاز شما نیستند." });
            }
        }

        // ارجاع زنجیره‌ای — اعتبارسنجی والد: باید باز باشد و من گیرندهٔ آن باشم (یا دستوردهنده‌اش)
        WorkOrder? parent = null;
        if (dto.ParentOrderId is > 0)
        {
            parent = await _db.WorkOrders.FirstOrDefaultAsync(w => w.Id == dto.ParentOrderId);
            if (parent == null)
                return BadRequest(new { message = "دستور والد یافت نشد." });
            if (parent.Status != "Open")
                return BadRequest(new { message = "دستور والد بسته شده — ارجاع زنجیره‌ای ممکن نیست." });
            var amAssignee = await _db.WorkOrderAssignees.AnyAsync(a => a.OrderId == parent.Id && a.UserId == MyUserId);
            if (!amAssignee && parent.OwnerUserId != MyUserId)
                return BadRequest(new { message = "فقط گیرندگان یا دستوردهندهٔ دستور والد می‌توانند زیر-دستور بسازند." });
            // جلوگیری از عمق بی‌نهایت: حداکثر ۵ سطح زنجیره
            var depth = 0; var cur = parent;
            while (cur?.ParentOrderId is > 0 && depth < 6)
            {
                cur = await _db.WorkOrders.AsNoTracking().FirstOrDefaultAsync(w => w.Id == cur.ParentOrderId);
                depth++;
            }
            if (depth >= 5)
                return BadRequest(new { message = "حداکثر عمق زنجیرهٔ ارجاع (۵ سطح) پر شده است." });
            // مهلت زیر-دستور نباید بعد از مهلت والد باشد
            if (dto.DueAt > parent.DueAt)
                return BadRequest(new { message = $"مهلت زیر-دستور نمی‌تواند بعد از مهلت دستور والد ({ToFa(parent.DueAt)}) باشد." });
        }

        // اتصال به مبدأ (سورس) — اختیاری. اگر عنوان سورس داده شده باشد، همزمان نباید خالی/بی‌اعتبار باشد.
        var inheritedSource = false;
        if (!string.IsNullOrWhiteSpace(dto.SourceModule) && dto.SourceId is not null and > 0)
        {
            dto.SourceModule = dto.SourceModule.Trim();
            // اعتبارسنجی: ماژول سورس شناخته‌شده باشد (برای حفظ عمومی‌بودن، فقط طول/الگو بررسی می‌شود)
            if (dto.SourceModule.Length > 50)
                return BadRequest(new { message = "نام ماژول سورس نامعتبر است." });
        }
        else if (parent?.SourceId is > 0 && !string.IsNullOrWhiteSpace(parent.SourceModule))
        {
            // زیر-دستورِ یک دستورکارِ متصل به نامه، همان مبدأ را به ارث می‌برد؛ در غیر این صورت
            // گیرندگان زیر-دستور از فهرست دستور کار به نامه دسترسی نداشتند.
            dto.SourceModule = parent.SourceModule;
            dto.SourceId = parent.SourceId;
            dto.SourceRefId ??= parent.SourceRefId;
            inheritedSource = true;
            // پیوند والد از قبل مجاز بوده است؛ شرط «حاضر در گردش نامه» برای زیر-دستور اعمال نمی‌شود.
        }
        else
        {
            dto.SourceModule = null;
            dto.SourceId = null;
        }

        // اتصال به پروژه کاری (اختیاری) — پروژه باید باز و متعلق به شرکت فعال باشد
        WorkProject? project = null;
        if (dto.ProjectId is > 0)
        {
            project = await _db.WorkProjects.FirstOrDefaultAsync(p => p.Id == dto.ProjectId.Value);
            if (project is null || project.CompanyId != companyId.Value)
                return BadRequest(new { message = "پروژه انتخابی معتبر نیست." });
            if (project.Status != WorkProjectStatus.Open)
                return BadRequest(new { message = "فقط به پروژه باز می‌توان دستور کار وصل کرد." });
        }
        if (dto.Weight is < 0 or > 10)
            return BadRequest(new { message = "وزن دستور باید بین ۰ تا ۱۰ باشد." });

        var users = await _db.Users.Where(u => dto.AssigneeUserIds.Contains(u.Id) && u.IsActive).ToListAsync();
        if (users.Count != dto.AssigneeUserIds.Distinct().Count())
            return BadRequest(new { message = "برخی گیرندگان وجود ندارند یا غیرفعال‌اند." });
        var noName = users.Where(u => string.IsNullOrWhiteSpace(u.FirstName) || string.IsNullOrWhiteSpace(u.LastName))
            .Select(u => u.Username).ToList();
        if (noName.Count > 0)
            return BadRequest(new { message = $"این کاربران نام و نام خانوادگی ندارند: {string.Join("، ", noName)} — از بخش کاربران تکمیل کنید." });
        if (parent != null && WorkOrderSchedule.Dates(dto.DueAt, dto.Recurrence).Last() > parent.DueAt)
            return BadRequest(new { message = "پایان سری تکرار از مهلت دستور والد عبور می‌کند؛ دستور مستقل بسازید یا مهلت والد را اصلاح کنید." });

        // ⛔ ضدِ ثبت تکراری — دو کلیک پشت‌سرهم روی «ثبت دستور کار» نباید دو دستور کار بسازد.
        // کلید: ClientRequestId (اگر کلاینت فرستاده باشد) وگرنه اثرانگشت محتوای درخواست.
        // جای‌نگهدارِ خالی پیش از ساخت ثبت می‌شود؛ درخواست هم‌زمانِ دوم آن را می‌بیند،
        // منتظر می‌ماند تا شناسهٔ دستور کارِ در حال ساخت پر شود و همان را برمی‌گرداند.
        var idemKey = string.IsNullOrWhiteSpace(dto.ClientRequestId)
            ? $"fp:{MyUserId}:{CreateFingerprint(MyUserId, dto, companyId.Value)}"
            : $"cid:{MyUserId}:{dto.ClientRequestId!.Trim()}";

        if (!RecentCreates.TryAdd(idemKey, (0, "", DateTime.UtcNow)))
        {
            for (var i = 0; i < 25; i++)
            {
                if (RecentCreates.TryGetValue(idemKey, out var hit))
                {
                    if (hit.OrderId > 0 && DateTime.UtcNow - hit.At <= DuplicateWindow)
                        return Ok(new
                        {
                            id = hit.OrderId, number = hit.Number, occurrenceCount = 0,
                            sms = (WorkOrderSmsResult?)null, letterFlowAdded = 0, duplicate = true
                        });
                    if (DateTime.UtcNow - hit.At > DuplicateWindow) break;
                    // جای‌نگهدار بیش از ۶ ثانیه خالی مانده یعنی ثبتِ اول شکست خورده
                    // است؛ بیشتر از این منتظر نمان و اجازهٔ ثبت تازه بده.
                    if (hit.OrderId == 0 && DateTime.UtcNow - hit.At > TimeSpan.FromSeconds(6)) break;
                }
                else break;
                await Task.Delay(200);
            }
            // جای‌نگهدارِ معلق/منقضی — اجازهٔ عبور بده تا کاربر در بن‌بست نماند.
            RecentCreates.TryRemove(idemKey, out _);
            RecentCreates.TryAdd(idemKey, (0, "", DateTime.UtcNow));
        }

        var wo = new WorkOrder
        {
            // شرحِ بلند (مثلاً دستور کارِ ساخته‌شده از نامهٔ دارای تصویر) در جدول جانبی
            // ذخیره می‌شود و ستون اصلی فقط پیش‌نمایش کوتاه می‌گیرد.
            Title = dto.Title.Trim(), Description = WorkOrderDescriptionStore.Preview(dto.Description),
            CompanyId = companyId.Value,
            OwnerUserId = MyUserId, OwnerName = await MyDisplayNameAsync(), DueAt = dto.DueAt,
            Priority = dto.Priority, Recurrence = dto.Recurrence, SourceModule = dto.SourceModule,
            SourceId = dto.SourceId, SourceRefId = dto.SourceRefId, Tags = NormalizeTags(dto.Tags), ParentOrderId = parent?.Id,
            ProjectId = project?.Id, Weight = dto.Weight
        };
        _db.WorkOrders.Add(wo);
        await _db.SaveChangesAsync();
        // متن کامل شرح (با تصاویر نامه) در جدول جانبی — شناسه پس از SaveChanges معلوم است.
        await WorkOrderDescriptionStore.SaveAsync(_db, wo.Id, dto.Description);
        // The database identity prevents number reuse after a deletion and count-based races.
        wo.Number = $"WO/{new PersianCalendar().GetYear(wo.CreatedAt)}/{wo.Id}";
        foreach (var u in users)
            _db.WorkOrderAssignees.Add(new WorkOrderAssignee { OrderId = wo.Id, UserId = u.Id, Name = UserDisplay.Name(u) });
        var clOrder = 0;
        foreach (var item in dto.ChecklistItems.Where(t => !string.IsNullOrWhiteSpace(t)).Take(50))
            _db.WorkOrderChecklistItems.Add(new WorkOrderChecklistItem
            { OrderId = wo.Id, Text = item.Trim(), SortOrder = clOrder++ });
        Log(wo.Id, "Created", $"دستور کار {wo.Number} «{wo.Title}» — مهلت: {ToFa(wo.DueAt)} — گیرندگان: {string.Join("، ", users.Select(UserDisplay.Name))}");
        if (parent != null)
        {
            Log(wo.Id, "Chained", $"زیر-دستورِ {parent.Number} «{parent.Title}»");
            Log(parent.Id, "Chained", $"زیر-دستور {wo.Number} «{wo.Title}» توسط {wo.OwnerName} ساخته شد");
        }
        if (project != null)
            _db.WorkProjectLogs.Add(new WorkProjectLog { ProjectId = project.Id, ActorName = MyUsername, Action = "OrderAdded", Text = $"دستور {wo.Number} «{wo.Title}» به پروژه وصل شد." });
        // اگر دستورکار از روی نامه ساخته شده باشد، افراد جدید باید در گردش واقعی
        // همان نامه نیز قرار بگیرند؛ صرفاً لینک‌کردن دستورکار به نامه کافی نیست.
        var letterFlowAdded = await AddNewLetterWorkOrderAssigneesToFlowAsync(dto, wo, requireRequestorInFlow: !inheritedSource);
        await _db.SaveChangesAsync();
        var added = await new WorkOrderSchedulingService(_db).MaterializeAsync(wo);
        await tx.CommitAsync();
        // ثبتِ موفق در حافظهٔ کوتاه‌مدت ذخیره می‌شود تا ارسال دوبارهٔ همان درخواست
        // (کلیک دوم، تلاش مجدد مرورگر) دستور کار تکراری نسازد.
        RememberCreate(idemKey, wo.Id, wo.Number);

        // One summary per recipient, not dozens of future-occurrence notifications.
        if (parent != null && parent.OwnerUserId != MyUserId)
            await _notify.SendAsync(parent.OwnerUserId, "ارجاع زنجیره‌ای 🔗",
                $"{wo.OwnerName} بخشی از {parent.Number} را به‌صورت زیر-دستور {wo.Number} ارجاع داد.",
                wo.OwnerName, "دستور کار", $"/work-orders?open={wo.Id}");
        var prTag = wo.Priority >= WorkOrderPriority.High ? $" — اولویت: {WorkOrderPriority.ToFa(wo.Priority)}" : "";
        var scheduleTag = added > 0 ? $" — {added + 1} نوبت در تقویم شما ثبت شد" : "";
        foreach (var uid in users.Select(u => u.Id).Where(id => id != MyUserId))
            await _notify.SendAsync(uid, wo.Priority == WorkOrderPriority.Urgent ? "دستور کار فوری 🔴" : "دستور کار جدید 📋",
                $"{wo.Number} — «{wo.Title}» — مهلت: {ToFa(wo.DueAt)}{prTag}{scheduleTag}",
                wo.OwnerName, "دستور کار", $"/work-orders?open={wo.Id}");

        // The order is committed before calling the external gateway. SMS failures are returned as
        // a per-recipient result and must not make the client retry work-order creation.
        WorkOrderSmsResult? smsResult = dto.SendSms
            ? await _sms.SendNewWorkOrderAsync(wo, users)
            : null;

        await _notify.BroadcastChangedAsync("workorders");
        // ردیف‌های ارجاعِ تازه، کارتابل و شمارندهٔ نخواندهٔ نامه‌ها را تغییر می‌دهند
        if (letterFlowAdded > 0)
        {
            await _notify.BroadcastChangedAsync("letters");
            await _notify.BroadcastChangedAsync("incoming-letters");
            await _notify.BroadcastChangedAsync("outgoing-letters");
        }
        if (project != null) await _notify.BroadcastChangedAsync("workprojects");
        return Ok(new { id = wo.Id, number = wo.Number, occurrenceCount = added + 1, sms = smsResult, letterFlowAdded, duplicate = false });
    }

    /// <summary>ویرایش دستور کار باز توسط دستوردهنده — عنوان، شرح، مهلت و گیرندگان.</summary>
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] CreateDto dto)
    {
        if (!await HasAsync("Create")) return Forbid();
        await using var tx = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var wo = await _db.WorkOrders.FirstOrDefaultAsync(w => w.Id == id);
        if (wo == null) return NotFound(new { message = "دستور کار پیدا نشد." });
        if (wo.OwnerUserId != MyUserId) return Forbid();
        if (wo.Status == "Closed")
            return BadRequest(new { message = "دستور کار بسته شده و قابل ویرایش نیست." });
        // بعد از اولین پاسخ، محتوای دستور برای دستوردهنده نیز قفل می‌شود.
        if (await _db.WorkOrderAssignees.AnyAsync(a => a.OrderId == id && (a.SeenAt != null || a.RepliedAt != null)))
            return BadRequest(new { message = "یکی از گیرندگان دستور کار را رویت یا پاسخ داده است؛ این دستور کار دیگر قابل ویرایش نیست." });
        if (string.IsNullOrWhiteSpace(dto.Title))
            return BadRequest(new { message = "عنوان دستور کار را وارد کنید." });
        if (dto.AssigneeUserIds.Count == 0)
            return BadRequest(new { message = "حداقل یک نفر را انتخاب کنید." });
        if (dto.DueAt <= DateTime.Now)
            return BadRequest(new { message = "تاریخ و ساعت مقرر نمی‌تواند قبل از زمان فعلی باشد." });
        if (!WorkOrderPriority.IsValid(dto.Priority))
            return BadRequest(new { message = "اولویت انتخابی نامعتبر است." });
        if (!WorkOrderRecurrence.IsValid(dto.Recurrence))
            return BadRequest(new { message = "الگوی تکرار نامعتبر است." });

        // اتصال به پروژه کاری — اگر عوض شده، پروژه جدید باید باز و هم‌شرکت دستور باشد
        WorkProject? newProject = null;
        if (dto.ProjectId is > 0)
        {
            newProject = await _db.WorkProjects.FirstOrDefaultAsync(p => p.Id == dto.ProjectId.Value);
            if (newProject is null || (wo.CompanyId != null && newProject.CompanyId != wo.CompanyId.Value))
                return BadRequest(new { message = "پروژه انتخابی معتبر نیست." });
            if (newProject.Id != wo.ProjectId && newProject.Status != WorkProjectStatus.Open)
                return BadRequest(new { message = "فقط به پروژه باز می‌توان دستور کار وصل کرد." });
        }
        if (dto.Weight is < 0 or > 10)
            return BadRequest(new { message = "وزن دستور باید بین ۰ تا ۱۰ باشد." });

        if (wo.RecurrenceSeriesId != null && dto.Recurrence != wo.Recurrence)
            return BadRequest(new { message = "الگوی سری ثبت‌شده قابل تغییر از ویرایش یک نوبت نیست؛ ویرایش فقط روی همین نوبت اعمال می‌شود." });
        if (wo.ParentOrderId.HasValue)
        {
            var parent = await _db.WorkOrders.FirstOrDefaultAsync(w => w.Id == wo.ParentOrderId);
            var last = wo.RecurrenceSeriesId == null ? WorkOrderSchedule.Dates(dto.DueAt, dto.Recurrence).Last() : dto.DueAt;
            if (parent == null || last > parent.DueAt)
                return BadRequest(new { message = "مهلت نوبت یا سری از مهلت دستور والد عبور می‌کند." });
        }

        var others = dto.AssigneeUserIds.Where(x => x != MyUserId).Distinct().ToList();
        if (others.Count > 0)
        {
            if (!await HasAsync("AssignOthers"))
                return BadRequest(new { message = "شما فقط مجاز به ثبت دستور کار برای خودتان هستید." });
            if (!IsLegacyAdmin)
            {
                var allowed = await _db.WorkOrderAllowedAssignees.Where(a => a.OwnerUserId == MyUserId)
                    .Select(a => a.TargetUserId).ToListAsync();
                if (others.Any(x => !allowed.Contains(x)))
                    return BadRequest(new { message = "برخی افراد انتخابی در لیست مجاز شما نیستند." });
            }
        }

        var users = await _db.Users.Where(u => dto.AssigneeUserIds.Contains(u.Id) && u.IsActive).ToListAsync();
        if (users.Count != dto.AssigneeUserIds.Distinct().Count())
            return BadRequest(new { message = "برخی گیرندگان وجود ندارند یا غیرفعال شده‌اند؛ فقط کاربران فعال را انتخاب کنید." });
        var noName = users.Where(u => string.IsNullOrWhiteSpace(u.FirstName) || string.IsNullOrWhiteSpace(u.LastName))
            .Select(u => u.Username).ToList();
        if (noName.Count > 0)
            return BadRequest(new { message = $"این کاربران نام و نام خانوادگی ندارند: {string.Join("، ", noName)} — از بخش کاربران تکمیل کنید." });

        var existing = await _db.WorkOrderAssignees.Where(a => a.OrderId == id).ToListAsync();
        var keep = dto.AssigneeUserIds.Distinct().ToHashSet();
        var removedUserIds = existing.Where(a => !keep.Contains(a.UserId)).Select(a => a.UserId).ToList();
        var addedUserIds = keep.Where(uid => !existing.Any(a => a.UserId == uid)).ToList();
        foreach (var a in existing.Where(a => !keep.Contains(a.UserId)).ToList())
        {
            if (a.RepliedAt != null)
                return BadRequest(new { message = $"نمی‌توان «{a.Name}» را حذف کرد چون پاسخ ثبت کرده است." });
            _db.WorkOrderAssignees.Remove(a);
        }
        foreach (var uid in addedUserIds)
        {
            var u = users.FirstOrDefault(x => x.Id == uid);
            if (u == null) continue;
            _db.WorkOrderAssignees.Add(new WorkOrderAssignee { OrderId = wo.Id, UserId = u.Id, Name = UserDisplay.Name(u) });
        }

        wo.Title = dto.Title.Trim();
        wo.Description = WorkOrderDescriptionStore.Preview(dto.Description);
        // متن کامل (تصاویر نامه) هم به‌روزرسانی می‌شود؛ اگر شرح کوتاه شد، ردیف جانبی حذف می‌شود.
        await WorkOrderDescriptionStore.SaveAsync(_db, wo.Id, dto.Description);
        wo.DueAt = dto.DueAt;
        wo.Priority = dto.Priority;
        wo.Recurrence = dto.Recurrence;
        wo.Tags = NormalizeTags(dto.Tags);
        wo.OwnerName = await MyDisplayNameAsync();

        // گیرندگان تازهٔ دستورکارِ متصل به نامه باید در گردش واقعی همان نامه هم قرار بگیرند؛
        // وگرنه از فهرست دستور کار روی لینک نامه ۴۰۳ می‌گیرند. (ویرایش فقط پیش از رویت ممکن است)
        var letterFlowAdded = 0;
        if (addedUserIds.Count > 0)
            letterFlowAdded = await SyncOrderAssigneesToLetterFlowAsync(wo, addedUserIds);

        var oldProjectId = wo.ProjectId;
        wo.ProjectId = newProject?.Id;
        wo.Weight = dto.Weight;

        // همگام‌سازی چک‌لیست: آیتم‌های تیک‌خورده حفظ می‌شوند (تطبیق بر اساس متن)، بقیه بازنویسی
        var oldItems = await _db.WorkOrderChecklistItems.Where(c => c.OrderId == id).ToListAsync();
        var newTexts = dto.ChecklistItems.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).Take(50).ToList();
        _db.WorkOrderChecklistItems.RemoveRange(oldItems.Where(o => !newTexts.Contains(o.Text)));
        var clSort = 0;
        foreach (var text in newTexts)
        {
            var existing2 = oldItems.FirstOrDefault(o => o.Text == text);
            if (existing2 != null) existing2.SortOrder = clSort++;
            else _db.WorkOrderChecklistItems.Add(new WorkOrderChecklistItem { OrderId = id, Text = text, SortOrder = clSort++ });
        }

        var projectNote = "";
        if (oldProjectId != wo.ProjectId)
        {
            if (oldProjectId is > 0)
                _db.WorkProjectLogs.Add(new WorkProjectLog { ProjectId = oldProjectId.Value, ActorName = MyUsername, Action = "OrderRemoved", Text = $"دستور {wo.Number} «{wo.Title}» از پروژه جدا شد." });
            if (wo.ProjectId is > 0)
            {
                _db.WorkProjectLogs.Add(new WorkProjectLog { ProjectId = wo.ProjectId.Value, ActorName = MyUsername, Action = "OrderAdded", Text = $"دستور {wo.Number} «{wo.Title}» به پروژه وصل شد." });
                projectNote = $" — پروژه: {newProject!.Number}";
            }
            else projectNote = " — جدا شدن از پروژه";
        }
        Log(wo.Id, "Edited", $"ویرایش دستور کار — مهلت: {ToFa(wo.DueAt)} — گیرندگان: {string.Join("، ", users.Select(UserDisplay.Name))}{projectNote}");
        await _db.SaveChangesAsync();
        foreach (var uid in addedUserIds)
            await _notify.SendAsync(uid, "به دستور کار اضافه شدید", $"شما به دستور کار «{wo.Title}» ({wo.Number}) اضافه شدید.", wo.OwnerName, "دستور کار", $"/work-orders?open={wo.Id}");
        foreach (var uid in removedUserIds)
            await _notify.SendAsync(uid, "از دستور کار حذف شدید", $"شما از دستور کار «{wo.Title}» ({wo.Number}) حذف شدید.", wo.OwnerName, "دستور کار", $"/work-orders?open={wo.Id}");
        var added = await new WorkOrderSchedulingService(_db).MaterializeAsync(wo);
        await tx.CommitAsync();
        await _notify.BroadcastChangedAsync("workorders");
        if (letterFlowAdded > 0)
        {
            await _notify.BroadcastChangedAsync("letters");
            await _notify.BroadcastChangedAsync("incoming-letters");
            await _notify.BroadcastChangedAsync("outgoing-letters");
        }
        if (oldProjectId != wo.ProjectId) await _notify.BroadcastChangedAsync("workprojects");
        return Ok(new { id = wo.Id, number = wo.Number, occurrenceCount = added + 1, letterFlowAdded });
    }

    private static string ToFa(DateTime d)
    {
        var pc = new PersianCalendar();
        return $"{pc.GetYear(d)}/{pc.GetMonth(d):00}/{pc.GetDayOfMonth(d):00} {d:HH:mm}";
    }

    // ================== لیست‌ها ==================

    /// <summary>پارامترهای فیلتر لیست — همه اختیاری؛ بدون پارامتر، رفتار قبلی حفظ می‌شود.</summary>
    public class ListFilterDto
    {
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 10;
        /// <summary>جستجو در شماره و عنوان (بدون حساسیت به بزرگی/کوچکی).</summary>
        public string? Q { get; set; }

        /// <summary>فیلتر اولویت: 0..3 — null یعنی همه.</summary>
        public int? Priority { get; set; }

        /// <summary>بازه مهلت — از (شامل).</summary>
        public DateTime? DueFrom { get; set; }

        /// <summary>بازه مهلت — تا (غیرشامل؛ برای «تا امروز» مقدار فردا را بفرستید).</summary>
        public DateTime? DueTo { get; set; }

        /// <summary>فیلتر شخص: دستوردهنده یا یکی از گیرندگان.</summary>
        public int? UserId { get; set; }

        /// <summary>فیلتر برچسب — تطبیق دقیق یک برچسب.</summary>
        public string? Tag { get; set; }

        /// <summary>فیلتر پروژه کاری — فقط دستورهای متصل به این پروژه.</summary>
        public int? ProjectId { get; set; }

        /// <summary>نوع ماژول مبدأ: InnerLetter، IncomingLetter، OutgoingLetter یا سایر ماژول‌ها.</summary>
        public string? SourceModule { get; set; }

        /// <summary>نوع نامه مبدأ: 1=داخلی، 2=صادره، 3=وارده.</summary>
        public int? SourceType { get; set; }

        /// <summary>
        /// فیلتر تصمیم دستوردهنده روی پاسخ گیرندگان:
        /// approved = حداقل یک تأیید، rejected = حداقل یک رد،
        /// undecided = پاسخ داده شده ولی هنوز تصمیمی ثبت نشده،
        /// awaiting = گیرنده دارد ولی هیچ‌کس هنوز پاسخی نداده است.
        /// </summary>
        public string? Decision { get; set; }
    }

    /// <summary>
    /// نرمال‌سازی عبارت جستجو: ارقام فارسی/عربی → لاتین، ي/ك عربی → ی/ک فارسی،
    /// یکسان‌سازی نیم‌فاصله، حذف کاراکترهای ویژهٔ LIKE (% و _) و فشرده‌کردن فاصله‌ها.
    /// <para>
    /// چرا: کاربر شمارهٔ «۱۲۳» یا «علي» را با حروف عربی تایپ می‌کند؛ بدون این نرمال‌سازی
    /// هیچ نتیجه‌ای نمی‌گرفت و فیلتر «کار نمی‌کرد». و اگر % تایپ می‌شد، LIKE همهٔ ردیف‌ها
    /// را برمی‌گرداند («اطلاعات اشتباه»).
    /// </para>
    /// </summary>
    private static string NormalizeTerm(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var t = Inventory.Shared.Fa.ToEn(text)
            .Replace('\u064A', '\u06CC')   // ي عربی → ی فارسی
            .Replace('\u0643', '\u06A9')   // ك عربی → ک فارسی
            .Replace('\u200C', ' ')         // نیم‌فاصله → فاصله
            .Replace("%", "").Replace("_", "");
        return string.Join(' ', t.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>اعمال فیلترهای مشترک روی کوئری — سمت دیتابیس تا حد ممکن.</summary>
    private IQueryable<WorkOrder> ApplyFilter(IQueryable<WorkOrder> q, ListFilterDto f, bool myAssignmentOnly = false)
    {
        if (!string.IsNullOrWhiteSpace(f.Q))
        {
            // جستجو در شماره، عنوان، نام دستوردهنده، نام گیرندگان و برچسب‌ها — همان چیزی که
            // placeholder نوار جستجو وعده می‌دهد («شماره، عنوان یا دستوردهنده»).
            var term = NormalizeTerm(f.Q);
            if (term.Length > 0)
            {
                var pat = $"%{term}%";
                q = q.Where(w => EF.Functions.Like(w.Title, pat)
                              || EF.Functions.Like(w.Number, pat)
                              || EF.Functions.Like(w.OwnerName, pat)
                              || (w.Tags != null && EF.Functions.Like(w.Tags, pat))
                              || _db.WorkOrderAssignees.Any(a => a.OrderId == w.Id && EF.Functions.Like(a.Name, pat)));
            }
        }
        if (f.Priority is >= WorkOrderPriority.Low and <= WorkOrderPriority.Urgent)
            q = q.Where(w => w.Priority == f.Priority);
        if (f.DueFrom != null) q = q.Where(w => w.DueAt >= f.DueFrom);
        if (f.DueTo != null) q = q.Where(w => w.DueAt < f.DueTo);
        if (!string.IsNullOrWhiteSpace(f.Tag))
        {
            // برچسب‌ها به شکل ",برق,کارگاه," ذخیره می‌شوند → تطبیق دقیق با LIKE '%,برق,%'
            // نرمال‌سازی لازم است، وگرنه برچسبی که با «ك/ي» عربی یا فاصلهٔ اضافه ذخیره شده
            // با چیپ فیلتر (که با «ک/ی» فارسی نمایش داده می‌شود) هیچ‌وقت تطبیق نمی‌خورد و
            // فیلتر «کار نمی‌کند». هر دو حالت (نرمال‌شده و خام) بررسی می‌شود.
            var raw = f.Tag.Trim().Replace(",", "،");
            var norm = NormalizeTerm(raw);
            var t1 = $"%,{raw},%";
            var t2 = $"%,{norm},%";
            q = q.Where(w => w.Tags != null
                && (EF.Functions.Like(w.Tags, t1) || EF.Functions.Like(w.Tags, t2)
                    || EF.Functions.Like(w.Tags, $"%{raw}%")));
        }
        if (f.UserId is > 0)
        {
            var uid = f.UserId.Value;
            var uidOrders = _db.WorkOrderAssignees.Where(a => a.UserId == uid).Select(a => a.OrderId);
            q = q.Where(w => w.OwnerUserId == uid || uidOrders.Contains(w.Id));
        }
        if (f.ProjectId is > 0)
            q = q.Where(w => w.ProjectId == f.ProjectId);
        // فیلتر «تصمیم دستوردهنده» — تصمیم روی پاسخ هر گیرنده ثبت می‌شود.
        if (!string.IsNullOrWhiteSpace(f.Decision))
        {
            var dec = f.Decision.Trim().ToLowerInvariant();
            switch (dec)
            {
                // در تب «محول به من» ملاک، ردیفِ خودِ کاربر است: «در انتظار پاسخ» یعنی
                // *من* پاسخ نداده‌ام (نه اینکه هیچ‌کس پاسخ نداده باشد)؛ وگرنه وقتی یکی از
                // همکاران پاسخ می‌داد، دستورِ بی‌پاسخِ من از فیلتر بیرون می‌افتاد و فیلتر
                // خالی به‌نظر می‌رسید. در تب «دستورهای من» (دستوردهنده) ملاک همهٔ گیرندگان است.
                case "approved":
                    if (myAssignmentOnly)
                        q = q.Where(w => _db.WorkOrderAssignees.Any(a => a.OrderId == w.Id
                            && a.UserId == MyUserId && a.OwnerDecision == "Approved"));
                    else
                        q = q.Where(w => _db.WorkOrderAssignees
                            .Any(a => a.OrderId == w.Id && a.OwnerDecision == "Approved"));
                    break;
                case "rejected":
                    if (myAssignmentOnly)
                        q = q.Where(w => _db.WorkOrderAssignees.Any(a => a.OrderId == w.Id
                            && a.UserId == MyUserId && a.OwnerDecision == "Rejected"));
                    else
                        q = q.Where(w => _db.WorkOrderAssignees
                            .Any(a => a.OrderId == w.Id && a.OwnerDecision == "Rejected"));
                    break;
                case "undecided":
                    // پاسخ داده شده ولی دستوردهنده هنوز تأیید/رد نکرده است
                    if (myAssignmentOnly)
                        q = q.Where(w => _db.WorkOrderAssignees.Any(a => a.OrderId == w.Id
                            && a.UserId == MyUserId && a.RepliedAt != null
                            && a.OwnerDecision != "Approved" && a.OwnerDecision != "Rejected"));
                    else
                        q = q.Where(w => _db.WorkOrderAssignees
                            .Any(a => a.OrderId == w.Id && a.RepliedAt != null
                                      && (a.OwnerDecision == null
                                          || (a.OwnerDecision != "Approved" && a.OwnerDecision != "Rejected"))));
                    break;
                case "awaiting":
                    // «در انتظار پاسخ» = گیرنده دارد و پاسخ ثبت نشده است.
                    // در تب «محول به من»: پاسخِ خودِ من ثبت نشده باشد.
                    if (myAssignmentOnly)
                        q = q.Where(w => _db.WorkOrderAssignees.Any(a => a.OrderId == w.Id
                                              && a.UserId == MyUserId)
                                          && !_db.WorkOrderAssignees.Any(a => a.OrderId == w.Id
                                              && a.UserId == MyUserId && a.RepliedAt != null));
                    else
                        q = q.Where(w => _db.WorkOrderAssignees.Any(a => a.OrderId == w.Id)
                                      && !_db.WorkOrderAssignees.Any(a => a.OrderId == w.Id && a.RepliedAt != null));
                    break;
            }
        }
        // نوع نامه از LetterSource خوانده می‌شود، نه از نام رشته‌ای SourceModule.
        // این کار داده‌های قدیمی و دستورکارهایی را که با نام ماژول متفاوت ذخیره شده‌اند
        // نیز درست دسته‌بندی می‌کند.
        if (f.SourceType is >= 1 and <= 3)
        {
            var letterType = f.SourceType.Value;
            var moduleNames = letterType switch
            {
                1 => new[] { "InnerLetter", "InnerLetters" },
                2 => new[] { "OutgoingLetter", "OutgoingLetters" },
                3 => new[] { "IncomingLetter", "IncomingLetters" },
                _ => Array.Empty<string>()
            };
            q = q.Where(w => moduleNames.Contains(w.SourceModule!) ||
                (w.SourceId != null && _db.LetterSources
                    .Any(s => s.Id == w.SourceId.Value && !s.IsDelete && s.SourceType == letterType)));
        }
        else if (!string.IsNullOrWhiteSpace(f.SourceModule))
        {
            var source = f.SourceModule.Trim();
            q = source.Equals("letters", StringComparison.OrdinalIgnoreCase)
                ? q.Where(w => w.SourceId != null && _db.LetterSources
                    .Any(s => s.Id == w.SourceId.Value && !s.IsDelete && s.SourceType >= 1 && s.SourceType <= 3))
                : q.Where(w => w.SourceModule == source);
        }
        return q;
    }

    // ==================== نوعِ نامهٔ مبدأ ====================
    // منبعِ حقیقت، جدولِ LetterSources است: هر نامه (داخلی/صادره/وارده) یک ردیف
    // در آن جدول دارد که SourceType آن (۱=داخلی، ۲=صادره، ۳=وارده) نوعِ واقعیِ نامه
    // را مشخص می‌کند و Id آن کلیدِ مشترک با هر سه نوع نامه است. رشتهٔ
    // WorkOrder.SourceModule یک متنِ آزاد است و ممکن است در داده‌های قدیمی اشتباه،
    // قدیمی یا خالی باشد — به همین دلیل هرگز مبنای تصمیم دربارهٔ نوعِ نامه نیست.

    /// <summary>نگاشتِ شناسهٔ مبدأ → نوعِ نامه (۱=داخلی، ۲=صادره، ۳=وارده)</summary>
    private async Task<Dictionary<int, int>> LetterTypeMapAsync(IEnumerable<int?> sourceIds)
    {
        var ids = sourceIds.Where(x => x is > 0).Select(x => x!.Value).Distinct().ToList();
        if (ids.Count == 0) return new();
        return await _db.LetterSources.AsNoTracking()
            .Where(s => ids.Contains(s.Id) && !s.IsDelete && s.SourceType >= 1 && s.SourceType <= 3)
            .ToDictionaryAsync(s => s.Id, s => s.SourceType);
    }

    /// <summary>
    /// نوعِ نامهٔ مبدأ را با اولویتِ جدولِ LetterSources و سپس نامِ ماژول برمی‌گرداند.
    /// اگر مبدأ صریحاً یک ماژولِ غیرنامه (مدرک/صورتجلسه) باشد، null برمی‌گردد تا
    /// لینکِ «نامه» اشتباه ساخته نشود.
    /// </summary>
    private static int? LetterTypeOf(string? module, int? sourceId, Dictionary<int, int> map)
    {
        var byModule = ModuleToLetterType(module);
        if (!IsLetterModuleOrUnknown(module)) return null;
        if (sourceId is > 0 && map.TryGetValue(sourceId.Value, out var st)) return st;
        return byModule;
    }

    /// <summary>نگاشتِ نامِ ماژول → نوعِ نامه (۱=داخلی، ۲=صادره، ۳=وارده)</summary>
    private static int? ModuleToLetterType(string? module)
    {
        if (string.IsNullOrWhiteSpace(module)) return null;
        var m = module.Trim().ToLowerInvariant();
        if (m is "innerletter" or "innerletters") return 1;
        if (m is "outgoingletter" or "outgoingletters") return 2;
        if (m is "incomingletter" or "incomingletters") return 3;
        return null;
    }

    /// <summary>
    /// آیا مبدأ می‌تواند نامه باشد؟ ماژولِ خالی (داده‌های قدیمی) «نامعلوم» است و
    /// اجازه می‌دهد نوعِ نامه از LetterSource خوانده شود؛ اما ماژولِ صریحِ غیرنامه
    /// (مدرک/صورتجلسه) رد می‌شود تا شناسهٔ آن با شناسهٔ یک نامه اشتباه گرفته نشود.
    /// </summary>
    private static bool IsLetterModuleOrUnknown(string? module)
        => string.IsNullOrWhiteSpace(module) || ModuleToLetterType(module) is not null;

    /// <summary>نامِ ماژولِ استانداردِ متناظر با نوعِ نامه</summary>
    private static string? LetterModuleName(int sourceType) => sourceType switch
    {
        1 => "InnerLetter",
        2 => "OutgoingLetter",
        3 => "IncomingLetter",
        _ => null
    };

    private async Task<List<object>> BuildList(IQueryable<WorkOrder> q)
    {
        // مرتب‌سازی: اول اولویت (فوری بالاتر)، بعد جدیدترین
        var orders = await q.OrderByDescending(w => w.Priority).ThenByDescending(w => w.Id).ToListAsync();
        var ids = orders.Select(w => w.Id).ToList();
        var asgs = await _db.WorkOrderAssignees.Where(a => ids.Contains(a.OrderId)).ToListAsync();
        var attCounts = await _db.AppAttachments.Where(a => a.Module == "WorkOrders" && ids.Contains(a.RefId))
            .GroupBy(a => a.RefId).Select(g => new { g.Key, C = g.Count() }).ToListAsync();
        var clStats = await _db.WorkOrderChecklistItems.Where(c => ids.Contains(c.OrderId))
            .GroupBy(c => c.OrderId)
            .Select(g => new { g.Key, Total = g.Count(), Done = g.Count(x => x.IsDone) }).ToListAsync();
        var cmCounts = await _db.WorkOrderComments.Where(c => ids.Contains(c.OrderId) && !c.IsDeleted)
            .GroupBy(c => c.OrderId).Select(g => new { g.Key, C = g.Count() }).ToListAsync();
        // دستور کارهایی که شرحشان «بلند» است و متن کامل در جدول جانبی نگهداری می‌شود
        // (مثل دستور کارِ ساخته‌شده از نامهٔ دارای تصویر). فهرست فقط همین علامت را
        // می‌گیرد و متن کامل هنگام باز کردن جزئیات جداگانه خوانده می‌شود.
        var longDescIds = await WorkOrderDescriptionStore.LongIdsAsync(_db, ids);

        // ارجاع زنجیره‌ای: شماره/عنوان والدها + خلاصهٔ زیر-دستورها
        var parentIds = orders.Where(w => w.ParentOrderId is > 0).Select(w => w.ParentOrderId!.Value).Distinct().ToList();
        var parents = await _db.WorkOrders.AsNoTracking().Where(w => parentIds.Contains(w.Id))
            .Select(w => new { w.Id, w.Number, w.Title }).ToListAsync();
        var children = await _db.WorkOrders.AsNoTracking()
            .Where(w => w.ParentOrderId != null && ids.Contains(w.ParentOrderId.Value))
            .Select(w => new { w.Id, ParentId = w.ParentOrderId!.Value, w.Number, w.Title, w.Status, w.OwnerName })
            .ToListAsync();

        // پروژه کاری متصل — پروژه حذف‌شده هم با پسوند «(حذف‌شده)» نمایش داده می‌شود تا تاریخچه حفظ شود
        var projIds = orders.Where(w => w.ProjectId is > 0).Select(w => w.ProjectId!.Value).Distinct().ToList();
        var projDict = new Dictionary<int, (string Number, string Title, bool Deleted)>();
        if (projIds.Count > 0)
        {
            var prows = await _db.WorkProjects.IgnoreQueryFilters().AsNoTracking()
                .Where(p => projIds.Contains(p.Id))
                .Select(p => new { p.Id, p.Number, p.Title, p.DeletedAt }).ToListAsync();
            projDict = prows.ToDictionary(p => p.Id, p => (p.Number, p.Title, p.DeletedAt != null));
        }
        string? ProjCode(int? id) => id != null && projDict.TryGetValue(id.Value, out var a) ? a.Number : null;
        string? ProjName(int? id) => id != null && projDict.TryGetValue(id.Value, out var b)
            ? (b.Deleted ? b.Title + " (حذف‌شده)" : b.Title) : null;

        // نوعِ نامهٔ مبدأ — از جدولِ LetterSources خوانده می‌شود (مرجعِ اصلی) و نه از
        // رشتهٔ SourceModule. نامِ ماژول یک متنِ آزاد است و در داده‌های قدیمی می‌تواند
        // اشتباه یا خالی باشد؛ اما SourceType همیشه نوعِ واقعیِ نامه را می‌گوید
        // (۱=داخلی، ۲=صادره، ۳=وارده) تا لینکِ «نامه» هرگز نامهٔ اشتباهی را باز نکند.
        var letterTypeBySourceId = await LetterTypeMapAsync(orders.Select(w => w.SourceId));

        return orders.Select(w => (object)new
        {
            w.Id, w.Number, w.Title, w.Description, w.OwnerUserId, w.OwnerName,
            HasFullDescription = longDescIds.Contains(w.Id),
            w.DueAt, w.Status, w.CloseNote, w.ClosedAt, w.ExtensionCount, w.CreatedAt,
            w.Priority, w.Recurrence, w.RecurrenceSeriesId, w.RecurrenceScheduledAt,
            w.SourceModule, w.SourceId, w.SourceRefId,
            // نوعِ نامهٔ مبدأ (۱=داخلی، ۲=صادره، ۳=وارده) — از LetterSource خوانده شده
            SourceType = LetterTypeOf(w.SourceModule, w.SourceId, letterTypeBySourceId),
            w.ProjectId, w.Weight,
            ProjectCode = ProjCode(w.ProjectId),
            ProjectName = ProjName(w.ProjectId),
            OrderProgress = WorkProjectProgress.OrderProgress(w.Status == "Closed",
                clStats.FirstOrDefault(c => c.Key == w.Id)?.Total ?? 0,
                clStats.FirstOrDefault(c => c.Key == w.Id)?.Done ?? 0),
            Tags = TagsToList(w.Tags),
            ChecklistTotal = clStats.FirstOrDefault(c => c.Key == w.Id)?.Total ?? 0,
            ChecklistDone = clStats.FirstOrDefault(c => c.Key == w.Id)?.Done ?? 0,
            AttachmentCount = attCounts.FirstOrDefault(c => c.Key == w.Id)?.C ?? 0,
            CommentCount = cmCounts.FirstOrDefault(c => c.Key == w.Id)?.C ?? 0,
            w.ParentOrderId,
            ParentNumber = parents.FirstOrDefault(p => p.Id == w.ParentOrderId)?.Number,
            ParentTitle = parents.FirstOrDefault(p => p.Id == w.ParentOrderId)?.Title,
            Children = children.Where(c => c.ParentId == w.Id)
                .Select(c => new { c.Id, c.Number, c.Title, c.Status, c.OwnerName }).ToList(),
            Assignees = asgs.Where(a => a.OrderId == w.Id).Select(a => new
            {
                a.Id, a.UserId, a.Name, a.SeenAt, a.RepliedAt, a.Done, a.ReplyText,
                a.OwnerDecision, a.OwnerDecisionNote
            }).ToList()
        }).ToList();
    }

    /// <summary>دستورهایی که من داده‌ام (باز) — با فیلتر اختیاری جستجو/اولویت/بازه/شخص.</summary>
    public class WorkOrderPageDto
    {
        public List<object> Items { get; set; } = new();
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
    }

    private async Task<IActionResult> BuildPage(IQueryable<WorkOrder> query, ListFilterDto filter, int companyId,
        bool myAssignmentOnly = false)
    {
        query = query.Where(w => w.CompanyId == companyId);
        var page = Math.Max(1, filter.Page);
        var pageSize = Math.Clamp(filter.PageSize <= 0 ? 10 : filter.PageSize, 1, 100);
        var filtered = ApplyFilter(query, filter, myAssignmentOnly);
        var total = await filtered.CountAsync();
        var rows = await BuildList(filtered.OrderByDescending(w => w.Priority).ThenByDescending(w => w.Id)
            .Skip((page - 1) * pageSize).Take(pageSize));
        return Ok(new WorkOrderPageDto { Items = rows, TotalCount = total, Page = page, PageSize = pageSize });
    }

    [HttpGet("mine")]
    public async Task<IActionResult> Mine([FromQuery] ListFilterDto filter, [FromQuery] int? skip = null, [FromQuery] int? take = null)
    {
        filter.Page = Paging.ToPage(skip, take, filter.Page);
        filter.PageSize = Paging.ToPageSize(take, filter.PageSize);
        var companyId = await ActiveCompanyIdAsync();
        if (companyId is null) return BadRequest(new { message = "شرکت فعال انتخاب نشده است." });
        return await BuildPage(_db.WorkOrders.Where(w => w.OwnerUserId == MyUserId && w.Status == "Open"), filter, companyId.Value);
    }

    /// <summary>دستورهای محول به من (باز) — با فیلتر اختیاری.</summary>
    [HttpGet("assigned")]
    public async Task<IActionResult> Assigned([FromQuery] ListFilterDto filter, [FromQuery] int? skip = null, [FromQuery] int? take = null)
    {
        filter.Page = Paging.ToPage(skip, take, filter.Page);
        filter.PageSize = Paging.ToPageSize(take, filter.PageSize);
        var companyId = await ActiveCompanyIdAsync();
        if (companyId is null) return BadRequest(new { message = "شرکت فعال انتخاب نشده است." });
        var myOrderIds = _db.WorkOrderAssignees.Where(a => a.UserId == MyUserId).Select(a => a.OrderId);
        return await BuildPage(_db.WorkOrders.Where(w => myOrderIds.Contains(w.Id) && w.Status == "Open"), filter, companyId.Value,
            myAssignmentOnly: true);
    }

    /// <summary>بایگانی — دستورهای بسته‌شده (من دستور داده‌ام یا به من محول شده) — با فیلتر اختیاری.</summary>
    [HttpGet("archive")]
    public async Task<IActionResult> Archive([FromQuery] ListFilterDto filter, [FromQuery] int? skip = null, [FromQuery] int? take = null)
    {
        filter.Page = Paging.ToPage(skip, take, filter.Page);
        filter.PageSize = Paging.ToPageSize(take, filter.PageSize);
        var companyId = await ActiveCompanyIdAsync();
        if (companyId is null) return BadRequest(new { message = "شرکت فعال انتخاب نشده است." });
        var myOrderIds = _db.WorkOrderAssignees.Where(a => a.UserId == MyUserId).Select(a => a.OrderId);
        return await BuildPage(_db.WorkOrders.Where(w =>
            w.Status == "Closed" && (w.OwnerUserId == MyUserId || myOrderIds.Contains(w.Id))), filter, companyId.Value);
    }

    public class WorkOrderCountsDto
    {
        public int Mine { get; set; }
        public int Assigned { get; set; }
        public int Archive { get; set; }
    }

    /// <summary>
    /// شمارندهٔ تب‌ها — «تعداد سطر دستور کار»، نه تعداد گیرندگان.
    /// هر دستور کار یک بار شمرده می‌شود، حتی اگر چند گیرنده (یا گیرندهٔ تکراری) داشته باشد؛
    /// فقط سطرهای همان شرکت فعال هم لحاظ می‌شوند تا با فهرست همان تب هم‌خوان باشد.
    /// </summary>
    [HttpGet("counts")]
    public async Task<IActionResult> Counts()
    {
        var companyId = await ActiveCompanyIdAsync();
        if (companyId is null) return BadRequest(new { message = "شرکت فعال انتخاب نشده است." });

        var baseQ = _db.WorkOrders.AsNoTracking().Where(w => w.CompanyId == companyId.Value);
        var myOrderIds = _db.WorkOrderAssignees.Where(a => a.UserId == MyUserId).Select(a => a.OrderId);

        return Ok(new WorkOrderCountsDto
        {
            // Count() روی سطرهای جدول دستور کار = یک عدد به‌ازای هر دستور کار
            Mine = await baseQ.CountAsync(w => w.OwnerUserId == MyUserId && w.Status == "Open"),
            Assigned = await baseQ.CountAsync(w => w.Status == "Open" && myOrderIds.Contains(w.Id)),
            Archive = await baseQ.CountAsync(w =>
                w.Status == "Closed" && (w.OwnerUserId == MyUserId || myOrderIds.Contains(w.Id)))
        });
    }

    /// <summary>شناسه منابعی که حداقل یک دستورکار دارند؛ برای نشان‌گذاری لیست نامه‌ها.</summary>
    [HttpGet("source-ids")]
    public async Task<IActionResult> SourceIds([FromQuery] string? module)
    {
        var companyId = await ActiveCompanyIdAsync();
        if (companyId is null) return BadRequest(new { message = "شرکت فعال انتخاب نشده است." });
        var q = _db.WorkOrders.Where(w => w.CompanyId == companyId.Value && w.SourceId != null);
        var letterType = ModuleToLetterType(module);
        // نامِ ماژول‌های نامه — برای اینکه دستورهای مدرک/صورتجلسه (که شناسهٔ
        // مبدأشان ممکن است با شناسهٔ یک نامه برخورد کند) قاطی نشوند.
        var letterModuleNames = new[] { "InnerLetter", "InnerLetters", "OutgoingLetter", "OutgoingLetters", "IncomingLetter", "IncomingLetters" };
        if (letterType is int lt)
        {
            // نوعِ نامه از جدولِ LetterSources خوانده می‌شود (مرجعِ اصلی) تا دستورهایی
            // که با نامِ ماژولِ قدیمی یا نادرست ذخیره شده‌اند هم دیده شوند.
            q = q.Where(w => letterModuleNames.Contains(w.SourceModule!) && w.SourceId != null && _db.LetterSources
                .Any(s => s.Id == w.SourceId.Value && !s.IsDelete && s.SourceType == lt));
        }
        else if (!string.IsNullOrWhiteSpace(module))
        {
            var source = module.Trim();
            q = source.Equals("letters", StringComparison.OrdinalIgnoreCase)
                ? q.Where(w => letterModuleNames.Contains(w.SourceModule!) && w.SourceId != null && _db.LetterSources
                    .Any(s => s.Id == w.SourceId.Value && !s.IsDelete && s.SourceType >= 1 && s.SourceType <= 3))
                : q.Where(w => w.SourceModule == source);
        }
        return Ok(await q.Select(w => w.SourceId!.Value).Distinct().ToListAsync());
    }

    /// <summary>دستورهای کارِ ساخته‌شده از یک مبدأ (سورس) — مثلاً نامه داخلی. برای لینک/نشان «دستورکار شده».</summary>
    /// <remarks>فقط متادیتای سبک برمی‌گرداند (بدون شرح/گیرندگان) تا به‌عنوان نشان در کارتابل نامه استفاده شود.</remarks>
    [HttpGet("for-source")]
    public async Task<IActionResult> ForSource([FromQuery] string? module, [FromQuery] int? sourceId, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        var pagination = new Paging.Request(skip, take);
        var companyId = await ActiveCompanyIdAsync();
        if (companyId is null) return BadRequest(new { message = "شرکت فعال انتخاب نشده است." });
        if (sourceId is not > 0) return Ok(new List<object>());

        var q = _db.WorkOrders.Where(w => w.CompanyId == companyId.Value && w.SourceId == sourceId);
        var letterType = ModuleToLetterType(module);
        int? canonicalType = null;
        if (letterType is int lt)
        {
            // شناسهٔ نامه یکتا و مشترک میان هر سه نوع نامه است؛ نوعِ واقعی از
            // LetterSource خوانده می‌شود. اگر این شناسه به نامه‌ای از نوعِ دیگری
            // اشاره کند، این نامه دستورکاری ندارد.
            var src = await _db.LetterSources.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == sourceId.Value && !x.IsDelete);
            if (src == null || src.SourceType != lt) return Ok(new List<object>());
            canonicalType = src.SourceType;
            // نامِ ماژولِ ذخیره‌شده ممکن است قدیمی یا اشتباه باشد؛ پس ملاک، «نامه
            // بودنِ مبدأ» است و نه رشتهٔ ماژول — وگرنه دستورهای قدیمی از قلم می‌افتادند.
            var letterModuleNames = new[] { "InnerLetter", "InnerLetters", "OutgoingLetter", "OutgoingLetters", "IncomingLetter", "IncomingLetters" };
            q = q.Where(w => letterModuleNames.Contains(w.SourceModule!));
        }
        else if (!string.IsNullOrWhiteSpace(module))
        {
            q = q.Where(w => w.SourceModule == module.Trim());
        }
        else return Ok(new List<object>());

        var list = await q
            .OrderByDescending(w => w.Id)
            .Select(w => new
            {
                w.Id, w.Number, w.Title, w.OwnerName, w.Status, w.SourceModule, w.SourceId,
                SourceType = canonicalType,
                Assignees = _db.WorkOrderAssignees.Where(a => a.OrderId == w.Id).Select(a => a.Name).ToList(),
                AssigneeCount = _db.WorkOrderAssignees.Count(a => a.OrderId == w.Id)
            })
            .ToPageListAsync(pagination);
        return Ok(pagination.Result(list));
    }

    /// <summary>دستورهای کارِ یک پروژه کاری — خوراک تب «دستورها» در جزئیات پروژه.</summary>
    [HttpGet("by-project/{projectId:int}")]
    public async Task<IActionResult> ByProject(int projectId, [FromQuery] string? status, [FromQuery] int? take)
    {
        if (!await HasAsync("View")) return Forbid();
        var companyId = await ActiveCompanyIdAsync();
        if (companyId is null) return BadRequest(new { message = "شرکت فعال انتخاب نشده است." });
        var exists = await _db.WorkProjects.AsNoTracking()
            .AnyAsync(p => p.Id == projectId && p.CompanyId == companyId.Value);
        if (!exists) return NotFound(new { message = "پروژه پیدا نشد." });
        var q = _db.WorkOrders.Where(w => w.CompanyId == companyId.Value && w.ProjectId == projectId);
        if (status == "open") q = q.Where(w => w.Status == "Open");
        else if (status == "closed") q = q.Where(w => w.Status == "Closed");
        var n = Math.Clamp(take is > 0 ? take.Value : 200, 1, 500);
        return Ok(await BuildList(q.OrderByDescending(w => w.Priority).ThenByDescending(w => w.Id).Take(n)));
    }

    /// <summary>جزئیات یک دستور کار به‌صورت مستقیم — برای لینک عمیق (مثلاً از نشان «دستورکار شده» نامه).</summary>
    [HttpGet("{id:int}")]
    public async Task<IActionResult> Detail(int id)
    {
        var companyId = await ActiveCompanyIdAsync();
        if (companyId is null) return BadRequest(new { message = "شرکت فعال انتخاب نشده است." });
        if (!await HasAsync("View") && !await CanSeeOrderAsync(id)) return Forbid();
        var list = await BuildList(_db.WorkOrders.Where(w => w.Id == id && w.CompanyId == companyId.Value));
        return list.Count == 0 ? NotFound() : Ok(list[0]);
    }

    /// <summary>
    /// متن کامل شرح دستور کار — برای شرح‌های بلند (دستور کارِ ساخته‌شده از نامهٔ دارای تصویر)
    /// که ستون اصلی فقط پیش‌نمایش کوتاه دارد. فهرست‌ها این را نمی‌خوانند؛ فقط جزئیات.
    /// </summary>
    [HttpGet("{id:int}/description")]
    public async Task<IActionResult> Description(int id)
    {
        if (!await HasAsync("View") && !await CanSeeOrderAsync(id)) return Forbid();
        var full = await WorkOrderDescriptionStore.GetAsync(_db, id);
        if (full is null)
        {
            // شرح کوتاه — همان ستونی که در فهرست آمده است
            var wo = await _db.WorkOrders.AsNoTracking().Where(w => w.Id == id)
                .Select(w => new { w.Description }).FirstOrDefaultAsync();
            if (wo is null) return NotFound();
            return Ok(new { description = wo.Description ?? "", full = false });
        }
        return Ok(new { description = full, full = true });
    }

    /// <summary>تقویم شمسی — دستورهای بازه زمانی (بند ۱۳).</summary>
    [HttpGet("calendar")]
    public async Task<IActionResult> Calendar([FromQuery] DateTime from, [FromQuery] DateTime to, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        var pagination = new Paging.Request(skip, take);
        var myOrderIds = _db.WorkOrderAssignees.Where(a => a.UserId == MyUserId).Select(a => a.OrderId);
        var orders = await _db.WorkOrders
            .Where(w => w.DueAt >= from && w.DueAt < to && (w.OwnerUserId == MyUserId || myOrderIds.Contains(w.Id))).OrderBy(x => x.Id)
            .ToPageListAsync(pagination);
        var ids = orders.Select(o => o.Id).ToList();
        var asgs = await _db.WorkOrderAssignees.Where(a => ids.Contains(a.OrderId)).ToListAsync();

        return Ok(pagination.Result(orders.Select(w =>
        {
            var mine = w.OwnerUserId == MyUserId;
            var list = asgs.Where(a => a.OrderId == w.Id).ToList();
            var toSelf = list.All(a => a.UserId == w.OwnerUserId);
            // Kind: Self (برای خودم) | Others (برای دیگران) | Mixed (مشترک)
            var kind = mine && toSelf ? "Self"
                     : mine && list.Any(a => a.UserId == w.OwnerUserId) ? "Mixed"
                     : mine ? "Others" : "Assigned";

            // وضعیت رنگی (همان قوانین بند ۱۱ و ۱۲) — برای تیک/ضربدر در تقویم
            string tone;
            var allDone = list.Count > 0 && list.All(a => a.Done == true);
            if (allDone)
            {
                var last = list.Max(a => a.RepliedAt) ?? DateTime.MaxValue;
                tone = last <= w.DueAt ? "ontime" : "latedone";
            }
            else if (w.Status == "Closed") tone = "closed";
            else if (w.DueAt < DateTime.Now && w.DueAt.Date != DateTime.Today) tone = "late";
            else if (w.DueAt.Date == DateTime.Today) tone = DateTime.Now > w.DueAt ? "late" : "today";
            else tone = "none";

            return new { w.Id, w.Number, w.Title, w.DueAt, w.Status, Kind = kind, Tone = tone };
        })));
    }

    // ================== رویت (بند ۸) ==================
    [HttpPost("{id:int}/seen")]
    public async Task<IActionResult> Seen(int id)
    {
        if (!await _db.WorkOrders.AnyAsync(w => w.Id == id)) return NotFound();
        var asg = await _db.WorkOrderAssignees.FirstOrDefaultAsync(a => a.OrderId == id && a.UserId == MyUserId);
        if (asg != null && asg.SeenAt == null)
        {
            asg.SeenAt = DateTime.Now;
            Log(id, "Seen", $"{MyUsername} دستور کار را رویت کرد.");
            await _db.SaveChangesAsync();
        }
        return Ok();
    }

    // ================== پاسخ گیرنده ==================
    public class ReplyDto { public bool Done { get; set; } = true; public string? Text { get; set; } }

    [HttpPost("{id:int}/reply")]
    public async Task<IActionResult> Reply(int id, [FromBody] ReplyDto dto)
    {
        var wo = await _db.WorkOrders.FirstOrDefaultAsync(w => w.Id == id);
        if (wo == null) return NotFound(new { message = "دستور کار پیدا نشد." });
        if (wo.Status == "Closed")
            return BadRequest(new { message = "این دستور کار بسته شده و قابل تغییر نیست." }); // بند ۱۰

        var asg = await _db.WorkOrderAssignees.FirstOrDefaultAsync(a => a.OrderId == id && a.UserId == MyUserId);
        if (asg == null) return NotFound(new { message = "این دستور کار به شما محول نشده است." });

        asg.Done = dto.Done;
        asg.ReplyText = dto.Text?.Trim();
        asg.RepliedAt = DateTime.Now;
        asg.OwnerDecision = null; // پاسخ جدید = تصمیم قبلی باطل
        if (asg.SeenAt == null) asg.SeenAt = DateTime.Now;

        Log(id, "Reply", $"{MyUsername}: {(dto.Done ? "✅ انجام شد" : "❌ انجام نشد")}{(string.IsNullOrWhiteSpace(dto.Text) ? "" : " — " + dto.Text)}");
        await _db.SaveChangesAsync();

        await NotifyReplyOwnersAsync(wo, dto);
        await _notify.BroadcastChangedAsync("workorders");
        return Ok();
    }

    /// <summary>
    /// دستوردهندگانِ بالادست یک دستور کار (والد، جد، … تا ۵ سطح).
    /// برای زیر-دستورها لازم است: گیرندهٔ زیر-دستور پاسخ می‌دهد، اما صاحبِ
    /// دستور کار اصلی هم باید از نتیجه باخبر شود.
    /// </summary>
    private async Task<List<WorkOrder>> LoadAncestorsAsync(WorkOrder wo)
    {
        var ancestors = new List<WorkOrder>();
        var cur = wo;
        var guard = 0;
        while (cur.ParentOrderId is > 0 && guard++ < 6)
        {
            var parent = await _db.WorkOrders.AsNoTracking()
                .FirstOrDefaultAsync(w => w.Id == cur.ParentOrderId);
            if (parent == null) break;
            ancestors.Add(parent);
            cur = parent;
        }
        return ancestors;
    }

    /// <summary>
    /// اطلاع‌رسانی پاسخ ثبت‌شده: مالکِ همین دستور کار (مثلاً B) + مالکان همهٔ والدهای
    /// زنجیره (مثلاً A که دستور کار اصلی را داده است) تا ۵ سطح.
    /// هر کاربر فقط یک‌بار و هرگز خودِ پاسخ‌دهنده اعلان نمی‌گیرد. پاسخِ زیر-دستور در
    /// تاریخچهٔ همهٔ دستورهای بالاسری هم ثبت می‌شود تا در «گردش کار» والد دیده شود.
    /// </summary>
    private async Task NotifyReplyOwnersAsync(WorkOrder wo, ReplyDto dto)
    {
        var result = dto.Done ? "انجام شد ✅" : "انجام نشد ❌";
        var note = string.IsNullOrWhiteSpace(dto.Text) ? "" : " — " + dto.Text.Trim();
        var selfLink = $"/work-orders?open={wo.Id}";

        var ancestors = await LoadAncestorsAsync(wo);
        var parent = ancestors.FirstOrDefault(); // نزدیک‌ترین والد
        var chainTag = parent is null ? "" : $" (زیر-دستورِ {parent.Number})";
        var title = parent is null ? "پاسخ دستور کار" : "پاسخ زیر-دستور کار 📋";

        // کاربرانی که مجوز «View» دستور کار را دارند می‌توانند خودِ زیر-دستور را باز کنند؛
        // بقیه (مثلاً دستوردهندهٔ بالاسری بدون این مجوز) به دستور خودشان هدایت می‌شوند تا
        // لینک اعلان برایشان ۴۰۳ نشود و ردّ پاسخ را در «گردش کار» همان دستور ببینند.
        var viewers = await UsersWithWorkOrderViewAsync();
        var subAssigneesFa = await AssigneesFaAsync(wo.Id);

        var notified = new HashSet<int>();

        // ۱) مالکِ همین دستور کار — پاسخ‌دهنده به او خبر می‌دهد.
        if (wo.OwnerUserId > 0 && wo.OwnerUserId != MyUserId)
        {
            await _notify.SendAsync(wo.OwnerUserId, title,
                $"{wo.Number}{chainTag} — {MyUsername}: {result}{note}",
                MyUsername, "دستور کار", selfLink);
            notified.Add(wo.OwnerUserId);
        }

        // ۲) مالکان بالادست — پاسخِ زیر-دستور باید به اطلاع کسی هم برسد که دستور کار
        //    بالاسری را صادر کرده است (A). برای هر والد یک اعلان، با لینک قابل‌بازکردن.
        foreach (var anc in ancestors)
        {
            if (anc.OwnerUserId <= 0) continue;
            if (anc.OwnerUserId == MyUserId) continue;
            if (!notified.Add(anc.OwnerUserId)) continue;

            var link = viewers.Contains(anc.OwnerUserId) ? selfLink : $"/work-orders?open={anc.Id}";
            var body = anc.Id == parent?.Id
                ? $"{wo.Number} «{wo.Title}» — زیر-دستورِ {anc.Number} («{anc.Title}») که {wo.OwnerName} به {subAssigneesFa} سپرده بود — {MyUsername}: {result}{note}"
                : $"{wo.Number} «{wo.Title}» — در زنجیرهٔ {anc.Number} («{anc.Title}») — {MyUsername}: {result}{note}";

            await _notify.SendAsync(anc.OwnerUserId, "پاسخ زیر-دستور کار 📋", body,
                MyUsername, "دستور کار", link);
        }

        // ۳) ثبت در تاریخچهٔ دستورهای بالاسری — تا در «گردش کار» والد هم ردّ پاسخ بماند.
        if (ancestors.Count > 0)
        {
            foreach (var anc in ancestors)
                Log(anc.Id, "SubReply", $"پاسخ زیر-دستور {wo.Number} «{wo.Title}» — {MyUsername}: {result}{note}");
            await _db.SaveChangesAsync();
        }
    }

    /// <summary>
    /// شناسهٔ کاربرانی که مجوز «WorkOrders.View» دارند (+ ادمین‌های قدیمی بدون نقش RBAC).
    /// این کنترلر از RbacControllerBase ارث نمی‌برد، پس متد کمکی در همین کلاس تعریف شده است.
    /// </summary>
    private async Task<List<int>> UsersWithWorkOrderViewAsync()
    {
        var rbac = await _db.UserRoles
            .Where(ur => _db.Roles.Any(r => r.Id == ur.RoleId && r.IsActive))
            .Join(_db.RolePermissions, ur => ur.RoleId, rp => rp.RoleId, (ur, rp) => new { ur.UserId, rp.PermissionId })
            .Join(_db.Permissions, x => x.PermissionId, p => p.Id, (x, p) => new { x.UserId, p.Module, p.Action })
            .Where(x => x.Module == "WorkOrders" && x.Action == "View")
            .Select(x => x.UserId)
            .Distinct()
            .ToListAsync();

        // ادمین‌های قدیمی (بدون نقش RBAC) به همهٔ مجوزها دسترسی دارند
        var legacyAdmins = await _db.Users
            .Where(u => u.Role == "Admin" && u.IsActive && !_db.UserRoles.Any(ur => ur.UserId == u.Id))
            .Select(u => u.Id)
            .ToListAsync();

        return rbac.Concat(legacyAdmins).Distinct().ToList();
    }

    /// <summary>نام گیرندگان یک دستور برای متن اعلان (حداکثر سه نفر و بقیه با شمارش).</summary>
    private async Task<string> AssigneesFaAsync(int orderId)
    {
        var names = await _db.WorkOrderAssignees.AsNoTracking()
            .Where(a => a.OrderId == orderId).Select(a => a.Name).ToListAsync();
        if (names.Count == 0) return "کسی";
        return names.Count <= 3
            ? string.Join("، ", names)
            : string.Join("، ", names.Take(3)) + $" و {names.Count - 3} نفر دیگر";
    }

    // ================== تایید/رد پاسخ هر گیرنده (بند ۹ و ۱۸) ==================
    public class DecideDto { public bool Approved { get; set; } public string? Note { get; set; } }

    [HttpPost("{id:int}/assignees/{asgId:int}/decide")]
    public async Task<IActionResult> Decide(int id, int asgId, [FromBody] DecideDto dto)
    {
        var wo = await _db.WorkOrders.FirstOrDefaultAsync(w => w.Id == id);
        if (wo == null) return NotFound(new { message = "دستور کار پیدا نشد." });
        if (wo.OwnerUserId != MyUserId) return Forbid();
        if (wo.Status == "Closed") return BadRequest(new { message = "دستور کار بسته شده است." });

        var asg = await _db.WorkOrderAssignees.FirstOrDefaultAsync(a => a.Id == asgId && a.OrderId == id);
        if (asg == null) return NotFound();
        if (asg.RepliedAt == null) return BadRequest(new { message = "این فرد هنوز پاسخی ثبت نکرده است." });

        asg.OwnerDecision = dto.Approved ? "Approved" : "Rejected";
        asg.OwnerDecisionNote = dto.Note?.Trim();
        if (!dto.Approved)
        {
            // رد: پاسخ باطل و برگشت به گیرنده
            asg.RepliedAt = null;
            asg.Done = null;
        }

        Log(id, dto.Approved ? "Approved" : "Rejected",
            $"پاسخ {asg.Name} {(dto.Approved ? "تایید" : "رد")} شد{(string.IsNullOrWhiteSpace(dto.Note) ? "" : " — " + dto.Note)}");
        await _db.SaveChangesAsync();

        await _notify.SendAsync(asg.UserId,
            dto.Approved ? "پاسخ شما تایید شد ✅" : "پاسخ شما رد شد ❌",
            $"{wo.Number} — «{wo.Title}»{(string.IsNullOrWhiteSpace(dto.Note) ? "" : " — " + dto.Note)}",
            MyUsername, "دستور کار", $"/work-orders?open={id}");
        await _notify.BroadcastChangedAsync("workorders");
        return Ok();
    }

    // ================== تمدید مهلت — تا ۵ بار (بند ۱۶) ==================
    public class ExtendDto { public DateTime NewDueAt { get; set; } public string? Note { get; set; } }

    [HttpPost("{id:int}/extend")]
    public async Task<IActionResult> Extend(int id, [FromBody] ExtendDto dto)
    {
        var wo = await _db.WorkOrders.FirstOrDefaultAsync(w => w.Id == id);
        if (wo == null) return NotFound(new { message = "دستور کار پیدا نشد." });
        if (wo.OwnerUserId != MyUserId) return Forbid();
        if (wo.Status == "Closed") return BadRequest(new { message = "دستور کار بسته شده است." });
        if (wo.ExtensionCount >= 5)
            return BadRequest(new { message = "حداکثر ۵ بار امکان تمدید وجود دارد." });
        if (dto.NewDueAt <= DateTime.Now)
            return BadRequest(new { message = "تاریخ تمدید نمی‌تواند قبل از زمان فعلی باشد." });

        var old = wo.DueAt;
        wo.DueAt = dto.NewDueAt;
        wo.ExtensionCount++;

        Log(id, "Extended", $"تمدید {wo.ExtensionCount} از ۵ — از {ToFa(old)} به {ToFa(dto.NewDueAt)}{(string.IsNullOrWhiteSpace(dto.Note) ? "" : " — " + dto.Note)}");
        await _db.SaveChangesAsync();

        var asgs = await _db.WorkOrderAssignees.Where(a => a.OrderId == id).ToListAsync();
        foreach (var a in asgs.Where(a => a.UserId != MyUserId))
            await _notify.SendAsync(a.UserId, "تمدید مهلت دستور کار ⏳",
                $"{wo.Number} — مهلت جدید: {ToFa(dto.NewDueAt)}",
                MyUsername, "دستور کار", $"/work-orders?open={id}");
        await _notify.BroadcastChangedAsync("workorders");
        return Ok(new { extensionCount = wo.ExtensionCount });
    }

    // ================== بستن نهایی (بند ۹ و ۱۰) ==================
    public class CloseDto { public string? Note { get; set; } }

    [HttpPost("{id:int}/close")]
    public async Task<IActionResult> Close(int id, [FromBody] CloseDto dto)
    {
        var wo = await _db.WorkOrders.FirstOrDefaultAsync(w => w.Id == id);
        if (wo == null) return NotFound(new { message = "دستور کار پیدا نشد." });
        if (wo.OwnerUserId != MyUserId) return Forbid();
        if (wo.Status == "Closed") return BadRequest(new { message = "قبلاً بسته شده است." });

        // ارجاع زنجیره‌ای — تا زیر-دستورهای باز بسته نشوند، والد قابل بستن نیست
        var openChildren = await _db.WorkOrders.AsNoTracking()
            .Where(w => w.ParentOrderId == id && w.Status == "Open")
            .Select(w => w.Number).ToListAsync();
        if (openChildren.Count > 0)
            return BadRequest(new { message = $"ابتدا زیر-دستورهای باز بسته شوند: {string.Join("، ", openChildren)}" });

        wo.Status = "Closed";
        wo.ClosedAt = DateTime.Now;
        wo.CloseNote = dto.Note?.Trim();

        Log(id, "Closed", $"دستور کار توسط {MyUsername} نهایی و بسته شد{(string.IsNullOrWhiteSpace(dto.Note) ? "" : " — " + dto.Note)}");
        if (wo.ProjectId is > 0)
            _db.WorkProjectLogs.Add(new WorkProjectLog { ProjectId = wo.ProjectId.Value, ActorName = MyUsername, Action = "OrderClosed", Text = $"دستور {wo.Number} «{wo.Title}» بسته شد." });
        await _db.SaveChangesAsync();

        var asgs = await _db.WorkOrderAssignees.Where(a => a.OrderId == id).ToListAsync();
        foreach (var a in asgs.Where(a => a.UserId != MyUserId))
            await _notify.SendAsync(a.UserId, "دستور کار بسته شد 🔒",
                $"{wo.Number} — «{wo.Title}»", MyUsername, "دستور کار", $"/work-orders?open={id}");

        await _notify.BroadcastChangedAsync("workorders");
        if (wo.ProjectId is > 0) await _notify.BroadcastChangedAsync("workprojects");
        return Ok();
    }

    // ================== افزودن نفر به گردش دستور کار (فقط دستوردهنده) ==================
    /// <summary>
    /// افزودن گیرندهٔ جدید به دستور کارِ باز از داخل «گردش» — حتی بعد از رویت/پاسخِ دیگران
    /// (برخلاف ویرایش که پس از اولین رویت قفل می‌شود). فقط اضافه می‌کند؛ گیرندگان فعلی دست نمی‌خورند.
    /// قواعد انتخاب افراد همان ثبت دستور کار است: «به دیگران» + لیست مجاز.
    /// </summary>
    public class AddAssigneesDto { public List<int> UserIds { get; set; } = new(); public string? Note { get; set; } public bool SendSms { get; set; } }

    [HttpPost("{id:int}/assignees")]
    public async Task<IActionResult> AddAssignees(int id, [FromBody] AddAssigneesDto dto)
    {
        var ids = (dto?.UserIds ?? new()).Where(x => x > 0).Distinct().ToList();
        if (ids.Count == 0) return BadRequest(new { message = "حداقل یک نفر را انتخاب کنید." });
        if (ids.Count > 50) return BadRequest(new { message = "در هر مرحله حداکثر ۵۰ نفر قابل افزودن است." });

        await using var tx = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var wo = await _db.WorkOrders.FirstOrDefaultAsync(w => w.Id == id);
        if (wo == null) return NotFound(new { message = "دستور کار پیدا نشد." });
        if (wo.OwnerUserId != MyUserId) return Forbid();
        if (wo.Status != "Open")
            return BadRequest(new { message = "دستور کار بسته است؛ برای افزودن نفر ابتدا آن را بازگشایی کنید." });

        var existingIds = await _db.WorkOrderAssignees.Where(a => a.OrderId == id).Select(a => a.UserId).ToListAsync();
        var dup = ids.Where(existingIds.Contains).ToList();
        if (dup.Count == ids.Count) return BadRequest(new { message = "افراد انتخابی از قبل در گردش این دستور کار هستند." });
        ids = ids.Except(dup).ToList();

        var others = ids.Where(x => x != MyUserId).ToList();
        if (others.Count > 0)
        {
            if (!await HasAsync("AssignOthers"))
                return BadRequest(new { message = "شما فقط مجاز به ثبت دستور کار برای خودتان هستید." });
            if (!IsLegacyAdmin)
            {
                var allowed = await _db.WorkOrderAllowedAssignees.Where(a => a.OwnerUserId == MyUserId)
                    .Select(a => a.TargetUserId).ToListAsync();
                if (others.Any(x => !allowed.Contains(x)))
                    return BadRequest(new { message = "برخی افراد انتخابی در لیست مجاز شما نیستند." });
            }
        }

        var users = await _db.Users.Where(u => ids.Contains(u.Id) && u.IsActive).ToListAsync();
        if (users.Count != ids.Count)
            return BadRequest(new { message = "برخی افراد انتخابی وجود ندارند یا غیرفعال‌اند." });
        var noName = users.Where(u => string.IsNullOrWhiteSpace(u.FirstName) || string.IsNullOrWhiteSpace(u.LastName))
            .Select(u => u.Username).ToList();
        if (noName.Count > 0)
            return BadRequest(new { message = $"این کاربران نام و نام خانوادگی ندارند: {string.Join("، ", noName)} — از بخش کاربران تکمیل کنید." });

        foreach (var u in users)
            _db.WorkOrderAssignees.Add(new WorkOrderAssignee { OrderId = wo.Id, UserId = u.Id, Name = UserDisplay.Name(u) });

        var note = dto!.Note?.Trim();
        var names = string.Join("، ", users.Select(UserDisplay.Name));
        var text = $"افزودن به گردش: {names}" + (string.IsNullOrWhiteSpace(note) ? "" : " — " + note);
        Log(wo.Id, "AssigneesAdded", text.Length > 4000 ? text[..4000] : text);

        // دستور کارِ متصل به نامه: افراد جدید در گردش واقعی همان نامه هم قرار می‌گیرند تا از
        // فهرست دستور کار بتوانند نامه را باز کنند (مبدأ از خود رکورد خوانده می‌شود، نه از کلاینت).
        var letterFlowAdded = await SyncOrderAssigneesToLetterFlowAsync(wo, users.Select(u => u.Id));
        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        var prTag = wo.Priority >= WorkOrderPriority.High ? $" — اولویت: {WorkOrderPriority.ToFa(wo.Priority)}" : "";
        foreach (var u in users.Where(u => u.Id != MyUserId))
            await _notify.SendAsync(u.Id, "به دستور کار اضافه شدید 📋",
                $"{wo.Number} — «{wo.Title}» — مهلت: {ToFa(wo.DueAt)}{prTag}{(string.IsNullOrWhiteSpace(note) ? "" : " — " + note)}",
                wo.OwnerName, "دستور کار", $"/work-orders?open={wo.Id}");

        WorkOrderSmsResult? smsResult = dto.SendSms
            ? await _sms.SendNewWorkOrderAsync(wo, users.Where(u => u.Id != MyUserId).ToList())
            : null;

        await _notify.BroadcastChangedAsync("workorders");
        if (letterFlowAdded > 0)
        {
            await _notify.BroadcastChangedAsync("letters");
            await _notify.BroadcastChangedAsync("incoming-letters");
            await _notify.BroadcastChangedAsync("outgoing-letters");
        }
        return Ok(new { added = users.Count, skipped = dup.Count, sms = smsResult, letterFlowAdded });
    }

    // ================== همگام‌سازی دسترسی به نامه برای گیرندگان ==================
    /// <summary>
    /// هر گیرندهٔ دستورکاری که به یک نامه متصل است باید از فهرست دستور کار بتواند همان نامه را
    /// باز کند. این endpoint برای «دادهٔ قبلی» است: روی همهٔ دستورکارهایی که کاربر در آن‌ها
    /// گیرنده یا دستوردهنده است اجرا می‌شود و در صورت نبودِ ردیف ارجاع در گردش نامه، آن را
    /// idempotent اضافه می‌کند. ردیف‌های موجود دست نمی‌خورند.
    /// </summary>
    [HttpPost("letter-flow/sync")]
    public async Task<IActionResult> SyncLetterFlow()
    {
        if (!await HasAsync("View")) return Forbid();

        // ---- پاک‌سازی ردیف‌های اشتباهِ قبلی ----
        // نسخهٔ قبلیِ همین همگام‌سازی، *خودِ کاربر* را در گردش نامه ثبت می‌کرد؛ نتیجه این بود
        // که بعد از ساختن دستور کار روی یک نامه، همان نامه در «دریافتی» خودش هم ظاهر می‌شد.
        // ردیف‌های ساختِ همین همگام‌سازی (متن اختصاصی + فرستنده = گیرنده) اگر هنوز خوانده
        // نشده‌اند حذف می‌شوند تا صندوق کاربر پاک شود. ردیف‌های ارجاع واقعی (فرستنده ≠ گیرنده)
        // یا خوانده‌شده/پاسخ‌داده‌شده هرگز دست نمی‌خورند.
        const string autoText = "دسترسی به نامه از طریق دستورکار";
        var junk = await _db.Erjas
            .Where(e => e.ReciverUserId == MyUserId && e.SenderUserId == MyUserId
                        && !e.IsDelete && e.Type == "ارجاع"
                        && e.MatnErja.StartsWith(autoText)
                        && !e.IsRead && (e.IsBayegani == null || e.IsBayegani == false)
                        && (e.Answer == null || e.Answer == "") && e.TypeTaeed == 0
                        && !e.IsNeshan)
            .ToListAsync();
        var removed = junk.Count;
        if (removed > 0) _db.Erjas.RemoveRange(junk);

        // ---- برقراری دسترسی برای «دیگر گیرندگان» دستورکارهای متصل به نامه ----
        // فقط افراد دیگر؛ خودِ کاربر جاری هرگز به گردش نامه اضافه نمی‌شود (نه به‌عنوان
        // دستوردهنده و نه وقتی خودش را گیرنده کند). در متد هسته هم دوباره فیلتر می‌شود.
        var orders = await _db.WorkOrders.AsNoTracking()
            .Where(w => w.DeletedAt == null && w.SourceId != null && w.SourceId > 0
                        && w.SourceModule != null && w.SourceModule != ""
                        && (w.OwnerUserId == MyUserId
                            || _db.WorkOrderAssignees.Any(a => a.OrderId == w.Id && a.UserId == MyUserId)))
            .Select(w => new
            {
                w.Title, w.Number, SourceId = w.SourceId!.Value, w.OwnerUserId,
                Assignees = _db.WorkOrderAssignees.Where(a => a.OrderId == w.Id && a.UserId != MyUserId)
                    .Select(a => a.UserId).ToList()
            })
            .ToListAsync();

        var added = 0;
        foreach (var o in orders.Where(x => x.Assignees.Count > 0))
        {
            // فرستندهٔ ارجاع، دستوردهندهٔ همان دستور کار است تا ردیف در گردش نامه منطقی بماند
            added += await EnsureLetterFlowForUsersAsync(o.SourceId, o.Assignees, o.OwnerUserId, o.Title, o.Number);
        }
        if (added > 0 || removed > 0)
        {
            await _db.SaveChangesAsync();
            // ردیف‌های ارجاعِ تازه، کارتابل و شمارندهٔ نخواندهٔ نامه‌ها را تغییر می‌دهند
            await _notify.BroadcastChangedAsync("workorders");
            await _notify.BroadcastChangedAsync("letters");
            await _notify.BroadcastChangedAsync("incoming-letters");
            await _notify.BroadcastChangedAsync("outgoing-letters");
        }
        return Ok(new { added, removed, orders = orders.Count });
    }

    // ================== بازگشایی دستور کار بسته‌شده و برگشت به گردش ==================
    /// <param name="Note">علت بازگشایی (اختیاری) — در تاریخچه ثبت می‌شود.</param>
    /// <param name="NewDueAt">مهلت جدید (اختیاری)؛ جزو ۵ بار تمدید حساب نمی‌شود.</param>
    /// <param name="ResetReplies">پاسخ‌ها و تأییدهای قبلی گیرندگان باطل شود تا دوباره پاسخ دهند.</param>
    public class ReopenDto { public string? Note { get; set; } public DateTime? NewDueAt { get; set; } public bool ResetReplies { get; set; } = true; }

    [HttpPost("{id:int}/reopen")]
    public async Task<IActionResult> Reopen(int id, [FromBody] ReopenDto? dto)
    {
        dto ??= new ReopenDto();
        var wo = await _db.WorkOrders.FirstOrDefaultAsync(w => w.Id == id);
        if (wo == null) return NotFound(new { message = "دستور کار پیدا نشد." });
        if (wo.OwnerUserId != MyUserId) return Forbid(); // مثل بستن: فقط دستوردهنده
        if (wo.Status != "Closed") return BadRequest(new { message = "این دستور کار باز است." });
        if (dto.NewDueAt is { } nd && nd <= DateTime.Now)
            return BadRequest(new { message = "مهلت جدید باید بعد از زمان فعلی باشد." });

        // ارجاع زنجیره‌ای — والد بسته یعنی همهٔ زیر-دستورها بسته‌اند؛ اول والد بازگشایی شود
        if (wo.ParentOrderId is > 0)
        {
            var parent = await _db.WorkOrders.AsNoTracking()
                .Where(w => w.Id == wo.ParentOrderId).Select(w => new { w.Number, w.Status }).FirstOrDefaultAsync();
            if (parent != null && parent.Status == "Closed")
                return BadRequest(new { message = $"دستور والد ({parent.Number}) بسته است؛ ابتدا آن را بازگشایی کنید." });
        }
        // پروژهٔ بسته دستور باز نمی‌پذیرد
        if (wo.ProjectId is > 0)
        {
            var prj = await _db.WorkProjects.AsNoTracking()
                .Where(p => p.Id == wo.ProjectId).Select(p => new { p.Number, p.Status }).FirstOrDefaultAsync();
            if (prj != null && prj.Status == WorkProjectStatus.Closed)
                return BadRequest(new { message = $"پروژهٔ این دستور ({prj.Number}) بسته است؛ ابتدا پروژه را بازگشایی کنید." });
        }

        wo.Status = "Open";
        wo.ClosedAt = null;
        wo.CloseNote = null; // یادداشت بستن قبلی در تاریخچه محفوظ است
        var oldDue = wo.DueAt;
        if (dto.NewDueAt is { } newDue) wo.DueAt = newDue;

        var asgs = await _db.WorkOrderAssignees.Where(a => a.OrderId == id).ToListAsync();
        if (dto.ResetReplies)
            foreach (var a in asgs)
            {
                a.Done = null; a.RepliedAt = null; a.ReplyText = null;
                a.OwnerDecision = null; a.OwnerDecisionNote = null;
            }

        var note = dto.Note?.Trim();
        var text = $"دستور کار توسط {MyUsername} بازگشایی شد و به گردش برگشت"
                 + (dto.NewDueAt is { } d2 ? $" — مهلت جدید: {ToFa(d2)} (قبلی: {ToFa(oldDue)})" : "")
                 + (dto.ResetReplies && asgs.Count > 0 ? " — پاسخ‌های قبلی گیرندگان باطل شد" : "")
                 + (string.IsNullOrWhiteSpace(note) ? "" : " — " + note);
        Log(id, "Reopened", text.Length > 4000 ? text[..4000] : text);
        if (wo.ProjectId is > 0)
            _db.WorkProjectLogs.Add(new WorkProjectLog { ProjectId = wo.ProjectId.Value, ActorName = MyUsername, Action = "OrderReopened", Text = $"دستور {wo.Number} «{wo.Title}» بازگشایی شد." });
        await _db.SaveChangesAsync();

        foreach (var a in asgs.Where(a => a.UserId != MyUserId))
            await _notify.SendAsync(a.UserId, "دستور کار بازگشایی شد 🔓",
                $"{wo.Number} — «{wo.Title}»{(dto.ResetReplies ? " — لطفاً دوباره پاسخ دهید" : "")}{(string.IsNullOrWhiteSpace(note) ? "" : " — " + note)}",
                MyUsername, "دستور کار", $"/work-orders?open={id}");

        await _notify.BroadcastChangedAsync("workorders");
        if (wo.ProjectId is > 0) await _notify.BroadcastChangedAsync("workprojects");
        return Ok();
    }

    /// <summary>حذف منطقی یک نوبت؛ نه کل سری. دسترسی مستقل در نقش‌ها.</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        if (!await HasAsync("Delete")) return Forbid();
        if (!await CanSeeOrderAsync(id)) return Forbid();
        await using var tx = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var wo = await _db.WorkOrders.FirstOrDefaultAsync(w => w.Id == id);
        if (wo == null) return NotFound();
        if (await _db.WorkOrders.AnyAsync(w => w.ParentOrderId == id))
            return BadRequest(new { message = "این دستور زیر‌دستور دارد؛ ابتدا زیر‌دستورها را بررسی و حذف کنید. حذف آبشاری انجام نمی‌شود." });
        // حذف واقعی پیوست‌ها از دیسک؛ رکوردهای قدیمیِ دیتابیسی نیز پاک می‌شوند.
        var attachments = await _db.AppAttachments.Where(a => a.Module == "WorkOrders" && a.RefId == id).ToListAsync();
        foreach (var attachment in attachments) _store.Delete(attachment.FilePath);
        _db.AppAttachments.RemoveRange(attachments);
        wo.DeletedAt = DateTime.Now;
        wo.DeletedByUserId = MyUserId;
        Log(id, "Deleted", $"حذف نوبت {wo.Number} توسط {await MyDisplayNameAsync()} (شناسه کاربر {MyUserId})؛ سوابق محفوظ است.");
        if (wo.ProjectId is > 0)
            _db.WorkProjectLogs.Add(new WorkProjectLog { ProjectId = wo.ProjectId.Value, ActorName = MyUsername, Action = "OrderRemoved", Text = $"دستور {wo.Number} حذف شد و از محاسبه پروژه خارج شد." });
        await _db.SaveChangesAsync();
        await tx.CommitAsync();
        await _notify.BroadcastChangedAsync("workorders");
        if (wo.ProjectId is > 0) await _notify.BroadcastChangedAsync("workprojects");
        return NoContent();
    }

    public class BulkOrdersDto { public List<int> Ids { get; set; } = new(); public string Action { get; set; } = "archive"; }

    /// <summary>عملیات گروهی امن روی دستورهای انتخاب‌شده.</summary>
    [HttpPost("bulk")]
    public async Task<IActionResult> Bulk([FromBody] BulkOrdersDto dto)
    {
        if (dto.Ids == null || dto.Ids.Count == 0)
            return BadRequest(new { message = "حداقل یک دستور کار را انتخاب کنید." });
        var orders = await _db.WorkOrders.Where(w => dto.Ids.Distinct().Contains(w.Id)).ToListAsync();
        if (orders.Count != dto.Ids.Distinct().Count())
            return BadRequest(new { message = "برخی دستورهای انتخاب‌شده یافت نشدند." });
        if (orders.Any(w => w.OwnerUserId != MyUserId))
            return Forbid();
        var archiving = dto.Action == "archive" ? orders.Where(w => w.Status == "Open").ToList() : new List<WorkOrder>();
        if (dto.Action == "delete")
        {
            foreach (var wo in orders)
            {
                var attachments = await _db.AppAttachments.Where(a => a.Module == "WorkOrders" && a.RefId == wo.Id).ToListAsync();
                foreach (var a in attachments) _store.Delete(a.FilePath);
                _db.AppAttachments.RemoveRange(attachments);
                wo.DeletedAt = DateTime.Now; wo.DeletedByUserId = MyUserId;
                Log(wo.Id, "Deleted", "حذف گروهی دستور کار");
            }
        }
        else if (dto.Action == "archive")
        {
            foreach (var wo in orders.Where(w => w.Status == "Open"))
            { wo.Status = "Closed"; wo.ClosedAt = DateTime.Now; Log(wo.Id, "Closed", "بایگانی گروهی دستور کار"); }
        }
        else return BadRequest(new { message = "نوع عملیات گروهی نامعتبر است." });
        // لاگ خلاصه در پروژه‌ها (یک رکورد به‌ازای هر پروژه، نه هر دستور)
        var touched = (dto.Action == "delete" ? orders : archiving).Where(w => w.ProjectId is > 0).ToList();
        foreach (var g in touched.GroupBy(w => w.ProjectId!.Value))
            _db.WorkProjectLogs.Add(new WorkProjectLog
            {
                ProjectId = g.Key, ActorName = MyUsername,
                Action = dto.Action == "delete" ? "OrderRemoved" : "OrderClosed",
                Text = dto.Action == "delete"
                    ? $"{g.Count()} دستور با حذف گروهی از محاسبه پروژه خارج شد."
                    : $"{g.Count()} دستور با بایگانی گروهی بسته شد."
            });
        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("workorders");
        if (touched.Count > 0) await _notify.BroadcastChangedAsync("workprojects");
        return Ok();
    }

    // ================== چک‌لیست زیرکار ==================

    /// <summary>آیتم‌های چک‌لیست یک دستور کار — به ترتیب SortOrder.</summary>
    [HttpGet("{id:int}/checklist")]
    public async Task<IActionResult> Checklist(int id, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (!await CanSeeOrderAsync(id)) return Forbid();
        return Ok(await Paging.ResultAsync(_db.WorkOrderChecklistItems.Where(c => c.OrderId == id)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Id)
            .Select(c => new { c.Id, c.Text, c.SortOrder, c.IsDone, c.DoneByName, c.DoneAt }), skip, take));
    }

    public class ChecklistAddDto { public string Text { get; set; } = ""; }

    /// <summary>افزودن آیتم چک‌لیست — فقط دستوردهنده و فقط تا وقتی دستور باز است.</summary>
    [HttpPost("{id:int}/checklist")]
    public async Task<IActionResult> ChecklistAdd(int id, [FromBody] ChecklistAddDto dto)
    {
        var wo = await _db.WorkOrders.FirstOrDefaultAsync(w => w.Id == id);
        if (wo == null) return NotFound(new { message = "دستور کار پیدا نشد." });
        if (wo.OwnerUserId != MyUserId) return Forbid();
        if (wo.Status == "Closed") return BadRequest(new { message = "دستور کار بسته شده است." });
        if (string.IsNullOrWhiteSpace(dto.Text)) return BadRequest(new { message = "متن آیتم را وارد کنید." });
        if (await _db.WorkOrderChecklistItems.CountAsync(c => c.OrderId == id) >= 50)
            return BadRequest(new { message = "حداکثر ۵۰ آیتم مجاز است." });

        var maxSort = await _db.WorkOrderChecklistItems.Where(c => c.OrderId == id)
            .Select(c => (int?)c.SortOrder).MaxAsync() ?? -1;
        var item = new WorkOrderChecklistItem { OrderId = id, Text = dto.Text.Trim(), SortOrder = maxSort + 1 };
        _db.WorkOrderChecklistItems.Add(item);
        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("workorders");
        return Ok(new { item.Id });
    }

    public class ChecklistToggleDto { public bool IsDone { get; set; } }

    /// <summary>تیک/برداشتن تیک آیتم — دستوردهنده یا هر گیرنده، تا وقتی دستور باز است.</summary>
    [HttpPost("{id:int}/checklist/{itemId:int}/toggle")]
    public async Task<IActionResult> ChecklistToggle(int id, int itemId, [FromBody] ChecklistToggleDto dto)
    {
        var wo = await _db.WorkOrders.FirstOrDefaultAsync(w => w.Id == id);
        if (wo == null) return NotFound(new { message = "دستور کار پیدا نشد." });
        if (wo.Status == "Closed") return BadRequest(new { message = "دستور کار بسته شده است." });

        var isOwner = wo.OwnerUserId == MyUserId;
        var isAssignee = await _db.WorkOrderAssignees.AnyAsync(a => a.OrderId == id && a.UserId == MyUserId);
        if (!isOwner && !isAssignee) return Forbid();

        var item = await _db.WorkOrderChecklistItems.FirstOrDefaultAsync(c => c.Id == itemId && c.OrderId == id);
        if (item == null) return NotFound(new { message = "آیتم پیدا نشد." });
        // گیرنده فقط می‌تواند آیتمِ انجام‌نشده را تیک بزند؛ برداشتن تیک یا تغییر
        // آیتمی که گیرنده دیگری انجام داده، از سمت API ممنوع است.
        if (isAssignee && !isOwner && !dto.IsDone)
            return BadRequest(new { message = "گیرنده نمی‌تواند تیک ثبت‌شده را بردارد." });
        if (item.IsDone && dto.IsDone && item.DoneByUserId != MyUserId && !isOwner)
            return BadRequest(new { message = "این آیتم قبلاً توسط گیرنده دیگری انجام شده و قابل تغییر نیست." });

        item.IsDone = dto.IsDone;
        if (dto.IsDone)
        {
            item.DoneByUserId = MyUserId;
            item.DoneByName = await MyDisplayNameAsync();
            item.DoneAt = DateTime.Now;
        }
        else
        {
            item.DoneByUserId = null;
            item.DoneByName = null;
            item.DoneAt = null;
        }
        await _db.SaveChangesAsync();

        var total = await _db.WorkOrderChecklistItems.CountAsync(c => c.OrderId == id);
        var done = await _db.WorkOrderChecklistItems.CountAsync(c => c.OrderId == id && c.IsDone);
        await _notify.BroadcastChangedAsync("workorders");
        return Ok(new { total, done });
    }

    /// <summary>حذف آیتم چک‌لیست — فقط دستوردهنده، فقط دستور باز.</summary>
    [HttpDelete("{id:int}/checklist/{itemId:int}")]
    public async Task<IActionResult> ChecklistDelete(int id, int itemId)
    {
        var wo = await _db.WorkOrders.FirstOrDefaultAsync(w => w.Id == id);
        if (wo == null) return NotFound(new { message = "دستور کار پیدا نشد." });
        if (wo.OwnerUserId != MyUserId) return Forbid();
        if (wo.Status == "Closed") return BadRequest(new { message = "دستور کار بسته شده است." });

        var item = await _db.WorkOrderChecklistItems.FirstOrDefaultAsync(c => c.Id == itemId && c.OrderId == id);
        if (item == null) return NotFound();
        _db.WorkOrderChecklistItems.Remove(item);
        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("workorders");
        return Ok();
    }

    /// <summary>آیا کاربر جاری حق دیدن این دستور را دارد؟ (دستوردهنده یا گیرنده یا مجوز View)</summary>
    private async Task<bool> CanSeeOrderAsync(int id)
    {
        var wo = await _db.WorkOrders.AsNoTracking().FirstOrDefaultAsync(w => w.Id == id);
        if (wo == null) return false;
        if (await HasAsync("View")) return true;
        return wo.OwnerUserId == MyUserId
            || await _db.WorkOrderAssignees.AnyAsync(a => a.OrderId == id && a.UserId == MyUserId);
    }

    // ================== داشبورد آماری ==================

    /// <summary>
    /// داشبورد آماری دستور کار — همیشه «شخصی» است و محدود به «شرکت فعال».
    /// برای همهٔ کاربران (حتی دارندهٔ مجوز «View») فقط دستورهایی شمرده می‌شود که
    /// خود کاربر صادر کرده یا به او محول شده است؛ آمار کل شرکت در این داشبورد نمایش داده نمی‌شود.
    /// خروجی: کارت‌های KPI + توزیع وضعیت و اولویت + عملکرد گیرندگان + روند ۶ ماه شمسی.
    /// </summary>
    [HttpGet("stats")]
    public async Task<IActionResult> Stats()
    {
        var companyId = await ActiveCompanyIdAsync();
        if (companyId is null)
            return BadRequest(new { message = "شرکت فعال انتخاب نشده است. از منوی بالای صفحه یک شرکت انتخاب کنید." });

        // داشبورد برای همه شخصی است: مجوز «View» (دیدن کل دستورهای شرکت) اینجا
        // باعث دیده‌شدن آمار دیگران نمی‌شود؛ فقط دستورهای خودِ کاربر شمرده می‌شوند.
        var myOrderIds = _db.WorkOrderAssignees.Where(a => a.UserId == MyUserId).Select(a => a.OrderId);

        var query = _db.WorkOrders.AsNoTracking()
            .Where(w => w.CompanyId == companyId.Value)
            .Where(w => w.OwnerUserId == MyUserId || myOrderIds.Contains(w.Id));

        var orders = await query.ToListAsync();
        var ids = orders.Select(o => o.Id).ToList();
        var asgs = ids.Count == 0
            ? new List<WorkOrderAssignee>()
            : await _db.WorkOrderAssignees.AsNoTracking().Where(a => ids.Contains(a.OrderId)).ToListAsync();

        var now = DateTime.Now;

        // وضعیت هر دستور از دید نتیجه (همان قوانین Tone کلاینت)
        string ToneOf(WorkOrder w)
        {
            var list = asgs.Where(a => a.OrderId == w.Id).ToList();
            var allDone = list.Count > 0 && list.All(a => a.Done == true);
            if (allDone)
            {
                var last = list.Max(a => a.RepliedAt) ?? DateTime.MaxValue;
                return last <= w.DueAt ? "ontime" : "latedone";
            }
            if (w.Status == "Closed") return "closednodone";
            if (w.DueAt < now) return "late";
            return "open";
        }

        var tones = orders.ToDictionary(o => o.Id, ToneOf);

        var open = orders.Count(o => o.Status == "Open");
        var overdue = orders.Count(o => o.Status == "Open" && tones[o.Id] == "late");
        var dueToday = orders.Count(o => o.Status == "Open" && o.DueAt.Date == now.Date);
        var doneOnTime = orders.Count(o => tones[o.Id] == "ontime");
        var doneLate = orders.Count(o => tones[o.Id] == "latedone");
        var closedNoDone = orders.Count(o => tones[o.Id] == "closednodone");
        // «در انتظار پاسخ» باید بر اساس «سطر دستور کار» باشد، نه تعداد گیرندگانی که پاسخ
        // نداده‌اند: دستوری که به سه نفر واگذار شده و هیچ‌کدام پاسخ نداده‌اند، «یک» دستور
        // در انتظار پاسخ است (مطابق شمارش دیگر کارت‌های همین داشبورد که همه سطری‌اند).
        var pendingOrderIds = asgs.Where(a => a.RepliedAt == null)
            .Select(a => a.OrderId).Distinct().ToHashSet();
        var pendingReply = orders.Count(o => o.Status == "Open" && pendingOrderIds.Contains(o.Id));

        // توزیع اولویت دستورهای باز
        var byPriority = orders.Where(o => o.Status == "Open")
            .GroupBy(o => o.Priority)
            .Select(g => new { Priority = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Priority).ToList();

        // عملکرد گیرندگان — فقط گیرندگانِ دستورهایی که خودِ کاربر صادر کرده است
        var scopeIds = orders.Where(o => o.OwnerUserId == MyUserId).Select(o => o.Id).ToHashSet();
        var dueMap = orders.Where(o => scopeIds.Contains(o.Id)).ToDictionary(o => o.Id, o => o.DueAt);
        var perAssignee = asgs.Where(a => scopeIds.Contains(a.OrderId))
            .GroupBy(a => new { a.UserId, a.Name })
            .Select(g => new
            {
                g.Key.UserId,
                g.Key.Name,
                Total = g.Count(),
                Done = g.Count(x => x.Done == true),
                OnTime = g.Count(x => x.Done == true && x.RepliedAt != null && x.RepliedAt <= dueMap[x.OrderId]),
                NotDone = g.Count(x => x.Done == false),
                Pending = g.Count(x => x.RepliedAt == null)
            })
            .OrderByDescending(x => x.Total).Take(15).ToList();

        // روند ۶ ماه اخیر شمسی — ایجادشده / انجام به‌موقع / انجام با تاخیر / بستهٔ بی‌نتیجه
        var pc = new PersianCalendar();
        var months = new List<object>();
        for (var i = 5; i >= 0; i--)
        {
            var refDate = now.AddMonths(-i);
            var py = pc.GetYear(refDate); var pm = pc.GetMonth(refDate);
            var monthOrders = orders.Where(o => pc.GetYear(o.CreatedAt) == py && pc.GetMonth(o.CreatedAt) == pm).ToList();
            months.Add(new
            {
                Year = py,
                Month = pm,
                Created = monthOrders.Count,
                OnTime = monthOrders.Count(o => tones[o.Id] == "ontime"),
                Late = monthOrders.Count(o => tones[o.Id] == "latedone"),
                NotDone = monthOrders.Count(o => tones[o.Id] == "closednodone"),
                Open = monthOrders.Count(o => tones[o.Id] is "open" or "late")
            });
        }

        return Ok(new
        {
            Scope = "personal",
            CompanyId = companyId.Value,
            Cards = new
            {
                Total = orders.Count,
                Open = open,
                Overdue = overdue,
                DueToday = dueToday,
                DoneOnTime = doneOnTime,
                DoneLate = doneLate,
                ClosedNoDone = closedNoDone,
                PendingReply = pendingReply
            },
            ByPriority = byPriority,
            ByStatus = new
            {
                Open = open,
                OverdueOpen = overdue,
                OnTime = doneOnTime,
                LateDone = doneLate,
                ClosedNoDone = closedNoDone
            },
            PerAssignee = perAssignee,
            Months = months
        });
    }

    // ================== گزارش عملکرد افراد (موج ۸) ==================

    /// <summary>
    /// محاسبهٔ عملکرد گیرندگانِ دستورهایی که کاربر جاری داده است —
    /// دریافتی، انجام به‌موقع/با تاخیر، انجام‌نشده، در انتظار، میانگین تاخیر و درصد به‌موقع.
    /// خروجی برای رتبه‌بندی مرتب می‌شود: درصد به‌موقع ↓ سپس تعداد کل ↓.
    /// <paramref name="days"/>: بازهٔ روزهای اخیر بر اساس تاریخ ایجاد دستور — 0 یعنی همه.
    /// </summary>
    private async Task<List<PerfRow>> BuildPerformanceAsync(int days, Paging.Request? pagination = null)
    {
        var q = _db.WorkOrders.AsNoTracking().Where(w => w.OwnerUserId == MyUserId);
        if (days > 0)
        {
            var from = DateTime.Now.AddDays(-days);
            q = q.Where(w => w.CreatedAt >= from);
        }
        var now = DateTime.Now;
        var source = from assignee in _db.WorkOrderAssignees.AsNoTracking()
                     join order in q on assignee.OrderId equals order.Id
                     select new { assignee.UserId, assignee.Name, assignee.Done, assignee.RepliedAt, order.DueAt, order.Status };
        var grouped = source.GroupBy(x => new { x.UserId, x.Name }).Select(g => new
        {
            g.Key.UserId, g.Key.Name,
            Total = g.Count(),
            Done = g.Count(x => x.Done == true),
            OnTime = g.Count(x => x.Done == true && x.RepliedAt != null && x.RepliedAt <= x.DueAt),
            NotDone = g.Count(x => x.Done == false),
            Pending = g.Count(x => x.RepliedAt == null && x.Status == "Open"),
            OverdueOpen = g.Count(x => x.RepliedAt == null && x.Status == "Open" && x.DueAt < now)
        });
        // Preserve .NET's midpoint-to-even rounding rather than SQL ROUND's
        // midpoint-away-from-zero behavior. All operands here are positive integers.
        var ranked = grouped.Select(g => new PerfRow
        {
            UserId = g.UserId, Name = g.Name, Total = g.Total, Done = g.Done,
            OnTime = g.OnTime, DoneLate = g.Done - g.OnTime, NotDone = g.NotDone,
            Pending = g.Pending, OverdueOpen = g.OverdueOpen,
            OnTimePercent = (int)(g.OnTime * 100L / g.Total +
                ((g.OnTime * 100L % g.Total * 2 > g.Total ||
                  (g.OnTime * 100L % g.Total * 2 == g.Total && g.OnTime * 100L / g.Total % 2 != 0)) ? 1 : 0))
        });
        var rows = await ranked.OrderByDescending(r => r.OnTimePercent).ThenByDescending(r => r.Total)
            .ThenBy(r => r.Name).ThenBy(r => r.UserId).ToPageListAsync(pagination);
        var userIds = rows.Select(r => r.UserId).ToList();
        // Date subtraction is provider-specific; hydrate only late replies for
        // the selected assignees, never the complete order/assignee tables.
        var delays = await source.Where(x => userIds.Contains(x.UserId) && x.Done == true &&
            x.RepliedAt != null && x.RepliedAt > x.DueAt)
            .Select(x => new { x.UserId, x.Name, x.RepliedAt, x.DueAt }).ToListAsync();
        var averages = delays.GroupBy(x => (x.UserId, x.Name)).ToDictionary(g => g.Key,
            g => Math.Round(g.Average(x => (x.RepliedAt!.Value - x.DueAt).TotalHours), 1));
        for (var i = 0; i < rows.Count; i++)
        {
            rows[i].Rank = (int)Math.Min(int.MaxValue, (long)(pagination?.Skip ?? 0) + i + 1);
            rows[i].AvgDelayHours = averages.TryGetValue((rows[i].UserId, rows[i].Name), out var avg) ? avg : 0;
        }
        return rows;
    }

    public class PerfRow
    {
        public int Rank { get; set; }
        public int UserId { get; set; }
        public string Name { get; set; } = "";
        public int Total { get; set; }          // کل دریافتی
        public int Done { get; set; }           // انجام‌شده (تایید گیرنده)
        public int OnTime { get; set; }         // انجام به‌موقع
        public int DoneLate { get; set; }       // انجام با تاخیر
        public int NotDone { get; set; }        // اعلام «انجام نشد»
        public int Pending { get; set; }        // در انتظار پاسخ (دستور باز)
        public int OverdueOpen { get; set; }    // در انتظار و گذشته از مهلت
        public double AvgDelayHours { get; set; } // میانگین تاخیر انجام‌های دیرهنگام (ساعت)
        public int OnTimePercent { get; set; }  // درصد به‌موقع از کل دریافتی
    }

    /// <summary>گزارش عملکرد افراد — فقط دستورهایی که کاربر جاری داده است. days=0 یعنی همهٔ بازه.</summary>
    [HttpGet("performance")]
    public async Task<IActionResult> Performance([FromQuery] int days = 0, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (!await HasAsync("View")) return Forbid();
        if (days is < 0 or > 3660) return BadRequest(new { message = "بازهٔ زمانی نامعتبر است." });
        return Ok(await Paging.ResultAsync(pagination => BuildPerformanceAsync(days, pagination), skip, take));
    }

    /// <summary>خروجی Excel/PDF گزارش عملکرد افراد — همان داده با رتبه‌بندی.</summary>
    [HttpGet("performance/export")]
    public async Task<IActionResult> PerformanceExport([FromQuery] string format = "xlsx", [FromQuery] int days = 0)
    {
        if (!await HasAsync("View")) return Forbid();
        if (days is < 0 or > 3660) return BadRequest(new { message = "بازهٔ زمانی نامعتبر است." });
        var rows = await BuildPerformanceAsync(days);

        var periodFa = days switch
        {
            0 => "همهٔ بازهٔ زمانی",
            30 => "۳۰ روز اخیر",
            90 => "۹۰ روز اخیر",
            365 => "سال اخیر",
            _ => $"{days} روز اخیر"
        };

        var spec = new Services.Export.ExportSpec
        {
            Title = "گزارش عملکرد افراد",
            Subtitle = "دستورهای کاری که من داده‌ام",
            Module = "دستور کار",
            FileBaseName = "workorders_performance",
            Landscape = true,
            Columns =
            {
                new("رتبه", Services.Export.ExportValueKind.Number, 35),
                new("نام"),
                new("دریافتی", Services.Export.ExportValueKind.Number, 52),
                new("انجام‌شده", Services.Export.ExportValueKind.Number, 58),
                new("به‌موقع", Services.Export.ExportValueKind.Number, 52),
                new("با تاخیر", Services.Export.ExportValueKind.Number, 52),
                new("انجام نشد", Services.Export.ExportValueKind.Number, 58),
                new("در انتظار", Services.Export.ExportValueKind.Number, 55),
                new("منقضی باز", Services.Export.ExportValueKind.Number, 58),
                new("میانگین تاخیر (ساعت)", Services.Export.ExportValueKind.Number, 78),
                new("٪ به‌موقع", Services.Export.ExportValueKind.Number, 52),
            }
        };
        spec.Meta.Add(new("بازه", periodFa));

        foreach (var r in rows)
        {
            var row = new Services.Export.ExportRow(
                r.Rank, r.Name, r.Total, r.Done, r.OnTime, r.DoneLate,
                r.NotDone, r.Pending, r.OverdueOpen, r.AvgDelayHours, r.OnTimePercent);
            if (r.OnTimePercent >= 80) row.Style = Services.Export.ExportRowStyle.Success;
            else if (r.OverdueOpen > 0) row.Style = Services.Export.ExportRowStyle.Danger;
            spec.Rows.Add(row);
        }

        spec.Summary.Add(new("تعداد افراد", rows.Count.ToString()));
        spec.Summary.Add(new("مجموع دستورهای محول‌شده", rows.Sum(r => r.Total).ToString()));
        spec.Summary.Add(new("مجموع انجام به‌موقع", rows.Sum(r => r.OnTime).ToString()));

        var isExcel = format.Equals("xlsx", StringComparison.OrdinalIgnoreCase);
        if (isExcel) foreach (var c in spec.Columns) c.Width = 0;
        var bytes = isExcel ? Services.Export.ExcelWriter.Build(spec) : Services.Export.PdfWriter.Build(spec);
        return File(bytes,
            isExcel ? "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" : "application/pdf",
            spec.FileName(isExcel ? "xlsx" : "pdf"));
    }

    // ================== کانبان: تغییر سریع اولویت ==================

    public class PriorityDto { public int Priority { get; set; } }
    /// <summary>
    /// تغییر سریع اولویت (درگ‌اند‌دراپ کانبان) — فقط دستوردهنده و فقط تا وقتی دستور باز است.
    /// </summary>
    [HttpPost("{id:int}/priority")]
    public async Task<IActionResult> SetPriority(int id, [FromBody] PriorityDto dto)
    {
        var wo = await _db.WorkOrders.FirstOrDefaultAsync(w => w.Id == id);
        if (wo == null) return NotFound(new { message = "دستور کار پیدا نشد." });
        if (wo.OwnerUserId != MyUserId) return Forbid();
        if (wo.Status != "Open") return BadRequest(new { message = "دستور بسته شده است؛ اولویت قابل تغییر نیست." });
        if (!WorkOrderPriority.IsValid(dto.Priority))
            return BadRequest(new { message = "اولویت انتخابی نامعتبر است." });

        if (wo.Priority != dto.Priority)
        {
            Log(id, "Edited", $"تغییر اولویت از «{WorkOrderPriority.ToFa(wo.Priority)}» به «{WorkOrderPriority.ToFa(dto.Priority)}» (کانبان)");
            wo.Priority = dto.Priority;
            await _db.SaveChangesAsync();
            await _notify.BroadcastChangedAsync("workorders");
        }
        return Ok();
    }

    // ================== برچسب/دسته‌بندی ==================

    /// <summary>
    /// برچسب‌های پرکاربرد کاربر جاری (از دستورهای خودش یا محول به خودش) —
    /// برای پیشنهاد در فرم و چیپ‌های فیلتر. مرتب بر اساس بیشترین استفاده.
    /// این endpoint ذاتاً Top-N است (۳۰ برچسب پرکاربرد) و صفحه‌بندی کلاسیک ندارد؛
    /// همیشه همان فهرست کامل برگزیده برمی‌گردد.
    /// </summary>
    [HttpGet("my-tags")]
    public async Task<IActionResult> MyTags()
    {
        var myOrderIds = _db.WorkOrderAssignees.Where(a => a.UserId == MyUserId).Select(a => a.OrderId);
        var tagStrings = await _db.WorkOrders.AsNoTracking()
            .Where(w => w.Tags != null && (w.OwnerUserId == MyUserId || myOrderIds.Contains(w.Id)))
            .Select(w => w.Tags!)
            .ToListAsync();

        var top = tagStrings.SelectMany(t => TagsToList(t))
            .GroupBy(t => t)
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key)
            .Take(30)
            .Select(g => g.Key)
            .ToList();
        return Ok(top);
    }

    // ================== قالب‌های آمادهٔ دستور کار (موج ۷) ==================

    public class TemplateDto
    {
        public string Name { get; set; } = "";
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public int Priority { get; set; } = WorkOrderPriority.Normal;
        public int Recurrence { get; set; } = WorkOrderRecurrence.None;
        public List<int> AssigneeUserIds { get; set; } = new();
        public List<string> ChecklistItems { get; set; } = new();
        public List<string> Tags { get; set; } = new();
    }

    /// <summary>فهرست قالب‌های کاربر جاری — پرکاربردها بالا. هر کاربر فقط قالب‌های خودش را می‌بیند.</summary>
    [HttpGet("templates")]
    public async Task<IActionResult> Templates([FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        var pagination = new Paging.Request(skip, take);
        if (!await HasAsync("Create")) return Forbid();
        var rows = await _db.WorkOrderTemplates.AsNoTracking()
            .Where(t => t.OwnerUserId == MyUserId)
            .OrderByDescending(t => t.UsageCount).ThenByDescending(t => t.Id)
            .ToPageListAsync(pagination);
        return Ok(pagination.Result(rows.Select(t => new
        {
            t.Id, t.Name, t.Title, t.Description, t.Priority, t.Recurrence, t.UsageCount, t.CreatedAt,
            AssigneeUserIds = (t.AssigneeUserIds ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => int.TryParse(s, out var v) ? v : 0).Where(v => v > 0).ToList(),
            ChecklistItems = (t.ChecklistItems ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim()).Where(s => s.Length > 0).ToList(),
            Tags = TagsToList(t.Tags)
        })));
    }

    /// <summary>ذخیرهٔ قالب جدید — حداکثر ۲۰ قالب برای هر کاربر تا فهرست شلوغ نشود.</summary>
    [HttpPost("templates")]
    public async Task<IActionResult> TemplateCreate([FromBody] TemplateDto dto)
    {
        if (!await HasAsync("Create")) return Forbid();

        var name = (dto.Name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name)) return BadRequest(new { message = "نام قالب را وارد کنید." });
        if (name.Length > 100) return BadRequest(new { message = "نام قالب حداکثر ۱۰۰ حرف است." });
        if (string.IsNullOrWhiteSpace(dto.Title)) return BadRequest(new { message = "عنوان دستور کار قالب خالی است." });
        if (!WorkOrderPriority.IsValid(dto.Priority)) return BadRequest(new { message = "اولویت انتخابی نامعتبر است." });
        if (!WorkOrderRecurrence.IsValid(dto.Recurrence)) return BadRequest(new { message = "الگوی تکرار نامعتبر است." });
        if (dto.ChecklistItems.Count > 50) return BadRequest(new { message = "چک‌لیست حداکثر ۵۰ آیتم می‌تواند داشته باشد." });

        var count = await _db.WorkOrderTemplates.CountAsync(t => t.OwnerUserId == MyUserId);
        if (count >= 20) return BadRequest(new { message = "حداکثر ۲۰ قالب می‌توانید ذخیره کنید؛ ابتدا یکی را حذف کنید." });

        // نام تکراری — به‌روزرسانی همان قالب (ذخیرهٔ مجدد با یک نام = جایگزینی)
        var existing = await _db.WorkOrderTemplates
            .FirstOrDefaultAsync(t => t.OwnerUserId == MyUserId && t.Name == name);

        var tpl = existing ?? new WorkOrderTemplate { OwnerUserId = MyUserId, Name = name };
        tpl.Title = dto.Title.Trim();
        // قالب هم سقف طول دارد؛ اگر شرح بلند (با تصویر نامه) از فرم آمده باشد،
        // همان پیش‌نمایش کوتاه ذخیره می‌شود تا ثبت قالب با خطای طول نشکند.
        tpl.Description = WorkOrderDescriptionStore.Preview(dto.Description);
        tpl.Priority = dto.Priority;
        tpl.Recurrence = dto.Recurrence;
        tpl.AssigneeUserIds = dto.AssigneeUserIds.Count > 0 ? string.Join(",", dto.AssigneeUserIds.Distinct()) : null;
        tpl.ChecklistItems = dto.ChecklistItems.Count > 0
            ? string.Join("\n", dto.ChecklistItems.Select(s => (s ?? "").Trim()).Where(s => s.Length > 0).Take(50))
            : null;
        tpl.Tags = NormalizeTags(dto.Tags);

        if (existing == null) _db.WorkOrderTemplates.Add(tpl);
        await _db.SaveChangesAsync();
        return Ok(new { tpl.Id, updated = existing != null });
    }

    /// <summary>ثبت یک‌بار استفاده از قالب — برای مرتب‌سازی پرکاربردها در بالا.</summary>
    [HttpPost("templates/{tplId:int}/used")]
    public async Task<IActionResult> TemplateUsed(int tplId)
    {
        var tpl = await _db.WorkOrderTemplates.FirstOrDefaultAsync(t => t.Id == tplId && t.OwnerUserId == MyUserId);
        if (tpl == null) return NotFound(new { message = "قالب پیدا نشد." });
        tpl.UsageCount++;
        await _db.SaveChangesAsync();
        return Ok();
    }

    /// <summary>حذف قالب — فقط سازنده.</summary>
    [HttpDelete("templates/{tplId:int}")]
    public async Task<IActionResult> TemplateDelete(int tplId)
    {
        var tpl = await _db.WorkOrderTemplates.FirstOrDefaultAsync(t => t.Id == tplId && t.OwnerUserId == MyUserId);
        if (tpl == null) return NotFound(new { message = "قالب پیدا نشد." });
        _db.WorkOrderTemplates.Remove(tpl);
        await _db.SaveChangesAsync();
        return Ok();
    }

    // ================== رشتهٔ گفتگو (کامنت) داخل دستور ==================

    /// <summary>لیست کامنت‌های یک دستور — قدیمی به جدید. کامنت‌های حذف‌شده با متن خالی می‌آیند تا رشتهٔ پاسخ‌ها نشکند.</summary>
    [HttpGet("{id:int}/comments")]
    public async Task<IActionResult> Comments(int id, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        var pagination = new Paging.Request(skip, take);
        if (!await CanSeeOrderAsync(id)) return Forbid();
        var items = await _db.WorkOrderComments.AsNoTracking()
            .Where(c => c.OrderId == id)
            .OrderBy(c => c.CreatedAt).ThenBy(c => c.Id)
            .ToPageListAsync(pagination);
        return Ok(pagination.Result(items.Select(c => new
        {
            c.Id, c.AuthorUserId, c.AuthorName,
            Text = c.IsDeleted ? "" : c.Text,
            c.ReplyToId, c.IsDeleted, c.CreatedAt, c.EditedAt
        })));
    }

    public class CommentDto { public string Text { get; set; } = ""; public int? ReplyToId { get; set; } }

    /// <summary>ثبت کامنت — دستوردهنده و گیرندگان؛ فقط تا وقتی دستور باز است. به بقیهٔ طرف‌ها اعلان می‌رود.</summary>
    [HttpPost("{id:int}/comments")]
    public async Task<IActionResult> AddComment(int id, [FromBody] CommentDto dto)
    {
        var wo = await _db.WorkOrders.FirstOrDefaultAsync(w => w.Id == id);
        if (wo == null) return NotFound();
        if (wo.Status != "Open") return BadRequest(new { message = "دستور بسته شده است؛ امکان ثبت کامنت نیست." });

        var isOwner = wo.OwnerUserId == MyUserId;
        var isAssignee = await _db.WorkOrderAssignees.AnyAsync(a => a.OrderId == id && a.UserId == MyUserId);
        if (!isOwner && !isAssignee) return Forbid();

        var text = (dto.Text ?? "").Trim();
        if (text.Length == 0) return BadRequest(new { message = "متن کامنت خالی است." });
        if (text.Length > 2000) return BadRequest(new { message = "متن کامنت حداکثر ۲۰۰۰ حرف است." });

        // پاسخ باید به کامنتی از همین دستور باشد
        if (dto.ReplyToId is > 0 &&
            !await _db.WorkOrderComments.AnyAsync(c => c.Id == dto.ReplyToId && c.OrderId == id))
            return BadRequest(new { message = "کامنت مرجع یافت نشد." });

        var me = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == MyUserId);
        var myName = UserDisplay.Name(me) is { Length: > 0 } n ? n : MyUsername;

        var cm = new WorkOrderComment
        {
            OrderId = id, AuthorUserId = MyUserId, AuthorName = myName,
            Text = text, ReplyToId = dto.ReplyToId is > 0 ? dto.ReplyToId : null
        };
        _db.WorkOrderComments.Add(cm);
        Log(id, "Commented", text.Length > 120 ? text[..120] + "…" : text);
        await _db.SaveChangesAsync();

        // اعلان به همهٔ طرف‌های دستور به‌جز نویسنده
        var others = await _db.WorkOrderAssignees.Where(a => a.OrderId == id && a.UserId != MyUserId)
            .Select(a => a.UserId).Distinct().ToListAsync();
        if (wo.OwnerUserId != MyUserId) others.Add(wo.OwnerUserId);
        var brief = text.Length > 80 ? text[..80] + "…" : text;
        foreach (var uid in others.Distinct())
            await _notify.SendAsync(uid, "کامنت جدید دستور کار 💬",
                $"{wo.Number} — «{wo.Title}»: {brief}",
                myName, "دستور کار", $"/work-orders?open={wo.Id}");
        await _notify.BroadcastChangedAsync("workorders");

        return Ok(new { cm.Id, cm.AuthorUserId, cm.AuthorName, cm.Text, cm.ReplyToId, cm.IsDeleted, cm.CreatedAt, cm.EditedAt });
    }

    /// <summary>ویرایش کامنت — فقط نویسنده و فقط تا وقتی دستور باز است.</summary>
    [HttpPut("comments/{commentId:int}")]
    public async Task<IActionResult> EditComment(int commentId, [FromBody] CommentDto dto)
    {
        var cm = await _db.WorkOrderComments.FindAsync(commentId);
        if (cm == null || cm.IsDeleted) return NotFound();
        if (cm.AuthorUserId != MyUserId) return Forbid();

        var wo = await _db.WorkOrders.FirstOrDefaultAsync(w => w.Id == cm.OrderId);
        if (wo == null || wo.Status != "Open") return BadRequest(new { message = "دستور بسته شده است." });

        var text = (dto.Text ?? "").Trim();
        if (text.Length == 0) return BadRequest(new { message = "متن کامنت خالی است." });
        if (text.Length > 2000) return BadRequest(new { message = "متن کامنت حداکثر ۲۰۰۰ حرف است." });

        cm.Text = text;
        cm.EditedAt = DateTime.Now;
        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("workorders");
        return Ok(new { cm.Id, cm.Text, cm.EditedAt });
    }

    /// <summary>حذف نرم کامنت — نویسنده یا دستوردهنده؛ متن پاک می‌شود اما جای آن در رشته می‌ماند.</summary>
    [HttpDelete("comments/{commentId:int}")]
    public async Task<IActionResult> DeleteComment(int commentId)
    {
        var cm = await _db.WorkOrderComments.FindAsync(commentId);
        if (cm == null || cm.IsDeleted) return NotFound();

        var wo = await _db.WorkOrders.FirstOrDefaultAsync(w => w.Id == cm.OrderId);
        if (wo == null) return NotFound();
        if (cm.AuthorUserId != MyUserId && wo.OwnerUserId != MyUserId) return Forbid();

        cm.IsDeleted = true;
        cm.Text = "";
        await _db.SaveChangesAsync();
        var recipients = await _db.WorkOrderAssignees.Where(a => a.OrderId == cm.OrderId && a.UserId != MyUserId)
            .Select(a => a.UserId).Distinct().ToListAsync();
        if (wo.OwnerUserId != MyUserId) recipients.Add(wo.OwnerUserId);
        foreach (var uid in recipients.Distinct())
            await _notify.SendAsync(uid, "کامنت حذف شد", $"{wo.Number} — کامنتی که در اعلان دیده‌اید حذف شده و قابل مشاهده نیست.", MyUsername, "دستور کار", $"/work-orders?open={wo.Id}");
        await _notify.BroadcastChangedAsync("workorders");
        return Ok();
    }

    // ================== خروجی Excel / PDF ==================

    /// <summary>
    /// خروجی گرفتن از لیست دستورها — همان فیلترهای لیست + انتخاب تب.
    /// tab: mine | assigned | archive — format: xlsx | pdf
    /// </summary>
    /// <summary>
    /// خلاصهٔ «تصمیم دستوردهنده» برای خروجی متنی: تعداد تأیید/رد/بی‌تصمیم و نام افراد.
    /// تصمیم روی هر پاسخ (WorkOrderAssignee.OwnerDecision) ثبت می‌شود.
    /// </summary>
    private static string DecisionSummaryText(List<WorkOrderAssignee> assignees)
    {
        if (assignees.Count == 0) return "—";
        var approved = assignees.Where(a => a.OwnerDecision == "Approved").ToList();
        var rejected = assignees.Where(a => a.OwnerDecision == "Rejected").ToList();
        var awaiting = assignees.Where(a => a.RepliedAt != null
                                            && a.OwnerDecision != "Approved"
                                            && a.OwnerDecision != "Rejected").ToList();
        if (approved.Count == 0 && rejected.Count == 0 && awaiting.Count == 0)
            return "در انتظار پاسخ";
        var parts = new List<string>();
        if (approved.Count > 0) parts.Add($"{approved.Count} تأیید ({string.Join("، ", approved.Select(a => a.Name))})");
        if (rejected.Count > 0) parts.Add($"{rejected.Count} رد ({string.Join("، ", rejected.Select(a => a.Name))})");
        if (awaiting.Count > 0) parts.Add($"{awaiting.Count} بی‌تصمیم ({string.Join("، ", awaiting.Select(a => a.Name))})");
        return string.Join(" | ", parts);
    }

    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] string tab = "mine", [FromQuery] string format = "xlsx",
        [FromQuery] ListFilterDto? filter = null)
    {
        if (!await HasAsync("View")) return Forbid();
        filter ??= new ListFilterDto();

        var myOrderIds = _db.WorkOrderAssignees.Where(a => a.UserId == MyUserId).Select(a => a.OrderId);
        var (query, tabFa) = tab switch
        {
            "assigned" => (_db.WorkOrders.Where(w => myOrderIds.Contains(w.Id) && w.Status == "Open"), "محول به من"),
            "archive" => (_db.WorkOrders.Where(w => w.Status == "Closed" && (w.OwnerUserId == MyUserId || myOrderIds.Contains(w.Id))), "بایگانی"),
            _ => (_db.WorkOrders.Where(w => w.OwnerUserId == MyUserId && w.Status == "Open"), "دستورهای من")
        };

        var orders = await ApplyFilter(query, filter, myAssignmentOnly: tab == "assigned")
            .OrderByDescending(w => w.Priority).ThenByDescending(w => w.Id).ToListAsync();
        var ids = orders.Select(o => o.Id).ToList();
        var asgs = await _db.WorkOrderAssignees.AsNoTracking().Where(a => ids.Contains(a.OrderId)).ToListAsync();
        var clStats = await _db.WorkOrderChecklistItems.Where(c => ids.Contains(c.OrderId))
            .GroupBy(c => c.OrderId)
            .Select(g => new { g.Key, Total = g.Count(), Done = g.Count(x => x.IsDone) }).ToListAsync();

        var spec = new Services.Export.ExportSpec
        {
            Title = "گزارش دستور کار",
            Subtitle = tabFa,
            Module = "دستور کار",
            FileBaseName = $"workorders_{tab}",
            Landscape = true,
            Columns =
            {
                // عرض‌ها بر حسب point در PDF (مثل DocArchiveExportService) — صفر یعنی نسبی
                new("شماره", Services.Export.ExportValueKind.Text, 70),
                new("عنوان") { Wrap = true },
                new("برچسب‌ها", Services.Export.ExportValueKind.Text, 70) { Wrap = true },
                new("اولویت", Services.Export.ExportValueKind.Text, 45),
                new("تکرار", Services.Export.ExportValueKind.Text, 48),
                new("دستوردهنده", Services.Export.ExportValueKind.Text, 80),
                new("مهلت", Services.Export.ExportValueKind.DateTime, 85),
                new("گیرندگان") { Wrap = true },
                new("پاسخ", Services.Export.ExportValueKind.Text, 50),
                new("چک‌لیست", Services.Export.ExportValueKind.Text, 55),
                new("وضعیت", Services.Export.ExportValueKind.Text, 70),
                // تصمیم دستوردهنده روی پاسخ گیرندگان — همان ستونی که در لیست نمایش داده می‌شود
                new("تصمیم دستوردهنده", Services.Export.ExportValueKind.Text, 95) { Wrap = true },
            }
        };

        if (!string.IsNullOrWhiteSpace(filter.Q)) spec.Meta.Add(new("جستجو", filter.Q));
        if (filter.Priority is >= 0) spec.Meta.Add(new("اولویت", WorkOrderPriority.ToFa(filter.Priority.Value)));
        if (!string.IsNullOrWhiteSpace(filter.Tag)) spec.Meta.Add(new("برچسب", filter.Tag));

        var now = DateTime.Now;
        foreach (var w in orders)
        {
            var list = asgs.Where(a => a.OrderId == w.Id).ToList();
            var replied = list.Count(a => a.RepliedAt != null);
            var cl = clStats.FirstOrDefault(c => c.Key == w.Id);
            var allDone = list.Count > 0 && list.All(a => a.Done == true);
            var lastReply = allDone ? list.Max(a => a.RepliedAt) : null;
            var status = allDone
                ? (lastReply <= w.DueAt ? "انجام به‌موقع" : "انجام با تاخیر")
                : w.Status == "Closed" ? "بسته شده"
                : w.DueAt < now ? "گذشته از مهلت"
                : w.DueAt.Date == now.Date ? "مهلت امروز" : "در جریان";

            var row = new Services.Export.ExportRow(
                w.Number, w.Title,
                string.Join("، ", TagsToList(w.Tags)),
                WorkOrderPriority.ToFa(w.Priority),
                WorkOrderRecurrence.ToFa(w.Recurrence),
                w.OwnerName, w.DueAt,
                string.Join("، ", list.Select(a => a.Name)),
                $"{replied} از {list.Count}",
                cl == null ? "—" : $"{cl.Done} از {cl.Total}",
                status,
                DecisionSummaryText(list));
            if (status == "گذشته از مهلت") row.Style = Services.Export.ExportRowStyle.Danger;
            else if (status == "انجام به‌موقع") row.Style = Services.Export.ExportRowStyle.Success;
            spec.Rows.Add(row);
        }

        spec.Summary.Add(new("تعداد کل", spec.Rows.Count.ToString()));
        spec.Summary.Add(new("گذشته از مهلت", orders.Count(o => o.Status == "Open" && o.DueAt < now).ToString()));

        var isExcel = format.Equals("xlsx", StringComparison.OrdinalIgnoreCase);
        // عرض ستون در اکسل کاراکتری است؛ صفر می‌کنیم تا خودِ ExcelWriter بر اساس محتوا تنظیم کند
        if (isExcel) foreach (var c in spec.Columns) c.Width = 0;
        var bytes = isExcel ? Services.Export.ExcelWriter.Build(spec) : Services.Export.PdfWriter.Build(spec);
        return File(bytes,
            isExcel ? "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" : "application/pdf",
            spec.FileName(isExcel ? "xlsx" : "pdf"));
    }

    // ================== تاریخچه (بند ۱۷ و ۱۸) ==================
    [HttpGet("{id:int}/logs")]
    public async Task<IActionResult> Logs(int id, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (!await CanSeeOrderAsync(id)) return Forbid();
        return Ok(await Paging.ResultAsync(_db.WorkOrderLogs.Where(l => l.OrderId == id).OrderBy(l => l.Id)
            .Select(l => new { l.Id, l.ActorName, l.Action, l.Text, l.CreatedAt }), skip, take));
    }

    // ================== پیوست‌ها (بند ۴) ==================
    [HttpGet("{id:int}/attachments")]
    public async Task<IActionResult> Attachments(int id, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        var pagination = new Paging.Request(skip, take);
        if (!await CanSeeOrderAsync(id)) return Forbid();
        var rows = await _db.AppAttachments.Where(a => a.Module == "WorkOrders" && a.RefId == id).OrderBy(x => x.Id)
            .Select(a => new { a.Id, a.FileName, a.UploaderName, a.UploadedAt, a.FilePath, a.Data })
            .ToPageListAsync(pagination);
        return Ok(pagination.Result(rows.Select(a => new { a.Id, a.FileName, a.UploaderName, a.UploadedAt,
            Size = a.FilePath is not null ? _store.Size(a.FilePath) : (long)a.Data.Length })));
    }

    [HttpPost("{id:int}/attachments")]
    [DisableRequestSizeLimit]
    public async Task<IActionResult> Upload(int id, IFormFile file)
    {
        if (!await CanSeeOrderAsync(id)) return Forbid();
        var wo = await _db.WorkOrders.FirstOrDefaultAsync(w => w.Id == id);
        if (wo == null) return NotFound(new { message = "دستور کار پیدا نشد." });
        if (file == null || file.Length == 0) return BadRequest(new { message = "فایلی انتخاب نشده است." });
        if (file.Length > 10 * 1024 * 1024) return BadRequest(new { message = "حداکثر حجم فایل ۱۰ مگابایت است." });

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        ms.Position = 0;
        // فایل‌های دستور کار در wwwroot/uploads/WorkOrders/{id}/ ذخیره می‌شوند.
        var relPath = await _store.SaveAsync("WorkOrders", id, ms, file.FileName);
        _db.AppAttachments.Add(new AppAttachment
        {
            Module = "WorkOrders",
            RefId = id,
            FileName = Path.GetFileName(file.FileName),
            ContentType = file.ContentType ?? "application/octet-stream",
            FilePath = relPath,
            Data = Array.Empty<byte>(),
            UploaderName = MyUsername,
            UploaderUserId = MyUserId
        });
        await _db.SaveChangesAsync();
        return Ok();
    }

    [HttpGet("attachments/{attId:int}/download")]
    public async Task<IActionResult> Download(int attId)
    {
        var att = await _db.AppAttachments.FirstOrDefaultAsync(a => a.Id == attId && a.Module == "WorkOrders");
        if (att == null) return NotFound();
        if (!await CanSeeOrderAsync(att.RefId)) return Forbid();
        var bytes = _store.ReadBytes(att.FilePath) ?? (att.Data is { Length: > 0 } ? att.Data : null);
        if (bytes is null) return NotFound(new { message = "فایل در دسترس نیست." });
        return File(bytes, att.ContentType, att.FileName);
    }
    /// <summary>همهٔ دستورهای متصل به گره‌های نگار ذهنی یک پروژه، همراه با عنوان گره و مسیر زیرنگاشت.</summary>
    [HttpGet("by-project/{projectId:int}/mindflow-linked")]
    public async Task<IActionResult> MindFlowLinkedByProject(int projectId)
    {
        if (!await HasAsync("View")) return Forbid();
        var companyId = await ActiveCompanyIdAsync();
        if (companyId is null) return BadRequest(new { message = "شرکت فعال انتخاب نشده است." });
        var project = await _db.WorkProjects.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == projectId && p.CompanyId == companyId.Value);
        if (project is null) return NotFound(new { message = "پروژه پیدا نشد." });
        var canViewMindFlow = project.OwnerUserId == MyUserId
            || await _db.WorkProjectMembers.AnyAsync(m => m.ProjectId == projectId && m.UserId == MyUserId)
            || await _db.WorkProjectMindFlowMembers.AnyAsync(m => m.ProjectId == projectId && m.UserId == MyUserId);
        if (!canViewMindFlow) return Forbid();
        if (string.IsNullOrWhiteSpace(project.MindFlowJson)) return Ok(new List<WorkProjectMindFlowLinkedOrderDto>());

        WorkProjectMindFlowDto? map;
        try { map = System.Text.Json.JsonSerializer.Deserialize<WorkProjectMindFlowDto>(project.MindFlowJson, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)); }
        catch (System.Text.Json.JsonException) { return Conflict(new { message = "دادهٔ نگار ذهنی این پروژه قابل خواندن نیست." }); }
        if (map?.Nodes is null) return Ok(new List<WorkProjectMindFlowLinkedOrderDto>());

        var links = EnumerateMindFlowNodes(map)
            .SelectMany(x => WorkProjectsController.NodeWorkOrderIds(x.Node).Select(orderId => new { OrderId = orderId, x.Node.Id, x.Node.Text, x.Path }))
            .ToList();
        if (links.Count == 0) return Ok(new List<WorkProjectMindFlowLinkedOrderDto>());
        var orderIds = links.Select(x => x.OrderId).Distinct().ToList();
        var orders = await _db.WorkOrders.AsNoTracking()
            .Where(w => w.CompanyId == companyId.Value && orderIds.Contains(w.Id))
            .ToDictionaryAsync(w => w.Id);
        var linkedProjectIds = orders.Values.Where(w => w.ProjectId.HasValue).Select(w => w.ProjectId!.Value).Distinct().ToList();
        var linkedProjects = new Dictionary<int, (string Number, string Title)>();
        if (linkedProjectIds.Count > 0)
            linkedProjects = await _db.WorkProjects.IgnoreQueryFilters().AsNoTracking()
                .Where(p => p.CompanyId == companyId.Value && linkedProjectIds.Contains(p.Id))
                .Select(p => new { p.Id, p.Number, p.Title })
                .ToDictionaryAsync(p => p.Id, p => (Number: p.Number, Title: p.Title));

        var result = new List<WorkProjectMindFlowLinkedOrderDto>();
        foreach (var link in links)
        {
            if (!orders.TryGetValue(link.OrderId, out var order)) continue;
            linkedProjects.TryGetValue(order.ProjectId ?? 0, out var linkedProject);
            result.Add(new WorkProjectMindFlowLinkedOrderDto
            {
                Id = order.Id, Number = order.Number, Title = order.Title, OwnerName = order.OwnerName,
                Status = order.Status, DueAt = order.DueAt, Priority = order.Priority, Weight = order.Weight,
                ProjectId = order.ProjectId, ProjectNumber = linkedProject.Number, ProjectTitle = linkedProject.Title,
                NodeId = link.Id, NodeTitle = link.Text, SubFlowPath = string.Join(",", link.Path)
            });
        }
        return Ok(result.OrderByDescending(x => x.Priority).ThenBy(x => x.DueAt).ThenBy(x => x.Number).ToList());
    }

    /// <summary>فهرست دستورهای شرکت برای اتصال به گره‌های MindFlow؛ فقط کاربران دارای مجوز مشاهدهٔ دستورکار.</summary>
    [HttpGet("mindflow-lookup")]
    public async Task<IActionResult> MindFlowLookup([FromQuery] string? q, [FromQuery] int? take, [FromQuery] int skip = 0)
    {
        if (!await HasAsync("View")) return Forbid();
        var companyId = await ActiveCompanyIdAsync();
        if (companyId is null) return BadRequest(new { message = "شرکت فعال انتخاب نشده است." });
        var query = _db.WorkOrders.Where(w => w.CompanyId == companyId.Value);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(w => EF.Functions.Like(w.Number, $"%{term}%") || EF.Functions.Like(w.Title, $"%{term}%"));
        }
        var n = Math.Clamp(take is > 0 ? take.Value : 200, 1, 500);
        var offset = Math.Max(0, skip);
        return Ok(await BuildList(query.OrderByDescending(w => w.Priority).ThenByDescending(w => w.Id).Skip(offset).Take(n)));
    }

    /// <summary>MindFlowهایی که این دستور به آن‌ها وصل است و کاربر مجاز به دیدنشان است.</summary>
    [HttpGet("{id:int}/mindflows")]
    public async Task<IActionResult> MindFlowsForOrder(int id)
    {
        if (!await CanSeeOrderAsync(id)) return Forbid();
        var companyId = await ActiveCompanyIdAsync();
        if (companyId is null) return BadRequest(new { message = "شرکت فعال انتخاب نشده است." });
        var orderExists = await _db.WorkOrders.AsNoTracking().AnyAsync(w => w.Id == id && w.CompanyId == companyId.Value);
        if (!orderExists) return NotFound(new { message = "دستور کار پیدا نشد." });

        var projects = await _db.WorkProjects.AsNoTracking()
            .Where(p => p.CompanyId == companyId.Value && p.MindFlowJson != null)
            .Select(p => new { p.Id, p.Number, p.Title, p.OwnerUserId, p.MindFlowJson }).ToListAsync();
        var projectMemberIds = await _db.WorkProjectMembers.AsNoTracking()
            .Where(m => m.UserId == MyUserId).Select(m => m.ProjectId).ToListAsync();
        var directMemberIds = await _db.WorkProjectMindFlowMembers.AsNoTracking()
            .Where(m => m.UserId == MyUserId).Select(m => m.ProjectId).ToListAsync();
        var result = new List<WorkOrderMindFlowLinkDto>();
        foreach (var project in projects)
        {
            if (project.OwnerUserId != MyUserId && !projectMemberIds.Contains(project.Id) && !directMemberIds.Contains(project.Id)) continue;
            WorkProjectMindFlowDto? map;
            try { map = System.Text.Json.JsonSerializer.Deserialize<WorkProjectMindFlowDto>(project.MindFlowJson!, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)); }
            catch (System.Text.Json.JsonException) { continue; }
            if (map is null) continue;
            foreach (var (node, path) in EnumerateMindFlowNodes(map))
            {
                if (node.WorkOrderId != id && !(node.WorkOrderIds?.Contains(id) ?? false)) continue;
                result.Add(new WorkOrderMindFlowLinkDto
                {
                    ProjectId = project.Id, ProjectNumber = project.Number, ProjectTitle = project.Title,
                    MapTitle = map.Title, NodeId = node.Id, NodeText = node.Text, SubFlowPath = path
                });
            }
        }

        var standaloneMaps = await _db.StandaloneMindFlows.AsNoTracking()
            .Where(m => m.CompanyId == companyId.Value && (m.OwnerUserId == MyUserId
                || _db.StandaloneMindFlowMembers.Any(s => s.MindFlowId == m.Id && s.UserId == MyUserId)))
            .Select(m => new { m.Id, m.Title, m.MapJson }).ToListAsync();
        foreach (var standalone in standaloneMaps)
        {
            WorkProjectMindFlowDto? map;
            try { map = System.Text.Json.JsonSerializer.Deserialize<WorkProjectMindFlowDto>(standalone.MapJson, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)); }
            catch (System.Text.Json.JsonException) { continue; }
            if (map is null) continue;
            foreach (var (node, path) in EnumerateMindFlowNodes(map))
            {
                if (node.WorkOrderId != id && !(node.WorkOrderIds?.Contains(id) ?? false)) continue;
                result.Add(new WorkOrderMindFlowLinkDto
                {
                    StandaloneMindFlowId = standalone.Id, ProjectNumber = "مستقل", ProjectTitle = standalone.Title,
                    MapTitle = standalone.Title, NodeId = node.Id, NodeText = node.Text, SubFlowPath = path
                });
            }
        }
        return Ok(result);
    }

    private static IEnumerable<(WorkProjectMindFlowNodeDto Node, List<Guid> Path)> EnumerateMindFlowNodes(
        WorkProjectMindFlowDto map, List<Guid>? path = null)
    {
        path ??= new();
        if (map.Nodes is null) yield break;
        foreach (var node in map.Nodes)
        {
            if (node is null) continue;
            yield return (node, path);
            if (node.ChildMindFlow is not null)
            {
                var childPath = new List<Guid>(path) { node.Id };
                foreach (var child in EnumerateMindFlowNodes(node.ChildMindFlow, childPath)) yield return child;
            }
        }
    }


    public class ChecklistReorderDto { public List<int> ItemIds { get; set; } = new(); }

    /// <summary>تغییر ترتیب آیتم‌های چک‌لیست اصلی — فقط دستوردهنده و فقط تا وقتی دستور باز است.</summary>
    [HttpPost("{id:int}/checklist/reorder")]
    public async Task<IActionResult> ChecklistReorder(int id, [FromBody] ChecklistReorderDto dto)
    {
        var wo = await _db.WorkOrders.FirstOrDefaultAsync(w => w.Id == id);
        if (wo == null) return NotFound(new { message = "دستور کار پیدا نشد." });
        if (wo.OwnerUserId != MyUserId) return Forbid();
        if (wo.Status == "Closed") return BadRequest(new { message = "دستور کار بسته شده است." });
        if (dto?.ItemIds is null) return BadRequest(new { message = "فهرست ترتیب ارسال نشده است." });

        var items = await _db.WorkOrderChecklistItems.Where(c => c.OrderId == id)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Id).ToListAsync();
        if (dto.ItemIds.Count != items.Count || dto.ItemIds.Distinct().Count() != items.Count
            || !dto.ItemIds.ToHashSet().SetEquals(items.Select(i => i.Id)))
            return BadRequest(new { message = "فهرست باید شامل تمام آیتم‌های فعلی چک‌لیست، بدون تکرار باشد." });

        var byId = items.ToDictionary(i => i.Id);
        for (var index = 0; index < dto.ItemIds.Count; index++)
            byId[dto.ItemIds[index]].SortOrder = index;
        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("workorders");
        return Ok(new { count = items.Count });
    }


}
