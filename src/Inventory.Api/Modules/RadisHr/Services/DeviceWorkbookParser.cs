using ClosedXML.Excel;
using RadisHr.Shared.Calculations;
using RadisHr.Shared.Models;

namespace RadisHr.Api.Services;

/// <summary>
/// خوانندهٔ فایل اکسل دستگاه حضور و غیاب — پورت parseBlockReport() و parseFlat()
/// از assets/attendance-engine.js. اندیس ستون‌ها دقیقاً مطابق نسخهٔ اصلی است
/// (اندیس صفر‌مبنای SheetJS = شمارهٔ ستون ClosedXML منهای یک).
/// </summary>
public class DeviceWorkbookParser
{
    private readonly List<Employee> _employees;

    public DeviceWorkbookParser(List<Employee> employees) => _employees = employees;

    public List<AttendanceDay> Parse(Stream stream)
    {
        using var workbook = new XLWorkbook(stream);
        var result = new List<AttendanceDay>();

        foreach (var sheet in workbook.Worksheets)
        {
            var grid = ToGrid(sheet);
            var block = ParseBlockReport(grid);
            if (block.Count > 0) { result.AddRange(block); continue; }

            var flat = ParseFlat(grid);
            if (flat.Count > 0) result.AddRange(flat);
        }

        // حذف تکراری‌ها بر اساس کلید code|date (اولین رکورد برنده است — سیاست append-only)
        return result
            .GroupBy(d => d.Key)
            .Select(g => g.First())
            .ToList();
    }

    // ───────── ابزار ─────────
    private static string[][] ToGrid(IXLWorksheet sheet)
    {
        var used = sheet.RangeUsed();
        if (used == null) return Array.Empty<string[]>();

        var lastRow = used.LastRow().RowNumber();
        var lastColumn = Math.Max(used.LastColumn().ColumnNumber(), 25);
        var grid = new string[lastRow][];

        for (var r = 1; r <= lastRow; r++)
        {
            var row = new string[lastColumn];
            for (var c = 1; c <= lastColumn; c++)
                row[c - 1] = sheet.Cell(r, c).GetFormattedString() ?? "";
            grid[r - 1] = row;
        }
        return grid;
    }

    private static string Cell(string[] row, int index) =>
        index >= 0 && index < row.Length ? row[index] ?? "" : "";

    private static string Norm(string? value) => AttendanceEngine.Norm(value);
    private static string Latin(string? value) => PersianCalendarUtil.Digits(value);
    private static string JDate(string? value) => PersianCalendarUtil.JDate(value);
    private static int Mins(string? value) => AttendanceEngine.Mins(value);

    private Employee? FindEmployee(string deviceCode, string deviceName)
    {
        var key = AttendanceEngine.NameKey(deviceName);
        if (!string.IsNullOrEmpty(key))
        {
            var byName = _employees.FirstOrDefault(x => AttendanceEngine.NameKey($"{x.First} {x.Last}") == key);
            if (byName != null) return byName;
        }
        return _employees.FirstOrDefault(x => x.Code == deviceCode);
    }

    // ───────── قالب بلوکی دستگاه ─────────
    private List<AttendanceDay> ParseBlockReport(string[][] rows)
    {
        var output = new List<AttendanceDay>();

        for (var r = 0; r < rows.Length; r++)
        {
            if (Norm(Cell(rows[r], 24)) != "کد پرسنلی :") continue;

            var deviceCode = Latin(Cell(rows[r], 21)).Trim();
            var deviceName = Norm(Cell(rows[r], 13));
            var employee = FindEmployee(deviceCode, deviceName);
            var code = employee?.Code ?? deviceCode;
            var name = employee != null ? $"{employee.First} {employee.Last}" : deviceName;

            for (var q = r + 4; q < Math.Min(r + 40, rows.Length); q++)
            {
                var date = JDate(Cell(rows[q], 24));
                if (string.IsNullOrEmpty(date)) continue;

                var day = AttendanceEngine.EmptyDay(code, name, date);
                day.DeviceCode = deviceCode;
                day.First = Latin(Cell(rows[q], 17)).Trim();
                day.Last = Latin(Cell(rows[q], 15)).Trim();
                day.Presence = Mins(Cell(rows[q], 12));
                day.Mission = Mins(Cell(rows[q], 4));
                day.Leave = Mins(Cell(rows[q], 5));
                day.Shortfall = Mins(Cell(rows[q], 6)) + Mins(Cell(rows[q], 7))
                              + Mins(Cell(rows[q], 8)) + Mins(Cell(rows[q], 9));
                day.UnauthorizedOt = Mins(Cell(rows[q], 10));
                day.Ot = Mins(Cell(rows[q], 11));
                day.Status = employee != null ? "تطبیق قطعی" : "نیازمند تطبیق";
                day.Month = PersianCalendarUtil.MonthOf(date);
                output.Add(day);
            }
        }
        return output;
    }

