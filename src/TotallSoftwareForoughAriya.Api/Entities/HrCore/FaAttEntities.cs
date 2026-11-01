using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Inventory.Api.Data;

/// <summary>
/// ================== حضور و غیاب فروغ آریا (FaAtt) — ماژول جدید و مستقل ==================
/// محاسبه دقیق کارکرد پرسنل و مبنای ورودی حقوق و دستمزد.
/// کاملاً جدا از ماژول قبلی (Hr/Attendance/Leave)؛ جدول‌ها با پیشوند FaAtt یکتا هستند:
/// FaAttShifts، FaAttShiftAssigns، FaAttDevices، FaAttLogs، FaAttDailies،
/// FaAttMissions، FaAttLeaveTypes، FaAttLeaves.
/// اسکیما با FaAttSchemaV1 به‌صورت Ensure ساخته می‌شود (نه EF Migration).
/// تنها خواندن بدون تغییر از جدول موجود: CompanyHoliday (تعطیلات) و HrEmployees (پرسنل).
/// </summary>

/// <summary>نوع شیفت کاری</summary>
public enum FaAttShiftType
{
    Fixed = 0,     // ثابت
    Rotating = 1,  // چرخشی (با تخصیص‌های دوره‌ای متوالی می‌چرخد)
    Night = 2,     // شب‌کاری (پنجره گذرنده از نیمه‌شب)
    Flexible = 3   // شناور (بدون تأخیر/تعجیل؛ فقط کارکرد)
}

/// <summary>نوع تردد</summary>
public enum FaAttLogType
{
    In = 0,
    Out = 1,
    Unknown = 2   // پانچ دستگاه بدون جهت مشخص
}

/// <summary>مسیر ثبت تردد</summary>
public enum FaAttLogSource
{
    Device = 0,   // دستگاه اثر انگشت/تشخیص چهره
    Mobile = 1,   // اپلیکیشن موبایل + موقعیت‌یاب
    Web = 2,      // وب‌کلاک مرورگر
    Manual = 3    // ثبت دستی ادمین
}

/// <summary>وضعیت روزانه پرسنل</summary>
public enum FaAttDayStatus
{
    Present = 0,
    Late = 1,
    EarlyLeave = 2,
    LateAndEarly = 3,
    Absent = 4,
    Mission = 5,
    Leave = 6,
    Holiday = 7,
    OffDay = 8,
    WorkedOff = 9,    // تعطیل‌کار (کار در روز تعطیل/استراحت)
    Incomplete = 10   // تردد ناقص (تک‌تردد)
}

/// <summary>وضعیت درخواست (ماموریت/مرخصی)</summary>
public enum FaAttRequestStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2
}

/// <summary>نوع دستگاه تردد</summary>
public enum FaAttDeviceType
{
    Fingerprint = 0,
    Face = 1,
    Card = 2
}

/// <summary>شیفت کاری</summary>
public class FaAttShift
{
    public int Id { get; set; }

    [MaxLength(20)]
    public string Code { get; set; } = "";

    [MaxLength(100)]
    public string Name { get; set; } = "";

    public FaAttShiftType Type { get; set; } = FaAttShiftType.Fixed;

    public TimeSpan StartTime { get; set; } = new(8, 0, 0);

    public TimeSpan EndTime { get; set; } = new(17, 0, 0);

    /// <summary>ارفاق تأخیر (دقیقه)</summary>
    public int LateToleranceMin { get; set; }

    /// <summary>ارفاق تعجیل (دقیقه)</summary>
    public int EarlyToleranceMin { get; set; }

    /// <summary>ارفاق اضافه‌کاری (دقیقه) — کمتر از این نادیده گرفته می‌شود</summary>
    public int OvertimeGraceMin { get; set; } = 10;

    /// <summary>دقایق کارکرد موظف روزانه — مبنای اضافه‌کاری شیفت شناور</summary>
    public int RequiredMinutes { get; set; } = 480;

    /// <summary>روزهای استراحت هفتگی با کاما — اعداد DayOfWeek (پیش‌فرض 5=جمعه)</summary>
    [MaxLength(20)]
    public string? OffDays { get; set; } = "5";

