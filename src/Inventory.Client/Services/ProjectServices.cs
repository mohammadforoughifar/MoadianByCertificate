using Inventory.Shared.Dtos;

namespace Inventory.Client.Services;

// =====================================================================
// سرویس‌های ماژول مدیریت پروژه‌ها (سمت کلاینت)
// =====================================================================

/// <summary>سرویس کارفرماها.</summary>
public interface IKarfarmaService
{
    Task<PagedResult<KarFarmaDto>> GetPagedAsync(int skip, int take, string? search = null);
    Task<List<KarFarmaDto>> GetAllAsync(string? search = null);
    Task<KarFarmaDto> GetAsync(int id);
    Task<KarFarmaDto> CreateAsync(KarFarmaDto dto);
    Task<KarFarmaDto> UpdateAsync(int id, KarFarmaDto dto);
    Task DeleteAsync(int id);
}

/// <summary>سرویس انواع فاکتور.</summary>
public interface ITypeFactorService
{
    Task<PagedResult<TypeFactorDto>> GetPagedAsync(int skip, int take, string? search = null);
    Task<List<TypeFactorDto>> GetAllAsync(string? search = null);
    Task<TypeFactorDto> CreateAsync(TypeFactorDto dto);
    Task<TypeFactorDto> UpdateAsync(int id, TypeFactorDto dto);
    Task DeleteAsync(int id);
}

/// <summary>سرویس ورود و خروج پروژه‌ها.</summary>
public interface IProjectService
{
    /// <param name="withProjects">
    /// false = فهرست کامل پروژه‌ها در پاسخ نیاید (صفحه‌هایی که کمبوی پروژه را با جستجوی سروری پر می‌کنند).
    /// </param>
    Task<ProjectLookups> GetLookupsAsync(bool withProjects = true);

    /// <summary>
    /// جستجوی سمت سرور پروژه‌ها برای کمبو — فقط <paramref name="take"/> نتیجهٔ منطبق می‌آید
    /// (نه کل جدول پروژه‌ها). اگر <paramref name="reportable"/> روشن باشد فقط پروژه‌های
    /// تأییدشدهٔ مدیر برمی‌گردند (همان شرط فرم گزارش کار).
    /// </summary>
    Task<List<LookupItem>> SearchProjectsAsync(string? q, int take = 20, bool reportable = false);
    Task<string> GetNextSerialAsync();
    Task<List<ProjectEntryExitDto>> GetAllAsync(string? search = null, int? karfarmaId = null,
        int? typeFactorId = null, int? userId = null, bool? returned = null);

    /// <summary>لیست صفحه‌بندی‌شده سمت سرور (فقط ردیف‌های همان صفحه از دیتابیس خوانده می‌شوند).</summary>
    Task<PagedResult<ProjectEntryExitDto>> GetPagedAsync(ProjectListQuery query);

    /// <summary>همان لیست با امکان لغو درخواست (جستجوی زندهٔ سرستون‌ها).</summary>
    Task<PagedResult<ProjectEntryExitDto>> GetPagedAsync(ProjectListQuery query, CancellationToken cancellationToken);

    /// <summary>کدهای برگشتی (RE) یک پروژه — برای نمایش در ستون «برگشتی» بدون خواندن کل لیست.</summary>
    Task<ProjectReturnsDto?> GetReturnsAsync(int id);

    /// <summary>آدرس دانلود اکسل با همان فیلترهای جاری لیست.</summary>
    string BuildExportUrl(ProjectListQuery query);

    Task<ProjectEntryExitDto> GetAsync(int id);
    /// <summary>ایجاد پروژه — خروجی: شناسه + کد پروژه صادرشده در سرور</summary>
    Task<(int Id, string Code)> CreateAsync(ProjectEntryExitDto dto);

    /// <summary>ویرایش پروژه — اگر کاربر مدیر نباشد، فقط «درخواست ویرایش» ثبت می‌شود (Pending=true).</summary>
    Task<ChangeActionResult> UpdateAsync(int id, ProjectEntryExitDto dto);

    /// <summary>حذف پروژه — اگر کاربر مدیر نباشد، فقط «درخواست حذف» ثبت می‌شود (Pending=true).</summary>
    Task<ChangeActionResult> DeleteAsync(int id);

    /// <summary>ثبت/ویرایش اطلاعات فاکتور (فرم مجزا)</summary>
    Task UpdateFactorAsync(int id, ProjectFactorDto dto);

