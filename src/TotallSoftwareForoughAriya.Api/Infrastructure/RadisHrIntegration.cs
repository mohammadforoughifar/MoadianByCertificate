using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.EntityFrameworkCore;
using RadisHr.Api.Data;
using RadisHr.Api.Services;

namespace Inventory.Api.Infrastructure;

/// <summary>
/// آداپتور میزبانی RADIS-HR در برنامه جامع. کد کنترلرها و منطق تجاری ماژول
/// بدون تغییر کامپایل می‌شود و این Convention فقط پیشوند مسیر و سیاست دسترسی را
/// در لایه میزبان اعمال می‌کند.
/// </summary>
public sealed class RadisHrControllerConvention : IApplicationModelConvention
{
    private static readonly AttributeRouteModel Prefix = new(new RouteAttribute("radis-hr"));

    public void Apply(ApplicationModel application)
    {
        foreach (var controller in application.Controllers.Where(c =>
                     c.ControllerType.Namespace?.StartsWith("RadisHr.Api.Controllers", StringComparison.Ordinal) == true))
        {
            foreach (var selector in controller.Selectors)
                selector.AttributeRouteModel = AttributeRouteModel.CombineAttributeRouteModel(Prefix, selector.AttributeRouteModel);

            controller.Filters.Add(new AuthorizeFilter("RadisHrAccess"));
        }
    }
}

public static class RadisHrIntegration
{
    public static IServiceCollection AddRadisHrModule(
        this IServiceCollection services,
        IConfiguration configuration,
        string provider,
        string connectionString)
    {
        services.AddDbContext<RadisHr.Api.Data.AppDbContext>(options =>
        {
            if (provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
                options.UseSqlite(connectionString, sqlite =>
                    sqlite.MigrationsHistoryTable("__EFMigrationsHistory_RadisHr"));
            else
                options.UseSqlServer(connectionString, sql =>
                {
                    sql.EnableRetryOnFailure(3);
                    sql.MigrationsHistoryTable("__EFMigrationsHistory_RadisHr");
                });
        });

        services.AddScoped<TokenService>();
        services.AddScoped<PayrollService>();
        services.AddScoped<AttendanceService>();
        return services;
    }

    public static async Task InitializeRadisHrAsync(this WebApplication app, bool isDesignTime, bool failOnError = false)
    {
        if (isDesignTime) return;

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RadisHr.Api.Data.AppDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("RadisHr");
        try
        {
            await db.Database.MigrateAsync();
            await DbSeeder.SeedAsync(db, app.Configuration);
            logger.LogInformation("ماژول منابع انسانی بن‌سازه روی دیتابیس اصلی آماده شد.");
        }
        catch (Exception ex)
        {
            // A maintenance command must fail rather than report success after a migration error.
            if (failOnError) throw;
            // جدول Users/PK_Users مجموعه نباید مانع روشن‌شدن برنامه شود.
            logger.LogError(ex, "راه‌اندازی دیتابیس ماژول منابع انسانی بن‌سازه ناموفق بود؛ سامانه جامع ادامه می‌دهد.");
        }
    }
}
