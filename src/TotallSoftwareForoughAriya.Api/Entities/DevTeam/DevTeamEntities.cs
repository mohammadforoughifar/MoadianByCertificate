using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Inventory.Api.Data;

// ============================================================
//  ماژول «میز کار توسعه» — مدیریت تیم نرم‌افزار
//  پیشوند جدول‌ها: Dt*  (Dev Team)
// ============================================================

/// <summary>اولویت تسک توسعه.</summary>
public static class DtPriority
{
    public const int Low = 0;
    public const int Normal = 1;
    public const int High = 2;
    public const int Critical = 3;

    public static bool IsValid(int p) => p is >= Low and <= Critical;
    public static string ToFa(int p) => p switch
    {
        Low => "کم",
        High => "بالا",
        Critical => "بحرانی",
        _ => "عادی"
    };
}

/// <summary>نوع تسک.</summary>
public static class DtTaskType
{
    public const string Feature = "Feature";
    public const string Bug = "Bug";
    public const string Improvement = "Improvement";
    public const string Spike = "Spike";
    public const string Chore = "Chore";
    public const string Docs = "Docs";

    public static readonly string[] All = { Feature, Bug, Improvement, Spike, Chore, Docs };

    public static bool IsValid(string? t) =>
        !string.IsNullOrWhiteSpace(t) && All.Contains(t, StringComparer.OrdinalIgnoreCase);

    public static string ToFa(string? t) => (t ?? "") switch
    {
        Feature => "فیچر",
        Bug => "باگ",
        Improvement => "بهبود",
        Spike => "تحقیق",
        Chore => "کار فنی",
        Docs => "مستند",
        _ => t ?? "—"
    };
}

/// <summary>شدت مشکل.</summary>
public static class DtProblemSeverity
{
    public const string Error = "Error";
    public const string Warning = "Warning";
    public const string Info = "Info";

    public static readonly string[] All = { Error, Warning, Info };
    public static bool IsValid(string? s) =>
        !string.IsNullOrWhiteSpace(s) && All.Contains(s, StringComparer.OrdinalIgnoreCase);

    public static string ToFa(string? s) => (s ?? "") switch
    {
        Error => "خطا",
        Warning => "هشدار",
        Info => "اطلاع",
        _ => s ?? "—"
    };
}

/// <summary>وضعیت مشکل.</summary>
public static class DtProblemStatus
{
    public const string Open = "Open";
    public const string Investigating = "Investigating";
    public const string Resolved = "Resolved";
    public const string WontFix = "WontFix";

    public static readonly string[] All = { Open, Investigating, Resolved, WontFix };
    public static bool IsValid(string? s) =>
        !string.IsNullOrWhiteSpace(s) && All.Contains(s, StringComparer.OrdinalIgnoreCase);

    public static string ToFa(string? s) => (s ?? "") switch
    {
        Open => "باز",
        Investigating => "در حال بررسی",
        Resolved => "رفع‌شده",
        WontFix => "رفع نمی‌شود",
        _ => s ?? "—"
    };
}

/// <summary>نوع لینک گیت.</summary>
public static class DtGitLinkKind
{
    public const string Commit = "Commit";
    public const string Branch = "Branch";
    public const string PullRequest = "PullRequest";
    public const string Tag = "Tag";

    public static readonly string[] All = { Commit, Branch, PullRequest, Tag };
    public static bool IsValid(string? k) =>
        !string.IsNullOrWhiteSpace(k) && All.Contains(k, StringComparer.OrdinalIgnoreCase);

    public static string ToFa(string? k) => (k ?? "") switch
    {
        Commit => "کامیت",
        Branch => "شاخه",
        PullRequest => "درخواست ادغام",
        Tag => "تگ",
        _ => k ?? "—"
    };
}

/// <summary>وضعیت اسپرینت.</summary>
public static class DtSprintStatus
{
    public const string Planned = "Planned";
    public const string Active = "Active";
    public const string Closed = "Closed";

    public static readonly string[] All = { Planned, Active, Closed };
    public static bool IsValid(string? s) =>
        !string.IsNullOrWhiteSpace(s) && All.Contains(s, StringComparer.OrdinalIgnoreCase);

