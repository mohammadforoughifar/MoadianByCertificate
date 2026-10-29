using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MoudianMiddleware.DataLayer.Entities;

namespace MoudianMiddleware.DataLayer.Context.Configurations;

/// <summary>
/// نگاشت <see cref="AuditLog"/> (بدون فیلتر مستأجر؛ دسترسی با Permission کنترل می‌شود).
/// </summary>
public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.Property(x => x.EntityName).HasMaxLength(100);
        builder.Property(x => x.EntityId).HasMaxLength(50);
        builder.Property(x => x.Description).HasMaxLength(2000).IsRequired();
        builder.Property(x => x.IpAddress).HasMaxLength(60);
        builder.Property(x => x.UserAgent).HasMaxLength(400);
        builder.Property(x => x.CorrelationId).HasMaxLength(100);
        builder.Property(x => x.AdditionalData).HasMaxLength(4000);

        builder.HasIndex(x => new { x.TenantId, x.CreatedAt });
        builder.HasIndex(x => new { x.EntityName, x.EntityId });
        builder.HasIndex(x => new { x.UserId, x.CreatedAt });
    }
}

/// <summary>
/// نگاشت <see cref="ApiClient"/> (کلید API سیستم‌های خارجی).
/// </summary>
public sealed class ApiClientConfiguration : IEntityTypeConfiguration<ApiClient>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ApiClient> builder)
    {
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.KeyPrefix).HasMaxLength(20).IsRequired();
        builder.Property(x => x.KeyHash).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Scopes).HasConversion<int>();

        builder.HasIndex(x => x.KeyPrefix).IsUnique();
        builder.HasIndex(x => x.KeyHash).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.IsActive });
    }
}

/// <summary>
/// نگاشت <see cref="OutboxMessage"/> برای پردازش پس‌زمینه.
/// </summary>
public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.Property(x => x.Type).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Payload).IsRequired();
        builder.Property(x => x.LastError).HasMaxLength(2000);
        builder.Property(x => x.IdempotencyKey).HasMaxLength(150);

        builder.HasIndex(x => new { x.Status, x.NextRetryAt });
        builder.HasIndex(x => new { x.TenantId, x.InvoiceId });
        builder.HasIndex(x => new { x.TenantId, x.IdempotencyKey })
            .IsUnique()
            .HasFilter("[IdempotencyKey] IS NOT NULL");
    }
}
