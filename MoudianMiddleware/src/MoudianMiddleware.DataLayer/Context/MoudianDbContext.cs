using Microsoft.EntityFrameworkCore;
using MoudianMiddleware.DataLayer.Abstractions;
using MoudianMiddleware.DataLayer.Entities;
using MoudianMiddleware.DataLayer.Entities.Common;

namespace MoudianMiddleware.DataLayer.Context;

/// <summary>
/// Context اصلی دیتابیس؛ تنظیمات نگاشت، فیلترهای سراسری و DbSetها.
/// </summary>
/// <remarks>
/// <para>
/// <b>جداسازی مستأجر:</b> برای همه‌ی موجودیت‌های <see cref="ITenantEntity"/> فیلتر سراسری اعمال می‌شود.
/// اگر <see cref="CurrentTenantId"/> مقدار نداشته باشد، هیچ رکوردی بازنمی‌گردد (سیاست «رد پیش‌فرض»)
/// تا خطای فراموشی تزریق مستأجر به افشای داده منجر نشود.
/// </para>
/// <para>
/// <b>حذف نرم:</b> رکوردهای <see cref="ISoftDeletable"/> به‌طور پیش‌فرض در کوئری‌ها دیده نمی‌شوند.
/// </para>
/// <para>
/// فیلترها به اعضای همین Context ارجاع می‌دهند تا EF مقدار آن را برای هر نمونه (per-request) پارامتر کند.
/// </para>
/// </remarks>
public class MoudianDbContext : DbContext
{
    private readonly ITenantProvider? _tenantProvider;
    private readonly ITenantScope? _tenantScope;

    /// <summary>
    /// سازنده‌ی Context.
    /// </summary>
    /// <param name="options">تنظیمات EF (Provider، Interceptorها و ...).</param>
    /// <param name="tenantProvider">مودی جاری درخواست HTTP؛ در درخواست ناشناس مقدارش null است.</param>
    /// <param name="tenantScope">مودی صریح برای worker/seed؛ مستقل از query-filter و بدون خاموش‌کردن آن.</param>
    public MoudianDbContext(
        DbContextOptions<MoudianDbContext> options,
        ITenantProvider? tenantProvider,
        ITenantScope? tenantScope = null)
        : base(options)
    {
        _tenantProvider = tenantProvider;
        _tenantScope = tenantScope;
    }

    /// <summary>شناسه‌ی مودی صریح، یا مودی درخواست HTTP؛ null باعث فیلتر خالی می‌شود.</summary>
    public int? CurrentTenantId => _tenantScope?.TenantId ?? _tenantProvider?.TenantId;

    /// <summary>مودیان (مستأجرها).</summary>
    public DbSet<Tenant> Tenants => Set<Tenant>();

    /// <summary>کاربران.</summary>
    public DbSet<User> Users => Set<User>();

    /// <summary>نقش‌ها.</summary>
    public DbSet<Role> Roles => Set<Role>();

    /// <summary>Permissionهای نقش‌ها.</summary>
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();

    /// <summary>عضویت کاربران در مودیان.</summary>
    public DbSet<UserTenant> UserTenants => Set<UserTenant>();

    /// <summary>توکن‌های تازه‌سازی.</summary>
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    /// <summary>تنظیمات مودیان.</summary>
    public DbSet<MoadianSetting> MoadianSettings => Set<MoadianSetting>();

    /// <summary>کلید/گواهی امضای مودیان.</summary>
    public DbSet<MoadianCredential> MoadianCredentials => Set<MoadianCredential>();

    /// <summary>واحدهای اندازه‌گیری.</summary>
    public DbSet<MeasuringUnit> MeasuringUnits => Set<MeasuringUnit>();

    /// <summary>کالاها و خدمات.</summary>
    public DbSet<Product> Products => Set<Product>();

    /// <summary>مشتریان.</summary>
    public DbSet<Customer> Customers => Set<Customer>();

    /// <summary>صورتحساب‌ها.</summary>
    public DbSet<Invoice> Invoices => Set<Invoice>();

    /// <summary>اقلام صورتحساب.</summary>
    public DbSet<InvoiceItem> InvoiceItems => Set<InvoiceItem>();

    /// <summary>پرداخت‌های صورتحساب.</summary>
    public DbSet<InvoicePayment> InvoicePayments => Set<InvoicePayment>();

    /// <summary>تاریخچه‌ی تلاش‌های ارسال/استعلام.</summary>
    public DbSet<InvoiceSubmission> InvoiceSubmissions => Set<InvoiceSubmission>();

    /// <summary>لاگ حسابرسی.</summary>
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    /// <summary>کلیدهای API سیستم‌های خارجی.</summary>
    public DbSet<ApiClient> ApiClients => Set<ApiClient>();

