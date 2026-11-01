using System.Security.Claims;
using Inventory.Api.Data;
using Inventory.Api.Services.Core;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services.ReportStudio;

/// <summary>نتیجهٔ بررسی دسترسی به یک گزارش.</summary>
public sealed class RsAccessInfo
{
    public bool CanView { get; init; }
    public bool CanExport { get; init; }
    public bool CanEdit { get; init; }
    public bool CanShare { get; init; }
    public bool IsOwner { get; init; }
    public RsAccess Level { get; init; }
    /// <summary>اگر دسترسی نیست، دلیلش برای نمایش به کاربر.</summary>
    public string? Reason { get; init; }

    public static RsAccessInfo Deny(string reason) => new() { Reason = reason };
}

public interface IRsAccessService
{
    Task<RsAccessInfo> EvaluateAsync(ClaimsPrincipal user, RsReport report);
    /// <summary>گزارش‌هایی که کاربر حق دیدنشان را دارد.</summary>
    Task<List<RsReport>> VisibleReportsAsync(ClaimsPrincipal user, Paging.Request? pagination = null);
    /// <summary>آیا کاربر به همهٔ ماژول‌های این گزارش دسترسی داده دارد؟</summary>
    Task<(bool Ok, string? Missing)> HasDataAccessAsync(ClaimsPrincipal user, IEnumerable<string> modules);
}

// =====================================================================
//  کنترل دسترسی گزارش‌ها
//
//  دو لایهٔ مستقل — هر دو باید برقرار باشند:
//
//   ۱) «دسترسی به گزارش»  : مالک / اشتراک کاربری / اشتراک نقشی / عمومی
//   ۲) «دسترسی به داده»   : مجوز RBAC همهٔ ماژول‌های به‌کاررفته
//
//  لایهٔ دوم حیاتی است: اگر مدیر گزارشی از حقوق پرسنل را با کارمند
//  انبار share کند، آن کارمند چون مجوز ماژول HR را ندارد داده را
//  نمی‌بیند. اشتراک‌گذاری هرگز RBAC را دور نمی‌زند.
// =====================================================================
public sealed class RsAccessService : IRsAccessService
{
    private readonly AppDbContext _db;
    private readonly IEffectivePermissions _perms;

    public RsAccessService(AppDbContext db, IEffectivePermissions perms)
    {
        _db = db;
        _perms = perms;
    }

    private static int UserId(ClaimsPrincipal u) =>
        int.TryParse(u.FindFirstValue(ClaimTypes.NameIdentifier), out var v) ? v : 0;

    private static bool IsAdmin(ClaimsPrincipal u) =>
        u.IsInRole("Admin") || u.FindFirstValue(ClaimTypes.Role) == "Admin";

    public async Task<RsAccessInfo> EvaluateAsync(ClaimsPrincipal user, RsReport report)
    {
        var uid = UserId(user);
        var admin = IsAdmin(user);

        // --- لایهٔ ۱: دسترسی به خود گزارش ---
        if (report.OwnerUserId == uid)
            return new RsAccessInfo
            {
                CanView = true, CanExport = true, CanEdit = true, CanShare = true,
                IsOwner = true, Level = RsAccess.Edit
            };

        if (admin)
            return new RsAccessInfo
            {
                CanView = true, CanExport = true, CanEdit = true, CanShare = true,
                Level = RsAccess.Edit
            };

        RsAccess? level = null;

        // اشتراک مستقیم با کاربر — بالاترین سطح برنده است
        var direct = report.UserShares.Where(s => s.UserId == uid).ToList();
        if (direct.Count > 0) level = (RsAccess)direct.Max(s => s.Access);

        // اشتراک از طریق نقش
        if (report.RoleShares.Count > 0)
        {
            var myRoles = await _perms.MyRoleIdsAsync(uid);
            var viaRole = report.RoleShares.Where(s => myRoles.Contains(s.RoleId)).ToList();
            if (viaRole.Count > 0)
            {
                var roleLevel = (RsAccess)viaRole.Max(s => s.Access);
                level = level is null ? roleLevel
                      : (RsAccess)Math.Max((int)level.Value, (int)roleLevel);
            }
        }

        // عمومی
        if (level is null && report.Visibility == (int)RsVisibility.Everyone)
            level = RsAccess.View;

        if (level is null)
            return RsAccessInfo.Deny("این گزارش با شما به اشتراک گذاشته نشده است.");

        // --- لایهٔ ۲: دسترسی به دادهٔ ماژول‌ها ---
        var modules = SplitModules(report.ModulesCsv);
        var (ok, missing) = await HasDataAccessAsync(user, modules);
        if (!ok)
            return RsAccessInfo.Deny($"برای دیدن دادهٔ این گزارش به مجوز ماژول «{missing}» نیاز دارید.");

        return new RsAccessInfo
        {
            CanView = true,
            CanExport = level >= RsAccess.Export,
            CanEdit = level == RsAccess.Edit,
            CanShare = false,          // فقط مالک و ادمین می‌توانند اشتراک را تغییر دهند
            Level = level.Value
        };
    }

    public async Task<(bool Ok, string? Missing)> HasDataAccessAsync(
        ClaimsPrincipal user, IEnumerable<string> modules)
    {
        if (IsAdmin(user)) return (true, null);
        foreach (var m in modules)
        {
            if (string.IsNullOrWhiteSpace(m)) continue;
            if (!await _perms.CanSeeModuleAsync(user, m))
                return (false, m);
        }
        return (true, null);
    }

    public async Task<List<RsReport>> VisibleReportsAsync(ClaimsPrincipal user, Paging.Request? pagination = null)
    {
        var uid = UserId(user);
        var admin = IsAdmin(user);
        var myRoles = await _perms.MyRoleIdsAsync(uid);

        var q = _db.RsReports
            .Include(r => r.UserShares)
            .Include(r => r.RoleShares)
            .Where(r => !r.IsDelete);

        if (!admin)
            q = q.Where(r =>
                r.OwnerUserId == uid
                || r.Visibility == (int)RsVisibility.Everyone
                || r.UserShares.Any(s => s.UserId == uid)
                || r.RoleShares.Any(s => myRoles.Contains(s.RoleId)));

        if (!admin)
        {
            // Permissions are application policy, not translatable SQL. Resolve distinct
            // policy keys, then apply the allowed-key predicate BEFORE count/offset.
            var policies = await q.Select(r => r.ModulesCsv).Distinct().ToListAsync();
            var allowed = new List<string?>();
            foreach (var policy in policies)
                if ((await HasDataAccessAsync(user, SplitModules(policy))).Ok) allowed.Add(policy);
            q = q.Where(r => allowed.Contains(r.ModulesCsv));
        }
        return await q.OrderByDescending(r => r.UpdatedAt).ThenBy(r => r.Id).ToPageListAsync(pagination);
    }

    public static string[] SplitModules(string? csv) =>
        string.IsNullOrWhiteSpace(csv)
            ? Array.Empty<string>()
            : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
