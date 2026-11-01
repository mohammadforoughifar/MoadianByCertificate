namespace Inventory.Shared.Dtos;

// ============================================================
//  ماژول هسته پرسنلی (کارگزینی) — HrCore — DTOها
// ============================================================

public class HrEmployeeDto
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string FullName => $"{FirstName} {LastName}".Trim();
    public string NationalCode { get; set; } = "";
    public DateTime? BirthDate { get; set; }
    public int Gender { get; set; }
    public int MaritalStatus { get; set; }
    public string? Mobile { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? Landline { get; set; }
    public string? PhotoPath { get; set; }
    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactRelation { get; set; }
    public string? EmergencyContactPhone { get; set; }
    public string? Workplace { get; set; }
    public string? Degree { get; set; }
    public string? FieldOfStudy { get; set; }
    public DateTime HireDate { get; set; }
    public int? OrgUnitId { get; set; }
    public string? OrgUnitName { get; set; }
    public string? PostTitle { get; set; }
    public int? HrMainNodeId { get; set; }
    public string? HrMainNodeName { get; set; }
    public int? HrMainPositionId { get; set; }
    public string? HrMainPositionTitle { get; set; }
    public int? ManagerId { get; set; }
    public string? ManagerName { get; set; }
    public int EmploymentType { get; set; }
    public int Status { get; set; }
    public int? SystemUserId { get; set; }
    public string? SystemUserName { get; set; }
    public decimal BaseSalary { get; set; }
    public bool IsActive { get; set; }
    /// <summary>قرارداد فعال جاری (اگر داشته باشد)</summary>
    public string? ActiveContractNo { get; set; }
    public DateTime? ActiveContractEnd { get; set; }
    public int ContractsCount { get; set; }
    public int DecreesCount { get; set; }
    public string? Sheba { get; set; }
    public string? BankName { get; set; }
    public string? InsuranceNo { get; set; }
}