    /// <summary>پیام‌های Outbox.</summary>
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    /// <summary>کدهای خطای سامانه‌ی مودیان.</summary>
    public DbSet<MoadianErrorCode> MoadianErrorCodes => Set<MoadianErrorCode>();

    /// <summary>الگوهای صورتحساب.</summary>
    public DbSet<InvoicePattern> InvoicePatterns => Set<InvoicePattern>();

    /// <summary>موضوع‌های صورتحساب.</summary>
    public DbSet<InvoiceSubject> InvoiceSubjects => Set<InvoiceSubject>();

    /// <summary>نگاشت روش‌های پرداخت.</summary>
    public DbSet<PaymentMethodCode> PaymentMethodCodes => Set<PaymentMethodCode>();

    /// <summary>
    /// پیکربندی مدل: بارگذاری همه‌ی Configurationها و اعمال فیلترهای سراسری.
    /// </summary>
    /// <param name="modelBuilder">سازنده‌ی مدل EF.</param>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        base.OnModelCreating(modelBuilder);

        // همه‌ی IEntityTypeConfigurationهای این اسمبلی (Context/Configurations) اعمال می‌شوند.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MoudianDbContext).Assembly);

        ApplySoftDeleteFilter<Tenant>(modelBuilder);
        ApplySoftDeleteFilter<User>(modelBuilder);
        // Match User's soft-delete filter across the required RefreshToken.User relationship.
        // This keeps soft-deleted users' tokens out of ordinary queries and avoids EF's
        // required-navigation/global-filter warning. Authentication flows that validate a
        // presented token explicitly use IgnoreQueryFilters and re-check User.IsDeleted.
        modelBuilder.Entity<RefreshToken>()
            .HasQueryFilter(token => token.User != null && !token.User.IsDeleted);
        ApplySoftDeleteFilter<MeasuringUnit>(modelBuilder);
        ApplySoftDeleteFilter<MoadianErrorCode>(modelBuilder);

        ApplyTenantSoftDeleteFilter<MoadianSetting>(modelBuilder);
        ApplyTenantSoftDeleteFilter<MoadianCredential>(modelBuilder);
        ApplyTenantSoftDeleteFilter<UserTenant>(modelBuilder);
        ApplyTenantSoftDeleteFilter<Product>(modelBuilder);
        ApplyTenantSoftDeleteFilter<Customer>(modelBuilder);
        ApplyTenantSoftDeleteFilter<Invoice>(modelBuilder);
        ApplyTenantSoftDeleteFilter<ApiClient>(modelBuilder);

        ApplyTenantFilter<InvoiceItem>(modelBuilder);
        ApplyTenantFilter<InvoicePayment>(modelBuilder);
        ApplyTenantFilter<InvoiceSubmission>(modelBuilder);
        ApplyTenantFilter<OutboxMessage>(modelBuilder);
    }

    /// <summary>فیلتر «مستأجر جاری + حذف نشده» برای موجودیت‌های سازمانی دارای Audit.</summary>
    /// <typeparam name="TEntity">نوع موجودیت.</typeparam>
    /// <param name="modelBuilder">سازنده‌ی مدل.</param>
    private void ApplyTenantSoftDeleteFilter<TEntity>(ModelBuilder modelBuilder)
        where TEntity : AuditableEntity, ITenantEntity
        => modelBuilder.Entity<TEntity>()
            .HasQueryFilter(e => CurrentTenantId != null && e.TenantId == CurrentTenantId && !e.IsDeleted);

    /// <summary>فیلتر «مستأجر جاری» برای موجودیت‌های فرزند (بدون حذف نرم).</summary>
    /// <typeparam name="TEntity">نوع موجودیت.</typeparam>
    /// <param name="modelBuilder">سازنده‌ی مدل.</param>
    private void ApplyTenantFilter<TEntity>(ModelBuilder modelBuilder)
        where TEntity : class, ITenantEntity
        => modelBuilder.Entity<TEntity>()
            .HasQueryFilter(e => CurrentTenantId != null && e.TenantId == CurrentTenantId);

    /// <summary>فیلتر «حذف نشده» برای داده‌های مرجع سراسری.</summary>
    /// <typeparam name="TEntity">نوع موجودیت.</typeparam>
    /// <param name="modelBuilder">سازنده‌ی مدل.</param>
    private static void ApplySoftDeleteFilter<TEntity>(ModelBuilder modelBuilder)
        where TEntity : AuditableEntity
        => modelBuilder.Entity<TEntity>().HasQueryFilter(e => !e.IsDeleted);
}
