using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MoudianMiddleware.DataLayer.Entities;

namespace MoudianMiddleware.DataLayer.Context.Configurations;

/// <summary>
/// نگاشت <see cref="User"/> به جدول <c>Users</c>.
/// </summary>
public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.Property(x => x.UserName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.NormalizedUserName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.FullName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Email).HasMaxLength(200);
        builder.Property(x => x.PasswordHash).HasMaxLength(500).IsRequired();

        builder.HasIndex(x => x.NormalizedUserName).IsUnique();
        builder.HasIndex(x => x.Email);
    }
}

/// <summary>
/// نگاشت <see cref="Role"/> و <see cref="RolePermission"/>.
/// </summary>
public sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.Property(x => x.Name).HasMaxLength(100).IsRequired();
        builder.Property(x => x.NormalizedName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(300);

        builder.HasIndex(x => x.NormalizedName).IsUnique();
    }
}

/// <summary>
/// نگاشت <see cref="RolePermission"/> به جدول <c>RolePermissions</c>.
/// </summary>
public sealed class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        builder.Property(x => x.Permission).HasMaxLength(100).IsRequired();
        builder.HasIndex(x => new { x.RoleId, x.Permission }).IsUnique();

        builder.HasOne(x => x.Role)
            .WithMany(r => r.Permissions)
            .HasForeignKey(x => x.RoleId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>
/// نگاشت <see cref="UserTenant"/> (عضویت کاربر در مودی).
/// </summary>
public sealed class UserTenantConfiguration : IEntityTypeConfiguration<UserTenant>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<UserTenant> builder)
    {
        builder.HasIndex(x => new { x.UserId, x.TenantId, x.RoleId }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.UserId });

        builder.HasOne(x => x.User)
            .WithMany(u => u.UserTenants)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Tenant)
            .WithMany(t => t.UserTenants)
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Role)
            .WithMany()
            .HasForeignKey(x => x.RoleId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// نگاشت <see cref="RefreshToken"/>.
/// </summary>
public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.Property(x => x.TokenHash).HasMaxLength(200).IsRequired();
        builder.Property(x => x.ReplacedByTokenHash).HasMaxLength(200);
        builder.Property(x => x.CreatedByIp).HasMaxLength(60);
        builder.Property(x => x.RevokedByIp).HasMaxLength(60);
        builder.Property(x => x.UserAgent).HasMaxLength(400);

        builder.HasIndex(x => x.TokenHash).IsUnique();
        builder.HasIndex(x => new { x.UserId, x.ExpiresAt });

        builder.HasOne(x => x.User)
            .WithMany(u => u.RefreshTokens)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
