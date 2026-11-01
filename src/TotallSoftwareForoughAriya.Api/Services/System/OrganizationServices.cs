using Inventory.Api.Data;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services;

/// <summary>
/// سرویس سازمان‌ها — تأمین جزء «واحد» در شماره اندیکاتور نامه‌ها.
/// (پورت OrganizationService کارفرما با متد GetOrganizationNameUniqAsync)
/// </summary>
public interface IOrganizationServices
{
    /// <summary>
    /// نام اختصاصی سازمان (NameUniq) برای یک سمت مشخص.
    /// اگر سمت وجود نداشته باشد (یا چارت سازمانی هنوز فعال نشده)،
    /// از سازمانِ پیش‌فرض (IsDefault) و سپس اولین سازمانِ فعال خوانده می‌شود.
    /// </summary>
    Task<string> GetOrganizationNameUniqAsync(int? sematId = null);

    /// <summary>
    /// نام اختصاصی سازمان (NameUniq) بر اساس شناسه کاربر — برای جزء «واحد» در شماره نامه.
    /// اگر کاربر سازمانی اختصاص یافته نداشت، به روش پیش‌فرض (sematId null) بازمی‌گردد.
    /// </summary>
    Task<string> GetOrganizationNameUniqByUserIdAsync(int? userId);

    /// <summary>
    /// واحدِ اختصاصی خودِ کاربر (فقط همان) — از جدول کاربرانِ سامانه (`Users`).
    /// مقدار null یعنی برای این کاربر واحدی ثبت نشده است (هیچ fallback‌ای انجام نمی‌شود).
    /// این متد مبنای جزء «واحد» در شمارهٔ نامه است تا شمارهٔ نامه با واحدِ سازندهٔ نامه ساخته شود.
    /// </summary>
    Task<string?> GetUserOrganizationNameUniqAsync(int? userId);

    /// <summary>فهرست سازمان‌های فعال (برای انتخاب در تنظیمات)</summary>
    Task<List<OrganizationDto>> GetOrganizationsAsync();

    /// <summary>شناسه سازمان پیش‌فرض — یا null اگر تعریف نشده</summary>
    Task<int?> GetDefaultOrganizationIdAsync();
}

public class OrganizationServices : IOrganizationServices
{
    private readonly AppDbContext _db;

    public OrganizationServices(AppDbContext db) => _db = db;

    public async Task<string> GetOrganizationNameUniqAsync(int? sematId = null)
    {
        // ۱) اگر سمت (Semat) مشخص است → سازمانِ همان سمت
        if (sematId is > 0)
        {
            var name = await _db.Semats.AsNoTracking()
                .Where(s => s.SematId == sematId && !s.IsDelete && s.IsActive
                            && s.Organization != null && !s.Organization.IsDelete && s.Organization.IsActive)
                .Select(s => s.Organization!.NameUniq)
                .FirstOrDefaultAsync();
            if (!string.IsNullOrWhiteSpace(name)) return name;
        }

        // ۲) سازمانِ پیش‌فرض — ابتدا در محدودهٔ شرکت فعال، سپس عمومی
        var active = _db.ActiveCompanyId;
        var def = await _db.Organizations.AsNoTracking()
            .Where(o => !o.IsDelete && o.IsActive && o.IsDefault
                        && (active == null || o.CompanyId == null || o.CompanyId == active))
            .Select(o => o.NameUniq)
            .FirstOrDefaultAsync();
        if (string.IsNullOrWhiteSpace(def))
            def = await _db.Organizations.AsNoTracking()
                .Where(o => !o.IsDelete && o.IsActive && o.IsDefault)
                .Select(o => o.NameUniq)
                .FirstOrDefaultAsync();
        if (!string.IsNullOrWhiteSpace(def)) return def;

        // ۳) اولین سازمان فعال به‌عنوان آخرین fallback
        var first = await _db.Organizations.AsNoTracking()
            .Where(o => !o.IsDelete && o.IsActive
                        && (active == null || o.CompanyId == null || o.CompanyId == active))
            .OrderBy(o => o.OrganizationId)
            .Select(o => o.NameUniq)
            .FirstOrDefaultAsync();
        if (!string.IsNullOrWhiteSpace(first)) return first;

        return await _db.Organizations.AsNoTracking()
                   .Where(o => !o.IsDelete && o.IsActive)
                   .OrderBy(o => o.OrganizationId)
                   .Select(o => o.NameUniq)
                   .FirstOrDefaultAsync() ?? "";
    }

