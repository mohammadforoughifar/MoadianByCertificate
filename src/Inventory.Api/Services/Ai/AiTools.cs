using System.Text.Json;
using System.Globalization;
using Inventory.Api.Data;
using Inventory.Api.Services.FaAtt;
using Inventory.Api.Services.FaPay;
using Inventory.Api.Services.Invoicing;
using Inventory.Api.Services.Treasury;
using Inventory.Shared;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Inventory.Api.Services.Ai;

// =====================================================================
// ابزارهای دستیار فروغ آریا — دست‌های اجرایی مدل زبانی.
//
// اصل امنیتی: هر ابزار فقط از سرویس‌های موجود سامانه با شناسه همان کاربر
// استفاده می‌کند؛ یعنی سطح دسترسی ابزار دقیقاً همان چیزی است که کاربر در
// خود سامانه دارد (مثلاً letter_detail برای نامه‌ای که کاربر به آن دسترسی
// ندارد null می‌دهد و ابزار خطای دسترسی برمی‌گرداند).
// =====================================================================

public class AiToolContext
{
    public int UserId { get; set; }
    public string UserName { get; set; } = "";
    public bool IsAdmin { get; set; }
    public IServiceProvider Services { get; set; } = null!;
    public CancellationToken CancellationToken { get; set; }
}

public interface IAiTool
{
    string Name { get; }
    string Description { get; }
    object ParametersSchema { get; }
    Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx);
}

public class AiToolRegistry
{
    private readonly Dictionary<string, IAiTool> _tools;

    public AiToolRegistry(IEnumerable<IAiTool> tools) =>
        _tools = tools.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);

    public List<AiToolSchema> Schemas() => _tools.Values
        .Select(t => new AiToolSchema { Name = t.Name, Description = t.Description, Parameters = t.ParametersSchema })
        .ToList();

    public async Task<string> ExecuteAsync(string name, string argumentsJson, AiToolContext ctx)
    {
        if (!_tools.TryGetValue(name, out var tool))
            return JsonSerializer.Serialize(new { error = "unknown_tool", tool = name });
        try
        {
            using var doc = string.IsNullOrWhiteSpace(argumentsJson)
                ? JsonDocument.Parse("{}")
                : JsonDocument.Parse(argumentsJson);
            return await tool.ExecuteAsync(doc.RootElement, ctx);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = "tool_failed", tool = name, message = ex.Message });
        }
    }
}

internal static class AiToolArgs
{
    public static string? GetString(JsonElement args, string name)
    {
        if (args.ValueKind != JsonValueKind.Object) return null;
        return args.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }

    public static int? GetInt(JsonElement args, string name)
    {
        if (args.ValueKind != JsonValueKind.Object) return null;
        if (!args.TryGetProperty(name, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.Number when v.TryGetInt32(out var n) => n,
            JsonValueKind.String when int.TryParse(v.GetString(), out var s) => s,
            _ => null,
        };
    }

    public static double? GetDouble(JsonElement args, string name)
    {
        if (args.ValueKind != JsonValueKind.Object) return null;
        if (!args.TryGetProperty(name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var n)) return n;
        if (v.ValueKind == JsonValueKind.String)
        {
            var str = AiTextUtil.ToEnDigits(v.GetString() ?? "").Replace(",", "").Replace("٬", "").Replace(" ", "").Trim();
            if (double.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out var d)) return d;
        }
        return null;
    }

    public static bool GetBool(JsonElement args, string name, bool defaultValue = false)
    {
        if (args.ValueKind != JsonValueKind.Object) return defaultValue;
        if (!args.TryGetProperty(name, out var v)) return defaultValue;
        if (v.ValueKind == JsonValueKind.True) return true;
        if (v.ValueKind == JsonValueKind.False) return false;
        if (v.ValueKind == JsonValueKind.String)
            return v.GetString()?.Trim().ToLowerInvariant() is "true" or "1" or "yes";
        return defaultValue;
    }

    public static string StatusFa(int s) => s switch { 0 => "در انتظار", 1 => "تأیید شده", 2 => "رد شده", _ => "نامشخص" };
}

// ---------------- راهنمای هوشمند (قابلیت ۵) ----------------

public class GuideSearchTool : IAiTool
{
    public string Name => "guide_search";
    public string Description => "جستجو در راهنمای قدم‌به‌قدم سامانه. برای سؤال‌های «چطور ...؟» و «کجا ...؟» اول از این ابزار استفاده کن. لینک صفحه مرتبط هم برمی‌گرداند.";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            query = new { type = "string", description = "عبارت جستجو به فارسی (مثل: ثبت مرخصی، فاکتور فروش)" },
        },
        required = new[] { "query" },
    };

    public async Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx)
    {
        var kb = ctx.Services.GetRequiredService<AiKnowledgeService>();
        var query = AiToolArgs.GetString(args, "query") ?? "";
        var matches = await kb.SearchAsync(query, 4, ctx.CancellationToken);
        return JsonSerializer.Serialize(matches.Select(m => new
        {
            m.Title,
            content = AiTextUtil.Truncate(m.Content, 700),
            m.Category,
            link = m.Link,
        }));
    }
}

// ---------------- منشی کارمندی (قابلیت ۲۲) ----------------

public class MyLeaveBalanceTool : IAiTool
{
    public string Name => "my_leave_balance";
    public string Description => "مانده مرخصی کاربر جاری به تفکیک نوع مرخصی (استحقاقی، استعلاجی و...). برای «چند روز مرخصی دارم؟» از این استفاده کن.";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            year = new { type = "integer", description = "سال شمسی (اختیاری؛ خالی = سال جاری)" },
        },
    };

    public async Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx)
    {
        var fa = ctx.Services.GetRequiredService<IFaAttService>();
        var list = await fa.MyBalancesAsync(ctx.UserId, AiToolArgs.GetInt(args, "year"));
        return JsonSerializer.Serialize(list.Select(b => new
        {
            نوع = b.LeaveTypeName,
            سال = b.Year,
            استحقاق = b.EntitledDays,
            استفاده_شده = b.UsedDays,
            مانده = b.Remaining,
        }));
    }
}

public class MyLeavesTool : IAiTool
{
    public string Name => "my_leaves";
    public string Description => "فهرست درخواست‌های مرخصی کاربر جاری با وضعیت (در انتظار/تأیید/رد).";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            limit = new { type = "integer", description = "تعداد (پیش‌فرض ۵، حداکثر ۲۰)" },
        },
    };

    public async Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx)
    {
        var fa = ctx.Services.GetRequiredService<IFaAttService>();
        var limit = Math.Clamp(AiToolArgs.GetInt(args, "limit") ?? 5, 1, 20);
        var list = (await fa.MyLeavesAsync(ctx.UserId)).Take(limit).ToList();
        return JsonSerializer.Serialize(list.Select(l => new
        {
            نوع = l.LeaveTypeName,
            از = AiDateUtil.ToFaShort(l.FromDate),
            تا = AiDateUtil.ToFaShort(l.ToDate),
            وضعیت = AiToolArgs.StatusFa(l.Status),
        }));
    }
}

public class MyAttendanceTool : IAiTool
{
    public string Name => "my_attendance_today";
    public string Description => "وضعیت حضور امروز کاربر جاری: شیفت، ساعت ورود/خروج، تأخیر، کارکرد.";
    public object ParametersSchema => new { type = "object", properties = new { } };

    public async Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx)
    {
        var fa = ctx.Services.GetRequiredService<IFaAttService>();
        var t = await fa.MyTodayAsync(ctx.UserId);
        return JsonSerializer.Serialize(new
        {
            تاریخ = AiDateUtil.ToFaShort(t.Date),
            شیفت = t.ShiftName,
            ورود = t.Daily?.FirstIn?.ToString("HH:mm"),
            خروج = t.Daily?.LastOut?.ToString("HH:mm"),
            کارکرد_دقیقه = t.Daily?.WorkMinutes,
            تأخیر_دقیقه = t.Daily?.LateMinutes,
            اضافه_کار_دقیقه = t.Daily?.OvertimeMinutes,
            ناقص = t.Daily?.IsIncomplete,
        });
    }
}

public class MyPayslipTool : IAiTool
{
    public string Name => "my_payslip";
    public string Description => "فیش حقوقی کاربر جاری: ناخالص، کسورات، مالیات، بیمه و خالص پرداختی به‌همراه اقلام. بدون ورودی = آخرین فیش.";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            year = new { type = "integer", description = "سال (اختیاری)" },
            month = new { type = "integer", description = "ماه ۱ تا ۱۲ (اختیاری)" },
        },
    };

    public async Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx)
    {
        var pay = ctx.Services.GetRequiredService<IFaPayService>();
        var slips = await pay.MySlipsAsync(ctx.UserId);
        var year = AiToolArgs.GetInt(args, "year");
        var month = AiToolArgs.GetInt(args, "month");
        var pick = slips
            .Where(s => (year == null || s.RunYear == year) && (month == null || s.RunMonth == month))
            .OrderByDescending(s => s.RunYear).ThenByDescending(s => s.RunMonth)
            .FirstOrDefault();
        if (pick == null)
            return JsonSerializer.Serialize(new { error = "not_found", message = "فیشی برای این دوره یافت نشد." });
        var full = await pay.GetMySlipAsync(pick.Id, ctx.UserId) ?? pick;
        return JsonSerializer.Serialize(new
        {
            دوره = $"{full.RunYear}/{full.RunMonth:00}",
            حقوق_پایه = full.BaseSalary,
            ناخالص = full.GrossEarnings,
            کسورات = full.TotalDeductions,
            مالیات = full.TaxAmount,
            بیمه = full.InsuranceAmount,
            خالص_پرداختی = full.NetPay,
            پرداخت_شده = full.IsPaid,
            اقلام = full.Items.Select(i => new { عنوان = i.Title, مبلغ = i.Amount }),
        });
    }
}

public class MyLoansTool : IAiTool
{
    public string Name => "my_loans";
    public string Description => "وام‌ها و مساعده‌های کاربر جاری: مبلغ، اقساط، پرداخت‌شده و مانده.";
    public object ParametersSchema => new { type = "object", properties = new { } };

    public async Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx)
    {
        var db = ctx.Services.GetRequiredService<AppDbContext>();
        var extra = ctx.Services.GetRequiredService<IFaPayExtraService>();
        var empId = await db.HrEmployees.AsNoTracking()
            .Where(e => e.SystemUserId == ctx.UserId).Select(e => e.Id).FirstOrDefaultAsync(ctx.CancellationToken);
        if (empId == 0)
            return JsonSerializer.Serialize(new { error = "no_employee", message = "پرونده پرسنلی برای این کاربر یافت نشد." });
        var loans = await extra.ListLoansAsync(empId, null);
        return JsonSerializer.Serialize(loans.Select(l => new
        {
            عنوان = l.Title,
            مبلغ_کل = l.TotalAmount,
            تعداد_اقساط = l.InstallmentCount,
            مبلغ_قسط = l.InstallmentAmount,
            پرداخت_شده = l.PaidAmount,
            مانده = l.RemainingAmount,
        }));
    }
}

public class MyMissionsTool : IAiTool
{
    public string Name => "my_missions";
    public string Description => "مأموریت‌های کاربر جاری با مقصد و وضعیت.";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            limit = new { type = "integer", description = "تعداد (پیش‌فرض ۵، حداکثر ۲۰)" },
        },
    };

    public async Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx)
    {
        var fa = ctx.Services.GetRequiredService<IFaAttService>();
        var limit = Math.Clamp(AiToolArgs.GetInt(args, "limit") ?? 5, 1, 20);
        var list = (await fa.MyMissionsAsync(ctx.UserId)).Take(limit).ToList();
        return JsonSerializer.Serialize(list.Select(m => new
        {
            مقصد = m.Destination,
            از = AiDateUtil.ToFaShort(m.FromDate),
            تا = AiDateUtil.ToFaShort(m.ToDate),
            وضعیت = AiToolArgs.StatusFa(m.Status),
        }));
    }
}

// ---------------- نامه‌ها در گفتگو ----------------

public class MyLettersStatsTool : IAiTool
{
    public string Name => "my_letters_stats";
    public string Description => "آمار کارتابل نامه کاربر جاری: خوانده‌نشده‌ها، ارسالی‌ها، پیش‌نویس‌ها، مهلت‌های نزدیک.";
    public object ParametersSchema => new { type = "object", properties = new { } };

    public async Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx)
    {
        var letters = ctx.Services.GetRequiredService<IInnerLetterService>();
        var s = await letters.GetStatsAsync(ctx.UserId);
        return JsonSerializer.Serialize(new
        {
            خوانده_نشده = s.InboxUnread,
            کل_وارده = s.InboxTotal,
            ارسالی = s.SentTotal,
            پیش_نویس = s.PishnevisTotal,
            مهلت_نزدیک = s.DeadlineSoon,
        });
    }
}

public class MyLettersInboxTool : IAiTool
{
    public string Name => "my_letters_inbox";
    public string Description => "فهرست نامه‌های وارده کاربر جاری (تازه‌ترین‌ها). برای «نامه‌ای دارم؟» و «نامه‌های خوانده‌نشده» استفاده کن.";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            unread_only = new { type = "boolean", description = "فقط خوانده‌نشده‌ها (پیش‌فرض false)" },
            limit = new { type = "integer", description = "تعداد (پیش‌فرض ۵، حداکثر ۱۵)" },
        },
    };

    public async Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx)
    {
        var letters = ctx.Services.GetRequiredService<IInnerLetterService>();
        var limit = Math.Clamp(AiToolArgs.GetInt(args, "limit") ?? 5, 1, 15);
        var page = await letters.GetInboxAsync(ctx.UserId, null, AiToolArgs.GetBool(args, "unread_only"), 1, limit);
        return JsonSerializer.Serialize(new
        {
            تعداد_کل = page.TotalCount,
            نامه_ها = page.Items.Select(l => new
            {
                شناسه = l.LetterId,
                شماره = l.LetterNumber,
                عنوان = l.Title,
                فرستنده = l.Sender,
                تاریخ = AiDateUtil.ToFaShort(l.Date),
                خوانده_شده = l.IsRead,
                فوریت = l.Foriat,
            }),
        });
    }
}

public class LetterDetailTool : IAiTool
{
    public string Name => "letter_detail";
    public string Description => "متن کامل یک نامه + زنجیره ارجاع‌های آن (گردش). فقط اگر کاربر به نامه دسترسی داشته باشد داده برمی‌گردد.";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            letter_id = new { type = "integer", description = "شناسه نامه (از خروجی my_letters_inbox)" },
        },
        required = new[] { "letter_id" },
    };

    public async Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx)
    {
        var letters = ctx.Services.GetRequiredService<IInnerLetterService>();
        var erja = ctx.Services.GetRequiredService<IErjaService>();
        var id = AiToolArgs.GetInt(args, "letter_id") ?? 0;
        var d = await letters.GetDetailAsync(id, ctx.UserId, ctx.IsAdmin);
        if (d == null)
            return JsonSerializer.Serialize(new { error = "access_denied", message = "نامه یافت نشد یا دسترسی ندارید." });
        List<object> chain = new();
        try
        {
            var tree = await erja.GetGardeshTreeAsync(id, ctx.UserId, ctx.IsAdmin);
            chain = tree.Take(20).Select(n => (object)new
            {
                از = n.Sender,
                به = n.Reciver,
                دستور = AiTextUtil.Truncate(AiTextUtil.StripHtml(n.MatnErja), 250),
                تاریخ = AiDateUtil.ToFaShort(n.Date),
                خوانده = n.IsRead,
                پاسخ = AiTextUtil.Truncate(AiTextUtil.StripHtml(n.Answer), 200),
            }).ToList();
        }
        catch { /* گردش اختیاری است */ }
        return JsonSerializer.Serialize(new
        {
            شماره = d.LetterNumber,
            عنوان = d.Title,
            فرستنده = d.SenderName,
            تاریخ = AiDateUtil.ToFaShort(d.DateSabt),
            متن = AiTextUtil.Truncate(AiTextUtil.StripHtml(d.Text), 3000),
            گیرندگان = d.ReciversGirande.Select(r => r.FullName).Where(x => x != "").Take(10),
            گردش_ارجاع = chain,
        });
    }
}

public class UsersLookupTool : IAiTool
{
    public string Name => "users_lookup";
    public string Description => "جستجوی همکاران (نام و واحد سازمانی) برای پیشنهاد ارجاع نامه یا معرفی مسئول. شناسه کاربر هم برمی‌گرداند.";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            search = new { type = "string", description = "نام/نام‌خانوادگی یا واحد (اختیاری؛ خالی = همه، حداکثر ۶۰ نفر)" },
        },
    };

    public async Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx)
    {
        var db = ctx.Services.GetRequiredService<AppDbContext>();
        var search = AiTextUtil.NormalizeFa(AiToolArgs.GetString(args, "search"));
        var users = await db.Users.AsNoTracking()
            .Where(u => u.IsActive && u.Username != Data.AiSeeder.AiUsername)
            .OrderBy(u => u.LastName).ThenBy(u => u.FirstName)
            .Select(u => new { u.Id, u.Username, u.FirstName, u.LastName })
            .Take(200)
            .ToListAsync(ctx.CancellationToken);

        // واحد سازمانی از پروفایل پرسنلی (اتصال با نام کاربری — مثل سرویس چت)
        var deptRows = await (from profile in db.SystemUsers.AsNoTracking()
                              join department in db.SystemDepartments.AsNoTracking()
                                  on profile.DepartmentId equals (int?)department.Id
                              where profile.IsActive && department.IsActive && profile.Username != ""
                              select new { profile.Username, department.Name }).ToListAsync(ctx.CancellationToken);
        var deptMap = deptRows.GroupBy(p => p.Username.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Name, StringComparer.OrdinalIgnoreCase);

        var filtered = users
            .Select(u => new
            {
                u.Id,
                نام = ((u.FirstName ?? "") + " " + (u.LastName ?? "")).Trim(),
                واحد = deptMap.TryGetValue((u.Username ?? "").Trim(), out var dpt) ? dpt : null,
            })
            .Where(u => u.نام != "")
            .Where(u => search == "" || AiTextUtil.NormalizeFa(u.نام + " " + (u.واحد ?? "")).Contains(search))
            .Take(60)
            .ToList();
        return JsonSerializer.Serialize(filtered.Select(u => new { شناسه = u.Id, u.نام, u.واحد }));
    }
}

