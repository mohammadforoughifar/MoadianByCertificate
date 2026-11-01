using Inventory.Api.Data;
using Inventory.Api.Hubs;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services;

/// <summary>
/// سرویس امنیت حضور و غیاب:
/// ۱) ثبت/تشخیص دستگاه‌ها (Device ID از localStorage + IP + User-Agent)
/// ۲) هشدار «دستگاه جدید» و «دستگاه مشترک» برای مدیر
/// ۳) اعتبارسنجی موقعیت مکانی نسبت به محدوده‌ی مجاز تعیین‌شده توسط مدیر سیستم
/// </summary>
public class AttendanceSecurityService
{
    public const string AlertNewDevice = "NewDevice";
    public const string AlertSharedDevice = "SharedDevice";
    public const string AlertOutOfRange = "OutOfRange";

    public const string StatusPending = "Pending";
    public const string StatusApproved = "Approved";
    public const string StatusRejected = "Rejected";

    private readonly AppDbContext _db;
    private readonly INotifyService _notify;

    public AttendanceSecurityService(AppDbContext db, INotifyService notify)
    {
        _db = db;
        _notify = notify;
    }

    // ====================================================================
    //  ثبت ورود/خروج: تشخیص دستگاه و موقعیت
    // ====================================================================

    /// <summary>
    /// در هر ورود/خروج صدا زده می‌شود:
    ///  - دستگاه را ثبت/به‌روزرسانی می‌کند (اولین دستگاه = اصلی)
    ///  - اگر دستگاه جدید بود → هشدار «دستگاه جدید» (نیازمند تأیید مدیر)
    ///  - اگر دستگاه قبلاً توسط کاربر دیگری استفاده شده بود → هشدار «دستگاه مشترک»
    ///  - اگر موقعیت خارج از محدوده‌ی مجاز بود → هشدار «خارج از محدوده»
    /// </summary>
    /// <param name="userId">شناسه کاربر</param>
    /// <param name="userName">نام کاربر (برای هشدارها)</param>
    /// <param name="deviceId">شناسه دستگاه (از localStorage مرورگر)</param>
    /// <param name="ip">IP اتصال (سمت سرور)</param>
    /// <param name="userAgent">User-Agent مرورگر</param>
    /// <param name="lat">عرض جغرافیایی (اختیاری)</param>
    /// <param name="lng">طول جغرافیایی (اختیاری)</param>
    /// <param name="adminIds">شناسه مدیرانی که هشدار برایشان ارسال می‌شود</param>
    public async Task<IReadOnlyList<AttendanceAlert>> ValidateAsync(
        int userId, string userName, string? deviceId, string? ip, string? userAgent,
        double? lat, double? lng, IReadOnlyList<int> adminIds)
    {
        var alerts = new List<AttendanceAlert>();

        // ---------- ۱) دستگاه ----------
        if (!string.IsNullOrWhiteSpace(deviceId))
        {
            var deviceIdNorm = deviceId.Trim();
            deviceIdNorm = deviceIdNorm.Length > 100 ? deviceIdNorm[..100] : deviceIdNorm;

            var existing = await _db.UserDevices
                .FirstOrDefaultAsync(d => d.UserId == userId && d.DeviceId == deviceIdNorm);

            if (existing != null)
            {
                // دستگاه شناخته‌شده: فقط به‌روزرسانی
                existing.LastSeenAt = DateTime.Now;
                existing.UsedCount++;
                if (!string.IsNullOrWhiteSpace(ip)) existing.Ip = ip;
                if (!string.IsNullOrWhiteSpace(userAgent)) existing.UserAgent = userAgent;
            }
            else
            {
                // دستگاه جدید برای این کاربر
                var hasPrimary = await _db.UserDevices.AnyAsync(d => d.UserId == userId && d.IsPrimary);
                var newDev = new UserDevice
                {
                    UserId = userId,
                    UserName = userName,
                    DeviceId = deviceIdNorm,
                    Ip = ip,
                    UserAgent = userAgent,
                    // اگر این اولین دستگاه کاربر است → همان «اصلی» و تأییدشده است؛
                    // در غیر این صورت تأیید مدیر لازم دارد.
                    IsPrimary = !hasPrimary,
                    IsApproved = !hasPrimary,
                    FirstSeenAt = DateTime.Now,
                    LastSeenAt = DateTime.Now,
                    UsedCount = 1,
                };
                _db.UserDevices.Add(newDev);

                if (hasPrimary)
                {
                    // هشدار «دستگاه جدید» — مدیر باید تأیید کند
                    var alert = new AttendanceAlert
                    {
                        UserId = userId,
                        UserName = userName,
                        AlertType = AlertNewDevice,
                        DeviceId = deviceIdNorm,
                        Ip = ip,
                        Lat = lat,
                        Lng = lng,
                        Message = $"{userName} با یک دستگاه جدید وارد شده است. در صورت تأیید مدیر، این دستگاه به‌عنوان دستگاه اصلی ثبت می‌شود.",
                        Status = StatusPending,
                    };
                    _db.AttendanceAlerts.Add(alert);
                    alerts.Add(alert);

                    if (adminIds.Count > 0)
                    {
                        try
                        {
                            var uaShort = string.IsNullOrWhiteSpace(userAgent) ? "—" : (userAgent.Length > 80 ? userAgent[..80] : userAgent);
                            await _notify.SendManyAsync(adminIds,
                                "⚠️ ورود با دستگاه جدید",
                                $"{userName} با دستگاهی جدید ثبت حضور/غیاب کرد.\nدستگاه: {uaShort}\nIP: {ip ?? "—"}\n— از بخش «مدیریت حضور و غیاب» بررسی و تأیید/رد کنید.",
                                userName, "حضور و غیاب", "/attendance-admin?tab=security");
                        }
                        catch { /* نوتیفیکیشن ثانویه است؛ خطا بی‌اثر */ }
                    }
                }
            }

            // ---------- ۲) دستگاه مشترک بین دو نفر ----------
            var otherUserDevice = await _db.UserDevices.AsNoTracking()
                .FirstOrDefaultAsync(d => d.DeviceId == deviceIdNorm && d.UserId != userId);
            if (otherUserDevice != null)
            {
                var dup = await _db.AttendanceAlerts.AsNoTracking()
                    .AnyAsync(a => a.AlertType == AlertSharedDevice
                                   && a.DeviceId == deviceIdNorm
                                   && a.Status == StatusPending);
                if (!dup)
                {
                    var alert = new AttendanceAlert
                    {
                        UserId = userId,
                        UserName = userName,
                        AlertType = AlertSharedDevice,
                        DeviceId = deviceIdNorm,
                        Ip = ip,
                        Lat = lat,
                        Lng = lng,
                        Message = $"دستگاه «{deviceIdNorm}» توسط {otherUserDevice.UserName} نیز استفاده شده است (مشکوک به ثبت حضور توسط یک نفر به جای دیگری).",
                        Status = StatusPending,
                    };
                    _db.AttendanceAlerts.Add(alert);
                    alerts.Add(alert);

                    if (adminIds.Count > 0)
                    {
                        try
                        {
                            await _notify.SendManyAsync(adminIds,
                                "⚠️ استفاده از دستگاه مشترک",
                                $"دستگاهی که {userName} با آن ورود زده، قبلاً توسط {otherUserDevice.UserName} استفاده شده است. لطفاً بررسی کنید.",
                                userName, "حضور و غیاب", "/attendance-admin?tab=security");
                        }
                        catch { }
                    }
                }
            }
        }

        // ---------- ۳) موقعیت مکانی ----------
        if (lat.HasValue && lng.HasValue)
        {
            var area = await _db.AttendanceAreaSettings.OrderByDescending(s => s.UpdatedAt).FirstOrDefaultAsync();
            if (area != null)
            {
                var dist = HaversineMeters(area.Latitude, area.Longitude, lat.Value, lng.Value);
                if (dist > area.RadiusMeters)
                {
                    var dup = await _db.AttendanceAlerts.AsNoTracking()
                        .AnyAsync(a => a.AlertType == AlertOutOfRange && a.Status == StatusPending && a.UserId == userId);
                    if (!dup)
                    {
                        var alert = new AttendanceAlert
                        {
                            UserId = userId,
                            UserName = userName,
                            AlertType = AlertOutOfRange,
                            DeviceId = deviceId,
                            Ip = ip,
                            Lat = lat,
                            Lng = lng,
                            DistanceMeters = Math.Round(dist, 1),
                            Message = $"ثبت حضور/غیاب {userName} از فاصله‌ی حدود {FaDigits(Math.Round(dist))} متر از محل مجاز انجام شده (محدوده‌ی مجاز: {FaDigits(area.RadiusMeters)} متر).",
                            Status = StatusPending,
                        };
                        _db.AttendanceAlerts.Add(alert);
                        alerts.Add(alert);

                        if (adminIds.Count > 0)
                        {
                            try
                            {
                                await _notify.SendManyAsync(adminIds,
                                    "⚠️ ثبت حضور خارج از محدوده",
                                    $"{userName} از فاصله‌ی ~{FaDigits(Math.Round(dist))} متری محل مجاز ورود زده است. لطفاً بررسی کنید.",
                                    userName, "حضور و غیاب", "/attendance-admin?tab=security");
                            }
                            catch { }
                        }
                    }
                }
            }
        }

        await _db.SaveChangesAsync();
        return alerts;
    }

