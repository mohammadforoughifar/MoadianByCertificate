using Inventory.Api.Services;
using Inventory.Api.Data;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services.Office.Outgoing;

// ============================================================
//  سرویس پیش‌نویس نامه صادره — مشابه PishnevisService داخلی
//  هر کاربر فقط پیش‌نویس‌های خودش را می‌بیند/ویرایش می‌کند
//  پوشه‌بندی تمیز: Services/Office/Outgoing
// ============================================================

public interface IOutgoingPishnevisService
{
    Task<List<OutgoingPishnevisDto>> GetAllAsync(int userId, string? search, Paging.Request? pagination = null);
    Task<OutgoingPishnevisDto?> GetByIdAsync(int id, int userId);
    Task<int> AddAsync(OutgoingPishnevisDto dto, int userId);
    Task EditAsync(OutgoingPishnevisDto dto, int userId);
    Task DeleteAsync(int id, int userId);
}

public class OutgoingPishnevisService : IOutgoingPishnevisService
{
    private readonly AppDbContext _db;
    public OutgoingPishnevisService(AppDbContext db) => _db = db;

    public async Task<List<OutgoingPishnevisDto>> GetAllAsync(int userId, string? search, Paging.Request? pagination = null)
    {
        var q = _db.OutgoingPishnevisLetters.AsNoTracking()
            .Where(p => p.UserId == userId && !p.IsDelete);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(p => p.Title.Contains(s) ||
                             (p.ReceiverOrganization != null && p.ReceiverOrganization.Contains(s)));
        }

        return await q
            .OrderByDescending(p => p.IsNeshan)
            .ThenByDescending(p => p.PishnevisId)
            .Select(p => new OutgoingPishnevisDto
            {
                PishnevisId = p.PishnevisId,
                Title = p.Title,
                Text = p.Text,
                ReceiverOrganization = p.ReceiverOrganization,
                ReceiverName = p.ReceiverName,
                ReceiverTitle = p.ReceiverTitle,
                IsNeshan = p.IsNeshan
            })
            .ToPageListAsync(pagination);
    }

    public Task<OutgoingPishnevisDto?> GetByIdAsync(int id, int userId) =>
        _db.OutgoingPishnevisLetters.AsNoTracking()
            .Where(p => p.PishnevisId == id && p.UserId == userId && !p.IsDelete)
            .Select(p => new OutgoingPishnevisDto
            {
                PishnevisId = p.PishnevisId,
                Title = p.Title,
                Text = p.Text,
                ReceiverOrganization = p.ReceiverOrganization,
                ReceiverName = p.ReceiverName,
                ReceiverTitle = p.ReceiverTitle,
                IsNeshan = p.IsNeshan
            })
            .FirstOrDefaultAsync();

    public async Task<int> AddAsync(OutgoingPishnevisDto dto, int userId)
    {
        if (string.IsNullOrWhiteSpace(dto.Title))
            throw new Exception("عنوان پیش‌نویس الزامی است.");

        var p = new OutgoingPishnevisLetter
        {
            Title = dto.Title.Trim(),
            Text = dto.Text ?? "",
            ReceiverOrganization = dto.ReceiverOrganization?.Trim(),
            ReceiverName = dto.ReceiverName?.Trim(),
            ReceiverTitle = dto.ReceiverTitle?.Trim(),
            UserId = userId,
            IsNeshan = dto.IsNeshan,
            IsDelete = false
        };
        _db.OutgoingPishnevisLetters.Add(p);
        await _db.SaveChangesAsync();
        return p.PishnevisId;
    }

    public async Task EditAsync(OutgoingPishnevisDto dto, int userId)
    {
        var p = await _db.OutgoingPishnevisLetters
            .FirstOrDefaultAsync(x => x.PishnevisId == dto.PishnevisId && x.UserId == userId && !x.IsDelete)
            ?? throw new Exception("پیش‌نویس پیدا نشد.");

        if (string.IsNullOrWhiteSpace(dto.Title))
            throw new Exception("عنوان پیش‌نویس الزامی است.");

        p.Title = dto.Title.Trim();
        p.Text = dto.Text ?? "";
        p.ReceiverOrganization = dto.ReceiverOrganization?.Trim();
        p.ReceiverName = dto.ReceiverName?.Trim();
        p.ReceiverTitle = dto.ReceiverTitle?.Trim();
        p.IsNeshan = dto.IsNeshan;
        await _db.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id, int userId)
    {
        var p = await _db.OutgoingPishnevisLetters
            .FirstOrDefaultAsync(x => x.PishnevisId == id && x.UserId == userId && !x.IsDelete)
            ?? throw new Exception("پیش‌نویس پیدا نشد.");
        p.IsDelete = true;
        await _db.SaveChangesAsync();
    }
}
