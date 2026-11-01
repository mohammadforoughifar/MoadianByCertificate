using Microsoft.EntityFrameworkCore;
using Inventory.Shared.Entities;

namespace Inventory.Api.Data;

public class AppDbContext : DbContext
{
    private readonly IHttpContextAccessor? _http;

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public AppDbContext(DbContextOptions<AppDbContext> options, IHttpContextAccessor? http) : base(options)
    {
        _http = http;
    }

    // ==================== زمینهٔ شرکت فعال (چند شرکتی) ====================

    /// <summary>
    /// شناسهٔ شرکت فعالِ درخواست جاری. میان‌افزار <c>ActiveCompanyMiddleware</c> آن را پس از
    /// احراز هویت از هدر <c>X-Company-Id</c> (با اعتبارسنجی دسترسی کاربر) در
    /// <c>HttpContext.Items</c> می‌گذارد.
    /// <list type="bullet">
    /// <item>مقدار null یعنی «زمینهٔ شرکتی تعیین نشده» (اجرای بدون HTTP مثل بذرکاری دیتابیس) —
    /// در این حالت هیچ فیلتری اعمال نمی‌شود تا کارهای پس‌زمینه و نصب اولیه نشکنند.</item>
    /// <item>برای کاربر واردشده، همیشه مقدار دارد؛ پس همهٔ داده‌ها به همان شرکت محدود می‌شوند.</item>
    /// </list>
    /// </summary>
    public int? ActiveCompanyId => ReadActiveCompanyId();

    private int? ReadActiveCompanyId()
    {
        var items = _http?.HttpContext?.Items;
        if (items is not null && items.TryGetValue("ActiveCompanyId", out var v) && v is int id && id >= 0)
            return id; // ۰ = کاربر به هیچ شرکتی دسترسی ندارد → هیچ رکوردی نباید دیده/ساخته شود
        return null;
    }

    private bool _defaultCompanyResolved;
    private int? _defaultCompanyId;

    /// <summary>شرکت پیش‌فرض سیستم (اولین شرکت فعال) — برای رکوردهایی که بیرون از درخواست ساخته می‌شوند.</summary>
    public int? ResolveDefaultCompanyId()
    {
        if (_defaultCompanyResolved) return _defaultCompanyId;
        _defaultCompanyResolved = true;
        try
        {
            _defaultCompanyId = SystemCompanies.AsNoTracking()
                .Where(c => c.IsActive)
                .OrderBy(c => c.Id)
                .Select(c => (int?)c.Id)
                .FirstOrDefault();
        }
        catch
        {
            // جدول شرکت‌ها هنوز ساخته نشده است (نصب اولیه) — رکورد بدون شرکت ذخیره می‌شود.
            _defaultCompanyId = null;
        }
        return _defaultCompanyId;
    }

    /// <summary>
    /// مقداردهی خودکار CompanyId برای رکوردهای تازه و جلوگیری از تغییر شرکتِ رکورد در ویرایش.
    /// </summary>
    private void ApplyCompanyScopeOnSave()
    {
        var active = ActiveCompanyId;
        foreach (var entry in ChangeTracker.Entries<ICompanyOwned>())
        {
            if (entry.State == EntityState.Added)
            {
                if ((entry.Entity.CompanyId ?? 0) <= 0)
                {
                    // کاربرِ بدون شرکت (۰) اجازهٔ ساخت رکورد ندارد؛ زمینهٔ تعیین‌نشده (null) → شرکت پیش‌فرض
                    entry.Entity.CompanyId = active is > 0 ? active
                        : active == 0 ? null
                        : ResolveDefaultCompanyId();
                }
            }
            else if (entry.State == EntityState.Modified)
            {
                // جابه‌جایی رکورد بین شرکت‌ها از راه ویرایش فرم ممکن نیست
                entry.Property(nameof(ICompanyOwned.CompanyId)).IsModified = false;
            }
        }
    }

