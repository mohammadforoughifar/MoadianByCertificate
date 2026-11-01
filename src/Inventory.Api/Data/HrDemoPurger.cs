using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>
/// پاک‌سازی کامل داده‌های تجاری ماژول منابع انسانی — برای حذف داده‌های آزمایشی/فیک.
/// دامنه: HrMain (ساختار سازمانی جدید)، HrCore (پرونده/قرارداد/حکم/جذب/خروج/ارزیابی)،
/// HrTalent (درخواست ویرایش پرونده)، FaAtt (حضور و مرخصی)، FaPay (حقوق)، FaLms (آموزش)،
/// FaCom (ارتباطات) و داده‌های تجاری سامانه‌های پیشین HR (حضور/مرخصی/حقوق قدیمی) + تاریخچه عملیات همین ماژول‌ها.
///
/// دست‌نخورده می‌ماند: کاربران، نقش‌ها و مجوزها (RBAC)، اشتراک‌های Push و اعلان‌ها،
/// داده‌های پایه‌ای که MasterSeederها در اولین اجرا می‌سازند (انواع مرخصی، شیفت‌ها، دستگاه‌های تردد،
/// اقلام و تنظیمات و جدول مالیات حقوق، قالب‌های قرارداد)، تنظیمات (HrMainRules/HrMainLocale)،
/// تقویم کاری و تعطیلات، و قالب‌های گزارش پرسنلی.
///
/// نکته: اگر فلگ Database:SeedDemoData=true باشد، با خالی‌شدن جداول، داده نمونه در ری‌استارت بعدی
/// دوباره ساخته می‌شود — اندپوینت پاک‌سازی در پاسخ هشدار می‌دهد.
/// </summary>
public static class HrDemoPurger
{
    public record PurgeResult(int Total, Dictionary<string, int> Areas);

    /// <summary>ماژول‌هایی که تاریخچه‌ی عملیات (AuditLog) آن‌ها هم پاک می‌شود.</summary>
    private static readonly string[] HrAuditModules =
    [
        "HrCore", "HrMain", "HrTalent", "FaAtt", "FaPay", "FaLms", "FaCom",
        "Hr", "HrPay", "HrTime", "HrPerf", "Attendance", "LeaveRequests", "WorkCalendar"
    ];

