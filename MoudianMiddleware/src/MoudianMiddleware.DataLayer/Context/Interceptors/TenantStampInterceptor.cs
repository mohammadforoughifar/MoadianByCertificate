using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using MoudianMiddleware.DataLayer.Abstractions;
using MoudianMiddleware.DataLayer.Entities.Common;

namespace MoudianMiddleware.DataLayer.Context.Interceptors;

/// <summary>
/// ست کردن خودکار <c>TenantId</c> هنگام درج و جلوگیری از تغییر آن در ویرایش.
/// </summary>
/// <remarks>
/// این Interceptor خط دفاعی دوم است؛ خط اول، فیلتر سراسری کوئری در <c>MoudianDbContext</c> است.
/// </remarks>
public sealed class TenantStampInterceptor : SaveChangesInterceptor
{
    private readonly ITenantProvider _tenantProvider;
    private readonly ITenantScope? _tenantScope;

    /// <summary>
    /// سازنده با تزریق تأمین‌کننده‌ی درخواست و scope صریح.
    /// </summary>
    /// <param name="tenantProvider">تأمین‌کننده‌ی مستأجر درخواست HTTP.</param>
    /// <param name="tenantScope">Scope صریح برای عملیات پس‌زمینه/Seed.</param>
    public TenantStampInterceptor(ITenantProvider tenantProvider, ITenantScope? tenantScope = null)
    {
        _tenantProvider = tenantProvider;
        _tenantScope = tenantScope;
    }

    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        Apply(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        Apply(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Apply(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var tenantId = _tenantScope?.TenantId ?? _tenantProvider.TenantId;

        foreach (var entry in context.ChangeTracker.Entries<ITenantEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.TenantId = ResolveTenantId(entry.Entity.TenantId, tenantId);
                    break;

                case EntityState.Modified:
                    // TenantId پس از ایجاد، تغییرناپذیر است
                    entry.Property(x => x.TenantId).IsModified = false;
                    break;

                default:
                    break;
            }
        }
    }

    private static int ResolveTenantId(int entityTenantId, int? currentTenantId)
    {
        if (entityTenantId > 0)
        {
            if (currentTenantId is not null && entityTenantId != currentTenantId.Value)
            {
                throw new InvalidOperationException(
                    "ثبت رکورد برای مستأجری غیر از مستأجر جاری مجاز نیست.");
            }

            return entityTenantId;
        }

        return currentTenantId
            ?? throw new InvalidOperationException(
                "شناسه‌ی مستأجر جاری مشخص نیست؛ ثبت داده‌ی سازمانی بدون مستأجر مجاز نیست.");
    }
}
