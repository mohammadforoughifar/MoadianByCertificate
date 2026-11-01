using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Inventory.Api.Services.DevTeam;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace Inventory.Api.Controllers.DevTeam;

/// <summary>
/// وب‌هوک گیت برای میز کار توسعه.
/// GitHub: POST /api/dev-team/hooks/github  + هدر X-Hub-Signature-256 یا X-DevTeam-Token
/// GitLab: POST /api/dev-team/hooks/gitlab  + هدر X-Gitlab-Token یا X-DevTeam-Token
///
/// در پیام کامیت بنویسید:
///   DT-1405-0001   یا  #DT-1405-0001  یا  task:12
///   mod:Office     (اختیاری — کلید ماژول)
/// </summary>
[ApiController]
[Route("api/dev-team/hooks")]
[AllowAnonymous]
public class DevTeamGitWebhookController : ControllerBase
{
    private readonly IDevTeamService _svc;
    private readonly IConfiguration _config;
    private readonly ILogger<DevTeamGitWebhookController> _log;

    public DevTeamGitWebhookController(IDevTeamService svc, IConfiguration config, ILogger<DevTeamGitWebhookController> log)
    {
        _svc = svc;
        _config = config;
        _log = log;
    }

    private string? Secret =>
        _config["DevTeam:GitWebhookSecret"]
        ?? _config["DevTeam__GitWebhookSecret"]
        ?? Environment.GetEnvironmentVariable("DEVTEAM_GIT_WEBHOOK_SECRET");

    private bool IsEnabled =>
        string.Equals(_config["DevTeam:GitWebhookEnabled"] ?? "true", "true", StringComparison.OrdinalIgnoreCase);

