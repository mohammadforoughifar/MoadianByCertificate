namespace MoudianMiddleware.DataLayer.Abstractions;

/// <summary>
/// تأمین‌کننده‌ی شناسه‌ی مستأجر (مودی) جاری.
/// </summary>
/// <remarks>
/// این قرارداد در DataLayer تعریف می‌شود تا لایه‌ی داده به لایه‌های بالاتر وابسته نشود
/// (اصل Dependency Inversion). پیاده‌سازی آن در CoreLayer و از روی Claim توکن یا کلید API است.
/// مقدار <c>null</c> یعنی «مستأجر مشخص نیست» و فیلترهای سراسری در این حالت هیچ داده‌ای برنمی‌گردانند.
/// </remarks>
public interface ITenantProvider
{
    /// <summary>شناسه‌ی مستأجر جاری یا <c>null</c>.</summary>
    int? TenantId { get; }
}
