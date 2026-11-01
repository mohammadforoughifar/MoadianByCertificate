using Inventory.Api.Services;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;
using Db = Inventory.Api.Data;

namespace Inventory.Api.Services.Accounting;

/// <summary>
/// سرویس ابعاد تحلیلی (مراکز هزینه / شعبه / پروژه).
/// هر بُعد یک مجموعه از «مقادیر» دارد که به آرتیکل‌های سند حسابداری متصل می‌شوند
/// تا بتوان سود/هزینه را به تفکیک مرکز هزینه و شعبه گزارش گرفت.
/// </summary>
public interface IAnalyticalDimensionService
{
    Task<List<AccDimension>> GetDimensionsAsync(Paging.Request? pagination = null);
    Task<AccDimension> SaveDimensionAsync(AccDimension dto);
    Task DeleteDimensionAsync(int id);

    Task<List<AccDimensionValue>> GetValuesAsync(int dimensionId, bool activeOnly = false, Paging.Request? pagination = null);
    Task<AccDimensionValue> SaveValueAsync(AccDimensionValue dto);
    Task DeleteValueAsync(int id);

    Task<List<LookupItem>> GetValueLookupsAsync(int dimensionId, string? search = null, Paging.Request? pagination = null);
}

public class AnalyticalDimensionService : IAnalyticalDimensionService
{
    private readonly Db.AppDbContext _db;
    public AnalyticalDimensionService(Db.AppDbContext db) => _db = db;

    // =====================================================================
    // ابعاد
    // =====================================================================
    public async Task<List<AccDimension>> GetDimensionsAsync(Paging.Request? pagination = null)
    {
        var dims = await _db.AccDimensions.AsNoTracking()
            .OrderBy(d => d.SortOrder).ThenBy(d => d.Code).ThenBy(x => x.Id).ToPageListAsync(pagination);

        var counts = await _db.AccDimensionValues.AsNoTracking()
            .Where(v => v.IsActive)
            .GroupBy(v => v.DimensionId)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count);

