using MoudianMiddleware.Contracts.ViewModels.Invoices;
using MoudianMiddleware.DataLayer.Entities;

namespace MoudianMiddleware.CoreLayer.Interfaces.Services;

/// <summary>محاسبه‌ی واحد مبالغ صورتحساب و اقلام آن.</summary>
public interface IInvoiceCalculationService
{
    /// <summary>ساخت اقلام از ورودی و محاسبه‌ی مبالغ در ریال با گردکردن AwayFromZero.</summary>
    void Apply(Invoice invoice, IReadOnlyList<InvoiceItemWriteRequest> items);
}
