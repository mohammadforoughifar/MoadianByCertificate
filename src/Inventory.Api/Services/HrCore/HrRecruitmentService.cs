using Inventory.Api.Services;
using Inventory.Api.Data;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services.HrCore;

/// <summary>جذب و استخدام (§۴.۱): آگهی، متقاضی، مصاحبه و تبدیل یک‌کلیکه به پرسنل.</summary>
public interface IHrRecruitmentService
{
    Task<List<HrJobPostingDto>> ListPostingsAsync(bool? onlyOpen, Paging.Request? pagination = null);
    Task<HrJobPostingDto?> GetPostingAsync(int id);
    Task<HrJobPostingDto> SavePostingAsync(int? id, HrJobPostingSaveDto dto);
    Task DeletePostingAsync(int id);

    Task<List<HrApplicantDto>> ListApplicantsAsync(int? postingId, int? status, Paging.Request? pagination = null);
    Task<HrApplicantDto?> GetApplicantAsync(int id);
    Task<HrApplicantDto> SaveApplicantAsync(int? id, HrApplicantSaveDto dto);
    Task<HrApplicantDto> MoveApplicantAsync(int id, int status);
    Task DeleteApplicantAsync(int id);
    Task<HrApplicantDto> ConvertToEmployeeAsync(int id, string nationalCode, DateTime? hireDate);

    Task<List<HrInterviewDto>> ListInterviewsAsync(int applicantId, Paging.Request? pagination = null);
    Task<HrInterviewDto> SaveInterviewAsync(int? id, HrInterviewSaveDto dto);
    Task DeleteInterviewAsync(int id);
}

public class HrRecruitmentService : IHrRecruitmentService
{
    private readonly AppDbContext _db;
    private readonly IHrCoreService _core;

    public HrRecruitmentService(AppDbContext db, IHrCoreService core) => (_db, _core) = (db, core);

    // ================== آگهی ==================

    public async Task<List<HrJobPostingDto>> ListPostingsAsync(bool? onlyOpen, Paging.Request? pagination = null)
    {
        var q = _db.HrJobPostings.AsNoTracking().AsQueryable();
        if (onlyOpen == true) q = q.Where(p => p.Status == 1);
        var rows = await q.OrderByDescending(p => p.Id).ToPageListAsync(pagination, defaultCap: 200);
        var list = new List<HrJobPostingDto>();
        foreach (var p in rows)
        {
            var orgName = p.OrgUnitId is > 0
                ? await _db.HrOrgUnits.Where(u => u.Id == p.OrgUnitId!.Value).Select(u => u.Name).FirstOrDefaultAsync()
                : null;
            list.Add(new HrJobPostingDto
            {
                Id = p.Id, Title = p.Title, OrgUnitId = p.OrgUnitId, OrgUnitName = orgName,
                Description = p.Description, Requirements = p.Requirements, Headcount = p.Headcount,
                Status = p.Status, PublishDate = p.PublishDate, ExpireDate = p.ExpireDate,
                CreatedAt = p.CreatedAt,
                ApplicantsCount = await _db.HrApplicants.CountAsync(a => a.JobPostingId == p.Id)
            });
        }
        return list;
    }

    public async Task<HrJobPostingDto?> GetPostingAsync(int id)
    {
        var p = await _db.HrJobPostings.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (p == null) return null;
        var orgName = p.OrgUnitId is > 0
            ? await _db.HrOrgUnits.Where(u => u.Id == p.OrgUnitId!.Value).Select(u => u.Name).FirstOrDefaultAsync()
            : null;
        return new HrJobPostingDto
        {
            Id = p.Id, Title = p.Title, OrgUnitId = p.OrgUnitId, OrgUnitName = orgName,
            Description = p.Description, Requirements = p.Requirements, Headcount = p.Headcount,
            Status = p.Status, PublishDate = p.PublishDate, ExpireDate = p.ExpireDate,
            CreatedAt = p.CreatedAt,
            ApplicantsCount = await _db.HrApplicants.CountAsync(a => a.JobPostingId == p.Id)
        };
    }