    /// <summary>ثبت/ویرایش تاریخ‌های چرخهٔ پروژه (خروج، خروج موقت، نیاز مشتری، تحویل پروژه، تحویل پرونده)</summary>
    Task UpdateDatesAsync(int id, ProjectDatesDto dto);
}

/// <summary>سرویس گزارش‌های کار.</summary>
public interface IReportWorkService
{
    Task<List<ReportWorkDto>> GetAllAsync(int? projectId = null, int? userId = null,
        DateTime? from = null, DateTime? to = null, int? operatorId = null);

    /// <summary>لیست صفحه‌بندی‌شده سمت سرور با فیلتر سرستون‌ها.</summary>
    Task<PagedResult<ReportWorkDto>> GetPagedAsync(ReportWorkListQuery query);

    /// <summary>همان لیست با امکان لغو درخواست (جستجوی زندهٔ سرستون‌ها).</summary>
    Task<PagedResult<ReportWorkDto>> GetPagedAsync(ReportWorkListQuery query, CancellationToken cancellationToken);

    /// <summary>آدرس دانلود اکسل گزارش‌های کار با همان فیلترهای جاری.</summary>
    string BuildExportUrl(ReportWorkListQuery query);

    Task CreateAsync(ReportWorkDto dto);
    Task UpdateAsync(int id, ReportWorkDto dto);
    Task DeleteAsync(int id);
}

// =====================================================================
// پیاده‌سازی‌ها
// =====================================================================

public class KarfarmaService : IKarfarmaService
{
    private readonly IApiClient _api;
    public KarfarmaService(IApiClient api) => _api = api;

    public Task<PagedResult<KarFarmaDto>> GetPagedAsync(int skip, int take, string? search = null)
        => ListOrPaged.GetPagedAsync<KarFarmaDto>(_api, $"api/karfarmas?skip={skip}&take={take}&search={Uri.EscapeDataString(search ?? "")}");

    public Task<List<KarFarmaDto>> GetAllAsync(string? search = null)
        => _api.GetAsync<List<KarFarmaDto>>($"api/karfarmas?search={Uri.EscapeDataString(search ?? "")}");

    public Task<KarFarmaDto> GetAsync(int id)
        => _api.GetAsync<KarFarmaDto>($"api/karfarmas/{id}");

    public Task<KarFarmaDto> CreateAsync(KarFarmaDto dto)
        => _api.PostAsync<KarFarmaDto>("api/karfarmas", dto);

    public Task<KarFarmaDto> UpdateAsync(int id, KarFarmaDto dto)
        => _api.PutAsync<KarFarmaDto>($"api/karfarmas/{id}", dto);

    public Task DeleteAsync(int id)
        => _api.DeleteAsync($"api/karfarmas/{id}");
}

public class TypeFactorService : ITypeFactorService
{
    private readonly IApiClient _api;
    public TypeFactorService(IApiClient api) => _api = api;

    public Task<PagedResult<TypeFactorDto>> GetPagedAsync(int skip, int take, string? search = null)
        => ListOrPaged.GetPagedAsync<TypeFactorDto>(_api, $"api/typefactors?skip={skip}&take={take}&search={Uri.EscapeDataString(search ?? "")}");

    public Task<List<TypeFactorDto>> GetAllAsync(string? search = null)
        => _api.GetAsync<List<TypeFactorDto>>($"api/typefactors?search={Uri.EscapeDataString(search ?? "")}");

    public Task<TypeFactorDto> CreateAsync(TypeFactorDto dto)
        => _api.PostAsync<TypeFactorDto>("api/typefactors", dto);

    public Task<TypeFactorDto> UpdateAsync(int id, TypeFactorDto dto)
        => _api.PutAsync<TypeFactorDto>($"api/typefactors/{id}", dto);

    public Task DeleteAsync(int id)
        => _api.DeleteAsync($"api/typefactors/{id}");
}

public class ProjectService : IProjectService
{
    private readonly IApiClient _api;
    public ProjectService(IApiClient api) => _api = api;

    public Task<ProjectLookups> GetLookupsAsync(bool withProjects = true)
        => _api.GetAsync<ProjectLookups>($"api/projects/lookups?withProjects={(withProjects ? "true" : "false")}");

    public async Task<List<LookupItem>> SearchProjectsAsync(string? q, int take = 20, bool reportable = false)
    {
        var url = $"api/projects/lookup?q={Uri.EscapeDataString(q ?? "")}&take={take}&reportable={(reportable ? "true" : "false")}";
        return await _api.GetAsync<List<LookupItem>>(url) ?? new();
    }