    // ====================================================================
    //  تأیید/رد دستگاه جدید توسط مدیر
    // ====================================================================

    /// <summary>تأیید دستگاه جدید: دستگاهِ هشدار، اصلی می‌شود و دستگاه قبلی از اصلی بودن خارج می‌شود.</summary>
    public async Task<bool> ApproveDeviceAsync(int alertId, int adminId)
    {
        var alert = await _db.AttendanceAlerts.FirstOrDefaultAsync(a => a.Id == alertId && a.Status == StatusPending);
        if (alert == null || alert.AlertType != AlertNewDevice) return false;

        var device = await _db.UserDevices.FirstOrDefaultAsync(d => d.UserId == alert.UserId && d.DeviceId == alert.DeviceId);
        if (device == null) return false;

        // دستگاه قبلی اصلی → غیراصلی
        var oldPrimary = await _db.UserDevices.Where(d => d.UserId == alert.UserId && d.IsPrimary && d.Id != device.Id).ToListAsync();
        foreach (var op in oldPrimary) { op.IsPrimary = false; op.IsApproved = false; }

        device.IsPrimary = true;
        device.IsApproved = true;
        device.ApprovedBy = adminId;
        device.ApprovedAt = DateTime.Now;

        alert.Status = StatusApproved;
        alert.HandledBy = adminId;
        alert.HandledAt = DateTime.Now;

        await _db.SaveChangesAsync();
        return true;
    }

