namespace Inventory.Api.Data;

/// <summary>
/// موجودیت‌هایی که داده‌شان به شرکت (کمپانی) تعلق دارد.
/// <para>
/// هر رکورد از این موجودیت‌ها فقط در شرکت فعالِ انتخاب‌شده در بالای نرم‌افزار دیده می‌شود.
/// فیلتر خودکار در <see cref="AppDbContext"/> اعمال می‌شود (HasQueryFilter) و مقدار
/// CompanyId هنگام ذخیره‌سازی به‌صورت خودکار از زمینهٔ درخواست پر می‌شود.
/// </para>
/// </summary>
public interface ICompanyOwned
{
    /// <summary>شناسه شرکت مالک رکورد.</summary>
    int? CompanyId { get; set; }
}
