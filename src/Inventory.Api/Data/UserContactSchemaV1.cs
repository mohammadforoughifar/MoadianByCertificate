using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>
/// ستون‌های ایمیل/وضعیت تأیید و جدول محدودکنندهٔ OTP کاربران.
/// این اسکیمای idempotent علاوه بر دیتابیس تازه، دیتابیس‌های موجود را هم ارتقا می‌دهد؛
/// چون EnsureCreated/Migrate ستون‌های مدل را به جدول‌های قدیمی اضافه نمی‌کنند.
/// </summary>
public static class UserContactSchemaV1
{
    public static Task EnsureAsync(AppDbContext db) =>
        db.Database.IsSqlite() ? EnsureSqliteAsync(db) : EnsureSqlServerAsync(db);

    private static async Task EnsureSqlServerAsync(AppDbContext db)
    {
        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.Users', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.Users', N'MobileVerified') IS NULL
        ALTER TABLE dbo.Users ADD MobileVerified bit NOT NULL CONSTRAINT DF_Users_MobileVerified DEFAULT(0);
    IF COL_LENGTH(N'dbo.Users', N'Email') IS NULL
        ALTER TABLE dbo.Users ADD Email nvarchar(254) NULL;
    IF COL_LENGTH(N'dbo.Users', N'EmailVerified') IS NULL
        ALTER TABLE dbo.Users ADD EmailVerified bit NOT NULL CONSTRAINT DF_Users_EmailVerified DEFAULT(0);
END;");

        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.Users', N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.UserContactOtpChallenges', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.UserContactOtpChallenges
    (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_UserContactOtpChallenges PRIMARY KEY,
        UserId int NOT NULL,
        Purpose nvarchar(10) NOT NULL,
        Destination nvarchar(254) NOT NULL,
        ProtectedCode nvarchar(1000) NOT NULL,
        CreatedAt datetime2 NOT NULL,
        ExpiresAt datetime2 NOT NULL,
        LastSentAt datetime2 NOT NULL,
        WindowStartedAt datetime2 NOT NULL,
        SendCount int NOT NULL CONSTRAINT DF_UserContactOtpChallenges_SendCount DEFAULT(0),
        FailedAttempts int NOT NULL CONSTRAINT DF_UserContactOtpChallenges_FailedAttempts DEFAULT(0),
        LockedUntil datetime2 NULL,
        CONSTRAINT FK_UserContactOtpChallenges_Users_UserId
            FOREIGN KEY (UserId) REFERENCES dbo.Users(Id) ON DELETE CASCADE
    );
END;");

        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.UserContactOtpChallenges', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.UserContactOtpChallenges') AND name = N'IX_UserContactOtpChallenges_UserId_Purpose')
    CREATE UNIQUE INDEX IX_UserContactOtpChallenges_UserId_Purpose
        ON dbo.UserContactOtpChallenges(UserId, Purpose);");
    }

    private static async Task EnsureSqliteAsync(AppDbContext db)
    {
        if (await TableExistsAsync(db, "Users"))
        {
            if (!await ColumnExistsAsync(db, "Users", "MobileVerified"))
                await SafeAsync(db, "ALTER TABLE Users ADD COLUMN MobileVerified INTEGER NOT NULL DEFAULT 0;");
            if (!await ColumnExistsAsync(db, "Users", "Email"))
                await SafeAsync(db, "ALTER TABLE Users ADD COLUMN Email TEXT NULL;");
            if (!await ColumnExistsAsync(db, "Users", "EmailVerified"))
                await SafeAsync(db, "ALTER TABLE Users ADD COLUMN EmailVerified INTEGER NOT NULL DEFAULT 0;");
        }

        await SafeAsync(db, @"
CREATE TABLE IF NOT EXISTS UserContactOtpChallenges (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    UserId INTEGER NOT NULL,
    Purpose TEXT NOT NULL,
    Destination TEXT NOT NULL,
    ProtectedCode TEXT NOT NULL,
    CreatedAt TEXT NOT NULL,
    ExpiresAt TEXT NOT NULL,
    LastSentAt TEXT NOT NULL,
    WindowStartedAt TEXT NOT NULL,
    SendCount INTEGER NOT NULL DEFAULT 0,
    FailedAttempts INTEGER NOT NULL DEFAULT 0,
    LockedUntil TEXT NULL,
    CONSTRAINT FK_UserContactOtpChallenges_Users_UserId
        FOREIGN KEY (UserId) REFERENCES Users(Id) ON DELETE CASCADE
);");
        await SafeAsync(db, @"
CREATE UNIQUE INDEX IF NOT EXISTS IX_UserContactOtpChallenges_UserId_Purpose
    ON UserContactOtpChallenges(UserId, Purpose);");
    }

    private static async Task<bool> TableExistsAsync(AppDbContext db, string table)
    {
        try
        {
            var connection = db.Database.GetDbConnection();
            var openedHere = connection.State != System.Data.ConnectionState.Open;
            if (openedHere) await connection.OpenAsync();
            try
            {
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=@name";
                var parameter = command.CreateParameter();
                parameter.ParameterName = "@name";
                parameter.Value = table;
                command.Parameters.Add(parameter);
                return Convert.ToInt32(await command.ExecuteScalarAsync()) > 0;
            }
            finally
            {
                if (openedHere) await connection.CloseAsync();
            }
        }
        catch { return false; }
    }

    private static async Task<bool> ColumnExistsAsync(AppDbContext db, string table, string column)
    {
        try
        {
            var connection = db.Database.GetDbConnection();
            var openedHere = connection.State != System.Data.ConnectionState.Open;
            if (openedHere) await connection.OpenAsync();
            try
            {
                await using var command = connection.CreateCommand();
                command.CommandText = $"PRAGMA table_info(\"{table}\")";
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase)) return true;
                return false;
            }
            finally
            {
                if (openedHere) await connection.CloseAsync();
            }
        }
        catch { return false; }
    }

    private static async Task SafeAsync(AppDbContext db, string sql)
    {
        try { await db.Database.ExecuteSqlRawAsync(sql); }
        catch (Exception ex)
        {
            var message = ex.Message ?? "";
            if (!message.Contains("already exists", StringComparison.OrdinalIgnoreCase) &&
                !message.Contains("duplicate", StringComparison.OrdinalIgnoreCase))
                Console.WriteLine($"[DB] UserContactSchemaV1: {message}");
        }
    }
}