    public static string ToFa(string? s) => (s ?? "") switch
    {
        Planned => "برنامه‌ریزی",
        Active => "فعال",
        Closed => "بسته",
        _ => s ?? "—"
    };
}

// -------------------- جداول --------------------

/// <summary>وضعیت workflow قابل‌تعریف توسط مدیر.</summary>
public class DtWorkflowStatus
{
    public int Id { get; set; }

    /// <summary>کلید پایدار انگلیسی (backlog, doing, …)</summary>
    [MaxLength(40)]
    public string Key { get; set; } = "";

    [MaxLength(80)]
    public string NameFa { get; set; } = "";

    /// <summary>کد رنگ hex مثل #6366f1</summary>
    [MaxLength(20)]
    public string Color { get; set; } = "#64748b";

    public int SortOrder { get; set; }

    /// <summary>وضعیت شروع پیش‌فرض برای تسک جدید</summary>
    public bool IsInitial { get; set; }

    /// <summary>وضعیت پایانی (انجام‌شده)</summary>
    public bool IsDone { get; set; }

    /// <summary>وضعیت مسدود</summary>
    public bool IsBlocked { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>سقف WIP ستون (null یا ۰ = بدون سقف). فقط ریشه‌ها شمرده می‌شوند.</summary>
    public int? WipLimit { get; set; }
}

/// <summary>کاتالوگ ماژول‌های نرم‌افزار پلتفرم.</summary>
public class DtProductModule
{
    public int Id { get; set; }

    /// <summary>کلید پایدار: Hr, Office, Sales, …</summary>
    [MaxLength(40)]
    public string Key { get; set; } = "";

    [MaxLength(120)]
    public string NameFa { get; set; } = "";

    [MaxLength(40)]
    public string? Icon { get; set; }

    [MaxLength(20)]
    public string Color { get; set; } = "#4f46e5";

    [MaxLength(300)]
    public string? Description { get; set; }

    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>اسپرینت / بازه زمانی.</summary>
public class DtSprint
{
    public int Id { get; set; }

    [MaxLength(120)]
    public string Name { get; set; } = "";

    [MaxLength(500)]
    public string? Goal { get; set; }

    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }

    /// <summary>Planned | Active | Closed</summary>
    [MaxLength(20)]
    public string Status { get; set; } = DtSprintStatus.Planned;

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public int CreatedByUserId { get; set; }

    [MaxLength(150)]
    public string CreatedByName { get; set; } = "";
}

/// <summary>تسک توسعه.</summary>
public class DtTask
{
    public int Id { get; set; }

    /// <summary>شماره نمایشی: DT-۱۴۰۵-۰۰۰۱</summary>
    [MaxLength(30)]
    public string Number { get; set; } = "";

    [MaxLength(250)]
    public string Title { get; set; } = "";

    /// <summary>شرح HTML / متن</summary>
    public string? Description { get; set; }

    /// <summary>Feature | Bug | Improvement | Spike | Chore | Docs</summary>
    [MaxLength(30)]
    public string Type { get; set; } = DtTaskType.Feature;

    public int Priority { get; set; } = DtPriority.Normal;

    public int StatusId { get; set; }
    public DtWorkflowStatus? Status { get; set; }

    public int? ModuleId { get; set; }
    public DtProductModule? Module { get; set; }

    public int? SprintId { get; set; }
    public DtSprint? Sprint { get; set; }

    public int? AssigneeUserId { get; set; }

    [MaxLength(150)]
    public string? AssigneeName { get; set; }

    public int ReporterUserId { get; set; }

    [MaxLength(150)]
    public string ReporterName { get; set; } = "";

    public DateTime? DueAt { get; set; }

    /// <summary>تخمین ساعت</summary>
    [Column(TypeName = "decimal(10,2)")]
    public decimal? EstimateHours { get; set; }

    /// <summary>ساعت واقعی جمع‌شده</summary>
    [Column(TypeName = "decimal(10,2)")]
    public decimal SpentHours { get; set; }