public class HrEmployeeSaveDto
{
    public string? Code { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string NationalCode { get; set; } = "";
    public DateTime? BirthDate { get; set; }
    public int Gender { get; set; }
    public int MaritalStatus { get; set; }
    public string? Mobile { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? Landline { get; set; }
    public string? PhotoPath { get; set; }
    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactRelation { get; set; }
    public string? EmergencyContactPhone { get; set; }
    public string? Workplace { get; set; }
    public string? Degree { get; set; }
    public string? FieldOfStudy { get; set; }
    public DateTime HireDate { get; set; } = DateTime.Today;
    public int? OrgUnitId { get; set; }
    public string? PostTitle { get; set; }
    public int? HrMainNodeId { get; set; }
    public int? HrMainPositionId { get; set; }
    public int? ManagerId { get; set; }
    public int EmploymentType { get; set; } = 1;
    public int Status { get; set; }
    public int? SystemUserId { get; set; }
    public decimal BaseSalary { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Sheba { get; set; }
    public string? BankName { get; set; }
    public string? InsuranceNo { get; set; }
}

public class HrOrgUnitDto
{
    public int Id { get; set; }
    public int? ParentId { get; set; }
    public int Type { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? ParentName { get; set; }
    public int? ManagerEmployeeId { get; set; }
    public string? ManagerName { get; set; }
    public string? Phone { get; set; }
    public bool IsActive { get; set; }
    public int SortOrder { get; set; }
    public int EmployeeCount { get; set; }
    public List<HrOrgUnitDto> Children { get; set; } = new();
}

public class HrOrgUnitSaveDto
{
    public int? ParentId { get; set; }
    public int Type { get; set; } = 2;
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public int? ManagerEmployeeId { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}

public class HrContractDto
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public string EmployeeName { get; set; } = "";
    public string ContractNo { get; set; } = "";
    public int Type { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    /// <summary>روزهای مانده تا پایان (null = دائمی)</summary>
    public int? DaysToEnd { get; set; }
    public decimal BaseSalary { get; set; }
    public string? JobTitle { get; set; }
    public int? OrgUnitId { get; set; }
    public string? OrgUnitName { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; }
    public int? TemplateId { get; set; }
    public string? TemplateName { get; set; }
    public int SignStatus { get; set; }
    public string? EmployeeSignedBy { get; set; }
    public DateTime? EmployeeSignedAt { get; set; }
    public string? EmployerSignedByName { get; set; }
    public DateTime? EmployerSignedAt { get; set; }
    public int VersionsCount { get; set; }
}

public class HrContractSaveDto
{
    public int EmployeeId { get; set; }
    public string ContractNo { get; set; } = "";
    public int Type { get; set; } = 1;
    public DateTime StartDate { get; set; } = DateTime.Today;
    public DateTime? EndDate { get; set; }
    public decimal BaseSalary { get; set; }
    public string? JobTitle { get; set; }
    public int? OrgUnitId { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public int? TemplateId { get; set; }
    public string? ChangeNote { get; set; }
}

public class HrDecreeDto
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public string EmployeeName { get; set; } = "";
    public string DecreeNo { get; set; } = "";
    public int Type { get; set; }
    public DateTime EffectiveDate { get; set; }
    public string? NewPostTitle { get; set; }
    public int? NewOrgUnitId { get; set; }
    public string? NewOrgUnitName { get; set; }
    public decimal? NewBaseSalary { get; set; }
    public int? NewStatus { get; set; }
    public string? Description { get; set; }
    public bool IsApplied { get; set; }
    public DateTime? AppliedAt { get; set; }
    public string? CreatedByName { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class HrDecreeSaveDto
{
    public int EmployeeId { get; set; }
    public string DecreeNo { get; set; } = "";
    public int Type { get; set; }
    public DateTime EffectiveDate { get; set; } = DateTime.Today;
    public string? NewPostTitle { get; set; }
    public int? NewOrgUnitId { get; set; }
    public decimal? NewBaseSalary { get; set; }
    public int? NewStatus { get; set; }
    public string? Description { get; set; }
    /// <summary>اگر true و تاریخ اجرا رسیده باشد، حکم بلافاصله روی پرونده پرسنل اعمال می‌شود (پیش‌فرض true).</summary>
    public bool AutoApply { get; set; } = true;
}

/// <summary>ویجت داشبورد کارگزینی</summary>
public class HrDashboardDto
{
    public int ActiveEmployees { get; set; }
    public int OrgUnits { get; set; }
    public int ExpiringContracts { get; set; }
    public int PendingDecrees { get; set; }
    public List<HrContractDto> ExpiringList { get; set; } = new();
    public List<HrDecreeDto> RecentDecrees { get; set; } = new();
    public Dictionary<string, int> ByEmploymentType { get; set; } = new();
}

// ============================================================
//  پرونده کارمندان — تحت‌تکفل، دوره‌ها، مهارت‌ها، زبان‌ها، اسناد
// ============================================================

public class HrEmployeeDependentDto
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public string FullName { get; set; } = "";
    public string Relation { get; set; } = "";
    public DateTime? BirthDate { get; set; }
    public string? NationalCode { get; set; }
    public bool IsActive { get; set; }
}

public class HrEmployeeDependentSaveDto
{
    public string FullName { get; set; } = "";
    public string Relation { get; set; } = "";
    public DateTime? BirthDate { get; set; }
    public string? NationalCode { get; set; }
    public bool IsActive { get; set; } = true;
}

public class HrEmployeeCourseDto
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public string Title { get; set; } = "";
    public string? Institute { get; set; }
    public int? Year { get; set; }
    public int? DurationHours { get; set; }
    public bool HasCertificate { get; set; }
}

public class HrEmployeeCourseSaveDto
{
    public string Title { get; set; } = "";
    public string? Institute { get; set; }
    public int? Year { get; set; }
    public int? DurationHours { get; set; }
    public bool HasCertificate { get; set; }
}

public class HrEmployeeSkillDto
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public string Title { get; set; } = "";
    public int Level { get; set; }
}

public class HrEmployeeSkillSaveDto
{
    public string Title { get; set; } = "";
    public int Level { get; set; } = 1;
}

public class HrEmployeeLanguageDto
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public string Language { get; set; } = "";
    public int Level { get; set; }
}

public class HrEmployeeLanguageSaveDto
{
    public string Language { get; set; } = "";
    public int Level { get; set; } = 1;
}

public class HrEmployeeDocumentDto
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public string? EmployeeName { get; set; }
    public string Title { get; set; } = "";
    public int DocType { get; set; }
    public string? FilePath { get; set; }
    public string? FileName { get; set; }
    public string? ContentType { get; set; }
    public long? FileSize { get; set; }
    public DateTime? IssueDate { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public string? Notes { get; set; }
    /// <summary>روزهای مانده تا انقضا — خالی یعنی بدون انقضا</summary>
    public int? DaysToExpiry { get; set; }
    public bool IsExpired { get; set; }
    public bool IsExpiringSoon { get; set; }
}

public class HrEmployeeDocumentSaveDto
{
    public string Title { get; set; } = "";
    public int DocType { get; set; } = 9;
    public DateTime? IssueDate { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public string? Notes { get; set; }
}

/// <summary>پرونده کامل یک کارمند — تجمیع همه بخش‌ها در یک فراخوانی</summary>
public class HrEmployeeDossierDto
{
    public HrEmployeeDto Employee { get; set; } = new();
    public List<HrEmployeeDependentDto> Dependents { get; set; } = new();
    public List<HrEmployeeCourseDto> Courses { get; set; } = new();
    public List<HrEmployeeSkillDto> Skills { get; set; } = new();
    public List<HrEmployeeLanguageDto> Languages { get; set; } = new();
    public List<HrEmployeeDocumentDto> Documents { get; set; } = new();
    /// <summary>اسناد منقضی‌شده یا نزدیک به انقضا (۳۰ روز) — برای هشدار خودکار</summary>
    public List<HrEmployeeDocumentDto> ExpiringDocuments { get; set; } = new();
}

public class HrContractTemplateDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Type { get; set; }
    public int? DurationMonths { get; set; }
    public string? JobTitle { get; set; }
    public string? Terms { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; }
}

public class HrContractTemplateSaveDto
{
    public string Name { get; set; } = "";
    public int Type { get; set; } = 1;
    public int? DurationMonths { get; set; }
    public string? JobTitle { get; set; }
    public string? Terms { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public class HrContractVersionDto
{
    public int Id { get; set; }
    public int ContractId { get; set; }
    public int VersionNo { get; set; }
    public string ContractNo { get; set; } = "";
    public int Type { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public double BaseSalary { get; set; }
    public string? JobTitle { get; set; }
    public int? OrgUnitId { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; }
    public string? ChangedByName { get; set; }
    public DateTime ChangedAt { get; set; }
    public string? ChangeNote { get; set; }
}

public class HrSignNameDto
{
    public string Name { get; set; } = "";
}

public class HrContractAlertDto
{
    public int Id { get; set; }
    public int ContractId { get; set; }
    public string ContractNo { get; set; } = "";
    public string EmployeeName { get; set; } = "";
    public int ThresholdDays { get; set; }
    public DateTime ExpireDate { get; set; }
    public int DaysToEnd { get; set; }
    public int NotifiedCount { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class HrJobPostingDto
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public int? OrgUnitId { get; set; }
    public string? OrgUnitName { get; set; }
    public string? Description { get; set; }
    public string? Requirements { get; set; }
    public int? Headcount { get; set; }
    public int Status { get; set; }
    public DateTime? PublishDate { get; set; }
    public DateTime? ExpireDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public int ApplicantsCount { get; set; }
}

public class HrJobPostingSaveDto
{
    public string Title { get; set; } = "";
    public int? OrgUnitId { get; set; }
    public string? Description { get; set; }
    public string? Requirements { get; set; }
    public int? Headcount { get; set; }
    public int Status { get; set; } = 1;
    public DateTime? PublishDate { get; set; }
    public DateTime? ExpireDate { get; set; }
}

public class HrApplicantDto
{
    public int Id { get; set; }
    public int JobPostingId { get; set; }
    public string JobPostingTitle { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string FullName => $"{FirstName} {LastName}".Trim();
    public string? Mobile { get; set; }
    public string? Email { get; set; }
    public int Status { get; set; }
    public int? Score { get; set; }
    public string? Note { get; set; }
    public int? EmployeeId { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class HrApplicantSaveDto
{
    public int? JobPostingId { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string? Mobile { get; set; }
    public string? Email { get; set; }
    public int Status { get; set; }
    public int? Score { get; set; }
    public string? Note { get; set; }
}

public class HrInterviewDto
{
    public int Id { get; set; }
    public int ApplicantId { get; set; }
    public DateTime InterviewDate { get; set; }
    public string? InterviewerName { get; set; }
    public int? Score { get; set; }
    public int Result { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class HrInterviewSaveDto
{
    public int? ApplicantId { get; set; }
    public DateTime InterviewDate { get; set; } = DateTime.Today;
    public string? InterviewerName { get; set; }
    public int? Score { get; set; }
    public int Result { get; set; }
    public string? Note { get; set; }
}

public class HrConvertRequestDto
{
    public string NationalCode { get; set; } = "";
    public DateTime? HireDate { get; set; }
}

// ==================== تاریخچه عملیات HR (§۶) ====================

public class HrAuditLogDto
{
    public long Id { get; set; }
    public DateTime At { get; set; }
    public int? UserId { get; set; }
    public string? Username { get; set; }
    public string Module { get; set; } = "";
    public string Action { get; set; } = "";
    public string HttpMethod { get; set; } = "";
    public string? Path { get; set; }
    public string? Summary { get; set; }
    public string? Payload { get; set; }
    public string? Ip { get; set; }
    public int StatusCode { get; set; }
    public int DurationMs { get; set; }
}

public class HrAuditListResult
{
    public int Total { get; set; }
    public List<HrAuditLogDto> Items { get; set; } = new();
}

/// <summary>پرسنل سبک برای انتخاب گروهی (قرارداد/حکم گروهی)</summary>
public class HrEmployeeLiteDto
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string FullName { get; set; } = "";
    public int? HrMainNodeId { get; set; }
    public string? HrMainNodeName { get; set; }
    public bool IsActive { get; set; }
}

/// <summary>
/// دامنه‌ی دید «تیم من» برای کاربر بدون مجوز HrCore.Manage: شناسه‌ی گره‌های سازمانی (HrMain)
/// و واحدهای قدیمی که کاربر اجازه‌ی دید پرونده پرسنلِ آن‌ها را دارد (گره خودش + زیرمجموعه‌ها).
/// </summary>
public class HrTeamScopeDto
{
    public int EmployeeId { get; set; }
    public List<int> NodeIds { get; set; } = new();
    public List<int> OrgUnitIds { get; set; } = new();
}

/// <summary>نتیجه عملیات گروهی</summary>
public class HrBulkResultDto
{
    public int Created { get; set; }
    public List<int> CreatedIds { get; set; } = new();
}

/// <summary>صدور گروهی قرارداد: یک فرم برای چند پرسنل</summary>
public class HrContractBulkDto
{
    public List<int> EmployeeIds { get; set; } = new();
    public HrContractSaveDto Contract { get; set; } = new();
}

/// <summary>صدور گروهی حکم</summary>
public class HrDecreeBulkDto
{
    public List<int> EmployeeIds { get; set; } = new();
    public HrDecreeSaveDto Decree { get; set; } = new();
    /// <summary>اجرای خودکار احکام روی پرونده پرسنل بلافاصله پس از صدور</summary>
    public bool ApplyAfterCreate { get; set; }
}

/// <summary>خطای یک سطر در ورود اکسل پرسنل</summary>
public class HrEmployeeImportErrorDto
{
    public int Row { get; set; }
    public string Name { get; set; } = "";
    public string Message { get; set; } = "";
}

/// <summary>نتیجه ورود اکسل پرسنل</summary>
public class HrEmployeeImportResultDto
{
    public int Created { get; set; }
    public int Failed { get; set; }
    public List<HrEmployeeImportErrorDto> Errors { get; set; } = new();
}

/// <summary>نتیجه‌ی پاک‌سازی داده‌های منابع انسانی (حذف داده‌های آزمایشی)</summary>
public record HrPurgeResultDto(string Message, int Total, Dictionary<string, int> Areas, string? Warning);
