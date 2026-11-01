using MoudianMiddleware.Contracts.Enums;

namespace MoudianMiddleware.Contracts.ViewModels.Invoices;

/// <summary>شناسه‌های تولیدشده‌ی صورتحساب آماده‌ی ارسال.</summary>
public sealed class InvoicePreparationResult
{
    /// <summary>شناسه‌ی پایگاه داده‌ی صورتحساب.</summary>
    public int InvoiceId { get; init; }
    /// <summary>TaxId تولیدشده با provider SDK 2.0.29.</summary>
    public string TaxId { get; init; } = string.Empty;
    /// <summary>serial شانزده‌شانزده‌ای ده‌رقمی.</summary>
    public string Serial { get; init; } = string.Empty;
    /// <summary>وضعیت صورتحساب پس از آماده‌سازی.</summary>
    public InvoiceStatus Status { get; init; }
}