    public async Task<string> GetNextSerialAsync()
    {
        var res = await _api.GetAsync<NextSerialResponse>("api/projects/next-serial");
        return res?.Next ?? "";
    }

    private class NextSerialResponse { public string Next { get; set; } = ""; }

    /// <summary>سازگاری با کدهای قدیمی — همهٔ ردیف‌ها را یکجا می‌گیرد (PageSize=0).</summary>
    public async Task<List<ProjectEntryExitDto>> GetAllAsync(string? search = null, int? karfarmaId = null,
        int? typeFactorId = null, int? userId = null, bool? returned = null)
    {
        var q = new ProjectListQuery
        {
            Search = search,
            KarfarmaId = karfarmaId,
            TypeFactorId = typeFactorId,
            UserId = userId,
            Returned = returned,
            PageSize = 0
        };
        var res = await GetPagedAsync(q);
        return res.Items;
    }

    public async Task<PagedResult<ProjectEntryExitDto>> GetPagedAsync(ProjectListQuery query)
        => await GetPagedAsync(query, CancellationToken.None);

    public async Task<PagedResult<ProjectEntryExitDto>> GetPagedAsync(ProjectListQuery query, CancellationToken cancellationToken)
    {
        var qs = (query ?? new ProjectListQuery()).ToQueryString();
        var res = await _api.GetAsync<PagedResult<ProjectEntryExitDto>>($"api/projects?{qs}", cancellationToken);
        return res ?? new PagedResult<ProjectEntryExitDto>();
    }

    public Task<ProjectReturnsDto?> GetReturnsAsync(int id)
        => _api.GetAsync<ProjectReturnsDto?>($"api/projects/{id}/returns");

    public string BuildExportUrl(ProjectListQuery query)
        => _api.BuildUrl($"api/projects/export?{(query ?? new ProjectListQuery()).ToQueryString()}");

    public Task<ProjectEntryExitDto> GetAsync(int id)
        => _api.GetAsync<ProjectEntryExitDto>($"api/projects/{id}");

    public async Task<(int Id, string Code)> CreateAsync(ProjectEntryExitDto dto)
    {
        var res = await _api.PostAsync<CreateResponse>("api/projects", dto);
        return (res.Id, res.CodeProject ?? res.Id.ToString());
    }

    private class CreateResponse { public int Id { get; set; } public string? CodeProject { get; set; } }

    public async Task<ChangeActionResult> UpdateAsync(int id, ProjectEntryExitDto dto)
    {
        var res = await _api.PutAsync<ChangeActionResult>($"api/projects/{id}", dto);
        return res ?? new ChangeActionResult { Id = id };
    }

    public async Task<ChangeActionResult> DeleteAsync(int id)
    {
        var res = await _api.DeleteAsync<ChangeActionResult>($"api/projects/{id}");
        return res ?? new ChangeActionResult { Id = id };
    }

    public Task UpdateFactorAsync(int id, ProjectFactorDto dto)
        => _api.PutAsync<object>($"api/projects/{id}/factor", dto);

    public Task UpdateDatesAsync(int id, ProjectDatesDto dto)
        => _api.PutAsync<object>($"api/projects/{id}/dates", dto);

    private class IdResponse { public int Id { get; set; } }
}

public class ReportWorkService : IReportWorkService
{
    private readonly IApiClient _api;
    public ReportWorkService(IApiClient api) => _api = api;

    /// <summary>سازگاری با کدهای قدیمی — همهٔ ردیف‌ها را یکجا می‌گیرد (PageSize=0).</summary>
    public async Task<List<ReportWorkDto>> GetAllAsync(int? projectId = null, int? userId = null,
        DateTime? from = null, DateTime? to = null, int? operatorId = null)

    {
        var res = await GetPagedAsync(new ReportWorkListQuery
        {
            ProjectId = projectId,
            UserId = userId,
            OperatorId = operatorId,
            From = from,
            To = to,
            PageSize = 0
        });
        return res.Items;

    }

    public async Task<PagedResult<ReportWorkDto>> GetPagedAsync(ReportWorkListQuery query)
        => await GetPagedAsync(query, CancellationToken.None);

    public async Task<PagedResult<ReportWorkDto>> GetPagedAsync(ReportWorkListQuery query, CancellationToken cancellationToken)
    {
        var qs = (query ?? new ReportWorkListQuery()).ToQueryString();
        var res = await _api.GetAsync<PagedResult<ReportWorkDto>>($"api/reportworks?{qs}", cancellationToken);
        return res ?? new PagedResult<ReportWorkDto>();
    }

