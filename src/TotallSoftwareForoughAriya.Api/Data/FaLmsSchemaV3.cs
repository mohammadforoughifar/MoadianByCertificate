using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>
/// §۴ آموزش: پرونده مدرس‌ها + حق‌التدریس، محل جلسات، نظرسنجی اثربخشی.
/// همه دستورات idempotent هستند و برای SQL Server و SQLite جدا اجرا می‌شوند.
/// </summary>
public static class FaLmsSchemaV3
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
            Console.WriteLine($"[DB] FaLmsSchemaV3 خطا: {ex.GetType().Name}: {ex.Message}");
        }
    }

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

        List<string> ColumnsOf(string table)
        {
            var list = new List<string>();
            using var c = raw.CreateCommand();
            c.CommandText = $"PRAGMA table_info({table})";
            using var r = c.ExecuteReader();
            while (r.Read()) list.Add(r.GetString(1));
            return list;
        }

        var courseCols = ColumnsOf("FaLmsCourses");
        if (!courseCols.Contains("InstructorId")) Exec("ALTER TABLE FaLmsCourses ADD COLUMN InstructorId INTEGER NULL");
        var sessCols = ColumnsOf("FaLmsSessions");
        if (!sessCols.Contains("Location")) Exec("ALTER TABLE FaLmsSessions ADD COLUMN Location TEXT NULL");

        Exec(@"
            CREATE TABLE IF NOT EXISTS FaLmsInstructors (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                Type INTEGER NOT NULL DEFAULT 0,
                Field TEXT NULL,
                Phone TEXT NULL,
                FeePerHour REAL NOT NULL DEFAULT 0,
                EmployeeId INTEGER NULL,
                IsActive INTEGER NOT NULL DEFAULT 1,
                CreatedAt TEXT NOT NULL
            )");

        Exec(@"
            CREATE TABLE IF NOT EXISTS FaLmsSurveyQuestions (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                CourseId INTEGER NOT NULL,
                Text TEXT NOT NULL,
                SortOrder INTEGER NOT NULL DEFAULT 0
            )");
        Exec("CREATE INDEX IF NOT EXISTS IX_FaLmsSurveyQ_Course ON FaLmsSurveyQuestions (CourseId)");

        Exec(@"
            CREATE TABLE IF NOT EXISTS FaLmsSurveyAnswers (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                QuestionId INTEGER NOT NULL,
                EmployeeId INTEGER NOT NULL,
                Score INTEGER NOT NULL DEFAULT 5,
                CreatedAt TEXT NOT NULL
            )");
        Exec("CREATE INDEX IF NOT EXISTS IX_FaLmsSurveyA_Q ON FaLmsSurveyAnswers (QuestionId)");
        Exec("CREATE INDEX IF NOT EXISTS IX_FaLmsSurveyA_Emp ON FaLmsSurveyAnswers (EmployeeId)");

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

        await Exec("IF COL_LENGTH('FaLmsCourses','InstructorId') IS NULL ALTER TABLE FaLmsCourses ADD InstructorId int NULL;");
        await Exec("IF COL_LENGTH('FaLmsSessions','Location') IS NULL ALTER TABLE FaLmsSessions ADD Location nvarchar(150) NULL;");

        await Exec(@"
IF OBJECT_ID(N'FaLmsInstructors', N'U') IS NULL
CREATE TABLE FaLmsInstructors (
    Id int NOT NULL IDENTITY(1,1) PRIMARY KEY,
    Name nvarchar(150) NOT NULL,
    Type int NOT NULL DEFAULT 0,
    Field nvarchar(150) NULL,
    Phone nvarchar(30) NULL,
    FeePerHour float NOT NULL DEFAULT 0,
    EmployeeId int NULL,
    IsActive bit NOT NULL DEFAULT 1,
    CreatedAt datetime2 NOT NULL
)");

        await Exec(@"
IF OBJECT_ID(N'FaLmsSurveyQuestions', N'U') IS NULL
CREATE TABLE FaLmsSurveyQuestions (
    Id int NOT NULL IDENTITY(1,1) PRIMARY KEY,
    CourseId int NOT NULL,
    Text nvarchar(300) NOT NULL,
    SortOrder int NOT NULL DEFAULT 0
)");

        await Exec(@"
IF OBJECT_ID(N'FaLmsSurveyAnswers', N'U') IS NULL
CREATE TABLE FaLmsSurveyAnswers (
    Id int NOT NULL IDENTITY(1,1) PRIMARY KEY,
    QuestionId int NOT NULL,
    EmployeeId int NOT NULL,
    Score int NOT NULL DEFAULT 5,
    CreatedAt datetime2 NOT NULL
)");
    }
}
