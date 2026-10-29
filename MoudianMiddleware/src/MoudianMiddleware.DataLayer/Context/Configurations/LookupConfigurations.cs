using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MoudianMiddleware.DataLayer.Entities;

namespace MoudianMiddleware.DataLayer.Context.Configurations;

/// <summary>
/// نگاشت <see cref="InvoicePattern"/> (الگوی صورتحساب).
/// </summary>
public sealed class InvoicePatternConfiguration : IEntityTypeConfiguration<InvoicePattern>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<InvoicePattern> builder)
    {
        builder.Property(x => x.Title).HasMaxLength(300).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(1000);
        builder.HasIndex(x => x.Code).IsUnique();
    }
}

/// <summary>
/// نگاشت <see cref="InvoiceSubject"/> (موضوع صورتحساب).
/// </summary>
public sealed class InvoiceSubjectConfiguration : IEntityTypeConfiguration<InvoiceSubject>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<InvoiceSubject> builder)
    {
        builder.Property(x => x.Title).HasMaxLength(300).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(1000);
        builder.HasIndex(x => x.Code).IsUnique();
    }
}

/// <summary>
/// نگاشت <see cref="PaymentMethodCode"/> (نگاشت روش پرداخت به کد pmt).
/// </summary>
public sealed class PaymentMethodCodeConfiguration : IEntityTypeConfiguration<PaymentMethodCode>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PaymentMethodCode> builder)
    {
        builder.Property(x => x.Title).HasMaxLength(100).IsRequired();
        builder.HasIndex(x => x.MethodCode).IsUnique();
    }
}

/// <summary>
/// نگاشت <see cref="MoadianErrorCode"/> (کدهای خطای سامانه).
/// </summary>
public sealed class MoadianErrorCodeConfiguration : IEntityTypeConfiguration<MoadianErrorCode>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<MoadianErrorCode> builder)
    {
        builder.Property(x => x.Code).HasMaxLength(50).IsRequired();
        builder.Property(x => x.PersianMessage).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.TechnicalMessage).HasMaxLength(1000);
        builder.Property(x => x.Severity).HasMaxLength(20).IsRequired();
        builder.Property(x => x.SuggestedAction).HasMaxLength(500);

        builder.HasIndex(x => x.Code).IsUnique();
    }
}
