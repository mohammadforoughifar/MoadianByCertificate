using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services;

/// <summary>
/// موتور محاسبه‌ی امتیاز سلامت سیستم (0 تا 100) بر اساس قطعات ثبت‌شده در شناسنامه‌ی سیستم.
/// وزن‌بندی: فضای درایوها ۳۰ + حافظه رم ۲۰ + نوع ذخیره‌سازی ۱۵ + پردازنده ۱۵ + تازه‌بودن اطلاعات ۱۰ + وضعیت ثبت ۱۰.
/// </summary>
public static class SystemHealth
{
    /// <summary>نمره‌ی یک دسته + توضیح فارسی (برای نمایش شکست نمره در UI).</summary>
    public class CategoryScore
    {
        public string Key { get; set; } = "";
        public string Fa { get; set; } = "";
        public string Icon { get; set; } = "";
        public int Score { get; set; }
        public int Max { get; set; }
        public string Note { get; set; } = "";
    }

    /// <summary>گزارش کامل سلامت یک سیستم.</summary>
    public class HealthReport
    {
        public int Score { get; set; }
        public string Grade { get; set; } = "";      // excellent | good | average | attention | critical
        public string GradeFa { get; set; } = "";
        public string Color { get; set; } = "";
        public List<CategoryScore> Categories { get; set; } = new();
    }

    /// <summary>ورودی‌های لازم برای محاسبه (از جدول‌های قطعات یا فیلدهای تخت).</summary>
    public class HwInput
    {
        public int RamGb;
        public int CpuCores;
        public int SsdCount;
        public int HddCount;
        public double MaxVolumeUsedPct;  // بیشترین درصد مصرف در میان درایوها
        public bool HasVolumes;
        /// <summary>بدترین وضعیت S.M.A.R.T در بین هاردها: ok | warn | fail | none</summary>
        public string SmartWorst = "none";
        public DateTime? ReceivedAt;
        public bool IsApproved;
        public bool HasUser;
        public bool HasDetails;          // جزئیات ساختاریافته دارد (ایجنت جدید)
    }

    /// <summary>تشخیص SSD از نام مدل و نوع رابط (NVMe/SSD/M.2/...).</summary>
    public static bool IsSsd(string? model, string? iface)
    {
        var m = ((model ?? "") + " " + (iface ?? "")).ToUpperInvariant();
        if (m.Contains("NVME") || m.Contains("M.2") || m.Contains("SSD") || m.Contains("SOLID STATE"))
            return true;
        // مدل‌های رایج SSD که «SSD» در نامشان نیست
        return m.Contains("860 EVO") || m.Contains("870 EVO") || m.Contains("970 EVO") || m.Contains("980 PRO")
            || m.Contains("A400") || m.Contains("A4000") || m.Contains("SV300") || m.Contains("SN750")
            || m.Contains("SN770") || m.Contains("PX512") || m.Contains("PM9") || m.Contains("PT9")
            || m.Contains("990 PRO") || m.Contains("KC3000");
    }

    /// <summary>درجه‌بندی کلیدی + فارسی + رنگ (هم در API و هم در کلاینت تکیه می‌کند).</summary>
    public static (string Grade, string Fa, string Color) GradeInfo(int score) => score switch
    {
        >= 90 => ("excellent", "عالی", "#0ea5a4"),
        >= 75 => ("good", "خوب", "#2ec47e"),
        >= 60 => ("average", "متوسط", "#f4a23c"),
        >= 40 => ("attention", "نیازمند توجه", "#ff7043"),
        _ => ("critical", "بحرانی", "#e0245e")
    };

