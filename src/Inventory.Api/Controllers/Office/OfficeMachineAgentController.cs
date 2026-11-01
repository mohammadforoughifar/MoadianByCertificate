using Inventory.Api.Services.Office;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers;

/// <summary>
/// ورودی ایجنت سیستم‌ها برای پایش چاپگرهای USB/محلی (دستور «InventoryAgent printers»).
/// مثل api/SystemInfo بدون JWT است و فقط با کلید مشترک ایجنت (هدر X-Agent-Key — اگر Agent:Key تنظیم شده باشد) محافظت می‌شود.
/// </summary>
[ApiController]
[Route("api/OfficeMachines/agent")]
public class OfficeMachineAgentController : ControllerBase
{
    private readonly OfficeMachineMonitor _monitor;
    private readonly IConfiguration _cfg;
    private readonly ILogger<OfficeMachineAgentController> _log;

    public OfficeMachineAgentController(OfficeMachineMonitor monitor, IConfiguration cfg, ILogger<OfficeMachineAgentController> log)
    {
        _monitor = monitor;
        _cfg = cfg;
        _log = log;
    }

    private bool AgentKeyOk()
    {
        // مقدار خالی در appsettings نباید متغیر محیطی AGENT_KEY را بی‌اثر کند
        var expected = (string.IsNullOrWhiteSpace(_cfg["Agent:Key"]) ? Environment.GetEnvironmentVariable("AGENT_KEY") ?? "" : _cfg["Agent:Key"]!).Trim();
        if (expected.Length == 0) return true;
        var got = (Request.Headers["X-Agent-Key"].FirstOrDefault() ?? "").Trim();
        return got.Length == expected.Length && string.Equals(got, expected, StringComparison.Ordinal);
    }

    /// <summary>⚠️ باید [AllowAnonymous] بماند؛ وگرنه DefaultAuthorizeConvention آن را پشت JWT می‌برد و ایجنت‌ها 401 می‌گیرند.</summary>
    [AllowAnonymous]
    [HttpPost("printers")]
    [RequestSizeLimit(512 * 1024)]
    public async Task<IActionResult> Printers([FromBody] AgentPrinterReportDto report, CancellationToken ct)
    {
        if (!AgentKeyOk()) return Unauthorized(new { message = "کلید ایجنت نامعتبر است (هدر X-Agent-Key)." });
        if (string.IsNullOrWhiteSpace(report?.AgentId)) return BadRequest(new { message = "AgentId الزامی است." });
        try
        {
            var r = await _monitor.IngestAgentReportAsync(report, ct);
            return Ok(new { ok = true, r.Printers, r.LinkedMachines, r.AutoLinked, r.SystemLabel });
        }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning(ex, "پردازش گزارش چاپگرهای ایجنت {Agent} ناموفق بود", report.AgentId);
            return StatusCode(500, new { message = "پردازش گزارش چاپگرها ناموفق بود." });
        }
    }
}
