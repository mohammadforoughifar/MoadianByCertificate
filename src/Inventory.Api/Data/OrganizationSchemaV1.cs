using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>
/// خودتعمیرِ اسکیمای «سازمان‌ها و سمت‌ها» — مبنای جزء «واحد» در شماره اندیکاتور نامه‌ها.
/// (Organizations + Semats) — همه دستورات idempotent هستند و برای SQL Server و SQLite جدا اجرا می‌شوند؛
/// ساخت جدول‌های قدیمی و ارتقای ستون‌های سازمانی در شروع برنامه؛ SQL ارتقا با مایگریشن EF مشترک است،
/// تا دیتابیس‌های موجود (حتی با تاریخچهٔ ناقص مایگریشن) پیش از اولین کوئری آماده باشند.
/// </summary>
public static class OrganizationSchemaV1
{
    public static async Task EnsureAsync(AppDbContext db)
    {
        try
        {
            if (db.Database.GetDbConnection() is SqliteConnection sqlConn)
                await EnsureSqliteAsync(sqlConn.ConnectionString);
            else if (db.Database.GetDbConnection() is SqlConnection sqlServerConn)
                await EnsureSqlServerAsync(sqlServerConn.ConnectionString);
        }
        catch (Exception ex)
        {
            // These columns are required by the EF model; do not continue into misleading query errors.
            throw new InvalidOperationException(
                "ارتقای جدول‌های سازمانی ناموفق بود؛ مجوز ALTER و ساختار Organizations/SystemUsers را بررسی کنید.", ex);
        }
    }

    // Shared by EF migration and startup repair. Separate batches avoid SQL Server compiling
    // indexes/foreign keys against columns which have not been added yet.
    internal const string SqlServerOrganizations = @"
            IF OBJECT_ID(N'dbo.Organizations', N'U') IS NULL
            BEGIN
                CREATE TABLE [Organizations] (
                    [OrganizationId] int NOT NULL IDENTITY PRIMARY KEY,
                    [NameUnit] nvarchar(200) NOT NULL,
                    [NameUniq] nvarchar(100) NOT NULL,
                    [IsDefault] bit NOT NULL CONSTRAINT [DF_Organizations_IsDefault] DEFAULT(0),
                    [IsActive] bit NOT NULL CONSTRAINT [DF_Organizations_IsActive] DEFAULT(1),
                    [IsDelete] bit NOT NULL CONSTRAINT [DF_Organizations_IsDelete] DEFAULT(0),
                    [CreatedAt] datetime2 NOT NULL
                );
            END
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Organizations_NameUniq' AND object_id = OBJECT_ID(N'dbo.Organizations'))
                CREATE UNIQUE INDEX [IX_Organizations_NameUniq] ON [Organizations] ([NameUniq]);";

