using System.Data;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>
/// هزینهٔ واحدِ ثبت‌شده روی سطر فاکتور. هزینهٔ تعمیرات باید از هزینهٔ واقعیِ هر ردیف
/// بیاید، نه از قیمت فروشِ کالای خدماتی یا میانگین عمومی انبار.
/// </summary>
public static class TransactionLineCostSchemaV1
{
    public static async Task EnsureAsync(AppDbContext db)
    {
        if (db.Database.IsSqlite())
            await EnsureSqliteAsync(db);
        else
            await EnsureSqlServerAsync(db);

        await BackfillRepairInvoiceCostsAsync(db);
    }

    private static Task EnsureSqlServerAsync(AppDbContext db) => db.Database.ExecuteSqlRawAsync(@"
IF OBJECT_ID(N'dbo.TransactionLines', N'U') IS NOT NULL
AND COL_LENGTH(N'dbo.TransactionLines', N'UnitCostSnapshot') IS NULL
    ALTER TABLE [dbo].[TransactionLines] ADD [UnitCostSnapshot] decimal(18,2) NULL;");

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

            var hasColumn = false;
            await using (var columnCommand = connection.CreateCommand())
            {
                columnCommand.CommandText = "PRAGMA table_info('TransactionLines');";
                await using var reader = await columnCommand.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    if (string.Equals(reader.GetString(1), "UnitCostSnapshot", StringComparison.OrdinalIgnoreCase))
                    {
                        hasColumn = true;
                        break;
                    }
                }
            }

            if (!hasColumn)
            {
                await using var alterCommand = connection.CreateCommand();
                alterCommand.CommandText = "ALTER TABLE TransactionLines ADD COLUMN UnitCostSnapshot TEXT NULL;";
                await alterCommand.ExecuteNonQueryAsync();
            }
        }
        finally
        {
            if (closeWhenDone) await connection.CloseAsync();
        }
    }

    /// <summary>
    /// فاکتورهای تعمیراتیِ قبلی را نیز از هزینهٔ واقعیِ ردیف‌های پذیرش اصلاح می‌کند.
    /// ترتیب درج هر دو طرف بر اساس شناسه است؛ فقط وقتی تعداد ردیف‌ها دقیقاً مطابق است بازنویسی می‌شود.
    /// </summary>
    private static async Task BackfillRepairInvoiceCostsAsync(AppDbContext db)
    {
        var repairs = await db.RepairOrders.AsNoTracking()
            .Include(r => r.Items)
            .Where(r => r.InvoiceTransactionId.HasValue &&
                        db.TransactionLines.Any(l => l.TransactionId == r.InvoiceTransactionId!.Value && l.UnitCostSnapshot == null))
            .ToListAsync();
        if (repairs.Count == 0) return;

        var transactionIds = repairs.Select(r => r.InvoiceTransactionId!.Value).Distinct().ToList();
        var lines = new List<TransactionLine>();
        foreach (var chunk in transactionIds.Chunk(500))
        {
            lines.AddRange(await db.TransactionLines
                .Where(l => chunk.Contains(l.TransactionId))
                .OrderBy(l => l.TransactionId).ThenBy(l => l.Id)
                .ToListAsync());
        }

        var linesByTransaction = lines.GroupBy(l => l.TransactionId)
            .ToDictionary(g => g.Key, g => g.OrderBy(l => l.Id).ToList());
        var changed = false;
        foreach (var repair in repairs)
        {
            var transactionId = repair.InvoiceTransactionId!.Value;
            var repairItems = repair.Items.Where(i => i.Price > 0).OrderBy(i => i.Id).ToList();
            if (!linesByTransaction.TryGetValue(transactionId, out var transactionLines) ||
                transactionLines.Count != repairItems.Count)
                continue;

            for (var i = 0; i < transactionLines.Count; i++)
            {
                if (transactionLines[i].UnitCostSnapshot is not null) continue;

                // نسخه‌های قبلی، اجرت را به یک سطر با مقدار ۱ و مبلغ تجمیعی تبدیل می‌کردند.
                // هزینهٔ کل پذیرش را بر مقدار همان سطر تقسیم می‌کنیم تا هم آن نسخه و هم سطرهای کالایی درست شوند.
                var lineQuantity = transactionLines[i].Quantity;
                var totalRepairCost = repairItems[i].Cost * repairItems[i].Quantity;
                transactionLines[i].UnitCostSnapshot = lineQuantity > 0
                    ? decimal.Round(totalRepairCost / lineQuantity, 2)
                    : repairItems[i].Cost;
                changed = true;
            }
        }

        if (changed) await db.SaveChangesAsync();
    }
}