    public async Task<HrJobPostingDto> SavePostingAsync(int? id, HrJobPostingSaveDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Title)) throw new InvalidOperationException("عنوان آگهی الزامی است.");
        if (dto.Status is < 0 or > 2) throw new InvalidOperationException("وضعیت آگهی نامعتبر است.");
        HrJobPosting p;
        if (id is > 0)
        {
            p = await _db.HrJobPostings.FirstOrDefaultAsync(x => x.Id == id.Value)
                ?? throw new InvalidOperationException("آگهی یافت نشد.");
        }
        else
        {
            p = new HrJobPosting();
            _db.HrJobPostings.Add(p);
        }
        p.Title = dto.Title.Trim();
        p.OrgUnitId = dto.OrgUnitId;
        p.Description = dto.Description?.Trim();
        p.Requirements = dto.Requirements?.Trim();
        p.Headcount = dto.Headcount;
        p.Status = dto.Status;
        p.PublishDate = dto.PublishDate;
        p.ExpireDate = dto.ExpireDate;
        await _db.SaveChangesAsync();
        return (await GetPostingAsync(p.Id))!;
    }

    public async Task DeletePostingAsync(int id)
    {
        var p = await _db.HrJobPostings.FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException("آگهی یافت نشد.");
        var n = await _db.HrApplicants.CountAsync(a => a.JobPostingId == id);
        if (n > 0) throw new InvalidOperationException($"این آگهی {n} متقاضی دارد و قابل حذف نیست.");
        _db.HrJobPostings.Remove(p);
        await _db.SaveChangesAsync();
    }

    // ================== متقاضی ==================

    public async Task<List<HrApplicantDto>> ListApplicantsAsync(int? postingId, int? status, Paging.Request? pagination = null)
    {
        var q = _db.HrApplicants.AsNoTracking().AsQueryable();
        if (postingId is > 0) q = q.Where(a => a.JobPostingId == postingId.Value);
        if (status is >= 0) q = q.Where(a => a.Status == status.Value);
        var rows = await q.OrderByDescending(a => a.Id).ToPageListAsync(pagination, defaultCap: 500);
        var posts = await _db.HrJobPostings.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.Title);
        return rows.Select(a => new HrApplicantDto
        {
            Id = a.Id, JobPostingId = a.JobPostingId,
            JobPostingTitle = posts.TryGetValue(a.JobPostingId, out var t) ? t : "",
            FirstName = a.FirstName, LastName = a.LastName,
            Mobile = a.Mobile, Email = a.Email, Status = a.Status, Score = a.Score,
            Note = a.Note, EmployeeId = a.EmployeeId, CreatedAt = a.CreatedAt
        }).ToList();
    }

    public async Task<HrApplicantDto?> GetApplicantAsync(int id)
    {
        var a = await _db.HrApplicants.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (a == null) return null;
        var title = await _db.HrJobPostings.AsNoTracking().Where(p => p.Id == a.JobPostingId).Select(p => p.Title).FirstOrDefaultAsync() ?? "";
        return new HrApplicantDto
        {
            Id = a.Id, JobPostingId = a.JobPostingId, JobPostingTitle = title,
            FirstName = a.FirstName, LastName = a.LastName,
            Mobile = a.Mobile, Email = a.Email, Status = a.Status, Score = a.Score,
            Note = a.Note, EmployeeId = a.EmployeeId, CreatedAt = a.CreatedAt
        };
    }

    public async Task<HrApplicantDto> SaveApplicantAsync(int? id, HrApplicantSaveDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.FirstName) || string.IsNullOrWhiteSpace(dto.LastName))
            throw new InvalidOperationException("نام و نام خانوادگی متقاضی الزامی است.");
        HrApplicant a;
        if (id is > 0)
        {
            a = await _db.HrApplicants.FirstOrDefaultAsync(x => x.Id == id.Value)
                ?? throw new InvalidOperationException("متقاضی یافت نشد.");
        }
        else
        {
            if (dto.JobPostingId is not > 0 || !await _db.HrJobPostings.AnyAsync(p => p.Id == dto.JobPostingId))
                throw new InvalidOperationException("آگهی نامعتبر است.");
            a = new HrApplicant { JobPostingId = dto.JobPostingId!.Value };
            _db.HrApplicants.Add(a);
        }
        a.FirstName = dto.FirstName.Trim();
        a.LastName = dto.LastName.Trim();
        a.Mobile = dto.Mobile?.Trim();
        a.Email = dto.Email?.Trim();
        if (dto.Status is >= 0 and <= 6 && dto.Status != 6) a.Status = dto.Status;
        a.Score = ClampScore(dto.Score);
        a.Note = dto.Note?.Trim();
        await _db.SaveChangesAsync();
        return (await GetApplicantAsync(a.Id))!;
    }

    public async Task<HrApplicantDto> MoveApplicantAsync(int id, int status)
    {
        if (status is < 0 or > 6) throw new InvalidOperationException("وضعیت نامعتبر است.");
        if (status == 6) throw new InvalidOperationException("برای استخدام از دکمه «تبدیل به پرسنل» استفاده کنید.");
        var a = await _db.HrApplicants.FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException("متقاضی یافت نشد.");
        if (a.EmployeeId is > 0) throw new InvalidOperationException("این متقاضی قبلاً استخدام شده است.");
        a.Status = status;
        await _db.SaveChangesAsync();
        return (await GetApplicantAsync(id))!;
    }

    public async Task DeleteApplicantAsync(int id)
    {
        var a = await _db.HrApplicants.FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException("متقاضی یافت نشد.");
        if (a.EmployeeId is > 0) throw new InvalidOperationException("متقاضی استخدام‌شده قابل حذف نیست.");
        _db.HrInterviews.RemoveRange(_db.HrInterviews.Where(i => i.ApplicantId == id));
        _db.HrApplicants.Remove(a);
        await _db.SaveChangesAsync();
    }

    public async Task<HrApplicantDto> ConvertToEmployeeAsync(int id, string nationalCode, DateTime? hireDate)
    {
        var a = await _db.HrApplicants.FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException("متقاضی یافت نشد.");
        if (a.EmployeeId is > 0) throw new InvalidOperationException("این متقاضی قبلاً به پرسنل تبدیل شده است.");
        if (a.Status == 5) throw new InvalidOperationException("متقاضی ردشده قابل تبدیل نیست.");
        var emp = await _core.CreateEmployeeAsync(new HrEmployeeSaveDto
        {
            FirstName = a.FirstName, LastName = a.LastName, NationalCode = (nationalCode ?? "").Trim(),
            Mobile = a.Mobile, Email = a.Email, HireDate = hireDate ?? DateTime.Today
        });
        a.EmployeeId = emp.Id;
        a.Status = 6;
        await _db.SaveChangesAsync();
        return (await GetApplicantAsync(id))!;
    }

    // ================== مصاحبه ==================

    public async Task<List<HrInterviewDto>> ListInterviewsAsync(int applicantId, Paging.Request? pagination = null)
    {
        var rows = await _db.HrInterviews.AsNoTracking().Where(i => i.ApplicantId == applicantId)
            .OrderBy(i => i.InterviewDate).ThenBy(i => i.Id).ToPageListAsync(pagination, defaultCap: 100);
        return rows.Select(i => new HrInterviewDto
        {
            Id = i.Id, ApplicantId = i.ApplicantId, InterviewDate = i.InterviewDate,
            InterviewerName = i.InterviewerName, Score = i.Score, Result = i.Result,
            Note = i.Note, CreatedAt = i.CreatedAt
        }).ToList();
    }

    public async Task<HrInterviewDto> SaveInterviewAsync(int? id, HrInterviewSaveDto dto)
    {
        HrInterview i;
        if (id is > 0)
        {
            i = await _db.HrInterviews.FirstOrDefaultAsync(x => x.Id == id.Value)
                ?? throw new InvalidOperationException("مصاحبه یافت نشد.");
        }
        else
        {
            if (dto.ApplicantId is not > 0 || !await _db.HrApplicants.AnyAsync(a => a.Id == dto.ApplicantId))
                throw new InvalidOperationException("متقاضی نامعتبر است.");
            i = new HrInterview { ApplicantId = dto.ApplicantId!.Value };
            _db.HrInterviews.Add(i);
        }
        i.InterviewDate = dto.InterviewDate == default ? DateTime.Today : dto.InterviewDate;
        i.InterviewerName = dto.InterviewerName?.Trim();
        i.Score = ClampScore(dto.Score);
        i.Result = dto.Result is >= 0 and <= 3 ? dto.Result : 0;
        i.Note = dto.Note?.Trim();
        await _db.SaveChangesAsync();
        return (await ListInterviewsAsync(i.ApplicantId)).First(x => x.Id == i.Id);
    }

    public async Task DeleteInterviewAsync(int id)
    {
        var i = await _db.HrInterviews.FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException("مصاحبه یافت نشد.");
        _db.HrInterviews.Remove(i);
        await _db.SaveChangesAsync();
    }

    private static int? ClampScore(int? s) => s == null ? null : Math.Max(0, Math.Min(100, s.Value));
}
