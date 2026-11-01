namespace Inventory.Shared.BonHr;

/// <summary>مساعده — radisHrAdvancesV015</summary>
public class Advance
{
    public int Id { get; set; }
    public string Uid { get; set; } = "";
    public string EmployeeCode { get; set; } = "";
    public string Date { get; set; } = "";          // تاریخ پرداخت شمسی
    public decimal Amount { get; set; }
    public decimal Deducted { get; set; }           // کسرشده تا کنون
    public decimal Balance => Math.Max(0m, Amount - Deducted);

    /// <summary>pending | full | installment</summary>
    public string SettlementMethod { get; set; } = "pending";
    public string SettlementStartMonth { get; set; } = "";
    public int SettlementCount { get; set; }
    public decimal SettlementAmount { get; set; }
    public string SettlementStatus { get; set; } = "تعیین تکلیف نشده";

    public List<AdvanceInstallment> Installments { get; set; } = new();

    public string CreatedBy { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class AdvanceInstallment
{
    public int Id { get; set; }
    public int AdvanceId { get; set; }
    public string Month { get; set; } = "";     // 1405/04
    public decimal Amount { get; set; }
    public bool Applied { get; set; }
    public DateTime? AppliedAt { get; set; }
}

/// <summary>اصلاح حسابداری روی فیش — accountingFields در v016-workflows.js</summary>
public class AccountingAdjustment
{
    public int Id { get; set; }
    public string Month { get; set; } = "";
    public string EmployeeCode { get; set; } = "";

    public decimal? Tax { get; set; }
    public decimal? Insurance { get; set; }
    public decimal? Overtime { get; set; }
    public decimal? Shortfall { get; set; }
    public decimal? Performance { get; set; }
    public decimal? Productivity { get; set; }
    public decimal? OtherPayments { get; set; }

    /// <summary>در صورت تغییر هر مبلغ، ثبت دلیل اجباری است</summary>
    public string Reason { get; set; } = "";
    public decimal FinalNet { get; set; }
    public string AdjustedBy { get; set; } = "";
    public DateTime AdjustedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>بایگانی معتبر ماهانهٔ حسابداری — radisHrAccountingArchiveV015</summary>
public class AccountingArchive
{
    public int Id { get; set; }
    public string Month { get; set; } = "";
    public string Status { get; set; } = "در حال بررسی"; // در حال بررسی | تأییدشده | تسویه‌شده
    public decimal TotalNet { get; set; }
    public int EmployeeCount { get; set; }
    public string ApprovedBy { get; set; } = "";
    public DateTime? ApprovedAt { get; set; }
    public string? SettledBy { get; set; }
    public DateTime? SettledAt { get; set; }
    public bool Locked { get; set; }
    public string PayloadJson { get; set; } = "[]";   // تصویر لحظه‌ای ردیف‌های تأییدشده
}

/// <summary>ثبت پرداخت فیش توسط مالی — radisHrPayrollPaymentsV019</summary>
public class PayrollPayment
{
    public int Id { get; set; }
    public string Month { get; set; } = "";
    public string EmployeeCode { get; set; } = "";
    public decimal Amount { get; set; }
    public string PaidDate { get; set; } = "";
    public string Reference { get; set; } = "";
    public string PaidBy { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>اطلاعیه — radisHrAnnouncementsV015</summary>
public class Announcement
{
    public int Id { get; set; }
    public string Uid { get; set; } = "";
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    /// <summary>مدت فعال‌بودن از اولین مشاهده (روز)</summary>
    public int DurationDays { get; set; } = 7;
    public List<AnnouncementRecipient> Recipients { get; set; } = new();
    public List<AnnouncementAttachment> Attachments { get; set; } = new();
    public string CreatedBy { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class AnnouncementRecipient
{
    public int Id { get; set; }
    public int AnnouncementId { get; set; }
    public string RoleKey { get; set; } = "";
    public DateTime? FirstSeenAt { get; set; }
}

public class AnnouncementAttachment
{
    public int Id { get; set; }
    public int AnnouncementId { get; set; }
    public string Uid { get; set; } = "";
    public string Name { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long Size { get; set; }
    public string? StoredFileId { get; set; }
}

/// <summary>گزارش تغییرات مهم برای مدیرعامل — radisHrCeoNotificationsV014 / OperationalAudits</summary>
public class AuditNotice
{
    public int Id { get; set; }
    public string Uid { get; set; } = "";
    /// <summary>statutory | operational</summary>
    public string Channel { get; set; } = "operational";
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    public string Actor { get; set; } = "";
    public string? Details { get; set; }
    public bool Seen { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>سابقهٔ تغییر قواعد قانونی — radisHrStatutoryRuleAuditsV014</summary>
public class StatutoryRuleAudit
{
    public int Id { get; set; }
    public int Year { get; set; }
    public string Field { get; set; } = "";
    public string FieldLabel { get; set; } = "";
    public string OldValue { get; set; } = "";
    public string NewValue { get; set; } = "";
    public string ChangedBy { get; set; } = "";
    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>فایل ذخیره‌شده — جایگزین سمت سرور برای IndexedDB radisHrFilesV015</summary>
public class StoredFile
{
    public int Id { get; set; }
    public string Uid { get; set; } = "";
    public string Name { get; set; } = "";
    public string ContentType { get; set; } = "application/octet-stream";
    public long Size { get; set; }
    public byte[] Content { get; set; } = Array.Empty<byte>();
    public string UploadedBy { get; set; } = "";
    public DateTime StoredAt { get; set; } = DateTime.UtcNow;
}

/// <summary>واحد سازمانی — radisHrOrganizationV013</summary>
public class Department
{
    public int Id { get; set; }
    public string Uid { get; set; } = "";
    public string Name { get; set; } = "";
    public int Ordinal { get; set; }
    public List<WorkStation> Stations { get; set; } = new();
}

public class WorkStation
{
    public int Id { get; set; }
    public int DepartmentId { get; set; }
    public string Uid { get; set; } = "";
    public string Name { get; set; } = "";
}
