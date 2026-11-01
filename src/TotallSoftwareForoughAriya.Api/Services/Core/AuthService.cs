using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Inventory.Api.Services.DocArchive;
using Inventory.Shared;
using Inventory.Shared.Dtos;
using Inventory.Shared.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Db = Inventory.Api.Data;

namespace Inventory.Api.Services;

/// <summary>پیاده‌سازی احراز هویت JWT و مدیریت کاربران.</summary>
public class AuthService : IAuthService
{
    /// <summary>کلید امضای توکن (در محیط عملیاتی از تنظیمات/متغیر محیطی بخوانید).</summary>
    public const string JwtKey = "InventoryApp-Jwt-Secret-Key-2026-!@#$%-Very-Long-Key-For-HS256";
    public const string JwtIssuer = "InventoryApp";

    private readonly Db.AppDbContext _db;
    private readonly IInventoryService _inventory;

    public AuthService(Db.AppDbContext db, IInventoryService inventory)
    {
        _db = db;
        _inventory = inventory;
    }

    // ---------------- رمزنگاری ----------------

    public static string HashPassword(string password)
    {
        // PBKDF2 با نمک تصادفی
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 100_000, HashAlgorithmName.SHA256, 32);
        return $"{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    public static bool VerifyPassword(string password, string stored)
    {
        var parts = stored.Split('.', 2);
        if (parts.Length != 2) return false;
        var salt = Convert.FromBase64String(parts[0]);
        var expected = Convert.FromBase64String(parts[1]);
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, 100_000, HashAlgorithmName.SHA256, 32);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    // ---------------- ورود ----------------

    public async Task<LoginResponse?> LoginAsync(LoginRequest request)
    {
        var username = (request.Username ?? "").Trim();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == username && u.IsActive);
        if (user is null || !VerifyPassword(request.Password ?? "", user.PasswordHash))
            return null;

        // ================== نقش‌های RBAC کاربر (فقط نقش‌های فعال) ==================
        var roleIds = await _db.UserRoles.Where(ur => ur.UserId == user.Id)
            .Select(ur => ur.RoleId).ToListAsync();

        var roleNames = await _db.Roles
            .Where(r => roleIds.Contains(r.Id) && r.IsActive)
            .Select(r => r.Name).ToListAsync();

        // ================== خودترمیمی سطح دسترسی قدیمی (ستون Users.Role) ==================
        // کاربرانی که با نام نقش (مثل «مدیر فروش») اشتباهاً «مدیر کل» شده بودند، همین‌جا
        // به سطح درست برمی‌گردند تا با «دسترسی کامل» وارد نشوند. توضیح کامل در XML-doc متد.
        await SelfHealLegacyRoleAsync(user, roleNames);

        // ================== اتصال خودکار معرف ==================
        // کاربر با نقش معرف که هنوز معرفی به او متصل نشده: یک معرف هم‌نام ساخته و متصل می‌شود
        // تا پنل معرف بدون خطا کار کند. مدیر می‌تواند بعداً از ویرایش کاربر، معرف دیگری متصل کند.
        if (user.Role == "Referrer" && user.ReferrerId is not > 0)
        {
            var autoRef = new Db.Referrer { Name = user.Username, IsActive = true };
            _db.Referrers.Add(autoRef);
            await _db.SaveChangesAsync();
            user.ReferrerId = autoRef.Id;
            await _db.SaveChangesAsync();
            Console.WriteLine($"[Auth] معرف «{user.Username}» به‌صورت خودکار ساخته و به کاربر متصل شد.");
        }

        var display = UserDisplay.Name(user);
        if (string.IsNullOrWhiteSpace(display)) display = user.Username;
        if (user.ReferrerId is > 0 && string.IsNullOrWhiteSpace($"{user.FirstName} {user.LastName}".Trim()))
        {
            var r = await _db.Referrers.FindAsync(user.ReferrerId);
            if (r is not null) display = r.Name;
        }

