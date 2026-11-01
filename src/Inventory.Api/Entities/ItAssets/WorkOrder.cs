using System.ComponentModel.DataAnnotations;

namespace Inventory.Api.Data;

/// <summary>سطوح اولویت دستور کار — مقادیر ثابت تا در دیتابیس به‌صورت عدد ذخیره شود.</summary>
public static class WorkOrderPriority
{
    public const int Low = 0;
    public const int Normal = 1;
    public const int High = 2;
    public const int Urgent = 3;

    public static bool IsValid(int p) => p is >= Low and <= Urgent;

    public static string ToFa(int p) => p switch
    {
        Low => "کم",
        High => "بالا",
        Urgent => "فوری",
        _ => "عادی"
    };
}

/// <summary>الگوهای تکرار دستور کار — نوبت‌ها هنگام ثبت، از پیش در تقویم ساخته می‌شوند.</summary>
public static class WorkOrderRecurrence
{
    public const int None = 0;
    public const int Daily = 1;
    public const int Weekly = 2;
    public const int Monthly = 3;

    public static bool IsValid(int r) => r is >= None and <= Monthly;

    public static string ToFa(int r) => r switch
    {
        Daily => "روزانه",
        Weekly => "هفتگی",
        Monthly => "ماهانه",
        _ => "بدون تکرار"
    };
}

/// <summary>دستور کار — قابل محول‌کردن به خود یا دیگران.</summary>
public class WorkOrder
{
    public int Id { get; set; }

    /// <summary>شرکت فعال مالک این دستور کار</summary>
    public int? CompanyId { get; set; }

    /// <summary>شماره منحصربه‌فرد: WO/سال شمسی/سریال</summary>
    [MaxLength(30)]
    public string Number { get; set; } = "";

    [MaxLength(200)]
    public string Title { get; set; } = "";

    /// <summary>شرح — HTML از ادیتور</summary>
    [MaxLength(8000)]
    public string Description { get; set; } = "";

    /// <summary>دستوردهنده</summary>
    public int OwnerUserId { get; set; }

    [MaxLength(150)]
    public string OwnerName { get; set; } = "";

    /// <summary>تاریخ و ساعت مقرر</summary>
    public DateTime DueAt { get; set; }

    /// <summary>Open | Closed — بعد از بستن هیچ عملیاتی مجاز نیست، مگر «بازگشایی» توسط دستوردهنده (برگشت به Open)</summary>
    [MaxLength(20)]
    public string Status { get; set; } = "Open";

    [MaxLength(1000)]
    public string? CloseNote { get; set; }
    public DateTime? ClosedAt { get; set; }

    /// <summary>تعداد تمدیدها — حداکثر ۵ بار</summary>
    public int ExtensionCount { get; set; }

    /// <summary>اولویت: 0=کم | 1=عادی | 2=بالا | 3=فوری (پیش‌فرض: عادی)</summary>
    public int Priority { get; set; } = WorkOrderPriority.Normal;

    /// <summary>تکرار: 0=بدون تکرار | 1=روزانه | 2=هفتگی | 3=ماهانه — روزانه تا آخر ماه، هفتگی/ماهانه تا آخر سال شمسی.</summary>
    public int Recurrence { get; set; } = WorkOrderRecurrence.None;

    /// <summary>شناسه نوبت مبدأ سری؛ در رکوردهای قدیمی اشاره به نوبت قبلی دارد.</summary>
    public int? RecurrenceParentId { get; set; }

    /// <summary>شناسهٔ سری تقویمی؛ null برای رکوردهای قدیمی یا بدون تکرار.</summary>
    [MaxLength(32)]
    public string? RecurrenceSeriesId { get; set; }
    /// <summary>تاریخ اولیه نوبت؛ با ویرایش یا تمدید تغییر نمی‌کند.</summary>
    public DateTime? RecurrenceScheduledAt { get; set; }
    public DateTime? DeletedAt { get; set; }
    public int? DeletedByUserId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// ماژول مبدأ (سورس) که دستور کار از آن ساخته شده — برای اتصالِ عمومی بخش‌ها به دستور کار.
    /// مثال: "InnerLetter" (نامه داخلی). برای سایر ماژول‌ها در آینده بدون تغییر ساختار قابل استفاده است.
    /// </summary>
    [MaxLength(50)]
    public string? SourceModule { get; set; }

    /// <summary>شناسه رکورد مبدأ در ماژول سورس (مثلاً شناسه نامه داخلی).</summary>
    public int? SourceId { get; set; }

