using Microsoft.EntityFrameworkCore;
using TaxCollectData.Library.Factories;
using MoudianMiddleware.Contracts.Enums;
using MoudianMiddleware.Contracts.ViewModels.Invoices;
using MoudianMiddleware.CoreLayer.Common.Exceptions;
using MoudianMiddleware.CoreLayer.Interfaces.Services;
using MoudianMiddleware.DataLayer.Context;
using MoudianMiddleware.DataLayer.Entities;

namespace MoudianMiddleware.CoreLayer.Services.Invoices;

/// <summary>تولید TaxId و serial معتبر از SDK و انتقال draft به ReadyToSend.</summary>
public sealed class InvoicePreparationService : IInvoicePreparationService
{
    /// <summary>ساخت سرویس آماده‌سازی صورتحساب.</summary>
    public InvoicePreparationService(MoudianDbContext context) => _context = context;

    private readonly MoudianDbContext _context;

    /// <inheritdoc />
    public async Task<InvoicePreparationResult> PrepareAsync(int invoiceId, CancellationToken cancellationToken = default)
    {
        var invoice = await _context.Invoices
            .Include(x => x.Items)
            .Include(x => x.Payments)
            .SingleOrDefaultAsync(x => x.Id == invoiceId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new EntityNotFoundException(nameof(Invoice), invoiceId);

        if (invoice.Status == InvoiceStatus.ReadyToSend
            && !string.IsNullOrWhiteSpace(invoice.TaxId)
            && !string.IsNullOrWhiteSpace(invoice.Serial))
        {
            return Map(invoice);
        }
        if (invoice.Status != InvoiceStatus.Draft)
        {
            throw new InvalidOperationException("فقط پیش‌نویس قابل آماده‌سازی است.");
        }
        if (!string.IsNullOrWhiteSpace(invoice.TaxId) || !string.IsNullOrWhiteSpace(invoice.Serial))
        {
            throw new InvalidOperationException("پیش‌نویس دارای TaxId/Serial ناقص است؛ آماده‌سازی خودکار آن امن نیست.");
        }
        if (invoice.Items.Count == 0)
        {
            throw new InvalidOperationException("صورتحساب بدون قلم قابل آماده‌سازی نیست.");
        }
        if (invoice.IssueDate == default)
        {
            throw new InvalidOperationException("تاریخ صدور صورتحساب معتبر نیست.");
        }
        if (invoice.Type != InvoiceType.Original && string.IsNullOrWhiteSpace(invoice.ReferenceTaxId))
        {
            throw new InvalidOperationException("صورتحساب غیر اصلی باید شناسه‌ی مالیاتی مرجع داشته باشد.");
        }

        var setting = await _context.MoadianSettings.SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new EntityNotFoundException(nameof(MoadianSetting), invoice.TenantId);
        if (!setting.IsActive)
        {
            throw new InvalidOperationException("تنظیمات مودیان غیرفعال است.");
        }

        // Invoice.Id is a database-allocated, globally unique, monotonic serial source; it is never client supplied.
        // SDK 2.0.29 computes its TaxId (including Verhoeff check digit) from MemoryId, serial, and issue date.
        var serialNumber = (long)invoice.Id;
        var serial = serialNumber.ToString("X10", System.Globalization.CultureInfo.InvariantCulture);
        var taxId = new TaxIdProviderFactory().Create()
            .GenerateTaxId(setting.MemoryId, serialNumber, invoice.IssueDate.UtcDateTime);
        if (taxId.Length != 22 || serial.Length != 10)
        {
            throw new InvalidOperationException("SDK شناسه‌ی مالیاتی/سریال با طول مورد انتظار تولید نکرد.");
        }

        invoice.TaxId = taxId;
        invoice.Serial = serial;
        invoice.Status = InvoiceStatus.ReadyToSend;
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Map(invoice);
    }

    private static InvoicePreparationResult Map(Invoice invoice) => new()
    {
        InvoiceId = invoice.Id,
        TaxId = invoice.TaxId!,
        Serial = invoice.Serial!,
        Status = invoice.Status
    };
}