    /// <summary>درصد پیشرفت ۰–۱۰۰</summary>
    public int Progress { get; set; }

    /// <summary>برچسب‌ها با کاما: api,ui,urgent</summary>
    [MaxLength(400)]
    public string? Tags { get; set; }

    /// <summary>دستور کار متصل‌شده (اختیاری) — SourceModule=DevTeam روی WorkOrder</summary>
    public int? WorkOrderId { get; set; }

    [MaxLength(30)]
    public string? WorkOrderNumber { get; set; }

    /// <summary>تسک والد — اگر پر باشد این رکورد ساب‌تسک است (فقط یک سطح توصیه می‌شود؛ حداکثر ۳ سطح).</summary>
    public int? ParentTaskId { get; set; }
    public DtTask? ParentTask { get; set; }

    /// <summary>ترتیب نمایش بین خواهر/برادرها</summary>
    public int SortOrder { get; set; }

    /// <summary>شروع تایمر زنده (null = متوقف).</summary>
    public DateTime? TimerStartedAt { get; set; }
    public int? TimerStartedByUserId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? UpdatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public int? DeletedByUserId { get; set; }

    public List<DtTask> SubTasks { get; set; } = new();
    public List<DtTaskComment> Comments { get; set; } = new();
    public List<DtTaskActivity> Activities { get; set; } = new();
    public List<DtTaskGitLink> GitLinks { get; set; } = new();
    public List<DtTimeEntry> TimeEntries { get; set; } = new();

    /// <summary>وابستگی‌هایی که این تسک را مسدود می‌کنند (باید اول انجام شوند).</summary>
    public List<DtTaskDependency> BlockedByLinks { get; set; } = new();

    /// <summary>تسک‌هایی که این تسک آن‌ها را مسدود کرده.</summary>
    public List<DtTaskDependency> BlockingLinks { get; set; } = new();
}

/// <summary>
/// وابستگی بین دو تسک.
/// معنی: TaskId تا وقتی DependsOnTaskId تمام نشده «مسدود منطقی» است.
/// Kind فعلاً فقط Blocks (قابل گسترش: RelatesTo).
/// </summary>
public class DtTaskDependency
{
    public int Id { get; set; }

    /// <summary>تسکی که منتظر است / مسدود شده</summary>
    public int TaskId { get; set; }
    public DtTask? Task { get; set; }

    /// <summary>تسکی که باید اول تمام شود</summary>
    public int DependsOnTaskId { get; set; }
    public DtTask? DependsOnTask { get; set; }

    /// <summary>Blocks | RelatesTo</summary>
    [MaxLength(20)]
    public string Kind { get; set; } = "Blocks";

    public int CreatedByUserId { get; set; }

