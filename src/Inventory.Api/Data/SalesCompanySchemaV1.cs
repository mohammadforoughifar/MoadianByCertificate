using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

// ============================================================
//  ماژول فروشگاهی چندشرکتی — خودتعمیرِ اسکیمای نسخه ۱
//
//  هدف: هر رکورد از اطلاعات پایه و عملیات فروشگاهی (کالا، گروه کالا، واحد،
//  انبار، طرف حساب، معرف، موجودی، فاکتور/سند خرید و فروش، بیجک، هزینه،
//  تعمیرات و چک/اقساط) به یک «شرکت» تعلق داشته باشد.
//
//  چرا این فایل لازم است؟
//   • مایگریشن Squashed روی دیتابیس موجود «stamp و رد» می‌شود و ستون‌های تازه
//     ساخته نمی‌شوند (همان الگوی بقیهٔ SchemaV1های پروژه).
//   • EnsureCreated هم روی دیتابیس SQLite موجود، ستون جدید اضافه نمی‌کند.
//
//  کارهایی که انجام می‌شود (idempotent و امن برای اجرای مکرر):
//   ۱) افزودن ستون CompanyId به جدول‌های ماژول (اگر نباشد)
//   ۲) انتساب رکوردهای قدیمی (CompanyId = NULL) به شرکت پیش‌فرض سیستم
//      تا هیچ داده‌ای بعد از ارتقا «گم» نشود
//   ۳) یکتا بودن کد/نام را از «کل سیستم» به «داخل هر شرکت» منتقل می‌کند
//      (ایندکس‌های یکتای قدیمی حذف و ایندکس‌های مرکب ساخته می‌شوند)
//   ۴) ایندکس شرکت روی جدول‌های پرتراکنش برای سرعت فیلتر
// ============================================================
public static class SalesCompanySchemaV1
{
    /// <summary>جدول‌های ماژول فروشگاهی که ستون CompanyId می‌گیرند.</summary>
    private static readonly string[] Tables =
    {
        "Products", "ProductCategories", "MeasureUnits", "Warehouses", "Parties",
        "Referrers", "ReferrerPayments", "Stocks",
        "Transactions", "TransactionLines",
        "GoodsIssues", "GoodsIssueLines",
        "Expenses", "ExpenseCategories",
        "Cheques", "Installments",
        "Technicians", "RepairOrders", "RepairDevices", "RepairItems"
    };

    /// <summary>ایندکس‌های یکتای قبلی و نسخهٔ مرکب (به تفکیک شرکت) آن‌ها.</summary>
    private static readonly (string Table, string OldIndex, string Column, string NewIndex)[] UniqueIndexes =
    {
        ("Products",          "IX_Products_Code",          "Code",   "IX_Products_CompanyId_Code"),
        ("ProductCategories", "IX_ProductCategories_Name", "Name",   "IX_ProductCategories_CompanyId_Name"),
        ("MeasureUnits",      "IX_MeasureUnits_Name",      "Name",   "IX_MeasureUnits_CompanyId_Name"),
        ("GoodsIssues",       "IX_GoodsIssues_Number",     "Number", "IX_GoodsIssues_CompanyId_Number"),
        ("ExpenseCategories", "IX_ExpenseCategories_Name", "Name",   "IX_ExpenseCategories_CompanyId_Name"),
    };

    /// <summary>جدول‌های پرتراکنشی که ایندکس سادهٔ CompanyId می‌گیرند (سرعت فیلتر گزارش‌ها).</summary>
    private static readonly string[] FilteredTables =
    {
        "Products", "Parties", "Stocks", "Transactions", "TransactionLines",
        "GoodsIssues", "Expenses", "RepairOrders", "Cheques", "Installments"
    };

    public static async Task EnsureAsync(AppDbContext db)
    {
        if (db.Database.IsSqlite()) await EnsureSqliteAsync(db);
        else await EnsureSqlServerAsync(db);

        // کنترل نهایی: اگر ستونی ساخته نشد (مثلاً کاربر دیتابیس مجوز ALTER ندارد)،
        // لاگ روشن می‌دهیم تا خطای بعدی «Invalid column name 'CompanyId'» بی‌توضیح نماند.
        var missing = await MissingColumnsAsync(db);
        if (missing.Count > 0)
        {
            Console.WriteLine("[DB] ⚠ SalesCompanySchemaV1: ستون CompanyId در این جدول‌ها ساخته نشد: " + string.Join(", ", missing));
            Console.WriteLine("[DB] ⚠ علت رایج: مجوز ALTER روی دیتابیس، یا اجرا با کاربری که مالک جدول‌ها نیست.");
            Console.WriteLine("[DB] ⚠ راه‌حل: اسکریپت FIX-COMPANYID-SQLSERVER.sql را با کاربر sa در SSMS اجرا کنید.");
        }
    }

