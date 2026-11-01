using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Drawing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Inventory.Api.Services;

/// <summary>ساخت PDF شناسنامه‌ی سیستم (با فونت فارسی وزیرمتن).</summary>
public static class SystemInfoPdf
{
    private static bool _fontsRegistered;

    static SystemInfoPdf()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    private static string FontDir => Path.Combine(AppContext.BaseDirectory, "Resources", "fonts");

    private static void EnsureFonts()
    {
        if (_fontsRegistered) return;
        var reg = Path.Combine(FontDir, "Vazirmatn-Regular.ttf");
        var bold = Path.Combine(FontDir, "Vazirmatn-Bold.ttf");
        if (File.Exists(reg)) FontManager.RegisterFontWithCustomName("Vazirmatn", File.OpenRead(reg));
        if (File.Exists(bold)) FontManager.RegisterFontWithCustomName("Vazirmatn-Bold", File.OpenRead(bold));
        _fontsRegistered = true;
    }

    public static async Task<byte[]?> GenerateAsync(AppDbContext db, int systemInfoId)
    {
        var s = await db.SystemInfos.AsNoTracking().FirstOrDefaultAsync(x => x.Id == systemInfoId);
        if (s == null) return null;

        var boards = await db.SystemBoards.AsNoTracking().Where(x => x.SystemInfoId == s.Id).ToListAsync();
        var cpus = await db.SystemCpus.AsNoTracking().Where(x => x.SystemInfoId == s.Id).ToListAsync();
        var rams = await db.SystemRams.AsNoTracking().Where(x => x.SystemInfoId == s.Id).ToListAsync();
        var disks = await db.SystemDisks.AsNoTracking().Where(x => x.SystemInfoId == s.Id).ToListAsync();
        var gpus = await db.SystemGpus.AsNoTracking().Where(x => x.SystemInfoId == s.Id).ToListAsync();
        var monitors = await db.SystemMonitors.AsNoTracking().Where(x => x.SystemInfoId == s.Id).ToListAsync();
        var nets = await db.SystemNetAdapters.AsNoTracking().Where(x => x.SystemInfoId == s.Id).ToListAsync();
        var vols = await db.SystemVolumes.AsNoTracking().Where(x => x.SystemInfoId == s.Id).ToListAsync();

        var health = await SystemHealth.ComputeAsync(db, s.Id);
        var user = s.UserId.HasValue ? await db.SystemUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Id == s.UserId) : null;
        var company = s.CompanyId.HasValue ? await db.SystemCompanies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == s.CompanyId) : null;
        var department = s.DepartmentId.HasValue ? await db.SystemDepartments.AsNoTracking().FirstOrDefaultAsync(c => c.Id == s.DepartmentId) : null;
        var itCount = await db.ItRequests.AsNoTracking().CountAsync(x => x.SystemInfoId == s.Id);
        var handoverCount = await db.SystemHandovers.AsNoTracking().CountAsync(x => x.SystemInfoId == s.Id);
        var userHistory = await db.SystemInfoUserHistories.AsNoTracking()
            .Where(h => h.SystemInfoId == s.Id).OrderByDescending(h => h.FromAt).Take(5).ToListAsync();

        string Fa(string en) => en switch
        {
            "Healthy" => "سالم",
            "Degraded" => "تضعیف‌شده",
            "PredFail" => "خرابی پیش‌بینی‌شده",
            "Failed" => "خراب",
            "OK" => "سالم",
            _ => en
        };

        string Cpu = cpus.Count > 0 ? string.Join(" + ", cpus.Select(c => $"{c.Name} ({c.Cores}C/{c.Threads}T)")) : (s.Cpu ?? "—");
        string Ram = rams.Count > 0 ? $"{rams.Sum(r => r.CapacityGb)} GB — {rams.Count} ماژول" : (s.Ram ?? "—");
        string Board = boards.FirstOrDefault() is { } b ? $"{b.Board} — {b.ComputerModel}" : (s.Motherboard ?? "—");
        string Disks = disks.Count > 0
            ? string.Join(" + ", disks.Select(d => $"{d.Model} ({d.SizeGb}GB) — S.M.A.R.T: {Fa(d.SmartStatus ?? "Unknown")}"))
            : (s.HardDisk ?? "—");
        string Gpus = gpus.Count > 0 ? string.Join(" + ", gpus.Select(g => g.Name)) : (s.Graphics ?? "—");
        string Mons = monitors.Count > 0 ? string.Join(" + ", monitors.Select(m => m.Name)) : (s.Monitor ?? "—");
        string Nets = nets.Count > 0
            ? string.Join(" + ", nets.Where(n => !string.IsNullOrEmpty(n.Ipv4)).Select(n => $"{n.Name} — {n.Ipv4}"))
            : "—";
        string Drives = vols.Count > 0
            ? string.Join("، ", vols.Select(v => $"{v.Letter}: {v.UsedGb}/{v.TotalGb} GB"))
            : "—";

        EnsureFonts();

        var doc = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(32);
                page.DefaultTextStyle(x => x.FontFamily("Vazirmatn").FontSize(10));

                page.Header().Column(col =>
                {
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().AlignRight().Text("شناسنامه سیستم").FontFamily("Vazirmatn-Bold").FontSize(20).FontColor("#4b2fb8");
                        row.ConstantItem(200, Unit.Point).AlignLeft().Text("نرم افزار یکپارچه فروغ آریا").FontSize(9).FontColor("#666");
                    });
                    col.Item().PaddingVertical(6).LineHorizontal(1.2f).LineColor("#8e5cff");
                    col.Item().PaddingTop(4).Row(r2 =>
                    {
                        r2.RelativeItem().AlignRight().Text($"ایجنت: {s.AgentId}    |    شناسه: #{s.Id}").FontSize(9).FontColor("#555");
                        r2.ConstantItem(210, Unit.Point).AlignLeft().Text($"تاریخ صدور: {DateTime.Now:yyyy/MM/dd HH:mm}").FontSize(8).FontColor("#999");
                    });
                });

                page.Content().Column(col =>
                {
                    // ---------- بلوک وضعیت ----------
                    col.Item().PaddingBottom(10).Table(t =>
                    {
                        t.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(1);
                            c.RelativeColumn(1);
                            c.RelativeColumn(1);
                            c.RelativeColumn(1);
                        });
                        void Cell(string title, string value, string? color = null)
                        {
                            t.Cell().Border(0.6f).BorderColor("#d8d4ee").Background("#f7f5ff").Padding(8).Column(cc =>
                            {
                                cc.Item().Text(title).FontSize(8).FontColor("#777");
                                cc.Item().PaddingTop(2).Text(value).FontSize(9).FontFamily("Vazirmatn-Bold")
                                    .FontColor(color ?? "#1c2130");
                            });
                        }
                        Cell("وضعیت", s.IsApproved ? "تأیید شده" : "در انتظار تایید", s.IsApproved ? "#0e8a55" : "#b06a00");
                        Cell("امتیاز سلامت", $"{health?.Score} از 100 — {health?.GradeFa}", health?.Color);
                        Cell("کاربر اختصاص‌یافته", user != null ? $"{user.FirstName} {user.LastName}" : "—");
                        Cell("سیستم‌عامل", s.OsName ?? "—");
                        Cell("کمپانی", company?.Name ?? "—");
                        Cell("واحد", department?.Name ?? "—");
                        Cell("آدرس IP", Nets);
                        Cell("آخرین گزارش ایجنت", $"{s.ReceivedAt:yyyy/MM/dd HH:mm}");
                    });

                    // ---------- سخت‌افزار ----------
                    col.Item().PaddingVertical(6).Text("سخت‌افزار").FontFamily("Vazirmatn-Bold").FontSize(13).FontColor("#4b2fb8");
                    col.Item().Table(t =>
                    {
                        t.ColumnsDefinition(c =>
                        {
                            c.ConstantColumn(110);
                            c.RelativeColumn(1);
                        });
                        void Row2(string k, string v)
                        {
                            t.Cell().Border(0.5f).BorderColor("#e3e0f2").Background("#faf9ff").Padding(6).Text(k).FontSize(9).FontFamily("Vazirmatn-Bold").FontColor("#555");
                            t.Cell().Border(0.5f).BorderColor("#e3e0f2").Padding(6).Text(v).FontSize(9);
                        }
                        Row2("مادربرد / مدل", Board);
                        Row2("پردازنده", Cpu);
                        Row2("حافظه رم", Ram);
                        Row2("هارد دیسک‌ها", Disks);
                        Row2("گرافیک", Gpus);
                        Row2("مانیتور", Mons);
                        Row2("شبکه", Nets);
                        Row2("درایوها", Drives);
                    });

                    // ---------- شکست امتیاز سلامت ----------
                    if (health != null)
                    {
                        col.Item().PaddingVertical(6).Text("جزئیات امتیاز سلامت").FontFamily("Vazirmatn-Bold").FontSize(13).FontColor("#4b2fb8");
                        col.Item().Table(t =>
                        {
                            t.ColumnsDefinition(c =>
                            {
                                c.ConstantColumn(140);
                                c.RelativeColumn(1);
                                c.ConstantColumn(70);
                            });
                            foreach (var cat in health.Categories)
                            {
                                t.Cell().Border(0.5f).BorderColor("#e3e0f2").Padding(5).Text(cat.Fa).FontSize(9);
                                t.Cell().Border(0.5f).BorderColor("#e3e0f2").Padding(5).Column(cb =>
                                {
                                    var pct = cat.Max > 0 ? (float)cat.Score / cat.Max : 0f;
                                    cb.Item().Row(rr =>
                                    {
                                        rr.RelativeItem(pct).Background(HealthBarColor(cat.Score, cat.Max)).Height(8);
                                        rr.RelativeItem().Background("#eef0f7").Height(8);
                                    });
                                    cb.Item().PaddingTop(3).Text(cat.Note).FontSize(8).FontColor("#888");
                                });
                                t.Cell().Border(0.5f).BorderColor("#e3e0f2").Padding(5).Text($"{cat.Score}/{cat.Max}").FontSize(9).FontFamily("Vazirmatn-Bold");
                            }
                        });
                    }

                    // ---------- آمار و تاریخچه ----------
                    col.Item().PaddingVertical(6).Text("آمار و سوابق").FontFamily("Vazirmatn-Bold").FontSize(13).FontColor("#4b2fb8");
                    col.Item().Row(r =>
                    {
                        r.ConstantItem(160).Text($"درخواست‌های IT: {itCount}").FontSize(9);
                        r.ConstantItem(170).Text($"تحویل‌های دیجیتال: {handoverCount}").FontSize(9);
                    });
                    if (userHistory.Count > 0)
                    {
                        col.Item().PaddingTop(4).Text("کاربران قبلی سیستم:").FontSize(9).FontColor("#777");
                        foreach (var h in userHistory)
                        {
                            col.Item().PaddingRight(14).Text(
                                $"• {h.UserName} — {h.FromAt:yyyy/MM/dd} تا {(h.ToAt?.ToString("yyyy/MM/dd") ?? "حال")}").FontSize(8).FontColor("#666");
                        }
                    }
                });

                page.Footer().AlignCenter().Text(t =>
                {
                    t.DefaultTextStyle(x => x.FontSize(8).FontColor("#999"));
                    t.Span("این شناسنامه توسط سامانه انبار و فروش «فروغ آریا» صادر شده است — ");
                    t.Span(DateTime.Now.ToString("yyyy/MM/dd HH:mm")).FontSize(7);
                });
            });
        });

        return doc.GeneratePdf();
    }

    private static string HealthBarColor(int score, int max)
    {
        var pct = max > 0 ? score * 100.0 / max : 0;
        return pct >= 75 ? "#2ec47e" : pct >= 50 ? "#f4a23c" : "#e0245e";
    }
}