    /// <summary>شناسهٔ رکورد والد مبدأ (مثلاً شناسهٔ صورتجلسه برای بند) — اختیاری.</summary>
    public int? SourceRefId { get; set; }

    /// <summary>
    /// برچسب‌ها/دسته‌بندی — حداکثر ۵ برچسب، هر یک تا ۳۰ حرف، جداشده با «,».
    /// برای فیلتر دقیق، جستجو به شکل ",tag," روی ",Tags," انجام می‌شود.
    /// </summary>
    [MaxLength(300)]
    public string? Tags { get; set; }

    /// <summary>
    /// ارجاع زنجیره‌ای — شناسهٔ دستور والد. وقتی گیرندهٔ یک دستور بخشی از کار را
    /// به نفر بعدی ارجاع می‌دهد، دستور جدید به والد متصل می‌ماند.
    /// تا زیر-دستورهای باز بسته نشوند، والد قابل بستن نیست.
    /// </summary>
    public int? ParentOrderId { get; set; }

    /// <summary>پروژه کاری متصل (اختیاری) — null یعنی بدون پروژه.</summary>
    public int? ProjectId { get; set; }

    /// <summary>
    /// وزن دستور در پیشرفت پروژه: ۰ تا ۱۰ (۰ = در محاسبهٔ پیشرفت حساب نشود)؛ پیش‌فرض ۱.
    /// فقط وقتی پروژه انتخاب شده باشد معنا دارد.
    /// </summary>
    public int Weight { get; set; } = 1;

    public WorkProject? Project { get; set; }
}

/// <summary>
/// قالب آمادهٔ دستور کار — دستورهای پرتکرار یک‌بار ذخیره می‌شوند و دفعات بعد
/// فرم «دستور کار جدید» با یک کلیک پیش‌پُر می‌شود (عنوان، شرح، گیرندگان،
/// اولویت، تکرار، چک‌لیست و برچسب‌ها). هر قالب خصوصیِ سازنده است.
/// </summary>
public class WorkOrderTemplate
{
    public int Id { get; set; }

    /// <summary>سازندهٔ قالب — فقط خودش می‌بیند/استفاده/حذف می‌کند.</summary>
    public int OwnerUserId { get; set; }

    /// <summary>نام قالب برای نمایش در فهرست (مثلاً «سرویس ماهانه ژنراتور»).</summary>
    [MaxLength(100)]
    public string Name { get; set; } = "";

    [MaxLength(200)]
    public string Title { get; set; } = "";

    /// <summary>شرح — HTML از ادیتور</summary>
    [MaxLength(8000)]
    public string Description { get; set; } = "";

    /// <summary>اولویت پیش‌فرض قالب: 0=کم | 1=عادی | 2=بالا | 3=فوری</summary>
    public int Priority { get; set; } = WorkOrderPriority.Normal;

    /// <summary>تکرار پیش‌فرض قالب: 0=بدون تکرار | 1=روزانه | 2=هفتگی | 3=ماهانه</summary>
    public int Recurrence { get; set; } = WorkOrderRecurrence.None;

    /// <summary>گیرندگان پیش‌فرض — شناسه‌ها جداشده با «,» (مثلاً "2,5"). هنگام استفاده دوباره اعتبارسنجی می‌شود.</summary>
    [MaxLength(500)]
    public string? AssigneeUserIds { get; set; }

    /// <summary>آیتم‌های چک‌لیست — هر خط یک آیتم (جداشده با \n).</summary>
    [MaxLength(4000)]
    public string? ChecklistItems { get; set; }

    /// <summary>برچسب‌ها — همان قالب دستور کار: ",tag1,tag2,"</summary>
    [MaxLength(300)]
    public string? Tags { get; set; }

    /// <summary>تعداد دفعات استفاده — برای مرتب‌سازی پرکاربردها در بالا.</summary>
    public int UsageCount { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>گیرنده دستور کار — پاسخ، رویت و تصمیم دستوردهنده.</summary>
public class WorkOrderAssignee
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public int UserId { get; set; }

    [MaxLength(150)]
    public string Name { get; set; } = "";

    /// <summary>تاریخ و ساعت رویت</summary>
    public DateTime? SeenAt { get; set; }

    /// <summary>تاریخ و ساعت پاسخ</summary>
    public DateTime? RepliedAt { get; set; }

    /// <summary>true=انجام شد | false=انجام نشد | null=بدون پاسخ</summary>
    public bool? Done { get; set; }

    [MaxLength(2000)]
    public string? ReplyText { get; set; }

    /// <summary>تصمیم دستوردهنده: Approved | Rejected | null</summary>
    [MaxLength(20)]
    public string? OwnerDecision { get; set; }

    [MaxLength(1000)]
    public string? OwnerDecisionNote { get; set; }
}

/// <summary>تاریخچه کامل رفت‌وبرگشت‌های دستور کار (آرشیو).</summary>
public class WorkOrderLog
{
    public int Id { get; set; }
    public int OrderId { get; set; }