    [MaxLength(20)]
    public string? Color { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>درصد فوق‌العاده نوبت‌کاری این شیفت روی حقوق پایه (۰ یعنی ندارد)</summary>
    public double AllowancePercent { get; set; }

    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>تخصیص شیفت به پرسنل در بازه تاریخی — چرخش یعنی تخصیص‌های متوالی</summary>
public class FaAttShiftAssign
{
    public int Id { get; set; }

    public int EmployeeId { get; set; }

    public int ShiftId { get; set; }

    public DateTime FromDate { get; set; } = DateTime.Today;

    /// <summary>خالی = باز (تا اطلاع ثانوی)</summary>
    public DateTime? ToDate { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>دستگاه تردد (اثر انگشت / تشخیص چهره / کارتی)</summary>
public class FaAttDevice
{
    public int Id { get; set; }

    [MaxLength(20)]
    public string Code { get; set; } = "";

    [MaxLength(100)]
    public string Name { get; set; } = "";

    public FaAttDeviceType Type { get; set; } = FaAttDeviceType.Fingerprint;

    [MaxLength(150)]
    public string? Location { get; set; }

    [MaxLength(50)]
    public string? SerialNo { get; set; }

    [MaxLength(50)]
    public string? IpAddress { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime? LastSyncAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>تردد خام — هر رکورد یک پانچ ورود/خروج از یکی از مسیرها</summary>
public class FaAttLog
{
    public int Id { get; set; }

    public int EmployeeId { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.Now;

    public FaAttLogType Type { get; set; } = FaAttLogType.Unknown;

    public FaAttLogSource Source { get; set; } = FaAttLogSource.Web;

    public int? DeviceId { get; set; }

    /// <summary>موقعیت ثبت موبایل/وب (اختیاری)</summary>
    public double? Latitude { get; set; }

    public double? Longitude { get; set; }

    [MaxLength(300)]
    public string? Note { get; set; }

    public int? CreatedByUserId { get; set; }

    [MaxLength(150)]
    public string? CreatedByName { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>وضعیت روزانه محاسبه‌شده — مبنای ورودی حقوق و دستمزد</summary>
public class FaAttDaily
{
    public int Id { get; set; }

    public int EmployeeId { get; set; }

    /// <summary>تاریخ روز (بدون زمان)</summary>
    public DateTime Date { get; set; }

    public int? ShiftId { get; set; }

    public DateTime? FirstIn { get; set; }

    public DateTime? LastOut { get; set; }

    public int WorkMinutes { get; set; }

    public int LateMinutes { get; set; }

    public int EarlyMinutes { get; set; }

    public int OvertimeMinutes { get; set; }

    /// <summary>دقایق شب‌کاری (۲۲ تا ۶ صبح) — مبنای فوق‌العاده شب‌کاری در حقوق</summary>
    public int NightMinutes { get; set; }

    public FaAttDayStatus Status { get; set; } = FaAttDayStatus.Absent;

    /// <summary>تردد ناقص (فقط یک پانچ در روز)</summary>
    public bool IsIncomplete { get; set; }

    [MaxLength(300)]
    public string? Note { get; set; }

    public DateTime CalculatedAt { get; set; } = DateTime.Now;
}

/// <summary>ماموریت پرسنل</summary>
public class FaAttMission
{
    public int Id { get; set; }

    public int EmployeeId { get; set; }

    public DateTime FromDate { get; set; } = DateTime.Today;

    public DateTime ToDate { get; set; } = DateTime.Today;

    [MaxLength(150)]
    public string? Destination { get; set; }

    [MaxLength(500)]
    public string? Reason { get; set; }

    public FaAttRequestStatus Status { get; set; } = FaAttRequestStatus.Pending;

    public int? DecidedByUserId { get; set; }

    [MaxLength(150)]
    public string? DecidedByName { get; set; }

    public DateTime? DecidedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>نوع مرخصی — داده پایه (با FaAttMasterSeeder مقداردهی می‌شود)</summary>
public class FaAttLeaveType
{
    public int Id { get; set; }

    [MaxLength(50)]
    public string Name { get; set; } = "";

    /// <summary>سقف سالانه (روز) — خالی یعنی نامحدود</summary>
    public int? AnnualLimitDays { get; set; }

    public bool IsActive { get; set; } = true;

    public int SortOrder { get; set; }
}

/// <summary>درخواست مرخصی روزانه/ساعتی</summary>
public class FaAttLeave
{
    public int Id { get; set; }

    public int EmployeeId { get; set; }

    public int LeaveTypeId { get; set; }

    public DateTime FromDate { get; set; } = DateTime.Today;

    public DateTime ToDate { get; set; } = DateTime.Today;

    /// <summary>مرخصی ساعتی (ساعت در روز) — خالی یعنی تمام‌روز</summary>
    public double? HoursPerDay { get; set; }

    [MaxLength(500)]
    public string? Reason { get; set; }

    public FaAttRequestStatus Status { get; set; } = FaAttRequestStatus.Pending;

    public int? DecidedByUserId { get; set; }

    [MaxLength(150)]
    public string? DecidedByName { get; set; }

    public DateTime? DecidedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    /// <summary>مرحله گردش‌کار §۷: 0=نزد مدیر مستقیم، 1=نزد HR، 2=تمام‌شده</summary>
    public int WorkflowStep { get; set; } = 0;

    public int? ManagerDecidedByUserId { get; set; }

    [MaxLength(150)]
    public string? ManagerDecidedByName { get; set; }

    public DateTime? ManagerDecidedAt { get; set; }

}

/// <summary>مانده مرخصی سالانه پرسنل به تفکیک نوع (سال شمسی) — §۷</summary>
public class FaAttLeaveBalance
{
    public int Id { get; set; }

    public int EmployeeId { get; set; }

    /// <summary>سال شمسی</summary>
    public int Year { get; set; }

    public int LeaveTypeId { get; set; }

    /// <summary>استحقاق سال (معادل روزانه)</summary>
    public double EntitledDays { get; set; }

    /// <summary>مصرف‌شده با تأیید نهایی (معادل روزانه؛ مرخصی ساعتی ÷ ۸)</summary>
    public double UsedDays { get; set; }

    /// <summary>انتقال‌یافته از سال قبل (ماده ۶۶: سقف ۹ روز)</summary>
    public double CarriedDays { get; set; }

    /// <summary>تبدیل به نقد شده (معادل روزانه)</summary>
    public double CashedDays { get; set; }

    /// <summary>مبلغ نقدشده به ریال (ثبت دستی)</summary>
    public double? CashAmount { get; set; }

    [MaxLength(300)]
    public string? Note { get; set; }

    [NotMapped]
    public double Remaining => EntitledDays + CarriedDays - UsedDays - CashedDays;
}
