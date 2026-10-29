using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MoudianMiddleware.DataLayer.Abstractions;
using MoudianMiddleware.DataLayer.Context;
using MoudianMiddleware.DataLayer.Entities;
using MoudianMiddleware.DataLayer.Seed.SeedData;

namespace MoudianMiddleware.DataLayer.Seed;

/// <summary>
/// پیاده‌سازی Seed اولیه‌ی داده‌های پایه.
/// </summary>
/// <remarks>
/// <para>ترتیب اجرا: نقش‌ها و Permissionها → Lookupها → کاربر مدیر.</para>
/// <para>همه‌ی مراحل Idempotent هستند؛ اجرای دوباره داده‌ی تکراری ایجاد نمی‌کند.</para>
/// </remarks>
public sealed class DatabaseSeeder : IDatabaseSeeder
{
    private readonly MoudianDbContext _context;
    private readonly IPasswordHasher _passwordHasher;
    private readonly DatabaseSeedOptions _options;
    private readonly ILogger<DatabaseSeeder> _logger;

    /// <summary>
    /// سازنده با تزریق وابستگی‌ها.
    /// </summary>
    /// <param name="context">Context دیتابیس.</param>
    /// <param name="passwordHasher">هش‌کننده‌ی رمز عبور (پیاده‌سازی CoreLayer).</param>
    /// <param name="options">تنظیمات Seed (کاربر مدیر).</param>
    /// <param name="logger">لاگر.</param>
    public DatabaseSeeder(
        MoudianDbContext context,
        IPasswordHasher passwordHasher,
        IOptions<DatabaseSeedOptions> options,
        ILogger<DatabaseSeeder> logger)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await SeedRolesAsync(cancellationToken).ConfigureAwait(false);
        await SeedLookupsAsync(cancellationToken).ConfigureAwait(false);
        await SeedAdminUserAsync(cancellationToken).ConfigureAwait(false);

        var affected = await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Seed پایه انجام شد؛ تعداد تغییرات: {Count}", affected);
    }

    private async Task SeedRolesAsync(CancellationToken cancellationToken)
    {
        var existingRoles = await _context.Roles
            .Include(x => x.Permissions)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var (name, description, permissions) in LookupSeedData.Roles())
        {
            var normalizedName = name.ToUpperInvariant();
            var role = existingRoles.FirstOrDefault(x => x.NormalizedName == normalizedName);

            if (role is null)
            {
                role = new Role
                {
                    Name = name,
                    NormalizedName = normalizedName,
                    Description = description,
                    IsSystem = true
                };

                foreach (var permission in permissions)
                {
                    role.Permissions.Add(new RolePermission { Permission = permission });
                }

                await _context.Roles.AddAsync(role, cancellationToken).ConfigureAwait(false);
                continue;
            }

            var existingPermissions = role.Permissions.Select(x => x.Permission).ToHashSet(StringComparer.Ordinal);

            foreach (var permission in permissions.Where(p => !existingPermissions.Contains(p)))
            {
                role.Permissions.Add(new RolePermission { RoleId = role.Id, Permission = permission });
            }
        }
    }

    private async Task SeedLookupsAsync(CancellationToken cancellationToken)
    {
        var patternCodes = await _context.InvoicePatterns
            .Select(x => x.Code).ToListAsync(cancellationToken).ConfigureAwait(false);

        foreach (var pattern in LookupSeedData.InvoicePatterns().Where(x => !patternCodes.Contains(x.Code)))
        {
            await _context.InvoicePatterns.AddAsync(pattern, cancellationToken).ConfigureAwait(false);
        }

        var subjectCodes = await _context.InvoiceSubjects
            .Select(x => x.Code).ToListAsync(cancellationToken).ConfigureAwait(false);

        foreach (var subject in LookupSeedData.InvoiceSubjects().Where(x => !subjectCodes.Contains(x.Code)))
        {
            await _context.InvoiceSubjects.AddAsync(subject, cancellationToken).ConfigureAwait(false);
        }

        var paymentMethodCodes = await _context.PaymentMethodCodes
            .Select(x => x.MethodCode).ToListAsync(cancellationToken).ConfigureAwait(false);

        foreach (var payment in LookupSeedData.PaymentMethods().Where(x => !paymentMethodCodes.Contains(x.MethodCode)))
        {
            await _context.PaymentMethodCodes.AddAsync(payment, cancellationToken).ConfigureAwait(false);
        }

        var unitCodes = await _context.MeasuringUnits
            .Select(x => x.Code).ToListAsync(cancellationToken).ConfigureAwait(false);

        foreach (var unit in LookupSeedData.MeasuringUnits().Where(x => !unitCodes.Contains(x.Code)))
        {
            await _context.MeasuringUnits.AddAsync(unit, cancellationToken).ConfigureAwait(false);
        }

        var errorCodes = await _context.MoadianErrorCodes
            .Select(x => x.Code).ToListAsync(cancellationToken).ConfigureAwait(false);

        foreach (var errorCode in LookupSeedData.ErrorCodes().Where(x => !errorCodes.Contains(x.Code)))
        {
            await _context.MoadianErrorCodes.AddAsync(errorCode, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task SeedAdminUserAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.AdminUserName) || string.IsNullOrWhiteSpace(_options.AdminPassword))
        {
            _logger.LogWarning(
                "کاربر مدیر ساخته نشد: DatabaseSeed:AdminUserName و DatabaseSeed:AdminPassword تنظیم نشده‌اند.");
            return;
        }

        var normalizedUserName = _options.AdminUserName.Trim().ToUpperInvariant();

        var exists = await _context.Users
            .AnyAsync(x => x.NormalizedUserName == normalizedUserName, cancellationToken)
            .ConfigureAwait(false);

        if (exists)
        {
            return;
        }

        var admin = new User
        {
            UserName = _options.AdminUserName.Trim(),
            NormalizedUserName = normalizedUserName,
            FullName = string.IsNullOrWhiteSpace(_options.AdminFullName) ? "مدیر سیستم" : _options.AdminFullName,
            Email = _options.AdminEmail,
            PasswordHash = _passwordHasher.Hash(_options.AdminPassword),
            IsActive = true,
            IsSuperAdmin = true
        };

        await _context.Users.AddAsync(admin, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("کاربر مدیر «{UserName}» ساخته شد.", admin.UserName);
    }
}
