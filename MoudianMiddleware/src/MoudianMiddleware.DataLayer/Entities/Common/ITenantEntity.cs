namespace MoudianMiddleware.DataLayer.Entities.Common;

/// <summary>
/// موجودیت‌های متعلق به یک مستأجر (مودی)؛ مبنای جداسازی داده در حالت چند‌مستأجری.
/// </summary>
/// <remarks>
/// برای همه‌ی این موجودیت‌ها در <c>MoudianDbContext</c> فیلتر سراسری اعمال می‌شود؛
/// بنابراین کوئری‌های عادی هرگز داده‌ی مستأجر دیگر را برنمی‌گردانند.
/// </remarks>
public interface ITenantEntity
{
    /// <summary>شناسه‌ی مستأجر (مودی) مالک رکورد؛ پس از ایجاد تغییرناپذیر است.</summary>
    int TenantId { get; set; }
}
