using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services.ItAssets;

/// <summary>تبدیل دادهٔ ساخت‌یافتهٔ Redfish به تاریخچهٔ پایدار برنامه، بدون ذخیرهٔ پاسخ خام یا اعتبارنامه.</summary>
public sealed record ServerHardwareRecordResult(IReadOnlyList<RedfishHardwareEvent> NewEvents);

public static class ServerHardwareRecorder
{
    public static async Task<ServerHardwareRecordResult> RecordAsync(AppDbContext db, ManagedServer server, RedfishSnapshot snapshot, DateTime capturedAt,
        CancellationToken ct = default)
    {
        foreach (var component in snapshot.Components
                     .Where(c => !string.IsNullOrWhiteSpace(c.Type) && !string.IsNullOrWhiteSpace(c.Key))
                     .GroupBy(c => $"{c.Type}\u001f{c.Key}", StringComparer.OrdinalIgnoreCase)
                     .Select(g => g.First()).Take(128))
        {
            db.ServerHardwareComponentSnapshots.Add(new ServerHardwareComponentSnapshot
            {
                ManagedServerId = server.Id, CapturedAt = capturedAt,
                ComponentType = Limit(component.Type, 40) ?? "Unknown", ComponentKey = Limit(component.Key, 200) ?? "Unknown",
                Name = Limit(component.Name, 200) ?? component.Type, Health = Limit(component.Health, 20) ?? "Unknown",
                State = Limit(component.State, 40), Reading = Limit(component.Reading, 100), Model = Limit(component.Model, 150),
                SerialNumber = Limit(component.SerialNumber, 100), Message = Limit(component.Message, 500)
            });
        }

        var incoming = snapshot.Events.Where(e => !string.IsNullOrWhiteSpace(e.Key) && !string.IsNullOrWhiteSpace(e.Message))
            .GroupBy(e => e.Key, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).Take(30).ToList();
        if (incoming.Count == 0) return new ServerHardwareRecordResult(Array.Empty<RedfishHardwareEvent>());
        var keys = incoming.Select(e => Limit(e.Key, 200)!).ToList();
        var newEvents = new List<RedfishHardwareEvent>();
        var existing = await db.ServerHardwareEvents.Where(e => e.ManagedServerId == server.Id && keys.Contains(e.EventKey))
            .ToDictionaryAsync(e => e.EventKey, StringComparer.OrdinalIgnoreCase, ct);
        foreach (var item in incoming)
        {
            var key = Limit(item.Key, 200)!;
            if (existing.TryGetValue(key, out var row))
            {
                row.Severity = Limit(item.Severity, 30) ?? "Unknown";
                row.Message = Limit(item.Message, 2000) ?? "";
                row.Origin = Limit(item.Origin, 500);
                row.OccurredAt = item.OccurredAt ?? row.OccurredAt;
                row.LastSeenAt = capturedAt;
            }
            else
            {
                newEvents.Add(item);
                db.ServerHardwareEvents.Add(new ServerHardwareEvent
                {
                    ManagedServerId = server.Id, EventKey = key, Severity = Limit(item.Severity, 30) ?? "Unknown",
                    Message = Limit(item.Message, 2000) ?? "", Origin = Limit(item.Origin, 500), OccurredAt = item.OccurredAt,
                    FirstSeenAt = capturedAt, LastSeenAt = capturedAt
                });
            }
        }
        return new ServerHardwareRecordResult(newEvents);
    }

    /// <summary>پیام هشدار قابل‌نمایش از قطعات مشکل‌دار؛ قطعهٔ Critical بر سلامت کلی اولویت دارد.</summary>
    public static (string? Severity, string? Message) DescribeHealthIssue(RedfishSnapshot snapshot)
    {
        var problemComponents = snapshot.Components
            .Where(c => IsProblem(c.Health))
            .OrderByDescending(c => IsCritical(c.Health))
            .Take(4).ToList();
        if (problemComponents.Count > 0)
        {
            var critical = problemComponents.Any(c => IsCritical(c.Health));
            var details = string.Join(" | ", problemComponents.Select(c =>
                $"{TypeFa(c.Type)} «{Limit(c.Name, 80) ?? c.Type}»: {c.Health}{(string.IsNullOrWhiteSpace(c.Reading) ? "" : $" ({c.Reading})")}"));
            return (critical ? "Critical" : "Warning", "وضعیت قطعات Redfish: " + details);
        }

        if (string.Equals(snapshot.Health, "OK", StringComparison.OrdinalIgnoreCase)) return (null, null);
        var severity = IsCritical(snapshot.Health) ? "Critical" : "Warning";
        return (severity, $"وضعیت سلامت سخت‌افزار Redfish: {snapshot.Health}");
    }

    public static (string? Severity, string? Message) DescribeImportantEvents(IEnumerable<RedfishHardwareEvent> events)
    {
        var important = events.Where(e => IsProblem(e.Severity)).OrderByDescending(e => IsCritical(e.Severity)).Take(3).ToList();
        if (important.Count == 0) return (null, null);
        var severity = important.Any(e => IsCritical(e.Severity)) ? "Critical" : "Warning";
        var text = string.Join(" | ", important.Select(e => Limit(e.Message, 300) ?? ""));
        return (severity, "رخداد جدید iLO/Redfish: " + text);
    }

    public static bool IsProblem(string? health) => string.Equals(health, "Warning", StringComparison.OrdinalIgnoreCase)
        || string.Equals(health, "Critical", StringComparison.OrdinalIgnoreCase);
    public static bool IsCritical(string? health) => string.Equals(health, "Critical", StringComparison.OrdinalIgnoreCase);

    private static string TypeFa(string? type) => type switch
    {
        "PowerSupply" => "پاور", "Fan" => "فن", "Temperature" => "دما", "Memory" => "حافظه",
        "Processor" => "پردازنده", "Storage" => "کنترلر ذخیره‌سازی", "Disk" => "دیسک", "Chassis" => "شاسی", _ => "قطعه"
    };

    private static string? Limit(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Trim();
        return value.Length <= max ? value : value[..max];
    }
}