    [MaxLength(150)]
    public string CreatedByName { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

public static class DtDependencyKind
{
    public const string Blocks = "Blocks";
    public const string RelatesTo = "RelatesTo";
    public static readonly string[] All = { Blocks, RelatesTo };
    public static bool IsValid(string? k) =>
        !string.IsNullOrWhiteSpace(k) && All.Contains(k, StringComparer.OrdinalIgnoreCase);
    public static string ToFa(string? k) => (k ?? "") switch
    {
        Blocks => "مسدودکننده",
        RelatesTo => "مرتبط",
        _ => k ?? "—"
    };
}

/// <summary>کامنت روی تسک.</summary>
public class DtTaskComment
{
    public int Id { get; set; }
    public int TaskId { get; set; }
    public DtTask? Task { get; set; }

    public int AuthorUserId { get; set; }

    [MaxLength(150)]
    public string AuthorName { get; set; } = "";

    [MaxLength(4000)]
    public string Text { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>لاگ فعالیت روی تسک.</summary>
public class DtTaskActivity
{
    public int Id { get; set; }
    public int TaskId { get; set; }
    public DtTask? Task { get; set; }

    public int ActorUserId { get; set; }

    [MaxLength(150)]
    public string ActorName { get; set; } = "";

    /// <summary>Created | StatusChanged | Assigned | Updated | Commented | GitLinked | TimeLogged | Deleted</summary>
    [MaxLength(40)]
    public string Action { get; set; } = "";

    [MaxLength(1000)]
    public string? Detail { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>اتصال گیت به تسک.</summary>
public class DtTaskGitLink
{
    public int Id { get; set; }
    public int TaskId { get; set; }
    public DtTask? Task { get; set; }

    /// <summary>Commit | Branch | PullRequest | Tag</summary>
    [MaxLength(20)]
    public string Kind { get; set; } = DtGitLinkKind.Commit;

    /// <summary>SHA کوتاه/کامل، نام شاخه، شماره PR</summary>
    [MaxLength(200)]
    public string Ref { get; set; } = "";

    [MaxLength(500)]
    public string? Url { get; set; }

    [MaxLength(300)]
    public string? Title { get; set; }

    public int AddedByUserId { get; set; }

    [MaxLength(150)]
    public string AddedByName { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>ثبت زمان کار.</summary>
public class DtTimeEntry
{
    public int Id { get; set; }
    public int TaskId { get; set; }
    public DtTask? Task { get; set; }

    public int UserId { get; set; }

    [MaxLength(150)]
    public string UserName { get; set; } = "";

    [Column(TypeName = "decimal(10,2)")]
    public decimal Hours { get; set; }

    public DateTime WorkDate { get; set; }

    [MaxLength(500)]
    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>مشکل / خطا / وارنینگ روی ماژول یا تسک.</summary>
public class DtProblem
{
    public int Id { get; set; }

    [MaxLength(250)]
    public string Title { get; set; } = "";

    public string? Description { get; set; }

    /// <summary>Error | Warning | Info</summary>
    [MaxLength(20)]
    public string Severity { get; set; } = DtProblemSeverity.Error;

    /// <summary>Open | Investigating | Resolved | WontFix</summary>
    [MaxLength(20)]
    public string Status { get; set; } = DtProblemStatus.Open;

    public int? ModuleId { get; set; }
    public DtProductModule? Module { get; set; }

    public int? TaskId { get; set; }
    public DtTask? Task { get; set; }

    public int? AssigneeUserId { get; set; }

    [MaxLength(150)]
    public string? AssigneeName { get; set; }

    public int ReporterUserId { get; set; }

    [MaxLength(150)]
    public string ReporterName { get; set; } = "";

    /// <summary>متن stack / لاگ کوتاه</summary>
    public string? StackTrace { get; set; }

    [MaxLength(300)]
    public string? Environment { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? ResolvedAt { get; set; }

    [MaxLength(1000)]
    public string? ResolutionNote { get; set; }

    public bool IsDeleted { get; set; }
}

/// <summary>Changelog تغییرات هر ماژول.</summary>
public class DtModuleChange
{
    public int Id { get; set; }

    public int ModuleId { get; set; }
    public DtProductModule? Module { get; set; }

    public int? TaskId { get; set; }
    public DtTask? Task { get; set; }

    [MaxLength(300)]
    public string Summary { get; set; } = "";

    public string? Details { get; set; }

    /// <summary>SHA کامیت (اختیاری)</summary>
    [MaxLength(80)]
    public string? CommitSha { get; set; }

    [MaxLength(120)]
    public string? Branch { get; set; }

    [MaxLength(500)]
    public string? CommitUrl { get; set; }

    public int AuthorUserId { get; set; }

    [MaxLength(150)]
    public string AuthorName { get; set; } = "";

    public DateTime ChangedAt { get; set; } = DateTime.Now;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>آیتم چک‌لیست داخل تسک (مسیر C).</summary>
public class DtTaskChecklistItem
{
    public int Id { get; set; }
    public int TaskId { get; set; }
    public DtTask? Task { get; set; }

    [MaxLength(300)]
    public string Title { get; set; } = "";

    public bool IsDone { get; set; }
    public int SortOrder { get; set; }

    public int CreatedByUserId { get; set; }

    [MaxLength(150)]
    public string CreatedByName { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public DateTime? DoneAt { get; set; }
    public int? DoneByUserId { get; set; }

    [MaxLength(150)]
    public string? DoneByName { get; set; }
}
