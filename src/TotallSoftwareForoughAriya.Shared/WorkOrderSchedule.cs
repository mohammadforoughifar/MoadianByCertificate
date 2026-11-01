using System.Globalization;

namespace Inventory.Shared;

/// <summary>Finite Jalali schedule, including the first occurrence and preserving its time.</summary>
public static class WorkOrderSchedule
{
    // Values match the persisted WorkOrderRecurrence constants.
    public static IReadOnlyList<DateTime> Dates(DateTime first, int recurrence)
    {
        if (recurrence is < 0 or > 3) throw new ArgumentOutOfRangeException(nameof(recurrence));
        if (recurrence == 0) return new[] { first };
        var pc = new PersianCalendar();
        var year = pc.GetYear(first);
        var month = pc.GetMonth(first);
        var day = pc.GetDayOfMonth(first);
        DateTime At(int m, int d) => DateTime.SpecifyKind(
            pc.ToDateTime(year, m, d, 0, 0, 0, 0).Add(first.TimeOfDay), first.Kind);
        var last = recurrence == 1
            ? At(month, pc.GetDaysInMonth(year, month))
            : At(12, pc.GetDaysInMonth(year, 12));
        var dates = new List<DateTime>();
        if (recurrence == 3)
        {
            // Anchor to the original Jalali day; short months use their last day.
            for (var m = month; m <= 12; m++)
                dates.Add(At(m, Math.Min(day, pc.GetDaysInMonth(year, m))));
        }
        else
        {
            var step = recurrence == 1 ? 1 : 7;
            for (var date = first; date <= last; date = date.AddDays(step)) dates.Add(date);
        }
        return dates;
    }
}