    [MaxLength(150)]
    public string ActorName { get; set; } = "";

    /// <summary>Created | Seen | Reply | Approved | Rejected | Extended | Closed | Reopened | AssigneesAdded</summary>
    [MaxLength(30)]
    public string Action { get; set; } = "";

    [MaxLength(4000)]
    public string? Text { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>پیوست دستور کار.</summary>
public class WorkOrderAttachment
{
    public int Id { get; set; }
    public int OrderId { get; set; }

    [MaxLength(255)]
    public string FileName { get; set; } = "";

    [MaxLength(100)]
    public string ContentType { get; set; } = "";

    public byte[] Data { get; set; } = Array.Empty<byte>();
    /// <summary>مسیر نسبی فایل داخل uploads/ (فایل‌های جدید روی دیسک؛ Data فقط برای رکوردهای قدیمی).</summary>
    [MaxLength(255)]
    public string? FilePath { get; set; }


    [MaxLength(150)]
    public string UploaderName { get; set; } = "";
    public int UploaderUserId { get; set; }
    public DateTime UploadedAt { get; set; } = DateTime.Now;
}

/// <summary>آیتم چک‌لیست زیرکار — گام‌های تیک‌خور داخل یک دستور کار + درصد پیشرفت.</summary>
public class WorkOrderChecklistItem
{
    public int Id { get; set; }
    public int OrderId { get; set; }

    [MaxLength(300)]
    public string Text { get; set; } = "";

    /// <summary>ترتیب نمایش.</summary>
    public int SortOrder { get; set; }

    public bool IsDone { get; set; }

    /// <summary>چه کسی و چه زمانی تیک زد.</summary>
    public int? DoneByUserId { get; set; }

    [MaxLength(150)]
    public string? DoneByName { get; set; }
    public DateTime? DoneAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>
/// ثبت یادآورهای مهلتِ ارسال‌شده — تضمین می‌کند برای هر (دستور، آستانه، مهلت) فقط یک‌بار اعلان برود.
/// اگر مهلت تمدید شود، DueAtSnapshot تغییر می‌کند و یادآورها دوباره فعال می‌شوند.
/// </summary>
public class WorkOrderReminderLog
{
    public int Id { get; set; }
    public int OrderId { get; set; }

    /// <summary>آستانه یادآور بر حسب ساعتِ مانده تا مهلت (مثلاً 24 یا 0=رسیدن مهلت).</summary>
    public int ThresholdHours { get; set; }

    /// <summary>مهلتی که یادآور بر مبنای آن ارسال شد — برای بی‌اثرشدن بعد از تمدید.</summary>
    public DateTime DueAtSnapshot { get; set; }

    public DateTime SentAt { get; set; } = DateTime.Now;
}

/// <summary>
/// کامنت (رشتهٔ گفتگو) داخل دستور کار — دستوردهنده و گیرندگان می‌توانند
/// دربارهٔ همان دستور گفتگو کنند و سابقه داخل خود دستور می‌ماند.
/// </summary>
public class WorkOrderComment
{
    public int Id { get; set; }
    public int OrderId { get; set; }

    public int AuthorUserId { get; set; }

    [MaxLength(150)]
    public string AuthorName { get; set; } = "";

    [MaxLength(2000)]
    public string Text { get; set; } = "";

    /// <summary>پاسخ به کامنت دیگر — null یعنی کامنت ریشه.</summary>
    public int? ReplyToId { get; set; }

    /// <summary>حذف نرم — متن پاک می‌شود اما جای آن در رشته می‌ماند.</summary>
    public bool IsDeleted { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? EditedAt { get; set; }
}

/// <summary>لیست افرادی که هر کاربر مجاز است به آن‌ها دستور کار بدهد.</summary>
public class WorkOrderAllowedAssignee
{
    public int Id { get; set; }

    /// <summary>دستوردهنده</summary>
    public int OwnerUserId { get; set; }

    /// <summary>گیرنده مجاز</summary>
    public int TargetUserId { get; set; }
}
