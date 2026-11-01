using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Inventory.Api.Data;
using Inventory.Api.Services;

namespace Inventory.Api.Controllers;

/// <summary>
/// ثبت‌نام دوربین‌های مداربسته + ورود گروهی از اکسل + دانلود قالب اکسل.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class CctvCamerasController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly Hubs.DashboardBroadcaster _dash;
    private readonly FileStore _store;

    public CctvCamerasController(AppDbContext db, Hubs.DashboardBroadcaster dash, FileStore store)
    {
        _db = db;
        _dash = dash;
        _store = store;
    }

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string? q, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        var list = _db.CctvCameras.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
        {
            q = q.Trim();
            list = list.Where(c =>
                c.Model.Contains(q) || c.SerialNumber.Contains(q) ||
                (c.Ip != null && c.Ip.Contains(q)) ||
                (c.Mac != null && c.Mac.Contains(q)) ||
                (c.Location != null && c.Location.Contains(q)));
        }
        return Ok(await Paging.ResultAsync(list.OrderByDescending(c => c.Id), skip, take));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(int id)
    {
        var item = await _db.CctvCameras.FirstOrDefaultAsync(c => c.Id == id);
        return item == null ? NotFound() : Ok(item);
    }

    [HttpPost]
    public async Task<IActionResult> Post([FromBody] CctvCamera c)
    {
        if (string.IsNullOrWhiteSpace(c.Model) || string.IsNullOrWhiteSpace(c.SerialNumber))
            return BadRequest(new { message = "مدل و شماره سریال الزامی است." });
        if (await _db.CctvCameras.AnyAsync(x => x.SerialNumber == c.SerialNumber))
            return BadRequest(new { message = $"دوربینی با سریال «{c.SerialNumber}» قبلاً ثبت شده است." });
        _db.CctvCameras.Add(c);
        await _db.SaveChangesAsync();
        _ = _dash.BroadcastAsync();
        return Ok(new { id = c.Id });
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Put(int id, [FromBody] CctvCamera c)
    {
        var item = await _db.CctvCameras.FirstOrDefaultAsync(x => x.Id == id);
        if (item == null) return NotFound();
        if (await _db.CctvCameras.AnyAsync(x => x.SerialNumber == c.SerialNumber && x.Id != id))
            return BadRequest(new { message = $"سریال «{c.SerialNumber}» برای دوربین دیگری ثبت شده است." });
        item.Model = c.Model;
        item.SerialNumber = c.SerialNumber;
        item.Ip = c.Ip;
        item.Mac = c.Mac;
        item.Location = c.Location;
        item.Notes = c.Notes;
        item.IsActive = c.IsActive;
        await _db.SaveChangesAsync();
        _ = _dash.BroadcastAsync();
        return Ok();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var item = await _db.CctvCameras.FirstOrDefaultAsync(x => x.Id == id);
        if (item == null) return NotFound();
        _db.CctvCameras.Remove(item);
        await _db.SaveChangesAsync();
        _ = _dash.BroadcastAsync();
        return Ok();
    }

    // ================= دانلود قالب اکسل =================

    [HttpGet("import-template")]
    public IActionResult Template()
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("دوربین‌ها");
        var headers = new[] { "مدل", "شماره سریال", "آی‌پی", "مک آدرس", "محل استقرار", "توضیحات", "فعال" };
        for (var i = 0; i < headers.Length; i++)
        {
            ws.Cell(1, i + 1).Value = headers[i];
            ws.Cell(1, i + 1).Style.Font.Bold = true;
            ws.Cell(1, i + 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#EFEAFF");
        }
        // یک ردیف نمونه
        ws.Cell(2, 1).Value = "Hikvision DS-2CD2143G2-I";
        ws.Cell(2, 2).Value = "C123456789-01";
        ws.Cell(2, 3).Value = "192.168.1.101";
        ws.Cell(2, 4).Value = "C0:56:E3:AA:BB:01";
        ws.Cell(2, 5).Value = "انبار مرکزی — سردر شرقی";
        ws.Cell(2, 6).Value = "دوربین بولت ۴ مگاپیکسل";
        ws.Cell(2, 7).Value = "بله";
        ws.Columns().AdjustToContents();
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return File(ms.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "CctvCameras-Template.xlsx");
    }

    // ================= خروجی اکسل =================

    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] string? q)
    {
        var list = _db.CctvCameras.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
        {
            q = q.Trim();
            list = list.Where(c =>
                c.Model.Contains(q) || c.SerialNumber.Contains(q) ||
                (c.Ip != null && c.Ip.Contains(q)) ||
                (c.Mac != null && c.Mac.Contains(q)) ||
                (c.Location != null && c.Location.Contains(q)));
        }
        var items = await list.OrderBy(c => c.Id).ToListAsync();
        var nvrs = await _db.CctvNvrs.AsNoTracking().ToListAsync();

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("دوربین‌ها");
        var headers = new[] { "#", "مدل", "شماره سریال", "آی‌پی", "مک آدرس", "محل استقرار", "NVR", "توضیحات", "وضعیت", "تاریخ ثبت" };
        for (var i = 0; i < headers.Length; i++)
        {
            ws.Cell(1, i + 1).Value = headers[i];
            ws.Cell(1, i + 1).Style.Font.Bold = true;
            ws.Cell(1, i + 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#EFEAFF");
        }
        var r = 2;
        foreach (var c in items)
        {
            ws.Cell(r, 1).Value = c.Id;
            ws.Cell(r, 2).Value = c.Model;
            ws.Cell(r, 3).Value = c.SerialNumber;
            ws.Cell(r, 4).Value = c.Ip ?? "";
            ws.Cell(r, 5).Value = c.Mac ?? "";
            ws.Cell(r, 6).Value = c.Location ?? "";
            ws.Cell(r, 7).Value = nvrs.FirstOrDefault(n => n.Id == c.NvrId) is { } nv ? $"{nv.Model} ({nv.SerialNumber})" : "";
            ws.Cell(r, 8).Value = c.Notes ?? "";
            ws.Cell(r, 9).Value = c.IsActive ? "فعال" : "غیرفعال";
            ws.Cell(r, 10).Value = c.CreatedAt.ToString("yyyy/MM/dd HH:mm");
            r++;
        }
        ws.Columns().AdjustToContents();
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return File(ms.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"CctvCameras-{DateTime.Now:yyyyMMdd-HHmm}.xlsx");
    }

    // ================= ورود گروهی از اکسل =================

    public class ImportResult
    {
        public int Inserted { get; set; }
        public int Skipped { get; set; }
        public List<string> Errors { get; set; } = new();
    }

    [HttpPost("import-excel")]
    [DisableRequestSizeLimit]
    public async Task<IActionResult> ImportExcel(IFormFile? file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { message = "فایلی ارسال نشد." });

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        ms.Position = 0;

        // ذخیره‌سازی فایل اکسل ورودی در wwwroot/uploads/cctv/imports/
        await _store.SaveAsync("cctv", 0, ms, file.FileName, "imports");
        ms.Position = 0;

        ImportResult result = new();
        try
        {
            using var wb = new XLWorkbook(ms);
            var ws = wb.Worksheets.First();

            // نقشه‌ی ستون‌ها از روی سطر عنوان
            var colMap = new Dictionary<string, int>();
            var headerRow = ws.FirstRowUsed();
            if (headerRow != null)
            {
                foreach (var cell in headerRow.CellsUsed())
                {
                    var h = (cell.GetString() ?? "").Trim().Replace("ي", "ی").Replace("ك", "ک");
                    if (h.Contains("مدل")) colMap["model"] = cell.Address.ColumnNumber;
                    else if (h.Contains("سریال")) colMap["serial"] = cell.Address.ColumnNumber;
                    else if (h.Contains("آی") || h.Contains("ایپی") || h.ToLower().Contains("ip")) colMap["ip"] = cell.Address.ColumnNumber;
                    else if (h.Contains("مک") || h.ToLower().Contains("mac")) colMap["mac"] = cell.Address.ColumnNumber;
                    else if (h.Contains("محل")) colMap["location"] = cell.Address.ColumnNumber;
                    else if (h.Contains("توضیح")) colMap["notes"] = cell.Address.ColumnNumber;
                    else if (h.Contains("فعال")) colMap["active"] = cell.Address.ColumnNumber;
                    else if (h.Contains("nvr") || h.Contains("ان وی آر")) colMap["nvr"] = cell.Address.ColumnNumber;
                }
            }

            if (!colMap.ContainsKey("model") || !colMap.ContainsKey("serial"))
                return BadRequest(new { message = "ستون‌های «مدل» و «شماره سریال» در فایل پیدا نشد. از قالب آماده استفاده کنید." });

            string? CellVal(IXLRow row, string key)
            {
                if (!colMap.TryGetValue(key, out var col)) return null;
                var v = row.Cell(col).GetString()?.Trim();
                return string.IsNullOrEmpty(v) ? null : v;
            }

            var existingSerials = (await _db.CctvCameras.Select(c => c.SerialNumber).ToListAsync()).ToHashSet();
            var seenInFile = new HashSet<string>();
            var nvrList = await _db.CctvNvrs.AsNoTracking().ToListAsync();
            int? MatchNvr(string? val)
            {
                if (string.IsNullOrWhiteSpace(val)) return null;
                var v = val.Trim();
                var nv = nvrList.FirstOrDefault(x => x.SerialNumber == v)
                      ?? nvrList.FirstOrDefault(x => x.Model.Contains(v, StringComparison.OrdinalIgnoreCase)
                                                   || v.Contains(x.Model, StringComparison.OrdinalIgnoreCase));
                return nv?.Id;
            }

            foreach (var row in ws.RowsUsed().Skip(1))
            {
                var rowNum = row.RowNumber();
                try
                {
                    var model = CellVal(row, "model");
                    var serial = CellVal(row, "serial");

                    if (model == null && serial == null) { result.Skipped++; continue; } // ردیف خالی
                    if (model == null || serial == null)
                    {
                        result.Errors.Add($"سطر {rowNum}: مدل یا سریال خالی است.");
                        result.Skipped++;
                        continue;
                    }
                    if (existingSerials.Contains(serial) || !seenInFile.Add(serial))
                    {
                        result.Skipped++;
                        continue; // تکراری
                    }

                    var activeStr = CellVal(row, "active");
                    var isActive = activeStr == null ||
                        activeStr.Contains("بله") || activeStr.Contains("1") || activeStr.Contains("true") || activeString(activeStr);

                    _db.CctvCameras.Add(new CctvCamera
                    {
                        Model = model,
                        SerialNumber = serial,
                        Ip = CellVal(row, "ip"),
                        Mac = CellVal(row, "mac"),
                        NvrId = MatchNvr(CellVal(row, "nvr")),
                        Location = CellVal(row, "location"),
                        Notes = CellVal(row, "notes"),
                        IsActive = isActive,
                        CreatedAt = DateTime.Now
                    });
                    existingSerials.Add(serial);
                    result.Inserted++;
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"سطر {rowNum}: {ex.Message}");
                    result.Skipped++;
                }
            }

            await _db.SaveChangesAsync();
        _ = _dash.BroadcastAsync();
            return Ok(result);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = "خواندن فایل اکسل ناموفق بود: " + ex.Message });
        }
    }

    private static bool activeString(string? s)
        => bool.TryParse(s, out var b) && b;
}
