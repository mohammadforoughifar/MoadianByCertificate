using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MoudianMiddleware.DataLayer.Abstractions.Repositories;
using MoudianMiddleware.DataLayer.Context;
using MoudianMiddleware.DataLayer.Context.Interceptors;
using MoudianMiddleware.DataLayer.Repositories;
using MoudianMiddleware.DataLayer.Seed;
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
    /// افزودن DbContext، Repositoryها، واحد کار و Seeder به سرویس‌ها.
    /// </summary>
    /// <param name="services">مجموعه‌ی سرویس‌ها.</param>
    /// <param name="configuration">تنظیمات برنامه (برای رشته‌ی اتصال).</param>
    /// <returns>همان مجموعه برای زنجیره‌سازی.</returns>
    /// <exception cref="InvalidOperationException">اگر رشته‌ی اتصال تنظیم نشده باشد.</exception>
    public static IServiceCollection AddDataLayer(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"رشته‌ی اتصال «{ConnectionStringName}» تنظیم نشده است. آن را در appsettings یا " +
                "متغیر محیطی ConnectionStrings__Default قرار دهید.");
        }

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

        services.AddScoped<IUnitOfWork, UnitOfWork>();
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