    public static HealthReport Compute(HwInput h)
    {
        var cats = new List<CategoryScore>();

        // ۱) فضای درایوها + S.M.A.R.T — ۳۰ امتیاز (بر اساس بدترین درایو)
        int storageScore; string storageNote;
        if (!h.HasVolumes)
        {
            storageScore = 15; storageNote = "درایویی گزارش نشده";
        }
        else
        {
            var p = h.MaxVolumeUsedPct;
            storageScore = p < 50 ? 30 : p < 70 ? 24 : p < 85 ? 15 : p < 92 ? 8 : 2;
            storageNote = $"بیشترین مصرف {p:0}٪";
        }
        // ضریب سلامت دیسک (S.M.A.R.T): خرابی پیش‌بینی‌شده امتیاز را به شدت می‌کاهد
        if (h.SmartWorst == "fail")
        {
            storageScore = Math.Min(storageScore, 3);
            storageNote += " — S.M.A.R.T: خرابی پیش‌بینی‌شده ⚠";
        }
        else if (h.SmartWorst == "warn")
        {
            storageScore = Math.Min(storageScore, 12);
            storageNote += " — S.M.A.R.T: تضعیف‌شده";
        }
        cats.Add(new CategoryScore { Key = "storage", Fa = "فضای درایوها و S.M.A.R.T", Icon = "bi-hdd-fill", Score = storageScore, Max = 30, Note = storageNote });

        // ۲) حافظه رم — ۲۰ امتیاز
        var ramScore = h.RamGb switch { >= 32 => 20, >= 16 => 17, >= 8 => 14, >= 4 => 9, > 0 => 5, _ => 2 };
        cats.Add(new CategoryScore { Key = "ram", Fa = "حافظه رم", Icon = "bi-memory", Score = ramScore, Max = 20, Note = h.RamGb > 0 ? $"{h.RamGb} GB" : "ثبت نشده" });

        // ۳) نوع ذخیره‌سازی — ۱۵ امتیاز
        int mediaScore; string mediaNote;
        if (h.SsdCount + h.HddCount == 0) { mediaScore = 7; mediaNote = "هاردی ثبت نشده"; }
        else if (h.HddCount == 0) { mediaScore = 15; mediaNote = "همه SSD"; }
        else if (h.SsdCount > 0) { mediaScore = 10; mediaNote = "ترکیب SSD + HDD"; }
        else { mediaScore = 5; mediaNote = "فقط HDD"; }
        cats.Add(new CategoryScore { Key = "media", Fa = "نوع ذخیره‌سازی", Icon = "bi-device-ssd-fill", Score = mediaScore, Max = 15, Note = mediaNote });

        // ۴) پردازنده — ۱۵ امتیاز (تعداد هسته)
        var cpuScore = h.CpuCores switch { >= 16 => 15, >= 8 => 13, >= 6 => 11, >= 4 => 9, >= 2 => 6, _ => 3 };
        cats.Add(new CategoryScore { Key = "cpu", Fa = "پردازنده", Icon = "bi-cpu-fill", Score = cpuScore, Max = 15, Note = h.CpuCores > 0 ? $"{h.CpuCores} هسته" : "ثبت نشده" });

        // ۵) تازه‌بودن اطلاعات — ۱۰ امتیاز (چند روز پیش آخرین گزارش ایجنت)
        int freshScore; string freshNote;
        if (h.ReceivedAt == null)
        {
            freshScore = 5; freshNote = "—";
        }
        else
        {
            var days = (DateTime.Now - h.ReceivedAt.Value).TotalDays;
            freshScore = days <= 3 ? 10 : days <= 14 ? 8 : days <= 30 ? 5 : days <= 90 ? 3 : 1;
            freshNote = days < 1 ? "همین امروز" : $"{days:0} روز پیش";
        }
        cats.Add(new CategoryScore { Key = "fresh", Fa = "تازه‌بودن اطلاعات", Icon = "bi-clock-history", Score = freshScore, Max = 10, Note = freshNote });

        // ۶) وضعیت ثبت — ۱۰ امتیاز
        var statusScore = h.IsApproved && h.HasUser ? 10 : h.IsApproved ? 8 : h.HasDetails ? 4 : 0;
        var statusNote = h.IsApproved ? (h.HasUser ? "تأییدشده و اختصاص‌یافته" : "تأییدشده — کاربر اختصاص نیافته") : "در انتظار تایید";
        cats.Add(new CategoryScore { Key = "status", Fa = "وضعیت ثبت", Icon = "bi-shield-check", Score = statusScore, Max = 10, Note = statusNote });

        var score = cats.Sum(c => c.Score);
        var (grade, gradeFa, color) = GradeInfo(score);
        return new HealthReport { Score = score, Grade = grade, GradeFa = gradeFa, Color = color, Categories = cats };
    }