    /// <summary>فهرست جدول‌هایی که وجود دارند ولی ستون CompanyId ندارند.</summary>
    private static async Task<List<string>> MissingColumnsAsync(AppDbContext db)
    {
        var missing = new List<string>();
        try
        {
            if (db.Database.IsSqlite())
            {
                foreach (var table in Tables)
                {
                    if (!await TableExistsAsync(db, table)) continue;
                    if (!await ColumnExistsAsync(db, table, "CompanyId")) missing.Add(table);
                }
            }
            else
            {
                var sql = "SELECT t.[name] FROM sys.tables t WHERE t.[name] IN (" +
                          string.Join(", ", Tables.Select(x => $"N'{x}'")) + ") AND NOT EXISTS (" +
                          "SELECT 1 FROM sys.columns c WHERE c.[object_id] = t.[object_id] AND c.[name] = N'CompanyId')";
                var conn = db.Database.GetDbConnection();
                if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;
                using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync()) missing.Add(r.GetString(0));
            }
        }
        catch { /* کنترل فقط برای لاگ است */ }
        return missing;
    }

    /// <summary>
    /// انتساب رکوردهای بدون شرکت (CompanyId IS NULL) به اولین شرکت فعال.
    /// <para>
    /// کاربرد: نصب تازه‌ای که هنوز هیچ شرکتی ندارد، دادهٔ پایه را با CompanyId = NULL می‌سازد.
    /// به‌محض ساخت اولین شرکت، این متد صدا زده می‌شود تا آن داده‌ها «گم» نشوند.
    /// </para>
    /// </summary>
    public static async Task BackfillAsync(AppDbContext db)
    {
        foreach (var table in Tables)
        {
            if (db.Database.IsSqlite())
            {
                if (!await TableExistsAsync(db, table)) continue;
                await SafeAsync(db, $@"
UPDATE [{table}]
   SET [CompanyId] = (SELECT [Id] FROM [SystemCompanies] WHERE [IsActive] = 1 ORDER BY [Id] LIMIT 1)
 WHERE [CompanyId] IS NULL
   AND EXISTS (SELECT 1 FROM [SystemCompanies] WHERE [IsActive] = 1);");
            }
            else
            {
                await SafeAsync(db, $@"
IF OBJECT_ID(N'dbo.{table}', N'U') IS NOT NULL
BEGIN
    DECLARE @c int = (SELECT TOP 1 [Id] FROM [dbo].[SystemCompanies] WHERE [IsActive] = 1 ORDER BY [Id]);
    IF @c IS NOT NULL
        UPDATE [dbo].[{table}] SET [CompanyId] = @c WHERE [CompanyId] IS NULL;
END");
            }
        }
    }

    // ==================== SQL Server ====================
    private static async Task EnsureSqlServerAsync(AppDbContext db)
    {
        foreach (var table in Tables)
        {
            await SafeAsync(db, $@"
IF OBJECT_ID(N'dbo.{table}', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.{table}', N'CompanyId') IS NULL
    ALTER TABLE [dbo].[{table}] ADD [CompanyId] int NULL;");

            // انتساب رکوردهای بی‌شرکت به شرکت پیش‌فرض (اولین شرکت فعال)
            await SafeAsync(db, $@"
IF OBJECT_ID(N'dbo.{table}', N'U') IS NOT NULL
BEGIN
    DECLARE @c int = (SELECT TOP 1 [Id] FROM [dbo].[SystemCompanies] WHERE [IsActive] = 1 ORDER BY [Id]);
    IF @c IS NOT NULL
        UPDATE [dbo].[{table}] SET [CompanyId] = @c WHERE [CompanyId] IS NULL;
END");
        }

        foreach (var (table, oldIndex, column, newIndex) in UniqueIndexes)
        {
            // ۱) هر ایندکس یکتای تک‌ستونی روی همان ستون (با هر نامی) حذف می‌شود تا
            //    یکتایی «کد/نام» از کل سیستم به داخل هر شرکت منتقل شود.
            // ۲) سپس ایندکس یکتای مرکب (CompanyId, Column) ساخته می‌شود.
            await SafeAsync(db, $@"
IF OBJECT_ID(N'dbo.{table}', N'U') IS NOT NULL
BEGIN
    DECLARE @drop nvarchar(max) = N'';
    SELECT @drop = @drop + N'DROP INDEX ' + QUOTENAME(i.[name]) + N' ON [dbo].[{table}];'
    FROM sys.indexes i
    WHERE i.[object_id] = OBJECT_ID(N'dbo.{table}')
      AND i.[is_unique] = 1 AND i.[is_primary_key] = 0 AND i.[is_unique_constraint] = 0
      AND (SELECT COUNT(*) FROM sys.index_columns ic
            WHERE ic.[object_id] = i.[object_id] AND ic.[index_id] = i.[index_id] AND ic.[is_included_column] = 0) = 1
      AND EXISTS (SELECT 1 FROM sys.index_columns ic
                   JOIN sys.columns c ON c.[object_id] = ic.[object_id] AND c.[column_id] = ic.[column_id]
                  WHERE ic.[object_id] = i.[object_id] AND ic.[index_id] = i.[index_id] AND c.[name] = N'{column}');
    IF @drop <> N'' EXEC(@drop);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'{newIndex}' AND [object_id] = OBJECT_ID(N'dbo.{table}'))
        EXEC(N'CREATE UNIQUE INDEX [{newIndex}] ON [dbo].[{table}] ([CompanyId], [{column}]) WHERE [CompanyId] IS NOT NULL');
END");
        }

        foreach (var table in FilteredTables)
        {
            await SafeAsync(db, $@"
IF OBJECT_ID(N'dbo.{table}', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_{table}_CompanyId' AND [object_id] = OBJECT_ID(N'dbo.{table}'))
    CREATE INDEX [IX_{table}_CompanyId] ON [dbo].[{table}] ([CompanyId]);");
        }
    }

    // ==================== SQLite ====================
    private static async Task EnsureSqliteAsync(AppDbContext db)
    {
        foreach (var table in Tables)
        {
            if (!await TableExistsAsync(db, table)) continue;

            if (!await ColumnExistsAsync(db, table, "CompanyId"))
                await SafeAsync(db, $"ALTER TABLE [{table}] ADD COLUMN [CompanyId] INTEGER NULL;");

            await SafeAsync(db, $@"
UPDATE [{table}]
   SET [CompanyId] = (SELECT [Id] FROM [SystemCompanies] WHERE [IsActive] = 1 ORDER BY [Id] LIMIT 1)
 WHERE [CompanyId] IS NULL
   AND EXISTS (SELECT 1 FROM [SystemCompanies] WHERE [IsActive] = 1);");
        }

        foreach (var (table, oldIndex, column, newIndex) in UniqueIndexes)
        {
            if (!await TableExistsAsync(db, table)) continue;
            await SafeAsync(db, $"DROP INDEX IF EXISTS [{oldIndex}];");
            await SafeAsync(db, $"CREATE UNIQUE INDEX IF NOT EXISTS [{newIndex}] ON [{table}] ([CompanyId], [{column}]) WHERE [CompanyId] IS NOT NULL;");
        }

        foreach (var table in FilteredTables)
        {
            if (!await TableExistsAsync(db, table)) continue;
            if (!await ColumnExistsAsync(db, table, "CompanyId")) continue;
            await SafeAsync(db, $"CREATE INDEX IF NOT EXISTS [IX_{table}_CompanyId] ON [{table}] ([CompanyId]);");
        }
    }

    // ==================== ابزار ====================
    private static async Task<bool> TableExistsAsync(AppDbContext db, string table)
    {
        try
        {
            var conn = db.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$n";
            var p = cmd.CreateParameter();
            p.ParameterName = "$n";
            p.Value = table;
            cmd.Parameters.Add(p);
            return Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
        }
        catch { return false; }
    }

    private static async Task<bool> ColumnExistsAsync(AppDbContext db, string table, string column)
    {
        try
        {
            var conn = db.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name='{column}'";
            return Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
        }
        catch { return false; }
    }

    private static async Task SafeAsync(AppDbContext db, string sql)
    {
        try { await db.Database.ExecuteSqlRawAsync(sql); }
        catch (Exception ex)
        {
            var m = ex.Message ?? "";
            if (!m.Contains("duplicate", StringComparison.OrdinalIgnoreCase) &&
                !m.Contains("already exists", StringComparison.OrdinalIgnoreCase))
                Console.WriteLine($"[DB] SalesCompanySchemaV1: {m}");
        }
    }
}
