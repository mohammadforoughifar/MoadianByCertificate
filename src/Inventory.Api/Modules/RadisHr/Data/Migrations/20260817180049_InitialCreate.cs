using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RadisHr.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AccountingAdjustments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Month = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    EmployeeCode = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Tax = table.Column<decimal>(type: "decimal(20,2)", nullable: true),
                    Insurance = table.Column<decimal>(type: "decimal(20,2)", nullable: true),
                    Overtime = table.Column<decimal>(type: "decimal(20,2)", nullable: true),
                    Shortfall = table.Column<decimal>(type: "decimal(20,2)", nullable: true),
                    Performance = table.Column<decimal>(type: "decimal(20,2)", nullable: true),
                    Productivity = table.Column<decimal>(type: "decimal(20,2)", nullable: true),
                    OtherPayments = table.Column<decimal>(type: "decimal(20,2)", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FinalNet = table.Column<decimal>(type: "decimal(20,2)", nullable: false),
                    AdjustedBy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AdjustedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountingAdjustments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AccountingArchives",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Month = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TotalNet = table.Column<decimal>(type: "decimal(20,2)", nullable: false),
                    EmployeeCount = table.Column<int>(type: "int", nullable: false),
                    ApprovedBy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ApprovedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SettledBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SettledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Locked = table.Column<bool>(type: "bit", nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountingArchives", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Advances",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Uid = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EmployeeCode = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Date = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(20,2)", nullable: false),
                    Deducted = table.Column<decimal>(type: "decimal(20,2)", nullable: false),
                    SettlementMethod = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SettlementStartMonth = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SettlementCount = table.Column<int>(type: "int", nullable: false),
                    SettlementAmount = table.Column<decimal>(type: "decimal(20,2)", nullable: false),
                    SettlementStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Advances", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Announcements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Uid = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Body = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DurationDays = table.Column<int>(type: "int", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Announcements", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AttendanceDays",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Key = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DeviceCode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Date = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Month = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    First = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Last = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Presence = table.Column<int>(type: "int", nullable: false),
                    Leave = table.Column<int>(type: "int", nullable: false),
                    Mission = table.Column<int>(type: "int", nullable: false),
                    Shortfall = table.Column<int>(type: "int", nullable: false),
                    Ot = table.Column<int>(type: "int", nullable: false),
                    UnauthorizedOt = table.Column<int>(type: "int", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ManualCorrectionApplied = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceDays", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CeoNotifications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Uid = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Channel = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Message = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Actor = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Details = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Seen = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CeoNotifications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Departments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Uid = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Ordinal = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Departments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Employees",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    First = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Last = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Nid = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Married = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Children = table.Column<int>(type: "int", nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    WorkStation = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ResponsibilityLevel = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PositionTitle = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Hire = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ContractType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ContractEnd = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PrimaryInsuredCode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SupplementaryFamilyCount = table.Column<int>(type: "int", nullable: false),
                    SupplementaryEmployeeShare = table.Column<decimal>(type: "decimal(20,2)", nullable: false),
                    SupplementaryEmployerShare = table.Column<decimal>(type: "decimal(20,2)", nullable: false),
                    Salary = table.Column<decimal>(type: "decimal(20,2)", nullable: false),
                    SeniorityPay = table.Column<decimal>(type: "decimal(20,2)", nullable: false),
                    AttractionPay = table.Column<decimal>(type: "decimal(20,2)", nullable: false),
                    SupervisorPay = table.Column<decimal>(type: "decimal(20,2)", nullable: false),
                    PerformancePay = table.Column<decimal>(type: "decimal(20,2)", nullable: false),
                    AgreedBenefits = table.Column<decimal>(type: "decimal(20,2)", nullable: false),
                    BankName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AccountNumber = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Iban = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CardNumber = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    BankBranch = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    BranchCode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Photo = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Employees", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Extinguishers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Uid = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AssetCode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Location = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ExpiryDate = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Extinguishers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GuardCycles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EmployeeCode = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    StartDate = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    StartShift = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RegisteredBy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GuardCycles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Holidays",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Date = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Holidays", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HseDefinitions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Group = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Ordinal = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HseDefinitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HseNotifications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Uid = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PpeId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Details = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Message = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Seen = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HseNotifications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ImportAudits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FileName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    At = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Rows = table.Column<int>(type: "int", nullable: false),
                    Inserted = table.Column<int>(type: "int", nullable: false),
                    Ignored = table.Column<int>(type: "int", nullable: false),
                    Policy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    From = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    To = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ImportedBy = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportAudits", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Incidents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Uid = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CaseNo = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Date = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Time = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Shift = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Severity = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EmployeeCode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Location = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LostDays = table.Column<int>(type: "int", nullable: false),
                    MedicalNotes = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LeaveRequired = table.Column<bool>(type: "bit", nullable: false),
                    LeaveType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RootCause = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Incidents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LeaveMissions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Employee = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Date = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    From = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    To = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RegisteredBy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaveMissions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ManualPunches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Employee = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Date = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Time = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RequestedBy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ReviewedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReviewNote = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReviewedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManualPunches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PayrollPayments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Month = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    EmployeeCode = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(20,2)", nullable: false),
                    PaidDate = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PaidBy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollPayments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PayrollRows",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Month = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DeviceCode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Presence = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Leave = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Mission = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Shortfall = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Ot = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UnauthorizedOt = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Base = table.Column<decimal>(type: "decimal(20,2)", nullable: false),
                    Seniority = table.Column<decimal>(type: "decimal(20,2)", nullable: false),
                    Housing = table.Column<decimal>(type: "decimal(20,2)", nullable: false),
                    Food = table.Column<decimal>(type: "decimal(20,2)", nullable: false),
                    Marriage = table.Column<decimal>(type: "decimal(20,2)", nullable: false),
                    Children = table.Column<decimal>(type: "decimal(20,2)", nullable: false),
                    Attraction = table.Column<decimal>(type: "decimal(20,2)", nullable: false),
                    Supervisor = table.Column<decimal>(type: "decimal(20,2)", nullable: false),
                    Performance = table.Column<decimal>(type: "decimal(20,2)", nullable: false),
                    Agreed = table.Column<decimal>(type: "decimal(20,2)", nullable: false),
                    ShiftPay = table.Column<decimal>(type: "decimal(20,2)", nullable: false),
                    NightPay = table.Column<decimal>(type: "decimal(20,2)", nullable: false),
                    FridayPay = table.Column<decimal>(type: "decimal(20,2)", nullable: false),
                    MissionPay = table.Column<decimal>(type: "decimal(20,2)", nullable: false),
                    OtPay = table.Column<decimal>(type: "decimal(20,2)", nullable: false),
                    ShortfallPay = table.Column<decimal>(type: "decimal(20,2)", nullable: false),
                    Gross = table.Column<decimal>(type: "decimal(20,2)", nullable: false),
                    InsuranceBase = table.Column<decimal>(type: "decimal(20,2)", nullable: false),
                    Insurance = table.Column<decimal>(type: "decimal(20,2)", nullable: false),
                    EmployerInsurance = table.Column<decimal>(type: "decimal(20,2)", nullable: false),
                    TaxableIncome = table.Column<decimal>(type: "decimal(20,2)", nullable: false),
                    Tax = table.Column<decimal>(type: "decimal(20,2)", nullable: false),
                    Net = table.Column<decimal>(type: "decimal(20,2)", nullable: false),
                    CalculatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollRows", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PpeAuthorizations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StationKey = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    EquipmentType = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PpeAuthorizations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PpeDeliveries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Uid = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EmployeeCode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EquipmentType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    BrandModel = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Size = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Serial = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DeliveryDate = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ReplacementIntervalDays = table.Column<int>(type: "int", nullable: false),
                    Condition = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EarlyReplacement = table.Column<bool>(type: "bit", nullable: false),
                    EarlyReplacementReason = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    HseApproval = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ProductionApproval = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    HseApprovedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ProductionApprovedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PpeDeliveries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RuleAudits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Field = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FieldLabel = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OldValue = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    NewValue = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ChangedBy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ChangedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RuleAudits", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StatutoryRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Year = table.Column<int>(type: "int", nullable: false),
                    MinimumDailyWage = table.Column<decimal>(type: "decimal(20,2)", nullable: false),
                    SeniorityDaily = table.Column<decimal>(type: "decimal(20,2)", nullable: false),
                    HousingMonthly = table.Column<decimal>(type: "decimal(20,2)", nullable: false),
                    FoodMonthly = table.Column<decimal>(type: "decimal(20,2)", nullable: false),
                    MarriageMonthly = table.Column<decimal>(type: "decimal(20,2)", nullable: false),
                    ChildMultiplier = table.Column<decimal>(type: "decimal(20,2)", nullable: false),
                    WorkerInsuranceRate = table.Column<decimal>(type: "decimal(20,2)", nullable: false),
                    EmployerInsuranceRate = table.Column<decimal>(type: "decimal(20,2)", nullable: false),
                    MaximumInsurableDailyMultiplier = table.Column<decimal>(type: "decimal(20,2)", nullable: false),
                    OvertimePremiumRate = table.Column<decimal>(type: "decimal(20,2)", nullable: false),
                    NightWorkPremiumRate = table.Column<decimal>(type: "decimal(20,2)", nullable: false),
                    FridayWorkPremiumRate = table.Column<decimal>(type: "decimal(20,2)", nullable: false),
                    ShiftMorningEveningRate = table.Column<decimal>(type: "decimal(20,2)", nullable: false),
                    ShiftThreeShiftRate = table.Column<decimal>(type: "decimal(20,2)", nullable: false),
                    ShiftNightRotationRate = table.Column<decimal>(type: "decimal(20,2)", nullable: false),
                    MissionMinimumDailyMultiplier = table.Column<decimal>(type: "decimal(20,2)", nullable: false),
                    TaxMonthlyExemption = table.Column<decimal>(type: "decimal(20,2)", nullable: false),
                    SourceReference = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SourceNote = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SourceUrls = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EffectiveFrom = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Insurable = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StatutoryRules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StoredFiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Uid = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    Content = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    UploadedBy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    StoredAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoredFiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UnitSchedules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Unit = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    WorkStart = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    WorkEnd = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EntryGrace = table.Column<int>(type: "int", nullable: false),
                    EarlyEntryGrace = table.Column<int>(type: "int", nullable: false),
                    ExitGrace = table.Column<int>(type: "int", nullable: false),
                    LateExitGrace = table.Column<int>(type: "int", nullable: false),
                    MaxLateWithoutLeave = table.Column<int>(type: "int", nullable: false),
                    MaxEarlyWithoutLeave = table.Column<int>(type: "int", nullable: false),
                    AbsenceStrategy = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UnitSchedules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RadisHrUsers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserKey = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RoleTitle = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PasswordSalt = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MustChangePassword = table.Column<bool>(type: "bit", nullable: false),
                    PasswordChangedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastLoginAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FailedAttempts = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RadisHrUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AdvanceInstallments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AdvanceId = table.Column<int>(type: "int", nullable: false),
                    Month = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(20,2)", nullable: false),
                    Applied = table.Column<bool>(type: "bit", nullable: false),
                    AppliedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdvanceInstallments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AdvanceInstallments_Advances_AdvanceId",
                        column: x => x.AdvanceId,
                        principalTable: "Advances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AnnouncementAttachments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AnnouncementId = table.Column<int>(type: "int", nullable: false),
                    Uid = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    StoredFileId = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnnouncementAttachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AnnouncementAttachments_Announcements_AnnouncementId",
                        column: x => x.AnnouncementId,
                        principalTable: "Announcements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AnnouncementRecipients",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AnnouncementId = table.Column<int>(type: "int", nullable: false),
                    RoleKey = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FirstSeenAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnnouncementRecipients", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AnnouncementRecipients_Announcements_AnnouncementId",
                        column: x => x.AnnouncementId,
                        principalTable: "Announcements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkStations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DepartmentId = table.Column<int>(type: "int", nullable: false),
                    Uid = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkStations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkStations_Departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "Departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EmployeeContracts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EmployeeId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Month = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FileId = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeContracts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmployeeContracts_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CorrectiveActions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IncidentId = table.Column<int>(type: "int", nullable: false),
                    Uid = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Owner = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DueDate = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CorrectiveActions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CorrectiveActions_Incidents_IncidentId",
                        column: x => x.IncidentId,
                        principalTable: "Incidents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MedicalDocuments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IncidentId = table.Column<int>(type: "int", nullable: false),
                    Uid = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    ArchivedBy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ArchivedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Immutable = table.Column<bool>(type: "bit", nullable: false),
                    StoredFileId = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MedicalDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MedicalDocuments_Incidents_IncidentId",
                        column: x => x.IncidentId,
                        principalTable: "Incidents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StatutoryTaxBrackets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StatutoryRulesId = table.Column<int>(type: "int", nullable: false),
                    Ordinal = table.Column<int>(type: "int", nullable: false),
                    Ceiling = table.Column<decimal>(type: "decimal(20,2)", nullable: true),
                    Rate = table.Column<decimal>(type: "decimal(20,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StatutoryTaxBrackets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StatutoryTaxBrackets_StatutoryRules_StatutoryRulesId",
                        column: x => x.StatutoryRulesId,
                        principalTable: "StatutoryRules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccountingAdjustments_EmployeeCode_Month",
                table: "AccountingAdjustments",
                columns: new[] { "EmployeeCode", "Month" });

            migrationBuilder.CreateIndex(
                name: "IX_AccountingArchives_Month",
                table: "AccountingArchives",
                column: "Month",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AdvanceInstallments_AdvanceId",
                table: "AdvanceInstallments",
                column: "AdvanceId");

            migrationBuilder.CreateIndex(
                name: "IX_Advances_EmployeeCode",
                table: "Advances",
                column: "EmployeeCode");

            migrationBuilder.CreateIndex(
                name: "IX_AnnouncementAttachments_AnnouncementId",
                table: "AnnouncementAttachments",
                column: "AnnouncementId");

            migrationBuilder.CreateIndex(
                name: "IX_AnnouncementRecipients_AnnouncementId",
                table: "AnnouncementRecipients",
                column: "AnnouncementId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceDays_Code_Month",
                table: "AttendanceDays",
                columns: new[] { "Code", "Month" });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceDays_Date",
                table: "AttendanceDays",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceDays_Key",
                table: "AttendanceDays",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CorrectiveActions_IncidentId",
                table: "CorrectiveActions",
                column: "IncidentId");

            migrationBuilder.CreateIndex(
                name: "IX_Departments_Name",
                table: "Departments",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeContracts_EmployeeId",
                table: "EmployeeContracts",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_Employees_Code",
                table: "Employees",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Employees_Nid",
                table: "Employees",
                column: "Nid");

            migrationBuilder.CreateIndex(
                name: "IX_Employees_Unit_WorkStation",
                table: "Employees",
                columns: new[] { "Unit", "WorkStation" });

            migrationBuilder.CreateIndex(
                name: "IX_GuardCycles_EmployeeCode",
                table: "GuardCycles",
                column: "EmployeeCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Holidays_Date",
                table: "Holidays",
                column: "Date",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HseDefinitions_Group_Title",
                table: "HseDefinitions",
                columns: new[] { "Group", "Title" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Incidents_CaseNo",
                table: "Incidents",
                column: "CaseNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeaveMissions_Employee_Date",
                table: "LeaveMissions",
                columns: new[] { "Employee", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_ManualPunches_Employee_Date",
                table: "ManualPunches",
                columns: new[] { "Employee", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_MedicalDocuments_IncidentId",
                table: "MedicalDocuments",
                column: "IncidentId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollPayments_EmployeeCode_Month",
                table: "PayrollPayments",
                columns: new[] { "EmployeeCode", "Month" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRows_Code_Month",
                table: "PayrollRows",
                columns: new[] { "Code", "Month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PpeAuthorizations_StationKey_EquipmentType",
                table: "PpeAuthorizations",
                columns: new[] { "StationKey", "EquipmentType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StatutoryRules_Year",
                table: "StatutoryRules",
                column: "Year",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StatutoryTaxBrackets_StatutoryRulesId",
                table: "StatutoryTaxBrackets",
                column: "StatutoryRulesId");

            migrationBuilder.CreateIndex(
                name: "IX_StoredFiles_Uid",
                table: "StoredFiles",
                column: "Uid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UnitSchedules_Unit",
                table: "UnitSchedules",
                column: "Unit",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RadisHrUsers_UserKey",
                table: "RadisHrUsers",
                column: "UserKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkStations_DepartmentId",
                table: "WorkStations",
                column: "DepartmentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccountingAdjustments");

            migrationBuilder.DropTable(
                name: "AccountingArchives");

            migrationBuilder.DropTable(
                name: "AdvanceInstallments");

            migrationBuilder.DropTable(
                name: "AnnouncementAttachments");

            migrationBuilder.DropTable(
                name: "AnnouncementRecipients");

            migrationBuilder.DropTable(
                name: "AttendanceDays");

            migrationBuilder.DropTable(
                name: "CeoNotifications");

            migrationBuilder.DropTable(
                name: "CorrectiveActions");

            migrationBuilder.DropTable(
                name: "EmployeeContracts");

            migrationBuilder.DropTable(
                name: "Extinguishers");

            migrationBuilder.DropTable(
                name: "GuardCycles");

            migrationBuilder.DropTable(
                name: "Holidays");

            migrationBuilder.DropTable(
                name: "HseDefinitions");

            migrationBuilder.DropTable(
                name: "HseNotifications");

            migrationBuilder.DropTable(
                name: "ImportAudits");

            migrationBuilder.DropTable(
                name: "LeaveMissions");

            migrationBuilder.DropTable(
                name: "ManualPunches");

            migrationBuilder.DropTable(
                name: "MedicalDocuments");

            migrationBuilder.DropTable(
                name: "PayrollPayments");

            migrationBuilder.DropTable(
                name: "PayrollRows");

            migrationBuilder.DropTable(
                name: "PpeAuthorizations");

            migrationBuilder.DropTable(
                name: "PpeDeliveries");

            migrationBuilder.DropTable(
                name: "RuleAudits");

            migrationBuilder.DropTable(
                name: "StatutoryTaxBrackets");

            migrationBuilder.DropTable(
                name: "StoredFiles");

            migrationBuilder.DropTable(
                name: "UnitSchedules");

            migrationBuilder.DropTable(
                name: "RadisHrUsers");

            migrationBuilder.DropTable(
                name: "WorkStations");

            migrationBuilder.DropTable(
                name: "Advances");

            migrationBuilder.DropTable(
                name: "Announcements");

            migrationBuilder.DropTable(
                name: "Employees");

            migrationBuilder.DropTable(
                name: "Incidents");

            migrationBuilder.DropTable(
                name: "StatutoryRules");

            migrationBuilder.DropTable(
                name: "Departments");
        }
    }
}
