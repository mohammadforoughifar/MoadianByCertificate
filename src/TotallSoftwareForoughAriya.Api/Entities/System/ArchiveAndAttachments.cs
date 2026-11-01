using System.ComponentModel.DataAnnotations;

namespace Inventory.Api.Data;

/// <summary>
/// ================== بایگانی جامع (عمومی) ==================
/// هر کاربر بایگانی اختصاصی خودش را دارد (OwnerUserId) با پوشه و زیرپوشه.
/// هر رکوردی از هر ماژولی با (Module + RefId) قابل بایگانی است —
/// برای ماژول‌های جدید هیچ کد جدیدی لازم نیست.
/// </summary>
public class ArchiveFolder
{
    public int Id { get; set; }

    /// <summary>صاحب بایگانی — بایگانی هر کاربر خصوصی است</summary>
    public int OwnerUserId { get; set; }

    /// <summary>پوشه والد — null یعنی ریشه</summary>
    public int? ParentId { get; set; }

    [MaxLength(150)]
    public string Name { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>آیتم بایگانی‌شده — ارجاع عمومی به هر رکورد از هر ماژول.</summary>
public class ArchiveItem
{
    public int Id { get; set; }
    public int OwnerUserId { get; set; }
    public int FolderId { get; set; }

    /// <summary>نام ماژول: WorkOrders | ItRequests | Orders | ...</summary>
    [MaxLength(50)]
    public string Module { get; set; } = "";

    /// <summary>شناسه رکورد در ماژول مبدأ</summary>
    public int RefId { get; set; }

    [MaxLength(250)]
    public string Title { get; set; } = "";

    /// <summary>لینک داخلی برای باز کردن رکورد</summary>
    [MaxLength(250)]
    public string? Link { get; set; }

    [MaxLength(500)]
    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>
/// ================== پیوست جامع (عمومی) ==================
/// یک جدول و یک API برای پیوست همه‌ی فرم‌ها — با (Module + RefId).
/// فرم جدید = صفر کد بک‌اند جدید.
/// </summary>
public class AppAttachment
{
    public int Id { get; set; }

    [MaxLength(50)]
    public string Module { get; set; } = "";

    public int RefId { get; set; }

    [MaxLength(255)]
    public string FileName { get; set; } = "";

    [MaxLength(100)]
    public string ContentType { get; set; } = "";

    public byte[] Data { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// مسیر نسبی فایل داخل uploads/ — فایل‌های جدید روی دیسک ذخیره می‌شوند و Data خالی می‌ماند.
    /// برای رکوردهای قدیمی (فایل داخل DB) این ستون خالی است و از Data استفاده می‌شود.
    /// </summary>
    [MaxLength(255)]
    public string? FilePath { get; set; }

    [MaxLength(150)]
    public string UploaderName { get; set; } = "";
    public int UploaderUserId { get; set; }
    public DateTime UploadedAt { get; set; } = DateTime.Now;
}

/// <summary>
/// لاگ مشاهده/دانلود پیوست — برای هر ماژول (Module/RefId) یک‌جا ثبت می‌شود
/// تا گردانه (Audit Trail) فایل‌ها قابل ردیابی باشد.
/// </summary>
public class AppAttachmentAccessLog
{
    public int Id { get; set; }

    public int AttachmentId { get; set; }

    [MaxLength(50)]
    public string Module { get; set; } = "";

    public int RefId { get; set; }

    [MaxLength(150)]
    public string FileName { get; set; } = "";

    /// <summary>Preview / Download</summary>
    [MaxLength(20)]
    public string Action { get; set; } = "";

    public int UserId { get; set; }

    [MaxLength(150)]
    public string UserName { get; set; } = "";

    [MaxLength(60)]
    public string? Ip { get; set; }

    public DateTime At { get; set; } = DateTime.Now;
}
