using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MoudianMiddleware.DataLayer.Entities;

namespace MoudianMiddleware.DataLayer.Context.Configurations;

/// <summary>
/// نگاشت <see cref="Tenant"/> به جدول <c>Tenants</c>.
/// </summary>
public sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.LegalName).HasMaxLength(300);
        builder.HasIndex(x => x.Name);
    }
}

/// <summary>
/// نگاشت <see cref="MoadianSetting"/> به جدول <c>MoadianSettings</c> (یک‌به‌یک با مودی).
/// </summary>
public sealed class MoadianSettingConfiguration : IEntityTypeConfiguration<MoadianSetting>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<MoadianSetting> builder)
    {
        builder.Property(x => x.MemoryId).HasMaxLength(20).IsRequired();
        builder.Property(x => x.EconomicCode).HasMaxLength(20).IsRequired();
        builder.Property(x => x.NationalId).HasMaxLength(20);
        builder.Property(x => x.BranchCode).HasMaxLength(50);
        builder.Property(x => x.SandboxBaseUrl).HasMaxLength(300);
        builder.Property(x => x.ProductionBaseUrl).HasMaxLength(300);
        builder.Property(x => x.LastConnectionTestMessage).HasMaxLength(1000);

        builder.HasIndex(x => x.TenantId).IsUnique();
        builder.HasIndex(x => x.MemoryId).IsUnique();

        builder.HasOne(x => x.Tenant)
            .WithOne(t => t.MoadianSetting)
            .HasForeignKey<MoadianSetting>(x => x.TenantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>
/// نگاشت <see cref="MoadianCredential"/> به جدول <c>MoadianCredentials</c>.
/// </summary>
public sealed class MoadianCredentialConfiguration : IEntityTypeConfiguration<MoadianCredential>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<MoadianCredential> builder)
    {
        builder.Property(x => x.CertificateBase64).HasMaxLength(8000);
        builder.Property(x => x.CertificateThumbprint).HasMaxLength(100);
        builder.Property(x => x.CertificateSubject).HasMaxLength(500);
        builder.Property(x => x.EncryptedPrivateKey).HasMaxLength(16000);
        builder.Property(x => x.PrivateKeyNonce).HasMaxLength(100);
        builder.Property(x => x.PrivateKeyTag).HasMaxLength(100);
        builder.Property(x => x.Pkcs11LibraryPath).HasMaxLength(500);
        builder.Property(x => x.Pkcs11CertificateAlias).HasMaxLength(200);
        builder.Property(x => x.EncryptedPin).HasMaxLength(1000);
        builder.Property(x => x.PinNonce).HasMaxLength(100);
        builder.Property(x => x.PinTag).HasMaxLength(100);

        builder.HasIndex(x => x.TenantId).IsUnique();

        // کلید/گواهی هر مودی حذف‌شده باید با خود مودی پاک شود (بدون Navigation، چون یک‌به‌یک مستقل است)
        builder.HasOne<Tenant>()
            .WithOne()
            .HasForeignKey<MoadianCredential>(x => x.TenantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
