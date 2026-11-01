using Microsoft.EntityFrameworkCore;
using RadisHr.Shared.Models;

namespace RadisHr.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    // پرسنل و کاربران
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<EmployeeContract> EmployeeContracts => Set<EmployeeContract>();
    public DbSet<AppUser> Users => Set<AppUser>();

    // الزامات قانونی
    public DbSet<StatutoryRules> StatutoryRules => Set<StatutoryRules>();
    public DbSet<StatutoryRuleAudit> RuleAudits => Set<StatutoryRuleAudit>();
    public DbSet<AuditNotice> CeoNotifications => Set<AuditNotice>();

    // حضور و غیاب
    public DbSet<AttendanceDay> AttendanceDays => Set<AttendanceDay>();
    public DbSet<LeaveMission> LeaveMissions => Set<LeaveMission>();
    public DbSet<ManualPunch> ManualPunches => Set<ManualPunch>();
    public DbSet<ImportAudit> ImportAudits => Set<ImportAudit>();
    public DbSet<PayrollRow> PayrollRows => Set<PayrollRow>();
    public DbSet<GuardCycle> GuardCycles => Set<GuardCycle>();

    // تقویم و ساختار
    public DbSet<Holiday> Holidays => Set<Holiday>();
    public DbSet<UnitSchedule> UnitSchedules => Set<UnitSchedule>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<WorkStation> WorkStations => Set<WorkStation>();

    // HSE
    public DbSet<Incident> Incidents => Set<Incident>();
    public DbSet<CorrectiveAction> CorrectiveActions => Set<CorrectiveAction>();
    public DbSet<MedicalDocument> MedicalDocuments => Set<MedicalDocument>();
    public DbSet<PpeDelivery> PpeDeliveries => Set<PpeDelivery>();
    public DbSet<Extinguisher> Extinguishers => Set<Extinguisher>();
    public DbSet<HseNotification> HseNotifications => Set<HseNotification>();
    public DbSet<PpeAuthorization> PpeAuthorizations => Set<PpeAuthorization>();
    public DbSet<HseDefinition> HseDefinitions => Set<HseDefinition>();

    // گردش کار مالی
    public DbSet<Advance> Advances => Set<Advance>();
    public DbSet<AdvanceInstallment> AdvanceInstallments => Set<AdvanceInstallment>();
    public DbSet<AccountingAdjustment> AccountingAdjustments => Set<AccountingAdjustment>();
    public DbSet<AccountingArchive> AccountingArchives => Set<AccountingArchive>();
    public DbSet<PayrollPayment> PayrollPayments => Set<PayrollPayment>();

    // اطلاعیه‌ها و فایل‌ها
    public DbSet<Announcement> Announcements => Set<Announcement>();
    public DbSet<AnnouncementRecipient> AnnouncementRecipients => Set<AnnouncementRecipient>();
    public DbSet<AnnouncementAttachment> AnnouncementAttachments => Set<AnnouncementAttachment>();
    public DbSet<StoredFile> StoredFiles => Set<StoredFile>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        // ── همهٔ decimal ها ریال هستند: بدون اعشار ذخیره نشوند، دقت کامل حفظ شود
        foreach (var entity in b.Model.GetEntityTypes())
            foreach (var prop in entity.GetProperties())
                if (prop.ClrType == typeof(decimal) || prop.ClrType == typeof(decimal?))
                    prop.SetColumnType("decimal(20,2)");

        // ── پرسنل
        b.Entity<Employee>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();
            e.HasIndex(x => x.Nid);
            e.HasIndex(x => new { x.Unit, x.WorkStation });
            e.HasMany(x => x.Contracts).WithOne()
             .HasForeignKey(c => c.EmployeeId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Photo).HasColumnType("nvarchar(max)");
        });

        b.Entity<AppUser>(e =>
        {
            // نام جدول فقط در لایه نگاشت تغییر کرده تا با Users هسته تداخل نداشته باشد؛ موجودیت دست‌نخورده است.
            e.ToTable("RadisHrUsers");
            e.HasIndex(x => x.UserKey).IsUnique();
        });

        // ── الزامات قانونی: یک ردیف برای هر سال
        b.Entity<StatutoryRules>(e =>
        {
            e.HasIndex(x => x.Year).IsUnique();
            e.OwnsMany(x => x.TaxBrackets, tb =>
            {
                tb.ToTable("StatutoryTaxBrackets");
                tb.WithOwner().HasForeignKey("StatutoryRulesId");
                tb.Property<int>("Id");
                tb.HasKey("Id");
            });
            e.OwnsOne(x => x.Insurable, i => i.ToJson());
        });

        // ── حضور و غیاب: کلید طبیعی code|date
        b.Entity<AttendanceDay>(e =>
        {
            e.HasIndex(x => x.Key).IsUnique();
            e.HasIndex(x => new { x.Code, x.Month });
            e.HasIndex(x => x.Date);
        });

        b.Entity<PayrollRow>(e =>
        {
            e.HasIndex(x => new { x.Code, x.Month }).IsUnique();
        });

        b.Entity<LeaveMission>(e => e.HasIndex(x => new { x.Employee, x.Date }));
        b.Entity<ManualPunch>(e => e.HasIndex(x => new { x.Employee, x.Date }));
        b.Entity<GuardCycle>(e => e.HasIndex(x => x.EmployeeCode).IsUnique());
        b.Entity<Holiday>(e => e.HasIndex(x => x.Date).IsUnique());
        b.Entity<UnitSchedule>(e => e.HasIndex(x => x.Unit).IsUnique());

        // ── ساختار سازمانی
        b.Entity<Department>(e =>
        {
            e.HasIndex(x => x.Name).IsUnique();
            e.HasMany(x => x.Stations).WithOne()
             .HasForeignKey(s => s.DepartmentId).OnDelete(DeleteBehavior.Cascade);
        });

        // ── HSE
        b.Entity<Incident>(e =>
        {
            e.HasIndex(x => x.CaseNo).IsUnique();
            e.HasMany(x => x.CorrectiveActions).WithOne()
             .HasForeignKey(a => a.IncidentId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.MedicalDocuments).WithOne()
             .HasForeignKey(d => d.IncidentId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<PpeAuthorization>(e => e.HasIndex(x => new { x.StationKey, x.EquipmentType }).IsUnique());
        b.Entity<HseDefinition>(e => e.HasIndex(x => new { x.Group, x.Title }).IsUnique());

        // ── مالی
        b.Entity<Advance>(e =>
        {
            e.HasIndex(x => x.EmployeeCode);
            e.HasMany(x => x.Installments).WithOne()
             .HasForeignKey(i => i.AdvanceId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<AccountingAdjustment>(e => e.HasIndex(x => new { x.EmployeeCode, x.Month }));
        b.Entity<AccountingArchive>(e => e.HasIndex(x => x.Month).IsUnique());
        b.Entity<PayrollPayment>(e => e.HasIndex(x => new { x.EmployeeCode, x.Month }));

        // ── اطلاعیه‌ها
        b.Entity<Announcement>(e =>
        {
            e.HasMany(x => x.Recipients).WithOne()
             .HasForeignKey(r => r.AnnouncementId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Attachments).WithOne()
             .HasForeignKey(a => a.AnnouncementId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<StoredFile>(e =>
        {
            e.HasIndex(x => x.Uid).IsUnique();
            e.Property(x => x.Content).HasColumnType("varbinary(max)");
        });
    }
}