    /// <summary>رد دستگاه جدید: دستگاه همچنان تأیید نشده می‌ماند (کاربر با آن می‌تواند ورود بزند ولی هشدار ثبت می‌شود).</summary>
    public async Task<bool> RejectDeviceAsync(int alertId, int adminId)
    {
        var alert = await _db.AttendanceAlerts.FirstOrDefaultAsync(a => a.Id == alertId && a.Status == StatusPending);
        if (alert == null) return false;

        var device = await _db.UserDevices.FirstOrDefaultAsync(d => d.UserId == alert.UserId && d.DeviceId == alert.DeviceId);
        if (device != null)
        {
            device.IsPrimary = false;
            device.IsApproved = false;
        }

        alert.Status = StatusRejected;
        alert.HandledBy = adminId;
        alert.HandledAt = DateTime.Now;

        await _db.SaveChangesAsync();
        return true;
    }

    /// <summary>نشانه‌گذاری دستی هشدار به‌عنوان رسیدگی‌شده (بدون تغییر وضعیت دستگاه).</summary>
    public async Task<bool> DismissAlertAsync(int alertId, int adminId)
    {
        var alert = await _db.AttendanceAlerts.FirstOrDefaultAsync(a => a.Id == alertId && a.Status == StatusPending);
        if (alert == null) return false;
        alert.Status = StatusRejected;
        alert.HandledBy = adminId;
        alert.HandledAt = DateTime.Now;
        await _db.SaveChangesAsync();
        return true;
    }

    // ====================================================================
    //  محدوده‌ی مکانی مجاز
    // ====================================================================

    public async Task<AttendanceAreaSetting?> GetAreaAsync() =>
        await _db.AttendanceAreaSettings.OrderByDescending(s => s.UpdatedAt).FirstOrDefaultAsync();

    public async Task<AttendanceAreaSetting> SaveAreaAsync(double lat, double lng, double radiusMeters, string? name, int? by)
    {
        var current = await _db.AttendanceAreaSettings.OrderByDescending(s => s.UpdatedAt).FirstOrDefaultAsync();
        if (current == null)
        {
            current = new AttendanceAreaSetting();
            _db.AttendanceAreaSettings.Add(current);
        }
        current.Latitude = lat;
        current.Longitude = lng;
        current.RadiusMeters = Math.Max(50, radiusMeters);
        current.LocationName = name;
        current.UpdatedAt = DateTime.Now;
        current.UpdatedBy = by;
        await _db.SaveChangesAsync();
        return current;
    }

    // ====================================================================
    //  ابزارها
    // ====================================================================

    /// <summary>فاصله دو نقطه جغرافیایی به متر (فرمول Haversine).</summary>
    public static double HaversineMeters(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371000;
        double dLat = (lat2 - lat1) * Math.PI / 180;
        double dLon = (lon2 - lon1) * Math.PI / 180;
        double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                   Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180) *
                   Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * R * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }

    private static string FaDigits(double d) =>
        string.Join("", Math.Round(d).ToString("N0").Select(ch => ch switch
        {
            '0' => '۰', '1' => '۱', '2' => '۲', '3' => '۳', '4' => '۴',
            '5' => '۵', '6' => '۶', '7' => '۷', '8' => '۸', '9' => '۹',
            _ => ch
        }));
}