    // ───────── قالب فهرست تردد (سطر به سطر) ─────────
    private static readonly string[] CodeHeaders = { "کد پرسنلی", "کد", "EmployeeCode", "PersonnelCode", "شماره پرسنلی" };
    private static readonly string[] NameHeaders = { "نام و نام خانوادگی", "نام", "Name", "EmployeeName" };
    private static readonly string[] DateHeaders = { "تاریخ", "Date", "تاریخ تردد" };
    private static readonly string[] TimeHeaders = { "ساعت", "زمان", "Time", "ساعت تردد" };
    private static readonly string[] TypeHeaders = { "نوع", "Type", "نوع تردد", "وضعیت" };

    private static string HeaderKey(string? value) =>
        new string(Norm(value).Where(c => !" _.-/".Contains(c)).ToArray()).ToLowerInvariant();

    private List<AttendanceDay> ParseFlat(string[][] rows)
    {
        if (rows.Length < 2) return new List<AttendanceDay>();

        var header = rows[0].Select(HeaderKey).ToArray();
        int IndexOf(string[] names)
        {
            var wanted = names.Select(HeaderKey).ToHashSet();
            for (var i = 0; i < header.Length; i++)
                if (wanted.Contains(header[i])) return i;
            return -1;
        }

        var codeIndex = IndexOf(CodeHeaders);
        var nameIndex = IndexOf(NameHeaders);
        var dateIndex = IndexOf(DateHeaders);
        var timeIndex = IndexOf(TimeHeaders);
        var typeIndex = IndexOf(TypeHeaders);
        if (dateIndex < 0 || timeIndex < 0) return new List<AttendanceDay>();

        var events = new List<(string Code, string Name, string DeviceCode, string Date, string Time, string Status)>();

        foreach (var row in rows.Skip(1))
        {
            var deviceCode = Latin(Cell(row, codeIndex)).Trim();
            var deviceName = Norm(Cell(row, nameIndex));
            var date = JDate(Cell(row, dateIndex));
            var time = Latin(Cell(row, timeIndex)).Trim();

            if (string.IsNullOrEmpty(date) || AttendanceEngine.Clock(time) == null) continue;
            if (string.IsNullOrEmpty(deviceCode) && string.IsNullOrEmpty(deviceName)) continue;

            var employee = FindEmployee(deviceCode, deviceName);
            events.Add((
                employee?.Code ?? deviceCode,
                employee != null ? $"{employee.First} {employee.Last}" : deviceName,
                deviceCode, date, time,
                employee != null ? "تطبیق قطعی" : "نیازمند تطبیق"));
        }

        return events
            .GroupBy(e => $"{e.Code}|{e.Date}")
            .Select(group =>
            {
                var list = group.OrderBy(e => AttendanceEngine.Clock(e.Time) ?? 0).ToList();
                var day = AttendanceEngine.EmptyDay(list[0].Code, list[0].Name, list[0].Date);
                day.DeviceCode = list[0].DeviceCode;
                day.First = list[0].Time;
                day.Last = list[^1].Time;
                day.Status = list[0].Status;
                day.Source = "raw";
                day.Month = PersianCalendarUtil.MonthOf(list[0].Date);

                if (list.Count >= 2)
                {
                    var first = AttendanceEngine.Clock(day.First) ?? 0;
                    var last = AttendanceEngine.Clock(day.Last) ?? 0;
                    day.Presence = Math.Max(0, last - first);
                }
                return day;
            })
            .ToList();
    }
}
