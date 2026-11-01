using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MoudianMiddleware.CoreLayer.Interfaces.Infrastructure;
using MoudianMiddleware.CoreLayer.Interfaces.Services;
using MoudianMiddleware.CoreLayer.Infrastructure.Moadian;
using MoudianMiddleware.CoreLayer.Infrastructure.Moadian.Signing;
using MoudianMiddleware.CoreLayer.Infrastructure.Security;
using MoudianMiddleware.CoreLayer.Services.Customers;
using MoudianMiddleware.CoreLayer.Services.Invoices;
using MoudianMiddleware.CoreLayer.Services.Moadian;
using MoudianMiddleware.CoreLayer.Services.Products;

namespace MoudianMiddleware.CoreLayer;

/// <summary>ثبت سرویس‌ها و validatorهای CoreLayer.</summary>
public static class DependencyInjection
{
    /// <summary>ثبت سرویس‌های Core و اتصال تنظیمات رمزنگاری به بخش SecretProtection.</summary>
    /// <param name="services">مجموعه‌ی سرویس‌ها.</param>
    /// <param name="configuration">پیکربندی میزبان؛ کلیدهای رمزنگاری باید از secret store تأمین شوند.</param>
    /// <returns>همان مجموعه برای زنجیره‌سازی.</returns>
    public static IServiceCollection AddCoreLayer(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        services.Configure<SecretProtectionOptions>(configuration.GetSection(SecretProtectionOptions.SectionName));
        return services.AddCoreLayer();
    }

    /// <summary>ثبت سرویس‌ها و validatorها؛ میزبان باید SecretProtectionOptions را جداگانه پیکربندی کند.</summary>
    /// <param name="services">مجموعه‌ی سرویس‌ها.</param>
    /// <returns>همان مجموعه برای زنجیره‌سازی.</returns>
    public static IServiceCollection AddCoreLayer(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);
        services.AddScoped<IProductService, ProductService>();
        services.AddScoped<ICustomerService, CustomerService>();
        services.AddScoped<IInvoiceService, InvoiceService>();
        services.AddScoped<IInvoiceCalculationService, InvoiceCalculationService>();
        services.AddScoped<IInvoicePreparationService, InvoicePreparationService>();
        services.AddScoped<IMoadianSettingService, MoadianSettingService>();
        services.AddScoped<IMoadianCredentialService, MoadianCredentialService>();
        services.AddScoped<IMoadianSubmissionService, MoadianSubmissionService>();
        services.AddScoped<ISecretProtector, AesGcmSecretProtector>();
        services.AddScoped<IMoadianSignerFactory, MoadianSignerFactory>();
        services.AddScoped<IMoadianGateway, TaxCollectDataMoadianGateway>();
        services.AddSingleton<IMoadianPayloadScrubber, MoadianPayloadScrubber>();
        return services;
    }
}