        return dims.Select(d => new AccDimension
        {
            Id = d.Id, Code = d.Code, Name = d.Name, IsSystem = d.IsSystem,
            IsActive = d.IsActive, SortOrder = d.SortOrder, Description = d.Description,
            ValueCount = counts.TryGetValue(d.Id, out var c) ? c : 0
        }).ToList();
    }

    public async Task<AccDimension> SaveDimensionAsync(AccDimension dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Code) || string.IsNullOrWhiteSpace(dto.Name))
            throw new InvalidOperationException("کد و نام بُعد الزامی است.");

        var dup = await _db.AccDimensions.AnyAsync(d =>
            (d.Code == dto.Code || d.Name == dto.Name) && d.Id != dto.Id);
        if (dup) throw new InvalidOperationException("بُعدی با این کد یا نام قبلاً ثبت شده است.");

        Db.AccDimension entity;
        if (dto.Id == 0)
        {
            entity = new Db.AccDimension();
            _db.AccDimensions.Add(entity);
        }
        else
        {
            entity = await _db.AccDimensions.FindAsync(dto.Id)
                     ?? throw new InvalidOperationException("بُعد یافت نشد.");
        }

        entity.Code = dto.Code.Trim();
        entity.Name = dto.Name.Trim();
        entity.IsActive = dto.IsActive;
        entity.SortOrder = dto.SortOrder;
        entity.Description = dto.Description;
        await _db.SaveChangesAsync();
        dto.Id = entity.Id;
        return dto;
    }

    public async Task DeleteDimensionAsync(int id)
    {
        var entity = await _db.AccDimensions
            .Include(d => d.Values)
            .FirstOrDefaultAsync(d => d.Id == id)
            ?? throw new InvalidOperationException("بُعد یافت نشد.");

        if (entity.IsSystem)
            throw new InvalidOperationException("بُعد سیستمی قابل حذف نیست.");

        var inUse = await _db.AccVoucherLines.AnyAsync(l => l.DimensionValueId != null &&
            _db.AccDimensionValues.Any(v => v.DimensionId == id && v.Id == l.DimensionValueId));
        if (inUse)
            throw new InvalidOperationException("این بُعد در اسناد حسابداری استفاده شده و قابل حذف نیست.");

        _db.AccDimensionValues.RemoveRange(entity.Values);
        _db.AccDimensions.Remove(entity);
        await _db.SaveChangesAsync();
    }

    // =====================================================================
    // مقادیر
    // =====================================================================
    public async Task<List<AccDimensionValue>> GetValuesAsync(int dimensionId, bool activeOnly = false, Paging.Request? pagination = null)
    {
        var q = _db.AccDimensionValues.AsNoTracking()
            .Where(v => v.DimensionId == dimensionId);
        if (activeOnly) q = q.Where(v => v.IsActive);

        var list = await q.OrderBy(v => v.SortOrder).ThenBy(v => v.Code).ThenBy(x => x.Id).ToPageListAsync(pagination);

        var parents = await _db.AccDimensionValues.AsNoTracking()
            .Where(v => list.Select(x => x.ParentId).Contains(v.Id))
            .ToDictionaryAsync(v => v.Id, v => v.Name);

        return list.Select(v => new AccDimensionValue
        {
            Id = v.Id, DimensionId = v.DimensionId, Code = v.Code, Name = v.Name,
            ParentId = v.ParentId,
            ParentName = v.ParentId != null && parents.TryGetValue(v.ParentId.Value, out var pn) ? pn : null,
            CodeTree = v.CodeTree, IsActive = v.IsActive, SortOrder = v.SortOrder,
            Description = v.Description
        }).ToList();
    }

    public async Task<AccDimensionValue> SaveValueAsync(AccDimensionValue dto)
    {
        if (dto.DimensionId == 0 || string.IsNullOrWhiteSpace(dto.Code) || string.IsNullOrWhiteSpace(dto.Name))
            throw new InvalidOperationException("بُعد، کد و نام مقدار الزامی است.");

        var dim = await _db.AccDimensions.FindAsync(dto.DimensionId)
                  ?? throw new InvalidOperationException("بُعد انتخاب‌شده یافت نشد.");

        var tree = dto.Code.Trim();
        if (dto.ParentId is > 0)
        {
            var parent = await _db.AccDimensionValues.FindAsync(dto.ParentId.Value)
                         ?? throw new InvalidOperationException("والد انتخاب‌شده یافت نشد.");
            if (parent.DimensionId != dto.DimensionId)
                throw new InvalidOperationException("والد باید از همان بُعد باشد.");
            tree = $"{parent.CodeTree}.{tree}";
        }

        var dup = await _db.AccDimensionValues.AnyAsync(v =>
            v.DimensionId == dto.DimensionId && v.Code == dto.Code && v.Id != dto.Id);
        if (dup) throw new InvalidOperationException("کد مقدار در این بُعد تکراری است.");

        Db.AccDimensionValue entity;
        if (dto.Id == 0)
        {
            entity = new Db.AccDimensionValue();
            _db.AccDimensionValues.Add(entity);
        }
        else
        {
            entity = await _db.AccDimensionValues.FindAsync(dto.Id)
                     ?? throw new InvalidOperationException("مقدار یافت نشد.");
        }

        entity.DimensionId = dto.DimensionId;
        entity.Code = dto.Code.Trim();
        entity.Name = dto.Name.Trim();
        entity.ParentId = dto.ParentId is > 0 ? dto.ParentId : null;
        entity.CodeTree = tree;
        entity.IsActive = dto.IsActive;
        entity.SortOrder = dto.SortOrder;
        entity.Description = dto.Description;
        await _db.SaveChangesAsync();

        _ = dim; // keeping reference for future validation hooks
        dto.Id = entity.Id;
        dto.CodeTree = entity.CodeTree;
        return dto;
    }

    public async Task DeleteValueAsync(int id)
    {
        var entity = await _db.AccDimensionValues.FindAsync(id)
                     ?? throw new InvalidOperationException("مقدار یافت نشد.");

        var hasChildren = await _db.AccDimensionValues.AnyAsync(v => v.ParentId == id);
        if (hasChildren)
            throw new InvalidOperationException("ابتدا مقادیر زیرمجموعه را حذف کنید.");

        var inUse = await _db.AccVoucherLines.AnyAsync(l => l.DimensionValueId == id);
        if (inUse)
            throw new InvalidOperationException("این مقدار در اسناد حسابداری استفاده شده و قابل حذف نیست.");

        _db.AccDimensionValues.Remove(entity);
        await _db.SaveChangesAsync();
    }

    public async Task<List<LookupItem>> GetValueLookupsAsync(int dimensionId, string? search = null, Paging.Request? pagination = null)
    {
        var q = _db.AccDimensionValues.AsNoTracking()
            .Where(v => v.DimensionId == dimensionId && v.IsActive);
        if (!string.IsNullOrWhiteSpace(search))
            q = q.Where(v => v.Code.Contains(search) || v.Name.Contains(search));
        return await q.OrderBy(v => v.Code).ThenBy(x => x.Id)
            .Select(v => new LookupItem { Id = v.Id, Name = $"{v.Code} — {v.Name}" })
            .ToPageListAsync(pagination, defaultCap: 200);
    }
}
