using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventory.Api.Migrations
{
    /// <summary>
    /// مازادِ کارکرد: ماندنِ بعد از پایان شیفت «در انتظار تصمیم مدیر» می‌ماند
    /// (نه کسری، نه اضافه‌کاری خودکار). تصمیم مدیر روی خودِ بازه ذخیره می‌شود:
    /// تأیید → اضافه‌کاری، رد → تردد غیرمجاز.
    /// دستورها idempotent هستند تا روی دیتابیس‌های قدیمی هم امن باشند.
    /// </summary>
    public partial class ExtraWorkDecision : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ---------- AttendanceRecords ----------
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'dbo.AttendanceRecords', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.AttendanceRecords', N'PendingOvertimeMinutes') IS NULL
    ALTER TABLE dbo.AttendanceRecords ADD PendingOvertimeMinutes int NOT NULL DEFAULT 0;");

            // ---------- AttendanceSegments ----------
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'dbo.AttendanceSegments', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.AttendanceSegments', N'PendingMinutes') IS NULL
    ALTER TABLE dbo.AttendanceSegments ADD PendingMinutes int NOT NULL DEFAULT 0;");
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'dbo.AttendanceSegments', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.AttendanceSegments', N'ExtraDecision') IS NULL
    ALTER TABLE dbo.AttendanceSegments ADD ExtraDecision int NOT NULL DEFAULT 0;");
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'dbo.AttendanceSegments', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.AttendanceSegments', N'ExtraDecidedByUserId') IS NULL
    ALTER TABLE dbo.AttendanceSegments ADD ExtraDecidedByUserId int NULL;");
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'dbo.AttendanceSegments', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.AttendanceSegments', N'ExtraDecidedAt') IS NULL
    ALTER TABLE dbo.AttendanceSegments ADD ExtraDecidedAt datetime2 NULL;");
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'dbo.AttendanceSegments', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.AttendanceSegments', N'ExtraDecisionNote') IS NULL
    ALTER TABLE dbo.AttendanceSegments ADD ExtraDecisionNote nvarchar(300) NULL;");

            // ---------- ShiftGroups ----------
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'dbo.ShiftGroups', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.ShiftGroups', N'RequireExtraApproval') IS NULL
    ALTER TABLE dbo.ShiftGroups ADD RequireExtraApproval bit NULL;");

            // ---------- WorkCalendarSettings ----------
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'dbo.WorkCalendarSettings', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.WorkCalendarSettings', N'RequireExtraApproval') IS NULL
    ALTER TABLE dbo.WorkCalendarSettings ADD RequireExtraApproval bit NOT NULL DEFAULT 1;");
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'dbo.WorkCalendarSettings', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.WorkCalendarSettings', N'ShiftOverridesCalendarTimes') IS NULL
    ALTER TABLE dbo.WorkCalendarSettings ADD ShiftOverridesCalendarTimes bit NOT NULL DEFAULT 1;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // حذف ستون‌ها روی دیتابیسِ در حال استفاده خطرناک است (داده‌ی تصمیم مدیر از بین می‌رود)
            // و با «ساختار فعلی» برنامه ناسازگار؛ به همین دلیل Down خالی است (Irreversible).
        }
    }
}