        // ================== دسترسی‌های مؤثر کاربر (RBAC) ==================
        List<string> permissions;
        if (roleIds.Count > 0)
        {
            // اجتماع پرمیشن‌های همه‌ی نقش‌های فعال کاربر
            permissions = await _db.RolePermissions
                .Where(rp => roleIds.Contains(rp.RoleId))
                .Join(_db.Permissions, rp => rp.PermissionId, p => p.Id, (rp, p) => p.Module + "." + p.Action)
                .Distinct().ToListAsync();
        }
        else
        {
            // کاربر قدیمی بدون نقش RBAC — دسترسی بر اساس نقش قدیمی (سازگاری عقب‌رو)
            permissions = user.Role switch
            {
                "Admin" => await _db.Permissions.Select(p => p.Module + "." + p.Action).ToListAsync(),
                "Operator" or "Accountant" => await _db.Permissions
                    .Where(p => (p.Module != "SystemUsers" && p.Module != "Settings" && p.Module != "DocArchive" && p.Module != "ItRequests")
                                || p.Module == "Dashboards" || p.Module == "ReportPages"
                                || (p.Module == "ItRequests" && (p.Action == "Create" || p.Action == "ViewDepartment"))
                                )
                    .Select(p => p.Module + "." + p.Action).ToListAsync(),
                // سایر نقش‌های قدیمی: پنل معرف + کارتابل شخصی منابع انسانی
                // + صفحه‌های شخصی (کارتابل من / بایگانی شخصی / داشبورد من) که برای همهٔ کاربران است
                _ => await _db.Permissions.Where(p => p.Module == "ReferrerPanel"
                                                      || p.Module == "MyCartable"
                                                      || p.Module == "MyArchive"
                                                      || p.Module == "MyDashboards")
                    .Select(p => p.Module + "." + p.Action).ToListAsync()
            };
        }

        // -------- آرشیو اسناد و مدارک: دسترسی «به‌ازای هر پوشه و هر مدرک» --------
        // مجوز DocArchive.* در «تنظیمات ← نقش‌ها و دسترسی‌ها» یک دسترسی سراسری است؛ اگر
        // کاربر آن را نداشته باشد اما روی پوشه/مدرکی به او دسترسی داده شده باشد، باید
        // منوی آرشیو را ببیند و داخل ماژول فقط همان مواردِ مجاز را ببیند (فیلتر واقعی
        // سمت سرور و به‌ازای هر آیتم انجام می‌شود). پس فقط «Read» مجازی اضافه می‌شود —
        // ایجاد/ویرایش/حذف همچنان به سطح همان پوشه/مدرک وابسته است.
        if (permissions.All(p => !p.StartsWith("DocArchive.", StringComparison.OrdinalIgnoreCase))
            && await DocArchiveAccessProbe.AnyAsync(_db, user.Id))
        {
            permissions.Add("DocArchive.Read");
        }

