using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

// ============================================================
//  خودتعمیرِ اسکیمای «مازادِ کارکرد» (تصمیم مدیر درباره‌ی ماندن بعد از پایان شیفت) — نسخه ۱
//  • EnsureCreated (SQLite) و Migrate (SQL Server) ستون‌های تازه را به دیتابیس‌های قدیمی
//    اضافه نمی‌کنند؛ اینجا به‌صورت امن و idempotent اضافه می‌شوند.
//  • ستون‌ها:
//      AttendanceRecords.PendingOvertimeMinutes      — جمع «مازادِ در انتظار تصمیم» روز
//      AttendanceSegments.PendingMinutes            — مازادِ همان بازه
//      AttendanceSegments.ExtraDecision             — ۰ در انتظار | ۱ تأیید(اضافه‌کار) | ۲ رد(غیرمجاز)
//      AttendanceSegments.ExtraDecidedByUserId       — چه کسی تصمیم گرفت
//      AttendanceSegments.ExtraDecidedAt            — چه زمانی
//      AttendanceSegments.ExtraDecisionNote         — یادداشت مدیر
//      ShiftGroups.RequireExtraApproval             — پرچم سه‌حالته (NULL = از تنظیمات سراسری)
//      WorkCalendarSettings.RequireExtraApproval    — رفتار پیش‌فرض مازاد
//      WorkCalendarSettings.ShiftOverridesCalendarTimes — اولویت ساعت شیفت بر ساعت تقویم
// ============================================================
public static class AttendanceExtraWorkSchemaV1
{
    public static Task EnsureAsync(AppDbContext db) =>
        db.Database.IsSqlite() ? EnsureSqliteAsync(db) : EnsureSqlServerAsync(db);

    // ==================== SQL Server ====================
    private static Task EnsureSqlServerAsync(AppDbContext db)
    {
        // AttendanceRecords
        Run(db, "AttendanceRecords.PendingOvertimeMinutes", "ستون PendingOvertimeMinutes",
            "IF OBJECT_ID(N'dbo.AttendanceRecords', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.AttendanceRecords', N'PendingOvertimeMinutes') IS NULL ALTER TABLE dbo.AttendanceRecords ADD PendingOvertimeMinutes int NOT NULL DEFAULT 0;");
        // AttendanceSegments
        Run(db, "AttendanceSegments.PendingMinutes", "ستون PendingMinutes",
            "IF OBJECT_ID(N'dbo.AttendanceSegments', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.AttendanceSegments', N'PendingMinutes') IS NULL ALTER TABLE dbo.AttendanceSegments ADD PendingMinutes int NOT NULL DEFAULT 0;");
        Run(db, "AttendanceSegments.ExtraDecision", "ستون ExtraDecision",
            "IF OBJECT_ID(N'dbo.AttendanceSegments', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.AttendanceSegments', N'ExtraDecision') IS NULL ALTER TABLE dbo.AttendanceSegments ADD ExtraDecision int NOT NULL DEFAULT 0;");
        Run(db, "AttendanceSegments.ExtraDecidedByUserId", "ستون ExtraDecidedByUserId",
            "IF OBJECT_ID(N'dbo.AttendanceSegments', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.AttendanceSegments', N'ExtraDecidedByUserId') IS NULL ALTER TABLE dbo.AttendanceSegments ADD ExtraDecidedByUserId int NULL;");
        Run(db, "AttendanceSegments.ExtraDecidedAt", "ستون ExtraDecidedAt",
            "IF OBJECT_ID(N'dbo.AttendanceSegments', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.AttendanceSegments', N'ExtraDecidedAt') IS NULL ALTER TABLE dbo.AttendanceSegments ADD ExtraDecidedAt datetime2 NULL;");
        Run(db, "AttendanceSegments.ExtraDecisionNote", "ستون ExtraDecisionNote",
            "IF OBJECT_ID(N'dbo.AttendanceSegments', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.AttendanceSegments', N'ExtraDecisionNote') IS NULL ALTER TABLE dbo.AttendanceSegments ADD ExtraDecisionNote nvarchar(300) NULL;");
        // ShiftGroups
        Run(db, "ShiftGroups.RequireExtraApproval", "ستون RequireExtraApproval",
            "IF OBJECT_ID(N'dbo.ShiftGroups', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.ShiftGroups', N'RequireExtraApproval') IS NULL ALTER TABLE dbo.ShiftGroups ADD RequireExtraApproval bit NULL;");
        // WorkCalendarSettings
        Run(db, "WorkCalendarSettings.RequireExtraApproval", "ستون RequireExtraApproval",
            "IF OBJECT_ID(N'dbo.WorkCalendarSettings', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.WorkCalendarSettings', N'RequireExtraApproval') IS NULL ALTER TABLE dbo.WorkCalendarSettings ADD RequireExtraApproval bit NOT NULL DEFAULT 1;");
        Run(db, "WorkCalendarSettings.ShiftOverridesCalendarTimes", "ستون ShiftOverridesCalendarTimes",
            "IF OBJECT_ID(N'dbo.WorkCalendarSettings', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.WorkCalendarSettings', N'ShiftOverridesCalendarTimes') IS NULL ALTER TABLE dbo.WorkCalendarSettings ADD ShiftOverridesCalendarTimes bit NOT NULL DEFAULT 1;");
        return Task.CompletedTask;
    }

    // ==================== SQLite ====================
    private static async Task EnsureSqliteAsync(AppDbContext db)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();

        async Task<List<string>> ColsOf(string table)
        {
            var list = new List<string>();
            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"SELECT name FROM pragma_table_info('{table}')";
                using var rd = await cmd.ExecuteReaderAsync();
                while (await rd.ReadAsync()) list.Add(rd.GetString(0));
            }
            catch { /* جدول نیست — EnsureCreated آن را ساخته است */ }
            return list;
        }

        async Task AddCol(string table, string column, string type, string? def = null)
        {
            var cols = await ColsOf(table);
            if (cols.Count == 0) return;            // جدول وجود ندارد
            if (cols.Contains(column)) return;      // از قبل هست
            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {type}{(def == null ? "" : $" DEFAULT {def}")}";
                await cmd.ExecuteNonQueryAsync();
                Console.WriteLine($"[DB] SQLite: ستون {column} به {table} اضافه شد.");
            }
            catch (Exception ex) { Console.WriteLine($"[DB] SQLite: ستون {column} → {ex.Message}"); }
        }

        await AddCol("AttendanceRecords", "PendingOvertimeMinutes", "INTEGER", "0");
        await AddCol("AttendanceSegments", "PendingMinutes", "INTEGER", "0");
        await AddCol("AttendanceSegments", "ExtraDecision", "INTEGER", "0");
        await AddCol("AttendanceSegments", "ExtraDecidedByUserId", "INTEGER");
        await AddCol("AttendanceSegments", "ExtraDecidedAt", "TEXT");
        await AddCol("AttendanceSegments", "ExtraDecisionNote", "TEXT");
        await AddCol("ShiftGroups", "RequireExtraApproval", "INTEGER");
        await AddCol("WorkCalendarSettings", "RequireExtraApproval", "INTEGER", "1");
        await AddCol("WorkCalendarSettings", "ShiftOverridesCalendarTimes", "INTEGER", "1");
    }

    private static void Run(AppDbContext db, string key, string label, string sql)
    {
        try { db.Database.ExecuteSqlRaw(sql); }
        catch (Exception ex) { Console.WriteLine($"[DB] AttendanceExtraWorkSchemaV1: {label} → {ex.Message}"); }
    }
}
