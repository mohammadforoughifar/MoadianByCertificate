using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MoudianMiddleware.DataLayer.Entities;

namespace MoudianMiddleware.DataLayer.Context.Configurations;

/// <summary>
/// نگاشت <see cref="Invoice"/> با ایندکس‌های لازم برای فیلترهای گزارش و Idempotency.
/// </summary>
public sealed class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.Property(x => x.InvoiceNumber).HasMaxLength(50).IsRequired();
        builder.Property(x => x.ExternalId).HasMaxLength(100);
        builder.Property(x => x.BuyerName).HasMaxLength(300).IsRequired();
        builder.Property(x => x.BuyerNationalId).HasMaxLength(20);
        builder.Property(x => x.BuyerEconomicCode).HasMaxLength(20);
        builder.Property(x => x.BuyerPostalCode).HasMaxLength(20);
        builder.Property(x => x.BuyerAddress).HasMaxLength(1000);
        builder.Property(x => x.BuyerBranchCode).HasMaxLength(50);
        builder.Property(x => x.TaxId).HasMaxLength(30);
        builder.Property(x => x.Serial).HasMaxLength(20);
        builder.Property(x => x.ReferenceTaxId).HasMaxLength(30);
        builder.Property(x => x.Uid).HasMaxLength(50);
        builder.Property(x => x.ReferenceNumber).HasMaxLength(100);
        builder.Property(x => x.LastErrorCode).HasMaxLength(50);
        builder.Property(x => x.LastErrorMessage).HasMaxLength(1000);
        builder.Property(x => x.Notes).HasMaxLength(2000);

        // شماره‌ی داخلی فاکتور در هر مودی یکتاست
        builder.HasIndex(x => new { x.TenantId, x.InvoiceNumber }).IsUnique();

        // Idempotency سیستم‌های خارجی: ExternalId در هر مودی یکتا (فقط وقتی مقدار دارد)
        builder.HasIndex(x => new { x.TenantId, x.ExternalId })
            .IsUnique()
            .HasFilter("[ExternalId] IS NOT NULL");

        // شماره‌ی مالیاتی یکتاست (فقط پس از صدور مقدار دارد)
        builder.HasIndex(x => new { x.TenantId, x.TaxId })
            .IsUnique()
            .HasFilter("[TaxId] IS NOT NULL");

        // ایندکس‌های گزارش‌گیری و Jobها
        builder.HasIndex(x => new { x.TenantId, x.Status });
        builder.HasIndex(x => new { x.TenantId, x.IssueDate });
        builder.HasIndex(x => new { x.TenantId, x.CustomerId });
        builder.HasIndex(x => x.ReferenceNumber);

        builder.HasOne(x => x.Customer)
            .WithMany()
            .HasForeignKey(x => x.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.Items)
            .WithOne(i => i.Invoice)
            .HasForeignKey(i => i.InvoiceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Payments)
            .WithOne(p => p.Invoice)
            .HasForeignKey(p => p.InvoiceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Submissions)
            .WithOne(s => s.Invoice)
            .HasForeignKey(s => s.InvoiceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>
/// نگاشت <see cref="InvoiceItem"/> (اقلام صورتحساب).
/// </summary>
public sealed class InvoiceItemConfiguration : IEntityTypeConfiguration<InvoiceItem>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<InvoiceItem> builder)
    {
        builder.Property(x => x.Sstid).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(500).IsRequired();
        builder.Property(x => x.MeasuringUnitCode).HasMaxLength(20).IsRequired();
        builder.Property(x => x.OtherTaxSubject).HasMaxLength(200);
        builder.Property(x => x.OtherLegalFeeSubject).HasMaxLength(200);
        builder.Property(x => x.HsCode).HasMaxLength(30);

        builder.Property(x => x.Quantity).HasPrecision(18, 4);
        builder.Property(x => x.UnitPrice).HasPrecision(18, 4);
        builder.Property(x => x.VatRate).HasPrecision(5, 2);
        builder.Property(x => x.OtherTaxRate).HasPrecision(5, 2);
        builder.Property(x => x.OtherLegalFeeRate).HasPrecision(5, 2);
        builder.Property(x => x.NetWeight).HasPrecision(18, 4);

        builder.HasIndex(x => new { x.InvoiceId, x.RowNumber });
        builder.HasIndex(x => new { x.TenantId, x.Sstid });
    }
}

/// <summary>
/// نگاشت <see cref="InvoicePayment"/> (ردیف‌های پرداخت صورتحساب).
/// </summary>
public sealed class InvoicePaymentConfiguration : IEntityTypeConfiguration<InvoicePayment>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<InvoicePayment> builder)
    {
        builder.Property(x => x.ReferenceNumber).HasMaxLength(100);
        builder.Property(x => x.TerminalNumber).HasMaxLength(50);
        builder.Property(x => x.AcceptorNumber).HasMaxLength(50);
        builder.Property(x => x.SwitchNumber).HasMaxLength(50);
        builder.Property(x => x.PayerNationalId).HasMaxLength(20);
        builder.Property(x => x.MaskedCardNumber).HasMaxLength(30);
        builder.Property(x => x.VatPaymentId).HasMaxLength(100);
        builder.Property(x => x.VatBillId).HasMaxLength(100);

        builder.HasIndex(x => x.InvoiceId);
    }
}

/// <summary>
/// نگاشت <see cref="InvoiceSubmission"/> (تاریخچه‌ی تلاش‌های ارسال/استعلام).
/// </summary>
public sealed class InvoiceSubmissionConfiguration : IEntityTypeConfiguration<InvoiceSubmission>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<InvoiceSubmission> builder)
    {
        builder.Property(x => x.Uid).HasMaxLength(50);
        builder.Property(x => x.ReferenceNumber).HasMaxLength(100);
        builder.Property(x => x.MoadianStatus).HasMaxLength(30);
        builder.Property(x => x.ErrorCode).HasMaxLength(50);
        builder.Property(x => x.ErrorMessage).HasMaxLength(1000);
        builder.Property(x => x.CorrelationId).HasMaxLength(100);

        builder.HasIndex(x => new { x.InvoiceId, x.StartedAt });
        builder.HasIndex(x => new { x.TenantId, x.Status });
        builder.HasIndex(x => x.Uid);
    }
}