        // توکن واحد برنامه شامل دسترسی‌های مؤثر است؛ Host ماژول RADIS-HR نیز همین
        // توکن را اعتبارسنجی می‌کند و بنابراین ورود مجدد یا حساب کاربری جدا لازم نیست.
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.Role, user.Role)
        };
        if (user.ReferrerId is > 0)
            claims.Add(new Claim("referrerId", user.ReferrerId.Value.ToString()));
        claims.AddRange(permissions.Select(permission => new Claim("permission", permission)));

        // کنترلرهای اصلی RADIS-HR در دو عملیات مدیریتی نقش‌های legacy خود ماژول را
        // بررسی می‌کنند. مدیر هسته هر دو نقش مدیریتی را در توکن واحد دریافت می‌کند.
        if (user.Role == "Admin" || permissions.Contains("RadisHr.Access", StringComparer.OrdinalIgnoreCase))
        {
            // RadisHr.Access در نسخه V019 مجوز کل ماژول است؛ نقش‌های زیر فقط برای حفظ
            // قواعد Authorize اصلی کنترلرها به توکن SSO افزوده می‌شوند.
            foreach (var radisRole in new[] { "hr", "ceo", "guard", "hse", "warehouse", "production", "finance", "accounting" })
                claims.Add(new Claim(ClaimTypes.Role, radisRole));
        }
        else
        {
            // دسترسی بخش‌محور (تنظیمات ← نقش‌ها و دسترسی‌ها ← منابع انسانی بن‌سازه):
            // دارندهٔ مجوز هر بخش، فقط نقش‌های legacy همان بخش را می‌گیرد تا عملیات
            // روزمرهٔ بخش خودش را انجام دهد؛ نقش‌های مدیریتی «hr» و «ceo» (پرسنل،
            // ساختار سازمانی، حقوق، الزامات و اطلاعیه‌ها) همچنان مخصوص مجوز کل ماژول است.
            if (permissions.Contains("RadisHr.Attendance", StringComparer.OrdinalIgnoreCase))
                claims.Add(new Claim(ClaimTypes.Role, "guard"));
            if (permissions.Contains("RadisHr.Hse", StringComparer.OrdinalIgnoreCase))
                claims.Add(new Claim(ClaimTypes.Role, "hse"));
            if (permissions.Contains("RadisHr.Finance", StringComparer.OrdinalIgnoreCase))
                claims.Add(new Claim(ClaimTypes.Role, "finance"));
            if (permissions.Contains("RadisHr.Accounting", StringComparer.OrdinalIgnoreCase))
                claims.Add(new Claim(ClaimTypes.Role, "accounting"));
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtKey));
        var token = new JwtSecurityToken(
            issuer: JwtIssuer,
            audience: JwtIssuer,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(12),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return new LoginResponse
        {
            UserId = user.Id,
            Token = new JwtSecurityTokenHandler().WriteToken(token),
            Username = user.Username,
            Role = user.Role,
            ReferrerId = user.ReferrerId,
            DisplayName = display,
            RoleNames = roleNames,
            Permissions = permissions
        };
    }

    // ---------------- مدیریت کاربران ----------------

    public async Task<List<UserDto>> GetUsersAsync(Paging.Request? pagination = null)
    {
        var users = await _db.Users.OrderBy(u => u.Username).ThenBy(x => x.Id).ToPageListAsync(pagination);
        var refNames = await _db.Referrers.ToDictionaryAsync(r => r.Id, r => r.Name);
        // نقش‌های RBAC هر کاربر لاگین
        var roleNames = await _db.Roles.ToDictionaryAsync(r => r.Id, r => r.Name);
        var ids = users.Select(u => u.Id).ToList();
        var userRoles = await _db.UserRoles.Where(ur => ids.Contains(ur.UserId)).ToListAsync();
        // سمت‌های فعالِ هر کاربر (هر کاربر می‌تواند چند سمت داشته باشد)
        var positions = await UserPositionMapAsync(ids);
        return users.Select(u => new UserDto
        {
            Id = u.Id,
            Username = u.Username,
            Role = u.Role,
            RoleIds = userRoles.Where(ur => ur.UserId == u.Id).Select(ur => ur.RoleId).ToList(),
            RoleNames = userRoles.Where(ur => ur.UserId == u.Id)
                .Select(ur => roleNames.GetValueOrDefault(ur.RoleId, ""))
                .Where(n => n != "").ToList(),
            ReferrerId = u.ReferrerId,
            ReferrerName = u.ReferrerId.HasValue && refNames.TryGetValue(u.ReferrerId.Value, out var n) ? n : null,
            FirstName = u.FirstName,
            LastName = u.LastName,
            Mobile = u.Mobile,
            BaleChatId = u.BaleChatId,
            EitaaChatId = u.EitaaChatId,
            SignaturePath = u.SignaturePath,
            IsActive = u.IsActive,
            CreatedAt = u.CreatedAt,
            Positions = positions.TryGetValue(u.Id, out var p) ? p : new List<string>()
        }).ToList();
    }

    /// <summary>
    /// نگاشتِ شناسهٔ کاربر → فهرستِ عنوانِ سمت‌های فعالِ او.
    /// سمت‌ها در جدولِ Semats نگه‌داری می‌شوند (Semats.UserId)؛ هر کاربر می‌تواند
    /// چند سمت فعال داشته باشد.
    /// </summary>
    private async Task<Dictionary<int, List<string>>> UserPositionMapAsync(List<int> userIds)
    {
        var map = new Dictionary<int, List<string>>();
        if (userIds.Count == 0) return map;
        var rows = await _db.Semats.AsNoTracking()
            .Where(s => s.UserId != null && userIds.Contains(s.UserId.Value) && !s.IsDelete && s.IsActive)
            .OrderBy(s => s.SematId)
            .Select(s => new { s.UserId, s.Title })
            .ToListAsync();
        foreach (var r in rows)
        {
            var title = (r.Title ?? "").Trim();
            if (title.Length == 0) continue;
            if (!map.TryGetValue(r.UserId!.Value, out var list))
            {
                list = new List<string>();
                map[r.UserId!.Value] = list;
            }
            if (!list.Any(t => string.Equals(t, title, StringComparison.OrdinalIgnoreCase)))
                list.Add(title);
        }
        return map;
    }

    /// <summary>سازمانِ پیش‌فرض — سمت‌های کاربر به آن نسبت داده می‌شوند.</summary>
    private async Task<int> DefaultOrganizationIdAsync()
    {
        var id = await _db.Organizations.AsNoTracking()
            .Where(o => !o.IsDelete && o.IsActive && o.IsDefault)
            .Select(o => (int?)o.OrganizationId)
            .FirstOrDefaultAsync();
        if (id is > 0) return id.Value;

        var any = await _db.Organizations.AsNoTracking()
            .Where(o => !o.IsDelete)
            .Select(o => (int?)o.OrganizationId)
            .FirstOrDefaultAsync();
        if (any is > 0) return any.Value;

        // سازمانِ پیش‌فرض معمولاً در DbInitializer ساخته می‌شود؛ اگر نبود، ساخته می‌شود.
        // چون NameUniq ایندکسِ یکتا دارد، کدِ یکتا انتخاب می‌شود.
        var code = "MQ";
        var suffix = 1;
        while (await _db.Organizations.AnyAsync(o => o.NameUniq == code))
            code = $"MQ-{suffix++}";

        var org = new Db.Organization { NameUnit = "سازمان پیش‌فرض", NameUniq = code, IsDefault = true, IsActive = true };
        _db.Organizations.Add(org);
        await _db.SaveChangesAsync();
        return org.OrganizationId;
    }

    /// <summary>
    /// هم‌گام‌سازیِ سمت‌های کاربر با فهرستِ عنوان‌های ارسالی.
    /// سمت‌هایی که دیگر در فهرست نیستند حذف (soft-delete) و سمت‌های جدید ساخته
    /// می‌شوند. اولین سمت به‌عنوان «سمت پیش‌فرض» علامت می‌خورد تا در انتخابِ
    /// امضاکنندگانِ نامهٔ صادره، سمتِ اصلیِ کاربر مشخص باشد.
    /// </summary>
    private async Task SyncUserPositionsAsync(int userId, List<string>? titles)
    {
        if (titles is null) return;
        var wanted = titles
            .Select(t => (t ?? "").Trim())
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var current = await _db.Semats.Where(s => s.UserId == userId && !s.IsDelete).ToListAsync();
        foreach (var s in current.Where(s => !wanted.Any(t =>
                     string.Equals(t, (s.Title ?? "").Trim(), StringComparison.OrdinalIgnoreCase))))
        {
            s.IsDelete = true;
            s.IsActive = false;
            s.DefaultSemat = false;
        }

        // آیا هنوز سمتِ پیش‌فرضِ فعالی برای این کاربر باقی مانده است؟
        var hasDefault = current.Any(s => s.DefaultSemat);

        var orgId = await DefaultOrganizationIdAsync();
        var newTitles = wanted.Where(t => !current.Any(s =>
                            string.Equals(t, (s.Title ?? "").Trim(), StringComparison.OrdinalIgnoreCase))).ToList();
        for (var i = 0; i < newTitles.Count; i++)
        {
            _db.Semats.Add(new Db.Semat
            {
                UserId = userId,
                Title = newTitles[i],
                OrganizationId = orgId,
                IsActive = true,
                IsDelete = false,
                // فقط اولین سمتِ جدید، در صورت نبودِ سمتِ پیش‌فرض، پیش‌فرض می‌شود
                DefaultSemat = !hasDefault && i == 0
            });
            hasDefault = true;
        }
        await _db.SaveChangesAsync();
    }

    /// <summary>نگاشت نقش‌های RBAC به نقش قدیمی (برای احراز هویت/JWT).</summary>
    /// <remarks>
    /// ⚠️ فقط «نام دقیق» نقش‌های استاندارد معیار است. پیش‌تر هر نقشی که در نامش «مدیر»/«ادمین»
    /// بود کاربر را «مدیر کل» می‌کرد؛ یعنی نقش‌هایی مثل «مدیر فروش» یا «مدیر انبار» (حتی با
    /// دو-سه دسترسی محدود) کاربر را صاحب «دسترسی کامل» می‌کردند، چون ده‌ها نقطهٔ برنامه
    /// با <c>User.IsInRole("Admin")</c> تصمیم می‌گیرند. حالا برای سطح مدیر، نقش «Admin»
    /// باید حتماً انتخاب شود.
    /// توجه: نام‌های فارسیِ «معرف…» همچنان سطح Referrer می‌دهند؛ این انتخاب عمدی است چون
    /// نتیجه‌اش در بدترین حالت «کاهش» دسترسی است، نه اعطای دسترسی اضافه.
    /// </remarks>
    private static string DeriveLegacyRole(List<string> rbacRoleNames, string fallback)
    {
        if (rbacRoleNames.Count == 0) return fallback;

        if (rbacRoleNames.Any(n => n.Equals("Admin", StringComparison.OrdinalIgnoreCase)))
            return "Admin";
        if (rbacRoleNames.Any(n => n.Equals("Referrer", StringComparison.OrdinalIgnoreCase) || n.Contains("معرف", StringComparison.Ordinal)))
            return "Referrer";
        // سایر نقش‌ها (اپراتور، حسابدار، «مدیر فروش» و…) در سطح ورود، اپراتور محسوب می‌شوند
        return "Operator";
    }

    /// <summary>
    /// خودترمیمی «سطح دسترسی قدیمی» (ستون <c>Users.Role</c>) بر اساس نقش‌های RBAC فعال کاربر.
    /// کاربرانی که با نسخهٔ قبلیِ DeriveLegacyRole به‌خاطر نام نقش (مثل «مدیر فروش») «مدیر کل»
    /// شده بودند، هنگام ورود به سطح درست برمی‌گردند تا «دسترسی کامل» ناخواسته باقی نماند.
    /// تغییر در «لاگ عملیات» (تنظیمات ← لاگ عملیات) ثبت می‌شود.
    /// </summary>
    private async Task SelfHealLegacyRoleAsync(Db.User user, List<string> activeRoleNames)
    {
        // کاربران قدیمی بدون نقش RBAC (و نقش‌های حذف‌شده) دست‌نخورده می‌مانند
        if (activeRoleNames.Count == 0) return;

        var derived = DeriveLegacyRole(activeRoleNames, user.Role);
        if (string.Equals(derived, user.Role, StringComparison.Ordinal)) return;

        // آخرین مدیر فعال سیستم هرگز خودکار پایین آورده نمی‌شود (جلوگیری از قفل‌شدن مدیریت)
        if (user.Role == "Admin" && user.IsActive
            && !await _db.Users.AnyAsync(u => u.Id != user.Id && u.IsActive && u.Role == "Admin"))
        {
            Console.WriteLine($"[Auth] کاربر «{user.Username}» تنها مدیر فعال است؛ تصحیح خودکار سطح دسترسی انجام نشد.");
            return;
        }

        var from = user.Role;
        user.Role = derived;
        _db.AuditLogs.Add(new Db.AuditLog
        {
            At = DateTime.Now,
            UserId = user.Id,
            Username = user.Username.Length > 100 ? user.Username[..100] : user.Username,
            Module = "Auth",
            Action = "SelfHealRole",
            HttpMethod = "SYSTEM",
            Path = "login",
            Summary = $"اصلاح خودکار سطح دسترسی: «{from}» ← «{derived}» (بر اساس نقش‌های RBAC)",
            StatusCode = 200
        });
        await _db.SaveChangesAsync();
        Console.WriteLine($"[Auth] سطح دسترسی کاربر «{user.Username}» از «{from}» به «{derived}» اصلاح شد (بر اساس نقش‌های RBAC).");
    }

    public async Task<UserDto> SaveUserAsync(UserDto dto, int currentUserId, bool callerIsAdmin = true)
    {
        var username = (dto.Username ?? "").Trim();
        var requestedMobile = string.IsNullOrWhiteSpace(dto.Mobile) ? null : dto.Mobile.Trim();
        if (string.IsNullOrWhiteSpace(username))
            throw new InvalidOperationException("نام کاربری را وارد کنید.");

        // ================== نقش‌های RBAC (انتخاب از بخش نقش‌ها و دسترسی‌ها) ==================
        // نقش قدیمی (Admin/Operator/Referrer) به‌صورت خودکار از نقش‌های انتخابی مشتق می‌شود
        List<string>? rbacNames = null;
        List<int>? validRoleIds = null;
        if (dto.RoleIds is { Count: > 0 } rids)
        {
            var selectedRoles = await _db.Roles.Where(r => rids.Contains(r.Id))
                .Select(r => new { r.Id, r.Name }).ToListAsync();
            if (selectedRoles.Count == 0)
                throw new InvalidOperationException("نقش‌های انتخاب‌شده معتبر نیستند؛ نقش را دوباره انتخاب کنید.");
            rbacNames = selectedRoles.Select(r => r.Name).ToList();
            validRoleIds = selectedRoles.Select(r => r.Id).ToList(); // فقط نقش‌های موجود ذخیره شوند (بدون رکورد یتیم)
            dto.Role = DeriveLegacyRole(rbacNames, dto.Role);
        }
        else if (dto.Id == 0)
        {
            // ⚠️ کاربر جدیدِ بدون نقش RBAC: هیچ محدودیت واقعی ندارد و پیش‌فرض فرم «مدیر» بود ⇒
            // چنین کاربری با «دسترسی کامل» وارد می‌شد. پس ثبتش ممنوع است.
            throw new InvalidOperationException(await _db.Roles.AnyAsync(r => r.IsActive)
                ? "حداقل یک نقش برای کاربر جدید انتخاب کنید."
                : "هیچ نقش فعالی تعریف نشده است؛ ابتدا از «تنظیمات ← نقش‌ها و دسترسی‌ها» یک نقش بسازید.");
        }
        else if (dto.RoleIds is { Count: 0 } && await _db.UserRoles.AnyAsync(ur => ur.UserId == dto.Id))
        {
            // پاک‌کردن همهٔ نقش‌های RBAC یک کاربر موجود ⇒ او به «سطح قدیمی» برمی‌گردد که برای
            // Admin/Operator معادل دسترسی گسترده است (همان باگ «دسترسی کامل»). برای قطع دسترسی،
            // کاربر باید «غیرفعال» شود.
            throw new InvalidOperationException("حداقل یک نقش برای کاربر لازم است؛ برای قطع دسترسی، وضعیت کاربر را «غیرفعال» کنید.");
        }

        if (dto.Role is not ("Admin" or "Operator" or "Referrer"))
            throw new InvalidOperationException("نقش کاربر نامعتبر است.");
        // معرف مرتبط اختیاری است — کاربر معرف می‌تواند بدون معرف ثبت شود
        // و بعداً از ویرایش کاربر، معرف به او متصل شود.

        // اپراتور: هیچ عملیاتی مرتبط با نقش ادمین مجاز نیست
        if (!callerIsAdmin && dto.Role == "Admin")
            throw new InvalidOperationException("اپراتور نمی‌تواند کاربر با نقش «مدیر» ایجاد کند یا نقش کاربری را به مدیر تغییر دهد.");

        var dup = await _db.Users.AnyAsync(u => u.Username == username && u.Id != dto.Id);
        if (dup) throw new InvalidOperationException("این نام کاربری قبلاً ثبت شده است.");

        Db.User entity;
        if (dto.Id == 0)
        {
            if (string.IsNullOrWhiteSpace(dto.Password))
                throw new InvalidOperationException("رمز عبور را وارد کنید.");
            entity = new Db.User { CreatedAt = DateTime.Now };
            _db.Users.Add(entity);
        }
        else
        {
            entity = await _db.Users.FindAsync(dto.Id)
                ?? throw new InvalidOperationException("کاربر یافت نشد.");

            // اپراتور نمی‌تواند کاربر ادمین موجود را ویرایش کند (نه نقش، نه رمز، نه وضعیت)
            if (!callerIsAdmin && entity.Role == "Admin")
                throw new InvalidOperationException("اپراتور اجازه ویرایش کاربران «مدیر» را ندارد.");

            // هر تغییر شمارهٔ خودِ کاربر باید از مسیر OTP انجام شود؛ وگرنه API مدیریت کاربران
            // می‌تواند هم تأییدِ شمارهٔ قطعی و هم مرحلهٔ تأییدِ شمارهٔ جدید را دور بزند.
            if (entity.Id == currentUserId
                && !string.Equals(entity.Mobile, requestedMobile, StringComparison.Ordinal))
                throw new InvalidOperationException(entity.MobileVerified
                    ? "شمارهٔ موبایلِ تأییدشدهٔ خودتان قابل تغییر نیست."
                    : "برای تغییر شمارهٔ موبایل خود از تنظیمات شخصی و تأیید OTP استفاده کنید.");
        }

        entity.Username = username;
        entity.Role = dto.Role;
        entity.FirstName = string.IsNullOrWhiteSpace(dto.FirstName) ? null : dto.FirstName.Trim();
        entity.LastName = string.IsNullOrWhiteSpace(dto.LastName) ? null : dto.LastName.Trim();
        if (!string.Equals(entity.Mobile, requestedMobile, StringComparison.Ordinal))
        {
            entity.Mobile = requestedMobile;
            entity.MobileVerified = false;

            // تغییر شماره از پنل مدیریت، کد قبلیِ در انتظار را باطل می‌کند؛
            // تأییدِ یک درخواست قدیمی نباید شمارهٔ تازهٔ مدیر را بی‌خبر جایگزین کند.
            if (entity.Id > 0)
            {
                var pendingMobileOtp = await _db.UserContactOtpChallenges.FirstOrDefaultAsync(c =>
                    c.UserId == entity.Id && c.Purpose == Db.UserContactOtpChallenge.MobilePurpose);
                if (pendingMobileOtp is not null)
                {
                    pendingMobileOtp.Destination = "";
                    pendingMobileOtp.ProtectedCode = "";
                    pendingMobileOtp.ExpiresAt = DateTime.UtcNow;
                    pendingMobileOtp.FailedAttempts = 0;
                    pendingMobileOtp.LockedUntil = null;
                }
            }
        }
        entity.BaleChatId = string.IsNullOrWhiteSpace(dto.BaleChatId) ? null : dto.BaleChatId.Trim();
        entity.EitaaChatId = string.IsNullOrWhiteSpace(dto.EitaaChatId) ? null : dto.EitaaChatId.Trim();
        // پیوند کاربر به «معرف» مستقل از سطح قدیمی است: ویزیتورِ میدانی با نقش RBAC
        // «SalesVisitor» در سطح قدیمی «اپراتور» می‌شود، ولی باید به معرف خودش وصل بماند
        // تا سفارش‌هایش در پروندهٔ درست ثبت شود (فقط سطح «مدیر» بی‌نیاز از معرف است).
        entity.ReferrerId = dto.Role == "Admin" ? null : (dto.ReferrerId is > 0 ? dto.ReferrerId : null);
        entity.IsActive = dto.IsActive;
        if (!string.IsNullOrWhiteSpace(dto.Password))
        {
            if (dto.Password.Length < 4)
                throw new InvalidOperationException("رمز عبور باید حداقل ۴ کاراکتر باشد.");
            entity.PasswordHash = HashPassword(dto.Password);
        }

        await _db.SaveChangesAsync();

        // ================== ذخیره نقش‌های RBAC کاربر ==================
        if (dto.RoleIds != null)
        {
            var oldRoles = await _db.UserRoles.Where(ur => ur.UserId == entity.Id).ToListAsync();
            _db.UserRoles.RemoveRange(oldRoles);
            foreach (var rid in (validRoleIds ?? dto.RoleIds).Distinct())
                _db.UserRoles.Add(new UserRole { UserId = entity.Id, RoleId = rid });
            await _db.SaveChangesAsync();
        }

        // ================== سمت‌های کاربر (می‌تواند چند سمت فعال داشته باشد) ==================
        // فقط وقتی فرم، فهرستِ سمت‌ها را فرستاده باشد هم‌گام می‌شود؛ تا فراخوانی‌های
        // دیگر (مثلاً تغییر رمز از پنل شخصی) سمت‌های کاربر را پاک نکنند.
        if (dto.Positions is not null)
            await SyncUserPositionsAsync(entity.Id, dto.Positions);

        return (await GetUsersAsync()).First(u => u.Id == entity.Id);
    }

    public async Task DeleteUserAsync(int id, int currentUserId, bool callerIsAdmin = true)
    {
        if (id == currentUserId)
            throw new InvalidOperationException("نمی‌توانید حساب کاربری خودتان را حذف کنید.");
        var u = await _db.Users.FindAsync(id);
        if (u is null) return;

        // اپراتور نمی‌تواند کاربر ادمین را حذف کند
        if (!callerIsAdmin && u.Role == "Admin")
            throw new InvalidOperationException("اپراتور اجازه حذف کاربران «مدیر» را ندارد.");
        var adminCount = await _db.Users.CountAsync(x => x.Role == "Admin" && x.IsActive);
        if (u.Role == "Admin" && adminCount <= 1)
            throw new InvalidOperationException("حداقل یک مدیر فعال باید باقی بماند.");
        // حذف نقش‌های RBAC کاربر (جلوگیری از رکورد یتیم)
        var uroles = await _db.UserRoles.Where(ur => ur.UserId == u.Id).ToListAsync();
        _db.UserRoles.RemoveRange(uroles);
        _db.Users.Remove(u);
        await _db.SaveChangesAsync();
    }

    public async Task ChangePasswordAsync(int userId, string currentPassword, string newPassword)
    {
        var u = await _db.Users.FindAsync(userId)
            ?? throw new InvalidOperationException("کاربر یافت نشد.");
        if (!VerifyPassword(currentPassword ?? "", u.PasswordHash))
            throw new InvalidOperationException("رمز عبور فعلی اشتباه است.");
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 4)
            throw new InvalidOperationException("رمز عبور جدید باید حداقل ۴ کاراکتر باشد.");
        u.PasswordHash = HashPassword(newPassword);
        await _db.SaveChangesAsync();
    }

    // ---------------- داشبورد معرف ----------------

    public async Task<ReferrerDashboard> GetReferrerDashboardAsync(int referrerId)
    {
        var referrer = await _db.Referrers.FindAsync(referrerId)
            ?? throw new InvalidOperationException("معرف یافت نشد.");

        var wallets = await _inventory.GetReferrerWalletsAsync(null, "name", false);
        var wallet = wallets.FirstOrDefault(w => w.Id == referrerId);

        var txns = await _db.Transactions
            .Where(t => t.ReferrerId == referrerId && t.Type == TransactionType.Sale)
            .OrderByDescending(t => t.Date).ThenByDescending(t => t.Id)
            .ToListAsync();

        var partyNames = await _db.Parties.ToDictionaryAsync(p => p.Id, p => p.Name);

        // پورسانت ماه جاری شمسی + آخرین فروش‌ها
        var (jy, jm, _) = PersianDate.FromGregorian(DateTime.Now);
        var monthStart = PersianDate.ToGregorian(jy, jm, 1);

        decimal monthCommission = 0;
        var recent = new List<ReferrerSaleRow>();
        foreach (var t in txns)
        {
            var order = await _inventory.GetOrderAsync(t.Id);
            var commission = order?.CommissionAmount ?? 0;
            if (t.Date >= monthStart) monthCommission += commission;
            if (recent.Count < 8)
            {
                recent.Add(new ReferrerSaleRow
                {
                    Number = t.Number,
                    Date = t.Date,
                    CustomerName = t.PartyId.HasValue && partyNames.TryGetValue(t.PartyId.Value, out var pn) ? pn : null,
                    Amount = t.Amount,
                    Commission = commission
                });
            }
        }

        var payments = await _inventory.GetReferrerPaymentsAsync(referrerId);

        return new ReferrerDashboard
        {
            ReferrerName = referrer.Name,
            Phone = referrer.Phone,
            CanViewProducts = referrer.CanViewProducts,
            CompanyName = referrer.CompanyName,
            GoodsCommissionPercent = referrer.GoodsCommissionPercent,
            ServiceCommissionPercent = referrer.ServiceCommissionPercent,
            OrderCount = txns.Count,
            TotalSales = txns.Sum(t => t.Amount),
            TotalCommission = wallet?.TotalCommission ?? 0,
            TotalPaid = wallet?.TotalPaid ?? 0,
            WalletBalance = wallet?.WalletBalance ?? 0,
            MonthCommission = monthCommission,
            RecentSales = recent,
            RecentPayments = payments.Take(8).ToList()
        };
    }
}