    public override int SaveChanges()
    {
        ApplyCompanyScopeOnSave();
        return base.SaveChanges();
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplyCompanyScopeOnSave();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyCompanyScopeOnSave();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ApplyCompanyScopeOnSave();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    public DbSet<DocTemporaryGrant> DocTemporaryGrants => Set<DocTemporaryGrant>();
    public DbSet<DocRenewalPolicy> DocRenewalPolicies => Set<DocRenewalPolicy>();
    public DbSet<DocRenewalRun> DocRenewalRuns => Set<DocRenewalRun>();
    public DbSet<DocIndexJob> DocIndexJobs => Set<DocIndexJob>();
    public DbSet<User> Users => Set<User>();
    public DbSet<UserContactOtpChallenge> UserContactOtpChallenges => Set<UserContactOtpChallenge>();

    // ---------- گزارش‌ساز حرفه‌ای ----------
    public DbSet<RsReport> RsReports => Set<RsReport>();
    public DbSet<RsReportUserShare> RsReportUserShares => Set<RsReportUserShare>();
    public DbSet<RsReportRoleShare> RsReportRoleShares => Set<RsReportRoleShare>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();

    // ==================== داشبورد شخصی کاربر ====================
    public DbSet<UserDashboard> UserDashboards => Set<UserDashboard>();
    public DbSet<UserDashWidget> UserDashWidgets => Set<UserDashWidget>();

    // ==================== گزارش‌ساز شخصی ====================
    public DbSet<Referrer> Referrers => Set<Referrer>();
    public DbSet<ReferrerPayment> ReferrerPayments => Set<ReferrerPayment>();
    public DbSet<ProductCategory> ProductCategories => Set<ProductCategory>();
    public DbSet<MeasureUnit> MeasureUnits => Set<MeasureUnit>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<Party> Parties => Set<Party>();
    public DbSet<Stock> Stocks => Set<Stock>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<TransactionLine> TransactionLines => Set<TransactionLine>();
    public DbSet<GoodsIssue> GoodsIssues => Set<GoodsIssue>();
    public DbSet<GoodsIssueLine> GoodsIssueLines => Set<GoodsIssueLine>();

    // ---------- ماژول ویزیتوری (پیش‌سند فروش) ----------
    public DbSet<VisOrder> VisOrders => Set<VisOrder>();
    public DbSet<VisOrderLine> VisOrderLines => Set<VisOrderLine>();
    public DbSet<VisOrderHistory> VisOrderHistories => Set<VisOrderHistory>();
    public DbSet<VisCustomerLink> VisCustomerLinks => Set<VisCustomerLink>();
    public DbSet<ExpenseCategory> ExpenseCategories => Set<ExpenseCategory>();
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<Cheque> Cheques => Set<Cheque>();
    public DbSet<InstallmentLine> Installments => Set<InstallmentLine>();
    public DbSet<Technician> Technicians => Set<Technician>();
    public DbSet<RepairOrder> RepairOrders => Set<RepairOrder>();
    public DbSet<RepairDevice> RepairDevices => Set<RepairDevice>();
    public DbSet<RepairItem> RepairItems => Set<RepairItem>();
    public DbSet<SystemInfo> SystemInfos => Set<SystemInfo>();

    // ================== درخواست خدمت آی‌تی ==================
    public DbSet<ItRequest> ItRequests => Set<ItRequest>();
    public DbSet<ItRequestAssignment> ItRequestAssignments => Set<ItRequestAssignment>();
    public DbSet<ItRequestAttachment> ItRequestAttachments => Set<ItRequestAttachment>();
    public DbSet<ItRequestLog> ItRequestLogs => Set<ItRequestLog>();
    public DbSet<ItRequestSeen> ItRequestSeens => Set<ItRequestSeen>();

    // ---------- کانال شرکت‌های راه‌دور (نسخه‌ی نصب‌شده در شرکت مشتری ↔ سرور مرکزی) ----------
    public DbSet<ItClientCompany> ItClientCompanies => Set<ItClientCompany>();
    public DbSet<ItRemoteConnection> ItRemoteConnections => Set<ItRemoteConnection>();
    public DbSet<ItRemoteRequest> ItRemoteRequests => Set<ItRemoteRequest>();
    public DbSet<ItRemoteUserKey> ItRemoteUserKeys => Set<ItRemoteUserKey>();

    // ================== اعلان‌ها (نوتیفیکیشن) ==================
    public DbSet<AppNotification> AppNotifications => Set<AppNotification>();
    public DbSet<PushDelivery> PushDeliveries => Set<PushDelivery>();
    public DbSet<PushSubscription> PushSubscriptions => Set<PushSubscription>();

    // ================== هوش مصنوعی فروغ آریا ==================
    public DbSet<AiConversation> AiConversations => Set<AiConversation>();
    public DbSet<AiMessage> AiMessages => Set<AiMessage>();
    public DbSet<AiKnowledgeDoc> AiKnowledgeDocs => Set<AiKnowledgeDoc>();
    public DbSet<AiPendingAction> AiPendingActions => Set<AiPendingAction>();
    public DbSet<AiReminder> AiReminders => Set<AiReminder>();
    public DbSet<AiReportSchedule> AiReportSchedules => Set<AiReportSchedule>();
    public DbSet<AiDocEmbedding> AiDocEmbeddings => Set<AiDocEmbedding>();
    public DbSet<AiAlertRule> AiAlertRules => Set<AiAlertRule>();
    public DbSet<AiFeedback> AiFeedbacks => Set<AiFeedback>();
    public DbSet<AiAuditLog> AiAuditLogs => Set<AiAuditLog>();

    // ================== دستور کار ==================
    public DbSet<WorkOrder> WorkOrders => Set<WorkOrder>();
    public DbSet<WorkOrderAssignee> WorkOrderAssignees => Set<WorkOrderAssignee>();
    public DbSet<WorkOrderLog> WorkOrderLogs => Set<WorkOrderLog>();
    public DbSet<WorkOrderAttachment> WorkOrderAttachments => Set<WorkOrderAttachment>();
    public DbSet<WorkOrderAllowedAssignee> WorkOrderAllowedAssignees => Set<WorkOrderAllowedAssignee>();
    public DbSet<WorkOrderReminderLog> WorkOrderReminderLogs => Set<WorkOrderReminderLog>();
    public DbSet<WorkOrderChecklistItem> WorkOrderChecklistItems => Set<WorkOrderChecklistItem>();
    public DbSet<WorkOrderComment> WorkOrderComments => Set<WorkOrderComment>();
    public DbSet<WorkOrderTemplate> WorkOrderTemplates => Set<WorkOrderTemplate>();

    // ================== پروژه کاری (داخل دستور کار) ==================
    public DbSet<WorkProject> WorkProjects => Set<WorkProject>();
    public DbSet<WorkProjectMember> WorkProjectMembers => Set<WorkProjectMember>();
    public DbSet<WorkProjectMindFlowMember> WorkProjectMindFlowMembers => Set<WorkProjectMindFlowMember>();
    public DbSet<StandaloneMindFlow> StandaloneMindFlows => Set<StandaloneMindFlow>();
    public DbSet<StandaloneMindFlowMember> StandaloneMindFlowMembers => Set<StandaloneMindFlowMember>();
    public DbSet<WorkProjectLog> WorkProjectLogs => Set<WorkProjectLog>();

    // ================== بایگانی و پیوست جامع (عمومی) ==================
    public DbSet<ArchiveFolder> ArchiveFolders => Set<ArchiveFolder>();
    public DbSet<ArchiveItem> ArchiveItems => Set<ArchiveItem>();
    public DbSet<AppAttachment> AppAttachments => Set<AppAttachment>();
    /// <summary>لاگ مشاهده/دانلود پیوست‌ها (گردانه فایل‌ها)</summary>
    public DbSet<AppAttachmentAccessLog> AppAttachmentAccessLogs => Set<AppAttachmentAccessLog>();
    public DbSet<SystemInfoChangeLog> SystemInfoChangeLogs => Set<SystemInfoChangeLog>();
    public DbSet<LeaveRequest> LeaveRequests => Set<LeaveRequest>();
    public DbSet<CompanyHoliday> CompanyHolidays => Set<CompanyHoliday>();
    public DbSet<WorkCalendarDay> WorkCalendarDays => Set<WorkCalendarDay>();
    public DbSet<WorkCalendarSettings> WorkCalendarSettings => Set<WorkCalendarSettings>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<CctvCamera> CctvCameras => Set<CctvCamera>();
    public DbSet<CctvNvr> CctvNvrs => Set<CctvNvr>();
    // گاوصندوق رمزهای تجهیزات (مدیریت سخت‌افزار) — جدول‌ها با ItVaultSchemaV1.EnsureAsync ساخته می‌شوند
    public DbSet<ItCredential> ItCredentials => Set<ItCredential>();
    public DbSet<ItCredentialLog> ItCredentialLogs => Set<ItCredentialLog>();
    // مدیریت سرورهای HPE/iLO و تاریخچهٔ سلامت — رمز اتصال فقط در ItCredentials نگه‌داری می‌شود
    public DbSet<ManagedServer> ManagedServers => Set<ManagedServer>();
    public DbSet<ServerHealthSnapshot> ServerHealthSnapshots => Set<ServerHealthSnapshot>();
    public DbSet<ServerHardwareComponentSnapshot> ServerHardwareComponentSnapshots => Set<ServerHardwareComponentSnapshot>();
    public DbSet<ServerHardwareEvent> ServerHardwareEvents => Set<ServerHardwareEvent>();
    public DbSet<ServerOperationLog> ServerOperationLogs => Set<ServerOperationLog>();
    public DbSet<ServerAlert> ServerAlerts => Set<ServerAlert>();
    // مدیریت آی‌پی (IPAM) — جدول‌ها با IpamSchemaV1.EnsureAsync ساخته می‌شوند
    public DbSet<IpSubnet> IpSubnets => Set<IpSubnet>();
    public DbSet<IpAddressEntry> IpAddressEntries => Set<IpAddressEntry>();
    public DbSet<OfficeMachine> OfficeMachines => Set<OfficeMachine>();
    public DbSet<OfficeMachineRepair> OfficeMachineRepairs => Set<OfficeMachineRepair>();

    // ================== حضور و غیاب ==================
    public DbSet<ShiftGroup> ShiftGroups => Set<ShiftGroup>();
    public DbSet<AttendanceRecord> AttendanceRecords => Set<AttendanceRecord>();
    public DbSet<AttendanceSegment> AttendanceSegments => Set<AttendanceSegment>();
    public DbSet<UserDevice> UserDevices => Set<UserDevice>();
    public DbSet<AttendanceAlert> AttendanceAlerts => Set<AttendanceAlert>();
    public DbSet<AttendanceAreaSetting> AttendanceAreaSettings => Set<AttendanceAreaSetting>();
    public DbSet<OfficeMachineCost> OfficeMachineCosts => Set<OfficeMachineCost>();
    public DbSet<OfficeMachineSupply> OfficeMachineSupplies => Set<OfficeMachineSupply>();
    public DbSet<OfficeMachineTray> OfficeMachineTrays => Set<OfficeMachineTray>();
    public DbSet<OfficeMachineCounter> OfficeMachineCounters => Set<OfficeMachineCounter>();
    public DbSet<OfficeMachineEvent> OfficeMachineEvents => Set<OfficeMachineEvent>();
    public DbSet<OfficeAgentPrinter> OfficeAgentPrinters => Set<OfficeAgentPrinter>();
    // ---- جدول‌های قطعات کامپیوتر ----
    public DbSet<SystemCpu> SystemCpus => Set<SystemCpu>();
    public DbSet<SystemBoard> SystemBoards => Set<SystemBoard>();
    public DbSet<SystemRam> SystemRams => Set<SystemRam>();
    public DbSet<SystemDisk> SystemDisks => Set<SystemDisk>();
    public DbSet<SystemGpu> SystemGpus => Set<SystemGpu>();
    public DbSet<SystemMonitor> SystemMonitors => Set<SystemMonitor>();
    public DbSet<SystemNetAdapter> SystemNetAdapters => Set<SystemNetAdapter>();
    public DbSet<SystemVolume> SystemVolumes => Set<SystemVolume>();
    public DbSet<SystemInfoUserHistory> SystemInfoUserHistories => Set<SystemInfoUserHistory>();
    public DbSet<SystemHandover> SystemHandovers => Set<SystemHandover>();
    public DbSet<SystemRemoteCommand> SystemRemoteCommands => Set<SystemRemoteCommand>();
    public DbSet<SystemCompany> SystemCompanies => Set<SystemCompany>();
    public DbSet<UserCompanyAccess> UserCompanyAccesses => Set<UserCompanyAccess>();
    public DbSet<SystemDepartment> SystemDepartments => Set<SystemDepartment>();
    public DbSet<SystemUser> SystemUsers => Set<SystemUser>();

    // ==================== سازمان‌ها و سمت‌ها (مبنای جزء «واحد» در شماره نامه) ====================
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<Semat> Semats => Set<Semat>();

    // ==================== مدیریت پروژه‌ها (ورود/خروج، گزارش کار، پیوست) ====================
    public DbSet<KarFarma> KarFarmas => Set<KarFarma>();
    public DbSet<TypeFactor> TypeFactors => Set<TypeFactor>();
    public DbSet<ProjectEntryExit> ProjectEntryExits => Set<ProjectEntryExit>();
    public DbSet<ReportWork> ReportWorks => Set<ReportWork>();
    public DbSet<ProjectAttach> ProjectAttaches => Set<ProjectAttach>();

    /// <summary>درخواست‌های ویرایش/حذف پروژه که در انتظار تایید مدیر هستند</summary>
    public DbSet<ProjectChangeRequest> ProjectChangeRequests => Set<ProjectChangeRequest>();

    // ==================== اتوماسیون اداری — نامه داخلی ====================
    public DbSet<LetterSource> LetterSources => Set<LetterSource>();
    public DbSet<InnerLetter> InnerLetters => Set<InnerLetter>();
    public DbSet<Erja> Erjas => Set<Erja>();
    public DbSet<Amalgar> Amalgars => Set<Amalgar>();
    public DbSet<PishnevisLetter> PishnevisLetters => Set<PishnevisLetter>();
    public DbSet<RelatedLetter> RelatedLetters => Set<RelatedLetter>();
    public DbSet<LetterBayegani> LetterBayeganis => Set<LetterBayegani>();
    public DbSet<LetterGroup> LetterGroups => Set<LetterGroup>();
    public DbSet<LetterGroupMember> LetterGroupMembers => Set<LetterGroupMember>();
    public DbSet<LetterStrature> LetterStratures => Set<LetterStrature>();

    // ==================== اتوماسیون اداری — نامه صادره (فاز دوم + امضا کنندگان) ====================
    public DbSet<OutgoingLetter> OutgoingLetters => Set<OutgoingLetter>();
    public DbSet<OutgoingPishnevisLetter> OutgoingPishnevisLetters => Set<OutgoingPishnevisLetter>();
    public DbSet<OutgoingLetterSigner> OutgoingLetterSigners => Set<OutgoingLetterSigner>();

    // ==================== فرم‌های متفرقه — صورتجلسه ====================
    public DbSet<MeetingMinutes> MeetingMinutes => Set<MeetingMinutes>();
    public DbSet<MeetingMinutesParticipant> MeetingMinutesParticipants => Set<MeetingMinutesParticipant>();
    public DbSet<MeetingMinutesItem> MeetingMinutesItems => Set<MeetingMinutesItem>();

    // ==================== میز کار توسعه (DevTeam) ====================
    public DbSet<DtWorkflowStatus> DtWorkflowStatuses => Set<DtWorkflowStatus>();
    public DbSet<DtProductModule> DtProductModules => Set<DtProductModule>();
    public DbSet<DtSprint> DtSprints => Set<DtSprint>();
    public DbSet<DtTask> DtTasks => Set<DtTask>();
    public DbSet<DtTaskComment> DtTaskComments => Set<DtTaskComment>();
    public DbSet<DtTaskActivity> DtTaskActivities => Set<DtTaskActivity>();
    public DbSet<DtTaskGitLink> DtTaskGitLinks => Set<DtTaskGitLink>();
    public DbSet<DtTimeEntry> DtTimeEntries => Set<DtTimeEntry>();
    public DbSet<DtProblem> DtProblems => Set<DtProblem>();
    public DbSet<DtModuleChange> DtModuleChanges => Set<DtModuleChange>();
    public DbSet<DtTaskDependency> DtTaskDependencies => Set<DtTaskDependency>();
    public DbSet<DtTaskChecklistItem> DtTaskChecklistItems => Set<DtTaskChecklistItem>();

    /// <summary>رونوشت‌گیرندگان نامه صادره (هر گیرنده یک ردیف)</summary>
    public DbSet<OutgoingLetterCopyTo> OutgoingLetterCopyToes => Set<OutgoingLetterCopyTo>();

    // ==================== اتوماسیون اداری — نامه وارده (Oto_TBL_VaredeLetter و TBL_RezervationNumberLetter) ====================
    public DbSet<IncomingLetter> IncomingLetters => Set<IncomingLetter>();
    public DbSet<LetterNumberReservation> LetterNumberReservations => Set<LetterNumberReservation>();

    // ==================== ایمیل سازمانی (پست الکترونیک) — جداول Oto_* دیتابیس Otomasion ====================
    public DbSet<OtoEmail> OtoEmails => Set<OtoEmail>();
    public DbSet<OtoSentEmail> OtoSentEmails => Set<OtoSentEmail>();
    public DbSet<OtoInboxEmail> OtoInboxEmails => Set<OtoInboxEmail>();
    public DbSet<OtoEmailFolder> OtoEmailFolders => Set<OtoEmailFolder>();
    public DbSet<OtoEmailAttachment> OtoEmailAttachments => Set<OtoEmailAttachment>();

    // ==================== آرشیو اسناد و مدارک (DocArchive) ====================
    public DbSet<DocFolder> DocFolders => Set<DocFolder>();
    public DbSet<DocFolderPermission> DocFolderPermissions => Set<DocFolderPermission>();
    public DbSet<ArchiveDocument> Documents => Set<ArchiveDocument>();
    public DbSet<DocumentPermission> DocumentPermissions => Set<DocumentPermission>();
    public DbSet<DocumentApprover> DocumentApprovers => Set<DocumentApprover>();
    public DbSet<DocumentVersion> DocumentVersions => Set<DocumentVersion>();
    public DbSet<DocumentLink> DocumentLinks => Set<DocumentLink>();
    public DbSet<DocCartableTask> DocCartableTasks => Set<DocCartableTask>();
    public DbSet<DocumentLog> DocumentLogs => Set<DocumentLog>();
    public DbSet<DocExpiryAlert> DocExpiryAlerts => Set<DocExpiryAlert>();
    public DbSet<DocTag> DocTags => Set<DocTag>();
    public DbSet<DocumentTag> DocumentTags => Set<DocumentTag>();
    public DbSet<DocExtractedText> DocExtractedTexts => Set<DocExtractedText>();
    public DbSet<DocEntityLink> DocEntityLinks => Set<DocEntityLink>();
    /// <summary>درخواست دسترسی به مدرک (از کاربر بدون دسترسی به مدیر مدرک)</summary>
    public DbSet<DocAccessRequest> DocAccessRequests => Set<DocAccessRequest>();
    /// <summary>تنظیمات شماره‌گذار خودکار کد مدرک (تک‌ردیف)</summary>
    public DbSet<DocCodeSettings> DocCodeSettings => Set<DocCodeSettings>();

    /// <summary>لاگ چاپ فایل‌های مدرک (DocArchive — جدول DocumentPrintLogs)</summary>
    public DbSet<DocumentPrintLog> DocumentPrintLogs => Set<DocumentPrintLog>();

    /// <summary>نوبت‌های ورود انبوه آرشیو (DocArchive — جدول DocImportBatches)</summary>
    public DbSet<DocImportBatch> DocImportBatches => Set<DocImportBatch>();

    /// <summary>نتیجهٔ هر فایل در یک نوبت ورود (جدول DocImportResults)</summary>
    public DbSet<DocImportResult> DocImportResults => Set<DocImportResult>();
    public DbSet<DocFavorite> DocFavorites => Set<DocFavorite>();
    public DbSet<DocComment> DocComments => Set<DocComment>();
    public DbSet<DocCommentMention> DocCommentMentions => Set<DocCommentMention>();
    public DbSet<DocLeakTrace> DocLeakTraces => Set<DocLeakTrace>();

    /// <summary>ماژول هسته پرسنلی (کارگزینی) — HrCore</summary>
    public DbSet<HrOrgUnit> HrOrgUnits => Set<HrOrgUnit>();
    public DbSet<HrEmployee> HrEmployees => Set<HrEmployee>();
    public DbSet<HrContract> HrContracts => Set<HrContract>();
    public DbSet<HrContractTemplate> HrContractTemplates => Set<HrContractTemplate>();
    public DbSet<HrContractVersion> HrContractVersions => Set<HrContractVersion>();
    public DbSet<HrContractExpiryAlert> HrContractExpiryAlerts => Set<HrContractExpiryAlert>();
    public DbSet<HrDecree> HrDecrees => Set<HrDecree>();
    public DbSet<HrReportTemplate> HrReportTemplates => Set<HrReportTemplate>();
    public DbSet<HrJobPosting> HrJobPostings => Set<HrJobPosting>();
    public DbSet<HrApplicant> HrApplicants => Set<HrApplicant>();
    public DbSet<HrInterview> HrInterviews => Set<HrInterview>();
    public DbSet<HrOnboarding> HrOnboardings => Set<HrOnboarding>();
    public DbSet<HrOnboardingItem> HrOnboardingItems => Set<HrOnboardingItem>();
    public DbSet<HrExitCase> HrExitCases => Set<HrExitCase>();
    public DbSet<HrExitItem> HrExitItems => Set<HrExitItem>();
    public DbSet<HrJobHistory> HrJobHistories => Set<HrJobHistory>();
    public DbSet<HrTrialPeriod> HrTrialPeriods => Set<HrTrialPeriod>();
    public DbSet<HrProfileEditRequest> HrProfileEditRequests => Set<HrProfileEditRequest>();
    public DbSet<HrAppraisal> HrAppraisals => Set<HrAppraisal>();
    public DbSet<HrAppraisalKpi> HrAppraisalKpis => Set<HrAppraisalKpi>();
    public DbSet<HrAppraisalScore> HrAppraisalScores => Set<HrAppraisalScore>();

    /// <summary>منابع انسانی اصلی — مدیریت پایه سازمانی (HrMain) — اسکیما با HrMainSchemaV1</summary>
    public DbSet<HrMainCompany> HrMainCompanies => Set<HrMainCompany>();
    public DbSet<HrMainBranch> HrMainBranches => Set<HrMainBranch>();
    public DbSet<HrMainOrgNode> HrMainOrgNodes => Set<HrMainOrgNode>();
    public DbSet<HrMainPosition> HrMainPositions => Set<HrMainPosition>();
    public DbSet<HrMainLocale> HrMainLocales => Set<HrMainLocale>();
    public DbSet<HrMainRules> HrMainRules => Set<HrMainRules>();
    public DbSet<HrMainChangeLog> HrMainChangeLogs => Set<HrMainChangeLog>();

    /// <summary>پرونده کارمندان — اسکیما با HrEmployeeDossierSchemaV1</summary>
    public DbSet<HrEmployeeDependent> HrEmployeeDependents => Set<HrEmployeeDependent>();
    public DbSet<HrEmployeeCourse> HrEmployeeCourses => Set<HrEmployeeCourse>();
    public DbSet<HrEmployeeSkill> HrEmployeeSkills => Set<HrEmployeeSkill>();
    public DbSet<HrEmployeeLanguage> HrEmployeeLanguages => Set<HrEmployeeLanguage>();
    public DbSet<HrEmployeeDocument> HrEmployeeDocuments => Set<HrEmployeeDocument>();

    /// <summary>حضور و غیاب فروغ آریا — اسکیما با FaAttSchemaV1</summary>
    public DbSet<FaAttShift> FaAttShifts => Set<FaAttShift>();
    public DbSet<FaAttShiftAssign> FaAttShiftAssigns => Set<FaAttShiftAssign>();
    public DbSet<FaAttDevice> FaAttDevices => Set<FaAttDevice>();
    public DbSet<FaAttLog> FaAttLogs => Set<FaAttLog>();
    public DbSet<FaAttDaily> FaAttDailies => Set<FaAttDaily>();
    public DbSet<FaAttMission> FaAttMissions => Set<FaAttMission>();
    public DbSet<FaAttLeaveType> FaAttLeaveTypes => Set<FaAttLeaveType>();
    public DbSet<FaAttLeave> FaAttLeaves => Set<FaAttLeave>();
    public DbSet<FaAttLeaveBalance> FaAttLeaveBalances => Set<FaAttLeaveBalance>();
    public DbSet<FaPaySettings> FaPaySettings => Set<FaPaySettings>();
    public DbSet<FaPayTaxBracket> FaPayTaxBrackets => Set<FaPayTaxBracket>();
    public DbSet<FaPayItemType> FaPayItemTypes => Set<FaPayItemType>();
    public DbSet<FaPayRun> FaPayRuns => Set<FaPayRun>();
    public DbSet<FaPaySlip> FaPaySlips => Set<FaPaySlip>();
    public DbSet<FaPaySlipItem> FaPaySlipItems => Set<FaPaySlipItem>();
    public DbSet<FaPayAdjustment> FaPayAdjustments => Set<FaPayAdjustment>();
    public DbSet<FaPayExtraSettings> FaPayExtraSettings => Set<FaPayExtraSettings>();
    public DbSet<FaPayLoan> FaPayLoans => Set<FaPayLoan>();
    public DbSet<FaPayLoanInstallment> FaPayLoanInstallments => Set<FaPayLoanInstallment>();
    public DbSet<FaPayArrear> FaPayArrears => Set<FaPayArrear>();
    public DbSet<FaPaySettlement> FaPaySettlements => Set<FaPaySettlement>();
    public DbSet<FaLmsCourse> FaLmsCourses => Set<FaLmsCourse>();
    public DbSet<FaLmsNeed> FaLmsNeeds => Set<FaLmsNeed>();
    public DbSet<FaLmsEnrollment> FaLmsEnrollments => Set<FaLmsEnrollment>();
    public DbSet<FaLmsSession> FaLmsSessions => Set<FaLmsSession>();
    public DbSet<FaLmsAttendance> FaLmsAttendances => Set<FaLmsAttendance>();
    public DbSet<FaLmsExam> FaLmsExams => Set<FaLmsExam>();
    public DbSet<FaLmsQuestion> FaLmsQuestions => Set<FaLmsQuestion>();
    public DbSet<FaLmsAttempt> FaLmsAttempts => Set<FaLmsAttempt>();
    public DbSet<FaLmsCertificate> FaLmsCertificates => Set<FaLmsCertificate>();
    public DbSet<FaLmsBudget> FaLmsBudgets => Set<FaLmsBudget>();
    public DbSet<FaLmsBank> FaLmsBanks => Set<FaLmsBank>();
    public DbSet<FaLmsBankQuestion> FaLmsBankQuestions => Set<FaLmsBankQuestion>();
    public DbSet<FaLmsTextAnswer> FaLmsTextAnswers => Set<FaLmsTextAnswer>();
    public DbSet<FaLmsInstructor> FaLmsInstructors => Set<FaLmsInstructor>();
    public DbSet<FaLmsSurveyQuestion> FaLmsSurveyQuestions => Set<FaLmsSurveyQuestion>();
    public DbSet<FaLmsSurveyAnswer> FaLmsSurveyAnswers => Set<FaLmsSurveyAnswer>();
    public DbSet<FaComAnnouncement> FaComAnnouncements => Set<FaComAnnouncement>();
    public DbSet<FaComTicket> FaComTickets => Set<FaComTicket>();
    public DbSet<FaComReply> FaComReplies => Set<FaComReply>();
    public DbSet<FaComPoll> FaComPolls => Set<FaComPoll>();
    public DbSet<FaComPollOption> FaComPollOptions => Set<FaComPollOption>();
    public DbSet<FaComVote> FaComVotes => Set<FaComVote>();
    public DbSet<FaComSuggestion> FaComSuggestions => Set<FaComSuggestion>();


    /// <summary>ماژول زمان‌بندی و حقوق — جدول‌های اختصاصی (بدون هیچ تغییری در جداول قبلی)</summary>
    public DbSet<HrRequestStep> HrRequestSteps => Set<HrRequestStep>();
    public DbSet<HrLeaveBalance> HrLeaveBalances => Set<HrLeaveBalance>();
    public DbSet<HrOvertimeRequest> HrOvertimeRequests => Set<HrOvertimeRequest>();
    public DbSet<HrShiftRoster> HrShiftRosters => Set<HrShiftRoster>();
    public DbSet<HrDevicePunch> HrDevicePunches => Set<HrDevicePunch>();
    public DbSet<HrDeviceUserMap> HrDeviceUserMaps => Set<HrDeviceUserMap>();
    public DbSet<HrTimeRule> HrTimeRules => Set<HrTimeRule>();
    public DbSet<HrLeaveExtra> HrLeaveExtras => Set<HrLeaveExtra>();
    public DbSet<HrDayExtra> HrDayExtras => Set<HrDayExtra>();
    public DbSet<HrUserLink> HrUserLinks => Set<HrUserLink>();
    public DbSet<HrProbation> HrProbations => Set<HrProbation>();
    public DbSet<HrMinWage> HrMinWages => Set<HrMinWage>();
    public DbSet<HrNursingBreak> HrNursingBreaks => Set<HrNursingBreak>();
    public DbSet<HrOtApproval> HrOtApprovals => Set<HrOtApproval>();
    public DbSet<HrAttendClose> HrAttendCloses => Set<HrAttendClose>();
    public DbSet<HrPayItem> HrPayItems => Set<HrPayItem>();
    public DbSet<HrPayEmployeeItem> HrPayEmployeeItems => Set<HrPayEmployeeItem>();
    public DbSet<HrPayProfile> HrPayProfiles => Set<HrPayProfile>();
    public DbSet<HrPayTaxBracket> HrPayTaxBrackets => Set<HrPayTaxBracket>();
    public DbSet<HrPayLoan> HrPayLoans => Set<HrPayLoan>();
    public DbSet<HrPayArrear> HrPayArrears => Set<HrPayArrear>();
    public DbSet<HrPayOnAccount> HrPayOnAccounts => Set<HrPayOnAccount>();
    public DbSet<HrPayRun> HrPayRuns => Set<HrPayRun>();
    public DbSet<HrPaySlip> HrPaySlips => Set<HrPaySlip>();
    public DbSet<HrPaySettlement> HrPaySettlements => Set<HrPaySettlement>();
    public DbSet<HrPayFiling> HrPayFilings => Set<HrPayFiling>();
    public DbSet<HrPerfPeriod> HrPerfPeriods => Set<HrPerfPeriod>();
    public DbSet<HrPerfKpi> HrPerfKpis => Set<HrPerfKpi>();
    public DbSet<HrPerfScore> HrPerfScores => Set<HrPerfScore>();
    public DbSet<HrPerfResult> HrPerfResults => Set<HrPerfResult>();
    /// <summary>پرسنل اختصاصی «منابع انسانی فروغ آریا» — جدا از HrEmployees (منابع انسانی اصلی)</summary>
    public DbSet<FaHrPersonnel> FaHrPersonnel => Set<FaHrPersonnel>();
    public DbSet<FaHrSchemaMark> FaHrSchemaMarks => Set<FaHrSchemaMark>();
    /// <summary>تعطیلات اختصاصی «منابع انسانی اصلی» — جدا از CompanyHolidays (فروغ آریا)</summary>
    public DbSet<HrMainHoliday> HrMainHolidays => Set<HrMainHoliday>();
    /// <summary>حداقل دستمزد اختصاصی حقوق منابع انسانی اصلی — جدا از HrMinWages (فروغ آریا)</summary>
    public DbSet<FaPayMinWage> FaPayMinWages => Set<FaPayMinWage>();

    // ==================== ماژول پیام‌رسان سازمانی (Chat) ====================
    public DbSet<Inventory.Api.Entities.Chat.ChatConversation> ChatConversations => Set<Inventory.Api.Entities.Chat.ChatConversation>();
    public DbSet<Inventory.Api.Entities.Chat.ChatMember> ChatMembers => Set<Inventory.Api.Entities.Chat.ChatMember>();
    public DbSet<Inventory.Api.Entities.Chat.ChatMessage> ChatMessages => Set<Inventory.Api.Entities.Chat.ChatMessage>();
    public DbSet<Inventory.Api.Entities.Chat.ChatAttachment> ChatAttachments => Set<Inventory.Api.Entities.Chat.ChatAttachment>();

    // ==================== ماژول انبارداری ====================
    public DbSet<ProductAttributeDef> ProductAttributeDefs => Set<ProductAttributeDef>();
    public DbSet<ProductAttributeOption> ProductAttributeOptions => Set<ProductAttributeOption>();
    public DbSet<ProductAttributeValue> ProductAttributeValues => Set<ProductAttributeValue>();
    public DbSet<InvDocType> InvDocTypes => Set<InvDocType>();
    public DbSet<InvDoc> InvDocs => Set<InvDoc>();
    public DbSet<InvDocLine> InvDocLines => Set<InvDocLine>();
    public DbSet<InvLedgerEntry> InvLedger => Set<InvLedgerEntry>();
    public DbSet<InvStock> InvStocks => Set<InvStock>();

    // ==================== ماژول حسابداری ====================
    public DbSet<AccFiscalYear> AccFiscalYears => Set<AccFiscalYear>();
    public DbSet<AccAccount> AccAccounts => Set<AccAccount>();
    public DbSet<AccVoucher> AccVouchers => Set<AccVoucher>();
    public DbSet<AccVoucherLine> AccVoucherLines => Set<AccVoucherLine>();
    public DbSet<AccInvRule> AccInvRules => Set<AccInvRule>();
    // ==================== ابعاد تحلیلی (مراکز هزینه / شعبه) ====================
    public DbSet<AccDimension> AccDimensions => Set<AccDimension>();
    public DbSet<AccDimensionValue> AccDimensionValues => Set<AccDimensionValue>();
    // ==================== دارایی ثابت و استهلاک ====================
    public DbSet<FixedAssetCategory> FixedAssetCategories => Set<FixedAssetCategory>();
    public DbSet<FixedAsset> FixedAssets => Set<FixedAsset>();
    public DbSet<FixedAssetDepreciationRun> FixedAssetDepreciationRuns => Set<FixedAssetDepreciationRun>();
    public DbSet<FixedAssetDepreciationLine> FixedAssetDepreciationLines => Set<FixedAssetDepreciationLine>();
    // ==================== بودجه و کنترل بودجه ====================
    public DbSet<Budget> Budgets => Set<Budget>();
    public DbSet<BudgetItem> BudgetItems => Set<BudgetItem>();
    public DbSet<BudgetTransaction> BudgetTransactions => Set<BudgetTransaction>();

    // ---------- ماژول فاکتور ----------
    public DbSet<FacInvoice> FacInvoices => Set<FacInvoice>();
    public DbSet<FacInvoiceLine> FacInvoiceLines => Set<FacInvoiceLine>();
    public DbSet<FacRule> FacRules => Set<FacRule>();

    // ---------- سامانه مودیان مبتنی بر گواهی امضا (SDK 2.0.29) ----------
    public DbSet<MdnTaxpayerProfile> MdnTaxpayerProfiles => Set<MdnTaxpayerProfile>();
    public DbSet<MdnMeasureUnit> MdnMeasureUnits => Set<MdnMeasureUnit>();
    public DbSet<MdnBuyer> MdnBuyers => Set<MdnBuyer>();
    public DbSet<MdnCatalogItem> MdnCatalogItems => Set<MdnCatalogItem>();
    public DbSet<MdnInvoice> MdnInvoices => Set<MdnInvoice>();
    public DbSet<MdnInvoiceLine> MdnInvoiceLines => Set<MdnInvoiceLine>();
    public DbSet<MdnInvoicePayment> MdnInvoicePayments => Set<MdnInvoicePayment>();
    public DbSet<MdnSubmissionLog> MdnSubmissionLogs => Set<MdnSubmissionLog>();

    // ---------- ماژول خزانه‌داری ----------
    public DbSet<TrsAccount> TrsAccounts => Set<TrsAccount>();
    public DbSet<TrsVoucher> TrsVouchers => Set<TrsVoucher>();
    public DbSet<TrsVoucherLine> TrsVoucherLines => Set<TrsVoucherLine>();
    public DbSet<TrsInvoiceAllocation> TrsInvoiceAllocations => Set<TrsInvoiceAllocation>();
    public DbSet<TrsCheque> TrsCheques => Set<TrsCheque>();
    public DbSet<TrsChequeAction> TrsChequeActions => Set<TrsChequeAction>();
    public DbSet<TrsRule> TrsRules => Set<TrsRule>();

    // ---------- ماژول انبارگردانی و بارکد ----------
    public DbSet<BcdBarcode> BcdBarcodes => Set<BcdBarcode>();
    public DbSet<StkSession> StkSessions => Set<StkSession>();
    public DbSet<StkLine> StkLines => Set<StkLine>();
    public DbSet<StkScan> StkScans => Set<StkScan>();

    // ==================== RBAC ====================
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);
        configurationBuilder.Properties<decimal>().HavePrecision(18, 4);
    }

    protected override void OnModelCreating(ModelBuilder mb)
    {
        base.OnModelCreating(mb);
        DocEvolutionModel.Configure(mb);
        PushDeliverySchema.Configure(mb);
        AiSchemaV1.Configure(mb);
        ItVaultSchemaV1.Configure(mb);
        ServerManagementSchemaV1.Configure(mb);
        ServerMonitoringSchemaV2.Configure(mb);
        ServerAlertWorkflowSchemaV3.Configure(mb);
        ServerHardwareDetailsSchemaV4.Configure(mb);
        IpamSchemaV1.Configure(mb);
        OfficeMachineSchemaV2.Configure(mb);
        OfficeMachineSchemaV3.Configure(mb);

        mb.Entity<User>().HasIndex(u => u.Username).IsUnique();
        mb.Entity<UserContactOtpChallenge>().HasIndex(c => new { c.UserId, c.Purpose }).IsUnique();
        mb.Entity<UserContactOtpChallenge>()
            .HasOne(c => c.User)
            .WithMany()
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        mb.Entity<UserCompanyAccess>().HasKey(x => new { x.UserId, x.CompanyId });
        mb.Entity<UserCompanyAccess>().HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        mb.Entity<UserCompanyAccess>().HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);

        // ==================== واحدهای سازمانی و سازمان کاربر ====================
        // کاربر → واحد سازمانی (اختیاری — برای جزء «واحد» در شماره نامه)
        mb.Entity<SystemUser>()
            .HasOne(u => u.Organization)
            .WithMany()
            .HasForeignKey(u => u.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        // واحد سازمانی → شرکت (اختیاری — واحدها به صورت جداگانه برای هر شرکت تعریف می‌شوند)
        mb.Entity<Organization>()
            .HasOne(o => o.Company)
            .WithMany()
            .HasForeignKey(o => o.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        // نام گروه/واحد/کد کالا فقط در محدودهٔ همان شرکت یکتاست (چند شرکتی)
        mb.Entity<ProductCategory>().HasIndex(c => new { c.CompanyId, c.Name }).IsUnique();
        mb.Entity<MeasureUnit>().HasIndex(u => new { u.CompanyId, u.Name }).IsUnique();
        mb.Entity<Stock>().HasIndex(s => new { s.WarehouseId, s.ProductId }).IsUnique();
        mb.Entity<Stock>().HasIndex(s => new { s.CompanyId, s.WarehouseId, s.ProductId });
        mb.Entity<Product>().HasIndex(p => new { p.CompanyId, p.Code }).IsUnique();
        mb.Entity<Transaction>().HasIndex(t => t.Type);
        mb.Entity<Transaction>().HasIndex(t => t.Date);
        mb.Entity<TransactionLine>().HasIndex(l => l.ProductId);

        // ==================== حواله تحویل کالا (بدون قیمت) ====================
        mb.Entity<GoodsIssue>().HasIndex(g => new { g.CompanyId, g.Number }).IsUnique();
        mb.Entity<GoodsIssue>().HasIndex(g => g.PartyId);
        mb.Entity<GoodsIssue>().HasIndex(g => g.Date);
        mb.Entity<GoodsIssueLine>().HasIndex(l => l.IssueId);
        mb.Entity<GoodsIssueLine>().HasIndex(l => l.ProductId);
        // حذف حواله = حذف سطرهایش (بدون FK به Parties/Products تا حذف طرف حساب یا کالا ممکن بماند)
        mb.Entity<GoodsIssue>().HasMany(g => g.Lines).WithOne()
            .HasForeignKey(l => l.IssueId).OnDelete(DeleteBehavior.Cascade);

        // ==================== سفارش ویزیتور (پیش‌سند فروش) ====================
        mb.Entity<VisOrder>().HasIndex(o => new { o.CompanyId, o.Number }).IsUnique();
        mb.Entity<VisOrder>().HasIndex(o => o.Status);
        mb.Entity<VisOrder>().HasIndex(o => new { o.ReferrerId, o.Date });
        mb.Entity<VisOrder>().HasIndex(o => o.PartyId);
        mb.Entity<VisOrder>().HasIndex(o => o.InvoiceId);
        mb.Entity<VisOrderLine>().HasIndex(l => l.OrderId);
        mb.Entity<VisOrderLine>().HasIndex(l => l.ProductId);
        mb.Entity<VisOrderHistory>().HasIndex(h => h.OrderId);
        mb.Entity<VisCustomerLink>().HasIndex(c => new { c.ReferrerId, c.PartyId }).IsUnique();
        mb.Entity<VisCustomerLink>().HasIndex(c => c.PartyId);
        // حذف سفارش = حذف سطرها و گردش آن (بدون FK به Parties/Products/Users)
        mb.Entity<VisOrder>().HasMany(o => o.Lines).WithOne()
            .HasForeignKey(l => l.OrderId).OnDelete(DeleteBehavior.Cascade);
        mb.Entity<VisOrder>().HasMany(o => o.History).WithOne()
            .HasForeignKey(h => h.OrderId).OnDelete(DeleteBehavior.Cascade);

        // ==================== آرشیو اسناد و مدارک ====================
        // کد مدرک در کل سیستم یکتاست
        mb.Entity<ArchiveDocument>().HasIndex(d => d.Code).IsUnique();
        mb.Entity<ArchiveDocument>().HasIndex(d => d.FolderId);
        mb.Entity<ArchiveDocument>().HasIndex(d => d.ExpireDate);
        mb.Entity<DocFolder>().HasIndex(f => f.ParentId);
        // ردیف فردی = (…,UserId>0,RoleId=0)، ردیف گروهی = (…,UserId=0,RoleId>0) — هر کدام یک‌بار
        mb.Entity<DocFolderPermission>().HasIndex(p => new { p.FolderId, p.UserId, p.RoleId }).IsUnique();
        mb.Entity<DocumentPermission>().HasIndex(p => new { p.DocumentId, p.UserId, p.RoleId }).IsUnique();
        mb.Entity<DocumentVersion>().HasIndex(v => new { v.DocumentId, v.VersionNo }).IsUnique();
        // هر مدرک برای هر آستانه و هر تاریخ انقضا فقط یک‌بار هشدار می‌گیرد
        mb.Entity<DocExpiryAlert>().HasIndex(a => new { a.DocumentId, a.ThresholdDays, a.ExpireDate }).IsUnique();
        mb.Entity<DocumentApprover>().HasIndex(a => new { a.DocumentId, a.VersionId });
        mb.Entity<DocumentLink>().HasIndex(l => new { l.DocumentId, l.LinkedDocumentId }).IsUnique();
        mb.Entity<DocCartableTask>().HasIndex(t => new { t.UserId, t.Status });
        mb.Entity<DocumentLog>().HasIndex(l => l.DocumentId);

        // ==================== آرشیو اسناد — ورود انبوه ====================
        // نام ایندکس‌ها با اسکیمای خودتعمیر (DocArchiveImportSchemaV1) یکی است تا
        // در دیتابیس تازه‌ساختِ SQLite ایندکس تکراری ساخته نشود.
        mb.Entity<DocImportResult>().HasIndex(r => new { r.BatchId, r.RelativePath }).IsUnique()
            .HasDatabaseName("UX_DocImportResults_Batch_Path");
        mb.Entity<DocImportResult>().HasIndex(r => new { r.BatchId, r.Status })
            .HasDatabaseName("IX_DocImportResults_Batch_Status");
        mb.Entity<DocTag>().HasIndex(t => t.Name).IsUnique();
        mb.Entity<DocumentTag>().HasIndex(t => new { t.DocumentId, t.TagId }).IsUnique();
        mb.Entity<DocExtractedText>().HasIndex(e => new { e.DocumentId, e.AttachmentId });
        mb.Entity<DocExtractedText>().HasIndex(e => e.AttachmentId);
        mb.Entity<DocEntityLink>().HasIndex(l => new { l.DocumentId, l.Module, l.EntityId });
        mb.Entity<DocEntityLink>().HasIndex(l => new { l.Module, l.EntityId });

        // پیوست‌ها: هر «باز کردن نامه/مدرک» و هر ردیف فهرست، یک EXISTS روی
        // (Module, RefId) می‌زند. بدون این ایندکس، آن جست‌وجو «اسکن کل جدول» بود و
        // چون جدول ستون varbinary(max) دارد (پیوست‌های قدیمی)، خواندن یک نامه
        // به‌طور محسوسی سنگین می‌شد. رکوردهای تازه‌تر فایل را روی دیسک دارند و
        // همین ستون را در همان ردیف خالی نگه می‌دارند.
        mb.Entity<AppAttachment>().HasIndex(a => new { a.Module, a.RefId });

        // لاگ دسترسی به پیوست‌ها (گردانه دانلود/مشاهده)
        mb.Entity<AppAttachmentAccessLog>().HasIndex(l => new { l.Module, l.RefId });
        mb.Entity<AppAttachmentAccessLog>().HasIndex(l => l.AttachmentId);
        mb.Entity<AppAttachmentAccessLog>().HasIndex(l => l.At);

        // درخواست دسترسی به مدرک
        mb.Entity<DocAccessRequest>().HasIndex(r => new { r.DocumentId, r.Status });
        mb.Entity<DocAccessRequest>().HasIndex(r => new { r.RequesterUserId, r.Status });

        // علاقه‌مندی‌ها
        mb.Entity<DocFavorite>().HasIndex(f => new { f.UserId, f.DocumentId }).IsUnique();
        mb.Entity<DocFavorite>().HasIndex(f => f.UserId);

        // ---------- ماژول پیام‌رسان سازمانی (Chat) ----------
        mb.Entity<Inventory.Api.Entities.Chat.ChatMember>().HasIndex(m => new { m.ConversationId, m.UserId }).IsUnique();
        mb.Entity<Inventory.Api.Entities.Chat.ChatMember>().HasIndex(m => m.UserId);
        mb.Entity<Inventory.Api.Entities.Chat.ChatMessage>().HasIndex(m => new { m.ConversationId, m.CreatedAt });
        mb.Entity<Inventory.Api.Entities.Chat.ChatMessage>().HasIndex(m => m.SenderUserId);
        mb.Entity<Inventory.Api.Entities.Chat.ChatConversation>().HasIndex(c => c.LastMessageAt);
        mb.Entity<Inventory.Api.Entities.Chat.ChatAttachment>().HasIndex(a => a.ConversationId);
        mb.Entity<Inventory.Api.Entities.Chat.ChatAttachment>().HasIndex(a => a.UploadedByUserId);

        // ---------- امنیت حضور و غیاب: ایندکس‌های دستگاه‌ها و هشدارها ----------
        // هر کاربر برای هر Device ID فقط یک رکورد دستگاه دارد (دستگاه یکتا)
        mb.Entity<UserDevice>().HasIndex(d => new { d.UserId, d.DeviceId }).IsUnique();
        mb.Entity<UserDevice>().HasIndex(d => d.DeviceId);
        mb.Entity<AttendanceAlert>().HasIndex(a => new { a.UserId, a.Status });
        mb.Entity<AttendanceAlert>().HasIndex(a => new { a.Status, a.CreatedAt });

        // ---------- دقت صریح اعداد اعشاری برای SQL Server ----------
        // قیمت‌ها و مبالغ: 2 رقم اعشار
        mb.Entity<Product>().Property(p => p.SalePrice).HasPrecision(18, 2);
        mb.Entity<Product>().Property(p => p.PurchasePrice).HasPrecision(18, 2);
        mb.Entity<Stock>().Property(s => s.AvgCost).HasPrecision(18, 4);
        mb.Entity<Transaction>().Property(t => t.Amount).HasPrecision(18, 2);
        mb.Entity<TransactionLine>().Property(l => l.Price).HasPrecision(18, 2);
        mb.Entity<TransactionLine>().Property(l => l.UnitCostSnapshot).HasPrecision(18, 2);
        mb.Entity<TransactionLine>().Property(l => l.DiscountPercent).HasPrecision(18, 2);
        mb.Entity<TransactionLine>().Property(l => l.DiscountAmount).HasPrecision(18, 2);

        // مقادیر (موجودی/مقدار) با 3 رقم اعشار
        mb.Entity<Product>().Property(p => p.ReorderPoint).HasPrecision(18, 3);
        mb.Entity<Product>().Property(p => p.MaxStock).HasPrecision(18, 3);
        mb.Entity<Stock>().Property(s => s.Quantity).HasPrecision(18, 3);
        mb.Entity<TransactionLine>().Property(l => l.Quantity).HasPrecision(18, 3);
        mb.Entity<Referrer>().Property(r => r.GoodsCommissionPercent).HasPrecision(5, 2);
        mb.Entity<Referrer>().Property(r => r.ServiceCommissionPercent).HasPrecision(5, 2);
        mb.Entity<ReferrerPayment>().Property(p => p.Amount).HasPrecision(18, 2);
        mb.Entity<ReferrerPayment>().HasIndex(p => p.ReferrerId);

        // ---------- هزینه‌ها ----------
        mb.Entity<ExpenseCategory>().HasIndex(c => new { c.CompanyId, c.Name }).IsUnique();
        mb.Entity<Expense>().Property(e => e.Amount).HasPrecision(18, 2);
        mb.Entity<Expense>().HasIndex(e => e.Date);
        mb.Entity<Expense>().HasIndex(e => e.CategoryId);

        // ---------- پرداخت فروش: چک و اقساط ----------
        mb.Entity<Transaction>().Property(t => t.SettledAmount).HasPrecision(18, 2);
        mb.Entity<Transaction>().Property(t => t.CashAmount).HasPrecision(18, 2);
        mb.Entity<Cheque>().Property(c => c.Amount).HasPrecision(18, 2);
        mb.Entity<Cheque>().HasIndex(c => c.DueDate);
        mb.Entity<Cheque>().HasIndex(c => c.IsCleared);
        mb.Entity<InstallmentLine>().Property(i => i.Amount).HasPrecision(18, 2);
        mb.Entity<InstallmentLine>().HasIndex(i => i.DueDate);
        mb.Entity<Transaction>()
            .HasMany(t => t.Cheques)
            .WithOne()
            .HasForeignKey(c => c.TransactionId)
            .OnDelete(DeleteBehavior.Cascade);
        mb.Entity<Transaction>()
            .HasMany(t => t.Installments)
            .WithOne()
            .HasForeignKey(i => i.TransactionId)
            .OnDelete(DeleteBehavior.Cascade);

        // ---------- تعمیرات ----------
        mb.Entity<RepairOrder>().HasIndex(r => r.Status);
        mb.Entity<RepairOrder>().HasIndex(r => r.PartyId);
        mb.Entity<RepairOrder>().HasIndex(r => r.TechnicianId);
        mb.Entity<RepairOrder>().Property(r => r.QuotedPrice).HasPrecision(18, 2);
        mb.Entity<RepairOrder>()
            .HasMany(r => r.Devices)
            .WithOne()
            .HasForeignKey(d => d.RepairOrderId)
            .OnDelete(DeleteBehavior.Cascade);
        mb.Entity<RepairDevice>().HasIndex(d => d.RepairOrderId);
        mb.Entity<RepairDevice>().Property(d => d.QuotedPrice).HasPrecision(18, 2);
        mb.Entity<RepairOrder>()
            .HasMany(r => r.Items)
            .WithOne()
            .HasForeignKey(i => i.RepairOrderId)
            .OnDelete(DeleteBehavior.Cascade);
        mb.Entity<RepairItem>().Property(i => i.Quantity).HasPrecision(18, 3);
        mb.Entity<RepairItem>().Property(i => i.Cost).HasPrecision(18, 2);
        mb.Entity<RepairItem>().Property(i => i.Price).HasPrecision(18, 2);

        // ==================== مدیریت پروژه‌ها ====================
        mb.Entity<KarFarma>().HasIndex(k => k.Name);
        mb.Entity<ProjectEntryExit>().HasIndex(p => p.CodeProject);
        mb.Entity<ProjectEntryExit>().HasIndex(p => p.SerialNumber);
        mb.Entity<ProjectEntryExit>().HasIndex(p => p.KarFarmaId);
        mb.Entity<ProjectEntryExit>().HasIndex(p => p.UserId);
        mb.Entity<ReportWork>().HasIndex(r => r.ProjectId);
        mb.Entity<ReportWork>().HasIndex(r => r.UserId);
        mb.Entity<ReportWork>().HasIndex(r => r.OperatorId);
        mb.Entity<ProjectAttach>().HasIndex(a => a.ProjectId);
        mb.Entity<ProjectChangeRequest>().HasIndex(c => c.ProjectId);
        mb.Entity<ProjectChangeRequest>().HasIndex(c => c.Status);

        // جمع ساعات پروژه به‌صورت تیک (bigint) — نوع time فقط تا ۲۴ ساعت را می‌پذیرد
        mb.Entity<ProjectEntryExit>().Property(p => p.TotalSpentTime).HasConversion<long>();

        // رکوردهای ورود/خروج → مراجع (حذف نرم — جلوگیری از حذف فیزیکی مرجع‌های درحال‌استفاده)
        mb.Entity<ProjectEntryExit>()
            .HasOne(p => p.KarFarma)
            .WithMany(k => k.Projects)
            .HasForeignKey(p => p.KarFarmaId)
            .OnDelete(DeleteBehavior.Restrict);
        mb.Entity<ProjectEntryExit>()
            .HasOne(p => p.TypeFactor)
            .WithMany(t => t.Projects)
            .HasForeignKey(p => p.FactorTypeId)
            .OnDelete(DeleteBehavior.Restrict);
        mb.Entity<ProjectEntryExit>()
            .HasOne(p => p.User)
            .WithMany()
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        // گزارش‌های کار و پیوست‌ها زیرمجموعه‌ی پروژه هستند
        mb.Entity<ReportWork>()
            .HasOne(r => r.Project)
            .WithMany(p => p.ReportWorks)
            .HasForeignKey(r => r.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);
        mb.Entity<ReportWork>()
            .HasOne(r => r.User)
            .WithMany()
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        // اپراتور (انجام‌دهندهٔ کار) — جدا از ثبت‌کننده؛ اختیاری
        mb.Entity<ReportWork>()
            .HasOne(r => r.Operator)
            .WithMany()
            .HasForeignKey(r => r.OperatorId)
            .OnDelete(DeleteBehavior.Restrict);
        mb.Entity<ProjectAttach>()
            .HasOne(a => a.Project)
            .WithMany(p => p.Attaches)
            .HasForeignKey(a => a.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);
        mb.Entity<ProjectAttach>()
            .HasOne(a => a.User)
            .WithMany()
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        // درخواست‌های تغییر پروژه (در انتظار تایید مدیر)
        mb.Entity<ProjectChangeRequest>()
            .HasOne(c => c.Project)
            .WithMany()
            .HasForeignKey(c => c.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);
        mb.Entity<ProjectChangeRequest>()
            .HasOne(c => c.RequestedBy)
            .WithMany()
            .HasForeignKey(c => c.RequestedById)
            .OnDelete(DeleteBehavior.Restrict);

        // ---------- ماژول شناسنامه سیستم: تاریخچه کاربر، تحویل، دستور از راه دور ----------
        mb.Entity<SystemInfoUserHistory>().HasIndex(h => h.SystemInfoId);
        mb.Entity<SystemHandover>().HasIndex(h => h.SystemInfoId);
        mb.Entity<SystemRemoteCommand>().HasIndex(c => c.SystemInfoId);
        mb.Entity<SystemRemoteCommand>().HasIndex(c => c.Status);

        // ==================== اتوماسیون اداری — نامه داخلی و صادره ====================
        // کلیدهای اصلی صریح (نام‌گذاری مطابق طرح کارفرما)
        mb.Entity<Erja>().HasKey(e => e.ErjaId);
        mb.Entity<Amalgar>().HasKey(a => a.AmalgarId);

        mb.Entity<Amalgar>().HasKey(a => a.AmalgarId);
        mb.Entity<PishnevisLetter>().HasKey(p => p.PishnevisId);
        mb.Entity<OutgoingPishnevisLetter>().HasKey(p => p.PishnevisId);
        mb.Entity<LetterBayegani>().HasKey(b => b.BayeganiId);

        // InnerLetter با LetterSource کلید مشترک دارد (الگوی SourceKeyID طرح اصلی)
        mb.Entity<InnerLetter>()
            .HasOne(l => l.Source)
            .WithOne(s => s.InnerLetter!)
            .HasForeignKey<InnerLetter>(l => l.Id)
            .OnDelete(DeleteBehavior.Cascade);
        mb.Entity<InnerLetter>()
            .HasOne(l => l.Creator)
            .WithMany()
            .HasForeignKey(l => l.CreatorUserId)
            .OnDelete(DeleteBehavior.Restrict);
        mb.Entity<InnerLetter>().HasIndex(l => l.Number);
        mb.Entity<InnerLetter>().HasIndex(l => l.DateSabt);
        mb.Entity<InnerLetter>().HasIndex(l => l.CreatorUserId);

        // IncomingLetter — الگوی کلید مشترک با SourceType=3 (نامه وارده)
        mb.Entity<IncomingLetter>()
            .HasOne(l => l.Source)
            .WithOne(s => s.IncomingLetter!)
            .HasForeignKey<IncomingLetter>(l => l.Id)
            .OnDelete(DeleteBehavior.Cascade);
        mb.Entity<IncomingLetter>().HasIndex(l => l.Number);
        mb.Entity<IncomingLetter>().HasIndex(l => l.NumberSabt);
        mb.Entity<IncomingLetter>().HasIndex(l => l.DateErsal);
        mb.Entity<IncomingLetter>().HasIndex(l => l.CreateUserId);
        mb.Entity<IncomingLetter>().HasIndex(l => l.Ferestande);

        // رزرو شماره نامه
        mb.Entity<LetterNumberReservation>().HasIndex(r => new { r.TypeForm, r.NumberSabt });
        mb.Entity<LetterNumberReservation>().HasIndex(r => r.UserId);

        // امضا کنندگان نامه صادره
        mb.Entity<OutgoingLetterSigner>()
            .HasOne(s => s.Source)
            .WithMany(src => src.OutgoingSigners)
            .HasForeignKey(s => s.SourceId)
            .OnDelete(DeleteBehavior.Cascade);
        mb.Entity<OutgoingLetterSigner>()
            .HasOne(s => s.User)
            .WithMany()
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        mb.Entity<OutgoingLetterSigner>().HasIndex(s => s.SourceId);

        // رونوشت‌گیرندگان نامه صادره — نام جدول صریحاً تعیین می‌شود
        // تا با اسکیمای خودتعمیر (OutgoingCopyToSchemaV1) دقیقاً یکسان باشد
        mb.Entity<OutgoingLetterCopyTo>().ToTable("OutgoingLetterCopyToes");
        mb.Entity<OutgoingLetterCopyTo>().HasIndex(c => c.OutgoingLetterId);
        mb.Entity<OutgoingLetterSigner>().HasIndex(s => s.UserId);
        mb.Entity<OutgoingLetterSigner>().HasIndex(s => new { s.SourceId, s.UserId }).IsUnique();
        mb.Entity<OutgoingLetterSigner>().HasIndex(s => new { s.UserId, s.IsSigned });

        mb.Entity<Erja>()
            .HasOne(e => e.Source)
            .WithMany(s => s.Erjas)
            .HasForeignKey(e => e.SourceId)
            .OnDelete(DeleteBehavior.Cascade);
        mb.Entity<Erja>()
            .HasOne(e => e.UserSender)
            .WithMany()
            .HasForeignKey(e => e.SenderUserId)
            .OnDelete(DeleteBehavior.Restrict);
        mb.Entity<Erja>()
            .HasOne(e => e.UserReciver)
            .WithMany()
            .HasForeignKey(e => e.ReciverUserId)
            .OnDelete(DeleteBehavior.Restrict);
        mb.Entity<Erja>()
            .HasOne(e => e.Amalgar)
            .WithMany(a => a.Erjas)
            .HasForeignKey(e => e.AmalgarId)
            .OnDelete(DeleteBehavior.Restrict);
        mb.Entity<Erja>().HasIndex(e => e.SourceId);
        mb.Entity<Erja>().HasIndex(e => e.ReciverUserId);
        mb.Entity<Erja>().HasIndex(e => e.SenderUserId);
        mb.Entity<Erja>().HasIndex(e => new { e.ReciverUserId, e.IsRead });

        mb.Entity<RelatedLetter>()
            .HasOne(r => r.Letter)
            .WithMany(s => s.RelatedLetters)
            .HasForeignKey(r => r.LetterId)
            .OnDelete(DeleteBehavior.Cascade);
        mb.Entity<RelatedLetter>()
            .HasOne(r => r.RelateLetter)
            .WithMany(s => s.RelatedToLetters)
            .HasForeignKey(r => r.RelateLetterId)
            .OnDelete(DeleteBehavior.Restrict);

        mb.Entity<PishnevisLetter>().HasIndex(p => p.UserId);
        mb.Entity<LetterBayegani>().HasIndex(b => b.UserId);

        // گروه‌های گیرندگان (پورت جدول Groups طرح کارفرما)
        mb.Entity<LetterGroup>().HasKey(g => g.GroupId);
        mb.Entity<LetterGroup>()
            .HasOne(g => g.Creator)
            .WithMany()
            .HasForeignKey(g => g.CreatorUserId)
            .OnDelete(DeleteBehavior.Restrict);
        mb.Entity<LetterGroupMember>()
            .HasOne(m => m.Group)
            .WithMany(g => g.Members)
            .HasForeignKey(m => m.GroupId)
            .OnDelete(DeleteBehavior.Cascade);
        mb.Entity<LetterGroupMember>()
            .HasOne(m => m.User)
            .WithMany()
            .HasForeignKey(m => m.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        mb.Entity<LetterGroupMember>().HasIndex(m => new { m.GroupId, m.UserId }).IsUnique();

        // ساختار شماره اندیکاتور (LetterStrature طرح کارفرما)
        mb.Entity<LetterStrature>().HasKey(s => s.StratureId);
        mb.Entity<LetterStrature>().HasIndex(s => s.TypeForm);

        // ---------- سازمان‌ها و سمت‌ها (مبنای جزء «واحد» در شماره نامه) ----------
        // NameUniq سازمان در شماره اندیکاتور می‌نشیند — برای جستجو/یکتایی ایندکس می‌شود
        mb.Entity<Organization>().HasIndex(o => o.NameUniq).IsUnique();
        mb.Entity<Semat>()
            .HasOne(s => s.Organization)
            .WithMany(o => o.Semats)
            .HasForeignKey(s => s.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
        mb.Entity<Semat>()
            .HasOne(s => s.User)
            .WithMany()
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.SetNull);
        mb.Entity<Semat>().HasIndex(s => s.UserId);
        mb.Entity<Semat>().HasIndex(s => s.OrganizationId);

        // ==================== RBAC Configuration ====================
        mb.Entity<Role>().HasIndex(r => r.Name).IsUnique();
        mb.Entity<Permission>().HasIndex(p => new { p.Module, p.Action }).IsUnique();

        mb.Entity<RolePermission>()
            .HasKey(rp => new { rp.RoleId, rp.PermissionId });

        mb.Entity<RolePermission>()
            .HasOne(rp => rp.Role)
            .WithMany(r => r.RolePermissions)
            .HasForeignKey(rp => rp.RoleId);

        mb.Entity<RolePermission>()
            .HasOne(rp => rp.Permission)
            .WithMany(p => p.RolePermissions)
            .HasForeignKey(rp => rp.PermissionId);

        mb.Entity<UserRole>()
            .HasKey(ur => new { ur.UserId, ur.RoleId });

        // ---------- تعطیلات شرکتی ----------
        mb.Entity<CompanyHoliday>().HasIndex(h => h.HolidayDate);
        // ---------- تفکیک «منابع انسانی فروغ آریا» از «منابع انسانی اصلی» ----------
        mb.Entity<FaHrPersonnel>().ToTable("FaHrPersonnel");
        mb.Entity<FaHrPersonnel>().HasIndex(p => p.Code);
        mb.Entity<FaHrPersonnel>().Property(p => p.BaseSalary).HasPrecision(18, 2);
        mb.Entity<FaHrSchemaMark>().ToTable("FaHrSchemaMarks");
        mb.Entity<HrMainHoliday>().ToTable("HrMainHolidays");
        mb.Entity<HrMainHoliday>().HasIndex(h => h.HolidayDate);
        mb.Entity<FaPayMinWage>().ToTable("FaPayMinWages");
        mb.Entity<FaPayMinWage>().Property(w => w.Year).ValueGeneratedNever();
        mb.Entity<FaPayMinWage>().Property(w => w.MonthlyWage).HasPrecision(18, 2);
        mb.Entity<FaPayMinWage>().Property(w => w.DailyWage).HasPrecision(18, 2);
        mb.Entity<WorkCalendarDay>().HasIndex(d => d.Date).IsUnique();
        mb.Entity<WorkCalendarSettings>().HasIndex(s => s.Id).IsUnique();
        mb.Entity<AuditLog>().HasIndex(a => a.At);
        mb.Entity<AuditLog>().HasIndex(a => a.UserId);

        // ---------- بازه‌های ورود/خروج روزانه (حداکثر ۵ بازه در روز) ----------
        mb.Entity<AttendanceSegment>()
            .HasIndex(s => new { s.UserId, s.WorkDate, s.Seq })
            .IsUnique();
        mb.Entity<AttendanceSegment>()
            .HasOne(s => s.LinkedLeaveRequest)
            .WithMany()
            .HasForeignKey(s => s.LinkedLeaveRequestId)
            .OnDelete(DeleteBehavior.SetNull);

        // ---------- اتصال مبدأ دستور کار (اتصال عمومی بخش‌ها به دستور کار) ----------
        mb.Entity<WorkOrder>().HasQueryFilter(w => w.DeletedAt == null);
        mb.Entity<WorkOrder>().HasIndex(w => new { w.RecurrenceSeriesId, w.RecurrenceScheduledAt })
            .IsUnique().HasFilter("[RecurrenceSeriesId] IS NOT NULL AND [RecurrenceScheduledAt] IS NOT NULL");
        mb.Entity<WorkOrder>().HasIndex(w => new { w.SourceModule, w.SourceId });

        // ---------- دستور کار: اولویت و یادآور مهلت ----------
        mb.Entity<WorkOrder>().HasIndex(w => new { w.Status, w.DueAt });   // کوئری یادآور/فیلترها
        mb.Entity<WorkOrderReminderLog>()
            .HasIndex(r => new { r.OrderId, r.ThresholdHours, r.DueAtSnapshot })
            .IsUnique();                                                    // هر آستانه فقط یک‌بار به‌ازای هر مهلت
        mb.Entity<WorkOrderChecklistItem>().HasIndex(c => c.OrderId);       // چک‌لیست زیرکار
        mb.Entity<WorkOrderTemplate>().HasIndex(t => t.OwnerUserId);        // قالب‌های آماده — فهرست هر کاربر

        // ---------- پروژه کاری (داخل دستور کار) ----------
        mb.Entity<WorkProject>().HasQueryFilter(p => p.DeletedAt == null);
        mb.Entity<WorkProject>().HasIndex(p => new { p.CompanyId, p.Status });
        mb.Entity<WorkProject>().HasIndex(p => new { p.CompanyId, p.DueAt });
        mb.Entity<WorkProjectMember>().HasIndex(m => new { m.ProjectId, m.UserId }).IsUnique();
        mb.Entity<WorkProjectMember>().HasOne(m => m.Project).WithMany(p => p.Members)
            .HasForeignKey(m => m.ProjectId).OnDelete(DeleteBehavior.Cascade);
        mb.Entity<WorkProjectMindFlowMember>().HasIndex(m => new { m.ProjectId, m.UserId }).IsUnique();
        mb.Entity<WorkProjectMindFlowMember>().HasOne(m => m.Project).WithMany(p => p.MindFlowMembers)
            .HasForeignKey(m => m.ProjectId).OnDelete(DeleteBehavior.Cascade);
        mb.Entity<StandaloneMindFlow>().HasIndex(m => new { m.CompanyId, m.OwnerUserId });
        mb.Entity<StandaloneMindFlowMember>().HasIndex(m => new { m.MindFlowId, m.UserId }).IsUnique();
        mb.Entity<StandaloneMindFlowMember>().HasIndex(m => m.UserId);
        mb.Entity<StandaloneMindFlowMember>().HasOne(m => m.MindFlow).WithMany(m => m.Members)
            .HasForeignKey(m => m.MindFlowId).OnDelete(DeleteBehavior.Cascade);
        mb.Entity<WorkProjectLog>().HasIndex(l => l.ProjectId);
        mb.Entity<WorkProjectLog>().HasOne(l => l.Project).WithMany(p => p.Logs)
            .HasForeignKey(l => l.ProjectId).OnDelete(DeleteBehavior.Cascade);
        mb.Entity<WorkOrder>().HasIndex(w => w.ProjectId);
        mb.Entity<WorkOrder>().HasOne(w => w.Project).WithMany(p => p.WorkOrders)
            .HasForeignKey(w => w.ProjectId).OnDelete(DeleteBehavior.NoAction);

        // ==================== ماژول انبارداری ====================

        // ---------- کالا: فیلدهای تکمیلی ----------
        mb.Entity<Product>().HasIndex(p => p.CategoryId);
        mb.Entity<Product>().HasIndex(p => p.TaxCode);
        mb.Entity<Product>().Property(p => p.SalePrice2).HasPrecision(18, 2);
        mb.Entity<Product>().Property(p => p.VatRate).HasPrecision(6, 2);
        mb.Entity<Product>().Property(p => p.DutyRate).HasPrecision(6, 2);
        mb.Entity<Product>().Property(p => p.OtherTaxRate).HasPrecision(6, 2);
        mb.Entity<Product>().Property(p => p.MinOrderQty).HasPrecision(18, 3);
        mb.Entity<Product>().Property(p => p.UnitFactor).HasPrecision(18, 4);
        mb.Entity<Product>().Property(p => p.Weight).HasPrecision(18, 3);
        mb.Entity<Product>().Property(p => p.Length).HasPrecision(18, 3);
        mb.Entity<Product>().Property(p => p.Width).HasPrecision(18, 3);
        mb.Entity<Product>().Property(p => p.Height).HasPrecision(18, 3);

        // ---------- انبار و گروه کالا ----------
        mb.Entity<Warehouse>().HasIndex(w => w.Code);
        mb.Entity<ProductCategory>().HasIndex(c => c.ParentId);

        // ---------- ویژگی‌های کالا ----------
        mb.Entity<ProductAttributeDef>().HasIndex(a => a.Name);
        mb.Entity<ProductAttributeDef>().HasIndex(a => a.CategoryId);
        mb.Entity<ProductAttributeOption>().HasIndex(o => o.AttributeId);
        mb.Entity<ProductAttributeValue>().HasIndex(v => new { v.ProductId, v.AttributeId }).IsUnique();
        mb.Entity<ProductAttributeValue>().Property(v => v.NumberValue).HasPrecision(18, 4);

        // ---------- نوع رسید و حواله ----------
        mb.Entity<InvDocType>().HasIndex(t => t.Code).IsUnique();
        mb.Entity<InvDocType>().HasIndex(t => t.Nature);

        // ---------- سند انبار ----------
        mb.Entity<InvDoc>().HasIndex(d => d.Number).IsUnique();
        mb.Entity<InvDoc>().HasIndex(d => d.Date);
        mb.Entity<InvDoc>().HasIndex(d => d.DocTypeId);
        mb.Entity<InvDoc>().HasIndex(d => d.WarehouseId);
        mb.Entity<InvDoc>().HasIndex(d => d.Status);
        mb.Entity<InvDoc>().Property(d => d.TotalQuantity).HasPrecision(18, 3);
        mb.Entity<InvDoc>().Property(d => d.TotalValue).HasPrecision(18, 2);
        mb.Entity<InvDoc>()
            .HasMany(d => d.Lines)
            .WithOne(l => l.Doc!)
            .HasForeignKey(l => l.DocId)
            .OnDelete(DeleteBehavior.Cascade);

        mb.Entity<InvDocLine>().HasIndex(l => l.ProductId);
        mb.Entity<InvDocLine>().Property(l => l.Quantity).HasPrecision(18, 3);
        mb.Entity<InvDocLine>().Property(l => l.UnitPrice).HasPrecision(18, 2);
        mb.Entity<InvDocLine>().Property(l => l.Discount).HasPrecision(18, 2);
        mb.Entity<InvDocLine>().Property(l => l.OutCost).HasPrecision(18, 4);

        // ---------- دفتر کاردکس ----------
        mb.Entity<InvLedgerEntry>().HasIndex(e => new { e.ProductId, e.WarehouseId, e.Date });
        mb.Entity<InvLedgerEntry>().HasIndex(e => e.DocId);
        mb.Entity<InvLedgerEntry>().Property(e => e.QtyIn).HasPrecision(18, 3);
        mb.Entity<InvLedgerEntry>().Property(e => e.QtyOut).HasPrecision(18, 3);
        mb.Entity<InvLedgerEntry>().Property(e => e.RemainingQty).HasPrecision(18, 3);
        mb.Entity<InvLedgerEntry>().Property(e => e.BalanceQty).HasPrecision(18, 3);
        mb.Entity<InvLedgerEntry>().Property(e => e.UnitCost).HasPrecision(18, 4);
        mb.Entity<InvLedgerEntry>().Property(e => e.ValueIn).HasPrecision(18, 2);
        mb.Entity<InvLedgerEntry>().Property(e => e.ValueOut).HasPrecision(18, 2);
        mb.Entity<InvLedgerEntry>().Property(e => e.BalanceValue).HasPrecision(18, 2);

        // ---------- مانده انبارداری ----------
        mb.Entity<InvStock>().HasIndex(s => new { s.ProductId, s.WarehouseId }).IsUnique();
        mb.Entity<InvStock>().Property(s => s.Quantity).HasPrecision(18, 3);
        mb.Entity<InvStock>().Property(s => s.Value).HasPrecision(18, 2);
        mb.Entity<InvStock>().Property(s => s.AvgCost).HasPrecision(18, 4);

        // ==================== ماژول حسابداری ====================
        mb.Entity<AccFiscalYear>().HasIndex(f => f.Title).IsUnique();
        mb.Entity<AccFiscalYear>().HasIndex(f => f.IsCurrent);

        mb.Entity<AccAccount>().HasIndex(a => a.Code).IsUnique();
        mb.Entity<AccAccount>().HasIndex(a => a.ParentId);
        mb.Entity<AccAccount>().HasIndex(a => a.Level);
        mb.Entity<AccAccount>()
          .HasOne(a => a.Parent).WithMany()
          .HasForeignKey(a => a.ParentId)
          .OnDelete(DeleteBehavior.Restrict);

        mb.Entity<AccVoucher>().HasIndex(v => new { v.FiscalYearId, v.Number }).IsUnique();
        // ---------- ابعاد تحلیلی ----------
        mb.Entity<AccDimension>().HasIndex(d => d.Code).IsUnique();
        mb.Entity<AccDimensionValue>().HasIndex(v => new { v.DimensionId, v.Code }).IsUnique();
        mb.Entity<AccDimensionValue>().HasIndex(v => v.ParentId);
        mb.Entity<AccDimensionValue>().HasIndex(v => v.CodeTree);
        // ---------- دارایی ثابت ----------
        mb.Entity<FixedAssetCategory>().HasIndex(c => c.Code).IsUnique();
        mb.Entity<FixedAsset>().HasIndex(a => a.Code).IsUnique();
        mb.Entity<FixedAsset>().HasIndex(a => a.CategoryId);
        mb.Entity<FixedAsset>().HasIndex(a => a.DimensionValueId);
        mb.Entity<FixedAssetDepreciationRun>().HasIndex(r => new { r.Year, r.Month }).IsUnique();
        mb.Entity<FixedAssetDepreciationLine>().HasIndex(l => new { l.RunId, l.AssetId }).IsUnique();
        mb.Entity<FixedAsset>().Property(a => a.PurchasePrice).HasPrecision(18, 2);
        mb.Entity<FixedAsset>().Property(a => a.SalvageValue).HasPrecision(18, 2);
        mb.Entity<FixedAsset>().Property(a => a.AccumulatedDepreciation).HasPrecision(18, 2);
        mb.Entity<FixedAssetDepreciationRun>().Property(r => r.TotalAmount).HasPrecision(18, 2);
        mb.Entity<FixedAssetDepreciationLine>().Property(l => l.Amount).HasPrecision(18, 2);
        mb.Entity<FixedAssetDepreciationLine>().Property(l => l.AccumulatedAfter).HasPrecision(18, 2);
        mb.Entity<FixedAssetDepreciationLine>().Property(l => l.BookValueAfter).HasPrecision(18, 2);
        // ---------- بودجه ----------
        mb.Entity<Budget>().HasIndex(b => new { b.FiscalYearId, b.DimensionValueId, b.Name }).IsUnique();
        mb.Entity<BudgetItem>().HasIndex(i => new { i.BudgetId, i.AccAccountId }).IsUnique();
        mb.Entity<BudgetTransaction>().HasIndex(t => t.BudgetId);
        mb.Entity<BudgetTransaction>().HasIndex(t => t.VoucherId);
        mb.Entity<Budget>().Property(b => b.TotalAmount).HasPrecision(18, 2);
        mb.Entity<BudgetItem>().Property(i => i.PlannedAmount).HasPrecision(18, 2);
        mb.Entity<BudgetItem>().Property(i => i.CommittedAmount).HasPrecision(18, 2);
        mb.Entity<BudgetItem>().Property(i => i.ActualAmount).HasPrecision(18, 2);
        mb.Entity<BudgetTransaction>().Property(t => t.Amount).HasPrecision(18, 2);

        // ---------- رفتار حذف ماژول‌های جدید (سازگار با SQL Server) ----------
        // SQL Server با «مسیرهای آبشاری چندگانه» (multiple cascade paths) سازگار نیست؛
        // برای همین FKهای الزامی به Restrict و FKهای اختیاری به SetNull تنظیم شده‌اند.
        // نمونه: BudgetTransaction هم مستقیم و هم از مسیر BudgetItem به Budget می‌رسد.
        mb.Entity<AccVoucher>()
          .HasOne(v => v.FiscalYear).WithMany()
          .HasForeignKey(v => v.FiscalYearId)
          .OnDelete(DeleteBehavior.Restrict);

        mb.Entity<AccVoucherLine>()
          .HasOne(l => l.Voucher).WithMany(v => v.Lines)
          .HasForeignKey(l => l.VoucherId)
          .OnDelete(DeleteBehavior.Cascade); // حذف سند = حذف اقلام (ترکیب)

        mb.Entity<AccVoucherLine>()
          .HasOne(l => l.Account).WithMany()
          .HasForeignKey(l => l.AccountId)
          .OnDelete(DeleteBehavior.Restrict);

        mb.Entity<AccVoucherLine>()
          .HasOne(l => l.DimensionValue).WithMany()
          .HasForeignKey(l => l.DimensionValueId)
          .OnDelete(DeleteBehavior.SetNull);

        mb.Entity<AccDimensionValue>()
          .HasOne(v => v.Dimension).WithMany()
          .HasForeignKey(v => v.DimensionId)
          .OnDelete(DeleteBehavior.Restrict);

        mb.Entity<AccDimensionValue>()
          .HasOne(v => v.Parent).WithMany()
          .HasForeignKey(v => v.ParentId)
          .OnDelete(DeleteBehavior.Restrict);

        mb.Entity<FixedAsset>()
          .HasOne(a => a.Category).WithMany()
          .HasForeignKey(a => a.CategoryId)
          .OnDelete(DeleteBehavior.Restrict);

        mb.Entity<FixedAsset>()
          .HasOne(a => a.DimensionValue).WithMany()
          .HasForeignKey(a => a.DimensionValueId)
          .OnDelete(DeleteBehavior.SetNull);

        mb.Entity<FixedAssetDepreciationLine>()
          .HasOne(l => l.Run).WithMany(r => r.Lines)
          .HasForeignKey(l => l.RunId)
          .OnDelete(DeleteBehavior.Cascade); // حذف اجرا = حذف اقلام (ترکیب)

        mb.Entity<FixedAssetDepreciationLine>()
          .HasOne(l => l.Asset).WithMany()
          .HasForeignKey(l => l.AssetId)
          .OnDelete(DeleteBehavior.Restrict);

        mb.Entity<Budget>()
          .HasOne(b => b.FiscalYear).WithMany()
          .HasForeignKey(b => b.FiscalYearId)
          .OnDelete(DeleteBehavior.Restrict);

        mb.Entity<Budget>()
          .HasOne(b => b.DimensionValue).WithMany()
          .HasForeignKey(b => b.DimensionValueId)
          .OnDelete(DeleteBehavior.SetNull);

        mb.Entity<BudgetItem>()
          .HasOne(i => i.Budget).WithMany(b => b.Items)
          .HasForeignKey(i => i.BudgetId)
          .OnDelete(DeleteBehavior.Restrict);

        mb.Entity<BudgetItem>()
          .HasOne(i => i.AccAccount).WithMany()
          .HasForeignKey(i => i.AccAccountId)
          .OnDelete(DeleteBehavior.Restrict);

        mb.Entity<BudgetTransaction>()
          .HasOne(t => t.Budget).WithMany()
          .HasForeignKey(t => t.BudgetId)
          .OnDelete(DeleteBehavior.Restrict);

        mb.Entity<BudgetTransaction>()
          .HasOne(t => t.BudgetItem).WithMany()
          .HasForeignKey(t => t.BudgetItemId)
          .OnDelete(DeleteBehavior.Restrict);

        mb.Entity<AccVoucher>().HasIndex(v => v.Date);
        mb.Entity<AccVoucher>().HasIndex(v => v.Status);
        mb.Entity<AccVoucher>().HasIndex(v => new { v.Source, v.SourceId });
        mb.Entity<AccVoucher>().Property(v => v.TotalDebit).HasPrecision(18, 2);
        mb.Entity<AccVoucher>().Property(v => v.TotalCredit).HasPrecision(18, 2);
        mb.Entity<AccVoucher>()
          .HasOne(v => v.FiscalYear).WithMany()
          .HasForeignKey(v => v.FiscalYearId)
          .OnDelete(DeleteBehavior.Restrict);

        mb.Entity<AccVoucherLine>().HasIndex(l => l.AccountId);
        mb.Entity<AccVoucherLine>().HasIndex(l => l.PartyId);
        mb.Entity<AccVoucherLine>().Property(l => l.Debit).HasPrecision(18, 2);
        mb.Entity<AccVoucherLine>().Property(l => l.Credit).HasPrecision(18, 2);
        mb.Entity<AccVoucherLine>()
          .HasOne(l => l.Voucher).WithMany(v => v.Lines)
          .HasForeignKey(l => l.VoucherId)
          .OnDelete(DeleteBehavior.Cascade);
        mb.Entity<AccVoucherLine>()
          .HasOne(l => l.Account).WithMany()
          .HasForeignKey(l => l.AccountId)
          .OnDelete(DeleteBehavior.Restrict);

        mb.Entity<AccInvRule>().HasIndex(r => r.DocTypeId).IsUnique();
        mb.Entity<AccInvRule>()
          .HasOne(r => r.DocType).WithMany()
          .HasForeignKey(r => r.DocTypeId)
          .OnDelete(DeleteBehavior.Cascade);

        // ============ ماژول فاکتور ============
        mb.Entity<FacInvoice>().HasIndex(i => new { i.Kind, i.Number }).IsUnique();
        mb.Entity<FacInvoice>().HasIndex(i => i.Date);
        mb.Entity<FacInvoice>().HasIndex(i => i.Status);
        mb.Entity<FacInvoice>().HasIndex(i => i.PartyId);
        mb.Entity<FacInvoice>().HasIndex(i => i.InvDocId);
        mb.Entity<FacInvoice>().HasIndex(i => i.VoucherId);
        foreach (var prop in new[] { "TotalGross", "TotalLineDiscount", "InvoiceDiscount",
                                     "TotalTaxable", "TotalVat", "ShippingCost", "TotalNet" })
            mb.Entity<FacInvoice>().Property(prop).HasPrecision(18, 2);
        mb.Entity<FacInvoice>()
          .HasOne(i => i.Party).WithMany()
          .HasForeignKey(i => i.PartyId)
          .OnDelete(DeleteBehavior.Restrict);
        mb.Entity<FacInvoice>()
          .HasOne(i => i.Warehouse).WithMany()
          .HasForeignKey(i => i.WarehouseId)
          .OnDelete(DeleteBehavior.Restrict);

        mb.Entity<FacInvoiceLine>().HasIndex(l => l.InvoiceId);
        mb.Entity<FacInvoiceLine>().HasIndex(l => l.ProductId);
        foreach (var prop in new[] { "Quantity", "UnitPrice", "DiscountPercent", "Discount",
                                     "VatRate", "VatAmount", "Taxable", "Total" })
            mb.Entity<FacInvoiceLine>().Property(prop).HasPrecision(18, 2);
        mb.Entity<FacInvoiceLine>()
          .HasOne(l => l.Invoice).WithMany(i => i.Lines)
          .HasForeignKey(l => l.InvoiceId)
          .OnDelete(DeleteBehavior.Cascade);
        mb.Entity<FacInvoiceLine>()
          .HasOne(l => l.Product).WithMany()
          .HasForeignKey(l => l.ProductId)
          .OnDelete(DeleteBehavior.Restrict);

        mb.Entity<FacRule>().HasIndex(r => r.Kind).IsUnique();
        mb.Entity<FacRule>()
          .HasOne(r => r.DocType).WithMany()
          .HasForeignKey(r => r.DocTypeId)
          .OnDelete(DeleteBehavior.SetNull);

        // ============ ماژول خزانه‌داری ============

        // ---------- صندوق / بانک ----------
        mb.Entity<TrsAccount>().HasIndex(a => a.Code).IsUnique();
        mb.Entity<TrsAccount>().HasIndex(a => a.Kind);
        mb.Entity<TrsAccount>().Property(a => a.OpeningBalance).HasPrecision(18, 2);
        mb.Entity<TrsAccount>()
          .HasOne(a => a.Account).WithMany()
          .HasForeignKey(a => a.AccountId)
          .OnDelete(DeleteBehavior.SetNull);

        // ---------- سند خزانه ----------
        mb.Entity<TrsVoucher>().HasIndex(v => new { v.Kind, v.Number }).IsUnique();
        mb.Entity<TrsVoucher>().HasIndex(v => v.Date);
        mb.Entity<TrsVoucher>().HasIndex(v => v.Status);
        mb.Entity<TrsVoucher>().HasIndex(v => v.PartyId);
        mb.Entity<TrsVoucher>().HasIndex(v => v.VoucherId);
        mb.Entity<TrsVoucher>().HasIndex(v => v.InvoiceId);
        foreach (var prop in new[] { "TotalAmount", "CashAmount", "ChequeAmount",
                                     "DiscountAmount", "FeeAmount" })
            mb.Entity<TrsVoucher>().Property(prop).HasPrecision(18, 2);
        mb.Entity<TrsVoucher>()
          .HasOne(v => v.Party).WithMany()
          .HasForeignKey(v => v.PartyId)
          .OnDelete(DeleteBehavior.Restrict);
        mb.Entity<TrsVoucher>()
          .HasOne(v => v.FromAccount).WithMany()
          .HasForeignKey(v => v.FromAccountId)
          .OnDelete(DeleteBehavior.Restrict);
        mb.Entity<TrsVoucher>()
          .HasOne(v => v.ToAccount).WithMany()
          .HasForeignKey(v => v.ToAccountId)
          .OnDelete(DeleteBehavior.Restrict);

        // ---------- سطر سند خزانه ----------
        mb.Entity<TrsVoucherLine>().HasIndex(l => l.TrsVoucherId);
        mb.Entity<TrsVoucherLine>().HasIndex(l => l.TrsAccountId);
        mb.Entity<TrsVoucherLine>().HasIndex(l => l.ChequeId);
        mb.Entity<TrsVoucherLine>().Property(l => l.Amount).HasPrecision(18, 2);
        mb.Entity<TrsVoucherLine>()
          .HasOne(l => l.TrsVoucher).WithMany(v => v.Lines)
          .HasForeignKey(l => l.TrsVoucherId)
          .OnDelete(DeleteBehavior.Cascade);
        mb.Entity<TrsVoucherLine>()
          .HasOne(l => l.TrsAccount).WithMany()
          .HasForeignKey(l => l.TrsAccountId)
          .OnDelete(DeleteBehavior.Restrict);
        mb.Entity<TrsVoucherLine>()
          .HasOne(l => l.Cheque).WithMany()
          .HasForeignKey(l => l.ChequeId)
          .OnDelete(DeleteBehavior.SetNull);

        // ---------- تخصیص سند خزانه به فاکتور (چند فاکتور / تسویه جزئی) ----------
        mb.Entity<TrsInvoiceAllocation>().HasIndex(a => a.InvoiceId);
        mb.Entity<TrsInvoiceAllocation>().HasIndex(a => new { a.TrsVoucherId, a.InvoiceId }).IsUnique();
        mb.Entity<TrsInvoiceAllocation>().Property(a => a.Amount).HasPrecision(18, 2);
        mb.Entity<TrsInvoiceAllocation>()
          .HasOne(a => a.TrsVoucher).WithMany(v => v.InvoiceAllocations)
          .HasForeignKey(a => a.TrsVoucherId)
          .OnDelete(DeleteBehavior.Cascade);
        mb.Entity<TrsInvoiceAllocation>()
          .HasOne(a => a.Invoice).WithMany()
          .HasForeignKey(a => a.InvoiceId)
          .OnDelete(DeleteBehavior.Restrict);

        // ---------- چک ----------
        mb.Entity<TrsCheque>().HasIndex(c => new { c.Kind, c.Number });
        mb.Entity<TrsCheque>().HasIndex(c => c.DueDate);
        mb.Entity<TrsCheque>().HasIndex(c => c.Status);
        mb.Entity<TrsCheque>().HasIndex(c => c.PartyId);
        mb.Entity<TrsCheque>().HasIndex(c => c.TrsVoucherId);
        mb.Entity<TrsCheque>().Property(c => c.Amount).HasPrecision(18, 2);
        mb.Entity<TrsCheque>()
          .HasOne(c => c.Party).WithMany()
          .HasForeignKey(c => c.PartyId)
          .OnDelete(DeleteBehavior.Restrict);
        mb.Entity<TrsCheque>()
          .HasOne(c => c.TrsAccount).WithMany()
          .HasForeignKey(c => c.TrsAccountId)
          .OnDelete(DeleteBehavior.Restrict);

        // ---------- عملیات چک ----------
        mb.Entity<TrsChequeAction>().HasIndex(a => a.ChequeId);
        mb.Entity<TrsChequeAction>()
          .HasOne(a => a.Cheque).WithMany(c => c.Actions)
          .HasForeignKey(a => a.ChequeId)
          .OnDelete(DeleteBehavior.Cascade);

        // ---------- قواعد خزانه ----------
        mb.Entity<TrsRule>().HasIndex(r => r.Kind).IsUnique();

        // ============ ماژول انبارگردانی و بارکد ============

        // ---------- بارکد ----------
        mb.Entity<BcdBarcode>().HasIndex(b => b.Code).IsUnique();
        mb.Entity<BcdBarcode>().HasIndex(b => b.ProductId);
        mb.Entity<BcdBarcode>().Property(b => b.PackQty).HasPrecision(18, 3);
        mb.Entity<BcdBarcode>()
          .HasOne(b => b.Product).WithMany()
          .HasForeignKey(b => b.ProductId)
          .OnDelete(DeleteBehavior.Cascade);

        // ---------- دوره انبارگردانی ----------
        mb.Entity<StkSession>().HasIndex(s => s.Number).IsUnique();
        mb.Entity<StkSession>().HasIndex(s => s.WarehouseId);
        mb.Entity<StkSession>().HasIndex(s => s.Status);
        mb.Entity<StkSession>().HasIndex(s => s.Date);
        mb.Entity<StkSession>()
          .HasOne(s => s.Warehouse).WithMany()
          .HasForeignKey(s => s.WarehouseId)
          .OnDelete(DeleteBehavior.Restrict);

        // ---------- اقلام شمارش ----------
        mb.Entity<StkLine>().HasIndex(l => l.SessionId);
        mb.Entity<StkLine>().HasIndex(l => new { l.SessionId, l.ProductId }).IsUnique();
        mb.Entity<StkLine>().Property(l => l.SystemQty).HasPrecision(18, 3);
        mb.Entity<StkLine>().Property(l => l.CountedQty).HasPrecision(18, 3);
        mb.Entity<StkLine>().Property(l => l.UnitCost).HasPrecision(18, 2);
        mb.Entity<StkLine>()
          .HasOne(l => l.Session).WithMany(s => s.Lines)
          .HasForeignKey(l => l.SessionId)
          .OnDelete(DeleteBehavior.Cascade);
        mb.Entity<StkLine>()
          .HasOne(l => l.Product).WithMany()
          .HasForeignKey(l => l.ProductId)
          .OnDelete(DeleteBehavior.Restrict);

        // ---------- تاریخچه اسکن ----------
        mb.Entity<StkScan>().HasIndex(s => s.SessionId);
        mb.Entity<StkScan>().HasIndex(s => s.LineId);
        mb.Entity<StkScan>().Property(s => s.Quantity).HasPrecision(18, 3);
        mb.Entity<StkScan>()
          .HasOne(s => s.Session).WithMany()
          .HasForeignKey(s => s.SessionId)
          .OnDelete(DeleteBehavior.Cascade);

        // ---------- ایمیل سازمانی ----------
        // جدولِ قدیمیِ پیوست‌ها (Oto_TBL_EmailAttachments) پیوست را با دو ستون
        // Type (Inbox/Sent) و Email_id (شناسه پیام) به نامه وصل می‌کند و ستونِ
        // کلیدِ خارجیِ جداگانه‌ای ندارد.
        // مجموعه‌های پیمایشیِ Attachments روی دریافتی/ارسالی بی‌کلید هستند، پس EF برای
        // آن‌ها کلیدِ پنهان می‌تراشد (OtoInboxEmailInboxId / OtoSentEmailSentId) که در
        // پایگاه‌داده وجود ندارد و هر بار خواندنِ پیوست با «Invalid column name» شکست
        // می‌خورد — در نتیجه باز کردنِ نامه خطای ۴۰۰ می‌داد.
        // پیوست‌ها در سرویس با همین دو ستون واکشی می‌شوند، پس این مجموعه‌ها نادیده
        // گرفته می‌شوند تا مدل با پایگاه‌داده‌ی موجود هم‌خوان بماند.
        mb.Entity<OtoInboxEmail>().Ignore(e => e.Attachments);
        mb.Entity<OtoSentEmail>().Ignore(e => e.Attachments);

        // ============ فرم‌های متفرقه — صورتجلسه ============
        mb.Entity<MeetingMinutes>().HasIndex(m => m.Status);
        mb.Entity<MeetingMinutesParticipant>()
            .HasOne(p => p.Minutes)
            .WithMany(m => m.Participants)
            .HasForeignKey(p => p.MinutesId)
            .OnDelete(DeleteBehavior.Cascade);
        mb.Entity<MeetingMinutesParticipant>().HasIndex(p => p.MinutesId);
        mb.Entity<MeetingMinutesItem>()
            .HasOne(i => i.Minutes)
            .WithMany(m => m.Items)
            .HasForeignKey(i => i.MinutesId)
            .OnDelete(DeleteBehavior.Cascade);
        mb.Entity<MeetingMinutesItem>().HasIndex(i => i.MinutesId);

        // ============ میز کار توسعه (DevTeam) ============
        mb.Entity<DtWorkflowStatus>().HasIndex(s => s.Key).IsUnique();
        mb.Entity<DtProductModule>().HasIndex(m => m.Key).IsUnique();

        mb.Entity<DtTask>().HasIndex(t => t.Number).IsUnique();
        mb.Entity<DtTask>().HasIndex(t => t.StatusId);
        mb.Entity<DtTask>().HasIndex(t => t.AssigneeUserId);
        mb.Entity<DtTask>().HasIndex(t => t.ModuleId);
        mb.Entity<DtTask>().HasIndex(t => t.SprintId);
        mb.Entity<DtTask>().HasIndex(t => t.IsDeleted);
        mb.Entity<DtTask>().HasIndex(t => t.ParentTaskId);
        mb.Entity<DtTask>().Property(t => t.EstimateHours).HasPrecision(10, 2);
        mb.Entity<DtTask>().Property(t => t.SpentHours).HasPrecision(10, 2);
        mb.Entity<DtTask>()
            .HasOne(t => t.Status).WithMany()
            .HasForeignKey(t => t.StatusId)
            .OnDelete(DeleteBehavior.Restrict);
        mb.Entity<DtTask>()
            .HasOne(t => t.Module).WithMany()
            .HasForeignKey(t => t.ModuleId)
            .OnDelete(DeleteBehavior.SetNull);
        mb.Entity<DtTask>()
            .HasOne(t => t.Sprint).WithMany()
            .HasForeignKey(t => t.SprintId)
            .OnDelete(DeleteBehavior.SetNull);
        mb.Entity<DtTask>()
            .HasOne(t => t.ParentTask)
            .WithMany(t => t.SubTasks)
            .HasForeignKey(t => t.ParentTaskId)
            .OnDelete(DeleteBehavior.Restrict);

        mb.Entity<DtTaskDependency>()
            .HasIndex(d => new { d.TaskId, d.DependsOnTaskId }).IsUnique();
        mb.Entity<DtTaskDependency>().HasIndex(d => d.DependsOnTaskId);
        mb.Entity<DtTaskDependency>()
            .HasOne(d => d.Task)
            .WithMany(t => t.BlockedByLinks)
            .HasForeignKey(d => d.TaskId)
            .OnDelete(DeleteBehavior.Cascade);
        mb.Entity<DtTaskDependency>()
            .HasOne(d => d.DependsOnTask)
            .WithMany(t => t.BlockingLinks)
            .HasForeignKey(d => d.DependsOnTaskId)
            .OnDelete(DeleteBehavior.Restrict);

        mb.Entity<DtTaskComment>()
            .HasOne(c => c.Task).WithMany(t => t.Comments)
            .HasForeignKey(c => c.TaskId)
            .OnDelete(DeleteBehavior.Cascade);
        mb.Entity<DtTaskComment>().HasIndex(c => c.TaskId);

        mb.Entity<DtTaskActivity>()
            .HasOne(a => a.Task).WithMany(t => t.Activities)
            .HasForeignKey(a => a.TaskId)
            .OnDelete(DeleteBehavior.Cascade);
        mb.Entity<DtTaskActivity>().HasIndex(a => a.TaskId);

        mb.Entity<DtTaskGitLink>()
            .HasOne(g => g.Task).WithMany(t => t.GitLinks)
            .HasForeignKey(g => g.TaskId)
            .OnDelete(DeleteBehavior.Cascade);
        mb.Entity<DtTaskGitLink>().HasIndex(g => g.TaskId);

        mb.Entity<DtTimeEntry>()
            .HasOne(e => e.Task).WithMany(t => t.TimeEntries)
            .HasForeignKey(e => e.TaskId)
            .OnDelete(DeleteBehavior.Cascade);
        mb.Entity<DtTimeEntry>().HasIndex(e => e.TaskId);
        mb.Entity<DtTimeEntry>().Property(e => e.Hours).HasPrecision(10, 2);

        mb.Entity<DtTaskChecklistItem>()
            .HasOne(c => c.Task).WithMany()
            .HasForeignKey(c => c.TaskId)
            .OnDelete(DeleteBehavior.Cascade);
        mb.Entity<DtTaskChecklistItem>().HasIndex(c => c.TaskId);

        mb.Entity<DtProblem>().HasIndex(p => p.Status);
        mb.Entity<DtProblem>().HasIndex(p => p.Severity);
        mb.Entity<DtProblem>().HasIndex(p => p.ModuleId);
        mb.Entity<DtProblem>()
            .HasOne(p => p.Module).WithMany()
            .HasForeignKey(p => p.ModuleId)
            .OnDelete(DeleteBehavior.SetNull);
        mb.Entity<DtProblem>()
            .HasOne(p => p.Task).WithMany()
            .HasForeignKey(p => p.TaskId)
            .OnDelete(DeleteBehavior.SetNull);

        mb.Entity<DtModuleChange>().HasIndex(c => c.ModuleId);
        mb.Entity<DtModuleChange>().HasIndex(c => c.ChangedAt);
        mb.Entity<DtModuleChange>()
            .HasOne(c => c.Module).WithMany()
            .HasForeignKey(c => c.ModuleId)
            .OnDelete(DeleteBehavior.Restrict);
        mb.Entity<DtModuleChange>()
            .HasOne(c => c.Task).WithMany()
            .HasForeignKey(c => c.TaskId)
            .OnDelete(DeleteBehavior.SetNull);

        // ==================== فیلتر خودکار چند شرکتی ====================
        // هر رکورد فقط در شرکت فعالِ انتخاب‌شده در نوار بالای برنامه دیده می‌شود.
        // ActiveCompanyId == null یعنی زمینهٔ شرکتی تعیین نشده (اجرای پس‌زمینه/نصب اولیه) → بدون فیلتر.
        mb.Entity<Product>().HasQueryFilter(x => ActiveCompanyId == null || x.CompanyId == ActiveCompanyId);
        mb.Entity<ProductCategory>().HasQueryFilter(x => ActiveCompanyId == null || x.CompanyId == ActiveCompanyId);
        mb.Entity<MeasureUnit>().HasQueryFilter(x => ActiveCompanyId == null || x.CompanyId == ActiveCompanyId);
        mb.Entity<Warehouse>().HasQueryFilter(x => ActiveCompanyId == null || x.CompanyId == ActiveCompanyId);
        mb.Entity<Party>().HasQueryFilter(x => ActiveCompanyId == null || x.CompanyId == ActiveCompanyId);
        mb.Entity<Referrer>().HasQueryFilter(x => ActiveCompanyId == null || x.CompanyId == ActiveCompanyId);
        mb.Entity<ReferrerPayment>().HasQueryFilter(x => ActiveCompanyId == null || x.CompanyId == ActiveCompanyId);
        mb.Entity<Stock>().HasQueryFilter(x => ActiveCompanyId == null || x.CompanyId == ActiveCompanyId);
        mb.Entity<Transaction>().HasQueryFilter(x => ActiveCompanyId == null || x.CompanyId == ActiveCompanyId);
        mb.Entity<TransactionLine>().HasQueryFilter(x => ActiveCompanyId == null || x.CompanyId == ActiveCompanyId);
        mb.Entity<GoodsIssue>().HasQueryFilter(x => ActiveCompanyId == null || x.CompanyId == ActiveCompanyId);
        mb.Entity<GoodsIssueLine>().HasQueryFilter(x => ActiveCompanyId == null || x.CompanyId == ActiveCompanyId);
        mb.Entity<VisOrder>().HasQueryFilter(x => ActiveCompanyId == null || x.CompanyId == ActiveCompanyId);
        mb.Entity<VisOrderLine>().HasQueryFilter(x => ActiveCompanyId == null || x.CompanyId == ActiveCompanyId);
        mb.Entity<VisOrderHistory>().HasQueryFilter(x => ActiveCompanyId == null || x.CompanyId == ActiveCompanyId);
        mb.Entity<VisCustomerLink>().HasQueryFilter(x => ActiveCompanyId == null || x.CompanyId == ActiveCompanyId);
        mb.Entity<Expense>().HasQueryFilter(x => ActiveCompanyId == null || x.CompanyId == ActiveCompanyId);
        mb.Entity<ExpenseCategory>().HasQueryFilter(x => ActiveCompanyId == null || x.CompanyId == ActiveCompanyId);
        mb.Entity<Cheque>().HasQueryFilter(x => ActiveCompanyId == null || x.CompanyId == ActiveCompanyId);
        mb.Entity<InstallmentLine>().HasQueryFilter(x => ActiveCompanyId == null || x.CompanyId == ActiveCompanyId);
        mb.Entity<Technician>().HasQueryFilter(x => ActiveCompanyId == null || x.CompanyId == ActiveCompanyId);
        mb.Entity<RepairOrder>().HasQueryFilter(x => ActiveCompanyId == null || x.CompanyId == ActiveCompanyId);
        mb.Entity<RepairDevice>().HasQueryFilter(x => ActiveCompanyId == null || x.CompanyId == ActiveCompanyId);
        mb.Entity<RepairItem>().HasQueryFilter(x => ActiveCompanyId == null || x.CompanyId == ActiveCompanyId);

        // ==================== سامانه مودیان (گواهی امضا — SDK 2.0.29) ====================
        mb.Entity<MdnMeasureUnit>().HasIndex(u => u.Code).IsUnique();
        mb.Entity<MdnTaxpayerProfile>().HasIndex(p => p.CompanyId);
        mb.Entity<MdnBuyer>().HasIndex(b => new { b.CompanyId, b.EconomicCode });
        mb.Entity<MdnBuyer>().HasIndex(b => new { b.CompanyId, b.NationalId });
        mb.Entity<MdnCatalogItem>().HasIndex(c => new { c.CompanyId, c.Sstid });
        mb.Entity<MdnInvoice>().HasIndex(i => new { i.CompanyId, i.InternalNumber });
        mb.Entity<MdnInvoice>().HasIndex(i => i.TaxId);
        mb.Entity<MdnInvoice>().HasIndex(i => i.ReferenceNumber);
        mb.Entity<MdnInvoice>().HasIndex(i => new { i.CompanyId, i.SubmissionStatus });
        mb.Entity<MdnInvoice>().HasIndex(i => new { i.CompanyId, i.FolderStatus });

        mb.Entity<MdnInvoiceLine>()
            .HasOne(l => l.Invoice)
            .WithMany(i => i.Lines)
            .HasForeignKey(l => l.InvoiceId)
            .OnDelete(DeleteBehavior.Cascade);

        mb.Entity<MdnInvoicePayment>()
            .HasOne(p => p.Invoice)
            .WithMany(i => i.Payments)
            .HasForeignKey(p => p.InvoiceId)
            .OnDelete(DeleteBehavior.Cascade);

        mb.Entity<MdnSubmissionLog>()
            .HasOne(l => l.Invoice)
            .WithMany(i => i.Logs)
            .HasForeignKey(l => l.InvoiceId)
            .OnDelete(DeleteBehavior.SetNull);

        mb.Entity<MdnTaxpayerProfile>().HasQueryFilter(x => ActiveCompanyId == null || x.CompanyId == ActiveCompanyId);
        mb.Entity<MdnBuyer>().HasQueryFilter(x => ActiveCompanyId == null || x.CompanyId == ActiveCompanyId);
        mb.Entity<MdnCatalogItem>().HasQueryFilter(x => ActiveCompanyId == null || x.CompanyId == ActiveCompanyId);
        mb.Entity<MdnInvoice>().HasQueryFilter(x => ActiveCompanyId == null || x.CompanyId == ActiveCompanyId);
        mb.Entity<MdnInvoiceLine>().HasQueryFilter(x => ActiveCompanyId == null || x.CompanyId == ActiveCompanyId);
        mb.Entity<MdnInvoicePayment>().HasQueryFilter(x => ActiveCompanyId == null || x.CompanyId == ActiveCompanyId);
        mb.Entity<MdnSubmissionLog>().HasQueryFilter(x => ActiveCompanyId == null || x.CompanyId == ActiveCompanyId);
    }
}
