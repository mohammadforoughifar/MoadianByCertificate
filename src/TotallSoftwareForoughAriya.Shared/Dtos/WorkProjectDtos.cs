namespace Inventory.Shared.Dtos;

// ============================================================
//  پروژه کاری — DTOهای مشترک API و کلاینت (داخل ماژول دستور کار)
// ============================================================

/// <summary>ساخت/ویرایش پروژه کاری (MemberIds فقط موقع ساخت استفاده می‌شود؛ بعدش از مسیر اعضا).</summary>
public class SaveWorkProjectDto
{
    /// <summary>0 = جدید، غیرصفر = ویرایش</summary>
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public DateTime StartAt { get; set; } = DateTime.Today;
    public DateTime DueAt { get; set; } = DateTime.Today.AddDays(7);

    /// <summary>اولویت: 0=کم | 1=عادی | 2=بالا | 3=فوری</summary>
    public int Priority { get; set; } = 1;

    /// <summary>اعضای اولیه (فقط موقع ساخت)</summary>
    public List<int> MemberIds { get; set; } = new();

    /// <summary>برچسب‌ها — حداکثر ۵ عدد</summary>
    public List<string> Tags { get; set; } = new();
}

/// <summary>عضو تیم پروژه</summary>
public class WorkProjectMemberDto
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Name { get; set; } = "";
    public DateTime AddedAt { get; set; }
}

/// <summary>یک رویداد از تاریخچه فعالیت پروژه</summary>
public class WorkProjectLogDto
{
    public int Id { get; set; }
    public string ActorName { get; set; } = "";
    public string Action { get; set; } = "";

    /// <summary>نام فارسی رویداد (آماده نمایش)</summary>
    public string ActionFa { get; set; } = "";
    public string? Text { get; set; }
    public DateTime CreatedAt { get; set; }

    public static string ToFa(string? action) => (action ?? "") switch
    {
        "Created" => "ساخت پروژه",
        "Edited" => "ویرایش",
        "Extended" => "تمدید مهلت",
        "Closed" => "بستن پروژه",
        "Reopened" => "بازگشایی",
        "Deleted" => "حذف",
        "MemberAdded" => "افزودن عضو",
        "MemberRemoved" => "حذف عضو",
        "OrderAdded" => "اتصال دستور کار",
        "OrderRemoved" => "جدا شدن دستور کار",
        "OrderClosed" => "بسته شدن دستور کار",
        "MindFlowUpdated" => "به‌روزرسانی نقشهٔ ذهنی",
        _ => "فعالیت"
    };
}

/// <summary>یک سطر از لیست پروژه‌ها (داشبورد یک‌نگاه)</summary>
public class WorkProjectListItemDto
{
    public int Id { get; set; }
    public string Number { get; set; } = "";
    public string Title { get; set; } = "";
    public int OwnerUserId { get; set; }
    public string OwnerName { get; set; } = "";
    public DateTime StartAt { get; set; }
    public DateTime DueAt { get; set; }

    /// <summary>Open | Closed</summary>
    public string Status { get; set; } = "Open";
    public int Priority { get; set; } = 1;
    public int ExtensionCount { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>درصد پیشرفت وزنی (null = بدون دستور واجد → نمایش «بدون دستور»)</summary>
    public int? Progress { get; set; }
    public int OrderCount { get; set; }
    public int OpenOrderCount { get; set; }
    public int ClosedOrderCount { get; set; }
    public int OverdueOrderCount { get; set; }
    public int MemberCount { get; set; }

    /// <summary>نام چند عضو اول (برای نمایش خلاصه)</summary>
    public List<string> MemberNames { get; set; } = new();

    /// <summary>روزهای مانده تا مهلت (منفی = گذشته) — فقط برای پروژه باز معنا دارد</summary>
    public int DaysLeft { get; set; }
    public bool IsOverdue { get; set; }
    public bool IsMine { get; set; }
    public bool IsMember { get; set; }
    public bool CanViewMindFlow { get; set; }

    public DateTime? LastActivityAt { get; set; }
    public string? LastActivityText { get; set; }
}

/// <summary>جزئیات کامل پروژه</summary>
public class WorkProjectDetailDto : WorkProjectListItemDto
{
    public string? Description { get; set; }
    public List<string> Tags { get; set; } = new();
    public string? CloseNote { get; set; }
    public DateTime? ClosedAt { get; set; }
    public List<WorkProjectMemberDto> Members { get; set; } = new();

    /// <summary>آیا کاربر جاری می‌تواند ویرایش/تمدید/بستن/اعضا انجام دهد؟ (مالک یا مجوز Update)</summary>
    public bool CanManage { get; set; }
    /// <summary>اختیار ویرایش ذهن‌نگار؛ دعوت مستقیم به MindFlow به‌تنهایی فقط‌خواندنی است.</summary>
    public bool CanManageMindFlow { get; set; }

    // ---------- دسترسی «هر قسمت» صفحهٔ پروژه ----------
    // همان مجوزهای WorkProjects.Summary/Orders/MindFlow/Members/Activity که در
    // «تنظیمات ← نقش‌ها ← دستور کار، پروژه‌ها و نگار ذهنی» جداگانه اعطا می‌شوند.
    // کلاینت تب‌ها را با همین پرچم‌ها نشان می‌دهد؛ سرویس هم همان‌ها را الزام می‌کند.
    public bool CanViewSummary { get; set; }
    public bool CanViewOrders { get; set; }
    public bool CanViewMembers { get; set; }
    public bool CanViewActivity { get; set; }
}

/// <summary>صفحه‌بندی لیست پروژه‌ها</summary>
public class WorkProjectPageDto
{
    public List<WorkProjectListItemDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

/// <summary>تمدید مهلت پروژه</summary>
public class ExtendWorkProjectDto
{
    public DateTime NewDueAt { get; set; }

    /// <summary>دلیل تمدید (الزامی)</summary>
    public string? Note { get; set; }
}

/// <summary>بستن پروژه</summary>
public class CloseWorkProjectDto
{
    public string? Note { get; set; }
}

/// <summary>افزودن عضو (یک یا چند نفره)</summary>
public class AddWorkProjectMembersDto
{
    public List<int> UserIds { get; set; } = new();
}

/// <summary>آیتم کمبوی پروژه (فرم دستور کار) — DueAt برای هشدار نرم مهلت لازم است</summary>
public class WorkProjectLookupDto
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public DateTime DueAt { get; set; }
}
