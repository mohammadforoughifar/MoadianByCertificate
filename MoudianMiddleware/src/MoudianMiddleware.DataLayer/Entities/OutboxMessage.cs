using MoudianMiddleware.Contracts.Enums;
using MoudianMiddleware.DataLayer.Entities.Common;

namespace MoudianMiddleware.DataLayer.Entities;

/// <summary>
/// پیام Outbox برای ارسال قابل‌اطمینان به سامانه‌ی مودیان (در صورت قطعی شبکه/سامانه از دست نمی‌رود).
/// </summary>
/// <remarks>
/// الگو: در همان تراکنش ثبت صورتحساب، پیام Outbox ثبت می‌شود؛ سپس BackgroundService آن را پردازش می‌کند.
/// </remarks>
public class OutboxMessage : BaseEntity, ITenantEntity
{
    /// <inheritdoc />
    public int TenantId { get; set; }

    /// <summary>نوع پیام (مثلاً SendInvoice).</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>محتوای JSON پیام.</summary>
    public string Payload { get; set; } = string.Empty;

    /// <summary>وضعیت پردازش.</summary>
    public OutboxStatus Status { get; set; } = OutboxStatus.Pending;

    /// <summary>زمان رخداد/ثبت.</summary>
    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>زمان پردازش موفق.</summary>
    public DateTimeOffset? ProcessedAt { get; set; }

    /// <summary>تعداد تلاش‌های انجام‌شده.</summary>
    public int RetryCount { get; set; }

    /// <summary>زمان تلاش بعدی (برای backoff).</summary>
    public DateTimeOffset? NextRetryAt { get; set; }

    /// <summary>آخرین خطا.</summary>
    public string? LastError { get; set; }

    /// <summary>شناسه‌ی صورتحساب مرتبط (در صورت وجود).</summary>
    public int? InvoiceId { get; set; }

    /// <summary>کلید یکتای Idempotency برای جلوگیری از پردازش دوباره.</summary>
    public string? IdempotencyKey { get; set; }
}