    public string BuildExportUrl(ReportWorkListQuery query)
        => _api.BuildUrl($"api/reportworks/export?{(query ?? new ReportWorkListQuery()).ToQueryString()}");

    public Task CreateAsync(ReportWorkDto dto)
        => _api.PostAsync<object>("api/reportworks", dto);

    public Task UpdateAsync(int id, ReportWorkDto dto)
        => _api.PutAsync<object>($"api/reportworks/{id}", dto);

    public Task DeleteAsync(int id)
        => _api.DeleteAsync($"api/reportworks/{id}");
}

// =====================================================================
// کارتابل مدیریت پروژه (صف مدیر + صف کارشناسی)
// =====================================================================

/// <summary>آیتم کارتابل پروژه — متناظر با خروجی GET api/projectcartable/queue</summary>
public class ProjectCartableItem
{
    public int Id { get; set; }
    public string CodeProject { get; set; } = "";
    public int ReturnProjectId { get; set; }
    public string SerialNumber { get; set; } = "";
    public string ProjectName { get; set; } = "";
    public string? KarFarmaName { get; set; }
    public DateTime? EntryDate { get; set; }
    public DateTime? ExitDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? RegisterUser { get; set; }
    public int DaysWaiting { get; set; }
    public int AttachCount { get; set; }
}

/// <summary>سرویس کارتابل پروژه (صف مدیر و صف کارشناسی + اقدام‌های تایید/رد/اتمام).</summary>
public interface IProjectCartableService
{
    Task<ProjectCartableCountsDto?> GetCountsAsync();
    Task<List<ProjectCartableItem>> GetQueueAsync(string kind);
    Task ApproveAsync(int id, string? note);
    Task RejectAsync(int id, string note);
    Task ExpertDoneAsync(int id, string? note);

    /// <summary>ارسال مجدد پروژهٔ ردشده به کارتابل مدیر (بعد از اصلاح)</summary>
    Task ResubmitAsync(int id, string? note);

    /// <summary>صف «درخواست‌های ویرایش/حذف» در انتظار تایید مدیر</summary>
    Task<List<ProjectChangeRequestDto>> GetChangeRequestsAsync();

    /// <summary>تایید درخواست ویرایش/حذف → تغییر اعمال می‌شود</summary>
    Task ApproveChangeAsync(int id, string? note);

    /// <summary>رد درخواست ویرایش/حذف (دلیل الزامی)</summary>
    Task RejectChangeAsync(int id, string note);
}

public class ProjectCartableService : IProjectCartableService
{
    private readonly IApiClient _api;
    public ProjectCartableService(IApiClient api) => _api = api;

    public Task<ProjectCartableCountsDto?> GetCountsAsync()
        => _api.GetAsync<ProjectCartableCountsDto?>("api/projectcartable/counts");

    public Task<List<ProjectCartableItem>> GetQueueAsync(string kind)
        => _api.GetAsync<List<ProjectCartableItem>>($"api/projectcartable/queue?kind={Uri.EscapeDataString(kind)}");

    public Task ApproveAsync(int id, string? note)
        => _api.PostAsync<object>($"api/projectcartable/{id}/approve", new ProjectFlowActionDto { Note = note });

    public Task RejectAsync(int id, string note)
        => _api.PostAsync<object>($"api/projectcartable/{id}/reject", new ProjectFlowActionDto { Note = note });

    public Task ExpertDoneAsync(int id, string? note)
        => _api.PostAsync<object>($"api/projectcartable/{id}/expert-done", new ProjectFlowActionDto { Note = note });

    public Task ResubmitAsync(int id, string? note)
        => _api.PostAsync<object>($"api/projectcartable/{id}/resubmit", new ProjectFlowActionDto { Note = note });

    public async Task<List<ProjectChangeRequestDto>> GetChangeRequestsAsync()
        => await _api.GetAsync<List<ProjectChangeRequestDto>>("api/projectcartable/changes")
           ?? new List<ProjectChangeRequestDto>();

    public Task ApproveChangeAsync(int id, string? note)
        => _api.PostAsync<object>($"api/projectcartable/changes/{id}/approve", new ProjectFlowActionDto { Note = note });

    public Task RejectChangeAsync(int id, string note)
        => _api.PostAsync<object>($"api/projectcartable/changes/{id}/reject", new ProjectFlowActionDto { Note = note });
}
