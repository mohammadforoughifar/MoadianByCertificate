using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MoudianMiddleware.DataLayer.Entities;

namespace MoudianMiddleware.DataLayer.Context.Configurations;

/// <summary>
/// نگاشت <see cref="MeasuringUnit"/> (داده‌ی مرجع سراسری).
/// </summary>
public sealed class MeasuringUnitConfiguration : IEntityTypeConfiguration<MeasuringUnit>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<MeasuringUnit> builder)
    {
        builder.Property(x => x.Code).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Title).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Symbol).HasMaxLength(20);

        builder.HasIndex(x => x.Code).IsUnique();
    }
}

/// <summary>
/// نگاشت <see cref="Product"/> (کالا/خدمت هر مودی).
/// </summary>
public sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.Property(x => x.Sstid).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Title).HasMaxLength(300).IsRequired();
        builder.Property(x => x.HsCode).HasMaxLength(30);
        builder.Property(x => x.Description).HasMaxLength(1000);

        // مبالغ و نرخ‌ها: دقت بالا برای جلوگیری از خطای گرد کردن در مالیات
        builder.Property(x => x.UnitPrice).HasPrecision(18, 4);
        builder.Property(x => x.VatRate).HasPrecision(5, 2);

        // شناسه‌ی کالا/خدمت در سطح هر مودی یکتاست
        builder.HasIndex(x => new { x.TenantId, x.Sstid }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.Title });

        builder.HasOne(x => x.MeasuringUnit)
            .WithMany()
            .HasForeignKey(x => x.MeasuringUnitId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// نگاشت <see cref="Customer"/> (خریدار).
/// </summary>
public sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.Property(x => x.Code).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(300).IsRequired();
        builder.Property(x => x.NationalId).HasMaxLength(20);
        builder.Property(x => x.EconomicCode).HasMaxLength(20);
        builder.Property(x => x.PostalCode).HasMaxLength(20);
        builder.Property(x => x.Address).HasMaxLength(1000);
        builder.Property(x => x.Phone).HasMaxLength(30);
        builder.Property(x => x.Email).HasMaxLength(200);
        builder.Property(x => x.ExternalCode).HasMaxLength(100);

        builder.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.NationalId });
        builder.HasIndex(x => new { x.TenantId, x.Name });
    }
}
