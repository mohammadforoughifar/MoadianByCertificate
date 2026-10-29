namespace MoudianMiddleware.DataLayer.Entities.Common;

/// <summary>
/// موجودیت دارای فیلدهای حسابرسی و حذف نرم.
/// </summary>
/// <remarks>
/// مقادیر <see cref="CreatedAt"/>، <see cref="CreatedByUserId"/> و همتایان Modified
/// به‌صورت خودکار توسط <c>AuditableEntityInterceptor</c> پر می‌شوند؛ در سرویس‌ها آن‌ها را دستی ست نکنید.
/// </remarks>
public abstract class AuditableEntity : BaseEntity, ISoftDeletable
{
    /// <summary>زمان ایجاد (UTC).</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>شناسه‌ی کاربر ایجادکننده؛ <c>null</c> برای عملیات سیستمی.</summary>
    public int? CreatedByUserId { get; set; }

    /// <summary>زمان آخرین ویرایش (UTC).</summary>
    public DateTimeOffset? ModifiedAt { get; set; }

    /// <summary>شناسه‌ی کاربر آخرین ویرایش‌کننده.</summary>
    public int? ModifiedByUserId { get; set; }

    /// <inheritdoc />
    public bool IsDeleted { get; set; }

    /// <inheritdoc />
    public DateTimeOffset? DeletedAt { get; set; }
}
