using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>
/// §۵ بانک سؤال + آزمون چندتلاشه + سؤال تشریحی.
/// همه دستورات idempotent هستند و برای SQL Server و SQLite جدا اجرا می‌شوند.
/// </summary>
public static class FaLmsSchemaV2
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
            Console.WriteLine($"[DB] FaLmsSchemaV2 خطا: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // ================== SQLite ==================

    private static async Task EnsureSqliteAsync(string connectionString)
    {
        using var raw = new SqliteConnection(connectionString);
        raw.Open();

        List<string> ColumnsOf(string table)
        {
            var cols = new List<string>();
            using var c = raw.CreateCommand();
            c.CommandText = $"SELECT name FROM pragma_table_info('{table}')";
            using var rd = c.ExecuteReader();
            while (rd.Read()) cols.Add(rd.GetString(0));
            return cols;
        }

        void Exec(string sql)
        {
            using var c = raw.CreateCommand();
            c.CommandText = sql;
            c.ExecuteNonQuery();
        }

        var examCols = ColumnsOf("FaLmsExams");
        if (!examCols.Contains("MaxAttempts")) Exec("ALTER TABLE FaLmsExams ADD COLUMN MaxAttempts INTEGER NOT NULL DEFAULT 1");
        var qCols = ColumnsOf("FaLmsQuestions");
        if (!qCols.Contains("Type")) Exec("ALTER TABLE FaLmsQuestions ADD COLUMN Type INTEGER NOT NULL DEFAULT 0");
        var attCols = ColumnsOf("FaLmsAttempts");
        if (!attCols.Contains("AttemptNo")) Exec("ALTER TABLE FaLmsAttempts ADD COLUMN AttemptNo INTEGER NOT NULL DEFAULT 1");

        Exec(@"
            CREATE TABLE IF NOT EXISTS FaLmsBanks (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                Title TEXT NOT NULL,
                Description TEXT NULL,
                CreatedAt TEXT NOT NULL
            );");
        Exec(@"
            CREATE TABLE IF NOT EXISTS FaLmsBankQuestions (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                BankId INTEGER NOT NULL,
                Text TEXT NOT NULL,
                OptA TEXT NULL,
                OptB TEXT NULL,
                OptC TEXT NULL,
                OptD TEXT NULL,
                CorrectIndex INTEGER NOT NULL DEFAULT 0,
                Score REAL NOT NULL DEFAULT 1,
                Type INTEGER NOT NULL DEFAULT 0,
                SortOrder INTEGER NOT NULL DEFAULT 0
            );");
        Exec("CREATE INDEX IF NOT EXISTS IX_FaLmsBankQ_Bank ON FaLmsBankQuestions (BankId);");
        Exec(@"
            CREATE TABLE IF NOT EXISTS FaLmsTextAnswers (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                AttemptId INTEGER NOT NULL,
                QuestionId INTEGER NOT NULL,
                AnswerText TEXT NULL,
                ManualScore REAL NULL
            );");
        Exec("CREATE INDEX IF NOT EXISTS IX_FaLmsTextA_Attempt ON FaLmsTextAnswers (AttemptId);");
        await Task.CompletedTask;
    }

    // ================== SQL Server ==================

    private static async Task EnsureSqlServerAsync(string connectionString)
    {
        using var raw = new SqlConnection(connectionString);
        await raw.OpenAsync();

        async Task Exec(string sql)
        {
            using var c = raw.CreateCommand();
            c.CommandText = sql;
            await c.ExecuteNonQueryAsync();
        }

        await Exec("IF COL_LENGTH('FaLmsExams','MaxAttempts') IS NULL ALTER TABLE FaLmsExams ADD MaxAttempts int NOT NULL DEFAULT 1;");
        await Exec("IF COL_LENGTH('FaLmsQuestions','Type') IS NULL ALTER TABLE FaLmsQuestions ADD Type int NOT NULL DEFAULT 0;");
        await Exec("IF COL_LENGTH('FaLmsAttempts','AttemptNo') IS NULL ALTER TABLE FaLmsAttempts ADD AttemptNo int NOT NULL DEFAULT 1;");
        await Exec(@"
IF OBJECT_ID(N'FaLmsBanks', N'U') IS NULL
CREATE TABLE FaLmsBanks (
    Id int NOT NULL IDENTITY(1,1) PRIMARY KEY,
    Title nvarchar(200) NOT NULL,
    Description nvarchar(500) NULL,
    CreatedAt datetime2 NOT NULL
);");
        await Exec(@"
IF OBJECT_ID(N'FaLmsBankQuestions', N'U') IS NULL
CREATE TABLE FaLmsBankQuestions (
    Id int NOT NULL IDENTITY(1,1) PRIMARY KEY,
    BankId int NOT NULL,
    Text nvarchar(1000) NOT NULL,
    OptA nvarchar(500) NULL,
    OptB nvarchar(500) NULL,
    OptC nvarchar(500) NULL,
    OptD nvarchar(500) NULL,
    CorrectIndex int NOT NULL DEFAULT 0,
    Score float NOT NULL DEFAULT 1,
    Type int NOT NULL DEFAULT 0,
    SortOrder int NOT NULL DEFAULT 0
);");
        await Exec(@"
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_FaLmsBankQ_Bank')
CREATE INDEX IX_FaLmsBankQ_Bank ON FaLmsBankQuestions (BankId);");
        await Exec(@"
IF OBJECT_ID(N'FaLmsTextAnswers', N'U') IS NULL
CREATE TABLE FaLmsTextAnswers (
    Id int NOT NULL IDENTITY(1,1) PRIMARY KEY,
    AttemptId int NOT NULL,
    QuestionId int NOT NULL,
    AnswerText nvarchar(max) NULL,
    ManualScore float NULL
);");
        await Exec(@"
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_FaLmsTextA_Attempt')
CREATE INDEX IX_FaLmsTextA_Attempt ON FaLmsTextAnswers (AttemptId);");
    }
}