    internal const string SqlServerColumns = @"
IF OBJECT_ID(N'dbo.Organizations', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.Organizations', N'CompanyId') IS NULL
    ALTER TABLE [dbo].[Organizations] ADD [CompanyId] int NULL;
IF OBJECT_ID(N'dbo.SystemUsers', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.SystemUsers', N'OrganizationId') IS NULL
    ALTER TABLE [dbo].[SystemUsers] ADD [OrganizationId] int NULL;
";

    internal const string SqlServerRelations = @"
IF OBJECT_ID(N'dbo.Organizations', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.Organizations') AND name = N'IX_Organizations_CompanyId')
        CREATE INDEX [IX_Organizations_CompanyId] ON [dbo].[Organizations] ([CompanyId]);
    IF OBJECT_ID(N'dbo.SystemCompanies', N'U') IS NOT NULL
       AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID(N'dbo.Organizations') AND name = N'FK_Organizations_SystemCompanies_CompanyId')
        ALTER TABLE [dbo].[Organizations] WITH CHECK
            ADD CONSTRAINT [FK_Organizations_SystemCompanies_CompanyId]
            FOREIGN KEY ([CompanyId]) REFERENCES [dbo].[SystemCompanies] ([Id]) ON DELETE NO ACTION;
END;
IF OBJECT_ID(N'dbo.SystemUsers', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.SystemUsers') AND name = N'IX_SystemUsers_OrganizationId')
        CREATE INDEX [IX_SystemUsers_OrganizationId] ON [dbo].[SystemUsers] ([OrganizationId]);
    IF OBJECT_ID(N'dbo.Organizations', N'U') IS NOT NULL
       AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID(N'dbo.SystemUsers') AND name = N'FK_SystemUsers_Organizations_OrganizationId')
        ALTER TABLE [dbo].[SystemUsers] WITH CHECK
            ADD CONSTRAINT [FK_SystemUsers_Organizations_OrganizationId]
            FOREIGN KEY ([OrganizationId]) REFERENCES [dbo].[Organizations] ([OrganizationId]) ON DELETE NO ACTION;
END;
";

    // ================== SQLite ==================

    private static async Task EnsureSqliteAsync(string connectionString)
    {
        using var raw = new SqliteConnection(connectionString);
        raw.Open();

        void Exec(string sql)
        {
            using var c = raw.CreateCommand();
            c.CommandText = sql;
            c.ExecuteNonQuery();
        }

        Exec(@"
            CREATE TABLE IF NOT EXISTS Organizations (
                OrganizationId INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                NameUnit TEXT NOT NULL,
                NameUniq TEXT NOT NULL,
                IsDefault INTEGER NOT NULL DEFAULT 0,
                IsActive INTEGER NOT NULL DEFAULT 1,
                IsDelete INTEGER NOT NULL DEFAULT 0,
                CreatedAt TEXT NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS IX_Organizations_NameUniq ON Organizations (NameUniq);");

        Exec(@"
            CREATE TABLE IF NOT EXISTS Semats (
                SematId INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                Parent INTEGER NULL,
                UserId INTEGER NULL,
                Title TEXT NOT NULL,
                OnvanMokatebati TEXT NULL,
                OrganizationId INTEGER NOT NULL,
                DefaultSemat INTEGER NOT NULL DEFAULT 0,
                IsActive INTEGER NOT NULL DEFAULT 1,
                IsDelete INTEGER NOT NULL DEFAULT 0
            );
            CREATE INDEX IF NOT EXISTS IX_Semats_UserId ON Semats (UserId);
            CREATE INDEX IF NOT EXISTS IX_Semats_OrganizationId ON Semats (OrganizationId);");

        bool TableExists(string table)
        {
            using var c = raw.CreateCommand();
            c.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$name";
            c.Parameters.AddWithValue("$name", table);
            return Convert.ToInt32(c.ExecuteScalar()) > 0;
        }

        void EnsureColumn(string table, string column, string definition)
        {
            using (var c = raw.CreateCommand())
            {
                c.CommandText = $"PRAGMA table_info([{table}]);";
                using var reader = c.ExecuteReader();
                while (reader.Read())
                    if (reader.GetString(1).Equals(column, StringComparison.OrdinalIgnoreCase)) return;
            }
            Exec($"ALTER TABLE [{table}] ADD COLUMN [{column}] {definition};");
        }

        EnsureColumn("Organizations", "CompanyId", "INTEGER NULL REFERENCES SystemCompanies(Id)");
        Exec("CREATE INDEX IF NOT EXISTS IX_Organizations_CompanyId ON Organizations (CompanyId);");
        if (TableExists("SystemUsers"))
        {
            EnsureColumn("SystemUsers", "OrganizationId", "INTEGER NULL REFERENCES Organizations(OrganizationId)");
            Exec("CREATE INDEX IF NOT EXISTS IX_SystemUsers_OrganizationId ON SystemUsers (OrganizationId);");
        }

        await Task.CompletedTask;
    }

    // ================== SQL Server ==================

    private static async Task EnsureSqlServerAsync(string connectionString)
    {
        using var raw = new SqlConnection(connectionString);
        await raw.OpenAsync();

        async Task ExecAsync(string sql)
        {
            using var c = raw.CreateCommand();
            c.CommandText = sql;
            await c.ExecuteNonQueryAsync();
        }

        // ---------- Organizations ----------
        await ExecAsync(SqlServerOrganizations);
        await ExecAsync(SqlServerColumns);
        await ExecAsync(SqlServerRelations);

        // ---------- Semats ----------
        await ExecAsync(@"
            IF OBJECT_ID(N'dbo.Semats', N'U') IS NULL
            BEGIN
                CREATE TABLE [Semats] (
                    [SematId] int NOT NULL IDENTITY PRIMARY KEY,
                    [Parent] int NULL,
                    [UserId] int NULL,
                    [Title] nvarchar(200) NOT NULL,
                    [OnvanMokatebati] nvarchar(300) NULL,
                    [OrganizationId] int NOT NULL,
                    [DefaultSemat] bit NOT NULL CONSTRAINT [DF_Semats_DefaultSemat] DEFAULT(0),
                    [IsActive] bit NOT NULL CONSTRAINT [DF_Semats_IsActive] DEFAULT(1),
                    [IsDelete] bit NOT NULL CONSTRAINT [DF_Semats_IsDelete] DEFAULT(0),
                    CONSTRAINT [FK_Semats_Organizations_OrganizationId] FOREIGN KEY ([OrganizationId])
                        REFERENCES [Organizations] ([OrganizationId]) ON DELETE NO ACTION,
                    CONSTRAINT [FK_Semats_Users_UserId] FOREIGN KEY ([UserId])
                        REFERENCES [Users] ([Id]) ON DELETE SET NULL
                );
            END
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Semats_UserId' AND object_id = OBJECT_ID(N'dbo.Semats'))
                CREATE INDEX [IX_Semats_UserId] ON [Semats] ([UserId]);
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Semats_OrganizationId' AND object_id = OBJECT_ID(N'dbo.Semats'))
                CREATE INDEX [IX_Semats_OrganizationId] ON [Semats] ([OrganizationId]);");
    }
}