    public async Task<string> GetOrganizationNameUniqByUserIdAsync(int? userId)
    {
        var own = await GetUserOrganizationNameUniqAsync(userId);
        if (!string.IsNullOrWhiteSpace(own)) return own!;

        // اگر کاربر واحدِ اختصاصی نداشته باشد، سازمانِ پیش‌فرض (همان شرکت فعال) مبنا می‌شود.
        return await GetOrganizationNameUniqAsync(null);
    }

    /// <summary>
    /// واحدِ اختصاصی خودِ کاربر — منبع اصلی: ستون `OrganizationId` در جدول `Users`
    /// (همان کاربری که با آن وارد سامانه می‌شود و نامه را می‌سازد).
    /// پیش از این کد از جدول قدیمی `SystemUsers` می‌خواند؛ چون شناسه‌های دو جدول
    /// مستقل‌اند، واحدِ شخص دیگری (یا پیش‌فرض) در شمارهٔ نامه می‌نشست.
    /// </summary>
    public async Task<string?> GetUserOrganizationNameUniqAsync(int? userId)
    {
        if (userId is not > 0) return null;

        var orgId = await _db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.OrganizationId)
            .FirstOrDefaultAsync();

        // سازگاری با داده‌های قدیمی: اگر کاربر در جدول `Users` واحد نداشت،
        // همان شناسه در جدول `SystemUsers` بررسی می‌شود.
        if (orgId is not > 0)
        {
            orgId = await _db.SystemUsers.AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => u.OrganizationId)
                .FirstOrDefaultAsync();
        }

        if (orgId is not > 0) return null;

        var name = await OrganizationCodeAsync(orgId.Value);
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    /// <summary>کد اختصاری یک واحد فعال (NameUniq) — با حفظ فیلتر شرکت فعال.</summary>
    private async Task<string?> OrganizationCodeAsync(int organizationId)
    {
        var active = _db.ActiveCompanyId;
        var code = await _db.Organizations.AsNoTracking()
            .Where(o => o.OrganizationId == organizationId
                        && !o.IsDelete && o.IsActive
                        && (active == null || o.CompanyId == null || o.CompanyId == active))
            .Select(o => o.NameUniq)
            .FirstOrDefaultAsync();
        if (!string.IsNullOrWhiteSpace(code)) return code;

        // اگر واحد به شرکت دیگری تعلق داشت، همان واحد کاربر معتبر است و استفاده می‌شود.
        return await _db.Organizations.AsNoTracking()
            .Where(o => o.OrganizationId == organizationId && !o.IsDelete && o.IsActive)
            .Select(o => o.NameUniq)
            .FirstOrDefaultAsync();
    }

    public async Task<List<OrganizationDto>> GetOrganizationsAsync() =>
        await _db.Organizations.AsNoTracking()
            .Where(o => !o.IsDelete && o.IsActive)
            .OrderBy(o => o.NameUnit)
            .Select(o => new OrganizationDto
            {
                OrganizationId = o.OrganizationId,
                CompanyId = o.CompanyId,
                NameUnit = o.NameUnit,
                NameUniq = o.NameUniq,
                IsDefault = o.IsDefault
            })
            .ToListAsync();

    public async Task<int?> GetDefaultOrganizationIdAsync()
    {
        var id = await _db.Organizations.AsNoTracking()
            .Where(o => !o.IsDelete && o.IsActive && o.IsDefault)
            .Select(o => (int?)o.OrganizationId)
            .FirstOrDefaultAsync();
        if (id is > 0) return id;

        // اگر هنوز پیش‌فرض تعیین نشده، اولین سازمان فعال مبناست
        return await _db.Organizations.AsNoTracking()
            .Where(o => !o.IsDelete && o.IsActive)
            .Select(o => (int?)o.OrganizationId)
            .FirstOrDefaultAsync();
    }
}
