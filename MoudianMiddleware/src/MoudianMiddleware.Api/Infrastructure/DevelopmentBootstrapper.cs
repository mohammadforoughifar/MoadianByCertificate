using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using MoudianMiddleware.DataLayer.Abstractions;
using MoudianMiddleware.DataLayer.Context;
using MoudianMiddleware.DataLayer.Entities;
using MoudianMiddleware.DataLayer.Seed;

namespace MoudianMiddleware.Api.Infrastructure;

/// <summary>Initializes an isolated LocalDB-backed demo tenant and login, only for Development.</summary>
public sealed class DevelopmentBootstrapper(
    MoudianDbContext db,
    ITenantScope tenantScope,
    IDatabaseSeeder seeder,
    IPasswordHasher passwordHasher,
    IConfiguration configuration,
    IWebHostEnvironment environment,
    ILogger<DevelopmentBootstrapper> logger)
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (!environment.IsDevelopment())
            throw new InvalidOperationException("DevelopmentBootstrapper must never run outside Development.");

        // This is deliberately Development-only. Production databases must use an explicit schema deployment process.
        await db.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);

        var tenant = await db.Tenants.OrderBy(x => x.Id).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (tenant is null)
        {
            tenant = new Tenant
            {
                Name = Environment.GetEnvironmentVariable("MOUDIAN__DEV_TENANT_NAME") ?? "مودی آزمایشی"
            };
            await db.Tenants.AddAsync(tenant, cancellationToken).ConfigureAwait(false);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        using var tenantLease = tenantScope.Enter(tenant.Id);
        await seeder.SeedAsync(cancellationToken).ConfigureAwait(false);
        await EnsureDevelopmentAdminAsync(tenant.Id, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Development database is ready for tenant {TenantId} ({TenantName}).", tenant.Id, tenant.Name);
    }

    private async Task EnsureDevelopmentAdminAsync(int tenantId, CancellationToken cancellationToken)
    {
        var userName = configuration["DatabaseSeed:AdminUserName"];
        if (string.IsNullOrWhiteSpace(userName)) userName = "admin";
        var normalizedUserName = userName.Trim().ToUpperInvariant();
        var user = await db.Users.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.NormalizedUserName == normalizedUserName, cancellationToken)
            .ConfigureAwait(false);

        if (user is { IsDeleted: true })
            throw new InvalidOperationException($"The Development login '{userName}' is soft-deleted. Use a different DatabaseSeed:AdminUserName or reset the local Development database.");

        var role = await db.Roles.SingleOrDefaultAsync(x => x.NormalizedName == "SUPERADMIN", cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("The SuperAdmin development role was not seeded.");

        string? generatedPassword = null;
        if (user is null)
        {
            var password = configuration["DatabaseSeed:AdminPassword"];
            if (string.IsNullOrWhiteSpace(password))
                password = generatedPassword = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(24));

            user = new User
            {
                UserName = userName.Trim(),
                NormalizedUserName = normalizedUserName,
                FullName = configuration["DatabaseSeed:AdminFullName"] ?? "مدیر آزمایشی",
                PasswordHash = passwordHasher.Hash(password),
                IsActive = true,
                IsSuperAdmin = true
            };
            await db.Users.AddAsync(user, cancellationToken).ConfigureAwait(false);
        }

        if (!user.IsActive)
            throw new InvalidOperationException($"The Development login '{userName}' is inactive.");

        var membership = await db.UserTenants.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.UserId == user.Id && x.TenantId == tenantId, cancellationToken)
            .ConfigureAwait(false);
        if (membership is { IsDeleted: true } or { IsActive: false })
            throw new InvalidOperationException($"The Development login '{userName}' has an inactive tenant membership.");

        if (membership is null)
        {
            user.UserTenants.Add(new UserTenant
            {
                TenantId = tenantId,
                RoleId = role.Id,
                Role = role,
                IsActive = true
            });
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        if (generatedPassword is not null)
        {
            Console.WriteLine("============================================================");
            Console.WriteLine("Created a local Development administrator. Save this password now; it is printed only once.");
            Console.WriteLine($"Username: {userName.Trim()}");
            Console.WriteLine($"Password: {generatedPassword}");
            Console.WriteLine("============================================================");
        }
    }
}