// ---------------- اقدام با تأیید ----------------

public class RequestLeaveTool : IAiTool
{
    public string Name => "request_leave";
    public string Description => "ثبت پیش‌فاکتور درخواست مرخصی (اجرا فقط بعد از تأیید کاربر). تاریخ‌ها را می‌توانی فارسی بدهی: امروز، فردا، پس‌فردا، نام روز هفته، یا ۱۴۰۴/۰۷/۰۵.";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            leave_type = new { type = "string", description = "نوع مرخصی (مثلاً استحقاقی، استعلاجی) — خالی = استحقاقی" },
            from_date = new { type = "string", description = "از تاریخ (اجباری): امروز، فردا، پس‌فردا، نام روز، یا ۱۴۰۴/۰۷/۰۵" },
            to_date = new { type = "string", description = "تا تاریخ (اختیاری؛ خالی = همان روز شروع)" },
            reason = new { type = "string", description = "دلیل (اختیاری)" },
        },
        required = new[] { "from_date" },
    };

    public async Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx)
    {
        var actions = ctx.Services.GetRequiredService<AiActionService>();
        var fa = ctx.Services.GetRequiredService<IFaAttService>();
        var today = DateTime.Today;

        var fromParsed = AiLeaveHelper.ParseFaDate(AiToolArgs.GetString(args, "from_date"), today);
        if (fromParsed == null)
            return JsonSerializer.Serialize(new { error = "bad_date", message = "تاریخ شروع را نفهمیدم؛ مثلاً بگو «فردا» یا «۱۴۰۴/۰۷/۰۵»." });
        var from = fromParsed.Value;
        var to = AiLeaveHelper.ParseFaDate(AiToolArgs.GetString(args, "to_date"), today) ?? from;
        if (to < from) (from, to) = (to, from);

        var type = await AiLeaveHelper.MatchLeaveTypeAsync(fa, AiToolArgs.GetString(args, "leave_type"));
        if (type == null)
            return JsonSerializer.Serialize(new { error = "no_leave_type", message = "نوع مرخصی فعالی تعریف نشده." });

        var reason = AiToolArgs.GetString(args, "reason");
        var summary = $"مرخصی {type.Value.name} از {AiDateUtil.ToFaShort(from)} تا {AiDateUtil.ToFaShort(to)}" +
                      (string.IsNullOrWhiteSpace(reason) ? "" : $" (دلیل: {reason.Trim()})");
        var pending = await actions.CreateAsync(ctx.UserId, "request_leave",
            JsonSerializer.Serialize(new
            {
                leaveTypeId = type.Value.id,
                from = from.ToString("yyyy-MM-dd"),
                to = to.ToString("yyyy-MM-dd"),
                reason,
            }), summary, ctx.CancellationToken);

        return JsonSerializer.Serialize(new
        {
            action_id = pending.Id,
            summary,
            hint = "برای اجرا، کاربر باید بنویسد: تأیید",
        });
    }
}

public class ClockTool : IAiTool
{
    public string Name => "clock";
    public string Description => "ثبت پیش‌فاکتور ساعت‌زنی ورود/خروج (اجرا فقط بعد از تأیید کاربر).";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            type = new { type = "string", description = "ورود یا خروج (یا in/out)" },
        },
        required = new[] { "type" },
    };

    public async Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx)
    {
        var actions = ctx.Services.GetRequiredService<AiActionService>();
        var t = AiTextUtil.NormalizeFa(AiToolArgs.GetString(args, "type"));
        var kind = t switch
        {
            var x when x.Contains("ورود") || x == "in" || x == "0" => 0,
            var x when x.Contains("خروج") || x == "out" || x == "1" => 1,
            _ => -1,
        };
        if (kind < 0)
            return JsonSerializer.Serialize(new { error = "bad_type", message = "مشخص کن: ورود یا خروج؟" });

        var summary = kind == 0 ? "ثبت ساعت ورود (الان)" : "ثبت ساعت خروج (الان)";
        var pending = await actions.CreateAsync(ctx.UserId, "clock",
            JsonSerializer.Serialize(new { type = kind }), summary, ctx.CancellationToken);
        return JsonSerializer.Serialize(new
        {
            action_id = pending.Id,
            summary,
            hint = "برای اجرا، کاربر باید بنویسد: تأیید",
        });
    }
}

public class ConfirmActionTool : IAiTool
{
    public string Name => "confirm_action";
    public string Description => "تأیید و اجرای پیش‌فاکتور باز کاربر. فقط وقتی صدا بزن که کاربر صراحتاً تأیید کرد (مثلاً گفت «تأیید»، «باشه»، «اوکی»). بدون شناسه = تازه‌ترین پیش‌فاکتور.";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            action_id = new { type = "integer", description = "شناسه پیش‌فاکتور (اختیاری؛ خالی = تازه‌ترین)" },
        },
    };

    public async Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx)
    {
        var actions = ctx.Services.GetRequiredService<AiActionService>();
        var (ok, message) = await actions.ConfirmAsync(ctx.UserId, ctx.UserName,
            AiToolArgs.GetInt(args, "action_id"), ctx.CancellationToken);
        return JsonSerializer.Serialize(new { ok, message });
    }
}

public class CancelActionTool : IAiTool
{
    public string Name => "cancel_action";
    public string Description => "لغو پیش‌فاکتور باز کاربر. وقتی کاربر گفت «لغو»، «کنسل» یا پشیمان شد صدا بزن. بدون شناسه = تازه‌ترین.";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            action_id = new { type = "integer", description = "شناسه پیش‌فاکتور (اختیاری؛ خالی = تازه‌ترین)" },
        },
    };

    public async Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx)
    {
        var actions = ctx.Services.GetRequiredService<AiActionService>();
        var (ok, message) = await actions.CancelAsync(ctx.UserId,
            AiToolArgs.GetInt(args, "action_id"), ctx.CancellationToken);
        return JsonSerializer.Serialize(new { ok, message });
    }
}

public class PendingActionsTool : IAiTool
{
    public string Name => "my_pending_actions";
    public string Description => "فهرست پیش‌فاکتورهای باز کاربر (اقدام‌های در انتظار تأیید).";
    public object ParametersSchema => new { type = "object", properties = new { } };

    public async Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx)
    {
        var actions = ctx.Services.GetRequiredService<AiActionService>();
        var list = await actions.ListPendingAsync(ctx.UserId, ctx.CancellationToken);
        return JsonSerializer.Serialize(list.Select(a => new
        {
            شناسه = a.Id,
            اقدام = AiActionNames.Fa(a.Action),
            خلاصه = a.Summary,
        }));
    }
}

// ---------------- نام فارسی اقدام‌ها ----------------

internal static class AiActionNames
{
    public static string Fa(string action) => action switch
    {
        "request_leave" => "درخواست مرخصی",
        "clock" => "ساعت‌زنی",
        "request_mission" => "درخواست مأموریت",
        "decide_leave" => "تأیید/رد مرخصی",
        "answer_referral" => "پاسخ به ارجاع",
        "create_ticket" => "ثبت تیکت پشتیبانی",
        "report_work" => "گزارش‌کار",
        "create_letter_draft" => "پیش‌نویس نامه",
        _ => action,
    };
}

// ---------------- تأییدها و اقدام‌های اجرایی (فاز ۲) ----------------

public class PendingApprovalsTool : IAiTool
{
    public string Name => "pending_approvals";
    public string Description => "مرخصی‌های در انتظار تأییدِ کاربر جاری (برای مدیر/سرپرست: درخواست‌های نیروهای مستقیم). شناسه هر مورد را هم می‌دهد تا با decide_leave تأیید/رد شود. قبل از decide_leave حتماً این را صدا بزن تا شناسه درست را پیدا کنی.";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            limit = new { type = "integer", description = "تعداد (پیش‌فرض ۱۰، حداکثر ۳۰)" },
        },
    };

    public async Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx)
    {
        var fa = ctx.Services.GetRequiredService<IFaAttService>();
        var limit = Math.Clamp(AiToolArgs.GetInt(args, "limit") ?? 10, 1, 30);
        var list = (await fa.TeamLeavesAsync(ctx.UserId)).Take(limit).ToList();
        return JsonSerializer.Serialize(list.Select(l => new
        {
            شناسه = l.Id,
            کارمند = l.EmployeeName,
            نوع = l.LeaveTypeName,
            از = AiDateUtil.ToFaShort(l.FromDate),
            تا = AiDateUtil.ToFaShort(l.ToDate),
            دلیل = l.Reason,
        }));
    }
}

public class MyReferralsPendingTool : IAiTool
{
    public string Name => "my_referrals_pending";
    public string Description => "ارجاع‌های بی‌پاسخ کاربر جاری از کارتابل (نامه‌هایی که باید جواب بدهد). شناسه ارجاع (erja_id) را می‌دهد تا با answer_referral پاسخ/تأیید/رد شود. قبل از answer_referral حتماً این را صدا بزن.";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            limit = new { type = "integer", description = "تعداد (پیش‌فرض ۱۰، حداکثر ۲۰)" },
        },
    };

    public async Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx)
    {
        var letters = ctx.Services.GetRequiredService<IInnerLetterService>();
        var limit = Math.Clamp(AiToolArgs.GetInt(args, "limit") ?? 10, 1, 20);
        var page = await letters.GetInboxAsync(ctx.UserId, null, false, 1, 50);
        var items = page.Items
            .Where(l => l.ErjaId != null && !l.HasAnswer)
            .Take(limit).ToList();
        return JsonSerializer.Serialize(items.Select(l => new
        {
            erja_id = l.ErjaId,
            letter_id = l.LetterId,
            شماره = l.LetterNumber,
            عنوان = l.Title,
            فرستنده = l.Sender,
            پاراف = AiTextUtil.Truncate(AiTextUtil.StripHtml(l.MatnErja ?? ""), 200),
            تاریخ = AiDateUtil.ToFaShort(l.Date),
            مهلت = l.MohlatPasokh == null ? null : AiDateUtil.ToFaShort(l.MohlatPasokh.Value),
        }));
    }
}

public class RequestMissionTool : IAiTool
{
    public string Name => "request_mission";
    public string Description => "ثبت پیش‌فاکتور درخواست مأموریت (اجرا فقط بعد از تأیید کاربر). تاریخ‌ها را می‌توانی فارسی بدهی: امروز، فردا، پس‌فردا، نام روز هفته، یا ۱۴۰۴/۰۷/۰۵.";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            destination = new { type = "string", description = "مقصد مأموریت (اجباری)" },
            from_date = new { type = "string", description = "از تاریخ (اجباری)" },
            to_date = new { type = "string", description = "تا تاریخ (اختیاری؛ خالی = همان روز شروع)" },
            reason = new { type = "string", description = "دلیل/موضوع مأموریت (اختیاری)" },
        },
        required = new[] { "destination", "from_date" },
    };

    public async Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx)
    {
        var actions = ctx.Services.GetRequiredService<AiActionService>();
        var dest = (AiToolArgs.GetString(args, "destination") ?? "").Trim();
        if (dest == "")
            return JsonSerializer.Serialize(new { error = "bad_destination", message = "مقصد مأموریت مشخص نیست." });
        var today = DateTime.Today;
        var fromParsed = AiLeaveHelper.ParseFaDate(AiToolArgs.GetString(args, "from_date"), today);
        if (fromParsed == null)
            return JsonSerializer.Serialize(new { error = "bad_date", message = "تاریخ شروع را نفهمیدم؛ مثلاً بگو «فردا» یا «۱۴۰۴/۰۷/۰۵»." });
        var from = fromParsed.Value;
        var to = AiLeaveHelper.ParseFaDate(AiToolArgs.GetString(args, "to_date"), today) ?? from;
        if (to < from) (from, to) = (to, from);

        var reason = AiToolArgs.GetString(args, "reason");
        var summary = $"مأموریت {dest} از {AiDateUtil.ToFaShort(from)} تا {AiDateUtil.ToFaShort(to)}" +
                      (string.IsNullOrWhiteSpace(reason) ? "" : $" (دلیل: {reason.Trim()})");
        var pending = await actions.CreateAsync(ctx.UserId, "request_mission",
            JsonSerializer.Serialize(new
            {
                from = from.ToString("yyyy-MM-dd"),
                to = to.ToString("yyyy-MM-dd"),
                destination = dest,
                reason,
            }), summary, ctx.CancellationToken);
        return JsonSerializer.Serialize(new { action_id = pending.Id, summary, hint = "برای اجرا، کاربر باید بنویسد: تأیید" });
    }
}

public class DecideLeaveTool : IAiTool
{
    public string Name => "decide_leave";
    public string Description => "ثبت پیش‌فاکتور تأیید/رد یک مرخصی (اجرا فقط بعد از تأیید کاربر). شناسه را از pending_approvals بگیر. فقط برای مرخصی نیروهای مستقیم کاربر (یا کارشناس منابع انسانی) جواب می‌دهد.";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            leave_id = new { type = "integer", description = "شناسه مرخصی از خروجی pending_approvals (اجباری)" },
            approve = new { type = "boolean", description = "true=تأیید، false=رد (اجباری)" },
        },
        required = new[] { "leave_id", "approve" },
    };

    public async Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx)
    {
        var actions = ctx.Services.GetRequiredService<AiActionService>();
        var fa = ctx.Services.GetRequiredService<IFaAttService>();
        var leaveId = AiToolArgs.GetInt(args, "leave_id") ?? 0;
        if (leaveId <= 0)
            return JsonSerializer.Serialize(new { error = "bad_id", message = "شناسه مرخصی معتبر نیست؛ اول pending_approvals را ببین." });
        var approve = AiToolArgs.GetBool(args, "approve");

        var leave = (await fa.TeamLeavesAsync(ctx.UserId)).FirstOrDefault(l => l.Id == leaveId);
        var what = approve ? "تأیید" : "رد";
        var summary = leave == null
            ? $"{what} مرخصی شماره {leaveId}"
            : $"{what} مرخصی {leave.EmployeeName} ({leave.LeaveTypeName}، {AiDateUtil.ToFaShort(leave.FromDate)} تا {AiDateUtil.ToFaShort(leave.ToDate)})";
        var pending = await actions.CreateAsync(ctx.UserId, "decide_leave",
            JsonSerializer.Serialize(new { leave_id = leaveId, approve }), summary, ctx.CancellationToken);
        return JsonSerializer.Serialize(new { action_id = pending.Id, summary, hint = "برای اجرا، کاربر باید بنویسد: تأیید" });
    }
}

public class AnswerReferralTool : IAiTool
{
    public string Name => "answer_referral";
    public string Description => "ثبت پیش‌فاکتور پاسخ به یک ارجاع نامه (اجرا فقط بعد از تأیید کاربر). شناسه ارجاع را از my_referrals_pending بگیر. تصمیم: «تأیید» یا «رد» یا «پاسخ» (متن خالی).";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            erja_id = new { type = "integer", description = "شناسه ارجاع از خروجی my_referrals_pending (اجباری)" },
            decision = new { type = "string", description = "تأیید / رد / پاسخ (پیش‌فرض: پاسخ)" },
            text = new { type = "string", description = "متن پاسخ (برای تصمیم «پاسخ» اجباری است)" },
        },
        required = new[] { "erja_id" },
    };

    public async Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx)
    {
        var actions = ctx.Services.GetRequiredService<AiActionService>();
        var letters = ctx.Services.GetRequiredService<IInnerLetterService>();
        var erjaId = AiToolArgs.GetInt(args, "erja_id") ?? 0;
        if (erjaId <= 0)
            return JsonSerializer.Serialize(new { error = "bad_id", message = "شناسه ارجاع معتبر نیست؛ اول my_referrals_pending را ببین." });
        var d = AiTextUtil.NormalizeFa(AiToolArgs.GetString(args, "decision"));
        var decision = d.Contains("تایید") || d.Contains("تأیید") || d is "approve" or "1" ? 1
            : d is "رد" or "reject" or "2" || d.Contains("مخالف") ? 2 : 0;
        var text = (AiToolArgs.GetString(args, "text") ?? "").Trim();
        if (decision == 0 && text == "")
            return JsonSerializer.Serialize(new { error = "no_text", message = "متن پاسخ را بگو، یا تصمیم را «تأیید»/«رد» بگذار." });

        // عنوان نامه برای خلاصه خواناتر (اختیاری)
        string title = "";
        try
        {
            var page = await letters.GetInboxAsync(ctx.UserId, null, false, 1, 50);
            title = page.Items.FirstOrDefault(l => l.ErjaId == erjaId)?.Title ?? "";
        }
        catch { /* خلاصه بدون عنوان */ }
        var what = decision == 1 ? "تأیید" : decision == 2 ? "رد" : "پاسخ به";
        var summary = $"{what} ارجاع" + (title == "" ? $" شماره {erjaId}" : $" «{title}»") +
                      (decision == 0 ? $": {AiTextUtil.Truncate(text, 80)}" : "");
        var pending = await actions.CreateAsync(ctx.UserId, "answer_referral",
            JsonSerializer.Serialize(new { erja_id = erjaId, decision, text }), summary, ctx.CancellationToken);
        return JsonSerializer.Serialize(new { action_id = pending.Id, summary, hint = "برای اجرا، کاربر باید بنویسد: تأیید" });
    }
}

