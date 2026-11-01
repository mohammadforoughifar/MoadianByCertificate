using Inventory.Api.Data;
using Inventory.Shared;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services.Catalog;

/// <summary>سرویس حواله تحویل کالا (فقط مقدار — بدون قیمت).</summary>
public interface IGoodsIssueService
{
    Task<List<GoodsIssueDto>> GetAsync(int? partyId = null, DateTime? from = null, DateTime? to = null, string? search = null);
    Task<GoodsIssueDto?> GetAsync(int id);

    /// <summary>ثبت یا ویرایش حواله. شماره فقط هنگام ثبت جدید ساخته می‌شود.</summary>
    Task<GoodsIssueDto> SaveAsync(GoodsIssueCommand cmd);
    Task DeleteAsync(int id);
}

public class GoodsIssueService : IGoodsIssueService
{
    private readonly AppDbContext _db;

    public GoodsIssueService(AppDbContext db) => _db = db;

    public async Task<List<GoodsIssueDto>> GetAsync(int? partyId = null, DateTime? from = null, DateTime? to = null, string? search = null)
    {
        var q = _db.GoodsIssues.Include(g => g.Lines).AsNoTracking().AsQueryable();

        if (partyId is > 0) q = q.Where(g => g.PartyId == partyId);
        if (from.HasValue) q = q.Where(g => g.Date >= from.Value.Date);
        if (to.HasValue) q = q.Where(g => g.Date <= to.Value.Date.AddDays(1).AddTicks(-1));
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(g => g.Number.Contains(s) || g.PartyName.Contains(s)
                || (g.Description != null && g.Description.Contains(s))
                || g.Lines.Any(l => l.ProductName.Contains(s)));
        }

        var list = await q.OrderByDescending(g => g.Date).ThenByDescending(g => g.Id).ToListAsync();
        return list.Select(ToDto).ToList();
    }

    public async Task<GoodsIssueDto?> GetAsync(int id)
    {
        var g = await _db.GoodsIssues.Include(x => x.Lines).AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        return g is null ? null : ToDto(g);
    }

    public async Task<GoodsIssueDto> SaveAsync(GoodsIssueCommand cmd)
    {
        if (cmd.PartyId <= 0) throw new InvalidOperationException("مشتری را انتخاب کنید.");
        if (cmd.Lines.Count == 0) throw new InvalidOperationException("حداقل یک کالا با مقدار وارد کنید.");
        if (cmd.Lines.Any(l => string.IsNullOrWhiteSpace(l.ProductName)))
            throw new InvalidOperationException("نام کالای هر سطر را وارد کنید.");
        if (cmd.Lines.Any(l => l.Quantity <= 0)) throw new InvalidOperationException("مقدار هر سطر باید بزرگ‌تر از صفر باشد.");

        var party = await _db.Parties.FirstOrDefaultAsync(p => p.Id == cmd.PartyId)
            ?? throw new InvalidOperationException("مشتری انتخاب‌شده یافت نشد؛ از فهرست مشتری‌ها دوباره انتخاب کنید.");

        GoodsIssue entity;
        if (cmd.Id == 0)
        {
            entity = new GoodsIssue { Number = await GenerateNumberAsync(), CreatedAt = DateTime.Now };
            _db.GoodsIssues.Add(entity);
        }
        else
        {
            entity = await _db.GoodsIssues.Include(g => g.Lines).FirstOrDefaultAsync(g => g.Id == cmd.Id)
                ?? throw new InvalidOperationException("حواله یافت نشد.");
            _db.GoodsIssueLines.RemoveRange(entity.Lines);
            entity.Lines.Clear();
        }

        entity.Date = cmd.Date == default ? DateTime.Now : cmd.Date;
        entity.PartyId = party.Id;
        entity.PartyName = party.Name;
        entity.Description = string.IsNullOrWhiteSpace(cmd.Description) ? null : cmd.Description.Trim();

        foreach (var l in cmd.Lines)
            entity.Lines.Add(new GoodsIssueLine
            {
                ProductId = l.ProductId,
                ProductName = l.ProductName.Trim(),   // کالا آزاد است — از فهرست کالاها نیست
                Quantity = l.Quantity,
                Note = string.IsNullOrWhiteSpace(l.Note) ? null : l.Note.Trim()
            });

        await _db.SaveChangesAsync();

        // پس از ذخیره، Id سطرها هم پر شده است
        var saved = await _db.GoodsIssues.Include(g => g.Lines).AsNoTracking().FirstAsync(g => g.Id == entity.Id);
        return ToDto(saved);
    }

    public async Task DeleteAsync(int id)
    {
        var g = await _db.GoodsIssues.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id);
        if (g is null) return;
        _db.GoodsIssues.Remove(g);   // سطرها با Cascade حذف می‌شوند
        await _db.SaveChangesAsync();
    }

    private static GoodsIssueDto ToDto(GoodsIssue g) => new()
    {
        Id = g.Id,
        Number = g.Number,
        Date = g.Date,
        PartyId = g.PartyId,
        PartyName = g.PartyName,
        Description = g.Description,
        Lines = g.Lines.Select(l => new GoodsIssueLineDto
        {
            Id = l.Id,
            ProductId = l.ProductId,
            ProductName = l.ProductName,
            Quantity = l.Quantity,
            Note = l.Note
        }).ToList()
    };

    /// <summary>شمارهٔ حواله: <c>HI-1405-0001</c> — سال شمسی + شمارهٔ متوالی در همان سال.</summary>
    private async Task<string> GenerateNumberAsync()
    {
        var (jy, _, _) = PersianDate.FromGregorian(DateTime.Now);
        var prefix = $"HI-{jy}-";
        // شمارش بر اساس پیشوند، تا مرز سال میلادی/شمسی شماره تکراری نسازد
        var count = await _db.GoodsIssues.CountAsync(g => g.Number.StartsWith(prefix)) + 1;
        return $"{prefix}{count:0000}";
    }
}
