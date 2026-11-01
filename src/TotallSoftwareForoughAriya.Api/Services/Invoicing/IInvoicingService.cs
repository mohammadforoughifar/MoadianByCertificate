using Inventory.Api.Services;
using Inventory.Shared;
using Inventory.Shared.Dtos;

namespace Inventory.Api.Services.Invoicing;

/// <summary>
/// سرویس ماژول فاکتور: صدور فاکتور خرید/فروش و برگشتی‌ها،
/// به‌همراه صدور خودکار سند انبار و سند حسابداری هنگام قطعی شدن.
/// </summary>
public interface IInvoicingService
{
    // ---------- فاکتورها ----------
    Task<PagedResult<FacInvoice>> GetInvoicesAsync(InvoiceKind? kind, InvoiceStatus? status, int? partyId,
        int? warehouseId, string? search, DateTime? from, DateTime? to, int page, int pageSize);

    Task<FacInvoice?> GetInvoiceAsync(int id);

    /// <summary>فاکتور خالی با مقادیر پیش‌فرض (انبار پیش‌فرض، تاریخ امروز، شماره بعدی)</summary>
    Task<FacInvoice> NewInvoiceAsync(InvoiceKind kind);

    Task<FacInvoice> SaveInvoiceAsync(FacInvoice dto, string? user);

    /// <summary>قطعی‌سازی: صدور و قطعی کردن سند انبار و سند حسابداری</summary>
    Task<FacInvoice> ConfirmInvoiceAsync(int id, string? user);

    /// <summary>برگشت به پیش‌نویس: حذف اسناد خودکار</summary>
    Task<FacInvoice> UnconfirmInvoiceAsync(int id, string? user);

    Task<FacInvoice> CancelInvoiceAsync(int id, string? user);
    Task DeleteInvoiceAsync(int id);

    /// <summary>سطر آماده برای یک کالا: قیمت، نرخ مالیات، کد مالیاتی و موجودی</summary>
    Task<FacInvoiceLine> BuildLineAsync(int productId, InvoiceKind kind, int warehouseId);

    // ---------- قواعد ----------
    Task<List<FacRule>> GetRulesAsync(Paging.Request? pagination = null);
    Task<FacRule> SaveRuleAsync(FacRule dto);

    // ---------- سند خودکار «فروش فروشگاه» ----------

    /// <summary>
    /// سند حسابداری خودکار سند فروشِ ثبت‌شده از سفارش ویزیتور (پس از تأیید مالی).
    /// <para>
    /// نقدی/پیش‌دریافت → بدهکار صندوق یا بانک، چک و اقساط → بدهکار حساب دریافتنی،
    /// و بستانکار حساب «فروش کالا». اگر قاعدهٔ فاکتور فروش فعال نباشد یا سند قبلاً
    /// زده شده باشد، <c>null</c> برمی‌گردد.
    /// </para>
    /// </summary>
    Task<int?> PostStoreSaleVoucherAsync(int saleId, string? user);

    /// <summary>حذف سند خودکار «فروش فروشگاه» (وقتی ثبت فروش برمی‌گردد).</summary>
    Task UnpostStoreSaleVoucherAsync(int saleId);

    // ---------- گزارش‌ها ----------
    Task<FacSummaryResult> GetSummaryAsync(InvoiceKind kind, string groupBy, DateTime? from, DateTime? to);
    Task<FacDashboard> GetDashboardAsync(DateTime? from, DateTime? to);
}