public class CreateTicketTool : IAiTool
{
    public string Name => "create_ticket";
    public string Description => "ثبت پیش‌فاکتور تیکت پشتیبانی منابع انسانی (اجرا فقط بعد از تأیید کاربر). دسته: مرخصی، حقوق، بیمه، قرارداد، آموزش یا سایر.";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            subject = new { type = "string", description = "موضوع تیکت (اجباری)" },
            body = new { type = "string", description = "شرح مشکل/درخواست (اجباری)" },
            category = new { type = "string", description = "دسته: مرخصی، حقوق، بیمه، قرارداد، آموزش، سایر (پیش‌فرض: سایر)" },
        },
        required = new[] { "subject", "body" },
    };

    public async Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx)
    {
        var actions = ctx.Services.GetRequiredService<AiActionService>();
        var subject = (AiToolArgs.GetString(args, "subject") ?? "").Trim();
        var body = (AiToolArgs.GetString(args, "body") ?? "").Trim();
        if (subject == "" || body == "")
            return JsonSerializer.Serialize(new { error = "bad_input", message = "موضوع و شرح تیکت هر دو لازم است." });
        var c = AiTextUtil.NormalizeFa(AiToolArgs.GetString(args, "category"));
        var (category, catName) = c.Contains("مرخص") ? (1, "مرخصی")
            : c.Contains("حقوق") || c.Contains("فیش") || c.Contains("دستمزد") ? (2, "حقوق")
            : c.Contains("بیمه") ? (3, "بیمه")
            : c.Contains("قرارداد") ? (4, "قرارداد")
            : c.Contains("آموزش") ? (5, "آموزش") : (0, "سایر");
        var summary = $"تیکت «{subject}» (دسته: {catName})";
        var pending = await actions.CreateAsync(ctx.UserId, "create_ticket",
            JsonSerializer.Serialize(new { subject, body, category }), summary, ctx.CancellationToken);
        return JsonSerializer.Serialize(new { action_id = pending.Id, summary, hint = "برای اجرا، کاربر باید بنویسد: تأیید" });
    }
}

public class ReportWorkTool : IAiTool
{
    public string Name => "report_work";
    public string Description => "ثبت پیش‌فاکتور گزارش‌کار روزانه روی یک پروژه (اجرا فقط بعد از تأیید کاربر). پروژه را می‌توانی با نام (حتی ناقص) یا شناسه بدهی؛ اگر چند پروژه شبیه هم بود، ابزار فهرست می‌دهد تا دقیق بپرسی.";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            project = new { type = "string", description = "نام (کامل یا بخشی) یا شناسه عددی پروژه (اجباری)" },
            description = new { type = "string", description = "شرح کاری که انجام شد (اجباری)" },
            date = new { type = "string", description = "تاریخ گزارش: امروز، دیروز، یا ۱۴۰۴/۰۷/۰۵ (پیش‌فرض: امروز)" },
            start = new { type = "string", description = "ساعت شروع مثل 08:00 (پیش‌فرض 08:00)" },
            end = new { type = "string", description = "ساعت پایان مثل 17:00 (پیش‌فرض 17:00)" },
        },
        required = new[] { "project", "description" },
    };

    public async Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx)
    {
        var actions = ctx.Services.GetRequiredService<AiActionService>();
        var db = ctx.Services.GetRequiredService<AppDbContext>();
        var projArg = (AiToolArgs.GetString(args, "project") ?? "").Trim();
        var desc = (AiToolArgs.GetString(args, "description") ?? "").Trim();
        if (projArg == "" || desc == "")
            return JsonSerializer.Serialize(new { error = "bad_input", message = "پروژه و شرح کار هر دو لازم است." });

        var query = db.ProjectEntryExits.AsNoTracking().Where(x => !x.IsDelete);
        List<ProjectListRow> matches;
        if (int.TryParse(AiTextUtil.ToEnDigits(projArg), out var projId))
        {
            matches = await query.Where(x => x.Id == projId)
                .Select(x => new ProjectListRow(x.Id, x.ProjectName, x.CodeProject)).ToListAsync(ctx.CancellationToken);
        }
        else
        {
            var norm = AiTextUtil.NormalizeFa(projArg);
            var all = await query.Select(x => new ProjectListRow(x.Id, x.ProjectName, x.CodeProject))
                .ToListAsync(ctx.CancellationToken);
            matches = all.Where(x => AiTextUtil.NormalizeFa(x.Name + " " + x.Code).Contains(norm)).Take(6).ToList();
        }
        if (matches.Count == 0)
            return JsonSerializer.Serialize(new { error = "no_project", message = $"پروژه‌ای با نام «{projArg}» پیدا نکردم؛ نام دقیق‌تر یا شناسه را بگو." });
        if (matches.Count > 1)
            return JsonSerializer.Serialize(new
            {
                error = "ambiguous_project",
                message = "چند پروژه شبیه هم پیدا شد؛ کدام؟",
                candidates = matches.Select(m => new { شناسه = m.Id, نام = m.Name, کد = m.Code }),
            });
        var project = matches[0];

        var date = AiLeaveHelper.ParseFaDate(AiToolArgs.GetString(args, "date"), DateTime.Today) ?? DateTime.Today;
        var startRaw = AiTextUtil.ToEnDigits(AiToolArgs.GetString(args, "start") ?? "08:00");
        var endRaw = AiTextUtil.ToEnDigits(AiToolArgs.GetString(args, "end") ?? "17:00");
        if (!TimeOnly.TryParse(startRaw, out var start) || !TimeOnly.TryParse(endRaw, out var end))
            return JsonSerializer.Serialize(new { error = "bad_time", message = "ساعت شروع/پایان معتبر نیست؛ مثل 08:00 و 17:00." });
        if (start == end)
            return JsonSerializer.Serialize(new { error = "bad_time", message = "ساعت شروع و پایان نمی‌توانند یکسان باشند." });

        var summary = $"گزارش‌کار پروژه «{project.Name}» برای {AiDateUtil.ToFaShort(date)} ({start:HH:mm} تا {end:HH:mm})";
        var pending = await actions.CreateAsync(ctx.UserId, "report_work",
            JsonSerializer.Serialize(new
            {
                project_id = project.Id,
                description = desc,
                date = date.ToString("yyyy-MM-dd"),
                start = start.ToString("HH:mm"),
                end = end.ToString("HH:mm"),
            }), summary, ctx.CancellationToken);
        return JsonSerializer.Serialize(new { action_id = pending.Id, summary, hint = "برای اجرا، کاربر باید بنویسد: تأیید" });
    }

    private sealed record ProjectListRow(int Id, string Name, string Code);
}

public class CreateLetterDraftTool : IAiTool
{
    public string Name => "create_letter_draft";
    public string Description => "ثبت پیش‌فاکتور پیش‌نویس نامه داخلی جدید (اجرا فقط بعد از تأیید کاربر). پیش‌نویس در کارتابل ذخیره می‌شود تا بعداً گیرنده بگیرد و ارسال شود.";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            title = new { type = "string", description = "عنوان نامه (اجباری)" },
            text = new { type = "string", description = "متن نامه (اختیاری؛ می‌تواند خالی بماند تا بعداً نوشته شود)" },
        },
        required = new[] { "title" },
    };

    public async Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx)
    {
        var actions = ctx.Services.GetRequiredService<AiActionService>();
        var title = (AiToolArgs.GetString(args, "title") ?? "").Trim();
        if (title == "")
            return JsonSerializer.Serialize(new { error = "bad_input", message = "عنوان نامه لازم است." });
        var text = AiToolArgs.GetString(args, "text") ?? "";
        var summary = $"پیش‌نویس نامه «{title}»";
        var pending = await actions.CreateAsync(ctx.UserId, "create_letter_draft",
            JsonSerializer.Serialize(new { title, text }), summary, ctx.CancellationToken);
        return JsonSerializer.Serialize(new { action_id = pending.Id, summary, hint = "برای اجرا، کاربر باید بنویسد: تأیید" });
    }
}

// ---------------- هوش مدیریتی (فقط خواندنی) ----------------

/// <summary>کنترل دسترسی گزارش‌های مدیریتی — هر گزارش مجوز Read همان ماژول سامانه را می‌خواهد.</summary>
internal static class AiBiAccess
{
    public static async Task<string?> DeniedJsonAsync(AiToolContext ctx, string module, string reportName)
    {
        var db = ctx.Services.GetRequiredService<AppDbContext>();
        var role = await db.Users.AsNoTracking()
            .Where(u => u.Id == ctx.UserId).Select(u => u.Role).FirstOrDefaultAsync(ctx.CancellationToken);
        if (await AiAccessHelper.UserHasAsync(db, ctx.UserId, module, "Read", role, ctx.CancellationToken))
            return null;
        return JsonSerializer.Serialize(new
        {
            error = "access_denied",
            message = $"به گزارش «{reportName}» دسترسی نداری؛ اگر لازمش داری از مدیر سیستم بخواه. (مجوز: {module})",
        });
    }
}

/// <summary>تبدیل دوره فارسی به بازه میلادی (امروز/این هفته/این ماه/ماه گذشته/امسال یا بازه دلخواه).</summary>
internal static class AiBiPeriod
{
    public static (DateTime from, DateTime to, string label) Resolve(string? period, string? fromRaw, string? toRaw)
    {
        var today = DateTime.Today;
        var f = AiLeaveHelper.ParseFaDate(fromRaw, today);
        var t = AiLeaveHelper.ParseFaDate(toRaw, today);
        if (f != null || t != null)
        {
            var from = (f ?? t)!.Value.Date;
            var to = (t ?? f)!.Value.Date;
            if (to < from) (from, to) = (to, from);
            return (from, to, $"{AiDateUtil.ToFaShort(from)} تا {AiDateUtil.ToFaShort(to)}");
        }
        var p = AiTextUtil.NormalizeFa(period);
        var pc = new System.Globalization.PersianCalendar();
        if (p.Contains("امسال") || p == "year")
        {
            var from = pc.ToDateTime(pc.GetYear(today), 1, 1, 0, 0, 0, 0);
            return (from, today, "امسال");
        }
        if (p.Contains("ماه گذشته") || p.Contains("ماه قبل") || p == "last_month")
        {
            var y = pc.GetYear(today); var m = pc.GetMonth(today);
            var pm = m == 1 ? 12 : m - 1; var py = m == 1 ? y - 1 : y;
            var from = pc.ToDateTime(py, pm, 1, 0, 0, 0, 0);
            var to = pc.ToDateTime(py, pm, pc.GetDaysInMonth(py, pm), 0, 0, 0, 0);
            return (from, to, "ماه گذشته");
        }
        if (p.Contains("ماه") || p == "month")
        {
            var from = pc.ToDateTime(pc.GetYear(today), pc.GetMonth(today), 1, 0, 0, 0, 0);
            return (from, today, "این ماه");
        }
        if (p.Contains("هفته") || p == "week")
        {
            var daysSinceSaturday = (((int)today.DayOfWeek) + 1) % 7; // هفته از شنبه شروع می‌شود
            return (today.AddDays(-daysSinceSaturday), today, "این هفته");
        }
        return (today, today, "امروز");
    }
}

public class SalesSummaryTool : IAiTool
{
    public string Name => "sales_summary";
    public string Description => "خلاصه فروش و خرید یک دوره: جمع و تعداد فروش/خرید، برگشتی، سود ناخالص تقریبی، دریافتنی/پرداختنی، پرفروش‌ترین کالاها و بزرگ‌ترین طرف‌ها. دوره را می‌توانی فارسی بدهی: امروز، این هفته، این ماه، ماه گذشته، امسال.";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            period = new { type = "string", description = "دوره: امروز، این هفته، این ماه، ماه گذشته، امسال (پیش‌فرض: این ماه)" },
            from_date = new { type = "string", description = "شروع بازه دلخواه (مثل ۱۴۰۴/۰۷/۰۱) — جایگزین period" },
            to_date = new { type = "string", description = "پایان بازه دلخواه" },
        },
    };

    public async Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx)
    {
        var denied = await AiBiAccess.DeniedJsonAsync(ctx, "FacInvoices", "فروش و خرید");
        if (denied != null) return denied;
        var period = AiToolArgs.GetString(args, "period");
        if (string.IsNullOrWhiteSpace(period) && AiToolArgs.GetString(args, "from_date") == null)
            period = "این ماه";
        var (from, to, label) = AiBiPeriod.Resolve(period,
            AiToolArgs.GetString(args, "from_date"), AiToolArgs.GetString(args, "to_date"));
        var inv = ctx.Services.GetRequiredService<IInvoicingService>();
        var d = await inv.GetDashboardAsync(from, to);
        return JsonSerializer.Serialize(new
        {
            دوره = label,
            فروش_جمع = d.SaleTotal,
            فروش_تعداد = d.SaleCount,
            خرید_جمع = d.PurchaseTotal,
            برگشتی_فروش = d.SaleReturnTotal,
            سود_ناخالص_تقریبی = d.GrossProfit,
            دریافتنی = d.Receivable,
            پرداختنی = d.Payable,
            پیش_نویس_باز = d.DraftCount,
            پرفروش_ترین_کالاها = d.TopProducts.Select(r => new { عنوان = r.Title, تعداد = r.Count, مبلغ = r.Net }),
            بزرگ_ترین_طرف_ها = d.TopParties.Select(r => new { عنوان = r.Title, تعداد = r.Count, مبلغ = r.Net }),
        });
    }
}

public class RecentInvoicesTool : IAiTool
{
    public string Name => "recent_invoices";
    public string Description => "آخرین فاکتورهای قطعی‌شده (فروش یا خرید): شماره، طرف حساب، تاریخ، مبلغ، نقد/نسیه و سررسید.";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            kind = new { type = "string", description = "فروش، خرید یا همه (پیش‌فرض: فروش)" },
            limit = new { type = "integer", description = "تعداد (پیش‌فرض ۵، حداکثر ۱۵)" },
        },
    };

    public async Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx)
    {
        var denied = await AiBiAccess.DeniedJsonAsync(ctx, "FacInvoices", "فاکتورها");
        if (denied != null) return denied;
        var k = AiTextUtil.NormalizeFa(AiToolArgs.GetString(args, "kind"));
        InvoiceKind? kind = k.Contains("خرید") ? InvoiceKind.Purchase : k.Contains("همه") ? null : InvoiceKind.Sale;
        var limit = Math.Clamp(AiToolArgs.GetInt(args, "limit") ?? 5, 1, 15);
        var inv = ctx.Services.GetRequiredService<IInvoicingService>();
        var page = await inv.GetInvoicesAsync(kind, InvoiceStatus.Confirmed, null, null, null, null, null, 1, limit);
        return JsonSerializer.Serialize(page.Items.Select(i => new
        {
            شماره = i.Number,
            نوع = i.Kind == InvoiceKind.Sale ? "فروش" : i.Kind == InvoiceKind.Purchase ? "خرید" : "برگشتی",
            طرف = i.PartyName,
            تاریخ = AiDateUtil.ToFaShort(i.Date),
            مبلغ = i.TotalNet,
            تسویه = i.Settlement == SettlementType.Credit ? "نسیه" : "نقد",
            سررسید = i.DueDate == null ? null : AiDateUtil.ToFaShort(i.DueDate.Value),
        }));
    }
}

public class StockStatusTool : IAiTool
{
    public string Name => "stock_status";
    public string Description => "موجودی انبار. با search (نام یا کد کالا) = موجودی آن کالاها در همه انبارها؛ بدون search = فقط کالاهای زیر نقطه سفارش (کمبودها).";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            search = new { type = "string", description = "نام یا کد کالا (اختیاری؛ خالی = فقط کمبودها)" },
            limit = new { type = "integer", description = "تعداد (پیش‌فرض ۱۰، حداکثر ۲۰)" },
        },
    };

    public async Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx)
    {
        var denied = await AiBiAccess.DeniedJsonAsync(ctx, "Products", "موجودی انبار");
        if (denied != null) return denied;
        var search = (AiToolArgs.GetString(args, "search") ?? "").Trim();
        var limit = Math.Clamp(AiToolArgs.GetInt(args, "limit") ?? 10, 1, 20);
        var wh = ctx.Services.GetRequiredService<IWarehousingService>();
        var belowOnly = search == "";
        var page = await wh.GetStockAsync(null, null, belowOnly ? null : search, belowOnly, 1, limit);
        return JsonSerializer.Serialize(page.Items.Select(r => new
        {
            کد = r.ProductCode,
            کالا = r.ProductName,
            انبار = r.WarehouseName,
            موجودی = r.Quantity,
            واحد = r.Unit,
            نقطه_سفارش = r.ReorderPoint,
            وضعیت = r.BelowReorder ? "کمبود" : "موجود",
        }));
    }
}

