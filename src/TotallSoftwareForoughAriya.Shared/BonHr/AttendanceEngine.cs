using System.Text.RegularExpressions;
using Inventory.Shared.BonHr;

namespace Inventory.Shared.BonHr;

/// <summary>برنامهٔ کاری مؤثر یک روز — خروجی scheduleForEmployee</summary>
public class EffectiveSchedule
{
    public string WorkStart { get; set; } = "08:00";
    public string WorkEnd { get; set; } = "16:00";
    public int EntryGrace { get; set; } = 5;
    public int EarlyEntryGrace { get; set; }
    public int ExitGrace { get; set; } = 5;
    public int LateExitGrace { get; set; }
    public int MaxLateWithoutLeave { get; set; } = 30;
    public int MaxEarlyWithoutLeave { get; set; } = 30;
    public string AbsenceStrategy { get; set; } = "leaveThenDeduction";
    public bool IsRest { get; set; }
    public bool Overnight { get; set; }
    public string ShiftType { get; set; } = "";
}

/// <summary>نتیجهٔ چرخهٔ نگهبانی — خروجی RADIS_GUARD.cycleFor</summary>
public record GuardShiftInfo(int Index, string Type, string Start, string End);

/// <summary>
/// موتور حضور و غیاب — پورت مو به موی assets/attendance-engine.js و assets/guard-shifts.js
/// همان قواعد، همان ترتیب، همان گرد کردن.
/// </summary>
public static class AttendanceEngine
{
    // ───────── کمکی‌ها (پورت mins / clock / hm) ─────────

    /// <summary>mins: «۲:۳۰» → ۱۵۰ دقیقه</summary>
    public static int Mins(string? value)
    {
        var v = PersianCalendarUtil.Digits(value);
        var m = Regex.Match(v, @"(\d+)\s*[:٫]\s*(\d+)");
        return m.Success ? int.Parse(m.Groups[1].Value) * 60 + int.Parse(m.Groups[2].Value) : 0;
    }

    /// <summary>clock: «08:15» → ۴۹۵ ، در صورت نامعتبر بودن null</summary>
    public static int? Clock(string? value)
    {
        var v = PersianCalendarUtil.Digits(value);
        var m = Regex.Match(v, @"(\d{1,2}):(\d{1,2})");
        return m.Success ? int.Parse(m.Groups[1].Value) * 60 + int.Parse(m.Groups[2].Value) : null;
    }

    /// <summary>hm: ۱۵۰ → «2:30»</summary>
    public static string Hm(int minutes)
    {
        var v = Math.Max(0, minutes);
        return $"{v / 60}:{v % 60:00}";
    }

    public static string Hm(double minutes)
    {
        var v = Math.Max(0d, minutes);
        return $"{(int)Math.Floor(v / 60)}:{(int)Math.Round(v) % 60:00}";
    }

    /// <summary>نرمال‌سازی نام برای تطبیق — پورت nameKey</summary>
    public static string NameKey(string? value) =>
        Regex.Replace(Norm(value), @"\s", "");

    public static string Norm(string? value) =>
        Regex.Replace(PersianCalendarUtil.Digits(value).Replace("\u200c", " "), @"\s+", " ").Trim();

    // ───────── چرخهٔ نگهبانی (guard-shifts.js: cycleFor) ─────────
    public static GuardShiftInfo? CycleFor(GuardCycle? cycle, string date)
    {
        if (cycle == null) return null;
        var target = PersianCalendarUtil.JalaliDayNumber(date);
        var start = PersianCalendarUtil.JalaliDayNumber(cycle.StartDate);
        if (target == null || start == null || target < start) return null;

        var index = (target.Value - start.Value) % 6;
        var types = cycle.StartShift == "night"
            ? new[] { "شب", "شب", "صبح", "صبح", "استراحت", "استراحت" }
            : new[] { "صبح", "صبح", "شب", "شب", "استراحت", "استراحت" };
        var type = types[index];
        var s = type == "صبح" ? "06:00" : type == "شب" ? "18:00" : "";
        var e = type == "صبح" ? "18:00" : type == "شب" ? "06:00 روز بعد" : "";
        return new GuardShiftInfo(index + 1, type, s, e);
    }