    /// <summary>ساخت ورودی محاسبه از جداول قطعات (با fallback به فیلدهای تخت).</summary>
    public static HwInput BuildInput(SystemInfo s, List<SystemDisk> disks, List<SystemCpu> cpus, List<SystemRam> rams, List<SystemVolume> vols)
    {
        double maxPct = 0; bool hasVols = false;
        foreach (var v in vols)
        {
            if (v.TotalGb > 0)
            {
                hasVols = true;
                var p = v.UsedGb * 100.0 / v.TotalGb;
                if (p > maxPct) maxPct = p;
            }
        }
        var ramSum = rams.Sum(r => r.CapacityGb);

        // بدترین وضعیت S.M.A.R.T بین هاردها
        var smartWorst = "none";
        foreach (var d in disks)
        {
            var st = (d.SmartStatus ?? "").Trim();
            if (st.Length == 0) continue;
            if (st is "PredFail" or "Failed") { smartWorst = "fail"; break; }
            if (st == "Degraded" && smartWorst != "fail") smartWorst = "warn";
            if (smartWorst == "none") smartWorst = "ok";
        }

        return new HwInput
        {
            RamGb = ramSum > 0 ? ramSum : s.TotalRamGb,
            CpuCores = cpus.Sum(c => c.Cores),
            SsdCount = disks.Count(d => IsSsd(d.Model, d.Interface)),
            HddCount = disks.Count(d => !IsSsd(d.Model, d.Interface)),
            MaxVolumeUsedPct = Math.Min(100, Math.Round(maxPct)),
            HasVolumes = hasVols,
            SmartWorst = smartWorst,
            ReceivedAt = s.ReceivedAt > new DateTime(2000, 1, 1) ? s.ReceivedAt : null,
            IsApproved = s.IsApproved,
            HasUser = s.UserId > 0,
            HasDetails = !string.IsNullOrWhiteSpace(s.DetailsJson)
        };
    }

    /// <summary>محاسبه سلامت یک سیستم.</summary>
    public static async Task<HealthReport?> ComputeAsync(AppDbContext db, int systemInfoId)
    {
        var s = await db.SystemInfos.AsNoTracking().FirstOrDefaultAsync(x => x.Id == systemInfoId);
        if (s == null) return null;
        var disks = await db.SystemDisks.AsNoTracking().Where(x => x.SystemInfoId == systemInfoId).ToListAsync();
        var cpus = await db.SystemCpus.AsNoTracking().Where(x => x.SystemInfoId == systemInfoId).ToListAsync();
        var rams = await db.SystemRams.AsNoTracking().Where(x => x.SystemInfoId == systemInfoId).ToListAsync();
        var vols = await db.SystemVolumes.AsNoTracking().Where(x => x.SystemInfoId == systemInfoId).ToListAsync();
        return Compute(BuildInput(s, disks, cpus, rams, vols));
    }

    /// <summary>محاسبه گروهی (برای لیست و داشبورد) — با چند کوئری واحد.</summary>
    public static async Task<Dictionary<int, HealthReport>> ComputeManyAsync(AppDbContext db, IEnumerable<int> ids)
    {
        var idList = ids.Distinct().ToList();
        var result = new Dictionary<int, HealthReport>();
        if (idList.Count == 0) return result;

        var systems = await db.SystemInfos.AsNoTracking().Where(x => idList.Contains(x.Id)).ToListAsync();
        var disks = await db.SystemDisks.AsNoTracking().Where(x => idList.Contains(x.SystemInfoId)).ToListAsync();
        var cpus = await db.SystemCpus.AsNoTracking().Where(x => idList.Contains(x.SystemInfoId)).ToListAsync();
        var rams = await db.SystemRams.AsNoTracking().Where(x => idList.Contains(x.SystemInfoId)).ToListAsync();
        var vols = await db.SystemVolumes.AsNoTracking().Where(x => idList.Contains(x.SystemInfoId)).ToListAsync();

        foreach (var s in systems)
        {
            result[s.Id] = Compute(BuildInput(s,
                disks.Where(d => d.SystemInfoId == s.Id).ToList(),
                cpus.Where(c => c.SystemInfoId == s.Id).ToList(),
                rams.Where(r => r.SystemInfoId == s.Id).ToList(),
                vols.Where(v => v.SystemInfoId == s.Id).ToList()));
        }
        return result;
    }
}
