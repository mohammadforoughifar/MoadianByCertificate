using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MoudianMiddleware.DataLayer.Abstractions.Repositories;
using MoudianMiddleware.DataLayer.Context;
using MoudianMiddleware.DataLayer.Context.Interceptors;
using MoudianMiddleware.DataLayer.Abstractions;
using MoudianMiddleware.DataLayer.Repositories;
using MoudianMiddleware.DataLayer.Seed;
using UnitOfWorkService = MoudianMiddleware.DataLayer.UnitOfWork.UnitOfWork;
using MoudianMiddleware.DataLayer.UnitOfWork;

namespace MoudianMiddleware.DataLayer;

/// <summary>
/// ثبت وابستگی‌های لایه‌ی داده در Container تزریق وابستگی.
/// </summary>
public static class DependencyInjection
{
    /// <summary>نام بخش تنظیمات رشته‌ی اتصال.</summary>
    public const string ConnectionStringName = "Default";

    /// <summary>
    /// افزودن DbContext، Repositoryها، واحد کار و Seeder؛ رشته‌ی اتصال فقط از MOUDIAN__CONNECTION_STRING خوانده می‌شود.
    /// </summary>
    /// <param name="services">مجموعه‌ی سرویس‌ها.</param>
    /// <param name="configuration">تنظیمات برنامه (برای رشته‌ی اتصال).</param>
    /// <returns>همان مجموعه برای زنجیره‌سازی.</returns>
    /// <exception cref="InvalidOperationException">اگر رشته‌ی اتصال تنظیم نشده باشد.</exception>
    public static IServiceCollection AddDataLayer(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = Environment.GetEnvironmentVariable("MOUDIAN__CONNECTION_STRING");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "متغیر محیطی MOUDIAN__CONNECTION_STRING تنظیم نشده است.");
        }

        services.AddScoped<ITenantScope, TenantScope>();
        services.AddScoped<AuditableEntityInterceptor>();
        services.AddScoped<TenantStampInterceptor>();

        services.AddDbContext<MoudianDbContext>((serviceProvider, options) =>
        {
            options.UseSqlServer(connectionString, sql =>
            {
                sql.MigrationsAssembly(typeof(MoudianDbContext).Assembly.FullName);
                sql.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(10), errorNumbersToAdd: null);
            });

            options.AddInterceptors(
                serviceProvider.GetRequiredService<AuditableEntityInterceptor>(),
                serviceProvider.GetRequiredService<TenantStampInterceptor>());
        });

        services.AddScoped<IUnitOfWork, UnitOfWorkService>();
        services.AddScoped(typeof(IGenericRepository<>), typeof(GenericRepository<>));
        services.AddScoped<IInvoiceRepository, InvoiceRepository>();
        services.AddScoped<IInvoiceSubmissionRepository, InvoiceSubmissionRepository>();
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<ICustomerRepository, CustomerRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IApiClientRepository, ApiClientRepository>();
        services.AddScoped<ITenantRepository, TenantRepository>();

        services.Configure<DatabaseSeedOptions>(configuration.GetSection(DatabaseSeedOptions.SectionName));
        services.AddScoped<IDatabaseSeeder, DatabaseSeeder>();

        return services;
    }
}