    // ───────── برنامهٔ کاری مؤثر (attendance-engine.js: scheduleForEmployee) ─────────
    public static EffectiveSchedule ScheduleForEmployee(Employee? employee, UnitSchedule? unitSchedule, GuardShiftInfo? guardShift)
    {
        var defaults = new EffectiveSchedule();

        if (guardShift != null)
        {
            if (guardShift.Type == "استراحت")
                return new EffectiveSchedule
                {
                    WorkStart = "", WorkEnd = "", IsRest = true, ShiftType = "استراحت",
                    EntryGrace = defaults.EntryGrace, ExitGrace = defaults.ExitGrace,
                    MaxLateWithoutLeave = defaults.MaxLateWithoutLeave,
                    MaxEarlyWithoutLeave = defaults.MaxEarlyWithoutLeave,
                    AbsenceStrategy = defaults.AbsenceStrategy
                };

            return new EffectiveSchedule
            {
                WorkStart = guardShift.Start,
                WorkEnd = guardShift.Type == "شب" ? "06:00" : guardShift.End,
                Overnight = guardShift.Type == "شب",
                ShiftType = guardShift.Type,
                EntryGrace = defaults.EntryGrace, ExitGrace = defaults.ExitGrace,
                MaxLateWithoutLeave = defaults.MaxLateWithoutLeave,
                MaxEarlyWithoutLeave = defaults.MaxEarlyWithoutLeave,
                AbsenceStrategy = defaults.AbsenceStrategy
            };
        }

        if (unitSchedule == null) return defaults;

        return new EffectiveSchedule
        {
            WorkStart = unitSchedule.WorkStart,
            WorkEnd = unitSchedule.WorkEnd,
            EntryGrace = unitSchedule.EntryGrace,
            EarlyEntryGrace = unitSchedule.EarlyEntryGrace,
            ExitGrace = unitSchedule.ExitGrace,
            LateExitGrace = unitSchedule.LateExitGrace,
            MaxLateWithoutLeave = unitSchedule.MaxLateWithoutLeave,
            MaxEarlyWithoutLeave = unitSchedule.MaxEarlyWithoutLeave,
            AbsenceStrategy = unitSchedule.AbsenceStrategy
        };
    }

    /// <summary>applyRawRules — محاسبهٔ تأخیر/تعجیل و اضافه‌کار غیرمجاز از روی ترددهای خام</summary>
    public static void ApplyRawRules(AttendanceDay d, EffectiveSchedule s)
    {
        if (d.Source != "raw" || string.IsNullOrEmpty(d.First) || string.IsNullOrEmpty(d.Last)) return;

        if (s.IsRest)
        {
            d.Shortfall = 0;
            d.UnauthorizedOt = d.Presence;
            d.Ot = 0;
            d.Status = "حضور در استراحت؛ منتظر تصمیم اداری";
            return;
        }

        var start = Clock(s.WorkStart);
        var end = Clock(s.WorkEnd);
        var first = Clock(d.First);
        var last = Clock(d.Last);
        if (start == null || end == null || first == null || last == null) return;

        int st = start.Value, en = end.Value, fi = first.Value, la = last.Value;
        if (s.Overnight)
        {
            en += 1440;
            if (fi < st) fi += 1440;
            if (la < st) la += 1440;
        }

        var late = Math.Max(0, fi - st - s.EntryGrace);
        var early = Math.Max(0, en - la - s.ExitGrace);
        d.Shortfall = late + early;

        var extra = Math.Max(0, st - fi - s.EarlyEntryGrace) + Math.Max(0, la - en - s.LateExitGrace);
        d.UnauthorizedOt = extra;
        d.Ot = 0;
    }

    /// <summary>manualMinutes — دقیقهٔ مرخصی/مأموریت دستی</summary>
    public static int ManualMinutes(LeaveMission record, EffectiveSchedule s)
    {
        if (record.Type.Contains("روزانه"))
        {
            if (s.IsRest) return 0;
            var end = Clock(s.WorkEnd);
            var start = Clock(s.WorkStart);
            if (end == null || start == null) return 0;
            var e = end.Value;
            if (s.Overnight) e += 1440;
            return Math.Max(0, e - start.Value);
        }
        return Math.Max(0, (Clock(record.To) ?? 0) - (Clock(record.From) ?? 0));
    }

    public static AttendanceDay EmptyDay(string code, string name, string date) => new()
    {
        Key = $"{code}|{date}",
        Code = code,
        Name = name,
        Date = date,
        Month = PersianCalendarUtil.MonthOf(date),
        Source = "device"
    };
}