public class ChequesDueTool : IAiTool
{
    public string Name => "cheques_due";
    public string Description => "چک‌های باز نزدیک سررسید (دریافتی و صادره، شامل معوق‌ها): شماره، نوع، طرف، مبلغ، بانک، سررسید + جمع دریافتی/صادره.";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            days = new { type = "integer", description = "تا چند روز آینده (پیش‌فرض ۷، حداکثر ۹۰)" },
            kind = new { type = "string", description = "دریافتی، صادره یا همه (پیش‌فرض: همه)" },
        },
    };

    public async Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx)
    {
        var denied = await AiBiAccess.DeniedJsonAsync(ctx, "TrsCheques", "چک‌ها");
        if (denied != null) return denied;
        var k = AiTextUtil.NormalizeFa(AiToolArgs.GetString(args, "kind"));
        ChequeKind? kind = k.Contains("دریافت") ? ChequeKind.Received
            : k.Contains("صادر") || k.Contains("پرداخت") ? ChequeKind.Issued : null;
        var days = Math.Clamp(AiToolArgs.GetInt(args, "days") ?? 7, 0, 90);
        var today = DateTime.Today;
        var trs = ctx.Services.GetRequiredService<ITreasuryService>();
        var page = await trs.GetChequesAsync(kind, null, null, null, null,
            today.AddDays(-60), today.AddDays(days), true, 1, 50);
        var items = page.Items
            .Select(c => new
            {
                شماره = c.Number,
                نوع = c.Kind == ChequeKind.Received ? "دریافتی" : "صادره",
                طرف = c.PartyName ?? c.OwnerName,
                مبلغ = c.Amount,
                بانک = c.BankName,
                سررسید = AiDateUtil.ToFaShort(c.DueDate),
                وضعیت = c.DueDate.Date < today ? $"معوق ({(today - c.DueDate.Date).Days} روز)"
                    : c.DueDate.Date == today ? "امروز" : $"{(c.DueDate.Date - today).Days} روز مانده",
            })
            .Take(20).ToList();
        return JsonSerializer.Serialize(new
        {
            تعداد_کل = page.TotalCount,
            جمع_دریافتی = page.Items.Where(c => c.Kind == ChequeKind.Received).Sum(c => c.Amount),
            جمع_صادره = page.Items.Where(c => c.Kind == ChequeKind.Issued).Sum(c => c.Amount),
            چک_ها = items,
        });
    }
}

public class TopDebtorsTool : IAiTool
{
    public string Name => "top_debtors";
    public string Description => "بدهکاران بزرگ: طرف‌های با بیشترین جمع فاکتور نسیه (منهای برگشتی) — دقیقاً با همان تعریف «دریافتنی» خود سامانه.";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            limit = new { type = "integer", description = "تعداد (پیش‌فرض ۱۰، حداکثر ۲۰)" },
        },
    };

    public async Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx)
    {
        var denied = await AiBiAccess.DeniedJsonAsync(ctx, "FacInvoices", "مطالبات");
        if (denied != null) return denied;
        var limit = Math.Clamp(AiToolArgs.GetInt(args, "limit") ?? 10, 1, 20);
        var svc = ctx.Services.GetRequiredService<AiReportService>();
        var rows = await svc.GetDebtorRowsAsync(limit, ctx.CancellationToken);
        return JsonSerializer.Serialize(rows.Select(r => new
        {
            طرف = r.Name,
            جمع_نسیه = r.Total,
            تعداد_فاکتور = r.Count,
        }));
    }
}

public class CashStatusTool : IAiTool
{
    public string Name => "cash_status";
    public string Description => "وضعیت نقدینگی: جمع موجودی صندوق‌ها و بانک‌ها، مانده هر حساب، و گردش دوره (جمع دریافت/پرداخت).";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            period = new { type = "string", description = "دوره گردش: امروز، این هفته، این ماه، امسال (پیش‌فرض: این ماه)" },
        },
    };

    public async Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx)
    {
        var denied = await AiBiAccess.DeniedJsonAsync(ctx, "TrsAccounts", "صندوق و بانک");
        if (denied != null) return denied;
        var period = AiToolArgs.GetString(args, "period");
        if (string.IsNullOrWhiteSpace(period)) period = "این ماه";
        var (from, to, label) = AiBiPeriod.Resolve(period, null, null);
        var trs = ctx.Services.GetRequiredService<ITreasuryService>();
        var d = await trs.GetDashboardAsync(from, to);
        return JsonSerializer.Serialize(new
        {
            دوره = label,
            جمع_کل = d.TotalBalance,
            صندوق = d.TotalCashBalance,
            بانک = d.TotalBankBalance,
            حساب_ها = d.Accounts.Select(a => new
            {
                نام = a.Name,
                نوع = a.Kind switch
                {
                    TreasuryAccountKind.Cash => "صندوق",
                    TreasuryAccountKind.Bank => "بانک",
                    TreasuryAccountKind.Pos => "کارتخوان",
                    _ => "تنخواه",
                },
                مانده = a.Balance,
                بانک = a.BankName,
            }),
            دریافت_دوره = d.PeriodIn,
            پرداخت_دوره = d.PeriodOut,
            تعداد_دریافت = d.ReceiptCount,
            تعداد_پرداخت = d.PaymentCount,
        });
    }
}

public class BuildReportTool : IAiTool
{
    public string Name => "build_report";
    public string Description => "ساخت گزارش تحلیلی با خروجی اکسل. وقتی کاربر «گزارش» خواست، گفت «به تفکیک ...»، یا جدول کامل/فایل اکسل لازم داشت. datasets: فروش (sales)، موجودی (stock)، چک‌ها (cheques)، بدهکاران (debtors). خروجی: report_id + پیش‌نمایش ۱۵ سطر اول. شناسه گزارش را به کاربر نشان نده؛ فقط جدول را خلاصه کن و بگو فایل اکسل کامل با دکمه زیر پیام قابل دانلود است.";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            dataset = new { type = "string", description = "نوع گزارش: فروش، موجودی، چک، بدهکاران (اجباری)" },
            period = new { type = "string", description = "دوره (فروش/چک): امروز، این هفته، این ماه، ماه گذشته، امسال (پیش‌فرض: این ماه)" },
            from_date = new { type = "string", description = "شروع بازه دلخواه" },
            to_date = new { type = "string", description = "پایان بازه دلخواه" },
            group_by = new { type = "string", description = "تفکیک گزارش فروش: طرف/مشتری، کالا، ماه، روز (پیش‌فرض: طرف)" },
            kind = new { type = "string", description = "فروش: فروش/خرید — چک: دریافتی/صادره (پیش‌فرض: فروش / همه)" },
            search = new { type = "string", description = "جستجوی کالا (موجودی؛ خالی = فقط کمبودها)" },
            days = new { type = "integer", description = "چک‌ها تا چند روز آینده (پیش‌فرض ۷)" },
        },
        required = new[] { "dataset" },
    };

    public async Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx)
    {
        var ds = AiTextUtil.NormalizeFa(AiToolArgs.GetString(args, "dataset"));
        var svc = ctx.Services.GetRequiredService<AiReportService>();
        AiReportPreview preview;
        if (ds.Contains("موجودی") || ds.Contains("انبار") || ds.Contains("کالا") || ds == "stock")
        {
            var denied = await AiBiAccess.DeniedJsonAsync(ctx, "Products", "موجودی انبار");
            if (denied != null) return denied;
            preview = await svc.BuildStockAsync(ctx.UserId,
                AiToolArgs.GetString(args, "search"), ctx.CancellationToken);
        }
        else if (ds.Contains("چک") || ds == "cheques")
        {
            var denied = await AiBiAccess.DeniedJsonAsync(ctx, "TrsCheques", "چک‌ها");
            if (denied != null) return denied;
            var k = AiTextUtil.NormalizeFa(AiToolArgs.GetString(args, "kind"));
            ChequeKind? kind = k.Contains("دریافت") ? ChequeKind.Received
                : k.Contains("صادر") || k.Contains("پرداخت") ? ChequeKind.Issued : null;
            var days = Math.Clamp(AiToolArgs.GetInt(args, "days") ?? 7, 0, 90);
            preview = await svc.BuildChequesAsync(ctx.UserId, kind, days, ctx.CancellationToken);
        }
        else if (ds.Contains("بدهکار") || ds.Contains("مطالبات") || ds == "debtors")
        {
            var denied = await AiBiAccess.DeniedJsonAsync(ctx, "FacInvoices", "مطالبات");
            if (denied != null) return denied;
            preview = await svc.BuildDebtorsAsync(ctx.UserId, ctx.CancellationToken);
        }
        else if (ds.Contains("فروش") || ds.Contains("خرید") || ds.Contains("فاکتور")
            || ds is "sales" or "fac")
        {
            var denied = await AiBiAccess.DeniedJsonAsync(ctx, "FacInvoices", "فروش و خرید");
            if (denied != null) return denied;
            var period = AiToolArgs.GetString(args, "period");
            if (string.IsNullOrWhiteSpace(period) && AiToolArgs.GetString(args, "from_date") == null)
                period = "این ماه";
            var (from, to, label) = AiBiPeriod.Resolve(period,
                AiToolArgs.GetString(args, "from_date"), AiToolArgs.GetString(args, "to_date"));
            var kind = AiTextUtil.NormalizeFa(AiToolArgs.GetString(args, "kind")).Contains("خرید")
                ? InvoiceKind.Purchase : InvoiceKind.Sale;
            preview = await svc.BuildSalesAsync(ctx.UserId, kind,
                AiToolArgs.GetString(args, "group_by") ?? "طرف", from, to, label, ctx.CancellationToken);
        }
        else
        {
            return JsonSerializer.Serialize(new
            {
                error = "bad_dataset",
                message = "نوع گزارش مشخص نیست؛ یکی از: فروش، موجودی، چک، بدهکاران.",
            });
        }

        return JsonSerializer.Serialize(new
        {
            report_id = preview.ReportId,
            title = preview.Title,
            columns = preview.Columns,
            rows = preview.Rows,
            total_row = preview.TotalRow,
            total_rows = preview.TotalRows,
            hint = $"این جدول فقط {preview.Rows.Count} سطر اول از {preview.TotalRows} سطر است؛ فایل اکسل کامل با دکمه دانلود زیر پیام.",
        });
    }
}

// ---------------- لایه داده حکمرانی‌شده (§۱۵) ----------------

public class DataCatalogTool : IAiTool
{
    public string Name => "data_catalog";
    public string Description => "راهنمای داده‌های سامانه: بدون ورودی، لیست همه موجودیت‌های قابل جستجو را می‌دهد؛ با دادن entity، فیلدها و مقادیر مجاز آن موجودیت را نشان می‌دهد. قبل از اولین explore_data روی یک موجودیت ناآشنا، حتماً این را صدا بزن.";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            entity = new { type = "string", description = "نام موجودیت (مثل invoice) برای دیدن فیلدهایش؛ خالی = لیست همه" },
        },
    };

    public Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx)
    {
        _ = ctx;
        var name = AiToolArgs.GetString(args, "entity");
        if (string.IsNullOrWhiteSpace(name))
            return Task.FromResult(JsonSerializer.Serialize(new
            {
                entities = AiDataCatalog.Entities.Select(e => new { name = e.Name, title = e.Fa, hint = e.Hint }),
                hint = "برای دیدن فیلدهای یک موجودیت، دوباره با entity صدا بزن.",
            }));
        var ent = AiDataCatalog.Find(name);
        if (ent == null)
            return Task.FromResult(JsonSerializer.Serialize(new
            {
                error = "unknown_entity",
                message = $"موجودیت «{name}» وجود ندارد؛ نام‌های معتبر: {string.Join("، ", AiDataCatalog.Entities.Select(e => e.Name))}",
            }));
        return Task.FromResult(JsonSerializer.Serialize(new
        {
            name = ent.Name,
            title = ent.Fa,
            hint = ent.Hint,
            fields = ent.Fields.Select(f => new
            {
                name = f.Name,
                title = f.Fa,
                kind = f.Kind,
                values = f.MapFa?.Values.ToList(),
                sensitive = f.Sensitive ? true : null as bool?,
            }),
            ops = "eq, neq, gt, gte, lt, lte, contains, starts, between, in, period (فقط تاریخ)",
            dates = "روز دقیق: ۱۴۰۴/۰۷/۰۵، امروز، دیروز، فردا. بازه نسبی با op=period: این هفته، هفته گذشته، این ماه، ماه گذشته، امسال، پارسال، ۷ روز گذشته",
        }));
    }
}

public class ExploreDataTool : IAiTool
{
    public string Name => "explore_data";
    public string Description => "جستجوی آزاد در داده‌های سامانه (فقط خواندن). entity: کلید موجودیت از data_catalog. مثال: (entity=invoice) فیلتر Date با op=period و value=این ماه، و Kind=فروش. فیلترها: field (نام فیلد)، op (eq,neq,gt,gte,lt,lte,contains,starts,between,in,period)، value و value2 برای between. تاریخ دقیق شمسی بده (۱۴۰۴/۰۷/۰۵، امروز، دیروز)؛ برای بازه نسبی (این هفته، هفته گذشته، این ماه، ماه گذشته، امسال، پارسال، ۷ روز گذشته) حتماً op=period بگذار. مقادیر enum را فارسی بده (مثل برگشتی، قطعی، دریافتی). group_by + agg(count/sum/avg) + agg_field برای «به تفکیک». order_by + desc برای مرتب‌سازی. limit حداکثر ۵۰. excel=true اگر کاربر فایل/اکسل/گزارش کامل خواست — آن‌وقت report_id برمی‌گردد و دکمه دانلود خودکار زیر پیام می‌آید.";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            entity = new { type = "string", description = "کلید موجودیت (اجباری)" },
            filters = new
            {
                type = "array",
                description = "فیلترها: هر کدام field و op و value (و value2 برای between)",
                items = new
                {
                    type = "object",
                    properties = new
                    {
                        field = new { type = "string" },
                        op = new { type = "string", description = "eq, neq, gt, gte, lt, lte, contains, starts, between, in, period (بازه نسبی تاریخ)" },
                        value = new { type = "string" },
                        value2 = new { type = "string" },
                    },
                },
            },
            select = new { type = "array", description = "فیلدهای نمایشی (اختیاری؛ پیش‌فرض همه)", items = new { type = "string" } },
            group_by = new { type = "string", description = "فیلد گروه‌بندی برای «به تفکیک»" },
            agg = new { type = "string", description = "count (پیش‌فرض)، sum، avg" },
            agg_field = new { type = "string", description = "فیلد تجمیع برای sum/avg (اجباری در آن حالت)" },
            order_by = new { type = "string", description = "فیلد مرتب‌سازی (پیش‌فرض کد)" },
            desc = new { type = "boolean", description = "نزولی؟ (پیش‌فرض true)" },
            limit = new { type = "integer", description = "تعداد سطر پیش‌نمایش (پیش‌فرض ۲۰، حداکثر ۵۰)" },
            excel = new { type = "boolean", description = "true اگر کاربر فایل اکسل خواست" },
        },
    };

    public async Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx)
    {
        var req = new AiExploreRequest
        {
            Entity = AiToolArgs.GetString(args, "entity") ?? "",
            GroupBy = AiToolArgs.GetString(args, "group_by"),
            Agg = AiToolArgs.GetString(args, "agg"),
            AggField = AiToolArgs.GetString(args, "agg_field"),
            OrderBy = AiToolArgs.GetString(args, "order_by"),
            Desc = AiToolArgs.GetBool(args, "desc", true),
            Limit = AiToolArgs.GetInt(args, "limit") ?? 20,
        };
        if (args.ValueKind == JsonValueKind.Object)
        {
            if (args.TryGetProperty("filters", out var fv) && fv.ValueKind == JsonValueKind.Array)
                foreach (var it in fv.EnumerateArray())
                {
                    if (it.ValueKind != JsonValueKind.Object) continue;
                    req.Filters.Add(new AiExploreFilter
                    {
                        Field = it.TryGetProperty("field", out var a) ? a.ToString() : "",
                        Op = it.TryGetProperty("op", out var b) ? b.ToString() : "eq",
                        Value = it.TryGetProperty("value", out var c) ? c.ToString() : null,
                        Value2 = it.TryGetProperty("value2", out var d) ? d.ToString() : null,
                    });
                }
            if (args.TryGetProperty("select", out var sv) && sv.ValueKind == JsonValueKind.Array)
                req.Select = sv.EnumerateArray().Select(x => x.ToString()).Where(s => s != "").ToList();
        }

        var explorer = ctx.Services.GetRequiredService<AiDataExplorer>();
        var (ok, error, result) = await explorer.QueryAsync(ctx.UserId, req, ctx.CancellationToken);
        if (!ok || result == null)
            return JsonSerializer.Serialize(new { error = "explore_failed", message = error });

        var wantExcel = AiToolArgs.GetBool(args, "excel");
        string? reportId = null;
        string? title = null;
        if (wantExcel && result.Rows.Count > 0)
        {
            var ent = AiDataCatalog.Find(req.Entity);
            title = $"کاوش {ent?.Fa ?? req.Entity} — {AiDateUtil.ToFaShort(DateTime.Today)}";
            var svc = ctx.Services.GetRequiredService<AiReportService>();
            var preview = await svc.BuildCustomAsync(ctx.UserId, title, result.Columns, result.Rows);
            reportId = preview.ReportId;
        }

        var previewRows = result.Rows.Take(req.Limit > 0 ? Math.Min(req.Limit, AiDataExplorer.MaxLimit) : 20).ToList();
        return JsonSerializer.Serialize(new
        {
            columns = result.Columns.Select(c => c.Title),
            rows = previewRows,
            total_count = result.TotalCount,
            shown = previewRows.Count,
            truncated = result.Truncated,
            notice = result.Notice,
            report_id = reportId,
            title,
            hint = reportId != null
                ? $"این جدول فقط {previewRows.Count} سطر اول است؛ فایل اکسل کامل با دکمه دانلود زیر پیام."
                : previewRows.Count < result.TotalCount
                    ? $"فقط {previewRows.Count} سطر اول از {result.TotalCount} سطر نشان داده شد؛ اگر همه را خواست با excel=true دوباره صدا بزن."
                    : null as string,
        });
    }
}

