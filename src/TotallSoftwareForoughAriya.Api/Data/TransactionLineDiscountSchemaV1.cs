using System.Data;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>
/// افزودن ستون‌های تخفیف سطر فاکتور به جدول TransactionLines.
/// DiscountPercent: درصد تخفیف (0 تا 100)
/// DiscountAmount: مبلغ ریالی تخفیف محاسبه‌شده
/// DiscountIsAmount: آیا کاربر مبلغ ریالی وارد کرده (true) یا درصد (false)
/// </summary>
public static class TransactionLineDiscountSchemaV1
{
    public static async Task EnsureAsync(AppDbContext db)
    {
        if (db.Database.IsSqlite())
            await EnsureSqliteAsync(db);
        else
            await EnsureSqlServerAsync(db);
    }

    private static Task EnsureSqlServerAsync(AppDbContext db) => db.Database.ExecuteSqlRawAsync(@"
IF OBJECT_ID(N'dbo.TransactionLines', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.TransactionLines', N'DiscountPercent') IS NULL
        ALTER TABLE [dbo].[TransactionLines] ADD [DiscountPercent] decimal(18,2) NOT NULL DEFAULT(0);
    IF COL_LENGTH(N'dbo.TransactionLines', N'DiscountAmount') IS NULL
        ALTER TABLE [dbo].[TransactionLines] ADD [DiscountAmount] decimal(18,2) NOT NULL DEFAULT(0);
    IF COL_LENGTH(N'dbo.TransactionLines', N'DiscountIsAmount') IS NULL
        ALTER TABLE [dbo].[TransactionLines] ADD [DiscountIsAmount] bit NOT NULL DEFAULT(0);
END");

    private static async Task EnsureSqliteAsync(AppDbContext db)
    {
        var connection = db.Database.GetDbConnection();
        var closeWhenDone = connection.State != ConnectionState.Open;
        if (closeWhenDone) await connection.OpenAsync();

        try
        {
            bool tableExists;
            await using (var tableCommand = connection.CreateCommand())
            {
                tableCommand.CommandText = "SELECT EXISTS(SELECT 1 FROM sqlite_master WHERE type='table' AND name='TransactionLines');";
                tableExists = Convert.ToInt32(await tableCommand.ExecuteScalarAsync()) != 0;
            }
            if (!tableExists) return;

            var existingColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using (var columnCommand = connection.CreateCommand())
            {
                columnCommand.CommandText = "PRAGMA table_info('TransactionLines');";
                await using var reader = await columnCommand.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    existingColumns.Add(reader.GetString(1));
                }
            }

            if (!existingColumns.Contains("DiscountPercent"))
            {
                await using var alterCommand = connection.CreateCommand();
                alterCommand.CommandText = "ALTER TABLE TransactionLines ADD COLUMN DiscountPercent NUMERIC NOT NULL DEFAULT 0;";
                await alterCommand.ExecuteNonQueryAsync();
            }

            if (!existingColumns.Contains("DiscountAmount"))
            {
                await using var alterCommand = connection.CreateCommand();
                alterCommand.CommandText = "ALTER TABLE TransactionLines ADD COLUMN DiscountAmount NUMERIC NOT NULL DEFAULT 0;";
                await alterCommand.ExecuteNonQueryAsync();
            }

            if (!existingColumns.Contains("DiscountIsAmount"))
            {
                await using var alterCommand = connection.CreateCommand();
                alterCommand.CommandText = "ALTER TABLE TransactionLines ADD COLUMN DiscountIsAmount INTEGER NOT NULL DEFAULT 0;";
                await alterCommand.ExecuteNonQueryAsync();
            }
        }
        finally
        {
            if (closeWhenDone) await connection.CloseAsync();
        }
    }
}
