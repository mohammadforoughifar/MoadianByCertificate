using FluentValidation;
using Microsoft.EntityFrameworkCore;
using MoudianMiddleware.Contracts.ViewModels.Moadian;
using MoudianMiddleware.CoreLayer.Interfaces.Services;
using MoudianMiddleware.DataLayer.Abstractions;
using MoudianMiddleware.DataLayer.Context;
using MoudianMiddleware.DataLayer.Entities;

namespace MoudianMiddleware.CoreLayer.Services.Moadian;

/// <summary>نگهداری تنظیمات اتصال مودیان در محدوده‌ی tenant جاری.</summary>
public sealed class MoadianSettingService : IMoadianSettingService
{
    private readonly MoudianDbContext _context;
    private readonly IValidator<MoadianSettingWriteRequest> _validator;
    private readonly ITenantProvider _tenantProvider;
    private readonly ITenantScope _tenantScope;

    /// <summary>ساخت سرویس تنظیمات مودیان.</summary>
    public MoadianSettingService(
        MoudianDbContext context,
        IValidator<MoadianSettingWriteRequest> validator,
        ITenantProvider tenantProvider,
        ITenantScope tenantScope)
    {
        _context = context;
        _validator = validator;
        _tenantProvider = tenantProvider;
        _tenantScope = tenantScope;
    }

    /// <inheritdoc />
    public async Task<MoadianSettingViewModel?> GetAsync(CancellationToken cancellationToken = default)
    {
        _ = CurrentTenantId();
        var setting = await _context.MoadianSettings.SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return setting is null ? null : Map(setting);
    }

    /// <inheritdoc />
    public async Task<MoadianSettingViewModel> SaveAsync(
        MoadianSettingWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _validator.ValidateAndThrowAsync(request, cancellationToken).ConfigureAwait(false);
        var tenantId = CurrentTenantId();
        var setting = await _context.MoadianSettings.SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (setting is null)
        {
            setting = new MoadianSetting { TenantId = tenantId };
            await _context.MoadianSettings.AddAsync(setting, cancellationToken).ConfigureAwait(false);
        }

        setting.MemoryId = request.MemoryId.Trim().ToUpperInvariant();
        setting.EconomicCode = request.EconomicCode.Trim();
        setting.NationalId = Normalize(request.NationalId);
        setting.BranchCode = Normalize(request.BranchCode);
        setting.SignatureMode = request.SignatureMode;
        setting.IsSignatureExperimentalApproved = request.IsSignatureExperimentalApproved;
        setting.ActiveEnvironment = request.ActiveEnvironment;
        setting.SandboxBaseUrl = Normalize(request.SandboxBaseUrl)?.TrimEnd('/');
        setting.ProductionBaseUrl = Normalize(request.ProductionBaseUrl)?.TrimEnd('/');
        setting.IsActive = request.IsActive;

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Map(setting);
    }

    private int CurrentTenantId() => _tenantScope.TenantId ?? _tenantProvider.TenantId
        ?? throw new InvalidOperationException("مودی جاری مشخص نیست؛ تغییر تنظیمات بدون tenant مجاز نیست.");

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static MoadianSettingViewModel Map(MoadianSetting setting) => new()
    {
        TenantId = setting.TenantId,
        MemoryId = setting.MemoryId,
        EconomicCode = setting.EconomicCode,
        NationalId = setting.NationalId,
        BranchCode = setting.BranchCode,
        SignatureMode = setting.SignatureMode,
        IsSignatureExperimentalApproved = setting.IsSignatureExperimentalApproved,
        ActiveEnvironment = setting.ActiveEnvironment,
        SandboxBaseUrl = setting.SandboxBaseUrl,
        ProductionBaseUrl = setting.ProductionBaseUrl,
        LastConnectionTestAt = setting.LastConnectionTestAt,
        LastConnectionTestSucceeded = setting.LastConnectionTestSucceeded,
        IsActive = setting.IsActive
    };
}