// ---------------- هشدارهای هوشمند (§۱۷) ----------------

public class MyAlertsTool : IAiTool
{
    public string Name => "my_alerts";
    public string Description => "هشدارهای امروز کاربر: کمبود انبار، چک برگشتی، پیش‌فاکتور قدیمی، تیکت جدید، قرارداد رو به اتمام، یادآوری گزارش‌کار. هر بخش فقط با مجوز همان ماژول برمی‌گردد.";
    public object ParametersSchema => new { type = "object", properties = new { } };

    public async Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx)
    {
        _ = args;
        var svc = ctx.Services.GetRequiredService<AiAlertsService>();
        var alerts = await svc.GetAlertsAsync(ctx.UserId, ctx.CancellationToken);
        return JsonSerializer.Serialize(new
        {
            count = alerts.Count,
            alerts = alerts.Select(a => new
            {
                icon = a.Icon,
                title = a.Title,
                lines = a.Lines,
                link = a.Link,
                link_text = a.LinkText,
            }),
        });
    }
}

// ---------------- اقدام‌های اجرایی موج دوم (§۱۰) ----------------

public class ReferLetterTool : IAiTool
{
    public string Name => "refer_letter";
    public string Description => "ثبت پیش‌فاکتور ارجاع نامه به همکار (اجرا فقط بعد از تأیید کاربر). نامه را با letter_id بده (از my_letters_inbox یا کاوش)؛ گیرنده را با نام یا شناسه (اگر نام مبهم بود، ابزار نامزدها را می‌دهد).";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            letter_id = new { type = "integer", description = "شناسه نامه (اجباری)" },
            receiver = new { type = "string", description = "نام همکار یا شناسه عددی او (اجباری)" },
            text = new { type = "string", description = "متن ارجاع/پاراف (اجباری)" },
            deadline = new { type = "string", description = "مهلت پاسخ: ۱۴۰۴/۰۷/۱۰ یا «فردا» (اختیاری)" },
        },
        required = new[] { "letter_id", "receiver", "text" },
    };

    public async Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx)
    {
        var db = ctx.Services.GetRequiredService<AppDbContext>();
        var letterId = AiToolArgs.GetInt(args, "letter_id") ?? 0;
        var receiverRaw = (AiToolArgs.GetString(args, "receiver") ?? "").Trim();
        var text = (AiToolArgs.GetString(args, "text") ?? "").Trim();
        if (letterId <= 0 || receiverRaw == "" || text == "")
            return JsonSerializer.Serialize(new { error = "bad_input", message = "نامه، گیرنده و متن ارجاع هر سه لازم است." });
        var letter = await db.InnerLetters.AsNoTracking()
            .Where(x => x.Id == letterId && !x.IsDelete)
            .Select(x => new { x.Title }).FirstOrDefaultAsync(ctx.CancellationToken);
        if (letter == null)
            return JsonSerializer.Serialize(new { error = "bad_letter", message = $"نامه‌ای با شناسه {letterId} پیدا نشد." });

        // حل گیرنده: عدد → شناسه؛ نام → جستجو با مدیریت ابهام
        int receiverId = 0;
        string receiverName = "";
        if (int.TryParse(AiTextUtil.ToEnDigits(receiverRaw), out var rid))
        {
            var u = await db.Users.AsNoTracking()
                .Where(x => x.Id == rid && x.IsActive && x.Username != Data.AiSeeder.AiUsername)
                .Select(x => new { x.FirstName, x.LastName, x.Username }).FirstOrDefaultAsync(ctx.CancellationToken);
            if (u == null)
                return JsonSerializer.Serialize(new { error = "bad_receiver", message = $"کاربری با شناسه {rid} پیدا نشد." });
            receiverId = rid;
            receiverName = (((u.FirstName ?? "") + " " + (u.LastName ?? "")).Trim() is { Length: > 0 } nm) ? nm : u.Username;
        }
        else
        {
            var q = AiTextUtil.NormalizeFa(receiverRaw);
            var matches = await db.Users.AsNoTracking()
                .Where(x => x.IsActive && x.Username != Data.AiSeeder.AiUsername)
                .Select(x => new { x.Id, x.FirstName, x.LastName, x.Username }).Take(200).ToListAsync(ctx.CancellationToken);
            var found = matches
                .Select(x => new { x.Id, Name = (((x.FirstName ?? "") + " " + (x.LastName ?? "")).Trim() is { Length: > 0 } nm) ? nm : x.Username })
                .Where(x => AiTextUtil.NormalizeFa(x.Name).Contains(q))
                .Take(8).ToList();
            if (found.Count == 0)
                return JsonSerializer.Serialize(new { error = "bad_receiver", message = $"کاربری با نام «{receiverRaw}» پیدا نشد؛ با users_lookup دقیقش کن." });
            if (found.Count > 1)
                return JsonSerializer.Serialize(new
                {
                    error = "ambiguous_receiver",
                    message = "چند نفر با این نام پیدا شد؛ شناسه دقیق را بپرس.",
                    candidates = found.Select(x => new { id = x.Id, name = x.Name }),
                });
            receiverId = found[0].Id;
            receiverName = found[0].Name;
        }

        var deadlineRaw = AiToolArgs.GetString(args, "deadline");
        DateTime? deadline = string.IsNullOrWhiteSpace(deadlineRaw) ? null : AiLeaveHelper.ParseFaDate(deadlineRaw, DateTime.Today)?.Date;
        if (!string.IsNullOrWhiteSpace(deadlineRaw) && deadline == null)
            return JsonSerializer.Serialize(new { error = "bad_date", message = $"مهلت «{deadlineRaw}» را نفهمیدم؛ مثل ۱۴۰۴/۰۷/۱۰ بنویس." });

        var summary = $"ارجاع نامه «{letter.Title}» به {receiverName}" +
            (deadline != null ? $" — مهلت {AiDateUtil.ToFaShort(deadline.Value)}" : "");
        var actions = ctx.Services.GetRequiredService<AiActionService>();
        var pending = await actions.CreateAsync(ctx.UserId, "refer_letter",
            JsonSerializer.Serialize(new
            {
                letter_id = letterId, receiver_id = receiverId, text,
                deadline = deadline?.ToString("yyyy-MM-dd"),
            }), summary, ctx.CancellationToken);
        return JsonSerializer.Serialize(new { action_id = pending.Id, summary, hint = "برای اجرا، کاربر باید بنویسد: تأیید" });
    }
}

public class AnswerTicketTool : IAiTool
{
    public string Name => "answer_ticket";
    public string Description => "ثبت پیش‌فاکتور پاسخ به تیکت (اجرا فقط بعد از تأیید کاربر). کارمند روی تیکت خودش، و HR روی هر تیکتی می‌تواند جواب بدهد.";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            ticket_id = new { type = "integer", description = "شناسه تیکت (اجباری)" },
            body = new { type = "string", description = "متن پاسخ (اجباری)" },
        },
        required = new[] { "ticket_id", "body" },
    };

    public async Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx)
    {
        var db = ctx.Services.GetRequiredService<AppDbContext>();
        var ticketId = AiToolArgs.GetInt(args, "ticket_id") ?? 0;
        var body = (AiToolArgs.GetString(args, "body") ?? "").Trim();
        if (ticketId <= 0 || body == "")
            return JsonSerializer.Serialize(new { error = "bad_input", message = "شناسه تیکت و متن پاسخ هر دو لازم است." });
        var t = await db.FaComTickets.AsNoTracking()
            .Where(x => x.Id == ticketId)
            .Select(x => new { x.Subject, x.Status, x.EmployeeId }).FirstOrDefaultAsync(ctx.CancellationToken);
        if (t == null)
            return JsonSerializer.Serialize(new { error = "bad_ticket", message = $"تیکتی با شناسه {ticketId} پیدا نشد." });
        if (t.Status == FaComTicketStatus.Closed)
            return JsonSerializer.Serialize(new { error = "closed", message = "این تیکت بسته است؛ برای پیگیری مجدد تیکت جدید ثبت کن." });
        var role = await db.Users.AsNoTracking()
            .Where(u => u.Id == ctx.UserId).Select(u => u.Role).FirstOrDefaultAsync(ctx.CancellationToken);
        var isHr = await AiAccessHelper.UserHasAsync(db, ctx.UserId, "FaCom", "Manage", role, ctx.CancellationToken);
        if (!isHr)
        {
            var myEmp = await db.HrEmployees.AsNoTracking()
                .Where(e => e.SystemUserId == ctx.UserId).Select(e => e.Id).FirstOrDefaultAsync(ctx.CancellationToken);
            if (myEmp == 0 || myEmp != t.EmployeeId)
                return JsonSerializer.Serialize(new { error = "access_denied", message = "به این تیکت دسترسی نداری." });
        }
        var summary = $"پاسخ به تیکت «{t.Subject}» (#{ticketId})" + (isHr ? " — به‌عنوان HR" : "");
        var actions = ctx.Services.GetRequiredService<AiActionService>();
        var pending = await actions.CreateAsync(ctx.UserId, "answer_ticket",
            JsonSerializer.Serialize(new { ticket_id = ticketId, body }), summary, ctx.CancellationToken);
        return JsonSerializer.Serialize(new { action_id = pending.Id, summary, hint = "برای اجرا، کاربر باید بنویسد: تأیید" });
    }
}

public class RegisterChequeTool : IAiTool
{
    public string Name => "register_cheque";
    public string Description => "ثبت پیش‌فاکتور چک دریافتی/صادره (اجرا فقط بعد از تأیید کاربر). چک فقط «نزد ما» ثبت می‌شود و سند حسابداری نمی‌زند. برای چک صادره، حساب بانکی صادرکننده لازم است (با نام یا شناسه از trs_account). طرف حساب اختیاری است (با نام یا شناسه).";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            kind = new { type = "string", description = "دریافتی یا صادره (اجباری)" },
            number = new { type = "string", description = "شماره چک (اجباری)" },
            amount = new { type = "string", description = "مبلغ به تومان با رقم، مثل ۱۲٬۵۰۰٬۰۰۰ (اجباری)" },
            due_date = new { type = "string", description = "سررسید شمسی، مثل ۱۴۰۴/۰۸/۱۵ (اجباری)" },
            issue_date = new { type = "string", description = "تاریخ صدور (اختیاری؛ پیش‌فرض امروز)" },
            bank = new { type = "string", description = "نام بانک (اختیاری)" },
            owner = new { type = "string", description = "صاحب چک (اختیاری)" },
            party = new { type = "string", description = "طرف حساب: نام یا شناسه (اختیاری)" },
            account = new { type = "string", description = "حساب بانکی: نام یا شناسه — برای صادره اجباری (اختیاری برای دریافتی)" },
            description = new { type = "string", description = "توضیح (اختیاری)" },
        },
        required = new[] { "kind", "number", "amount", "due_date" },
    };

    public async Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx)
    {
        var db = ctx.Services.GetRequiredService<AppDbContext>();
        var k = AiTextUtil.NormalizeFa(AiToolArgs.GetString(args, "kind"));
        var kind = k.Contains("صادر") || k.Contains("پرداخت") ? 1 : k.Contains("دریافت") ? 0 : -1;
        if (kind < 0)
            return JsonSerializer.Serialize(new { error = "bad_input", message = "نوع چک مشخص نیست؛ «دریافتی» یا «صادره»؟" });
        var number = AiTextUtil.ToEnDigits(AiToolArgs.GetString(args, "number") ?? "").Trim();
        if (number == "")
            return JsonSerializer.Serialize(new { error = "bad_input", message = "شماره چک لازم است." });
        var amount = ParseAmount(AiToolArgs.GetString(args, "amount"));
        if (amount == null || amount <= 0 || amount > 999_999_999_999_999m)
            return JsonSerializer.Serialize(new { error = "bad_input", message = "مبلغ معتبر نیست؛ مبلغ را با رقم بنویس، مثل ۱۲٬۵۰۰٬۰۰۰." });
        var due = AiLeaveHelper.ParseFaDate(AiToolArgs.GetString(args, "due_date"), DateTime.Today)?.Date;
        if (due == null)
            return JsonSerializer.Serialize(new { error = "bad_input", message = "سررسید معتبر نیست؛ مثل ۱۴۰۴/۰۸/۱۵ بنویس." });
        var issueRaw = AiToolArgs.GetString(args, "issue_date");
        var issue = string.IsNullOrWhiteSpace(issueRaw) ? DateTime.Today
            : AiLeaveHelper.ParseFaDate(issueRaw, DateTime.Today)?.Date;
        if (issue == null)
            return JsonSerializer.Serialize(new { error = "bad_input", message = "تاریخ صدور معتبر نیست." });

        // طرف حساب (اختیاری)
        var partyId = 0;
        string? partyName = null;
        var partyRaw = (AiToolArgs.GetString(args, "party") ?? "").Trim();
        if (partyRaw != "")
        {
            var pr = await ResolvePartyAsync(db, partyRaw, ctx.CancellationToken);
            if (!pr.ok) return pr.json!;
            partyId = pr.id;
            partyName = pr.name;
        }

        // حساب بانکی (برای صادره اجباری)
        var accountId = 0;
        string? accountName = null;
        var accountRaw = (AiToolArgs.GetString(args, "account") ?? "").Trim();
        if (accountRaw != "")
        {
            var ar = await ResolveAccountAsync(db, accountRaw, ctx.CancellationToken);
            if (!ar.ok) return ar.json!;
            accountId = ar.id;
            accountName = ar.name;
        }
        if (kind == 1 && accountId <= 0)
            return JsonSerializer.Serialize(new { error = "bad_input", message = "برای چک صادره، حساب بانکی صادرکننده لازم است." });

        var dup = await db.TrsCheques.AsNoTracking()
            .AnyAsync(c => c.Kind == (ChequeKind)kind && c.Number == number, ctx.CancellationToken);
        var kindFa = kind == 1 ? "صادره" : "دریافتی";
        var summary = $"چک {kindFa} شماره {number} — {AiTextUtil.ToFaDigits(amount.Value.ToString("#,##0"))} تومان — سررسید {AiDateUtil.ToFaShort(due.Value)}" +
            (partyName != null ? $" — طرف: {partyName}" : "") +
            (accountName != null ? $" — حساب: {accountName}" : "") +
            (dup ? " ⚠️ (تذکر: چکی با همین شماره قبلاً ثبت شده)" : "");
        var actions = ctx.Services.GetRequiredService<AiActionService>();
        var pending = await actions.CreateAsync(ctx.UserId, "register_cheque",
            JsonSerializer.Serialize(new
            {
                kind,
                number,
                amount = amount.Value,
                due_date = due.Value.ToString("yyyy-MM-dd"),
                issue_date = issue.Value.ToString("yyyy-MM-dd"),
                bank = (AiToolArgs.GetString(args, "bank") ?? "").Trim(),
                owner = (AiToolArgs.GetString(args, "owner") ?? "").Trim(),
                party_id = partyId,
                account_id = accountId,
                description = (AiToolArgs.GetString(args, "description") ?? "").Trim(),
            }), summary, ctx.CancellationToken);
        return JsonSerializer.Serialize(new { action_id = pending.Id, summary, hint = "برای اجرا، کاربر باید بنویسد: تأیید" });
    }

    private static decimal? ParseAmount(string? raw)
    {
        var s = AiTextUtil.ToEnDigits(raw ?? "").Trim()
            .Replace("تومان", "").Replace("تومان", "").Replace("ریال", "")
            .Replace("٬", "").Replace(",", "").Replace(" ", "").Replace(" ", "");
        return decimal.TryParse(s, System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    private static async Task<(bool ok, int id, string? name, string? json)> ResolvePartyAsync(
        AppDbContext db, string raw, CancellationToken ct)
    {
        if (int.TryParse(AiTextUtil.ToEnDigits(raw), out var id))
        {
            var p = await db.Parties.AsNoTracking()
                .Where(x => x.Id == id).Select(x => new { x.Id, x.Name }).FirstOrDefaultAsync(ct);
            if (p == null)
                return (false, 0, null, JsonSerializer.Serialize(new { error = "bad_party", message = $"طرف حسابی با شناسه {id} پیدا نشد." }));
            return (true, p.Id, p.Name, null);
        }
        var q = AiTextUtil.NormalizeFa(raw);
        var all = await db.Parties.AsNoTracking().Select(x => new { x.Id, x.Name }).Take(500).ToListAsync(ct);
        var found = all.Where(x => AiTextUtil.NormalizeFa(x.Name).Contains(q)).Take(8).ToList();
        if (found.Count == 0)
            return (false, 0, null, JsonSerializer.Serialize(new { error = "bad_party", message = $"طرف حسابی با نام «{raw}» پیدا نشد." }));
        if (found.Count > 1)
            return (false, 0, null, JsonSerializer.Serialize(new
            {
                error = "ambiguous_party",
                message = "چند طرف حساب با این نام پیدا شد؛ شناسه دقیق را بپرس.",
                candidates = found.Select(x => new { id = x.Id, name = x.Name }),
            }));
        return (true, found[0].Id, found[0].Name, null);
    }

    private static async Task<(bool ok, int id, string? name, string? json)> ResolveAccountAsync(
        AppDbContext db, string raw, CancellationToken ct)
    {
        if (int.TryParse(AiTextUtil.ToEnDigits(raw), out var id))
        {
            var a = await db.TrsAccounts.AsNoTracking()
                .Where(x => x.Id == id && x.IsActive).Select(x => new { x.Id, x.Name }).FirstOrDefaultAsync(ct);
            if (a == null)
                return (false, 0, null, JsonSerializer.Serialize(new { error = "bad_account", message = $"حساب فعالی با شناسه {id} پیدا نشد." }));
            return (true, a.Id, a.Name, null);
        }
        var q = AiTextUtil.NormalizeFa(raw);
        var all = await db.TrsAccounts.AsNoTracking()
            .Where(x => x.IsActive).Select(x => new { x.Id, x.Name }).Take(100).ToListAsync(ct);
        var found = all.Where(x => AiTextUtil.NormalizeFa(x.Name).Contains(q)).Take(8).ToList();
        if (found.Count == 0)
            return (false, 0, null, JsonSerializer.Serialize(new { error = "bad_account", message = $"حساب فعالی با نام «{raw}» پیدا نشد." }));
        if (found.Count > 1)
            return (false, 0, null, JsonSerializer.Serialize(new
            {
                error = "ambiguous_account",
                message = "چند حساب با این نام پیدا شد؛ شناسه دقیق را بپرس.",
                candidates = found.Select(x => new { id = x.Id, name = x.Name }),
            }));
        return (true, found[0].Id, found[0].Name, null);
    }
}

public class WeeklyDigestTool : IAiTool
{
    public string Name => "weekly_digest";
    public string Description => "خلاصه هفته مدیر: جمع‌بندی ۷ روز گذشته (فروش و خرید، منابع انسانی، تیکت‌ها، ارجاع‌ها، چک‌های هفته آینده، گزارش‌کارها و یادآوری‌ها). فقط مدیران (مجوز FaAtt.Manage).";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new { },
    };

    public async Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx)
    {
        var db = ctx.Services.GetRequiredService<AppDbContext>();
        var me = await db.Users.AsNoTracking()
            .Where(u => u.Id == ctx.UserId)
            .Select(u => new { u.FirstName, u.LastName, u.Username, u.Role })
            .FirstOrDefaultAsync(ctx.CancellationToken);
        if (me == null || !await AiAccessHelper.UserHasAsync(db, ctx.UserId, "FaAtt", "Manage", me.Role, ctx.CancellationToken))
            return JsonSerializer.Serialize(new { error = "access_denied", message = "خلاصه هفته فقط برای مدیران است." });
        var name = ((me.FirstName ?? "") + " " + (me.LastName ?? "")).Trim();
        if (name == "") name = me.Username;
        var digest = ctx.Services.GetRequiredService<AiDigestService>();
        var text = await digest.BuildDigestAsync(ctx.UserId, name, ctx.CancellationToken);
        return JsonSerializer.Serialize(new { digest = text });
    }
}