    public static async Task<PurgeResult> PurgeAsync(AppDbContext db, CancellationToken ct = default)
    {
        var areas = new Dictionary<string, int>();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var total = 0;

        // ترتیب: اول فرزندان (بر اساس FK) بعد والدان — در هر گروه.
        total += await Area(areas, "ارزیابی عملکرد",
            () => db.HrAppraisalScores.ExecuteDeleteAsync(ct), () => db.HrAppraisalKpis.ExecuteDeleteAsync(ct), () => db.HrAppraisals.ExecuteDeleteAsync(ct));

        total += await Area(areas, "درخواست ویرایش پرونده",
            () => db.HrProfileEditRequests.ExecuteDeleteAsync(ct));

        // پرونده پرسنل: پیوست‌ها و جزئیات، سپس خود پرسنل
        total += await Area(areas, "پرونده پرسنل",
            () => db.HrEmployeeDependents.ExecuteDeleteAsync(ct), () => db.HrEmployeeCourses.ExecuteDeleteAsync(ct), () => db.HrEmployeeSkills.ExecuteDeleteAsync(ct), () => db.HrEmployeeLanguages.ExecuteDeleteAsync(ct), () => db.HrEmployeeDocuments.ExecuteDeleteAsync(ct), () => db.HrContractVersions.ExecuteDeleteAsync(ct), () => db.HrContractExpiryAlerts.ExecuteDeleteAsync(ct), () => db.HrContracts.ExecuteDeleteAsync(ct), () => db.HrDecrees.ExecuteDeleteAsync(ct), () => db.HrJobHistories.ExecuteDeleteAsync(ct), () => db.HrTrialPeriods.ExecuteDeleteAsync(ct), () => db.HrInterviews.ExecuteDeleteAsync(ct), () => db.HrApplicants.ExecuteDeleteAsync(ct), () => db.HrJobPostings.ExecuteDeleteAsync(ct), () => db.HrOnboardingItems.ExecuteDeleteAsync(ct), () => db.HrOnboardings.ExecuteDeleteAsync(ct), () => db.HrExitItems.ExecuteDeleteAsync(ct), () => db.HrExitCases.ExecuteDeleteAsync(ct), () => db.HrEmployees.ExecuteDeleteAsync(ct));

        // ساختار سازمانی جدید + ساختار پیشین (هر دو توسط داده نمونه ساخته شده بودند)
        total += await Area(areas, "ساختار سازمانی",
            () => db.HrMainChangeLogs.ExecuteDeleteAsync(ct), () => db.HrMainPositions.ExecuteDeleteAsync(ct), () => db.HrMainOrgNodes.ExecuteDeleteAsync(ct), () => db.HrMainBranches.ExecuteDeleteAsync(ct), () => db.HrMainCompanies.ExecuteDeleteAsync(ct), () => db.HrOrgUnits.ExecuteDeleteAsync(ct));

        // حضور و مرخصی فروغ آریا (نمونه‌ها و داده‌های ثبت‌شده؛ انواع مرخصی/شیفت/دستگاه می‌ماند)
        total += await Area(areas, "حضور و مرخصی",
            () => db.FaAttLeaveBalances.ExecuteDeleteAsync(ct), () => db.FaAttLeaves.ExecuteDeleteAsync(ct), () => db.FaAttMissions.ExecuteDeleteAsync(ct), () => db.FaAttDailies.ExecuteDeleteAsync(ct), () => db.FaAttLogs.ExecuteDeleteAsync(ct), () => db.FaAttShiftAssigns.ExecuteDeleteAsync(ct));

        // حقوق و دستمزد (دوره‌ها و اسناد؛ تنظیمات/جدول مالیات/اقلام می‌ماند)
        total += await Area(areas, "حقوق و دستمزد",
            () => db.FaPaySlipItems.ExecuteDeleteAsync(ct), () => db.FaPaySlips.ExecuteDeleteAsync(ct), () => db.FaPayRuns.ExecuteDeleteAsync(ct), () => db.FaPayLoanInstallments.ExecuteDeleteAsync(ct), () => db.FaPayLoans.ExecuteDeleteAsync(ct), () => db.FaPayArrears.ExecuteDeleteAsync(ct), () => db.FaPaySettlements.ExecuteDeleteAsync(ct), () => db.FaPayAdjustments.ExecuteDeleteAsync(ct));

        // آموزش (دوره‌ها تا بانک سؤال و مدرس‌ها)
        total += await Area(areas, "آموزش",
            () => db.FaLmsTextAnswers.ExecuteDeleteAsync(ct), () => db.FaLmsSurveyAnswers.ExecuteDeleteAsync(ct), () => db.FaLmsAttempts.ExecuteDeleteAsync(ct), () => db.FaLmsCertificates.ExecuteDeleteAsync(ct), () => db.FaLmsAttendances.ExecuteDeleteAsync(ct), () => db.FaLmsSessions.ExecuteDeleteAsync(ct), () => db.FaLmsEnrollments.ExecuteDeleteAsync(ct), () => db.FaLmsExams.ExecuteDeleteAsync(ct), () => db.FaLmsQuestions.ExecuteDeleteAsync(ct), () => db.FaLmsBankQuestions.ExecuteDeleteAsync(ct), () => db.FaLmsBanks.ExecuteDeleteAsync(ct), () => db.FaLmsSurveyQuestions.ExecuteDeleteAsync(ct), () => db.FaLmsBudgets.ExecuteDeleteAsync(ct), () => db.FaLmsNeeds.ExecuteDeleteAsync(ct), () => db.FaLmsCourses.ExecuteDeleteAsync(ct), () => db.FaLmsInstructors.ExecuteDeleteAsync(ct));

        // ارتباطات و اطلاع‌رسانی
        total += await Area(areas, "ارتباطات",
            () => db.FaComVotes.ExecuteDeleteAsync(ct), () => db.FaComPollOptions.ExecuteDeleteAsync(ct), () => db.FaComPolls.ExecuteDeleteAsync(ct), () => db.FaComReplies.ExecuteDeleteAsync(ct), () => db.FaComTickets.ExecuteDeleteAsync(ct), () => db.FaComSuggestions.ExecuteDeleteAsync(ct), () => db.FaComAnnouncements.ExecuteDeleteAsync(ct));

        // سامانه‌های پیشین HR (داده تجاری؛ اقلام/حداقل دستمزد/قواعد زمانی می‌ماند)
        total += await Area(areas, "سامانه‌های پیشین",
            () => db.HrOtApprovals.ExecuteDeleteAsync(ct), () => db.HrAttendCloses.ExecuteDeleteAsync(ct), () => db.HrOvertimeRequests.ExecuteDeleteAsync(ct), () => db.HrLeaveBalances.ExecuteDeleteAsync(ct), () => db.HrLeaveExtras.ExecuteDeleteAsync(ct), () => db.HrDayExtras.ExecuteDeleteAsync(ct), () => db.HrNursingBreaks.ExecuteDeleteAsync(ct), () => db.HrShiftRosters.ExecuteDeleteAsync(ct), () => db.HrDevicePunches.ExecuteDeleteAsync(ct), () => db.HrDeviceUserMaps.ExecuteDeleteAsync(ct), () => db.HrUserLinks.ExecuteDeleteAsync(ct), () => db.HrRequestSteps.ExecuteDeleteAsync(ct), () => db.HrProbations.ExecuteDeleteAsync(ct), () => db.HrPayFilings.ExecuteDeleteAsync(ct), () => db.HrPaySettlements.ExecuteDeleteAsync(ct), () => db.HrPaySlips.ExecuteDeleteAsync(ct), () => db.HrPayRuns.ExecuteDeleteAsync(ct), () => db.HrPayOnAccounts.ExecuteDeleteAsync(ct), () => db.HrPayArrears.ExecuteDeleteAsync(ct), () => db.HrPayLoans.ExecuteDeleteAsync(ct), () => db.HrPayEmployeeItems.ExecuteDeleteAsync(ct), () => db.HrPayProfiles.ExecuteDeleteAsync(ct), () => db.HrPerfScores.ExecuteDeleteAsync(ct), () => db.HrPerfResults.ExecuteDeleteAsync(ct), () => db.HrPerfKpis.ExecuteDeleteAsync(ct), () => db.HrPerfPeriods.ExecuteDeleteAsync(ct), () => db.AttendanceAlerts.ExecuteDeleteAsync(ct), () => db.LeaveRequests.ExecuteDeleteAsync(ct), () => db.AttendanceSegments.ExecuteDeleteAsync(ct), () => db.AttendanceRecords.ExecuteDeleteAsync(ct), () => db.FaHrPersonnel.ExecuteDeleteAsync(ct));

        // تاریخچه‌ی عملیات همین ماژول‌ها
        var audit = await db.AuditLogs.Where(a => HrAuditModules.Contains(a.Module)).ExecuteDeleteAsync(ct);
        areas["تاریخچه عملیات"] = audit;
        total += audit;

        await tx.CommitAsync(ct);
        return new PurgeResult(total, areas);
    }

    /// <summary>حذف گروهی چند DbSet با ExecuteDelete و جمع نتیجه در یک ناحیه.</summary>
    private static async Task<int> Area(Dictionary<string, int> areas, string name, params Func<Task<int>>[] deletes)
    {
        var n = 0;
        foreach (var d in deletes) n += await d();
        areas[name] = n;
        return n;
    }
}