    private IActionResult? Guard(string? gitlabToken, string? hubSignature256, string rawBody)
    {
        if (!IsEnabled)
            return StatusCode(503, new { message = "وب‌هوک DevTeam غیرفعال است." });

        var secret = Secret;
        if (string.IsNullOrWhiteSpace(secret))
            return StatusCode(503, new { message = "DevTeam:GitWebhookSecret در تنظیمات تعریف نشده است." });

        // 1) توکن ساده مشترک
        if (Request.Headers.TryGetValue("X-DevTeam-Token", out var simple) &&
            FixedEqual(simple.ToString(), secret))
            return null;

        // 2) GitLab token
        if (!string.IsNullOrEmpty(gitlabToken) && FixedEqual(gitlabToken, secret))
            return null;

        // 3) GitHub HMAC SHA-256: sha256=hex
        if (!string.IsNullOrEmpty(hubSignature256) && hubSignature256.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase))
        {
            var their = hubSignature256["sha256=".Length..].Trim();
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(rawBody ?? ""));
            var mine = Convert.ToHexString(hash).ToLowerInvariant();
            if (FixedEqual(mine, their.ToLowerInvariant()))
                return null;
        }

        _log.LogWarning("DevTeam webhook: unauthorized from {IP}", HttpContext.Connection.RemoteIpAddress);
        return Unauthorized(new { message = "توکن/امضای وب‌هوک نامعتبر است." });
    }

    private static bool FixedEqual(string a, string b)
    {
        var ba = Encoding.UTF8.GetBytes(a ?? "");
        var bb = Encoding.UTF8.GetBytes(b ?? "");
        if (ba.Length != bb.Length) return false;
        return CryptographicOperations.FixedTimeEquals(ba, bb);
    }

    [HttpPost("github")]
    public async Task<IActionResult> GitHub()
    {
        using var reader = new StreamReader(Request.Body, Encoding.UTF8);
        var raw = await reader.ReadToEndAsync();

        var sig = Request.Headers.TryGetValue("X-Hub-Signature-256", out var s) ? s.ToString() : null;
        var deny = Guard(null, sig, raw);
        if (deny != null) return deny;

        var ghEvent = Request.Headers.TryGetValue("X-GitHub-Event", out var ev) ? ev.ToString() : "push";
        if (string.Equals(ghEvent, "ping", StringComparison.OrdinalIgnoreCase))
            return Ok(new DtGitWebhookResultDto { Ok = true, Provider = "GitHub", Messages = { "pong" } });
        if (!string.Equals(ghEvent, "push", StringComparison.OrdinalIgnoreCase))
            return Ok(new DtGitWebhookResultDto { Ok = true, Provider = "GitHub", Messages = { $"رویداد {ghEvent} نادیده گرفته شد." } });

        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(raw) ? "{}" : raw);
        var result = await _svc.ProcessGitHubPushAsync(doc.RootElement);
        return Ok(result);
    }

    [HttpPost("gitlab")]
    public async Task<IActionResult> GitLab()
    {
        using var reader = new StreamReader(Request.Body, Encoding.UTF8);
        var raw = await reader.ReadToEndAsync();

        var token = Request.Headers.TryGetValue("X-Gitlab-Token", out var t) ? t.ToString() : null;
        var deny = Guard(token, null, raw);
        if (deny != null) return deny;

        if (Request.Headers.TryGetValue("X-Gitlab-Event", out var ev) &&
            !string.IsNullOrEmpty(ev.ToString()) &&
            !ev.ToString().Contains("Push", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(ev.ToString(), "Push Hook", StringComparison.OrdinalIgnoreCase))
        {
            return Ok(new DtGitWebhookResultDto { Ok = true, Provider = "GitLab", Messages = { $"رویداد {ev} نادیده گرفته شد." } });
        }

        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(raw) ? "{}" : raw);
        var result = await _svc.ProcessGitLabPushAsync(doc.RootElement);
        return Ok(result);
    }

    /// <summary>
    /// رویداد CI عمومی (GitHub Actions / GitLab CI / generic).
    /// body: DtCiBuildEventDto — فقط failure → Problem
    /// </summary>
    [HttpPost("ci")]
    public async Task<IActionResult> CiGeneric([FromBody] DtCiBuildEventDto dto)
    {
        var deny = Guard(
            Request.Headers.TryGetValue("X-Gitlab-Token", out var gt) ? gt.ToString() : null,
            Request.Headers.TryGetValue("X-Hub-Signature-256", out var hs) ? hs.ToString() : null,
            "");
        // also accept simple token without body HMAC
        if (deny != null)
        {
            // re-check simple token only
            var secret = Secret;
            if (string.IsNullOrWhiteSpace(secret) ||
                !Request.Headers.TryGetValue("X-DevTeam-Token", out var tok) ||
                !FixedEqual(tok.ToString(), secret))
                return deny;
        }
        if (!IsEnabled)
            return StatusCode(503, new { message = "وب‌هوک DevTeam غیرفعال است." });
        if (string.IsNullOrWhiteSpace(Secret))
            return StatusCode(503, new { message = "DevTeam:GitWebhookSecret در تنظیمات تعریف نشده است." });

        dto ??= new DtCiBuildEventDto();
        if (string.IsNullOrWhiteSpace(dto.Provider)) dto.Provider = "generic";
        var result = await _svc.ProcessCiEventAsync(dto);
        return Ok(result);
    }

    /// <summary>GitHub Actions workflow_run / check_suite (failure → Problem).</summary>
    [HttpPost("github-ci")]
    public async Task<IActionResult> GitHubCi()
    {
        using var reader = new StreamReader(Request.Body, Encoding.UTF8);
        var raw = await reader.ReadToEndAsync();
        var sig = Request.Headers.TryGetValue("X-Hub-Signature-256", out var s) ? s.ToString() : null;
        var deny = Guard(null, sig, raw);
        if (deny != null) return deny;

        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(raw) ? "{}" : raw);
        var root = doc.RootElement;
        var ev = Request.Headers.TryGetValue("X-GitHub-Event", out var e) ? e.ToString() : "";

        var dto = new DtCiBuildEventDto { Provider = "github-actions" };
        if (root.TryGetProperty("repository", out var repo) && repo.TryGetProperty("full_name", out var fn))
            dto.Repo = fn.GetString();

        if (string.Equals(ev, "workflow_run", StringComparison.OrdinalIgnoreCase) &&
            root.TryGetProperty("workflow_run", out var wr))
        {
            dto.PipelineId = wr.TryGetProperty("id", out var id) ? id.ToString() : null;
            dto.JobName = wr.TryGetProperty("name", out var nm) ? nm.GetString() : null;
            dto.Status = wr.TryGetProperty("conclusion", out var c) ? c.GetString() ?? "failure"
                : wr.TryGetProperty("status", out var st) ? st.GetString() ?? "failure" : "failure";
            dto.Conclusion = dto.Status;
            dto.Branch = wr.TryGetProperty("head_branch", out var br) ? br.GetString() : null;
            dto.CommitSha = wr.TryGetProperty("head_sha", out var sha) ? sha.GetString() : null;
            dto.Url = wr.TryGetProperty("html_url", out var url) ? url.GetString() : null;
            if (wr.TryGetProperty("display_title", out var dt)) dto.CommitMessage = dt.GetString();
            if (wr.TryGetProperty("head_commit", out var hc) && hc.ValueKind == JsonValueKind.Object &&
                hc.TryGetProperty("message", out var msg))
                dto.CommitMessage = msg.GetString();
        }
        else if (string.Equals(ev, "check_suite", StringComparison.OrdinalIgnoreCase) &&
                 root.TryGetProperty("check_suite", out var cs))
        {
            dto.PipelineId = cs.TryGetProperty("id", out var id) ? id.ToString() : null;
            dto.Status = cs.TryGetProperty("conclusion", out var c) ? c.GetString() ?? "failure" : "failure";
            dto.Conclusion = dto.Status;
            dto.Branch = cs.TryGetProperty("head_branch", out var br) ? br.GetString() : null;
            dto.CommitSha = cs.TryGetProperty("head_sha", out var sha) ? sha.GetString() : null;
            dto.JobName = "check_suite";
        }
        else if (string.Equals(ev, "ping", StringComparison.OrdinalIgnoreCase))
        {
            return Ok(new DtGitWebhookResultDto { Ok = true, Provider = "github-actions", Messages = { "pong" } });
        }
        else
        {
            return Ok(new DtGitWebhookResultDto
            {
                Ok = true, Provider = "github-actions",
                Messages = { $"رویداد {ev} نادیده گرفته شد (workflow_run/check_suite)." }
            });
        }

        var result = await _svc.ProcessCiEventAsync(dto);
        return Ok(result);
    }

    /// <summary>GitLab Pipeline Hook (failed → Problem).</summary>
    [HttpPost("gitlab-ci")]
    public async Task<IActionResult> GitLabCi()
    {
        using var reader = new StreamReader(Request.Body, Encoding.UTF8);
        var raw = await reader.ReadToEndAsync();
        var token = Request.Headers.TryGetValue("X-Gitlab-Token", out var t) ? t.ToString() : null;
        var deny = Guard(token, null, raw);
        if (deny != null) return deny;

        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(raw) ? "{}" : raw);
        var root = doc.RootElement;
        var dto = new DtCiBuildEventDto { Provider = "gitlab-ci" };

        if (root.TryGetProperty("project", out var proj) && proj.TryGetProperty("path_with_namespace", out var path))
            dto.Repo = path.GetString();
        if (root.TryGetProperty("object_attributes", out var oa))
        {
            dto.PipelineId = oa.TryGetProperty("id", out var id) ? id.ToString() : null;
            dto.Status = oa.TryGetProperty("status", out var st) ? st.GetString() ?? "failed" : "failed";
            dto.Branch = oa.TryGetProperty("ref", out var r) ? r.GetString() : null;
            dto.Url = oa.TryGetProperty("url", out var u) ? u.GetString() : null;
        }
        if (root.TryGetProperty("commit", out var commit))
        {
            dto.CommitSha = commit.TryGetProperty("id", out var sha) ? sha.GetString() : null;
            dto.CommitMessage = commit.TryGetProperty("message", out var msg) ? msg.GetString() : null;
        }
        if (root.TryGetProperty("builds", out var builds) && builds.ValueKind == JsonValueKind.Array)
        {
            foreach (var b in builds.EnumerateArray())
            {
                var st = b.TryGetProperty("status", out var s) ? s.GetString() : "";
                if (string.Equals(st, "failed", StringComparison.OrdinalIgnoreCase))
                {
                    dto.JobName = b.TryGetProperty("name", out var n) ? n.GetString() : dto.JobName;
                    break;
                }
            }
        }

        var result = await _svc.ProcessCiEventAsync(dto);
        return Ok(result);
    }

    /// <summary>تست سلامت وب‌هوک (نیاز به توکن).</summary>
    [HttpGet("health")]
    public IActionResult Health()
    {
        var hasSecret = !string.IsNullOrWhiteSpace(Secret);
        return Ok(new
        {
            enabled = IsEnabled,
            secretConfigured = hasSecret,
            endpoints = new[]
            {
                "POST /api/dev-team/hooks/github",
                "POST /api/dev-team/hooks/gitlab",
                "POST /api/dev-team/hooks/ci",
                "POST /api/dev-team/hooks/github-ci",
                "POST /api/dev-team/hooks/gitlab-ci"
            },
            commitHints = new[]
            {
                "DT-1405-0001 or #DT-1405-0001 or task:12",
                "mod:Office (optional module key)"
            }
        });
    }
}