// ---------------- یادآور شخصی (§۲۰) ----------------

public class SetReminderTool : IAiTool
{
    public string Name => "set_reminder";
    public string Description => "ثبت یادآور شخصی (بدون نیاز به تأیید؛ ثبت همان انجام است). زمان را فارسی آزاد بده: «فردا ساعت ۹»، «هر روز ساعت ۸ صبح»، «جمعه ساعت ۵ عصر»، «۱۴۰۴/۰۷/۱۰ ساعت ۱۰»، «۲۰ دقیقه دیگه». تکرار (هر روز/هر هفته) را هم از متن می‌فهمد.";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            when = new { type = "string", description = "زمان فارسی (اجباری)" },
            text = new { type = "string", description = "متن یادآوری (اجباری)" },
            repeat = new { type = "string", description = "تکرار: none/daily/weekly یا یکبار/روزانه/هفتگی (اختیاری؛ اگر در when «هر روز» بود خودکار فهمیده می‌شود)" },
        },
        required = new[] { "when", "text" },
    };

    public async Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx)
    {
        var when = AiToolArgs.GetString(args, "when") ?? "";
        var text = AiToolArgs.GetString(args, "text") ?? "";
        var (at, recAuto) = AiReminderTime.Parse(when, DateTime.Now);
        if (at == null)
            return JsonSerializer.Serialize(new { error = "bad_date", message = "زمان را نفهمیدم؛ مثلاً «فردا ساعت ۹»، «هر روز ساعت ۸ صبح»، «جمعه ساعت ۵ عصر» یا «۲۰ دقیقه دیگه» بنویس." });
        var rec = ParseRepeat(AiToolArgs.GetString(args, "repeat"), recAuto);
        var svc = ctx.Services.GetRequiredService<AiReminderService>();
        var (r, err) = await svc.CreateAsync(ctx.UserId, text, at.Value, rec, ctx.CancellationToken);
        if (r == null)
        {
            var msg = err switch
            {
                "empty" => "متن یادآوری خالی است.",
                "past" => "این زمان گذشته! یک زمان در آینده بگو.",
                "far" => "حداکثر تا یک سال آینده می‌توانی یادآور بگذاری.",
                _ => "۲۰ یادآور فعال داری؛ اول با my_reminders ببین و با cancel_reminder یکی را لغو کن.",
            };
            return JsonSerializer.Serialize(new { error = err, message = msg });
        }
        var db = ctx.Services.GetRequiredService<AppDbContext>();
        var linked = await db.Users.AsNoTracking().Where(u => u.Id == ctx.UserId)
            .AnyAsync(u => u.BaleChatId != null || u.EitaaChatId != null, ctx.CancellationToken);
        return JsonSerializer.Serialize(new
        {
            reminder_id = r.Id,
            remind_at = AiReminderService.FaDateTime(r.RemindAt),
            repeat = AiReminderService.RepeatFa(r.Recurrence),
            note = linked ? null : "حسابت به بله/ایتا لینک نیست؛ برای دریافت سر وقت باید لینکش کنی.",
        });
    }

    private static int ParseRepeat(string? raw, int auto)
    {
        var v = AiTextUtil.NormalizeFa(raw ?? "").Trim();
        if (v == "") return auto;
        if (v.Contains("روزانه") || v.Contains("daily")) return 1;
        if (v.Contains("هفتگی") || v.Contains("weekly")) return 2;
        if (v.Contains("یکبار") || v.Contains("none")) return 0;
        return auto;
    }
}

public class MyRemindersTool : IAiTool
{
    public string Name => "my_reminders";
    public string Description => "فهرست یادآورهای فعال خودت (شناسه، متن، زمان شمسی، تکرار).";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new { },
    };

    public async Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx)
    {
        var svc = ctx.Services.GetRequiredService<AiReminderService>();
        var list = await svc.ListPendingAsync(ctx.UserId, ctx.CancellationToken);
        if (list.Count == 0)
            return JsonSerializer.Serialize(new { reminders = new object[0], message = "یادآور فعالی نداری." });
        return JsonSerializer.Serialize(new
        {
            reminders = list.Select(r => new
            {
                id = r.Id,
                text = AiTextUtil.Truncate(r.Text, 80),
                at = AiReminderService.FaDateTime(r.RemindAt),
                repeat = AiReminderService.RepeatFa(r.Recurrence),
            }),
        });
    }
}

public class CancelReminderTool : IAiTool
{
    public string Name => "cancel_reminder";
    public string Description => "لغو یک یادآور فعال با شناسه (از my_reminders).";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            reminder_id = new { type = "integer", description = "شناسه یادآور (اجباری)" },
        },
        required = new[] { "reminder_id" },
    };

    public async Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx)
    {
        var id = AiToolArgs.GetInt(args, "reminder_id") ?? 0;
        if (id <= 0)
            return JsonSerializer.Serialize(new { error = "bad_input", message = "شناسه یادآور لازم است." });
        var svc = ctx.Services.GetRequiredService<AiReminderService>();
        var ok = await svc.CancelAsync(ctx.UserId, id, ctx.CancellationToken);
        if (!ok)
            return JsonSerializer.Serialize(new { error = "not_found", message = "یادآور فعالی با این شناسه پیدا نشد." });
        return JsonSerializer.Serialize(new { cancelled = id, message = "یادآور لغو شد. ✅" });
    }
}

public class DecideLeaveBulkTool : IAiTool
{
    public string Name => "decide_leave_bulk";
    public string Description => "ثبت پیش‌فاکتور تأیید/رد گروهی چند مرخصی با فقط یک «تأیید» کاربر (اجرا فقط بعد از تأیید). شناسه‌ها را از pending_approvals بگیر (حداکثر ۲۰ تا).";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            leave_ids = new { type = "array", items = new { type = "integer" }, description = "شناسه مرخصی‌ها از pending_approvals (اجباری، حداکثر ۲۰)" },
            approve = new { type = "boolean", description = "true=تأیید همه، false=رد همه (اجباری)" },
        },
        required = new[] { "leave_ids", "approve" },
    };

    public async Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx)
    {
        var ids = new List<int>();
        if (args.TryGetProperty("leave_ids", out var arr) && arr.ValueKind == JsonValueKind.Array)
            foreach (var el in arr.EnumerateArray())
                if (el.TryGetInt32(out var n) && n > 0 && !ids.Contains(n)) ids.Add(n);
        if (ids.Count == 0)
            return JsonSerializer.Serialize(new { error = "bad_input", message = "شناسه مرخصی‌ها لازم است؛ اول pending_approvals را ببین." });
        if (ids.Count > 20)
            return JsonSerializer.Serialize(new { error = "too_many", message = "حداکثر ۲۰ مرخصی در هر بسته؛ بقیه را در بسته بعدی بفرست." });
        var approve = AiToolArgs.GetBool(args, "approve");
        var what = approve ? "تأیید" : "رد";

        var fa = ctx.Services.GetRequiredService<IFaAttService>();
        var team = await fa.TeamLeavesAsync(ctx.UserId);
        var names = new List<string>();
        var unknown = 0;
        foreach (var id in ids)
        {
            var l = team.FirstOrDefault(x => x.Id == id);
            if (l == null) { unknown++; continue; }
            names.Add(AiTextUtil.Truncate($"{l.EmployeeName} ({l.LeaveTypeName}، {AiDateUtil.ToFaShort(l.FromDate)})", 45));
        }
        var summary = $"{what} گروهی {AiTextUtil.ToFaDigits(ids.Count.ToString())} مرخصی" +
            (names.Count > 0 ? ": " + string.Join("، ", names.Take(3)) + (names.Count > 3 ? $"، +{AiTextUtil.ToFaDigits((names.Count - 3).ToString())} مورد دیگر" : "") : "") +
            (unknown > 0 ? $" (⚠️ {AiTextUtil.ToFaDigits(unknown.ToString())} مورد در فهرست درانتظار تو نیست؛ موقع اجرا بررسی می‌شود)" : "");
        var actions = ctx.Services.GetRequiredService<AiActionService>();
        var pending = await actions.CreateAsync(ctx.UserId, "decide_leave_bulk",
            JsonSerializer.Serialize(new { leave_ids = ids, approve }), summary, ctx.CancellationToken);
        return JsonSerializer.Serialize(new { action_id = pending.Id, summary, hint = "برای اجرا، کاربر باید بنویسد: تأیید" });
    }
}

// ---------------- صورتجلسه هوشمند ----------------

public class DraftMinutesTool : IAiTool
{
    public string Name => "draft_minutes";
    public string Description => "پیش‌نویس هوشمند صورتجلسه از متن خام جلسه (فقط پیش‌نمایش؛ ذخیره نمی‌کند). خروجی: عنوان، خلاصه و بندها با نوع (مصوبه/اقدام/اطلاع) + مسئول و مهلت پیشنهادی. برای ذخیره، بعدش create_minutes.";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            raw_text = new { type = "string", description = "متن خام جلسه (اجباری، حداقل ۱۰ نویسه)" },
            title = new { type = "string", description = "عنوان پیشنهادی جلسه (اختیاری)" },
        },
        required = new[] { "raw_text" },
    };

    public async Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx)
    {
        var db = ctx.Services.GetRequiredService<AppDbContext>();
        var role = await db.Users.AsNoTracking()
            .Where(u => u.Id == ctx.UserId).Select(u => u.Role).FirstOrDefaultAsync(ctx.CancellationToken);
        if (!await AiAccessHelper.UserHasAsync(db, ctx.UserId, "MeetingMinutes", "View", role, ctx.CancellationToken))
            return JsonSerializer.Serialize(new { error = "access_denied", message = "به صورتجلسه‌ها دسترسی نداری." });
        var raw = (AiToolArgs.GetString(args, "raw_text") ?? "").Trim();
        if (raw.Length < 10)
            return JsonSerializer.Serialize(new { error = "bad_input", message = "متن جلسه خیلی کوتاه است؛ خلاصه مذاکرات را بده." });
        var letters = ctx.Services.GetRequiredService<ILetterAiService>();
        var draft = await letters.DraftMinutesAsync(new AiMinutesDraftRequest
        {
            Title = AiToolArgs.GetString(args, "title") ?? "",
            RawText = raw,
        }, ctx.CancellationToken);
        return JsonSerializer.Serialize(new
        {
            title = draft.Title,
            summary = draft.Summary,
            items = draft.Items.Select((it, i) => new
            {
                n = i + 1,
                kind = it.Kind,
                text = it.Text,
                responsible = it.Responsible,
                deadline = it.DeadlineText,
            }),
            hint = "برای ذخیره: تاریخ جلسه و شناسه حاضران را بپرس (نام را با users_lookup به شناسه تبدیل کن)، بعد create_minutes بساز.",
        });
    }
}

public class CreateMinutesTool : IAiTool
{
    public string Name => "create_minutes";
    public string Description => "ثبت پیش‌فاکتور صورتجلسه جدید (اجرا فقط بعد از تأیید کاربر؛ ذخیره در وضعیت «در حال بررسی»). بندها را از draft_minutes بگیر. حاضران حداقل یک نفر با شناسه عددی کاربر (نام را اول با users_lookup به شناسه تبدیل کن). مسئول هر بند: نام یا شناسه.";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            title = new { type = "string", description = "عنوان صورتجلسه (اجباری)" },
            meeting_date = new { type = "string", description = "تاریخ جلسه شمسی، مثل ۱۴۰۴/۰۷/۰۸ یا «امروز» (اجباری)" },
            items = new
            {
                type = "array",
                description = "بندها (اجباری، ۱ تا ۳۰): هر بند text (اجباری)، responsible (نام یا شناسه، اختیاری)، due (مهلت شمسی، اختیاری)",
                items = new
                {
                    type = "object",
                    properties = new
                    {
                        text = new { type = "string" },
                        responsible = new { type = "string" },
                        due = new { type = "string" },
                    },
                    required = new[] { "text" },
                },
            },
            attendees = new { type = "array", items = new { type = "integer" }, description = "شناسه عددی حاضران (اجباری، حداقل یک نفر)" },
            absentees = new { type = "array", items = new { type = "integer" }, description = "شناسه عددی غایبان (اختیاری)" },
        },
        required = new[] { "title", "meeting_date", "items", "attendees" },
    };

    public async Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx)
    {
        var db = ctx.Services.GetRequiredService<AppDbContext>();
        var title = (AiToolArgs.GetString(args, "title") ?? "").Trim();
        if (title == "" || title.Length > 300)
            return JsonSerializer.Serialize(new { error = "bad_input", message = "عنوان صورتجلسه لازم است (حداکثر ۳۰۰ نویسه)." });
        var meetingDate = AiLeaveHelper.ParseFaDate(AiToolArgs.GetString(args, "meeting_date"), DateTime.Today)?.Date;
        if (meetingDate == null)
            return JsonSerializer.Serialize(new { error = "bad_date", message = "تاریخ جلسه را نفهمیدم؛ مثل ۱۴۰۴/۰۷/۰۸ یا «امروز» بنویس." });

        if (!args.TryGetProperty("items", out var itemsEl) || itemsEl.ValueKind != JsonValueKind.Array || itemsEl.GetArrayLength() == 0)
            return JsonSerializer.Serialize(new { error = "bad_input", message = "دست‌کم یک بند لازم است (از draft_minutes بگیر)." });
        var rawItems = itemsEl.EnumerateArray().ToList();
        if (rawItems.Count > 30)
            return JsonSerializer.Serialize(new { error = "too_many", message = "حداکثر ۳۰ بند در هر صورتجلسه." });
        var items = new List<object>();
        var withResp = 0;
        var n = 0;
        foreach (var it in rawItems)
        {
            n++;
            var text = it.ValueKind == JsonValueKind.Object && it.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
            if (text.Trim() == "")
                return JsonSerializer.Serialize(new { error = "bad_input", message = $"متن بند {n} خالی است." });
            var respId = 0;
            string? respRaw = null;
            if (it.ValueKind == JsonValueKind.Object && it.TryGetProperty("responsible", out var r))
                respRaw = r.ValueKind == JsonValueKind.String ? r.GetString() : r.ValueKind == JsonValueKind.Number && r.TryGetInt32(out var rn) ? rn.ToString() : null;
            if (!string.IsNullOrWhiteSpace(respRaw))
            {
                var rr = await ResolveUserAsync(db, respRaw!.Trim(), ctx.CancellationToken);
                if (!rr.ok)
                    return JsonSerializer.Serialize(new { error = rr.error, message = $"بند {n}: {rr.message}", candidates = rr.candidates });
                respId = rr.id;
                withResp++;
            }
            string? dueIso = null;
            if (it.ValueKind == JsonValueKind.Object && it.TryGetProperty("due", out var d) && d.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(d.GetString()))
            {
                var due = AiLeaveHelper.ParseFaDate(d.GetString(), DateTime.Today)?.Date;
                if (due == null)
                    return JsonSerializer.Serialize(new { error = "bad_date", message = $"مهلت بند {n} را نفهمیدم." });
                dueIso = due.Value.ToString("yyyy-MM-dd");
            }
            items.Add(new { text = text.Trim(), responsible_id = respId, due = dueIso });
        }

        var attendees = ReadIds(args, "attendees");
        if (attendees.Count == 0)
            return JsonSerializer.Serialize(new { error = "bad_input", message = "دست‌کم یک حاضر با شناسه عددی لازم است (نام را با users_lookup به شناسه تبدیل کن)." });
        var absentees = ReadIds(args, "absentees");
        var allIds = attendees.Concat(absentees).Distinct().ToList();
        var users = await db.Users.AsNoTracking()
            .Where(u => allIds.Contains(u.Id) && u.IsActive)
            .Select(u => new { u.Id, u.FirstName, u.LastName, u.Username }).ToListAsync(ctx.CancellationToken);
        var missing = allIds.Except(users.Select(u => u.Id)).ToList();
        if (missing.Count > 0)
            return JsonSerializer.Serialize(new { error = "bad_user", message = $"این شناسه‌ها کاربر فعال نیستند: {string.Join("، ", missing)}" });
        string NameOf(int id)
        {
            var u = users.First(x => x.Id == id);
            var nm = ((u.FirstName ?? "") + " " + (u.LastName ?? "")).Trim();
            return nm == "" ? u.Username : nm;
        }

        var summary = $"صورتجلسه «{title}» — جلسه {AiDateUtil.ToFaShort(meetingDate.Value)} — " +
            $"{AiTextUtil.ToFaDigits(items.Count.ToString())} بند ({AiTextUtil.ToFaDigits(withResp.ToString())} با مسئول) — " +
            $"حاضران: {string.Join("، ", attendees.Take(3).Select(NameOf))}" +
            (attendees.Count > 3 ? $" +{AiTextUtil.ToFaDigits((attendees.Count - 3).ToString())} نفر دیگر" : "");
        var actions = ctx.Services.GetRequiredService<AiActionService>();
        var pending = await actions.CreateAsync(ctx.UserId, "create_minutes",
            JsonSerializer.Serialize(new
            {
                title,
                meeting_date = meetingDate.Value.ToString("yyyy-MM-dd"),
                items,
                attendees,
                absentees,
            }), summary, ctx.CancellationToken);
        return JsonSerializer.Serialize(new { action_id = pending.Id, summary, hint = "برای اجرا، کاربر باید بنویسد: تأیید" });
    }

    private static List<int> ReadIds(JsonElement args, string prop)
    {
        var ids = new List<int>();
        if (args.TryGetProperty(prop, out var arr) && arr.ValueKind == JsonValueKind.Array)
            foreach (var el in arr.EnumerateArray())
                if (el.TryGetInt32(out var v) && v > 0 && !ids.Contains(v)) ids.Add(v);
        return ids;
    }

    private static async Task<(bool ok, int id, string? error, string? message, object? candidates)> ResolveUserAsync(
        AppDbContext db, string raw, CancellationToken ct)
    {
        if (int.TryParse(AiTextUtil.ToEnDigits(raw), out var id))
        {
            var ok = await db.Users.AsNoTracking().AnyAsync(u => u.Id == id && u.IsActive, ct);
            return ok ? (true, id, null, null, null)
                : (false, 0, "bad_user", $"کاربر فعالی با شناسه {id} نیست.", null);
        }
        var q = AiTextUtil.NormalizeFa(raw);
        var all = await db.Users.AsNoTracking()
            .Where(u => u.IsActive && u.Username != Data.AiSeeder.AiUsername)
            .Select(u => new { u.Id, u.FirstName, u.LastName, u.Username }).Take(200).ToListAsync(ct);
        var found = all
            .Select(u => new { u.Id, Name = (((u.FirstName ?? "") + " " + (u.LastName ?? "")).Trim() is { Length: > 0 } nm) ? nm : u.Username })
            .Where(u => AiTextUtil.NormalizeFa(u.Name).Contains(q)).Take(8).ToList();
        if (found.Count == 0)
            return (false, 0, "bad_user", $"کسی با نام «{raw}» پیدا نشد.", null);
        if (found.Count > 1)
            return (false, 0, "ambiguous_user", "چند نفر با این نام پیدا شد؛ شناسه دقیق را بپرس.",
                found.Select(u => new { id = u.Id, name = u.Name }));
        return (true, found[0].Id, null, null, null);
    }
}

public class MyMinutesActionsTool : IAiTool
{
    public string Name => "my_minutes_actions";
    public string Description => "بندهای باز صورتجلسات که مسئول اجرایشان خودتی (در جریان + سررسید) — برای پیگیری اقدام‌های خودت.";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            limit = new { type = "integer", description = "تعداد (پیش‌فرض ۱۰، حداکثر ۳۰)" },
        },
    };

    public async Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx)
    {
        var db = ctx.Services.GetRequiredService<AppDbContext>();
        var role = await db.Users.AsNoTracking()
            .Where(u => u.Id == ctx.UserId).Select(u => u.Role).FirstOrDefaultAsync(ctx.CancellationToken);
        if (!await AiAccessHelper.UserHasAsync(db, ctx.UserId, "MeetingMinutes", "View", role, ctx.CancellationToken))
            return JsonSerializer.Serialize(new { error = "access_denied", message = "به صورتجلسه‌ها دسترسی نداری." });
        var limit = Math.Clamp(AiToolArgs.GetInt(args, "limit") ?? 10, 1, 30);
        var rows = await db.MeetingMinutesItems.AsNoTracking()
            .Where(i => i.ResponsibleUserId == ctx.UserId && i.ItemStatus == MinutesItemStatus.InProgress
                && i.Minutes != null && !i.Minutes.IsDeleted)
            .OrderBy(i => i.DueDate == null).ThenBy(i => i.DueDate).ThenBy(i => i.RowNo)
            .Select(i => new { i.MinutesId, Title = i.Minutes!.Title, i.RowNo, i.Description, i.DueDate })
            .Take(limit).ToListAsync(ctx.CancellationToken);
        if (rows.Count == 0)
            return JsonSerializer.Serialize(new { actions = new object[0], message = "بند بازی به مسئولیت تو نیست. 🎉" });
        return JsonSerializer.Serialize(new
        {
            actions = rows.Select(r => new
            {
                minutes_id = r.MinutesId,
                minutes_title = r.Title,
                row_no = r.RowNo,
                text = AiTextUtil.Truncate(r.Description, 120),
                due = r.DueDate == null ? "—" : AiDateUtil.ToFaShort(r.DueDate.Value),
                link = $"/misc/minutes/{r.MinutesId}",
            }),
        });
    }
}

// ---------------- گزارش‌های زمان‌بندی‌شده (§۲۱) ----------------

public class ScheduleReportTool : IAiTool
{
    public string Name => "schedule_report";
    public string Description => "ثبت گزارش زمان‌بندی‌شده (بدون نیاز به تأیید). kind ‏bi = یکی از ۶ خلاصه آماده با period نسبی (متن فقط)؛ kind ‏explore = هر کاوش روی موجودیت‌ها با فیلتر/گروه‌بندی (متن + اکسل اختیاری). اگر کاربر اکسل خواست حتماً explore. زمان فارسی: «هر روز ساعت ۸»، «هر شنبه ساعت ۸»، «اول هر ماه ساعت ۹»، «آخر هر ماه».";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            kind = new { type = "string", description = "نوع: bi یا explore (اجباری)" },
            title = new { type = "string", description = "عنوان گزارش (اختیاری؛ خودکار ساخته می‌شود)" },
            schedule = new { type = "string", description = "دوره فارسی (اجباری): «هر روز ساعت ۸»، «هر شنبه ساعت ۸»، «اول هر ماه ساعت ۹»" },
            excel = new { type = "boolean", description = "فایل اکسل هم فرستاده شود؟ فقط برای explore" },
            report = new { type = "string", description = "برای bi: sales_summary/recent_invoices/stock_status/cheques_due/top_debtors/cash_status" },
            period = new { type = "string", description = "برای bi: دوره نسبی مثل «این هفته»، «این ماه»، «امروز»" },
            days = new { type = "integer", description = "برای cheques_due: بازه روز" },
            limit = new { type = "integer", description = "برای recent_invoices/top_debtors یا سقف سطر explore" },
            filter_kind = new { type = "string", description = "برای recent_invoices/cheques_due: نوع (فروش/خرید، دریافتی/صادره)" },
            search = new { type = "string", description = "برای stock_status: جستجوی کالا" },
            entity = new { type = "string", description = "برای explore: کلید موجودیت از data_catalog" },
            filters = new
            {
                type = "array",
                description = "برای explore: فیلترها {field, op, value, value2}",
                items = new { type = "object", properties = new { field = new { type = "string" }, op = new { type = "string" }, value = new { type = "string" }, value2 = new { type = "string" } } },
            },
            group_by = new { type = "string", description = "برای explore: فیلد گروه‌بندی" },
            agg = new { type = "string", description = "برای explore: count/sum/avg" },
            agg_field = new { type = "string", description = "برای explore: فیلد تجمیع" },
            order_by = new { type = "string", description = "برای explore: فیلد مرتب‌سازی" },
            desc = new { type = "boolean", description = "برای explore: نزولی؟" },
            select = new { type = "array", items = new { type = "string" }, description = "برای explore: ستون‌ها" },
        },
        required = new[] { "kind", "schedule" },
    };

    public async Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx)
    {
        var kindRaw = AiTextUtil.NormalizeFa(AiToolArgs.GetString(args, "kind") ?? "");
        var kind = kindRaw.Contains("کاوش") || kindRaw == "explore" ? "explore"
            : kindRaw.Contains("هوشمند") || kindRaw.Contains("خلاصه") || kindRaw == "bi" ? "bi" : "";
        if (kind == "")
            return JsonSerializer.Serialize(new { error = "bad_kind", message = "نوع گزارش مشخص نیست؛ bi (خلاصه آماده) یا explore (کاوش)؟" });
        var excel = AiToolArgs.GetBool(args, "excel");
        if (excel && kind == "bi")
            return JsonSerializer.Serialize(new { error = "bad_spec", message = "اکسل فقط برای کاوش است؛ برای فروش/چک با اکسل، روی موجودیت invoice/cheque کاوش (explore) بساز." });

        var (schedType, day, time) = AiScheduleTime.Parse(AiToolArgs.GetString(args, "schedule"), DateTime.Now);
        if (schedType == null)
            return JsonSerializer.Serialize(new { error = "bad_schedule", message = "دوره را نفهمیدم؛ مثلاً «هر روز ساعت ۸»، «هر شنبه ساعت ۸»، «اول هر ماه ساعت ۹» یا «آخر هر ماه» بنویس." });

        string specJson;
        string autoTitle;
        if (kind == "bi")
        {
            var report = (AiToolArgs.GetString(args, "report") ?? "").Trim();
            if (!AiScheduleService.BiReports.Contains(report))
                return JsonSerializer.Serialize(new { error = "bad_spec", message = "گزارش معتبر نیست؛ یکی از: sales_summary، ‏recent_invoices، ‏stock_status، ‏cheques_due، ‏top_debtors، ‏cash_status." });
            var bag = new Dictionary<string, object?>();
            foreach (var (k, v) in new[] { ("period", AiToolArgs.GetString(args, "period")), ("filter_kind", AiToolArgs.GetString(args, "filter_kind")), ("search", AiToolArgs.GetString(args, "search")) })
                if (!string.IsNullOrWhiteSpace(v)) bag[k == "filter_kind" ? "kind" : k] = v;
            var days = AiToolArgs.GetInt(args, "days");
            if (days != null) bag["days"] = days;
            var limit = AiToolArgs.GetInt(args, "limit");
            if (limit != null) bag["limit"] = limit;
            specJson = JsonSerializer.Serialize(new { report, args = bag });
            var period = AiToolArgs.GetString(args, "period");
            autoTitle = BiFa(report) + (string.IsNullOrWhiteSpace(period) ? "" : $" — {period}");
        }
        else
        {
            var entity = (AiToolArgs.GetString(args, "entity") ?? "").Trim();
            if (entity == "")
                return JsonSerializer.Serialize(new { error = "bad_spec", message = "موجودیت کاوش لازم است (از data_catalog)." });
            var req = new AiExploreRequest { Entity = entity };
            if (args.TryGetProperty("filters", out var fa) && fa.ValueKind == JsonValueKind.Array)
                foreach (var f in fa.EnumerateArray())
                {
                    if (f.ValueKind != JsonValueKind.Object) continue;
                    req.Filters.Add(new AiExploreFilter
                    {
                        Field = f.TryGetProperty("field", out var x) ? x.GetString() ?? "" : "",
                        Op = f.TryGetProperty("op", out var o) ? o.GetString() ?? "eq" : "eq",
                        Value = f.TryGetProperty("value", out var v) ? v.GetString() : null,
                        Value2 = f.TryGetProperty("value2", out var v2) ? v2.GetString() : null,
                    });
                }
            req.GroupBy = AiToolArgs.GetString(args, "group_by");
            req.Agg = AiToolArgs.GetString(args, "agg") ?? "count";
            req.AggField = AiToolArgs.GetString(args, "agg_field");
            req.OrderBy = AiToolArgs.GetString(args, "order_by");
            var desc = args.TryGetProperty("desc", out var dd) && dd.ValueKind == JsonValueKind.True;
            req.Desc = args.TryGetProperty("desc", out _) ? desc : true;
            req.Limit = Math.Clamp(AiToolArgs.GetInt(args, "limit") ?? 20, 1, 50);
            if (args.TryGetProperty("select", out var sv) && sv.ValueKind == JsonValueKind.Array)
                req.Select = sv.EnumerateArray().Select(x => x.ToString()).Where(s => s != "").ToList();
            specJson = JsonSerializer.Serialize(req);
            autoTitle = $"کاوش {AiDataCatalog.Find(entity)?.Fa ?? entity}";
        }

        var title = AiToolArgs.GetString(args, "title");
        var svc = ctx.Services.GetRequiredService<AiScheduleService>();
        var (sch, err, preview) = await svc.CreateAsync(ctx.UserId, string.IsNullOrWhiteSpace(title) ? autoTitle : title!,
            kind, specJson, schedType!, day, time, excel, ctx.Services, ctx.CancellationToken);
        if (sch == null)
        {
            var msg = err switch
            {
                "bad_kind" => "نوع گزارش معتبر نیست.",
                "bad_spec" => "مشخصات گزارش معتبر نیست.",
                "bad_schedule" => "دوره معتبر نیست.",
                "too_many" => "۱۰ زمان‌بندی فعال داری؛ اول با my_schedules ببین و با cancel_schedule یکی را لغو کن.",
                _ when err?.StartsWith("dry_failed:") == true => $"مشخصات اجرا نشد: {err["dry_failed:".Length..]}",
                _ => "ثبت نشد؛ دوباره تلاش کن.",
            };
            return JsonSerializer.Serialize(new { error = err, message = msg });
        }
        var db = ctx.Services.GetRequiredService<AppDbContext>();
        var linked = await db.Users.AsNoTracking().Where(u => u.Id == ctx.UserId)
            .AnyAsync(u => u.BaleChatId != null || u.EitaaChatId != null, ctx.CancellationToken);
        return JsonSerializer.Serialize(new
        {
            schedule_id = sch.Id,
            title = sch.Title,
            schedule = AiScheduleService.FaSchedule(sch.ScheduleType, sch.Day, sch.Time),
            next_run = AiScheduleService.FaDateTime(sch.NextRunAt),
            excel = sch.WantExcel,
            preview,
            note = linked ? null : "حسابت به بله/ایتا لینک نیست؛ برای دریافت باید لینکش کنی.",
        });
    }

    private static string BiFa(string report) => report switch
    {
        "sales_summary" => "فروش و خرید",
        "recent_invoices" => "آخرین فاکتورها",
        "stock_status" => "موجودی",
        "cheques_due" => "چک‌ها",
        "top_debtors" => "بدهکاران",
        _ => "نقدینگی",
    };
}

public class MySchedulesTool : IAiTool
{
    public string Name => "my_schedules";
    public string Description => "فهرست گزارش‌های زمان‌بندی‌شده فعال خودت (شناسه، عنوان، دوره، نوبت بعدی، اکسل).";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new { },
    };

    public async Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx)
    {
        var svc = ctx.Services.GetRequiredService<AiScheduleService>();
        var list = await svc.ListActiveAsync(ctx.UserId, ctx.CancellationToken);
        if (list.Count == 0)
            return JsonSerializer.Serialize(new { schedules = new object[0], message = "زمان‌بندی فعالی نداری." });
        return JsonSerializer.Serialize(new
        {
            schedules = list.Select(s => new
            {
                id = s.Id,
                title = s.Title,
                kind = s.Kind == "bi" ? "خلاصه آماده" : "کاوش",
                schedule = AiScheduleService.FaSchedule(s.ScheduleType, s.Day, s.Time),
                next_run = AiScheduleService.FaDateTime(s.NextRunAt),
                excel = s.WantExcel ? "بله" : "خیر",
            }),
        });
    }
}

public class CancelScheduleTool : IAiTool
{
    public string Name => "cancel_schedule";
    public string Description => "لغو یک گزارش زمان‌بندی‌شده با شناسه (از my_schedules).";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            schedule_id = new { type = "integer", description = "شناسه زمان‌بندی (اجباری)" },
        },
        required = new[] { "schedule_id" },
    };

    public async Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx)
    {
        var id = AiToolArgs.GetInt(args, "schedule_id") ?? 0;
        if (id <= 0)
            return JsonSerializer.Serialize(new { error = "bad_input", message = "شناسه زمان‌بندی لازم است." });
        var svc = ctx.Services.GetRequiredService<AiScheduleService>();
        var ok = await svc.CancelAsync(ctx.UserId, id, ctx.CancellationToken);
        if (!ok)
            return JsonSerializer.Serialize(new { error = "not_found", message = "زمان‌بندی فعالی با این شناسه پیدا نشد." });
        return JsonSerializer.Serialize(new { cancelled = id, message = "زمان‌بندی لغو شد. ✅" });
    }
}

// ---------------- جستجوی معنایی (§۲۲) ----------------

public class SearchDocsTool : IAiTool
{
    public string Name => "search_docs";
    public string Description => "جستجوی معنایی در نامه‌ها (داخلی/وارده/صادره) و تیکت‌های HR. query اجباری؛ scope اختیاری (letter/ticket/all یا «نامه»/«تیکت»)؛ فقط اسناد قابل‌مشاهده خود کاربر برمی‌گردد (نامه محرمانه/سری و حذف‌شده هرگز). اگر مدل معنایی خواب باشد، خودکار کلیدواژه‌ای می‌شود.";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            query = new { type = "string", description = "عبارت جستجو (اجباری)" },
            scope = new { type = "string", description = "محدوده: all (پیش‌فرض)، letter/نامه، ticket/تیکت" },
            limit = new { type = "integer", description = "تعداد نتیجه (۱ تا ۱۰، پیش‌فرض ۵)" },
        },
        required = new[] { "query" },
    };

    public async Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx)
    {
        var query = (AiToolArgs.GetString(args, "query") ?? "").Trim();
        if (query == "")
            return JsonSerializer.Serialize(new { error = "bad_input", message = "عبارت جستجو خالی است." });
        var svc = ctx.Services.GetRequiredService<AiSearchService>();
        var (matches, semantic) = await svc.SearchAsync(ctx.UserId, query,
            AiToolArgs.GetString(args, "scope"), AiToolArgs.GetInt(args, "limit") ?? 5, ctx.CancellationToken);
        if (matches.Count == 0)
            return JsonSerializer.Serialize(new
            {
                results = new object[0],
                mode = semantic ? "معنایی" : "کلیدواژه‌ای",
                message = "چیزی پیدا نشد؛ عبارت دیگری امتحان کن یا محدوده را عوض کن (نامه/تیکت).",
            });
        return JsonSerializer.Serialize(new
        {
            mode = semantic ? "معنایی" : "کلیدواژه‌ای",
            note = semantic ? null : "مدل معنایی در دسترس نبود؛ جستجوی کلیدواژه‌ای انجام شد.",
            results = matches.Select(m => new
            {
                type = AiSearchService.DocTypeFa(m.DocType),
                id = m.DocId,
                title = m.Title,
                snippet = m.Snippet,
                score = ((int)Math.Round(m.Score * 100)) + "٪",
                meta = m.Meta,
                link = m.Link,
            }),
        });
    }
}

public class ReindexDocsTool : IAiTool
{
    public string Name => "reindex_docs";
    public string Description => "ایندکس مجدد اسناد فاقد بردار یا تغییرکرده برای جستجوی معنایی (فقط مدیر سیستم). scope اختیاری (all/letter/ticket)؛ limit سقف تعداد (پیش‌فرض ۱۰۰).";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            scope = new { type = "string", description = "محدوده: all (پیش‌فرض)، letter، ticket" },
            limit = new { type = "integer", description = "سقف ایندکس (پیش‌فرض ۱۰۰، حداکثر ۵۰۰)" },
        },
    };

    public async Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx)
    {
        if (!ctx.IsAdmin)
            return JsonSerializer.Serialize(new { error = "forbidden", message = "ایندکس مجدد فقط برای مدیر سیستم است." });
        var svc = ctx.Services.GetRequiredService<AiSearchService>();
        var (indexed, failed, missing) = await svc.IndexMissingAsync(
            AiToolArgs.GetString(args, "scope"), AiToolArgs.GetInt(args, "limit") ?? 100, ctx.CancellationToken);
        return JsonSerializer.Serialize(new
        {
            indexed,
            failed,
            missing,
            message = missing == 0 ? "همه اسناد ایندکس‌اند. ✅"
                : failed > 0 ? $"از {missing} سند، {indexed} ایندکس شد و {failed} خطا خورد (مدل خواب است؟)."
                : $"{indexed} سند از {missing} ایندکس شد. ✅",
        });
    }
}

// ---------------- هشدارهای شرطی (§۲۳) ----------------

public class CreateAlertTool : IAiTool
{
    public string Name => "create_alert";
    public string Description => "ثبت قانون هشدار شرطی (بدون نیاز به تأیید): یک پرس‌وجوی کاوش روی ۲۶ موجودیت + آستانه. وقتی شرط برقرار شود (گذار به true) در بله/ایتا خبر می‌دهد. entity و op و value اجباری؛ agg: تعداد (پیش‌فرض)/جمع/میانگین؛ agg_field برای جمع/میانگین لازم است.";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            entity = new { type = "string", description = "کلید موجودیت از data_catalog (اجباری)" },
            op = new { type = "string", description = "عملگر (اجباری): بیشتر/کمتر/حداقل/حداکثر/برابر یا >/</>=/<=/=" },
            value = new { type = "number", description = "مقدار آستانه (اجباری)" },
            filters = new
            {
                type = "array",
                description = "فیلترها {field, op, value, value2}",
                items = new { type = "object", properties = new { field = new { type = "string" }, op = new { type = "string" }, value = new { type = "string" }, value2 = new { type = "string" } } },
            },
            agg = new { type = "string", description = "تجمیع: count/تعداد (پیش‌فرض)، sum/جمع، avg/میانگین" },
            agg_field = new { type = "string", description = "فیلد تجمیع برای جمع/میانگین (نام انگلیسی فیلد)" },
            title = new { type = "string", description = "عنوان قانون (اختیاری؛ خودکار ساخته می‌شود)" },
        },
        required = new[] { "entity", "op", "value" },
    };

    public async Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx)
    {
        var entity = (AiToolArgs.GetString(args, "entity") ?? "").Trim();
        if (entity == "")
            return JsonSerializer.Serialize(new { error = "bad_input", message = "موجودیت لازم است (از data_catalog)." });
        var op = AiAlertRuleService.NormalizeOp(AiToolArgs.GetString(args, "op"));
        if (op == null)
            return JsonSerializer.Serialize(new { error = "bad_op", message = "عملگر را نفهمیدم؛ مثلاً «بیشتر از»، «کمتر از»، «حداقل»، «حداکثر» یا «برابر»." });
        var value = AiToolArgs.GetDouble(args, "value");
        if (value == null)
            return JsonSerializer.Serialize(new { error = "bad_value", message = "مقدار آستانه لازم است (عدد)." });

        var aggRaw = AiTextUtil.NormalizeFa(AiToolArgs.GetString(args, "agg") ?? "").Trim();
        var agg = aggRaw.Contains("جمع") || aggRaw == "sum" ? "sum"
            : aggRaw.Contains("میانگین") || aggRaw == "avg" ? "avg" : "count";
        var aggField = AiToolArgs.GetString(args, "agg_field");
        if (agg != "count" && string.IsNullOrWhiteSpace(aggField))
            return JsonSerializer.Serialize(new { error = "bad_agg", message = "برای جمع/میانگین، فیلد تجمیع (agg_field) لازم است." });

        var req = new AiExploreRequest { Entity = entity };
        if (args.TryGetProperty("filters", out var fa) && fa.ValueKind == JsonValueKind.Array)
            foreach (var f in fa.EnumerateArray())
            {
                if (f.ValueKind != JsonValueKind.Object) continue;
                req.Filters.Add(new AiExploreFilter
                {
                    Field = f.TryGetProperty("field", out var x) ? x.GetString() ?? "" : "",
                    Op = f.TryGetProperty("op", out var o) ? o.GetString() ?? "eq" : "eq",
                    Value = f.TryGetProperty("value", out var v) ? v.GetString() : null,
                    Value2 = f.TryGetProperty("value2", out var v2) ? v2.GetString() : null,
                });
            }

        var entFa = AiDataCatalog.Find(entity)?.Fa ?? entity;
        var title = AiToolArgs.GetString(args, "title");
        if (string.IsNullOrWhiteSpace(title))
            title = $"{entFa} — {AiAlertRuleService.AggFa(agg)} {AiAlertRuleService.OpFa(op)} {AiAlertRuleService.FaNum(value.Value)}";

        var svc = ctx.Services.GetRequiredService<AiAlertRuleService>();
        var (rule, err, current, firesNow, approximate) = await svc.CreateAsync(
            ctx.UserId, title!, req, agg, aggField, op, value.Value, ctx.Services, ctx.CancellationToken);
        if (rule == null)
        {
            var msg = err switch
            {
                "bad_agg" => "برای جمع/میانگین، فیلد تجمیع لازم است.",
                "too_many" => "۱۰ قانون فعال داری؛ اول با my_alert_rules ببین و با delete_alert یکی را حذف کن.",
                _ when err?.StartsWith("dry_failed:") == true => $"پرس‌وجو اجرا نشد: {err["dry_failed:".Length..]}",
                _ => "ثبت نشد؛ دوباره تلاش کن.",
            };
            return JsonSerializer.Serialize(new { error = err, message = msg });
        }
        var db = ctx.Services.GetRequiredService<AppDbContext>();
        var linked = await db.Users.AsNoTracking().Where(u => u.Id == ctx.UserId)
            .AnyAsync(u => u.BaleChatId != null || u.EitaaChatId != null, ctx.CancellationToken);
        return JsonSerializer.Serialize(new
        {
            rule_id = rule.Id,
            title = rule.Title,
            condition = $"{AiAlertRuleService.AggFa(agg)} {AiAlertRuleService.OpFa(op)} {AiAlertRuleService.FaNum(value.Value)}",
            current = AiAlertRuleService.FaNum(current),
            state = firesNow ? "برقرار (اولین خبر در گذار بعدی)" : "نبرقرار",
            approximate = approximate ? "تقریبی (بیش از ۲۰۰۰ سطر)" : null,
            note = linked ? null : "حسابت به بله/ایتا لینک نیست؛ برای دریافت هشدار باید لینکش کنی.",
        });
    }
}

public class MyAlertRulesTool : IAiTool
{
    public string Name => "my_alert_rules";
    public string Description => "فهرست قانون‌های هشدار شرطی خودت (شناسه، عنوان، شرط، وضعیت برقرار/نبرقرار، آخرین خبر).";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new { },
    };

    public async Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx)
    {
        var svc = ctx.Services.GetRequiredService<AiAlertRuleService>();
        var list = await svc.ListActiveAsync(ctx.UserId, ctx.CancellationToken);
        if (list.Count == 0)
            return JsonSerializer.Serialize(new { rules = new object[0], message = "قانون هشدار فعالی نداری." });
        return JsonSerializer.Serialize(new
        {
            rules = list.Select(r => new
            {
                id = r.Id,
                title = r.Title,
                condition = $"{AiAlertRuleService.AggFa(r.Agg)} {AiAlertRuleService.OpFa(r.Op)} {AiAlertRuleService.FaNum(r.Value)}",
                state = r.LastState ? "برقرار" : "نبرقرار",
                last_fired = r.LastFiredAt == null ? "—" : AiDateUtil.ToFaShort(r.LastFiredAt.Value),
            }),
        });
    }
}

public class DeleteAlertTool : IAiTool
{
    public string Name => "delete_alert";
    public string Description => "حذف یک قانون هشدار شرطی با شناسه (از my_alert_rules).";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            alert_id = new { type = "integer", description = "شناسه قانون (اجباری)" },
        },
        required = new[] { "alert_id" },
    };

    public async Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx)
    {
        var id = AiToolArgs.GetInt(args, "alert_id") ?? 0;
        if (id <= 0)
            return JsonSerializer.Serialize(new { error = "bad_input", message = "شناسه قانون لازم است." });
        var svc = ctx.Services.GetRequiredService<AiAlertRuleService>();
        var ok = await svc.DeleteAsync(ctx.UserId, id, ctx.CancellationToken);
        if (!ok)
            return JsonSerializer.Serialize(new { error = "not_found", message = "قانون فعالی با این شناسه پیدا نشد." });
        return JsonSerializer.Serialize(new { deleted = id, message = "قانون هشدار حذف شد. ✅" });
    }
}

// ---------------- بازخورد پاسخ‌ها (§۲۴) ----------------

public class FeedbackStatsTool : IAiTool
{
    public string Name => "feedback_stats";
    public string Description => "آمار رضایت کاربران از پاسخ‌های دستیار (فقط مدیر سیستم): تعداد آرا، درصد رضایت، دلیل‌های نارضایتی، ابزارهای دخیل در پاسخ‌های ضعیف. days اختیاری (پیش‌فرض ۱۴).";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            days = new { type = "integer", description = "بازه روز (۱ تا ۹۰، پیش‌فرض ۱۴)" },
        },
    };

    public async Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx)
    {
        if (!ctx.IsAdmin)
            return JsonSerializer.Serialize(new { error = "forbidden", message = "آمار بازخورد فقط برای مدیر سیستم است." });
        var svc = ctx.Services.GetRequiredService<AiFeedbackService>();
        var st = await svc.StatsAsync(AiToolArgs.GetInt(args, "days") ?? 14, ctx.CancellationToken);
        if (st.Total == 0)
            return JsonSerializer.Serialize(new { message = "هنوز رأیی ثبت نشده است." });
        return JsonSerializer.Serialize(new
        {
            total = st.Total,
            up = st.Up,
            down = st.Down,
            satisfaction_pct = st.SatisfactionPct,
            by_reason = st.ByReason.Select(r => new { reason = r.Key, count = r.Count }),
            top_tools_down = st.TopToolsDown.Select(t => new { tool = t.Key, count = t.Count }),
            hint = "فهرست پاسخ‌های ضعیف در صفحه «بازبینی بازخوردها» (/ai-feedback).",
        });
    }
}

// ---------------- پنل حسابرسی (§۲۵) ----------------

public class AuditStatsTool : IAiTool
{
    public string Name => "audit_stats";
    public string Description => "آمار حسابرسی دستیار (فقط مدیر سیستم): تعداد نوبت‌ها، درصد موفقیت، پرکاربردترین ابزارها و کاربران، اقدام‌های تأییدی اجراشده/لغوشده. days اختیاری (پیش‌فرض ۱۴).";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            days = new { type = "integer", description = "بازه روز (۱ تا ۹۰، پیش‌فرض ۱۴)" },
        },
    };

    public async Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx)
    {
        if (!ctx.IsAdmin)
            return JsonSerializer.Serialize(new { error = "forbidden", message = "آمار حسابرسی فقط برای مدیر سیستم است." });
        var svc = ctx.Services.GetRequiredService<AiAuditService>();
        var st = await svc.StatsAsync(AiToolArgs.GetInt(args, "days") ?? 14, ctx.CancellationToken);
        if (st.Total == 0 && st.ActionsExecuted == 0 && st.ActionsPending == 0)
            return JsonSerializer.Serialize(new { message = "هنوز ردپایی ثبت نشده است." });
        return JsonSerializer.Serialize(new
        {
            turns = st.Total,
            failed = st.Failed,
            fallback = st.Fallback,
            success_pct = st.SuccessPct,
            actions_executed = st.ActionsExecuted,
            actions_rejected = st.ActionsRejected,
            actions_pending = st.ActionsPending,
            top_tools = st.TopTools.Select(t => new { tool = t.Key, count = t.Count }),
            top_users = st.TopUsers.Select(u => new { user = u.Key, count = u.Count }),
            hint = "فهرست جزئی نوبت‌ها در صفحه «حسابرسی دستیار» (/ai-audit).",
        });
    }
}
