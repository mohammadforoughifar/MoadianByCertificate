using System;
using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventory.Api.Migrations
{
    /// <inheritdoc />
    public partial class SquashedInitial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // اسکواش 8 مایگریشن در یک مایگریشن واحد — ترتیب اجرای اصلی حفظ شده است.
            // هر مایگریشن داخل یک local function است تا return; و دامنهٔ متغیرهایش
            // دقیقاً مثل قبل رفتار کند (مثلاً مسیر Sqlite ماژول چت).
            // شناسهٔ قدیمی‌ترین مایگریشن نگه داشته شد تا دیتابیس‌های موجود چیزی pending نداشته باشند.
            // ===== 20260911100108_SquashedInitial =====
            void M_20260911100108_SquashedInitial()
            {
            // اسکواش ۷۱ مایگریشن قبلی — ترتیب اجرای اصلی حفظ شده است.
// ===== 20260814161634_InitialCreate.cs =====
            migrationBuilder.CreateTable(
                name: "Parties",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Mobile = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Address = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Parties", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Products",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Barcode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SalePrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    PurchasePrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ReorderPoint = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    MaxStock = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Products", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Stocks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WarehouseId = table.Column<int>(type: "int", nullable: false),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    AvgCost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Stocks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Transactions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    WarehouseId = table.Column<int>(type: "int", nullable: false),
                    PartyId = table.Column<int>(type: "int", nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Transactions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Warehouses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Address = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Warehouses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TransactionLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TransactionId = table.Column<int>(type: "int", nullable: false),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    Price = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransactionLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TransactionLines_Transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "Transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Products_Code",
                table: "Products",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Stocks_WarehouseId_ProductId",
                table: "Stocks",
                columns: new[] { "WarehouseId", "ProductId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TransactionLines_ProductId",
                table: "TransactionLines",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_TransactionLines_TransactionId",
                table: "TransactionLines",
                column: "TransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_Date",
                table: "Transactions",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_Type",
                table: "Transactions",
                column: "Type");
        

// ===== 20260815051102_AddProductCategories.cs =====
            migrationBuilder.CreateTable(
                name: "ProductCategories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductCategories", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductCategories_Name",
                table: "ProductCategories",
                column: "Name",
                unique: true);
        

// ===== 20260815052346_AddUnitsAndCategoryTree.cs =====
            migrationBuilder.AddColumn<int>(
                name: "ParentId",
                table: "ProductCategories",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MeasureUnits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MeasureUnits", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MeasureUnits_Name",
                table: "MeasureUnits",
                column: "Name",
                unique: true);
        

// ===== 20260815062907_AddSettingsReferrersServices.cs =====
            migrationBuilder.AddColumn<int>(
                name: "ReferrerId",
                table: "Transactions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsService",
                table: "Products",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "AppSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CostingMethod = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    AllowNegativeStock = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Referrers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    GoodsCommissionPercent = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    ServiceCommissionPercent = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Referrers", x => x.Id);
                });
        

// ===== 20260815065428_AddReferrerCompanyName.cs =====
            migrationBuilder.AddColumn<string>(
                name: "CompanyName",
                table: "Referrers",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);
        

// ===== 20260815072211_AddWalletWarehouseCustomerReferrer.cs =====
            migrationBuilder.AddColumn<int>(
                name: "WarehouseId",
                table: "Products",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReferrerId",
                table: "Parties",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ReferrerPayments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReferrerId = table.Column<int>(type: "int", nullable: false),
                    Number = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReferrerPayments", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReferrerPayments_ReferrerId",
                table: "ReferrerPayments",
                column: "ReferrerId");
        

// ===== 20260815083133_AddUsers.cs =====
            // هرگز PK_Users را دوباره نساز — حتی اگر جدول Users با اسکیمای دیگر موجود باشد.
            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM sys.tables WHERE [name] = N'Users')
   OR EXISTS (SELECT 1 FROM sys.key_constraints WHERE [name] = N'PK_Users')
   OR EXISTS (SELECT 1 FROM sys.objects WHERE [name] = N'PK_Users')
BEGIN
    IF OBJECT_ID(N'dbo.Users', N'U') IS NOT NULL
       AND NOT EXISTS (
            SELECT 1 FROM sys.indexes
            WHERE [name] = N'IX_Users_Username' AND [object_id] = OBJECT_ID(N'dbo.Users')
       )
    BEGIN
        CREATE UNIQUE INDEX [IX_Users_Username] ON [dbo].[Users] ([Username]);
    END
END
ELSE
BEGIN
    CREATE TABLE [dbo].[Users] (
        [Id] int NOT NULL IDENTITY,
        [Username] nvarchar(100) NOT NULL,
        [PasswordHash] nvarchar(200) NOT NULL,
        [Role] nvarchar(20) NOT NULL,
        [ReferrerId] int NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_Users] PRIMARY KEY ([Id])
    );
    CREATE UNIQUE INDEX [IX_Users_Username] ON [dbo].[Users] ([Username]);
END
");
        

// ===== 20260815091428_AddReferrerBankInfo.cs =====
            migrationBuilder.AddColumn<string>(
                name: "CardNumber",
                table: "Referrers",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Iban",
                table: "Referrers",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);
        

// ===== 20260816033434_AddRepairs.cs =====
            migrationBuilder.CreateTable(
                name: "RepairOrders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    PartyId = table.Column<int>(type: "int", nullable: false),
                    TechnicianId = table.Column<int>(type: "int", nullable: true),
                    DeviceType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DeviceModel = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SerialNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ProblemDescription = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Accessories = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DeliveredAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    QuotedPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    InvoiceTransactionId = table.Column<int>(type: "int", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RepairOrders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Technicians",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Specialty = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Technicians", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RepairItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RepairOrderId = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    ProductId = table.Column<int>(type: "int", nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    Cost = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Price = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RepairItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RepairItems_RepairOrders_RepairOrderId",
                        column: x => x.RepairOrderId,
                        principalTable: "RepairOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RepairItems_RepairOrderId",
                table: "RepairItems",
                column: "RepairOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_RepairOrders_PartyId",
                table: "RepairOrders",
                column: "PartyId");

            migrationBuilder.CreateIndex(
                name: "IX_RepairOrders_Status",
                table: "RepairOrders",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_RepairOrders_TechnicianId",
                table: "RepairOrders",
                column: "TechnicianId");
        

// ===== 20260816042258_AddPaymentsAndRoles.cs =====
            migrationBuilder.AddColumn<int>(
                name: "CashType",
                table: "Transactions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DueDate",
                table: "Transactions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PaymentMethod",
                table: "Transactions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "SettledAmount",
                table: "Transactions",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "Cheques",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TransactionId = table.Column<int>(type: "int", nullable: false),
                    Number = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    BankName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    AccountInfo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    OwnerName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    DueDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsCleared = table.Column<bool>(type: "bit", nullable: false),
                    ClearedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cheques", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Cheques_Transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "Transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Installments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TransactionId = table.Column<int>(type: "int", nullable: false),
                    No = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    DueDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsPaid = table.Column<bool>(type: "bit", nullable: false),
                    PaidAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Installments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Installments_Transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "Transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Cheques_DueDate",
                table: "Cheques",
                column: "DueDate");

            migrationBuilder.CreateIndex(
                name: "IX_Cheques_IsCleared",
                table: "Cheques",
                column: "IsCleared");

            migrationBuilder.CreateIndex(
                name: "IX_Cheques_TransactionId",
                table: "Cheques",
                column: "TransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_Installments_DueDate",
                table: "Installments",
                column: "DueDate");

            migrationBuilder.CreateIndex(
                name: "IX_Installments_TransactionId",
                table: "Installments",
                column: "TransactionId");
        

// ===== 20260816043917_AddCashAmountMixedPayment.cs =====
            migrationBuilder.AddColumn<decimal>(
                name: "CashAmount",
                table: "Transactions",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);
        

// ===== 20260816051853_AddReferrerCanViewProducts.cs =====
            migrationBuilder.AddColumn<bool>(
                name: "CanViewProducts",
                table: "Referrers",
                type: "bit",
                nullable: false,
                defaultValue: false);
        

// ===== 20260816053917_AddExpenses.cs =====
            migrationBuilder.CreateTable(
                name: "ExpenseCategories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpenseCategories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Expenses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    CategoryId = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PayType = table.Column<int>(type: "int", nullable: false),
                    Payee = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Expenses", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseCategories_Name",
                table: "ExpenseCategories",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_CategoryId",
                table: "Expenses",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_Date",
                table: "Expenses",
                column: "Date");
        

// ===== 20260819040730_AddSystemTables.cs =====
            // حذف امن جدول‌های سیستمِ قدیمی/ناسازگار اگر از قبل وجود داشته باشند
            // (مثلاً SystemUsers قدیمی بدون ستون‌های FirstName/LastName/StaffNumber)
            migrationBuilder.Sql(@"
IF OBJECT_ID('dbo.SystemInfos', 'U')    IS NOT NULL DROP TABLE dbo.SystemInfos;
IF OBJECT_ID('dbo.SystemUsers', 'U')    IS NOT NULL DROP TABLE dbo.SystemUsers;
IF OBJECT_ID('dbo.SystemDepartments', 'U') IS NOT NULL DROP TABLE dbo.SystemDepartments;
IF OBJECT_ID('dbo.SystemCompanies', 'U') IS NOT NULL DROP TABLE dbo.SystemCompanies;
");

            migrationBuilder.CreateTable(
                name: "SystemCompanies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Address = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemCompanies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SystemDepartments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    CompanyId = table.Column<int>(type: "int", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemDepartments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SystemInfos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AgentId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Motherboard = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Cpu = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Ram = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    HardDisk = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Graphics = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Monitor = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsApproved = table.Column<bool>(type: "bit", nullable: false),
                    OsName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TotalRamGb = table.Column<int>(type: "int", nullable: false),
                    CompanyId = table.Column<int>(type: "int", nullable: true),
                    DepartmentId = table.Column<int>(type: "int", nullable: true),
                    UserId = table.Column<int>(type: "int", nullable: true),
                    ReceivedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemInfos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SystemUsers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FirstName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    LastName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    StaffNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    DepartmentId = table.Column<int>(type: "int", nullable: true),
                    CompanyId = table.Column<int>(type: "int", nullable: true),
                    Role = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemUsers", x => x.Id);
                });
        

// ===== 20260819043459_AddSystemInfoDetails.cs =====
            migrationBuilder.AddColumn<string>(
                name: "DetailsJson",
                table: "SystemInfos",
                type: "nvarchar(max)",
                nullable: true);
        

// ===== 20260819060845_AddSystemInfoChangeTracking.cs =====
            migrationBuilder.AddColumn<string>(
                name: "PendingPayloadJson",
                table: "SystemInfos",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PendingReceivedAt",
                table: "SystemInfos",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SystemInfoChangeLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SystemInfoId = table.Column<int>(type: "int", nullable: false),
                    AgentId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ChangedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ChangeCount = table.Column<int>(type: "int", nullable: false),
                    ChangesJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemInfoChangeLogs", x => x.Id);
                });
        

// ===== 20260819061901_AddCctvCameras.cs =====
            migrationBuilder.CreateTable(
                name: "CctvCameras",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Model = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    SerialNumber = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Ip = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Mac = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Location = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CctvCameras", x => x.Id);
                });
        

// ===== 20260819063240_AddCctvNvrs.cs =====
            migrationBuilder.AddColumn<int>(
                name: "NvrId",
                table: "CctvCameras",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CctvNvrs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Model = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    SerialNumber = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Ip = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Mac = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Location = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CctvNvrs", x => x.Id);
                });
        

// ===== 20260819065947_AddOfficeMachines.cs =====
            migrationBuilder.CreateTable(
                name: "OfficeMachineCosts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MachineId = table.Column<int>(type: "int", nullable: false),
                    CostDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfficeMachineCosts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OfficeMachineRepairs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MachineId = table.Column<int>(type: "int", nullable: false),
                    RepairDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Problem = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Fixed = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfficeMachineRepairs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OfficeMachines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Model = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    SerialNumber = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Location = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    InstallDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    GoneDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReturnDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfficeMachines", x => x.Id);
                });
        

// ===== 20260819070927_AddRepairDetails.cs =====
            migrationBuilder.AddColumn<decimal>(
                name: "Cost",
                table: "OfficeMachineRepairs",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTime>(
                name: "GoneDate",
                table: "OfficeMachineRepairs",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PerformedWork",
                table: "OfficeMachineRepairs",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReturnDate",
                table: "OfficeMachineRepairs",
                type: "datetime2",
                nullable: true);
        

// ===== 20260819084326_AddSystemComponentTables.cs =====
            migrationBuilder.CreateTable(
                name: "SystemCpus",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SystemInfoId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Cores = table.Column<int>(type: "int", nullable: false),
                    Threads = table.Column<int>(type: "int", nullable: false),
                    ClockGhz = table.Column<double>(type: "float", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemCpus", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SystemDisks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SystemInfoId = table.Column<int>(type: "int", nullable: false),
                    Model = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SizeGb = table.Column<int>(type: "int", nullable: false),
                    Interface = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SerialNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemDisks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SystemGpus",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SystemInfoId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Resolution = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemGpus", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SystemMonitors",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SystemInfoId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Resolution = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    SerialNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsPrimary = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemMonitors", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SystemNetAdapters",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SystemInfoId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Type = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    MacAddress = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Ipv4 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Gateway = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemNetAdapters", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SystemRams",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SystemInfoId = table.Column<int>(type: "int", nullable: false),
                    Slot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CapacityGb = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SpeedMhz = table.Column<int>(type: "int", nullable: false),
                    Manufacturer = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PartNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SerialNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemRams", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SystemVolumes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SystemInfoId = table.Column<int>(type: "int", nullable: false),
                    Letter = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Label = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TotalGb = table.Column<int>(type: "int", nullable: false),
                    UsedGb = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemVolumes", x => x.Id);
                });
        

// ===== 20260819090102_AddSystemBoard.cs =====
            migrationBuilder.CreateTable(
                name: "SystemBoards",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SystemInfoId = table.Column<int>(type: "int", nullable: false),
                    Board = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    BoardSerial = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    ComputerModel = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemBoards", x => x.Id);
                });
        

// ===== 20260822033216_AddRbacTables.cs =====
            migrationBuilder.AddColumn<string>(
                name: "Username",
                table: "SystemUsers",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "Permissions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Module = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Permissions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Roles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Roles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RolePermissions",
                columns: table => new
                {
                    RoleId = table.Column<int>(type: "int", nullable: false),
                    PermissionId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RolePermissions", x => new { x.RoleId, x.PermissionId });
                    table.ForeignKey(
                        name: "FK_RolePermissions_Permissions_PermissionId",
                        column: x => x.PermissionId,
                        principalTable: "Permissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RolePermissions_Roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserRoles",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "int", nullable: false),
                    RoleId = table.Column<int>(type: "int", nullable: false),
                    AssignedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_UserRoles_Roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Permissions_Module_Action",
                table: "Permissions",
                columns: new[] { "Module", "Action" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RolePermissions_PermissionId",
                table: "RolePermissions",
                column: "PermissionId");

            migrationBuilder.CreateIndex(
                name: "IX_Roles_Name",
                table: "Roles",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserRoles_RoleId",
                table: "UserRoles",
                column: "RoleId");
        

// ===== 20260822103039_AddItRequests.cs =====
            migrationBuilder.CreateTable(
                name: "ItRequestAssignments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RequestId = table.Column<int>(type: "int", nullable: false),
                    ExpertUserId = table.Column<int>(type: "int", nullable: false),
                    ExpertName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    ManagerInstruction = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ExpertReport = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    ReportSubmitted = table.Column<bool>(type: "bit", nullable: false),
                    IncludeInFinal = table.Column<bool>(type: "bit", nullable: false),
                    RepliedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItRequestAssignments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ItRequestAttachments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RequestId = table.Column<int>(type: "int", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Data = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    UploaderRole = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    UploaderName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    UploaderUserId = table.Column<int>(type: "int", nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItRequestAttachments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ItRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RequesterName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    RequesterUserId = table.Column<int>(type: "int", nullable: false),
                    SystemInfoId = table.Column<int>(type: "int", nullable: true),
                    SystemLabel = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ManagerNote = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    FinalResponse = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AssignedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItRequests", x => x.Id);
                });
        

// ===== 20260823043823_AddItWorkflowAndNotifications.cs =====
            migrationBuilder.AddColumn<string>(
                name: "ConnectionType",
                table: "OfficeMachines",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "IpAddress",
                table: "OfficeMachines",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LinkedSystemInfoId",
                table: "OfficeMachines",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LinkedSystemLabel",
                table: "OfficeMachines",
                type: "nvarchar(250)",
                maxLength: 250,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestType",
                table: "ItRequests",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "Done",
                table: "ItRequestAssignments",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ManagerDecision",
                table: "ItRequestAssignments",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ManagerDecisionNote",
                table: "ItRequestAssignments",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AppNotifications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    FromName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    FormName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Link = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    IsRead = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppNotifications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ItRequestLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RequestId = table.Column<int>(type: "int", nullable: false),
                    ActorName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    ActorRole = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Action = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Text = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    InternalOnly = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItRequestLogs", x => x.Id);
                });
        

// ===== 20260823052806_AddItNumberSeenReject.cs =====
            migrationBuilder.AddColumn<string>(
                name: "Number",
                table: "ItRequests",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "ItRequestSeens",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RequestId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    SeenAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItRequestSeens", x => x.Id);
                });
        

// ===== 20260823063932_AddItServerConfig.cs =====
            migrationBuilder.AddColumn<string>(
                name: "ItCompanyName",
                table: "AppSettings",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ItServerUrl",
                table: "AppSettings",
                type: "nvarchar(max)",
                nullable: true);
        

// ===== 20260823071118_AddMessengers.cs =====
            migrationBuilder.AddColumn<string>(
                name: "BaleChatId",
                table: "Users",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EitaaChatId",
                table: "Users",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Mobile",
                table: "Users",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BaleBotToken",
                table: "AppSettings",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EitaaToken",
                table: "AppSettings",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MessengerSenderNumber",
                table: "AppSettings",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);
        

// ===== 20260823085629_AddWorkOrders.cs =====
            migrationBuilder.CreateTable(
                name: "WorkOrderAllowedAssignees",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OwnerUserId = table.Column<int>(type: "int", nullable: false),
                    TargetUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkOrderAllowedAssignees", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorkOrderAssignees",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    SeenAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RepliedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Done = table.Column<bool>(type: "bit", nullable: true),
                    ReplyText = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    OwnerDecision = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    OwnerDecisionNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkOrderAssignees", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorkOrderAttachments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Data = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    UploaderName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    UploaderUserId = table.Column<int>(type: "int", nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkOrderAttachments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorkOrderLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    ActorName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Action = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Text = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkOrderLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorkOrders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: false),
                    OwnerUserId = table.Column<int>(type: "int", nullable: false),
                    OwnerName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    DueAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CloseNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ClosedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ExtensionCount = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkOrders", x => x.Id);
                });
        

// ===== 20260823094428_AddArchiveAndAttachments.cs =====
            migrationBuilder.AddColumn<string>(
                name: "FirstName",
                table: "Users",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastName",
                table: "Users",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AppAttachments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Module = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    RefId = table.Column<int>(type: "int", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Data = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    UploaderName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    UploaderUserId = table.Column<int>(type: "int", nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppAttachments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ArchiveFolders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OwnerUserId = table.Column<int>(type: "int", nullable: false),
                    ParentId = table.Column<int>(type: "int", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArchiveFolders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ArchiveItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OwnerUserId = table.Column<int>(type: "int", nullable: false),
                    FolderId = table.Column<int>(type: "int", nullable: false),
                    Module = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    RefId = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    Link = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArchiveItems", x => x.Id);
                });
        

// ===== 20260823120204_AddProjectManagement.cs =====
            migrationBuilder.CreateTable(
                name: "KarFarmas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Address = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ModirAmelPhone = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Telephone = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Fax = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ShomareSabt = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    IsDelete = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KarFarmas", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TypeFactors",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    IsDelete = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TypeFactors", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProjectEntryExits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReturnProjectId = table.Column<int>(type: "int", nullable: false),
                    SerialNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ProjectName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    GhabzExit = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    FactorNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    KarshenasiAvalie = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ProjectReceiver = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    KarFarmaId = table.Column<int>(type: "int", nullable: false),
                    FactorTypeId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    ExitDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EntryDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FileDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeliveryDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TemporaryExitDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ProjectRegistrationDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CustomerRequiredDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsFolder = table.Column<bool>(type: "bit", nullable: true),
                    IsDelete = table.Column<bool>(type: "bit", nullable: false),
                    TotalSpentTime = table.Column<TimeSpan>(type: "time", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectEntryExits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectEntryExits_KarFarmas_KarFarmaId",
                        column: x => x.KarFarmaId,
                        principalTable: "KarFarmas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProjectEntryExits_TypeFactors_FactorTypeId",
                        column: x => x.FactorTypeId,
                        principalTable: "TypeFactors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProjectEntryExits_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProjectAttaches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OriginalFileNameEncrypted = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    StoredFileName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Extension = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
                    DateSabt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    IsDelete = table.Column<bool>(type: "bit", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    ProjectId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectAttaches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectAttaches_ProjectEntryExits_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "ProjectEntryExits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProjectAttaches_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReportWorks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReportDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    WorkDescription = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    ProjectId = table.Column<int>(type: "int", nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "time", nullable: false),
                    EndTime = table.Column<TimeOnly>(type: "time", nullable: false),
                    BreakfastTime = table.Column<TimeOnly>(type: "time", nullable: false),
                    LunchTime = table.Column<TimeOnly>(type: "time", nullable: false),
                    SpentTime = table.Column<TimeSpan>(type: "time", nullable: false),
                    IsDelete = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportWorks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReportWorks_ProjectEntryExits_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "ProjectEntryExits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReportWorks_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_KarFarmas_Name",
                table: "KarFarmas",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectAttaches_ProjectId",
                table: "ProjectAttaches",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectAttaches_UserId",
                table: "ProjectAttaches",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectEntryExits_FactorTypeId",
                table: "ProjectEntryExits",
                column: "FactorTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectEntryExits_KarFarmaId",
                table: "ProjectEntryExits",
                column: "KarFarmaId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectEntryExits_SerialNumber",
                table: "ProjectEntryExits",
                column: "SerialNumber");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectEntryExits_UserId",
                table: "ProjectEntryExits",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ReportWorks_ProjectId",
                table: "ReportWorks",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_ReportWorks_UserId",
                table: "ReportWorks",
                column: "UserId");
        

// ===== 20260824050941_AddSystemIdModuleFeatures.cs =====
            migrationBuilder.AddColumn<string>(
                name: "SmartStatus",
                table: "SystemDisks",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SmartUpdatedAt",
                table: "SystemDisks",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SystemHandovers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SystemInfoId = table.Column<int>(type: "int", nullable: false),
                    FromUserName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ToUserId = table.Column<int>(type: "int", nullable: true),
                    ToUserName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsCompleted = table.Column<bool>(type: "bit", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ChecklistJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SignatureDataUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemHandovers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SystemInfoUserHistories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SystemInfoId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: true),
                    UserName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    StaffNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CompanyName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    FromAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ToAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemInfoUserHistories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SystemRemoteCommands",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SystemInfoId = table.Column<int>(type: "int", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ByUserName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Result = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemRemoteCommands", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SystemHandovers_SystemInfoId",
                table: "SystemHandovers",
                column: "SystemInfoId");

            migrationBuilder.CreateIndex(
                name: "IX_SystemInfoUserHistories_SystemInfoId",
                table: "SystemInfoUserHistories",
                column: "SystemInfoId");

            migrationBuilder.CreateIndex(
                name: "IX_SystemRemoteCommands_Status",
                table: "SystemRemoteCommands",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_SystemRemoteCommands_SystemInfoId",
                table: "SystemRemoteCommands",
                column: "SystemInfoId");
        

// ===== 20260824051902_ProjectFactorOptional.cs =====
            migrationBuilder.AlterColumn<int>(
                name: "FactorTypeId",
                table: "ProjectEntryExits",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");
        

// ===== 20260824075826_AddUserPhotosAndFileStore.cs =====
            migrationBuilder.AddColumn<string>(
                name: "FilePath",
                table: "WorkOrderAttachments",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PhotoPath",
                table: "Users",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PhotoPath",
                table: "SystemUsers",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FilePath",
                table: "ItRequestAttachments",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FilePath",
                table: "AppAttachments",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);
        

// ===== 20260824081257_AddCodeProject.cs =====
            migrationBuilder.AddColumn<string>(
                name: "CodeProject",
                table: "ReportWorks",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "CodeProject",
                table: "ProjectEntryExits",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectEntryExits_CodeProject",
                table: "ProjectEntryExits",
                column: "CodeProject");
        

// ===== 20260825045236_AddLeaveRequests.cs =====
            migrationBuilder.CreateTable(
                name: "LeaveRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RequesterUserId = table.Column<int>(type: "int", nullable: false),
                    RequesterName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    StartDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Days = table.Column<double>(type: "float", nullable: false),
                    Hours = table.Column<int>(type: "int", nullable: false),
                    Destination = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    ApprovedByName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ApproveNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaveRequests", x => x.Id);
                });
        

// ===== 20260825121203_AddProjectFlowCartableAndAttachFolders.cs =====
            migrationBuilder.AddColumn<DateTime>(
                name: "ExpertActionAt",
                table: "ProjectEntryExits",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ExpertActionById",
                table: "ProjectEntryExits",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExpertNote",
                table: "ProjectEntryExits",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FlowStatus",
                table: "ProjectEntryExits",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "ManagerActionAt",
                table: "ProjectEntryExits",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ManagerActionById",
                table: "ProjectEntryExits",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ManagerNote",
                table: "ProjectEntryExits",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            // پروژه‌های موجود از قبل وارد چرخهٔ کارتابل بوده‌اند — همه را «نهایی» (۳) می‌کنیم تا کارتابل‌های جدید فقط موارد تازه را نشان دهند
            migrationBuilder.Sql("UPDATE [ProjectEntryExits] SET [FlowStatus] = 3 WHERE [FlowStatus] = 0;");
        

// ===== 20260826035600_AddShiftGroupsAndAttendance.cs =====
            migrationBuilder.AddColumn<int>(
                name: "ShiftGroupId",
                table: "Users",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ShiftGroups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    StartTime = table.Column<TimeSpan>(type: "time", nullable: false),
                    EndTime = table.Column<TimeSpan>(type: "time", nullable: false),
                    GraceMinutes = table.Column<int>(type: "int", nullable: false),
                    IncludeFriday = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShiftGroups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AttendanceRecords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorkDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    UserName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    ShiftGroupId = table.Column<int>(type: "int", nullable: true),
                    EnterAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ExitAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    EnterIp = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ExitIp = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    EnterStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    LateMinutes = table.Column<int>(type: "int", nullable: false),
                    EarlyLeaveMinutes = table.Column<int>(type: "int", nullable: false),
                    WorkMinutes = table.Column<int>(type: "int", nullable: false),
                    DeficitMinutes = table.Column<int>(type: "int", nullable: false),
                    HasApprovedLeave = table.Column<bool>(type: "bit", nullable: false),
                    FinalStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AttendanceRecords_ShiftGroups_ShiftGroupId",
                        column: x => x.ShiftGroupId,
                        principalTable: "ShiftGroups",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Users_ShiftGroupId",
                table: "Users",
                column: "ShiftGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceRecords_ShiftGroupId",
                table: "AttendanceRecords",
                column: "ShiftGroupId");

            migrationBuilder.AddForeignKey(
                name: "FK_Users_ShiftGroups_ShiftGroupId",
                table: "Users",
                column: "ShiftGroupId",
                principalTable: "ShiftGroups",
                principalColumn: "Id");
        

// ===== 20260826044011_AddHourlyTimesAndAttendanceSegments.cs =====
            migrationBuilder.Sql(@"
-- Hours: int -> float (فقط اگر هنوز int باشد)
IF EXISTS (
    SELECT 1 FROM sys.columns c
    JOIN sys.types t ON c.user_type_id = t.user_type_id
    WHERE c.object_id = OBJECT_ID(N'[dbo].[LeaveRequests]')
      AND c.name = N'Hours' AND t.name IN ('int', 'smallint', 'tinyint')
)
    ALTER TABLE [dbo].[LeaveRequests] ALTER COLUMN [Hours] float NOT NULL;

-- ستون‌های ساعتی (فقط اگر نباشند)
IF COL_LENGTH(N'[dbo].[LeaveRequests]', N'EndTime') IS NULL
    ALTER TABLE [dbo].[LeaveRequests] ADD [EndTime] time NULL;

IF COL_LENGTH(N'[dbo].[LeaveRequests]', N'StartTime') IS NULL
    ALTER TABLE [dbo].[LeaveRequests] ADD [StartTime] time NULL;

IF COL_LENGTH(N'[dbo].[AttendanceRecords]', N'CoveredGapMinutes') IS NULL
    ALTER TABLE [dbo].[AttendanceRecords] ADD [CoveredGapMinutes] int NOT NULL CONSTRAINT [DF_AttendanceRecords_CoveredGapMinutes] DEFAULT 0;

-- جدول بازه‌های ورود/خروج (فقط اگر نباشد)
IF OBJECT_ID(N'[dbo].[AttendanceSegments]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[AttendanceSegments] (
        [Id] int NOT NULL IDENTITY,
        [UserId] int NOT NULL,
        [UserName] nvarchar(150) NOT NULL,
        [WorkDate] datetime2 NOT NULL,
        [Seq] int NOT NULL,
        [EnterAt] datetime2 NULL,
        [EnterIp] nvarchar(max) NULL,
        [ExitAt] datetime2 NULL,
        [ExitIp] nvarchar(max) NULL,
        [EnterStatus] nvarchar(20) NULL,
        [LateMinutes] int NOT NULL,
        [ExitCovered] bit NOT NULL,
        [LinkedLeaveRequestId] int NULL,
        [LinkedLeaveNumber] nvarchar(30) NULL,
        [Note] nvarchar(500) NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_AttendanceSegments] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AttendanceSegments_LeaveRequests_LinkedLeaveRequestId]
            FOREIGN KEY ([LinkedLeaveRequestId]) REFERENCES [dbo].[LeaveRequests] ([Id]) ON DELETE SET NULL
    );
END;

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = N'IX_AttendanceSegments_LinkedLeaveRequestId'
      AND object_id = OBJECT_ID(N'[dbo].[AttendanceSegments]')
)
    CREATE INDEX [IX_AttendanceSegments_LinkedLeaveRequestId]
        ON [dbo].[AttendanceSegments] ([LinkedLeaveRequestId]);

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = N'IX_AttendanceSegments_UserId_WorkDate_Seq'
      AND object_id = OBJECT_ID(N'[dbo].[AttendanceSegments]')
)
    CREATE UNIQUE INDEX [IX_AttendanceSegments_UserId_WorkDate_Seq]
        ON [dbo].[AttendanceSegments] ([UserId], [WorkDate], [Seq]);
");
        

// ===== 20260826101303_AddCompanyHolidaysAndAdminLeave.cs =====
            migrationBuilder.AddColumn<bool>(
                name: "AdminCreated",
                table: "LeaveRequests",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "CompanyHolidays",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    HolidayDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreatedByName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyHolidays", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CompanyHolidays_HolidayDate",
                table: "CompanyHolidays",
                column: "HolidayDate");
        

// ===== 20260831041448_AddWorkCalendarAndOvertime.cs =====
            migrationBuilder.AddColumn<bool>(
                name: "IsUnauthorized",
                table: "AttendanceSegments",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "OvertimeMinutes",
                table: "AttendanceSegments",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "OvertimeMinutes",
                table: "AttendanceRecords",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "UnauthorizedMinutes",
                table: "AttendanceRecords",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "WorkCalendarDays",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsWorkday = table.Column<bool>(type: "bit", nullable: false),
                    StartTime = table.Column<TimeSpan>(type: "time", nullable: true),
                    EndTime = table.Column<TimeSpan>(type: "time", nullable: true),
                    GraceMinutes = table.Column<int>(type: "int", nullable: false),
                    OvertimeHours = table.Column<double>(type: "float", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkCalendarDays", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkCalendarDays_Date",
                table: "WorkCalendarDays",
                column: "Date",
                unique: true);
        

// ===== 20260831062257_AddOvertimeModesToWorkCalendar.cs =====
            migrationBuilder.AddColumn<TimeSpan>(
                name: "OvertimeEnd",
                table: "WorkCalendarDays",
                type: "time",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OvertimeMode",
                table: "WorkCalendarDays",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<TimeSpan>(
                name: "OvertimeStart",
                table: "WorkCalendarDays",
                type: "time",
                nullable: true);
        

// ===== 20260831102303_AddCalendarSettingsAndOfficialHolidays.cs =====
            migrationBuilder.AddColumn<bool>(
                name: "IsOfficial",
                table: "CompanyHolidays",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "WorkCalendarSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DefaultStart = table.Column<TimeSpan>(type: "time", nullable: false),
                    DefaultEnd = table.Column<TimeSpan>(type: "time", nullable: false),
                    GraceMinutes = table.Column<int>(type: "int", nullable: false),
                    RestDayFlags = table.Column<int>(type: "int", nullable: false),
                    ApplyOfficialHolidays = table.Column<bool>(type: "bit", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkCalendarSettings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkCalendarSettings_Id",
                table: "WorkCalendarSettings",
                column: "Id",
                unique: true);
        

// ===== 20260831103000_AddOfficeAutomation.cs =====
            migrationBuilder.CreateTable(
                name: "Amalgars",
                columns: table => new
                {
                    AmalgarId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Title = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TaeedEmza = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    IsDelete = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Amalgars", x => x.AmalgarId);
                });

            migrationBuilder.CreateTable(
                name: "LetterBayeganis",
                columns: table => new
                {
                    BayeganiId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ErjaId = table.Column<int>(type: "int", nullable: true),
                    ParentId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    SematId = table.Column<int>(type: "int", nullable: true),
                    TypeBayegani = table.Column<int>(type: "int", nullable: false),
                    IsFolder = table.Column<bool>(type: "bit", nullable: false),
                    IsDelete = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LetterBayeganis", x => x.BayeganiId);
                });

            migrationBuilder.CreateTable(
                name: "LetterSources",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SourceType = table.Column<int>(type: "int", nullable: false),
                    IsDelete = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LetterSources", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PishnevisLetters",
                columns: table => new
                {
                    PishnevisId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    SematId = table.Column<int>(type: "int", nullable: true),
                    IsNeshan = table.Column<bool>(type: "bit", nullable: false),
                    IsDelete = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PishnevisLetters", x => x.PishnevisId);
                    table.ForeignKey(
                        name: "FK_PishnevisLetters_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Erjas",
                columns: table => new
                {
                    ErjaId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SourceId = table.Column<int>(type: "int", nullable: false),
                    SenderUserId = table.Column<int>(type: "int", nullable: false),
                    ReciverUserId = table.Column<int>(type: "int", nullable: false),
                    SenderSematId = table.Column<int>(type: "int", nullable: true),
                    ReciverSematId = table.Column<int>(type: "int", nullable: true),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    TypeTaeed = table.Column<int>(type: "int", nullable: false),
                    Answer = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsRead = table.Column<bool>(type: "bit", nullable: false),
                    IsBayegani = table.Column<bool>(type: "bit", nullable: true),
                    MohlatPasokh = table.Column<DateTime>(type: "datetime2", nullable: true),
                    MatnErja = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AmalgarId = table.Column<int>(type: "int", nullable: false),
                    IsNeshan = table.Column<bool>(type: "bit", nullable: false),
                    ShowForAll = table.Column<bool>(type: "bit", nullable: false),
                    ShowMassage = table.Column<bool>(type: "bit", nullable: false),
                    DateRead = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DateEmza = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DateAnswer = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsReadAnswer = table.Column<bool>(type: "bit", nullable: false),
                    ShowMassageAnswer = table.Column<bool>(type: "bit", nullable: false),
                    IsDelete = table.Column<bool>(type: "bit", nullable: false),
                    ParentErjaId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Erjas", x => x.ErjaId);
                    table.ForeignKey(
                        name: "FK_Erjas_Amalgars_AmalgarId",
                        column: x => x.AmalgarId,
                        principalTable: "Amalgars",
                        principalColumn: "AmalgarId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Erjas_LetterSources_SourceId",
                        column: x => x.SourceId,
                        principalTable: "LetterSources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Erjas_Users_ReciverUserId",
                        column: x => x.ReciverUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Erjas_Users_SenderUserId",
                        column: x => x.SenderUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InnerLetters",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    LetterNumber = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    Number = table.Column<int>(type: "int", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    CreatorSematId = table.Column<int>(type: "int", nullable: true),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DateSabt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Mahramanegi = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Foriat = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    IsDelete = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InnerLetters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InnerLetters_LetterSources_Id",
                        column: x => x.Id,
                        principalTable: "LetterSources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_InnerLetters_Users_CreatorUserId",
                        column: x => x.CreatorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RelatedLetters",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Related = table.Column<int>(type: "int", nullable: false),
                    LetterId = table.Column<int>(type: "int", nullable: false),
                    RelateLetterId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    SematId = table.Column<int>(type: "int", nullable: true),
                    IsDelete = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RelatedLetters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RelatedLetters_LetterSources_LetterId",
                        column: x => x.LetterId,
                        principalTable: "LetterSources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RelatedLetters_LetterSources_RelateLetterId",
                        column: x => x.RelateLetterId,
                        principalTable: "LetterSources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Erjas_AmalgarId",
                table: "Erjas",
                column: "AmalgarId");

            migrationBuilder.CreateIndex(
                name: "IX_Erjas_ReciverUserId",
                table: "Erjas",
                column: "ReciverUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Erjas_ReciverUserId_IsRead",
                table: "Erjas",
                columns: new[] { "ReciverUserId", "IsRead" });

            migrationBuilder.CreateIndex(
                name: "IX_Erjas_SenderUserId",
                table: "Erjas",
                column: "SenderUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Erjas_SourceId",
                table: "Erjas",
                column: "SourceId");

            migrationBuilder.CreateIndex(
                name: "IX_InnerLetters_CreatorUserId",
                table: "InnerLetters",
                column: "CreatorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_InnerLetters_DateSabt",
                table: "InnerLetters",
                column: "DateSabt");

            migrationBuilder.CreateIndex(
                name: "IX_InnerLetters_Number",
                table: "InnerLetters",
                column: "Number");

            migrationBuilder.CreateIndex(
                name: "IX_LetterBayeganis_UserId",
                table: "LetterBayeganis",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_PishnevisLetters_UserId",
                table: "PishnevisLetters",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_RelatedLetters_LetterId",
                table: "RelatedLetters",
                column: "LetterId");

            migrationBuilder.CreateIndex(
                name: "IX_RelatedLetters_RelateLetterId",
                table: "RelatedLetters",
                column: "RelateLetterId");



            migrationBuilder.CreateTable(
                name: "LetterGroups",
                columns: table => new
                {
                    GroupId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    NameGroup = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Condition = table.Column<bool>(type: "bit", nullable: false),
                    IsDelete = table.Column<bool>(type: "bit", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    CreatorSematId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LetterGroups", x => x.GroupId);
                    table.ForeignKey(
                        name: "FK_LetterGroups_Users_CreatorUserId",
                        column: x => x.CreatorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LetterGroupMembers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    GroupId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    SematId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LetterGroupMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LetterGroupMembers_LetterGroups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "LetterGroups",
                        principalColumn: "GroupId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LetterGroupMembers_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LetterGroupMembers_GroupId_UserId",
                table: "LetterGroupMembers",
                columns: new[] { "GroupId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LetterGroupMembers_UserId",
                table: "LetterGroupMembers",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_LetterGroups_CreatorUserId",
                table: "LetterGroups",
                column: "CreatorUserId");

        

// ===== 20260901000000_AddOutgoingLetters.cs =====
            migrationBuilder.CreateTable(
                name: "OutgoingPishnevisLetters",
                columns: table => new
                {
                    PishnevisId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ReceiverOrganization = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    ReceiverName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    ReceiverTitle = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    SematId = table.Column<int>(type: "int", nullable: true),
                    IsNeshan = table.Column<bool>(type: "bit", nullable: false),
                    IsDelete = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutgoingPishnevisLetters", x => x.PishnevisId);
                    table.ForeignKey(
                        name: "FK_OutgoingPishnevisLetters_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OutgoingLetters",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    LetterNumber = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    Number = table.Column<int>(type: "int", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    CreatorSematId = table.Column<int>(type: "int", nullable: true),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DateSabt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Mahramanegi = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Foriat = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ReceiverOrganization = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    ReceiverName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    ReceiverTitle = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    ReceiverAddress = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CopyTo = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ExternalRefNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    IsDelete = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutgoingLetters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OutgoingLetters_LetterSources_Id",
                        column: x => x.Id,
                        principalTable: "LetterSources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OutgoingLetters_Users_CreatorUserId",
                        column: x => x.CreatorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OutgoingLetters_CreatorUserId",
                table: "OutgoingLetters",
                column: "CreatorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OutgoingLetters_DateSabt",
                table: "OutgoingLetters",
                column: "DateSabt");

            migrationBuilder.CreateIndex(
                name: "IX_OutgoingLetters_Number",
                table: "OutgoingLetters",
                column: "Number");

            migrationBuilder.CreateIndex(
                name: "IX_OutgoingLetters_ReceiverOrganization",
                table: "OutgoingLetters",
                column: "ReceiverOrganization");

            migrationBuilder.CreateIndex(
                name: "IX_OutgoingPishnevisLetters_UserId",
                table: "OutgoingPishnevisLetters",
                column: "UserId");
        

// ===== 20260901042209_AddAuditLogAndDeviceTracking.cs =====
            migrationBuilder.AddColumn<string>(
                name: "EnterDevice",
                table: "AttendanceSegments",
                type: "nvarchar(250)",
                maxLength: 250,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    At = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: true),
                    Username = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Module = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Action = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    HttpMethod = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Path = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Summary = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Payload = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Ip = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Device = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    StatusCode = table.Column<int>(type: "int", nullable: false),
                    DurationMs = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_At",
                table: "AuditLogs",
                column: "At");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_UserId",
                table: "AuditLogs",
                column: "UserId");
        

// ===== 20260901090000_AddSplitShiftWindows.cs =====
            migrationBuilder.AddColumn<TimeSpan>(
                name: "StartTime2",
                table: "ShiftGroups",
                type: "time",
                nullable: true);

            migrationBuilder.AddColumn<TimeSpan>(
                name: "EndTime2",
                table: "ShiftGroups",
                type: "time",
                nullable: true);
        

// ===== 20260902000000_AddOutgoingSignersAndSadereNumber.cs =====
            migrationBuilder.AddColumn<string>(
                name: "SadereNumber",
                table: "OutgoingLetters",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DateSadere",
                table: "OutgoingLetters",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutgoingLetters_SadereNumber",
                table: "OutgoingLetters",
                column: "SadereNumber");

            migrationBuilder.CreateTable(
                name: "OutgoingLetterSigners",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SourceId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    SematId = table.Column<int>(type: "int", nullable: true),
                    Order = table.Column<int>(type: "int", nullable: false),
                    IsSigned = table.Column<bool>(type: "bit", nullable: false),
                    DateSigned = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SignNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IsDelete = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutgoingLetterSigners", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OutgoingLetterSigners_LetterSources_SourceId",
                        column: x => x.SourceId,
                        principalTable: "LetterSources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OutgoingLetterSigners_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OutgoingLetterSigners_SourceId",
                table: "OutgoingLetterSigners",
                column: "SourceId");

            migrationBuilder.CreateIndex(
                name: "IX_OutgoingLetterSigners_SourceId_UserId",
                table: "OutgoingLetterSigners",
                columns: new[] { "SourceId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutgoingLetterSigners_UserId",
                table: "OutgoingLetterSigners",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_OutgoingLetterSigners_UserId_IsSigned",
                table: "OutgoingLetterSigners",
                columns: new[] { "UserId", "IsSigned" });
        

// ===== 20260902100000_AddOutgoingDabirkhaneAndLetterhead.cs =====
            migrationBuilder.AddColumn<int>(
                name: "CompanyId",
                table: "OutgoingLetters",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "DabirkhaneSabt",
                table: "OutgoingLetters",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "DabirkhaneUserId",
                table: "OutgoingLetters",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DateDabirkhane",
                table: "OutgoingLetters",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DestRegNumber",
                table: "OutgoingLetters",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SendMethod",
                table: "OutgoingLetters",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DabirkhaneNote",
                table: "OutgoingLetters",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LetterheadFileName",
                table: "SystemCompanies",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);
        

// ===== 20260902110000_AddLetterStrature.cs =====
            migrationBuilder.CreateTable(
                name: "LetterStratures",
                columns: table => new
                {
                    StratureId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TypeForm = table.Column<int>(type: "int", nullable: false),
                    TypeStrature = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LetterStratures", x => x.StratureId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LetterStratures_TypeForm",
                table: "LetterStratures",
                column: "TypeForm");
        

// ===== 20260902120000_AddAttendanceSecurity.cs =====
            // ==================== امنیت حضور و غیاب ====================
            // ۱) جدول دستگاه‌های کاربران (Device ID از localStorage + IP + User-Agent)
            // ۲) جدول هشدارهای امنیتی برای مدیر (دستگاه جدید / دستگاه مشترک / خارج از محدوده)
            // ۳) جدول تنظیمات محدوده‌ی مکانی مجاز (مرکز + شعاع، پیش‌فرض ۱ کیلومتر)

            migrationBuilder.CreateTable(
                name: "UserDevices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    UserName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    DeviceId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Ip = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    UserAgent = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    IsPrimary = table.Column<bool>(type: "bit", nullable: false),
                    IsApproved = table.Column<bool>(type: "bit", nullable: false),
                    ApprovedBy = table.Column<int>(type: "int", nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FirstSeenAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UsedCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserDevices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AttendanceAlerts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    UserName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    AlertType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    DeviceId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Ip = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Lat = table.Column<double>(type: "float", nullable: true),
                    Lng = table.Column<double>(type: "float", nullable: true),
                    DistanceMeters = table.Column<double>(type: "float", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    HandledBy = table.Column<int>(type: "int", nullable: true),
                    HandledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceAlerts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AttendanceAreaSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Latitude = table.Column<double>(type: "float", nullable: false),
                    Longitude = table.Column<double>(type: "float", nullable: false),
                    RadiusMeters = table.Column<double>(type: "float", nullable: false),
                    LocationName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedBy = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceAreaSettings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserDevices_UserId_DeviceId",
                table: "UserDevices",
                columns: new[] { "UserId", "DeviceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserDevices_DeviceId",
                table: "UserDevices",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceAlerts_UserId_Status",
                table: "AttendanceAlerts",
                columns: new[] { "UserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceAlerts_Status_CreatedAt",
                table: "AttendanceAlerts",
                columns: new[] { "Status", "CreatedAt" });
        

// ===== 20260902120000_AddLetterNeshan.cs =====
            migrationBuilder.AddColumn<bool>(
                name: "IsNeshan",
                table: "InnerLetters",
                type: "bit",
                nullable: false,
                defaultValue: false);
        

// ===== 20260902130000_AddBayeganiLetterId.cs =====
            migrationBuilder.AddColumn<int>(
                name: "LetterId",
                table: "LetterBayeganis",
                type: "int",
                nullable: true);
        

// ===== 20260902150000_AddPushSubscriptions.cs =====
            // جدول اشتراک‌های نوتیفیکیشن گوشی/تبلت (Web Push)
            // هر کاربر می‌تواند چند دستگاه (مرورگر/گوشی/تبلت) ثبت کرده باشد.
            migrationBuilder.CreateTable(
                name: "PushSubscriptions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Endpoint = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    P256DH = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Auth = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    UserAgent = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PushSubscriptions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PushSubscriptions_UserId",
                table: "PushSubscriptions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_PushSubscriptions_UserId_Endpoint",
                table: "PushSubscriptions",
                columns: new[] { "UserId", "Endpoint" },
                unique: true);
        

// ===== 20260903070000_LegacyAriaImportPrep.cs =====
            migrationBuilder.AddColumn<int>(
                name: "OperatorId",
                table: "ReportWorks",
                type: "int",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "KarshenasiAvalie",
                table: "ProjectEntryExits",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldMaxLength: 50,
                oldNullable: true);

            // time → bigint (تیک): تبدیل صریح چون SQL Server تبدیل ضمنی time→bigint ندارد
            migrationBuilder.Sql(@"
ALTER TABLE [ProjectEntryExits] ADD [TotalSpentTime_Ticks] bigint NOT NULL CONSTRAINT [DF_ProjectEntryExits_TotalSpentTime_Ticks] DEFAULT(0);
EXEC('UPDATE [ProjectEntryExits] SET [TotalSpentTime_Ticks] = CAST(DATEDIFF(millisecond, CAST(''00:00:00'' AS time), [TotalSpentTime]) AS bigint) * 10000;');
ALTER TABLE [ProjectEntryExits] DROP COLUMN [TotalSpentTime];
EXEC sp_rename 'ProjectEntryExits.TotalSpentTime_Ticks', 'TotalSpentTime', 'COLUMN';
ALTER TABLE [ProjectEntryExits] DROP CONSTRAINT [DF_ProjectEntryExits_TotalSpentTime_Ticks];
");

            migrationBuilder.CreateIndex(
                name: "IX_ReportWorks_OperatorId",
                table: "ReportWorks",
                column: "OperatorId");

            migrationBuilder.AddForeignKey(
                name: "FK_ReportWorks_Users_OperatorId",
                table: "ReportWorks",
                column: "OperatorId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        

// ===== 20260903101000_AddWorkOrderSourceLink.cs =====
            migrationBuilder.AddColumn<string>(
                name: "SourceModule",
                table: "WorkOrders",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SourceId",
                table: "WorkOrders",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_SourceModule_SourceId",
                table: "WorkOrders",
                columns: new[] { "SourceModule", "SourceId" });
        

// ===== 20260905154353_AddWarehousingModule.cs =====
            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_PushSubscriptions_UserId' AND [object_id] = OBJECT_ID(N'[PushSubscriptions]'))
BEGIN
    DROP INDEX [IX_PushSubscriptions_UserId] ON [PushSubscriptions];
END;");

            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_PushSubscriptions_UserId_Endpoint' AND [object_id] = OBJECT_ID(N'[PushSubscriptions]'))
BEGIN
    DROP INDEX [IX_PushSubscriptions_UserId_Endpoint] ON [PushSubscriptions];
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Warehouses]', N'AllowNegative') IS NULL
BEGIN
    ALTER TABLE [Warehouses] ADD [AllowNegative] bit NOT NULL DEFAULT CAST(0 AS bit);
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Warehouses]', N'Code') IS NULL
BEGIN
    ALTER TABLE [Warehouses] ADD [Code] nvarchar(30) NULL;
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Warehouses]', N'IsDefault') IS NULL
BEGIN
    ALTER TABLE [Warehouses] ADD [IsDefault] bit NOT NULL DEFAULT CAST(0 AS bit);
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Warehouses]', N'KeeperName') IS NULL
BEGIN
    ALTER TABLE [Warehouses] ADD [KeeperName] nvarchar(120) NULL;
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Warehouses]', N'Kind') IS NULL
BEGIN
    ALTER TABLE [Warehouses] ADD [Kind] int NOT NULL DEFAULT 0;
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[ShiftGroups]', N'EndTime2') IS NULL
BEGIN
    ALTER TABLE [ShiftGroups] ADD [EndTime2] time NULL;
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[ShiftGroups]', N'StartTime2') IS NULL
BEGIN
    ALTER TABLE [ShiftGroups] ADD [StartTime2] time NULL;
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'Brand') IS NULL
BEGIN
    ALTER TABLE [Products] ADD [Brand] nvarchar(100) NULL;
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'CategoryId') IS NULL
BEGIN
    ALTER TABLE [Products] ADD [CategoryId] int NULL;
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'CountryOfOrigin') IS NULL
BEGIN
    ALTER TABLE [Products] ADD [CountryOfOrigin] nvarchar(80) NULL;
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'CustomsCode') IS NULL
BEGIN
    ALTER TABLE [Products] ADD [CustomsCode] nvarchar(30) NULL;
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'DutyRate') IS NULL
BEGIN
    ALTER TABLE [Products] ADD [DutyRate] decimal(6,2) NOT NULL DEFAULT 0.0;
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'EnName') IS NULL
BEGIN
    ALTER TABLE [Products] ADD [EnName] nvarchar(200) NULL;
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'Height') IS NULL
BEGIN
    ALTER TABLE [Products] ADD [Height] decimal(18,3) NULL;
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'ImageUrl') IS NULL
BEGIN
    ALTER TABLE [Products] ADD [ImageUrl] nvarchar(300) NULL;
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'IsVatIncluded') IS NULL
BEGIN
    ALTER TABLE [Products] ADD [IsVatIncluded] bit NOT NULL DEFAULT CAST(0 AS bit);
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'Length') IS NULL
BEGIN
    ALTER TABLE [Products] ADD [Length] decimal(18,3) NULL;
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'MinOrderQty') IS NULL
BEGIN
    ALTER TABLE [Products] ADD [MinOrderQty] decimal(18,3) NOT NULL DEFAULT 0.0;
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'Model') IS NULL
BEGIN
    ALTER TABLE [Products] ADD [Model] nvarchar(100) NULL;
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'Note') IS NULL
BEGIN
    ALTER TABLE [Products] ADD [Note] nvarchar(max) NULL;
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'OtherTaxRate') IS NULL
BEGIN
    ALTER TABLE [Products] ADD [OtherTaxRate] decimal(6,2) NOT NULL DEFAULT 0.0;
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'PartNumber') IS NULL
BEGIN
    ALTER TABLE [Products] ADD [PartNumber] nvarchar(100) NULL;
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'SalePrice2') IS NULL
BEGIN
    ALTER TABLE [Products] ADD [SalePrice2] decimal(18,2) NOT NULL DEFAULT 0.0;
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'SecondUnit') IS NULL
BEGIN
    ALTER TABLE [Products] ADD [SecondUnit] nvarchar(50) NULL;
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'ShelfCode') IS NULL
BEGIN
    ALTER TABLE [Products] ADD [ShelfCode] nvarchar(50) NULL;
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'ShelfLifeDays') IS NULL
BEGIN
    ALTER TABLE [Products] ADD [ShelfLifeDays] int NULL;
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'TaxCode') IS NULL
BEGIN
    ALTER TABLE [Products] ADD [TaxCode] nvarchar(20) NULL;
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'TaxUnitCode') IS NULL
BEGIN
    ALTER TABLE [Products] ADD [TaxUnitCode] nvarchar(20) NULL;
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'TrackBatch') IS NULL
BEGIN
    ALTER TABLE [Products] ADD [TrackBatch] bit NOT NULL DEFAULT CAST(0 AS bit);
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'TrackExpiry') IS NULL
BEGIN
    ALTER TABLE [Products] ADD [TrackExpiry] bit NOT NULL DEFAULT CAST(0 AS bit);
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'TrackSerial') IS NULL
BEGIN
    ALTER TABLE [Products] ADD [TrackSerial] bit NOT NULL DEFAULT CAST(0 AS bit);
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'UnitFactor') IS NULL
BEGIN
    ALTER TABLE [Products] ADD [UnitFactor] decimal(18,4) NULL;
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'Valuation') IS NULL
BEGIN
    ALTER TABLE [Products] ADD [Valuation] int NULL;
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'VatRate') IS NULL
BEGIN
    ALTER TABLE [Products] ADD [VatRate] decimal(6,2) NOT NULL DEFAULT 0.0;
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'Weight') IS NULL
BEGIN
    ALTER TABLE [Products] ADD [Weight] decimal(18,3) NULL;
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'Width') IS NULL
BEGIN
    ALTER TABLE [Products] ADD [Width] decimal(18,3) NULL;
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[ProductCategories]', N'Code') IS NULL
BEGIN
    ALTER TABLE [ProductCategories] ADD [Code] nvarchar(30) NULL;
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[ProductCategories]', N'SortOrder') IS NULL
BEGIN
    ALTER TABLE [ProductCategories] ADD [SortOrder] int NOT NULL DEFAULT 0;
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[ProductCategories]', N'Valuation') IS NULL
BEGIN
    ALTER TABLE [ProductCategories] ADD [Valuation] int NULL;
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[OutgoingLetterSigners]', N'OutgoingLetterId') IS NULL
BEGIN
    ALTER TABLE [OutgoingLetterSigners] ADD [OutgoingLetterId] int NULL;
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[CompanyHolidays]', N'IsOfficial') IS NULL
BEGIN
    ALTER TABLE [CompanyHolidays] ADD [IsOfficial] bit NOT NULL DEFAULT CAST(0 AS bit);
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[AttendanceSegments]', N'EnterDevice') IS NULL
BEGIN
    ALTER TABLE [AttendanceSegments] ADD [EnterDevice] nvarchar(250) NULL;
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[AttendanceSegments]', N'IsUnauthorized') IS NULL
BEGIN
    ALTER TABLE [AttendanceSegments] ADD [IsUnauthorized] bit NOT NULL DEFAULT CAST(0 AS bit);
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[AttendanceSegments]', N'OvertimeMinutes') IS NULL
BEGIN
    ALTER TABLE [AttendanceSegments] ADD [OvertimeMinutes] int NOT NULL DEFAULT 0;
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[AttendanceRecords]', N'OvertimeMinutes') IS NULL
BEGIN
    ALTER TABLE [AttendanceRecords] ADD [OvertimeMinutes] int NOT NULL DEFAULT 0;
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[AttendanceRecords]', N'UnauthorizedMinutes') IS NULL
BEGIN
    ALTER TABLE [AttendanceRecords] ADD [UnauthorizedMinutes] int NOT NULL DEFAULT 0;
END;");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[AuditLogs]', N'U') IS NULL
BEGIN
    CREATE TABLE [AuditLogs] (
        [Id] bigint NOT NULL IDENTITY,
        [At] datetime2 NOT NULL,
        [UserId] int NULL,
        [Username] nvarchar(100) NULL,
        [Module] nvarchar(80) NOT NULL,
        [Action] nvarchar(80) NOT NULL,
        [HttpMethod] nvarchar(10) NOT NULL,
        [Path] nvarchar(300) NULL,
        [Summary] nvarchar(200) NULL,
        [Payload] nvarchar(4000) NULL,
        [Ip] nvarchar(64) NULL,
        [Device] nvarchar(250) NULL,
        [StatusCode] int NOT NULL,
        [DurationMs] int NOT NULL,
        CONSTRAINT [PK_AuditLogs] PRIMARY KEY ([Id])
    );
END;");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[InvDocs]', N'U') IS NULL
BEGIN
    CREATE TABLE [InvDocs] (
        [Id] int NOT NULL IDENTITY,
        [Number] nvarchar(30) NOT NULL,
        [DocTypeId] int NOT NULL,
        [WarehouseId] int NOT NULL,
        [CounterWarehouseId] int NULL,
        [PartyId] int NULL,
        [Date] datetime2 NOT NULL,
        [RefNumber] nvarchar(50) NULL,
        [Description] nvarchar(max) NULL,
        [Status] int NOT NULL,
        [TotalQuantity] decimal(18,3) NOT NULL,
        [TotalValue] decimal(18,2) NOT NULL,
        [CreatedBy] nvarchar(80) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [ConfirmedBy] nvarchar(80) NULL,
        [ConfirmedAt] datetime2 NULL,
        CONSTRAINT [PK_InvDocs] PRIMARY KEY ([Id])
    );
END;");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[InvDocTypes]', N'U') IS NULL
BEGIN
    CREATE TABLE [InvDocTypes] (
        [Id] int NOT NULL IDENTITY,
        [Code] nvarchar(30) NOT NULL,
        [Name] nvarchar(120) NOT NULL,
        [Nature] int NOT NULL,
        [IsTransfer] bit NOT NULL,
        [RequiresParty] bit NOT NULL,
        [RequiresPrice] bit NOT NULL,
        [NumberPrefix] nvarchar(10) NULL,
        [Color] nvarchar(30) NULL,
        [Icon] nvarchar(50) NULL,
        [IsSystem] bit NOT NULL,
        [IsActive] bit NOT NULL,
        [SortOrder] int NOT NULL,
        [Description] nvarchar(max) NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_InvDocTypes] PRIMARY KEY ([Id])
    );
END;");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[InvLedger]', N'U') IS NULL
BEGIN
    CREATE TABLE [InvLedger] (
        [Id] bigint NOT NULL IDENTITY,
        [ProductId] int NOT NULL,
        [WarehouseId] int NOT NULL,
        [Date] datetime2 NOT NULL,
        [DocId] int NOT NULL,
        [DocLineId] int NOT NULL,
        [DocTypeId] int NOT NULL,
        [Nature] int NOT NULL,
        [Number] nvarchar(30) NOT NULL,
        [Description] nvarchar(300) NULL,
        [QtyIn] decimal(18,3) NOT NULL,
        [QtyOut] decimal(18,3) NOT NULL,
        [UnitCost] decimal(18,4) NOT NULL,
        [ValueIn] decimal(18,2) NOT NULL,
        [ValueOut] decimal(18,2) NOT NULL,
        [BalanceQty] decimal(18,3) NOT NULL,
        [BalanceValue] decimal(18,2) NOT NULL,
        [RemainingQty] decimal(18,3) NOT NULL,
        [Seq] int NOT NULL,
        CONSTRAINT [PK_InvLedger] PRIMARY KEY ([Id])
    );
END;");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[InvStocks]', N'U') IS NULL
BEGIN
    CREATE TABLE [InvStocks] (
        [Id] int NOT NULL IDENTITY,
        [ProductId] int NOT NULL,
        [WarehouseId] int NOT NULL,
        [Quantity] decimal(18,3) NOT NULL,
        [Value] decimal(18,2) NOT NULL,
        [AvgCost] decimal(18,4) NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_InvStocks] PRIMARY KEY ([Id])
    );
END;");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[ProductAttributeDefs]', N'U') IS NULL
BEGIN
    CREATE TABLE [ProductAttributeDefs] (
        [Id] int NOT NULL IDENTITY,
        [Name] nvarchar(120) NOT NULL,
        [Code] nvarchar(50) NULL,
        [ValueType] int NOT NULL,
        [Unit] nvarchar(50) NULL,
        [CategoryId] int NULL,
        [IsRequired] bit NOT NULL,
        [ShowInList] bit NOT NULL,
        [SortOrder] int NOT NULL,
        [IsActive] bit NOT NULL,
        [Description] nvarchar(max) NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_ProductAttributeDefs] PRIMARY KEY ([Id])
    );
END;");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[ProductAttributeOptions]', N'U') IS NULL
BEGIN
    CREATE TABLE [ProductAttributeOptions] (
        [Id] int NOT NULL IDENTITY,
        [AttributeId] int NOT NULL,
        [Title] nvarchar(150) NOT NULL,
        [SortOrder] int NOT NULL,
        CONSTRAINT [PK_ProductAttributeOptions] PRIMARY KEY ([Id])
    );
END;");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[ProductAttributeValues]', N'U') IS NULL
BEGIN
    CREATE TABLE [ProductAttributeValues] (
        [Id] int NOT NULL IDENTITY,
        [ProductId] int NOT NULL,
        [AttributeId] int NOT NULL,
        [OptionId] int NULL,
        [TextValue] nvarchar(500) NULL,
        [NumberValue] decimal(18,4) NULL,
        [BoolValue] bit NULL,
        [DateValue] datetime2 NULL,
        CONSTRAINT [PK_ProductAttributeValues] PRIMARY KEY ([Id])
    );
END;");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[WorkCalendarDays]', N'U') IS NULL
BEGIN
    CREATE TABLE [WorkCalendarDays] (
        [Id] int NOT NULL IDENTITY,
        [Date] datetime2 NOT NULL,
        [IsWorkday] bit NOT NULL,
        [StartTime] time NULL,
        [EndTime] time NULL,
        [GraceMinutes] int NOT NULL,
        [OvertimeHours] float NOT NULL,
        [OvertimeMode] int NOT NULL,
        [OvertimeStart] time NULL,
        [OvertimeEnd] time NULL,
        [Note] nvarchar(100) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        CONSTRAINT [PK_WorkCalendarDays] PRIMARY KEY ([Id])
    );
END;");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[WorkCalendarSettings]', N'U') IS NULL
BEGIN
    CREATE TABLE [WorkCalendarSettings] (
        [Id] int NOT NULL IDENTITY,
        [DefaultStart] time NOT NULL,
        [DefaultEnd] time NOT NULL,
        [GraceMinutes] int NOT NULL,
        [RestDayFlags] int NOT NULL,
        [ApplyOfficialHolidays] bit NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_WorkCalendarSettings] PRIMARY KEY ([Id])
    );
END;");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[InvDocLines]', N'U') IS NULL
BEGIN
    CREATE TABLE [InvDocLines] (
        [Id] int NOT NULL IDENTITY,
        [DocId] int NOT NULL,
        [RowNo] int NOT NULL,
        [ProductId] int NOT NULL,
        [Quantity] decimal(18,3) NOT NULL,
        [UnitPrice] decimal(18,2) NOT NULL,
        [Discount] decimal(18,2) NOT NULL,
        [BatchNo] nvarchar(50) NULL,
        [SerialNo] nvarchar(80) NULL,
        [ExpiryDate] datetime2 NULL,
        [Description] nvarchar(300) NULL,
        [OutCost] decimal(18,4) NULL,
        CONSTRAINT [PK_InvDocLines] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_InvDocLines_InvDocs_DocId] FOREIGN KEY ([DocId]) REFERENCES [InvDocs] ([Id]) ON DELETE CASCADE
    );
END;");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[Warehouses]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_Warehouses_Code' AND [object_id] = OBJECT_ID(N'[Warehouses]'))
BEGIN
    CREATE INDEX [IX_Warehouses_Code] ON [Warehouses] ([Code]);
END;");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[Products]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_Products_CategoryId' AND [object_id] = OBJECT_ID(N'[Products]'))
BEGIN
    CREATE INDEX [IX_Products_CategoryId] ON [Products] ([CategoryId]);
END;");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[Products]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_Products_TaxCode' AND [object_id] = OBJECT_ID(N'[Products]'))
BEGIN
    CREATE INDEX [IX_Products_TaxCode] ON [Products] ([TaxCode]);
END;");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[ProductCategories]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_ProductCategories_ParentId' AND [object_id] = OBJECT_ID(N'[ProductCategories]'))
BEGIN
    CREATE INDEX [IX_ProductCategories_ParentId] ON [ProductCategories] ([ParentId]);
END;");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[OutgoingLetterSigners]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_OutgoingLetterSigners_OutgoingLetterId' AND [object_id] = OBJECT_ID(N'[OutgoingLetterSigners]'))
BEGIN
    CREATE INDEX [IX_OutgoingLetterSigners_OutgoingLetterId] ON [OutgoingLetterSigners] ([OutgoingLetterId]);
END;");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[AuditLogs]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_AuditLogs_At' AND [object_id] = OBJECT_ID(N'[AuditLogs]'))
BEGIN
    CREATE INDEX [IX_AuditLogs_At] ON [AuditLogs] ([At]);
END;");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[AuditLogs]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_AuditLogs_UserId' AND [object_id] = OBJECT_ID(N'[AuditLogs]'))
BEGIN
    CREATE INDEX [IX_AuditLogs_UserId] ON [AuditLogs] ([UserId]);
END;");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[InvDocLines]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_InvDocLines_DocId' AND [object_id] = OBJECT_ID(N'[InvDocLines]'))
BEGIN
    CREATE INDEX [IX_InvDocLines_DocId] ON [InvDocLines] ([DocId]);
END;");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[InvDocLines]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_InvDocLines_ProductId' AND [object_id] = OBJECT_ID(N'[InvDocLines]'))
BEGIN
    CREATE INDEX [IX_InvDocLines_ProductId] ON [InvDocLines] ([ProductId]);
END;");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[InvDocs]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_InvDocs_Date' AND [object_id] = OBJECT_ID(N'[InvDocs]'))
BEGIN
    CREATE INDEX [IX_InvDocs_Date] ON [InvDocs] ([Date]);
END;");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[InvDocs]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_InvDocs_DocTypeId' AND [object_id] = OBJECT_ID(N'[InvDocs]'))
BEGIN
    CREATE INDEX [IX_InvDocs_DocTypeId] ON [InvDocs] ([DocTypeId]);
END;");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[InvDocs]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_InvDocs_Number' AND [object_id] = OBJECT_ID(N'[InvDocs]'))
BEGIN
    CREATE UNIQUE INDEX [IX_InvDocs_Number] ON [InvDocs] ([Number]);
END;");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[InvDocs]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_InvDocs_Status' AND [object_id] = OBJECT_ID(N'[InvDocs]'))
BEGIN
    CREATE INDEX [IX_InvDocs_Status] ON [InvDocs] ([Status]);
END;");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[InvDocs]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_InvDocs_WarehouseId' AND [object_id] = OBJECT_ID(N'[InvDocs]'))
BEGIN
    CREATE INDEX [IX_InvDocs_WarehouseId] ON [InvDocs] ([WarehouseId]);
END;");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[InvDocTypes]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_InvDocTypes_Code' AND [object_id] = OBJECT_ID(N'[InvDocTypes]'))
BEGIN
    CREATE UNIQUE INDEX [IX_InvDocTypes_Code] ON [InvDocTypes] ([Code]);
END;");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[InvDocTypes]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_InvDocTypes_Nature' AND [object_id] = OBJECT_ID(N'[InvDocTypes]'))
BEGIN
    CREATE INDEX [IX_InvDocTypes_Nature] ON [InvDocTypes] ([Nature]);
END;");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[InvLedger]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_InvLedger_DocId' AND [object_id] = OBJECT_ID(N'[InvLedger]'))
BEGIN
    CREATE INDEX [IX_InvLedger_DocId] ON [InvLedger] ([DocId]);
END;");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[InvLedger]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_InvLedger_ProductId_WarehouseId_Date' AND [object_id] = OBJECT_ID(N'[InvLedger]'))
BEGIN
    CREATE INDEX [IX_InvLedger_ProductId_WarehouseId_Date] ON [InvLedger] ([ProductId], [WarehouseId], [Date]);
END;");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[InvStocks]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_InvStocks_ProductId_WarehouseId' AND [object_id] = OBJECT_ID(N'[InvStocks]'))
BEGIN
    CREATE UNIQUE INDEX [IX_InvStocks_ProductId_WarehouseId] ON [InvStocks] ([ProductId], [WarehouseId]);
END;");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[ProductAttributeDefs]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_ProductAttributeDefs_CategoryId' AND [object_id] = OBJECT_ID(N'[ProductAttributeDefs]'))
BEGIN
    CREATE INDEX [IX_ProductAttributeDefs_CategoryId] ON [ProductAttributeDefs] ([CategoryId]);
END;");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[ProductAttributeDefs]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_ProductAttributeDefs_Name' AND [object_id] = OBJECT_ID(N'[ProductAttributeDefs]'))
BEGIN
    CREATE INDEX [IX_ProductAttributeDefs_Name] ON [ProductAttributeDefs] ([Name]);
END;");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[ProductAttributeOptions]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_ProductAttributeOptions_AttributeId' AND [object_id] = OBJECT_ID(N'[ProductAttributeOptions]'))
BEGIN
    CREATE INDEX [IX_ProductAttributeOptions_AttributeId] ON [ProductAttributeOptions] ([AttributeId]);
END;");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[ProductAttributeValues]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_ProductAttributeValues_ProductId_AttributeId' AND [object_id] = OBJECT_ID(N'[ProductAttributeValues]'))
BEGIN
    CREATE UNIQUE INDEX [IX_ProductAttributeValues_ProductId_AttributeId] ON [ProductAttributeValues] ([ProductId], [AttributeId]);
END;");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[WorkCalendarDays]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_WorkCalendarDays_Date' AND [object_id] = OBJECT_ID(N'[WorkCalendarDays]'))
BEGIN
    CREATE UNIQUE INDEX [IX_WorkCalendarDays_Date] ON [WorkCalendarDays] ([Date]);
END;");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[WorkCalendarSettings]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_WorkCalendarSettings_Id' AND [object_id] = OBJECT_ID(N'[WorkCalendarSettings]'))
BEGIN
    CREATE UNIQUE INDEX [IX_WorkCalendarSettings_Id] ON [WorkCalendarSettings] ([Id]);
END;");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[FK_OutgoingLetterSigners_OutgoingLetters_OutgoingLetterId]', N'F') IS NULL
BEGIN
    ALTER TABLE [OutgoingLetterSigners] ADD CONSTRAINT [FK_OutgoingLetterSigners_OutgoingLetters_OutgoingLetterId] FOREIGN KEY ([OutgoingLetterId]) REFERENCES [OutgoingLetters] ([Id]);
END;");
        

// ===== 20260905212404_AddAccountingModule.cs =====
        migrationBuilder.Sql(@"
IF OBJECT_ID(N'[AccAccounts]', N'U') IS NULL
BEGIN
    CREATE TABLE [AccAccounts] (
        [Id] int NOT NULL IDENTITY,
        [Code] nvarchar(30) NOT NULL,
        [Name] nvarchar(180) NOT NULL,
        [EnName] nvarchar(180) NULL,
        [ParentId] int NULL,
        [Level] int NOT NULL,
        [Type] int NOT NULL,
        [Nature] int NOT NULL,
        [IsPermanent] bit NOT NULL,
        [IsPostable] bit NOT NULL,
        [RequiresParty] bit NOT NULL,
        [IsSystem] bit NOT NULL,
        [IsActive] bit NOT NULL,
        [SortOrder] int NOT NULL,
        [Description] nvarchar(500) NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_AccAccounts] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AccAccounts_AccAccounts_ParentId] FOREIGN KEY ([ParentId]) REFERENCES [AccAccounts] ([Id]) ON DELETE NO ACTION
    );
END

IF OBJECT_ID(N'[AccFiscalYears]', N'U') IS NULL
BEGIN
    CREATE TABLE [AccFiscalYears] (
        [Id] int NOT NULL IDENTITY,
        [Title] nvarchar(120) NOT NULL,
        [Code] nvarchar(20) NULL,
        [StartDate] datetime2 NOT NULL,
        [EndDate] datetime2 NOT NULL,
        [IsCurrent] bit NOT NULL,
        [IsClosed] bit NOT NULL,
        [Description] nvarchar(500) NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_AccFiscalYears] PRIMARY KEY ([Id])
    );
END

IF OBJECT_ID(N'[AccInvRules]', N'U') IS NULL
BEGIN
    CREATE TABLE [AccInvRules] (
        [Id] int NOT NULL IDENTITY,
        [DocTypeId] int NOT NULL,
        [InventoryAccountId] int NULL,
        [CounterAccountId] int NULL,
        [UseCostValue] bit NOT NULL,
        [IsActive] bit NOT NULL,
        [Description] nvarchar(500) NULL,
        CONSTRAINT [PK_AccInvRules] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AccInvRules_InvDocTypes_DocTypeId] FOREIGN KEY ([DocTypeId]) REFERENCES [InvDocTypes] ([Id]) ON DELETE CASCADE
    );
END

IF OBJECT_ID(N'[AccVouchers]', N'U') IS NULL
BEGIN
    CREATE TABLE [AccVouchers] (
        [Id] int NOT NULL IDENTITY,
        [Number] int NOT NULL,
        [RefNumber] nvarchar(60) NULL,
        [FiscalYearId] int NOT NULL,
        [Date] datetime2 NOT NULL,
        [Description] nvarchar(600) NULL,
        [Status] int NOT NULL,
        [Source] int NOT NULL,
        [SourceId] int NULL,
        [SourceTitle] nvarchar(200) NULL,
        [TotalDebit] decimal(18,2) NOT NULL,
        [TotalCredit] decimal(18,2) NOT NULL,
        [CreatedBy] nvarchar(120) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [ConfirmedBy] nvarchar(120) NULL,
        [ConfirmedAt] datetime2 NULL,
        CONSTRAINT [PK_AccVouchers] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AccVouchers_AccFiscalYears_FiscalYearId] FOREIGN KEY ([FiscalYearId]) REFERENCES [AccFiscalYears] ([Id]) ON DELETE NO ACTION
    );
END

IF OBJECT_ID(N'[AccVoucherLines]', N'U') IS NULL
BEGIN
    CREATE TABLE [AccVoucherLines] (
        [Id] int NOT NULL IDENTITY,
        [VoucherId] int NOT NULL,
        [RowNo] int NOT NULL,
        [AccountId] int NOT NULL,
        [PartyId] int NULL,
        [ProjectId] int NULL,
        [Description] nvarchar(600) NULL,
        [RefNumber] nvarchar(60) NULL,
        [Debit] decimal(18,2) NOT NULL,
        [Credit] decimal(18,2) NOT NULL,
        CONSTRAINT [PK_AccVoucherLines] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AccVoucherLines_AccAccounts_AccountId] FOREIGN KEY ([AccountId]) REFERENCES [AccAccounts] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_AccVoucherLines_AccVouchers_VoucherId] FOREIGN KEY ([VoucherId]) REFERENCES [AccVouchers] ([Id]) ON DELETE CASCADE
    );
END

IF OBJECT_ID(N'[AccAccounts]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_AccAccounts_Code' AND [object_id] = OBJECT_ID(N'[AccAccounts]'))
BEGIN
    CREATE UNIQUE INDEX [IX_AccAccounts_Code] ON [AccAccounts] ([Code]);
END

IF OBJECT_ID(N'[AccAccounts]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_AccAccounts_Level' AND [object_id] = OBJECT_ID(N'[AccAccounts]'))
BEGIN
    CREATE INDEX [IX_AccAccounts_Level] ON [AccAccounts] ([Level]);
END

IF OBJECT_ID(N'[AccAccounts]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_AccAccounts_ParentId' AND [object_id] = OBJECT_ID(N'[AccAccounts]'))
BEGIN
    CREATE INDEX [IX_AccAccounts_ParentId] ON [AccAccounts] ([ParentId]);
END

IF OBJECT_ID(N'[AccFiscalYears]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_AccFiscalYears_IsCurrent' AND [object_id] = OBJECT_ID(N'[AccFiscalYears]'))
BEGIN
    CREATE INDEX [IX_AccFiscalYears_IsCurrent] ON [AccFiscalYears] ([IsCurrent]);
END

IF OBJECT_ID(N'[AccFiscalYears]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_AccFiscalYears_Title' AND [object_id] = OBJECT_ID(N'[AccFiscalYears]'))
BEGIN
    CREATE UNIQUE INDEX [IX_AccFiscalYears_Title] ON [AccFiscalYears] ([Title]);
END

IF OBJECT_ID(N'[AccInvRules]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_AccInvRules_DocTypeId' AND [object_id] = OBJECT_ID(N'[AccInvRules]'))
BEGIN
    CREATE UNIQUE INDEX [IX_AccInvRules_DocTypeId] ON [AccInvRules] ([DocTypeId]);
END

IF OBJECT_ID(N'[AccVoucherLines]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_AccVoucherLines_AccountId' AND [object_id] = OBJECT_ID(N'[AccVoucherLines]'))
BEGIN
    CREATE INDEX [IX_AccVoucherLines_AccountId] ON [AccVoucherLines] ([AccountId]);
END

IF OBJECT_ID(N'[AccVoucherLines]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_AccVoucherLines_PartyId' AND [object_id] = OBJECT_ID(N'[AccVoucherLines]'))
BEGIN
    CREATE INDEX [IX_AccVoucherLines_PartyId] ON [AccVoucherLines] ([PartyId]);
END

IF OBJECT_ID(N'[AccVoucherLines]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_AccVoucherLines_VoucherId' AND [object_id] = OBJECT_ID(N'[AccVoucherLines]'))
BEGIN
    CREATE INDEX [IX_AccVoucherLines_VoucherId] ON [AccVoucherLines] ([VoucherId]);
END

IF OBJECT_ID(N'[AccVouchers]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_AccVouchers_Date' AND [object_id] = OBJECT_ID(N'[AccVouchers]'))
BEGIN
    CREATE INDEX [IX_AccVouchers_Date] ON [AccVouchers] ([Date]);
END

IF OBJECT_ID(N'[AccVouchers]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_AccVouchers_FiscalYearId_Number' AND [object_id] = OBJECT_ID(N'[AccVouchers]'))
BEGIN
    CREATE UNIQUE INDEX [IX_AccVouchers_FiscalYearId_Number] ON [AccVouchers] ([FiscalYearId], [Number]);
END

IF OBJECT_ID(N'[AccVouchers]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_AccVouchers_Source_SourceId' AND [object_id] = OBJECT_ID(N'[AccVouchers]'))
BEGIN
    CREATE INDEX [IX_AccVouchers_Source_SourceId] ON [AccVouchers] ([Source], [SourceId]);
END

IF OBJECT_ID(N'[AccVouchers]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_AccVouchers_Status' AND [object_id] = OBJECT_ID(N'[AccVouchers]'))
BEGIN
    CREATE INDEX [IX_AccVouchers_Status] ON [AccVouchers] ([Status]);
END
");
    

// ===== 20260906033059_AddInvoicingModule.cs =====
        migrationBuilder.Sql(@"
IF OBJECT_ID(N'[FacInvoices]', N'U') IS NULL
BEGIN
    CREATE TABLE [FacInvoices] (
        [Id] int NOT NULL IDENTITY,
        [Number] int NOT NULL,
        [RefNumber] nvarchar(60) NULL,
        [Kind] int NOT NULL,
        [Date] datetime2 NOT NULL,
        [DueDate] datetime2 NULL,
        [PartyId] int NULL,
        [WarehouseId] int NOT NULL,
        [Settlement] int NOT NULL,
        [Description] nvarchar(600) NULL,
        [Status] int NOT NULL,
        [TotalGross] decimal(18,2) NOT NULL,
        [TotalLineDiscount] decimal(18,2) NOT NULL,
        [InvoiceDiscount] decimal(18,2) NOT NULL,
        [TotalTaxable] decimal(18,2) NOT NULL,
        [TotalVat] decimal(18,2) NOT NULL,
        [ShippingCost] decimal(18,2) NOT NULL,
        [TotalNet] decimal(18,2) NOT NULL,
        [InvDocId] int NULL,
        [VoucherId] int NULL,
        [CreatedBy] nvarchar(120) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [ConfirmedBy] nvarchar(120) NULL,
        [ConfirmedAt] datetime2 NULL,
        CONSTRAINT [PK_FacInvoices] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_FacInvoices_Parties_PartyId] FOREIGN KEY ([PartyId]) REFERENCES [Parties] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_FacInvoices_Warehouses_WarehouseId] FOREIGN KEY ([WarehouseId]) REFERENCES [Warehouses] ([Id]) ON DELETE NO ACTION
    );
END

IF OBJECT_ID(N'[FacRules]', N'U') IS NULL
BEGIN
    CREATE TABLE [FacRules] (
        [Id] int NOT NULL IDENTITY,
        [Kind] int NOT NULL,
        [DocTypeId] int NULL,
        [PartyAccountId] int NULL,
        [MainAccountId] int NULL,
        [VatAccountId] int NULL,
        [CashAccountId] int NULL,
        [ShippingAccountId] int NULL,
        [AutoInvDoc] bit NOT NULL,
        [AutoVoucher] bit NOT NULL,
        [IsActive] bit NOT NULL,
        [Description] nvarchar(500) NULL,
        CONSTRAINT [PK_FacRules] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_FacRules_InvDocTypes_DocTypeId] FOREIGN KEY ([DocTypeId]) REFERENCES [InvDocTypes] ([Id]) ON DELETE SET NULL
    );
END

IF OBJECT_ID(N'[FacInvoiceLines]', N'U') IS NULL
BEGIN
    CREATE TABLE [FacInvoiceLines] (
        [Id] int NOT NULL IDENTITY,
        [InvoiceId] int NOT NULL,
        [RowNo] int NOT NULL,
        [ProductId] int NOT NULL,
        [TaxCode] nvarchar(40) NULL,
        [Quantity] decimal(18,2) NOT NULL,
        [UnitPrice] decimal(18,2) NOT NULL,
        [DiscountPercent] decimal(18,2) NOT NULL,
        [Discount] decimal(18,2) NOT NULL,
        [VatRate] decimal(18,2) NOT NULL,
        [VatAmount] decimal(18,2) NOT NULL,
        [Taxable] decimal(18,2) NOT NULL,
        [Total] decimal(18,2) NOT NULL,
        [Description] nvarchar(400) NULL,
        CONSTRAINT [PK_FacInvoiceLines] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_FacInvoiceLines_FacInvoices_InvoiceId] FOREIGN KEY ([InvoiceId]) REFERENCES [FacInvoices] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_FacInvoiceLines_Products_ProductId] FOREIGN KEY ([ProductId]) REFERENCES [Products] ([Id]) ON DELETE NO ACTION
    );
END

IF OBJECT_ID(N'[FacInvoiceLines]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_FacInvoiceLines_InvoiceId' AND [object_id] = OBJECT_ID(N'[FacInvoiceLines]'))
BEGIN
    CREATE INDEX [IX_FacInvoiceLines_InvoiceId] ON [FacInvoiceLines] ([InvoiceId]);
END

IF OBJECT_ID(N'[FacInvoiceLines]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_FacInvoiceLines_ProductId' AND [object_id] = OBJECT_ID(N'[FacInvoiceLines]'))
BEGIN
    CREATE INDEX [IX_FacInvoiceLines_ProductId] ON [FacInvoiceLines] ([ProductId]);
END

IF OBJECT_ID(N'[FacInvoices]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_FacInvoices_Date' AND [object_id] = OBJECT_ID(N'[FacInvoices]'))
BEGIN
    CREATE INDEX [IX_FacInvoices_Date] ON [FacInvoices] ([Date]);
END

IF OBJECT_ID(N'[FacInvoices]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_FacInvoices_InvDocId' AND [object_id] = OBJECT_ID(N'[FacInvoices]'))
BEGIN
    CREATE INDEX [IX_FacInvoices_InvDocId] ON [FacInvoices] ([InvDocId]);
END

IF OBJECT_ID(N'[FacInvoices]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_FacInvoices_Kind_Number' AND [object_id] = OBJECT_ID(N'[FacInvoices]'))
BEGIN
    CREATE UNIQUE INDEX [IX_FacInvoices_Kind_Number] ON [FacInvoices] ([Kind], [Number]);
END

IF OBJECT_ID(N'[FacInvoices]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_FacInvoices_PartyId' AND [object_id] = OBJECT_ID(N'[FacInvoices]'))
BEGIN
    CREATE INDEX [IX_FacInvoices_PartyId] ON [FacInvoices] ([PartyId]);
END

IF OBJECT_ID(N'[FacInvoices]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_FacInvoices_Status' AND [object_id] = OBJECT_ID(N'[FacInvoices]'))
BEGIN
    CREATE INDEX [IX_FacInvoices_Status] ON [FacInvoices] ([Status]);
END

IF OBJECT_ID(N'[FacInvoices]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_FacInvoices_VoucherId' AND [object_id] = OBJECT_ID(N'[FacInvoices]'))
BEGIN
    CREATE INDEX [IX_FacInvoices_VoucherId] ON [FacInvoices] ([VoucherId]);
END

IF OBJECT_ID(N'[FacInvoices]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_FacInvoices_WarehouseId' AND [object_id] = OBJECT_ID(N'[FacInvoices]'))
BEGIN
    CREATE INDEX [IX_FacInvoices_WarehouseId] ON [FacInvoices] ([WarehouseId]);
END

IF OBJECT_ID(N'[FacRules]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_FacRules_DocTypeId' AND [object_id] = OBJECT_ID(N'[FacRules]'))
BEGIN
    CREATE INDEX [IX_FacRules_DocTypeId] ON [FacRules] ([DocTypeId]);
END

IF OBJECT_ID(N'[FacRules]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_FacRules_Kind' AND [object_id] = OBJECT_ID(N'[FacRules]'))
BEGIN
    CREATE UNIQUE INDEX [IX_FacRules_Kind] ON [FacRules] ([Kind]);
END
");
    

// ===== 20260906040427_AddTreasuryModule.cs =====
            migrationBuilder.Sql(@"IF OBJECT_ID(N'[TrsAccounts]', N'U') IS NULL
BEGIN
CREATE TABLE [TrsAccounts] (
    [Id] int NOT NULL IDENTITY,
    [Code] nvarchar(40) NOT NULL,
    [Name] nvarchar(200) NOT NULL,
    [Kind] int NOT NULL,
    [AccountId] int NULL,
    [BankName] nvarchar(120) NULL,
    [BranchName] nvarchar(120) NULL,
    [BranchCode] nvarchar(30) NULL,
    [AccountNumber] nvarchar(40) NULL,
    [Iban] nvarchar(34) NULL,
    [CardNumber] nvarchar(20) NULL,
    [OpeningBalance] decimal(18,2) NOT NULL,
    [IsDefault] bit NOT NULL,
    [IsActive] bit NOT NULL,
    [Description] nvarchar(500) NULL,
    [SortOrder] int NOT NULL,
    CONSTRAINT [PK_TrsAccounts] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_TrsAccounts_AccAccounts_AccountId] FOREIGN KEY ([AccountId]) REFERENCES [AccAccounts] ([Id]) ON DELETE SET NULL
);
END;

IF OBJECT_ID(N'[TrsRules]', N'U') IS NULL
BEGIN
CREATE TABLE [TrsRules] (
    [Id] int NOT NULL IDENTITY,
    [Kind] int NOT NULL,
    [PartyAccountId] int NULL,
    [ChequeAccountId] int NULL,
    [CollectionAccountId] int NULL,
    [DiscountAccountId] int NULL,
    [FeeAccountId] int NULL,
    [AutoVoucher] bit NOT NULL,
    [IsActive] bit NOT NULL,
    [Description] nvarchar(500) NULL,
    CONSTRAINT [PK_TrsRules] PRIMARY KEY ([Id])
);
END;

IF OBJECT_ID(N'[TrsCheques]', N'U') IS NULL
BEGIN
CREATE TABLE [TrsCheques] (
    [Id] int NOT NULL IDENTITY,
    [Kind] int NOT NULL,
    [Number] nvarchar(40) NOT NULL,
    [SayadId] nvarchar(20) NULL,
    [Amount] decimal(18,2) NOT NULL,
    [IssueDate] datetime2 NOT NULL,
    [DueDate] datetime2 NOT NULL,
    [BankName] nvarchar(120) NULL,
    [BranchName] nvarchar(120) NULL,
    [AccountNumber] nvarchar(40) NULL,
    [OwnerName] nvarchar(200) NULL,
    [PartyId] int NULL,
    [TrsAccountId] int NULL,
    [Status] int NOT NULL,
    [StatusDate] datetime2 NULL,
    [Description] nvarchar(500) NULL,
    [TrsVoucherId] int NULL,
    [CreatedBy] nvarchar(120) NULL,
    [CreatedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_TrsCheques] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_TrsCheques_Parties_PartyId] FOREIGN KEY ([PartyId]) REFERENCES [Parties] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_TrsCheques_TrsAccounts_TrsAccountId] FOREIGN KEY ([TrsAccountId]) REFERENCES [TrsAccounts] ([Id]) ON DELETE NO ACTION
);
END;

IF OBJECT_ID(N'[TrsVouchers]', N'U') IS NULL
BEGIN
CREATE TABLE [TrsVouchers] (
    [Id] int NOT NULL IDENTITY,
    [Number] int NOT NULL,
    [Kind] int NOT NULL,
    [Date] datetime2 NOT NULL,
    [PartyId] int NULL,
    [Description] nvarchar(600) NULL,
    [RefNumber] nvarchar(60) NULL,
    [Status] int NOT NULL,
    [FromAccountId] int NULL,
    [ToAccountId] int NULL,
    [FeeAmount] decimal(18,2) NOT NULL,
    [InvoiceId] int NULL,
    [TotalAmount] decimal(18,2) NOT NULL,
    [CashAmount] decimal(18,2) NOT NULL,
    [ChequeAmount] decimal(18,2) NOT NULL,
    [DiscountAmount] decimal(18,2) NOT NULL,
    [VoucherId] int NULL,
    [CreatedBy] nvarchar(120) NULL,
    [CreatedAt] datetime2 NOT NULL,
    [ConfirmedBy] nvarchar(120) NULL,
    [ConfirmedAt] datetime2 NULL,
    CONSTRAINT [PK_TrsVouchers] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_TrsVouchers_Parties_PartyId] FOREIGN KEY ([PartyId]) REFERENCES [Parties] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_TrsVouchers_TrsAccounts_FromAccountId] FOREIGN KEY ([FromAccountId]) REFERENCES [TrsAccounts] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_TrsVouchers_TrsAccounts_ToAccountId] FOREIGN KEY ([ToAccountId]) REFERENCES [TrsAccounts] ([Id]) ON DELETE NO ACTION
);
END;

IF OBJECT_ID(N'[TrsChequeActions]', N'U') IS NULL
BEGIN
CREATE TABLE [TrsChequeActions] (
    [Id] int NOT NULL IDENTITY,
    [ChequeId] int NOT NULL,
    [Status] int NOT NULL,
    [Date] datetime2 NOT NULL,
    [TrsAccountId] int NULL,
    [Description] nvarchar(500) NULL,
    [VoucherId] int NULL,
    [CreatedBy] nvarchar(120) NULL,
    [CreatedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_TrsChequeActions] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_TrsChequeActions_TrsCheques_ChequeId] FOREIGN KEY ([ChequeId]) REFERENCES [TrsCheques] ([Id]) ON DELETE CASCADE
);
END;

IF OBJECT_ID(N'[TrsVoucherLines]', N'U') IS NULL
BEGIN
CREATE TABLE [TrsVoucherLines] (
    [Id] int NOT NULL IDENTITY,
    [TrsVoucherId] int NOT NULL,
    [RowNo] int NOT NULL,
    [Method] int NOT NULL,
    [TrsAccountId] int NULL,
    [Amount] decimal(18,2) NOT NULL,
    [RefNumber] nvarchar(60) NULL,
    [Description] nvarchar(500) NULL,
    [ChequeId] int NULL,
    CONSTRAINT [PK_TrsVoucherLines] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_TrsVoucherLines_TrsAccounts_TrsAccountId] FOREIGN KEY ([TrsAccountId]) REFERENCES [TrsAccounts] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_TrsVoucherLines_TrsCheques_ChequeId] FOREIGN KEY ([ChequeId]) REFERENCES [TrsCheques] ([Id]) ON DELETE SET NULL,
    CONSTRAINT [FK_TrsVoucherLines_TrsVouchers_TrsVoucherId] FOREIGN KEY ([TrsVoucherId]) REFERENCES [TrsVouchers] ([Id]) ON DELETE CASCADE
);
END;

IF OBJECT_ID(N'[TrsAccounts]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_TrsAccounts_AccountId' AND [object_id] = OBJECT_ID(N'[TrsAccounts]'))
BEGIN
CREATE INDEX [IX_TrsAccounts_AccountId] ON [TrsAccounts] ([AccountId]);
END;

IF OBJECT_ID(N'[TrsAccounts]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_TrsAccounts_Code' AND [object_id] = OBJECT_ID(N'[TrsAccounts]'))
BEGIN
CREATE UNIQUE INDEX [IX_TrsAccounts_Code] ON [TrsAccounts] ([Code]);
END;

IF OBJECT_ID(N'[TrsAccounts]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_TrsAccounts_Kind' AND [object_id] = OBJECT_ID(N'[TrsAccounts]'))
BEGIN
CREATE INDEX [IX_TrsAccounts_Kind] ON [TrsAccounts] ([Kind]);
END;

IF OBJECT_ID(N'[TrsChequeActions]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_TrsChequeActions_ChequeId' AND [object_id] = OBJECT_ID(N'[TrsChequeActions]'))
BEGIN
CREATE INDEX [IX_TrsChequeActions_ChequeId] ON [TrsChequeActions] ([ChequeId]);
END;

IF OBJECT_ID(N'[TrsCheques]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_TrsCheques_DueDate' AND [object_id] = OBJECT_ID(N'[TrsCheques]'))
BEGIN
CREATE INDEX [IX_TrsCheques_DueDate] ON [TrsCheques] ([DueDate]);
END;

IF OBJECT_ID(N'[TrsCheques]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_TrsCheques_Kind_Number' AND [object_id] = OBJECT_ID(N'[TrsCheques]'))
BEGIN
CREATE INDEX [IX_TrsCheques_Kind_Number] ON [TrsCheques] ([Kind], [Number]);
END;

IF OBJECT_ID(N'[TrsCheques]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_TrsCheques_PartyId' AND [object_id] = OBJECT_ID(N'[TrsCheques]'))
BEGIN
CREATE INDEX [IX_TrsCheques_PartyId] ON [TrsCheques] ([PartyId]);
END;

IF OBJECT_ID(N'[TrsCheques]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_TrsCheques_Status' AND [object_id] = OBJECT_ID(N'[TrsCheques]'))
BEGIN
CREATE INDEX [IX_TrsCheques_Status] ON [TrsCheques] ([Status]);
END;

IF OBJECT_ID(N'[TrsCheques]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_TrsCheques_TrsAccountId' AND [object_id] = OBJECT_ID(N'[TrsCheques]'))
BEGIN
CREATE INDEX [IX_TrsCheques_TrsAccountId] ON [TrsCheques] ([TrsAccountId]);
END;

IF OBJECT_ID(N'[TrsCheques]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_TrsCheques_TrsVoucherId' AND [object_id] = OBJECT_ID(N'[TrsCheques]'))
BEGIN
CREATE INDEX [IX_TrsCheques_TrsVoucherId] ON [TrsCheques] ([TrsVoucherId]);
END;

IF OBJECT_ID(N'[TrsRules]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_TrsRules_Kind' AND [object_id] = OBJECT_ID(N'[TrsRules]'))
BEGIN
CREATE UNIQUE INDEX [IX_TrsRules_Kind] ON [TrsRules] ([Kind]);
END;

IF OBJECT_ID(N'[TrsVoucherLines]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_TrsVoucherLines_ChequeId' AND [object_id] = OBJECT_ID(N'[TrsVoucherLines]'))
BEGIN
CREATE INDEX [IX_TrsVoucherLines_ChequeId] ON [TrsVoucherLines] ([ChequeId]);
END;

IF OBJECT_ID(N'[TrsVoucherLines]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_TrsVoucherLines_TrsAccountId' AND [object_id] = OBJECT_ID(N'[TrsVoucherLines]'))
BEGIN
CREATE INDEX [IX_TrsVoucherLines_TrsAccountId] ON [TrsVoucherLines] ([TrsAccountId]);
END;

IF OBJECT_ID(N'[TrsVoucherLines]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_TrsVoucherLines_TrsVoucherId' AND [object_id] = OBJECT_ID(N'[TrsVoucherLines]'))
BEGIN
CREATE INDEX [IX_TrsVoucherLines_TrsVoucherId] ON [TrsVoucherLines] ([TrsVoucherId]);
END;

IF OBJECT_ID(N'[TrsVouchers]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_TrsVouchers_Date' AND [object_id] = OBJECT_ID(N'[TrsVouchers]'))
BEGIN
CREATE INDEX [IX_TrsVouchers_Date] ON [TrsVouchers] ([Date]);
END;

IF OBJECT_ID(N'[TrsVouchers]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_TrsVouchers_FromAccountId' AND [object_id] = OBJECT_ID(N'[TrsVouchers]'))
BEGIN
CREATE INDEX [IX_TrsVouchers_FromAccountId] ON [TrsVouchers] ([FromAccountId]);
END;

IF OBJECT_ID(N'[TrsVouchers]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_TrsVouchers_InvoiceId' AND [object_id] = OBJECT_ID(N'[TrsVouchers]'))
BEGIN
CREATE INDEX [IX_TrsVouchers_InvoiceId] ON [TrsVouchers] ([InvoiceId]);
END;

IF OBJECT_ID(N'[TrsVouchers]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_TrsVouchers_Kind_Number' AND [object_id] = OBJECT_ID(N'[TrsVouchers]'))
BEGIN
CREATE UNIQUE INDEX [IX_TrsVouchers_Kind_Number] ON [TrsVouchers] ([Kind], [Number]);
END;

IF OBJECT_ID(N'[TrsVouchers]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_TrsVouchers_PartyId' AND [object_id] = OBJECT_ID(N'[TrsVouchers]'))
BEGIN
CREATE INDEX [IX_TrsVouchers_PartyId] ON [TrsVouchers] ([PartyId]);
END;

IF OBJECT_ID(N'[TrsVouchers]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_TrsVouchers_Status' AND [object_id] = OBJECT_ID(N'[TrsVouchers]'))
BEGIN
CREATE INDEX [IX_TrsVouchers_Status] ON [TrsVouchers] ([Status]);
END;

IF OBJECT_ID(N'[TrsVouchers]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_TrsVouchers_ToAccountId' AND [object_id] = OBJECT_ID(N'[TrsVouchers]'))
BEGIN
CREATE INDEX [IX_TrsVouchers_ToAccountId] ON [TrsVouchers] ([ToAccountId]);
END;

IF OBJECT_ID(N'[TrsVouchers]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_TrsVouchers_VoucherId' AND [object_id] = OBJECT_ID(N'[TrsVouchers]'))
BEGIN
CREATE INDEX [IX_TrsVouchers_VoucherId] ON [TrsVouchers] ([VoucherId]);
END;");
        

// ===== 20260906044059_AddDocArchiveModule.cs =====
            migrationBuilder.CreateTable(
                name: "DocCartableTasks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Kind = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    DocumentId = table.Column<int>(type: "int", nullable: false),
                    VersionId = table.Column<int>(type: "int", nullable: true),
                    SourceDocumentId = table.Column<int>(type: "int", nullable: true),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DoneAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocCartableTasks", x => x.Id);
                });


            migrationBuilder.CreateTable(
                name: "DocFolderPermissions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FolderId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Level = table.Column<int>(type: "int", nullable: false),
                    CanDownload = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocFolderPermissions", x => x.Id);
                });


            migrationBuilder.CreateTable(
                name: "DocFolders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ParentId = table.Column<int>(type: "int", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsPublic = table.Column<bool>(type: "bit", nullable: false),
                    PublicCanDownload = table.Column<bool>(type: "bit", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false),
                    CreatedByName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocFolders", x => x.Id);
                });


            migrationBuilder.CreateTable(
                name: "DocumentApprovers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DocumentId = table.Column<int>(type: "int", nullable: false),
                    VersionId = table.Column<int>(type: "int", nullable: true),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    UserName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ActedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentApprovers", x => x.Id);
                });


            migrationBuilder.CreateTable(
                name: "DocumentLinks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DocumentId = table.Column<int>(type: "int", nullable: false),
                    LinkedDocumentId = table.Column<int>(type: "int", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentLinks", x => x.Id);
                });


            migrationBuilder.CreateTable(
                name: "DocumentLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DocumentId = table.Column<int>(type: "int", nullable: false),
                    VersionId = table.Column<int>(type: "int", nullable: true),
                    Action = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Detail = table.Column<string>(type: "nvarchar(600)", maxLength: 600, nullable: true),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    UserName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentLogs", x => x.Id);
                });


            migrationBuilder.CreateTable(
                name: "DocumentPermissions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DocumentId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Level = table.Column<int>(type: "int", nullable: false),
                    CanDownload = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentPermissions", x => x.Id);
                });


            migrationBuilder.CreateTable(
                name: "Documents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FolderId = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    Code = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ExpireDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AllowMultipleActiveVersions = table.Column<bool>(type: "bit", nullable: false),
                    IsPublic = table.Column<bool>(type: "bit", nullable: false),
                    PublicCanDownload = table.Column<bool>(type: "bit", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false),
                    CreatedByName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Documents", x => x.Id);
                });


            migrationBuilder.CreateTable(
                name: "DocumentVersions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DocumentId = table.Column<int>(type: "int", nullable: false),
                    VersionNo = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    ChangeNote = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ExpireDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsFrozen = table.Column<bool>(type: "bit", nullable: false),
                    ActivateOnApprove = table.Column<bool>(type: "bit", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false),
                    CreatedByName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ApprovedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentVersions", x => x.Id);
                });


            migrationBuilder.CreateIndex(
                name: "IX_DocCartableTasks_UserId_Status",
                table: "DocCartableTasks",
                columns: new[] { "UserId", "Status" });


            migrationBuilder.CreateIndex(
                name: "IX_DocFolderPermissions_FolderId_UserId",
                table: "DocFolderPermissions",
                columns: new[] { "FolderId", "UserId" },
                unique: true);


            migrationBuilder.CreateIndex(
                name: "IX_DocFolders_ParentId",
                table: "DocFolders",
                column: "ParentId");


            migrationBuilder.CreateIndex(
                name: "IX_DocumentApprovers_DocumentId_VersionId",
                table: "DocumentApprovers",
                columns: new[] { "DocumentId", "VersionId" });


            migrationBuilder.CreateIndex(
                name: "IX_DocumentLinks_DocumentId_LinkedDocumentId",
                table: "DocumentLinks",
                columns: new[] { "DocumentId", "LinkedDocumentId" },
                unique: true);


            migrationBuilder.CreateIndex(
                name: "IX_DocumentLogs_DocumentId",
                table: "DocumentLogs",
                column: "DocumentId");


            migrationBuilder.CreateIndex(
                name: "IX_DocumentPermissions_DocumentId_UserId",
                table: "DocumentPermissions",
                columns: new[] { "DocumentId", "UserId" },
                unique: true);


            migrationBuilder.CreateIndex(
                name: "IX_Documents_Code",
                table: "Documents",
                column: "Code",
                unique: true);


            migrationBuilder.CreateIndex(
                name: "IX_Documents_ExpireDate",
                table: "Documents",
                column: "ExpireDate");


            migrationBuilder.CreateIndex(
                name: "IX_Documents_FolderId",
                table: "Documents",
                column: "FolderId");


            migrationBuilder.CreateIndex(
                name: "IX_DocumentVersions_DocumentId_VersionNo",
                table: "DocumentVersions",
                columns: new[] { "DocumentId", "VersionNo" },
                unique: true);
        

// ===== 20260906050520_AddStocktakingModule.cs =====
            migrationBuilder.Sql(@"IF OBJECT_ID(N'[BcdBarcodes]', N'U') IS NULL
BEGIN
    CREATE TABLE [BcdBarcodes] (
        [Id] int NOT NULL IDENTITY,
        [ProductId] int NOT NULL,
        [Code] nvarchar(60) NOT NULL,
        [Type] int NOT NULL,
        [Unit] nvarchar(50) NULL,
        [PackQty] decimal(18,3) NOT NULL,
        [IsPrimary] bit NOT NULL,
        [IsActive] bit NOT NULL,
        [Description] nvarchar(300) NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_BcdBarcodes] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_BcdBarcodes_Products_ProductId] FOREIGN KEY ([ProductId]) REFERENCES [Products] ([Id]) ON DELETE CASCADE
    );
END;

IF OBJECT_ID(N'[StkSessions]', N'U') IS NULL
BEGIN
    CREATE TABLE [StkSessions] (
        [Id] int NOT NULL IDENTITY,
        [Number] int NOT NULL,
        [Title] nvarchar(200) NOT NULL,
        [WarehouseId] int NOT NULL,
        [Date] datetime2 NOT NULL,
        [Scope] int NOT NULL,
        [CategoryId] int NULL,
        [Status] int NOT NULL,
        [TreatUncountedAsZero] bit NOT NULL,
        [Description] nvarchar(600) NULL,
        [SurplusDocId] int NULL,
        [ShortageDocId] int NULL,
        [CreatedBy] nvarchar(120) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [AppliedBy] nvarchar(120) NULL,
        [AppliedAt] datetime2 NULL,
        CONSTRAINT [PK_StkSessions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_StkSessions_Warehouses_WarehouseId] FOREIGN KEY ([WarehouseId]) REFERENCES [Warehouses] ([Id]) ON DELETE NO ACTION
    );
END;

IF OBJECT_ID(N'[StkLines]', N'U') IS NULL
BEGIN
    CREATE TABLE [StkLines] (
        [Id] int NOT NULL IDENTITY,
        [SessionId] int NOT NULL,
        [RowNo] int NOT NULL,
        [ProductId] int NOT NULL,
        [SystemQty] decimal(18,3) NOT NULL,
        [CountedQty] decimal(18,3) NOT NULL,
        [IsCounted] bit NOT NULL,
        [UnitCost] decimal(18,2) NOT NULL,
        [Note] nvarchar(300) NULL,
        [CountedBy] nvarchar(120) NULL,
        [CountedAt] datetime2 NULL,
        [ScanCount] int NOT NULL,
        CONSTRAINT [PK_StkLines] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_StkLines_Products_ProductId] FOREIGN KEY ([ProductId]) REFERENCES [Products] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_StkLines_StkSessions_SessionId] FOREIGN KEY ([SessionId]) REFERENCES [StkSessions] ([Id]) ON DELETE CASCADE
    );
END;

IF OBJECT_ID(N'[StkScans]', N'U') IS NULL
BEGIN
    CREATE TABLE [StkScans] (
        [Id] bigint NOT NULL IDENTITY,
        [SessionId] int NOT NULL,
        [LineId] int NOT NULL,
        [ProductId] int NOT NULL,
        [Barcode] nvarchar(60) NULL,
        [Quantity] decimal(18,3) NOT NULL,
        [Accumulated] bit NOT NULL,
        [ScannedBy] nvarchar(120) NULL,
        [ScannedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_StkScans] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_StkScans_StkSessions_SessionId] FOREIGN KEY ([SessionId]) REFERENCES [StkSessions] ([Id]) ON DELETE CASCADE
    );
END;

IF OBJECT_ID(N'[BcdBarcodes]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_BcdBarcodes_Code' AND [object_id] = OBJECT_ID(N'[BcdBarcodes]'))
    CREATE UNIQUE INDEX [IX_BcdBarcodes_Code] ON [BcdBarcodes] ([Code]);

IF OBJECT_ID(N'[BcdBarcodes]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_BcdBarcodes_ProductId' AND [object_id] = OBJECT_ID(N'[BcdBarcodes]'))
    CREATE INDEX [IX_BcdBarcodes_ProductId] ON [BcdBarcodes] ([ProductId]);

IF OBJECT_ID(N'[StkLines]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_StkLines_ProductId' AND [object_id] = OBJECT_ID(N'[StkLines]'))
    CREATE INDEX [IX_StkLines_ProductId] ON [StkLines] ([ProductId]);

IF OBJECT_ID(N'[StkLines]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_StkLines_SessionId' AND [object_id] = OBJECT_ID(N'[StkLines]'))
    CREATE INDEX [IX_StkLines_SessionId] ON [StkLines] ([SessionId]);

IF OBJECT_ID(N'[StkLines]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_StkLines_SessionId_ProductId' AND [object_id] = OBJECT_ID(N'[StkLines]'))
    CREATE UNIQUE INDEX [IX_StkLines_SessionId_ProductId] ON [StkLines] ([SessionId], [ProductId]);

IF OBJECT_ID(N'[StkScans]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_StkScans_LineId' AND [object_id] = OBJECT_ID(N'[StkScans]'))
    CREATE INDEX [IX_StkScans_LineId] ON [StkScans] ([LineId]);

IF OBJECT_ID(N'[StkScans]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_StkScans_SessionId' AND [object_id] = OBJECT_ID(N'[StkScans]'))
    CREATE INDEX [IX_StkScans_SessionId] ON [StkScans] ([SessionId]);

IF OBJECT_ID(N'[StkSessions]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_StkSessions_Date' AND [object_id] = OBJECT_ID(N'[StkSessions]'))
    CREATE INDEX [IX_StkSessions_Date] ON [StkSessions] ([Date]);

IF OBJECT_ID(N'[StkSessions]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_StkSessions_Number' AND [object_id] = OBJECT_ID(N'[StkSessions]'))
    CREATE UNIQUE INDEX [IX_StkSessions_Number] ON [StkSessions] ([Number]);

IF OBJECT_ID(N'[StkSessions]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_StkSessions_Status' AND [object_id] = OBJECT_ID(N'[StkSessions]'))
    CREATE INDEX [IX_StkSessions_Status] ON [StkSessions] ([Status]);

IF OBJECT_ID(N'[StkSessions]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_StkSessions_WarehouseId' AND [object_id] = OBJECT_ID(N'[StkSessions]'))
    CREATE INDEX [IX_StkSessions_WarehouseId] ON [StkSessions] ([WarehouseId]);");
        

// ===== 20260906055526_AddDocCustomerCodeAndStatus.cs =====
            migrationBuilder.AddColumn<string>(
                name: "CustomerCode",
                table: "Documents",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeactivateReason",
                table: "Documents",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeactivatedAt",
                table: "Documents",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeactivatedByName",
                table: "Documents",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "Documents",
                type: "bit",
                nullable: false,
                defaultValue: false);
        

// ===== 20260906070735_AddDocExpiryAlerts.cs =====
            migrationBuilder.CreateTable(
                name: "DocExpiryAlerts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DocumentId = table.Column<int>(type: "int", nullable: false),
                    ThresholdDays = table.Column<int>(type: "int", nullable: false),
                    ExpireDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SentAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    NotifiedCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocExpiryAlerts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocExpiryAlerts_DocumentId_ThresholdDays_ExpireDate",
                table: "DocExpiryAlerts",
                columns: new[] { "DocumentId", "ThresholdDays", "ExpireDate" },
                unique: true);
        

// ===== 20260906072405_AddDocSoftDeleteInfo.cs =====
            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "Documents",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedByName",
                table: "Documents",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);
        

// ===== 20260906093024_AddAnalyticalAccounting_FixedAssets_Budgets.cs =====
            migrationBuilder.AddColumn<int>(
                name: "DimensionValueId",
                table: "AccVoucherLines",
                type: "int",
                nullable: true);
            migrationBuilder.CreateTable(
                name: "AccDimensions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(180)", maxLength: 180, nullable: false),
                    IsSystem = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccDimensions", x => x.Id);
                });
            migrationBuilder.CreateTable(
                name: "FixedAssetCategories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(180)", maxLength: 180, nullable: false),
                    DefaultUsefulLifeMonths = table.Column<int>(type: "int", nullable: false),
                    DefaultMethod = table.Column<int>(type: "int", nullable: false),
                    DefaultResidualPercent = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    DepreciationAccAccountId = table.Column<int>(type: "int", nullable: true),
                    ExpenseAccAccountId = table.Column<int>(type: "int", nullable: true),
                    AccumulatedAccAccountId = table.Column<int>(type: "int", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FixedAssetCategories", x => x.Id);
                });
            migrationBuilder.CreateTable(
                name: "FixedAssetDepreciationRuns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Month = table.Column<int>(type: "int", nullable: false),
                    RunDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    VoucherId = table.Column<int>(type: "int", nullable: true),
                    IsPosted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FixedAssetDepreciationRuns", x => x.Id);
                });
            migrationBuilder.CreateTable(
                name: "AccDimensionValues",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DimensionId = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(180)", maxLength: 180, nullable: false),
                    ParentId = table.Column<int>(type: "int", nullable: true),
                    CodeTree = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    AccDimensionId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccDimensionValues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccDimensionValues_AccDimensionValues_ParentId",
                        column: x => x.ParentId,
                        principalTable: "AccDimensionValues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccDimensionValues_AccDimensions_AccDimensionId",
                        column: x => x.AccDimensionId,
                        principalTable: "AccDimensions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AccDimensionValues_AccDimensions_DimensionId",
                        column: x => x.DimensionId,
                        principalTable: "AccDimensions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });
            migrationBuilder.CreateTable(
                name: "Budgets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FiscalYearId = table.Column<int>(type: "int", nullable: false),
                    DimensionValueId = table.Column<int>(type: "int", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(180)", maxLength: 180, nullable: false),
                    TotalAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    IsMaster = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Budgets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Budgets_AccDimensionValues_DimensionValueId",
                        column: x => x.DimensionValueId,
                        principalTable: "AccDimensionValues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Budgets_AccFiscalYears_FiscalYearId",
                        column: x => x.FiscalYearId,
                        principalTable: "AccFiscalYears",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });
            migrationBuilder.CreateTable(
                name: "FixedAssets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(180)", maxLength: 180, nullable: false),
                    EnName = table.Column<string>(type: "nvarchar(180)", maxLength: 180, nullable: true),
                    CategoryId = table.Column<int>(type: "int", nullable: false),
                    Location = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Vendor = table.Column<string>(type: "nvarchar(180)", maxLength: 180, nullable: true),
                    SerialNo = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    PurchaseDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PurchasePrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    SalvageValue = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    UsefulLifeMonths = table.Column<int>(type: "int", nullable: false),
                    DepreciationMethod = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    DimensionValueId = table.Column<int>(type: "int", nullable: true),
                    AssetAccAccountId = table.Column<int>(type: "int", nullable: true),
                    ExpenseAccAccountId = table.Column<int>(type: "int", nullable: true),
                    AccumulatedAccAccountId = table.Column<int>(type: "int", nullable: true),
                    AccumulatedDepreciation = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FixedAssets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FixedAssets_AccDimensionValues_DimensionValueId",
                        column: x => x.DimensionValueId,
                        principalTable: "AccDimensionValues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_FixedAssets_FixedAssetCategories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "FixedAssetCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });
            migrationBuilder.CreateTable(
                name: "BudgetItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BudgetId = table.Column<int>(type: "int", nullable: false),
                    AccAccountId = table.Column<int>(type: "int", nullable: false),
                    PlannedAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CommittedAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ActualAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BudgetItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BudgetItems_AccAccounts_AccAccountId",
                        column: x => x.AccAccountId,
                        principalTable: "AccAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BudgetItems_Budgets_BudgetId",
                        column: x => x.BudgetId,
                        principalTable: "Budgets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });
            migrationBuilder.CreateTable(
                name: "FixedAssetDepreciationLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RunId = table.Column<int>(type: "int", nullable: false),
                    AssetId = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    AccumulatedAfter = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    BookValueAfter = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FixedAssetDepreciationLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FixedAssetDepreciationLines_FixedAssetDepreciationRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "FixedAssetDepreciationRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FixedAssetDepreciationLines_FixedAssets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "FixedAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });
            migrationBuilder.CreateTable(
                name: "BudgetTransactions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BudgetId = table.Column<int>(type: "int", nullable: false),
                    BudgetItemId = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    VoucherId = table.Column<int>(type: "int", nullable: true),
                    SourceId = table.Column<int>(type: "int", nullable: true),
                    SourceTitle = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BudgetTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BudgetTransactions_BudgetItems_BudgetItemId",
                        column: x => x.BudgetItemId,
                        principalTable: "BudgetItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BudgetTransactions_Budgets_BudgetId",
                        column: x => x.BudgetId,
                        principalTable: "Budgets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });
            migrationBuilder.CreateIndex(
                name: "IX_AccVoucherLines_DimensionValueId",
                table: "AccVoucherLines",
                column: "DimensionValueId");
            migrationBuilder.CreateIndex(
                name: "IX_AccDimensions_Code",
                table: "AccDimensions",
                column: "Code",
                unique: true);
            migrationBuilder.CreateIndex(
                name: "IX_AccDimensionValues_AccDimensionId",
                table: "AccDimensionValues",
                column: "AccDimensionId");
            migrationBuilder.CreateIndex(
                name: "IX_AccDimensionValues_CodeTree",
                table: "AccDimensionValues",
                column: "CodeTree");
            migrationBuilder.CreateIndex(
                name: "IX_AccDimensionValues_DimensionId_Code",
                table: "AccDimensionValues",
                columns: new[] { "DimensionId", "Code" },
                unique: true);
            migrationBuilder.CreateIndex(
                name: "IX_AccDimensionValues_ParentId",
                table: "AccDimensionValues",
                column: "ParentId");
            migrationBuilder.CreateIndex(
                name: "IX_BudgetItems_AccAccountId",
                table: "BudgetItems",
                column: "AccAccountId");
            migrationBuilder.CreateIndex(
                name: "IX_BudgetItems_BudgetId_AccAccountId",
                table: "BudgetItems",
                columns: new[] { "BudgetId", "AccAccountId" },
                unique: true);
            migrationBuilder.CreateIndex(
                name: "IX_Budgets_DimensionValueId",
                table: "Budgets",
                column: "DimensionValueId");
            migrationBuilder.CreateIndex(
                name: "IX_Budgets_FiscalYearId_DimensionValueId_Name",
                table: "Budgets",
                columns: new[] { "FiscalYearId", "DimensionValueId", "Name" },
                unique: true,
                filter: "[DimensionValueId] IS NOT NULL");
            migrationBuilder.CreateIndex(
                name: "IX_BudgetTransactions_BudgetId",
                table: "BudgetTransactions",
                column: "BudgetId");
            migrationBuilder.CreateIndex(
                name: "IX_BudgetTransactions_BudgetItemId",
                table: "BudgetTransactions",
                column: "BudgetItemId");
            migrationBuilder.CreateIndex(
                name: "IX_BudgetTransactions_VoucherId",
                table: "BudgetTransactions",
                column: "VoucherId");
            migrationBuilder.CreateIndex(
                name: "IX_FixedAssetCategories_Code",
                table: "FixedAssetCategories",
                column: "Code",
                unique: true);
            migrationBuilder.CreateIndex(
                name: "IX_FixedAssetDepreciationLines_AssetId",
                table: "FixedAssetDepreciationLines",
                column: "AssetId");
            migrationBuilder.CreateIndex(
                name: "IX_FixedAssetDepreciationLines_RunId_AssetId",
                table: "FixedAssetDepreciationLines",
                columns: new[] { "RunId", "AssetId" },
                unique: true);
            migrationBuilder.CreateIndex(
                name: "IX_FixedAssetDepreciationRuns_Year_Month",
                table: "FixedAssetDepreciationRuns",
                columns: new[] { "Year", "Month" },
                unique: true);
            migrationBuilder.CreateIndex(
                name: "IX_FixedAssets_CategoryId",
                table: "FixedAssets",
                column: "CategoryId");
            migrationBuilder.CreateIndex(
                name: "IX_FixedAssets_Code",
                table: "FixedAssets",
                column: "Code",
                unique: true);
            migrationBuilder.CreateIndex(
                name: "IX_FixedAssets_DimensionValueId",
                table: "FixedAssets",
                column: "DimensionValueId");
            migrationBuilder.AddForeignKey(
                name: "FK_AccVoucherLines_AccDimensionValues_DimensionValueId",
                table: "AccVoucherLines",
                column: "DimensionValueId",
                principalTable: "AccDimensionValues",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        

// ===== 20260906093500_AddMoadianFiscalSystem.cs =====
            migrationBuilder.CreateTable(
                name: "FiscalPrinterSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PrinterName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PortName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    PaperWidthMm = table.Column<int>(type: "int", nullable: false),
                    Copies = table.Column<int>(type: "int", nullable: false),
                    HeaderLines = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    FooterLines = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CutPaper = table.Column<bool>(type: "bit", nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FiscalPrinterSettings", x => x.Id);
                });
            migrationBuilder.CreateTable(
                name: "MoadianCpcList",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    EnTitle = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MoadianCpcList", x => x.Id);
                });
            migrationBuilder.CreateTable(
                name: "MoadianFiscalPeriods",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Month = table.Column<int>(type: "int", nullable: false),
                    IsClosed = table.Column<bool>(type: "bit", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MoadianFiscalPeriods", x => x.Id);
                });
            migrationBuilder.CreateTable(
                name: "MoadianSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TaxId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    EconomicCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    SellerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SellerAddress = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    SellerPostalCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    SellerPhone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    TaxCardToken = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    BaseUrl = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    PrivateKeyPem = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PublicKeyPem = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AutoSend = table.Column<bool>(type: "bit", nullable: false),
                    SendIntervalMinutes = table.Column<int>(type: "int", nullable: false),
                    DefaultVatRate = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    LoggingEnabled = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MoadianSettings", x => x.Id);
                });
            migrationBuilder.CreateTable(
                name: "MoadianInvoices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FiscalPeriodId = table.Column<int>(type: "int", nullable: false),
                    FacInvoiceId = table.Column<int>(type: "int", nullable: true),
                    FacInvoiceRef = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    Settlement = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    TaxId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SellerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    EconomicCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    BuyerTaxId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    BuyerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    BuyerAddress = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    BuyerPostalCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    BuyerPhone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    TotalGross = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalDiscount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalTaxable = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalVat = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalNet = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ReferenceId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    TrackingId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ErrorCode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    QueuedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SendAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReturnedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(600)", maxLength: 600, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MoadianInvoices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MoadianInvoices_MoadianFiscalPeriods_FiscalPeriodId",
                        column: x => x.FiscalPeriodId,
                        principalTable: "MoadianFiscalPeriods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });
            migrationBuilder.CreateTable(
                name: "MoadianInvoiceLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InvoiceId = table.Column<int>(type: "int", nullable: false),
                    RowNo = table.Column<int>(type: "int", nullable: false),
                    SstId = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    SstTitle = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Discount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    VatRate = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    VatAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Total = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MoadianInvoiceLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MoadianInvoiceLines_MoadianInvoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "MoadianInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
            migrationBuilder.CreateTable(
                name: "MoadianLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InvoiceId = table.Column<int>(type: "int", nullable: false),
                    Action = table.Column<int>(type: "int", nullable: false),
                    Message = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Detail = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MoadianLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MoadianLogs_MoadianInvoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "MoadianInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
            migrationBuilder.CreateIndex(
                name: "IX_MoadianCpcList_Code",
                table: "MoadianCpcList",
                column: "Code",
                unique: true);
            migrationBuilder.CreateIndex(
                name: "IX_MoadianFiscalPeriods_Year_Month",
                table: "MoadianFiscalPeriods",
                columns: new[] { "Year", "Month" },
                unique: true);
            migrationBuilder.CreateIndex(
                name: "IX_MoadianInvoiceLines_InvoiceId",
                table: "MoadianInvoiceLines",
                column: "InvoiceId");
            migrationBuilder.CreateIndex(
                name: "IX_MoadianInvoices_FacInvoiceId",
                table: "MoadianInvoices",
                column: "FacInvoiceId");
            migrationBuilder.CreateIndex(
                name: "IX_MoadianInvoices_FiscalPeriodId_Number",
                table: "MoadianInvoices",
                columns: new[] { "FiscalPeriodId", "Number" },
                unique: true);
            migrationBuilder.CreateIndex(
                name: "IX_MoadianInvoices_ReferenceId",
                table: "MoadianInvoices",
                column: "ReferenceId");
            migrationBuilder.CreateIndex(
                name: "IX_MoadianInvoices_Status",
                table: "MoadianInvoices",
                column: "Status");
            migrationBuilder.CreateIndex(
                name: "IX_MoadianLogs_InvoiceId",
                table: "MoadianLogs",
                column: "InvoiceId");
        

// ===== 20260906185126_AddDocArchiveOcrTagsAndErpLinks.cs =====
            migrationBuilder.CreateTable(
                name: "DocEntityLinks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DocumentId = table.Column<int>(type: "int", nullable: false),
                    Module = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    EntityId = table.Column<int>(type: "int", nullable: false),
                    EntityCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    EntityTitle = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false),
                    CreatedByName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocEntityLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocEntityLinks_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DocExtractedTexts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DocumentId = table.Column<int>(type: "int", nullable: false),
                    VersionId = table.Column<int>(type: "int", nullable: false),
                    AttachmentId = table.Column<int>(type: "int", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SourceType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ExtractedText = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    NormalizedText = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ErrorMessage = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CharacterCount = table.Column<int>(type: "int", nullable: false),
                    IndexedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocExtractedTexts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DocTags",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Color = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocTags", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DocumentTags",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DocumentId = table.Column<int>(type: "int", nullable: false),
                    TagId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentTags", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocEntityLinks_DocumentId_Module_EntityId",
                table: "DocEntityLinks",
                columns: new[] { "DocumentId", "Module", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_DocEntityLinks_Module_EntityId",
                table: "DocEntityLinks",
                columns: new[] { "Module", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_DocExtractedTexts_AttachmentId",
                table: "DocExtractedTexts",
                column: "AttachmentId");

            migrationBuilder.CreateIndex(
                name: "IX_DocExtractedTexts_DocumentId_AttachmentId",
                table: "DocExtractedTexts",
                columns: new[] { "DocumentId", "AttachmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_DocTags_Name",
                table: "DocTags",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentTags_DocumentId_TagId",
                table: "DocumentTags",
                columns: new[] { "DocumentId", "TagId" },
                unique: true);
        

// ===== 20260907072032_AddChatModule.cs =====
        if (ActiveProvider == "Microsoft.EntityFrameworkCore.Sqlite")
        {
            migrationBuilder.Sql(ChatSchemaV1.SqliteCreateSql);
            return;
        }

        if (ActiveProvider != "Microsoft.EntityFrameworkCore.SqlServer")
            throw new NotSupportedException("Chat schema supports SQL Server and SQLite only.");

        migrationBuilder.Sql("""
                -- =========================================================================
                -- مایگریشن ساخت جداول ماژول پیام‌رسان سازمانی (Chat Module) مشابه تلگرام
                -- =========================================================================
                
                IF OBJECT_ID(N'[dbo].[ChatConversations]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [dbo].[ChatConversations] (
                        [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                        [Title] NVARCHAR(200) NOT NULL,
                        [Type] INT NOT NULL DEFAULT 1,
                        [Description] NVARCHAR(500) NULL,
                        [AvatarUrl] NVARCHAR(500) NULL,
                        [CreatedByUserId] INT NOT NULL,
                        [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                        [LastMessageAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                        [LastMessageSnippet] NVARCHAR(500) NULL,
                        [LastMessageSenderId] INT NULL,
                        [LastMessageSenderName] NVARCHAR(100) NULL,
                        [PinnedMessageId] INT NULL,
                        [IsArchived] BIT NOT NULL DEFAULT 0
                    );
                
                END;
                
                IF OBJECT_ID(N'[dbo].[ChatMembers]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [dbo].[ChatMembers] (
                        [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                        [ConversationId] INT NOT NULL,
                        [UserId] INT NOT NULL,
                        [UserName] NVARCHAR(100) NOT NULL,
                        [UserDisplayName] NVARCHAR(150) NOT NULL,
                        [UserAvatarUrl] NVARCHAR(500) NULL,
                        [Role] INT NOT NULL DEFAULT 3,
                        [JoinedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                        [LastReadMessageId] INT NOT NULL DEFAULT 0,
                        [UnreadCount] INT NOT NULL DEFAULT 0,
                        [IsMuted] BIT NOT NULL DEFAULT 0,
                        [IsPinned] BIT NOT NULL DEFAULT 0,
                        [IsArchived] BIT NOT NULL DEFAULT 0,
                        CONSTRAINT [FK_ChatMembers_ChatConversations_ConversationId] FOREIGN KEY ([ConversationId]) REFERENCES [dbo].[ChatConversations] ([Id]) ON DELETE CASCADE
                    );
                
                
                END;
                
                IF OBJECT_ID(N'[dbo].[ChatMessages]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [dbo].[ChatMessages] (
                        [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                        [ConversationId] INT NOT NULL,
                        [SenderUserId] INT NOT NULL,
                        [SenderName] NVARCHAR(150) NOT NULL,
                        [SenderAvatarUrl] NVARCHAR(500) NULL,
                        [Text] NVARCHAR(MAX) NULL,
                        [MessageType] INT NOT NULL DEFAULT 1,
                        [FileUrl] NVARCHAR(500) NULL,
                        [FileName] NVARCHAR(250) NULL,
                        [FileSizeBytes] BIGINT NULL,
                        [FileContentType] NVARCHAR(100) NULL,
                        [ReplyToMessageId] INT NULL,
                        [ReplyToSenderName] NVARCHAR(150) NULL,
                        [ReplyToSnippet] NVARCHAR(300) NULL,
                        [ForwardFromMessageId] INT NULL,
                        [ForwardFromSenderName] NVARCHAR(150) NULL,
                        [ErpModule] NVARCHAR(50) NULL,
                        [ErpEntityId] NVARCHAR(50) NULL,
                        [ErpEntityTitle] NVARCHAR(200) NULL,
                        [ErpEntitySummary] NVARCHAR(500) NULL,
                        [IsEdited] BIT NOT NULL DEFAULT 0,
                        [EditedAt] DATETIME2 NULL,
                        [IsDeleted] BIT NOT NULL DEFAULT 0,
                        [DeletedAt] DATETIME2 NULL,
                        [IsPinned] BIT NOT NULL DEFAULT 0,
                        [ReactionsJson] NVARCHAR(MAX) NULL,
                        [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                        CONSTRAINT [FK_ChatMessages_ChatConversations_ConversationId] FOREIGN KEY ([ConversationId]) REFERENCES [dbo].[ChatConversations] ([Id]) ON DELETE CASCADE
                    );
                
                
                END;
                
                -- ایندکس‌ها حتی در نصب‌هایی که جدول‌ها را قبلاً دستی ساخته‌اند بررسی می‌شوند.
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[ChatConversations]') AND name = N'IX_ChatConversations_LastMessageAt')
                    CREATE INDEX [IX_ChatConversations_LastMessageAt] ON [dbo].[ChatConversations] ([LastMessageAt]);
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[ChatMembers]') AND name = N'IX_ChatMembers_ConversationId_UserId')
                    CREATE UNIQUE INDEX [IX_ChatMembers_ConversationId_UserId] ON [dbo].[ChatMembers] ([ConversationId], [UserId]);
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[ChatMembers]') AND name = N'IX_ChatMembers_UserId')
                    CREATE INDEX [IX_ChatMembers_UserId] ON [dbo].[ChatMembers] ([UserId]);
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[ChatMessages]') AND name = N'IX_ChatMessages_ConversationId_CreatedAt')
                    CREATE INDEX [IX_ChatMessages_ConversationId_CreatedAt] ON [dbo].[ChatMessages] ([ConversationId], [CreatedAt]);
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[ChatMessages]') AND name = N'IX_ChatMessages_SenderUserId')
                    CREATE INDEX [IX_ChatMessages_SenderUserId] ON [dbo].[ChatMessages] ([SenderUserId]);
                """);
    

// ===== 20260907081538_AddPrivateChatAttachments.cs =====
            if (ActiveProvider == "Microsoft.EntityFrameworkCore.Sqlite")
            {
                migrationBuilder.Sql(Inventory.Api.Data.ChatAttachmentSchemaV1.SqliteCreateSql);
                return;
            }
            migrationBuilder.CreateTable(
                name: "ChatAttachments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UploadedByUserId = table.Column<int>(type: "int", nullable: false),
                    ConversationId = table.Column<int>(type: "int", nullable: true),
                    FileName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatAttachments", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChatAttachments_ConversationId",
                table: "ChatAttachments",
                column: "ConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatAttachments_UploadedByUserId",
                table: "ChatAttachments",
                column: "UploadedByUserId");
        

// ===== 20260907090000_AddProjectChangeRequests.cs =====
            migrationBuilder.CreateTable(
                name: "ProjectChangeRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProjectId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Summary = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    RequestNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ManagerNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RequestedById = table.Column<int>(type: "int", nullable: false),
                    RequestedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ManagerActionById = table.Column<int>(type: "int", nullable: true),
                    ManagerActionAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectChangeRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectChangeRequests_ProjectEntryExits_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "ProjectEntryExits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProjectChangeRequests_Users_RequestedById",
                        column: x => x.RequestedById,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectChangeRequests_ProjectId",
                table: "ProjectChangeRequests",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectChangeRequests_RequestedById",
                table: "ProjectChangeRequests",
                column: "RequestedById");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectChangeRequests_Status",
                table: "ProjectChangeRequests",
                column: "Status");
        
        
            }

            // ===== 20260911141302_AddHrCoreModule =====
            void M_20260911141302_AddHrCoreModule()
            {
            migrationBuilder.CreateTable(
                name: "HrContracts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EmployeeId = table.Column<int>(type: "int", nullable: false),
                    ContractNo = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    StartDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    BaseSalary = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    JobTitle = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    OrgUnitId = table.Column<int>(type: "int", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HrContracts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HrDecrees",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EmployeeId = table.Column<int>(type: "int", nullable: false),
                    DecreeNo = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    EffectiveDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    NewPostTitle = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    NewOrgUnitId = table.Column<int>(type: "int", nullable: true),
                    NewBaseSalary = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    NewStatus = table.Column<int>(type: "int", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsApplied = table.Column<bool>(type: "bit", nullable: false),
                    AppliedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: true),
                    CreatedByName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HrDecrees", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HrEmployees",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    FirstName = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    LastName = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    NationalCode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    BirthDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Gender = table.Column<int>(type: "int", nullable: false),
                    MaritalStatus = table.Column<int>(type: "int", nullable: false),
                    Mobile = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Address = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    HireDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    OrgUnitId = table.Column<int>(type: "int", nullable: true),
                    PostTitle = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    ManagerId = table.Column<int>(type: "int", nullable: true),
                    EmploymentType = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    SystemUserId = table.Column<int>(type: "int", nullable: true),
                    BaseSalary = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HrEmployees", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HrOrgUnits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ParentId = table.Column<int>(type: "int", nullable: true),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    ManagerEmployeeId = table.Column<int>(type: "int", nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Address = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HrOrgUnits", x => x.Id);
                });
        
            }

            // ===== 20260911163445_AddHrTimePayModules =====
            void M_20260911163445_AddHrTimePayModules()
            {
            migrationBuilder.CreateTable(
                name: "HrDayExtras",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    WorkDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    OtNormalMinutes = table.Column<int>(type: "int", nullable: false),
                    OtHolidayMinutes = table.Column<int>(type: "int", nullable: false),
                    OtNightMinutes = table.Column<int>(type: "int", nullable: false),
                    EnterLat = table.Column<double>(type: "float", nullable: true),
                    EnterLng = table.Column<double>(type: "float", nullable: true),
                    ExitLat = table.Column<double>(type: "float", nullable: true),
                    ExitLng = table.Column<double>(type: "float", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HrDayExtras", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HrDevicePunches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DeviceCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    UserCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    PunchTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Direction = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    MappedUserId = table.Column<int>(type: "int", nullable: true),
                    AppliedRecordId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HrDevicePunches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HrDeviceUserMaps",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DeviceCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    UserCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SystemUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HrDeviceUserMaps", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HrLeaveBalances",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Category = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    TotalDays = table.Column<double>(type: "float", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HrLeaveBalances", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HrLeaveExtras",
                columns: table => new
                {
                    LeaveRequestId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Category = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    MissionKind = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    AllowanceAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HrLeaveExtras", x => x.LeaveRequestId);
                });

            migrationBuilder.CreateTable(
                name: "HrOvertimeRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    RequesterUserId = table.Column<int>(type: "int", nullable: false),
                    RequesterName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    WorkDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    StartTime = table.Column<TimeSpan>(type: "time", nullable: false),
                    EndTime = table.Column<TimeSpan>(type: "time", nullable: false),
                    Minutes = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DecidedByUserId = table.Column<int>(type: "int", nullable: true),
                    DecidedByName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    DecidedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HrOvertimeRequests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HrPayArrears",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EmployeeId = table.Column<int>(type: "int", nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Month = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HrPayArrears", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HrPayEmployeeItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EmployeeId = table.Column<int>(type: "int", nullable: false),
                    ItemId = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HrPayEmployeeItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HrPayItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Formula = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    DefaultAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    IsTaxable = table.Column<bool>(type: "bit", nullable: false),
                    IsInsuranceable = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsAccrual = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HrPayItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HrPayLoans",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EmployeeId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Installments = table.Column<int>(type: "int", nullable: false),
                    MonthlyAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    PaidCount = table.Column<int>(type: "int", nullable: false),
                    StartYear = table.Column<int>(type: "int", nullable: false),
                    StartMonth = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HrPayLoans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HrPayOnAccounts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EmployeeId = table.Column<int>(type: "int", nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Month = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    PaidDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HrPayOnAccounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HrPayProfiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EmployeeId = table.Column<int>(type: "int", nullable: false),
                    ChildrenCount = table.Column<int>(type: "int", nullable: false),
                    InsuranceNo = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    BankName = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    Iban = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    AccountNo = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    ExtraTaxExempt = table.Column<decimal>(type: "decimal(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HrPayProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HrPayRuns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Month = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SlipCount = table.Column<int>(type: "int", nullable: false),
                    TotalGross = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TotalDeductions = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TotalNet = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TotalTax = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TotalInsuranceEmp = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TotalInsuranceEr = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    VoucherId = table.Column<int>(type: "int", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LockedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HrPayRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HrPaySettlements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EmployeeId = table.Column<int>(type: "int", nullable: false),
                    LeaveDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    YearsOfService = table.Column<double>(type: "float", nullable: false),
                    LastBase = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    UnusedLeaveDays = table.Column<double>(type: "float", nullable: false),
                    SeveranceAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    LeaveRefund = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    EydiProrata = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HrPaySettlements", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HrPaySlips",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RunId = table.Column<int>(type: "int", nullable: false),
                    EmployeeId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: true),
                    EmployeeName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    DaysPaid = table.Column<double>(type: "float", nullable: false),
                    DaysWorked = table.Column<double>(type: "float", nullable: false),
                    BaseAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    GrossEarnings = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TaxableAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    InsuranceableAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TaxAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    InsuranceAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    EmployerInsurance = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    OtherDeductions = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    NetPay = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    DetailsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HrPaySlips", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HrPayTaxBrackets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Year = table.Column<int>(type: "int", nullable: false),
                    FromAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ToAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Rate = table.Column<decimal>(type: "decimal(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HrPayTaxBrackets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HrRequestSteps",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RequestType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RequestId = table.Column<int>(type: "int", nullable: false),
                    StepNo = table.Column<int>(type: "int", nullable: false),
                    Role = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ApproverUserId = table.Column<int>(type: "int", nullable: true),
                    ApproverName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    DecidedByUserId = table.Column<int>(type: "int", nullable: true),
                    DecidedByName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    DecidedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HrRequestSteps", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HrShiftRosters",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ShiftGroupId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HrShiftRosters", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HrTimeRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Key = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HrTimeRules", x => x.Id);
                });
        
            }

            // ===== 20260911173039_AddHrUserLink =====
            void M_20260911173039_AddHrUserLink()
            {
            migrationBuilder.CreateTable(
                name: "HrUserLinks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    EmployeeId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HrUserLinks", x => x.Id);
                });
        
            }

            // ===== 20260911183156_AddHrLegalPhase1 =====
            void M_20260911183156_AddHrLegalPhase1()
            {
            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "HrPayRuns",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "IsHardJob",
                table: "HrPayProfiles",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "HrMinWages",
                columns: table => new
                {
                    Year = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MonthlyWage = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    DailyWage = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    EffectiveDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HrMinWages", x => x.Year);
                });

            migrationBuilder.CreateTable(
                name: "HrNursingBreaks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EmployeeId = table.Column<int>(type: "int", nullable: false),
                    ChildBirthDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DailyHours = table.Column<double>(type: "float", nullable: false),
                    StartDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HrNursingBreaks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HrProbations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EmployeeId = table.Column<int>(type: "int", nullable: false),
                    StartDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Months = table.Column<int>(type: "int", nullable: false),
                    SkillLevel = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    EndDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    ResultNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    DecidedBy = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    DecidedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HrProbations", x => x.Id);
                });
        
            }

            // ===== 20260911200116_AddHrAttendPhase2 =====
            void M_20260911200116_AddHrAttendPhase2()
            {
            migrationBuilder.CreateTable(
                name: "HrAttendCloses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Month = table.Column<int>(type: "int", nullable: false),
                    IsClosed = table.Column<bool>(type: "bit", nullable: false),
                    ClosedBy = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    ClosedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HrAttendCloses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HrOtApprovals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    WorkDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReqNormalMin = table.Column<int>(type: "int", nullable: false),
                    ReqHolidayMin = table.Column<int>(type: "int", nullable: false),
                    ReqNightMin = table.Column<int>(type: "int", nullable: false),
                    ApprNormalMin = table.Column<int>(type: "int", nullable: false),
                    ApprHolidayMin = table.Column<int>(type: "int", nullable: false),
                    ApprNightMin = table.Column<int>(type: "int", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    OvertimeRequestId = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DecidedByUserId = table.Column<int>(type: "int", nullable: true),
                    DecidedByName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    DecidedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HrOtApprovals", x => x.Id);
                });
        
            }

            // ===== 20260911211508_AddHrPayFilingPhase3 =====
            void M_20260911211508_AddHrPayFilingPhase3()
            {
            migrationBuilder.CreateTable(
                name: "HrPayFilings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RunId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Sha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SlipCount = table.Column<int>(type: "int", nullable: false),
                    TotalGross = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TotalTax = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TotalInsurance = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TotalNet = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    FiledBy = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    FiledAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReceiptNo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HrPayFilings", x => x.Id);
                });
        
            }

            // ===== 20260911213808_AddHrPerfPhase4 =====
            void M_20260911213808_AddHrPerfPhase4()
            {
            migrationBuilder.CreateTable(
                name: "HrPerfKpis",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PeriodId = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Weight = table.Column<double>(type: "float", nullable: false),
                    MaxScore = table.Column<double>(type: "float", nullable: false),
                    Category = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HrPerfKpis", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HrPerfPeriods",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Title = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    StartDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    BonusMonthSalary = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    MinScoreForBonus = table.Column<double>(type: "float", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HrPerfPeriods", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HrPerfResults",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PeriodId = table.Column<int>(type: "int", nullable: false),
                    EmployeeId = table.Column<int>(type: "int", nullable: false),
                    TotalScore = table.Column<double>(type: "float", nullable: false),
                    Grade = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false),
                    BonusAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    PayYear = table.Column<int>(type: "int", nullable: true),
                    PayMonth = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    FinalizedBy = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    FinalizedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HrPerfResults", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HrPerfScores",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PeriodId = table.Column<int>(type: "int", nullable: false),
                    EmployeeId = table.Column<int>(type: "int", nullable: false),
                    KpiId = table.Column<int>(type: "int", nullable: false),
                    Score = table.Column<double>(type: "float", nullable: false),
                    ScoredByUserId = table.Column<int>(type: "int", nullable: false),
                    ScoredByName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HrPerfScores", x => x.Id);
                });
        
            }

            M_20260911100108_SquashedInitial();
            M_20260911141302_AddHrCoreModule();
            M_20260911163445_AddHrTimePayModules();
            M_20260911173039_AddHrUserLink();
            M_20260911183156_AddHrLegalPhase1();
            M_20260911200116_AddHrAttendPhase2();
            M_20260911211508_AddHrPayFilingPhase3();
            M_20260911213808_AddHrPerfPhase4();
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // معکوسِ ترتیب Up
            // ===== 20260911213808_AddHrPerfPhase4 =====
            void M_20260911213808_AddHrPerfPhase4()
            {
            migrationBuilder.DropTable(
                name: "HrPerfKpis");

            migrationBuilder.DropTable(
                name: "HrPerfPeriods");

            migrationBuilder.DropTable(
                name: "HrPerfResults");

            migrationBuilder.DropTable(
                name: "HrPerfScores");
        
            }

            // ===== 20260911211508_AddHrPayFilingPhase3 =====
            void M_20260911211508_AddHrPayFilingPhase3()
            {
            migrationBuilder.DropTable(
                name: "HrPayFilings");
        
            }

            // ===== 20260911200116_AddHrAttendPhase2 =====
            void M_20260911200116_AddHrAttendPhase2()
            {
            migrationBuilder.DropTable(
                name: "HrAttendCloses");

            migrationBuilder.DropTable(
                name: "HrOtApprovals");
        
            }

            // ===== 20260911183156_AddHrLegalPhase1 =====
            void M_20260911183156_AddHrLegalPhase1()
            {
            migrationBuilder.DropTable(
                name: "HrMinWages");

            migrationBuilder.DropTable(
                name: "HrNursingBreaks");

            migrationBuilder.DropTable(
                name: "HrProbations");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "HrPayRuns");

            migrationBuilder.DropColumn(
                name: "IsHardJob",
                table: "HrPayProfiles");
        
            }

            // ===== 20260911173039_AddHrUserLink =====
            void M_20260911173039_AddHrUserLink()
            {
            migrationBuilder.DropTable(
                name: "HrUserLinks");
        
            }

            // ===== 20260911163445_AddHrTimePayModules =====
            void M_20260911163445_AddHrTimePayModules()
            {
            migrationBuilder.DropTable(
                name: "HrDayExtras");

            migrationBuilder.DropTable(
                name: "HrDevicePunches");

            migrationBuilder.DropTable(
                name: "HrDeviceUserMaps");

            migrationBuilder.DropTable(
                name: "HrLeaveBalances");

            migrationBuilder.DropTable(
                name: "HrLeaveExtras");

            migrationBuilder.DropTable(
                name: "HrOvertimeRequests");

            migrationBuilder.DropTable(
                name: "HrPayArrears");

            migrationBuilder.DropTable(
                name: "HrPayEmployeeItems");

            migrationBuilder.DropTable(
                name: "HrPayItems");

            migrationBuilder.DropTable(
                name: "HrPayLoans");

            migrationBuilder.DropTable(
                name: "HrPayOnAccounts");

            migrationBuilder.DropTable(
                name: "HrPayProfiles");

            migrationBuilder.DropTable(
                name: "HrPayRuns");

            migrationBuilder.DropTable(
                name: "HrPaySettlements");

            migrationBuilder.DropTable(
                name: "HrPaySlips");

            migrationBuilder.DropTable(
                name: "HrPayTaxBrackets");

            migrationBuilder.DropTable(
                name: "HrRequestSteps");

            migrationBuilder.DropTable(
                name: "HrShiftRosters");

            migrationBuilder.DropTable(
                name: "HrTimeRules");
        
            }

            // ===== 20260911141302_AddHrCoreModule =====
            void M_20260911141302_AddHrCoreModule()
            {
            migrationBuilder.DropTable(
                name: "HrContracts");

            migrationBuilder.DropTable(
                name: "HrDecrees");

            migrationBuilder.DropTable(
                name: "HrEmployees");

            migrationBuilder.DropTable(
                name: "HrOrgUnits");
        
            }

            // ===== 20260911100108_SquashedInitial =====
            void M_20260911100108_SquashedInitial()
            {
            // برگرداندن اسکواش — ترتیب معکوس اجرای اصلی.
// ===== 20260907090000_AddProjectChangeRequests.cs =====
            migrationBuilder.DropTable(
                name: "ProjectChangeRequests");
        

// ===== 20260907081538_AddPrivateChatAttachments.cs =====
            throw new NotSupportedException("Automatic rollback is disabled to protect attachment ownership metadata. Restore a reviewed backup if needed.");
        

// ===== 20260907072032_AddChatModule.cs =====
        // ممکن است جداول قبل از این مایگریشن به‌صورت دستی ساخته و پر شده باشند.
        // حذف خودکار آن‌ها در rollback باعث از دست رفتن گفتگوهای واقعی می‌شود.
        throw new NotSupportedException(
            "Automatic rollback of the chat schema is disabled to protect existing conversations. Restore a reviewed backup if required.");
    

// ===== 20260906185126_AddDocArchiveOcrTagsAndErpLinks.cs =====
            migrationBuilder.DropTable(
                name: "DocEntityLinks");

            migrationBuilder.DropTable(
                name: "DocExtractedTexts");

            migrationBuilder.DropTable(
                name: "DocTags");

            migrationBuilder.DropTable(
                name: "DocumentTags");
        

// ===== 20260906093500_AddMoadianFiscalSystem.cs =====
            migrationBuilder.DropTable(
                name: "FiscalPrinterSettings");
            migrationBuilder.DropTable(
                name: "MoadianCpcList");
            migrationBuilder.DropTable(
                name: "MoadianInvoiceLines");
            migrationBuilder.DropTable(
                name: "MoadianLogs");
            migrationBuilder.DropTable(
                name: "MoadianSettings");
            migrationBuilder.DropTable(
                name: "MoadianInvoices");
            migrationBuilder.DropTable(
                name: "MoadianFiscalPeriods");
        

// ===== 20260906093024_AddAnalyticalAccounting_FixedAssets_Budgets.cs =====
            migrationBuilder.DropForeignKey(
                name: "FK_AccVoucherLines_AccDimensionValues_DimensionValueId",
                table: "AccVoucherLines");
            migrationBuilder.DropTable(
                name: "BudgetTransactions");
            migrationBuilder.DropTable(
                name: "FixedAssetDepreciationLines");
            migrationBuilder.DropTable(
                name: "BudgetItems");
            migrationBuilder.DropTable(
                name: "FixedAssetDepreciationRuns");
            migrationBuilder.DropTable(
                name: "FixedAssets");
            migrationBuilder.DropTable(
                name: "Budgets");
            migrationBuilder.DropTable(
                name: "FixedAssetCategories");
            migrationBuilder.DropTable(
                name: "AccDimensionValues");
            migrationBuilder.DropTable(
                name: "AccDimensions");
            migrationBuilder.DropIndex(
                name: "IX_AccVoucherLines_DimensionValueId",
                table: "AccVoucherLines");
            migrationBuilder.DropColumn(
                name: "DimensionValueId",
                table: "AccVoucherLines");
        

// ===== 20260906072405_AddDocSoftDeleteInfo.cs =====
            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "DeletedByName",
                table: "Documents");
        

// ===== 20260906070735_AddDocExpiryAlerts.cs =====
            migrationBuilder.DropTable(
                name: "DocExpiryAlerts");
        

// ===== 20260906055526_AddDocCustomerCodeAndStatus.cs =====
            migrationBuilder.DropColumn(
                name: "CustomerCode",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "DeactivateReason",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "DeactivatedAt",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "DeactivatedByName",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "Documents");
        

// ===== 20260906050520_AddStocktakingModule.cs =====
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[StkScans]', N'U') IS NOT NULL DROP TABLE [StkScans];
IF OBJECT_ID(N'[StkLines]', N'U') IS NOT NULL DROP TABLE [StkLines];
IF OBJECT_ID(N'[StkSessions]', N'U') IS NOT NULL DROP TABLE [StkSessions];
IF OBJECT_ID(N'[BcdBarcodes]', N'U') IS NOT NULL DROP TABLE [BcdBarcodes];
");
        

// ===== 20260906044059_AddDocArchiveModule.cs =====
            migrationBuilder.DropTable(
                name: "DocCartableTasks");


            migrationBuilder.DropTable(
                name: "DocFolderPermissions");


            migrationBuilder.DropTable(
                name: "DocFolders");


            migrationBuilder.DropTable(
                name: "DocumentApprovers");


            migrationBuilder.DropTable(
                name: "DocumentLinks");


            migrationBuilder.DropTable(
                name: "DocumentLogs");


            migrationBuilder.DropTable(
                name: "DocumentPermissions");


            migrationBuilder.DropTable(
                name: "Documents");


            migrationBuilder.DropTable(
                name: "DocumentVersions");
        

// ===== 20260906040427_AddTreasuryModule.cs =====
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[TrsChequeActions]', N'U') IS NOT NULL DROP TABLE [TrsChequeActions];
IF OBJECT_ID(N'[TrsVoucherLines]', N'U') IS NOT NULL DROP TABLE [TrsVoucherLines];
IF OBJECT_ID(N'[TrsCheques]', N'U') IS NOT NULL DROP TABLE [TrsCheques];
IF OBJECT_ID(N'[TrsVouchers]', N'U') IS NOT NULL DROP TABLE [TrsVouchers];
IF OBJECT_ID(N'[TrsAccounts]', N'U') IS NOT NULL DROP TABLE [TrsAccounts];
IF OBJECT_ID(N'[TrsRules]', N'U') IS NOT NULL DROP TABLE [TrsRules];
");
        

// ===== 20260906033059_AddInvoicingModule.cs =====
        migrationBuilder.Sql(@"
IF OBJECT_ID(N'[FacInvoiceLines]', N'U') IS NOT NULL DROP TABLE [FacInvoiceLines];

IF OBJECT_ID(N'[FacInvoices]', N'U') IS NOT NULL DROP TABLE [FacInvoices];

IF OBJECT_ID(N'[FacRules]', N'U') IS NOT NULL DROP TABLE [FacRules];
");
    

// ===== 20260905212404_AddAccountingModule.cs =====
        migrationBuilder.Sql(@"
IF OBJECT_ID(N'[AccVoucherLines]', N'U') IS NOT NULL DROP TABLE [AccVoucherLines];

IF OBJECT_ID(N'[AccVouchers]', N'U') IS NOT NULL DROP TABLE [AccVouchers];

IF OBJECT_ID(N'[AccInvRules]', N'U') IS NOT NULL DROP TABLE [AccInvRules];

IF OBJECT_ID(N'[AccAccounts]', N'U') IS NOT NULL DROP TABLE [AccAccounts];

IF OBJECT_ID(N'[AccFiscalYears]', N'U') IS NOT NULL DROP TABLE [AccFiscalYears];
");
    

// ===== 20260905154353_AddWarehousingModule.cs =====
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[InvDocLines]', N'U') IS NOT NULL DROP TABLE [InvDocLines];");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[InvDocTypes]', N'U') IS NOT NULL DROP TABLE [InvDocTypes];");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[InvLedger]', N'U') IS NOT NULL DROP TABLE [InvLedger];");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[InvStocks]', N'U') IS NOT NULL DROP TABLE [InvStocks];");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[InvDocs]', N'U') IS NOT NULL DROP TABLE [InvDocs];");

            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_Warehouses_Code' AND [object_id] = OBJECT_ID(N'[Warehouses]'))
    DROP INDEX [IX_Warehouses_Code] ON [Warehouses];");

            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_Products_CategoryId' AND [object_id] = OBJECT_ID(N'[Products]'))
    DROP INDEX [IX_Products_CategoryId] ON [Products];");

            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_Products_TaxCode' AND [object_id] = OBJECT_ID(N'[Products]'))
    DROP INDEX [IX_Products_TaxCode] ON [Products];");

            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_ProductCategories_ParentId' AND [object_id] = OBJECT_ID(N'[ProductCategories]'))
    DROP INDEX [IX_ProductCategories_ParentId] ON [ProductCategories];");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Warehouses]', N'AllowNegative') IS NOT NULL
BEGIN
    DECLARE @df_Warehouses_AllowNegative sysname;
    SELECT @df_Warehouses_AllowNegative = dc.[name]
      FROM sys.default_constraints dc
      JOIN sys.columns c ON c.[default_object_id] = dc.[object_id]
     WHERE dc.[parent_object_id] = OBJECT_ID(N'[Warehouses]') AND c.[name] = N'AllowNegative';
    IF @df_Warehouses_AllowNegative IS NOT NULL EXEC(N'ALTER TABLE [Warehouses] DROP CONSTRAINT [' + @df_Warehouses_AllowNegative + N']');
    ALTER TABLE [Warehouses] DROP COLUMN [AllowNegative];
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Warehouses]', N'Code') IS NOT NULL
BEGIN
    DECLARE @df_Warehouses_Code sysname;
    SELECT @df_Warehouses_Code = dc.[name]
      FROM sys.default_constraints dc
      JOIN sys.columns c ON c.[default_object_id] = dc.[object_id]
     WHERE dc.[parent_object_id] = OBJECT_ID(N'[Warehouses]') AND c.[name] = N'Code';
    IF @df_Warehouses_Code IS NOT NULL EXEC(N'ALTER TABLE [Warehouses] DROP CONSTRAINT [' + @df_Warehouses_Code + N']');
    ALTER TABLE [Warehouses] DROP COLUMN [Code];
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Warehouses]', N'IsDefault') IS NOT NULL
BEGIN
    DECLARE @df_Warehouses_IsDefault sysname;
    SELECT @df_Warehouses_IsDefault = dc.[name]
      FROM sys.default_constraints dc
      JOIN sys.columns c ON c.[default_object_id] = dc.[object_id]
     WHERE dc.[parent_object_id] = OBJECT_ID(N'[Warehouses]') AND c.[name] = N'IsDefault';
    IF @df_Warehouses_IsDefault IS NOT NULL EXEC(N'ALTER TABLE [Warehouses] DROP CONSTRAINT [' + @df_Warehouses_IsDefault + N']');
    ALTER TABLE [Warehouses] DROP COLUMN [IsDefault];
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Warehouses]', N'KeeperName') IS NOT NULL
BEGIN
    DECLARE @df_Warehouses_KeeperName sysname;
    SELECT @df_Warehouses_KeeperName = dc.[name]
      FROM sys.default_constraints dc
      JOIN sys.columns c ON c.[default_object_id] = dc.[object_id]
     WHERE dc.[parent_object_id] = OBJECT_ID(N'[Warehouses]') AND c.[name] = N'KeeperName';
    IF @df_Warehouses_KeeperName IS NOT NULL EXEC(N'ALTER TABLE [Warehouses] DROP CONSTRAINT [' + @df_Warehouses_KeeperName + N']');
    ALTER TABLE [Warehouses] DROP COLUMN [KeeperName];
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Warehouses]', N'Kind') IS NOT NULL
BEGIN
    DECLARE @df_Warehouses_Kind sysname;
    SELECT @df_Warehouses_Kind = dc.[name]
      FROM sys.default_constraints dc
      JOIN sys.columns c ON c.[default_object_id] = dc.[object_id]
     WHERE dc.[parent_object_id] = OBJECT_ID(N'[Warehouses]') AND c.[name] = N'Kind';
    IF @df_Warehouses_Kind IS NOT NULL EXEC(N'ALTER TABLE [Warehouses] DROP CONSTRAINT [' + @df_Warehouses_Kind + N']');
    ALTER TABLE [Warehouses] DROP COLUMN [Kind];
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'Brand') IS NOT NULL
BEGIN
    DECLARE @df_Products_Brand sysname;
    SELECT @df_Products_Brand = dc.[name]
      FROM sys.default_constraints dc
      JOIN sys.columns c ON c.[default_object_id] = dc.[object_id]
     WHERE dc.[parent_object_id] = OBJECT_ID(N'[Products]') AND c.[name] = N'Brand';
    IF @df_Products_Brand IS NOT NULL EXEC(N'ALTER TABLE [Products] DROP CONSTRAINT [' + @df_Products_Brand + N']');
    ALTER TABLE [Products] DROP COLUMN [Brand];
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'CategoryId') IS NOT NULL
BEGIN
    DECLARE @df_Products_CategoryId sysname;
    SELECT @df_Products_CategoryId = dc.[name]
      FROM sys.default_constraints dc
      JOIN sys.columns c ON c.[default_object_id] = dc.[object_id]
     WHERE dc.[parent_object_id] = OBJECT_ID(N'[Products]') AND c.[name] = N'CategoryId';
    IF @df_Products_CategoryId IS NOT NULL EXEC(N'ALTER TABLE [Products] DROP CONSTRAINT [' + @df_Products_CategoryId + N']');
    ALTER TABLE [Products] DROP COLUMN [CategoryId];
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'CountryOfOrigin') IS NOT NULL
BEGIN
    DECLARE @df_Products_CountryOfOrigin sysname;
    SELECT @df_Products_CountryOfOrigin = dc.[name]
      FROM sys.default_constraints dc
      JOIN sys.columns c ON c.[default_object_id] = dc.[object_id]
     WHERE dc.[parent_object_id] = OBJECT_ID(N'[Products]') AND c.[name] = N'CountryOfOrigin';
    IF @df_Products_CountryOfOrigin IS NOT NULL EXEC(N'ALTER TABLE [Products] DROP CONSTRAINT [' + @df_Products_CountryOfOrigin + N']');
    ALTER TABLE [Products] DROP COLUMN [CountryOfOrigin];
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'CustomsCode') IS NOT NULL
BEGIN
    DECLARE @df_Products_CustomsCode sysname;
    SELECT @df_Products_CustomsCode = dc.[name]
      FROM sys.default_constraints dc
      JOIN sys.columns c ON c.[default_object_id] = dc.[object_id]
     WHERE dc.[parent_object_id] = OBJECT_ID(N'[Products]') AND c.[name] = N'CustomsCode';
    IF @df_Products_CustomsCode IS NOT NULL EXEC(N'ALTER TABLE [Products] DROP CONSTRAINT [' + @df_Products_CustomsCode + N']');
    ALTER TABLE [Products] DROP COLUMN [CustomsCode];
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'DutyRate') IS NOT NULL
BEGIN
    DECLARE @df_Products_DutyRate sysname;
    SELECT @df_Products_DutyRate = dc.[name]
      FROM sys.default_constraints dc
      JOIN sys.columns c ON c.[default_object_id] = dc.[object_id]
     WHERE dc.[parent_object_id] = OBJECT_ID(N'[Products]') AND c.[name] = N'DutyRate';
    IF @df_Products_DutyRate IS NOT NULL EXEC(N'ALTER TABLE [Products] DROP CONSTRAINT [' + @df_Products_DutyRate + N']');
    ALTER TABLE [Products] DROP COLUMN [DutyRate];
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'EnName') IS NOT NULL
BEGIN
    DECLARE @df_Products_EnName sysname;
    SELECT @df_Products_EnName = dc.[name]
      FROM sys.default_constraints dc
      JOIN sys.columns c ON c.[default_object_id] = dc.[object_id]
     WHERE dc.[parent_object_id] = OBJECT_ID(N'[Products]') AND c.[name] = N'EnName';
    IF @df_Products_EnName IS NOT NULL EXEC(N'ALTER TABLE [Products] DROP CONSTRAINT [' + @df_Products_EnName + N']');
    ALTER TABLE [Products] DROP COLUMN [EnName];
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'Height') IS NOT NULL
BEGIN
    DECLARE @df_Products_Height sysname;
    SELECT @df_Products_Height = dc.[name]
      FROM sys.default_constraints dc
      JOIN sys.columns c ON c.[default_object_id] = dc.[object_id]
     WHERE dc.[parent_object_id] = OBJECT_ID(N'[Products]') AND c.[name] = N'Height';
    IF @df_Products_Height IS NOT NULL EXEC(N'ALTER TABLE [Products] DROP CONSTRAINT [' + @df_Products_Height + N']');
    ALTER TABLE [Products] DROP COLUMN [Height];
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'ImageUrl') IS NOT NULL
BEGIN
    DECLARE @df_Products_ImageUrl sysname;
    SELECT @df_Products_ImageUrl = dc.[name]
      FROM sys.default_constraints dc
      JOIN sys.columns c ON c.[default_object_id] = dc.[object_id]
     WHERE dc.[parent_object_id] = OBJECT_ID(N'[Products]') AND c.[name] = N'ImageUrl';
    IF @df_Products_ImageUrl IS NOT NULL EXEC(N'ALTER TABLE [Products] DROP CONSTRAINT [' + @df_Products_ImageUrl + N']');
    ALTER TABLE [Products] DROP COLUMN [ImageUrl];
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'IsVatIncluded') IS NOT NULL
BEGIN
    DECLARE @df_Products_IsVatIncluded sysname;
    SELECT @df_Products_IsVatIncluded = dc.[name]
      FROM sys.default_constraints dc
      JOIN sys.columns c ON c.[default_object_id] = dc.[object_id]
     WHERE dc.[parent_object_id] = OBJECT_ID(N'[Products]') AND c.[name] = N'IsVatIncluded';
    IF @df_Products_IsVatIncluded IS NOT NULL EXEC(N'ALTER TABLE [Products] DROP CONSTRAINT [' + @df_Products_IsVatIncluded + N']');
    ALTER TABLE [Products] DROP COLUMN [IsVatIncluded];
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'Length') IS NOT NULL
BEGIN
    DECLARE @df_Products_Length sysname;
    SELECT @df_Products_Length = dc.[name]
      FROM sys.default_constraints dc
      JOIN sys.columns c ON c.[default_object_id] = dc.[object_id]
     WHERE dc.[parent_object_id] = OBJECT_ID(N'[Products]') AND c.[name] = N'Length';
    IF @df_Products_Length IS NOT NULL EXEC(N'ALTER TABLE [Products] DROP CONSTRAINT [' + @df_Products_Length + N']');
    ALTER TABLE [Products] DROP COLUMN [Length];
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'MinOrderQty') IS NOT NULL
BEGIN
    DECLARE @df_Products_MinOrderQty sysname;
    SELECT @df_Products_MinOrderQty = dc.[name]
      FROM sys.default_constraints dc
      JOIN sys.columns c ON c.[default_object_id] = dc.[object_id]
     WHERE dc.[parent_object_id] = OBJECT_ID(N'[Products]') AND c.[name] = N'MinOrderQty';
    IF @df_Products_MinOrderQty IS NOT NULL EXEC(N'ALTER TABLE [Products] DROP CONSTRAINT [' + @df_Products_MinOrderQty + N']');
    ALTER TABLE [Products] DROP COLUMN [MinOrderQty];
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'Model') IS NOT NULL
BEGIN
    DECLARE @df_Products_Model sysname;
    SELECT @df_Products_Model = dc.[name]
      FROM sys.default_constraints dc
      JOIN sys.columns c ON c.[default_object_id] = dc.[object_id]
     WHERE dc.[parent_object_id] = OBJECT_ID(N'[Products]') AND c.[name] = N'Model';
    IF @df_Products_Model IS NOT NULL EXEC(N'ALTER TABLE [Products] DROP CONSTRAINT [' + @df_Products_Model + N']');
    ALTER TABLE [Products] DROP COLUMN [Model];
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'Note') IS NOT NULL
BEGIN
    DECLARE @df_Products_Note sysname;
    SELECT @df_Products_Note = dc.[name]
      FROM sys.default_constraints dc
      JOIN sys.columns c ON c.[default_object_id] = dc.[object_id]
     WHERE dc.[parent_object_id] = OBJECT_ID(N'[Products]') AND c.[name] = N'Note';
    IF @df_Products_Note IS NOT NULL EXEC(N'ALTER TABLE [Products] DROP CONSTRAINT [' + @df_Products_Note + N']');
    ALTER TABLE [Products] DROP COLUMN [Note];
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'OtherTaxRate') IS NOT NULL
BEGIN
    DECLARE @df_Products_OtherTaxRate sysname;
    SELECT @df_Products_OtherTaxRate = dc.[name]
      FROM sys.default_constraints dc
      JOIN sys.columns c ON c.[default_object_id] = dc.[object_id]
     WHERE dc.[parent_object_id] = OBJECT_ID(N'[Products]') AND c.[name] = N'OtherTaxRate';
    IF @df_Products_OtherTaxRate IS NOT NULL EXEC(N'ALTER TABLE [Products] DROP CONSTRAINT [' + @df_Products_OtherTaxRate + N']');
    ALTER TABLE [Products] DROP COLUMN [OtherTaxRate];
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'PartNumber') IS NOT NULL
BEGIN
    DECLARE @df_Products_PartNumber sysname;
    SELECT @df_Products_PartNumber = dc.[name]
      FROM sys.default_constraints dc
      JOIN sys.columns c ON c.[default_object_id] = dc.[object_id]
     WHERE dc.[parent_object_id] = OBJECT_ID(N'[Products]') AND c.[name] = N'PartNumber';
    IF @df_Products_PartNumber IS NOT NULL EXEC(N'ALTER TABLE [Products] DROP CONSTRAINT [' + @df_Products_PartNumber + N']');
    ALTER TABLE [Products] DROP COLUMN [PartNumber];
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'SalePrice2') IS NOT NULL
BEGIN
    DECLARE @df_Products_SalePrice2 sysname;
    SELECT @df_Products_SalePrice2 = dc.[name]
      FROM sys.default_constraints dc
      JOIN sys.columns c ON c.[default_object_id] = dc.[object_id]
     WHERE dc.[parent_object_id] = OBJECT_ID(N'[Products]') AND c.[name] = N'SalePrice2';
    IF @df_Products_SalePrice2 IS NOT NULL EXEC(N'ALTER TABLE [Products] DROP CONSTRAINT [' + @df_Products_SalePrice2 + N']');
    ALTER TABLE [Products] DROP COLUMN [SalePrice2];
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'SecondUnit') IS NOT NULL
BEGIN
    DECLARE @df_Products_SecondUnit sysname;
    SELECT @df_Products_SecondUnit = dc.[name]
      FROM sys.default_constraints dc
      JOIN sys.columns c ON c.[default_object_id] = dc.[object_id]
     WHERE dc.[parent_object_id] = OBJECT_ID(N'[Products]') AND c.[name] = N'SecondUnit';
    IF @df_Products_SecondUnit IS NOT NULL EXEC(N'ALTER TABLE [Products] DROP CONSTRAINT [' + @df_Products_SecondUnit + N']');
    ALTER TABLE [Products] DROP COLUMN [SecondUnit];
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'ShelfCode') IS NOT NULL
BEGIN
    DECLARE @df_Products_ShelfCode sysname;
    SELECT @df_Products_ShelfCode = dc.[name]
      FROM sys.default_constraints dc
      JOIN sys.columns c ON c.[default_object_id] = dc.[object_id]
     WHERE dc.[parent_object_id] = OBJECT_ID(N'[Products]') AND c.[name] = N'ShelfCode';
    IF @df_Products_ShelfCode IS NOT NULL EXEC(N'ALTER TABLE [Products] DROP CONSTRAINT [' + @df_Products_ShelfCode + N']');
    ALTER TABLE [Products] DROP COLUMN [ShelfCode];
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'ShelfLifeDays') IS NOT NULL
BEGIN
    DECLARE @df_Products_ShelfLifeDays sysname;
    SELECT @df_Products_ShelfLifeDays = dc.[name]
      FROM sys.default_constraints dc
      JOIN sys.columns c ON c.[default_object_id] = dc.[object_id]
     WHERE dc.[parent_object_id] = OBJECT_ID(N'[Products]') AND c.[name] = N'ShelfLifeDays';
    IF @df_Products_ShelfLifeDays IS NOT NULL EXEC(N'ALTER TABLE [Products] DROP CONSTRAINT [' + @df_Products_ShelfLifeDays + N']');
    ALTER TABLE [Products] DROP COLUMN [ShelfLifeDays];
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'TaxCode') IS NOT NULL
BEGIN
    DECLARE @df_Products_TaxCode sysname;
    SELECT @df_Products_TaxCode = dc.[name]
      FROM sys.default_constraints dc
      JOIN sys.columns c ON c.[default_object_id] = dc.[object_id]
     WHERE dc.[parent_object_id] = OBJECT_ID(N'[Products]') AND c.[name] = N'TaxCode';
    IF @df_Products_TaxCode IS NOT NULL EXEC(N'ALTER TABLE [Products] DROP CONSTRAINT [' + @df_Products_TaxCode + N']');
    ALTER TABLE [Products] DROP COLUMN [TaxCode];
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'TaxUnitCode') IS NOT NULL
BEGIN
    DECLARE @df_Products_TaxUnitCode sysname;
    SELECT @df_Products_TaxUnitCode = dc.[name]
      FROM sys.default_constraints dc
      JOIN sys.columns c ON c.[default_object_id] = dc.[object_id]
     WHERE dc.[parent_object_id] = OBJECT_ID(N'[Products]') AND c.[name] = N'TaxUnitCode';
    IF @df_Products_TaxUnitCode IS NOT NULL EXEC(N'ALTER TABLE [Products] DROP CONSTRAINT [' + @df_Products_TaxUnitCode + N']');
    ALTER TABLE [Products] DROP COLUMN [TaxUnitCode];
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'TrackBatch') IS NOT NULL
BEGIN
    DECLARE @df_Products_TrackBatch sysname;
    SELECT @df_Products_TrackBatch = dc.[name]
      FROM sys.default_constraints dc
      JOIN sys.columns c ON c.[default_object_id] = dc.[object_id]
     WHERE dc.[parent_object_id] = OBJECT_ID(N'[Products]') AND c.[name] = N'TrackBatch';
    IF @df_Products_TrackBatch IS NOT NULL EXEC(N'ALTER TABLE [Products] DROP CONSTRAINT [' + @df_Products_TrackBatch + N']');
    ALTER TABLE [Products] DROP COLUMN [TrackBatch];
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'TrackExpiry') IS NOT NULL
BEGIN
    DECLARE @df_Products_TrackExpiry sysname;
    SELECT @df_Products_TrackExpiry = dc.[name]
      FROM sys.default_constraints dc
      JOIN sys.columns c ON c.[default_object_id] = dc.[object_id]
     WHERE dc.[parent_object_id] = OBJECT_ID(N'[Products]') AND c.[name] = N'TrackExpiry';
    IF @df_Products_TrackExpiry IS NOT NULL EXEC(N'ALTER TABLE [Products] DROP CONSTRAINT [' + @df_Products_TrackExpiry + N']');
    ALTER TABLE [Products] DROP COLUMN [TrackExpiry];
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'TrackSerial') IS NOT NULL
BEGIN
    DECLARE @df_Products_TrackSerial sysname;
    SELECT @df_Products_TrackSerial = dc.[name]
      FROM sys.default_constraints dc
      JOIN sys.columns c ON c.[default_object_id] = dc.[object_id]
     WHERE dc.[parent_object_id] = OBJECT_ID(N'[Products]') AND c.[name] = N'TrackSerial';
    IF @df_Products_TrackSerial IS NOT NULL EXEC(N'ALTER TABLE [Products] DROP CONSTRAINT [' + @df_Products_TrackSerial + N']');
    ALTER TABLE [Products] DROP COLUMN [TrackSerial];
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'UnitFactor') IS NOT NULL
BEGIN
    DECLARE @df_Products_UnitFactor sysname;
    SELECT @df_Products_UnitFactor = dc.[name]
      FROM sys.default_constraints dc
      JOIN sys.columns c ON c.[default_object_id] = dc.[object_id]
     WHERE dc.[parent_object_id] = OBJECT_ID(N'[Products]') AND c.[name] = N'UnitFactor';
    IF @df_Products_UnitFactor IS NOT NULL EXEC(N'ALTER TABLE [Products] DROP CONSTRAINT [' + @df_Products_UnitFactor + N']');
    ALTER TABLE [Products] DROP COLUMN [UnitFactor];
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'Valuation') IS NOT NULL
BEGIN
    DECLARE @df_Products_Valuation sysname;
    SELECT @df_Products_Valuation = dc.[name]
      FROM sys.default_constraints dc
      JOIN sys.columns c ON c.[default_object_id] = dc.[object_id]
     WHERE dc.[parent_object_id] = OBJECT_ID(N'[Products]') AND c.[name] = N'Valuation';
    IF @df_Products_Valuation IS NOT NULL EXEC(N'ALTER TABLE [Products] DROP CONSTRAINT [' + @df_Products_Valuation + N']');
    ALTER TABLE [Products] DROP COLUMN [Valuation];
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'VatRate') IS NOT NULL
BEGIN
    DECLARE @df_Products_VatRate sysname;
    SELECT @df_Products_VatRate = dc.[name]
      FROM sys.default_constraints dc
      JOIN sys.columns c ON c.[default_object_id] = dc.[object_id]
     WHERE dc.[parent_object_id] = OBJECT_ID(N'[Products]') AND c.[name] = N'VatRate';
    IF @df_Products_VatRate IS NOT NULL EXEC(N'ALTER TABLE [Products] DROP CONSTRAINT [' + @df_Products_VatRate + N']');
    ALTER TABLE [Products] DROP COLUMN [VatRate];
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'Weight') IS NOT NULL
BEGIN
    DECLARE @df_Products_Weight sysname;
    SELECT @df_Products_Weight = dc.[name]
      FROM sys.default_constraints dc
      JOIN sys.columns c ON c.[default_object_id] = dc.[object_id]
     WHERE dc.[parent_object_id] = OBJECT_ID(N'[Products]') AND c.[name] = N'Weight';
    IF @df_Products_Weight IS NOT NULL EXEC(N'ALTER TABLE [Products] DROP CONSTRAINT [' + @df_Products_Weight + N']');
    ALTER TABLE [Products] DROP COLUMN [Weight];
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[Products]', N'Width') IS NOT NULL
BEGIN
    DECLARE @df_Products_Width sysname;
    SELECT @df_Products_Width = dc.[name]
      FROM sys.default_constraints dc
      JOIN sys.columns c ON c.[default_object_id] = dc.[object_id]
     WHERE dc.[parent_object_id] = OBJECT_ID(N'[Products]') AND c.[name] = N'Width';
    IF @df_Products_Width IS NOT NULL EXEC(N'ALTER TABLE [Products] DROP CONSTRAINT [' + @df_Products_Width + N']');
    ALTER TABLE [Products] DROP COLUMN [Width];
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[ProductCategories]', N'Code') IS NOT NULL
BEGIN
    DECLARE @df_ProductCategories_Code sysname;
    SELECT @df_ProductCategories_Code = dc.[name]
      FROM sys.default_constraints dc
      JOIN sys.columns c ON c.[default_object_id] = dc.[object_id]
     WHERE dc.[parent_object_id] = OBJECT_ID(N'[ProductCategories]') AND c.[name] = N'Code';
    IF @df_ProductCategories_Code IS NOT NULL EXEC(N'ALTER TABLE [ProductCategories] DROP CONSTRAINT [' + @df_ProductCategories_Code + N']');
    ALTER TABLE [ProductCategories] DROP COLUMN [Code];
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[ProductCategories]', N'SortOrder') IS NOT NULL
BEGIN
    DECLARE @df_ProductCategories_SortOrder sysname;
    SELECT @df_ProductCategories_SortOrder = dc.[name]
      FROM sys.default_constraints dc
      JOIN sys.columns c ON c.[default_object_id] = dc.[object_id]
     WHERE dc.[parent_object_id] = OBJECT_ID(N'[ProductCategories]') AND c.[name] = N'SortOrder';
    IF @df_ProductCategories_SortOrder IS NOT NULL EXEC(N'ALTER TABLE [ProductCategories] DROP CONSTRAINT [' + @df_ProductCategories_SortOrder + N']');
    ALTER TABLE [ProductCategories] DROP COLUMN [SortOrder];
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[ProductCategories]', N'Valuation') IS NOT NULL
BEGIN
    DECLARE @df_ProductCategories_Valuation sysname;
    SELECT @df_ProductCategories_Valuation = dc.[name]
      FROM sys.default_constraints dc
      JOIN sys.columns c ON c.[default_object_id] = dc.[object_id]
     WHERE dc.[parent_object_id] = OBJECT_ID(N'[ProductCategories]') AND c.[name] = N'Valuation';
    IF @df_ProductCategories_Valuation IS NOT NULL EXEC(N'ALTER TABLE [ProductCategories] DROP CONSTRAINT [' + @df_ProductCategories_Valuation + N']');
    ALTER TABLE [ProductCategories] DROP COLUMN [Valuation];
END;");
        

// ===== 20260903101000_AddWorkOrderSourceLink.cs =====
            migrationBuilder.DropIndex(
                name: "IX_WorkOrders_SourceModule_SourceId",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "SourceId",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "SourceModule",
                table: "WorkOrders");
        

// ===== 20260903070000_LegacyAriaImportPrep.cs =====
            migrationBuilder.DropForeignKey(
                name: "FK_ReportWorks_Users_OperatorId",
                table: "ReportWorks");

            migrationBuilder.DropIndex(
                name: "IX_ReportWorks_OperatorId",
                table: "ReportWorks");

            migrationBuilder.DropColumn(
                name: "OperatorId",
                table: "ReportWorks");

            migrationBuilder.AlterColumn<string>(
                name: "KarshenasiAvalie",
                table: "ProjectEntryExits",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100,
                oldNullable: true);

            // bigint → time (مقادیر بیش از ۲۴ ساعت به ناچار بریده می‌شوند)
            migrationBuilder.Sql(@"
ALTER TABLE [ProjectEntryExits] ADD [TotalSpentTime_Time] time NOT NULL CONSTRAINT [DF_ProjectEntryExits_TotalSpentTime_Time] DEFAULT('00:00:00');
EXEC('UPDATE [ProjectEntryExits] SET [TotalSpentTime_Time] = DATEADD(second, ([TotalSpentTime] / 10000000) % 86400, CAST(''00:00:00'' AS time));');
ALTER TABLE [ProjectEntryExits] DROP COLUMN [TotalSpentTime];
EXEC sp_rename 'ProjectEntryExits.TotalSpentTime_Time', 'TotalSpentTime', 'COLUMN';
ALTER TABLE [ProjectEntryExits] DROP CONSTRAINT [DF_ProjectEntryExits_TotalSpentTime_Time];
");
        

// ===== 20260902150000_AddPushSubscriptions.cs =====
            migrationBuilder.DropTable(name: "PushSubscriptions");
        

// ===== 20260902130000_AddBayeganiLetterId.cs =====
            migrationBuilder.DropColumn(
                name: "LetterId",
                table: "LetterBayeganis");
        

// ===== 20260902120000_AddLetterNeshan.cs =====
            migrationBuilder.DropColumn(
                name: "IsNeshan",
                table: "InnerLetters");
        

// ===== 20260902120000_AddAttendanceSecurity.cs =====
            migrationBuilder.DropTable(
                name: "AttendanceAlerts");

            migrationBuilder.DropTable(
                name: "AttendanceAreaSettings");

            migrationBuilder.DropTable(
                name: "UserDevices");
        

// ===== 20260902110000_AddLetterStrature.cs =====
            migrationBuilder.DropTable(
                name: "LetterStratures");
        

// ===== 20260902100000_AddOutgoingDabirkhaneAndLetterhead.cs =====
            migrationBuilder.DropColumn(name: "CompanyId", table: "OutgoingLetters");
            migrationBuilder.DropColumn(name: "DabirkhaneSabt", table: "OutgoingLetters");
            migrationBuilder.DropColumn(name: "DabirkhaneUserId", table: "OutgoingLetters");
            migrationBuilder.DropColumn(name: "DateDabirkhane", table: "OutgoingLetters");
            migrationBuilder.DropColumn(name: "DestRegNumber", table: "OutgoingLetters");
            migrationBuilder.DropColumn(name: "SendMethod", table: "OutgoingLetters");
            migrationBuilder.DropColumn(name: "DabirkhaneNote", table: "OutgoingLetters");
            migrationBuilder.DropColumn(name: "LetterheadFileName", table: "SystemCompanies");
        

// ===== 20260902000000_AddOutgoingSignersAndSadereNumber.cs =====
            migrationBuilder.DropTable(name: "OutgoingLetterSigners");

            migrationBuilder.DropIndex(name: "IX_OutgoingLetters_SadereNumber", table: "OutgoingLetters");

            migrationBuilder.DropColumn(name: "SadereNumber", table: "OutgoingLetters");
            migrationBuilder.DropColumn(name: "DateSadere", table: "OutgoingLetters");
        

// ===== 20260901090000_AddSplitShiftWindows.cs =====
            migrationBuilder.DropColumn(
                name: "StartTime2",
                table: "ShiftGroups");

            migrationBuilder.DropColumn(
                name: "EndTime2",
                table: "ShiftGroups");
        

// ===== 20260901042209_AddAuditLogAndDeviceTracking.cs =====
            migrationBuilder.DropTable(
                name: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "EnterDevice",
                table: "AttendanceSegments");
        

// ===== 20260901000000_AddOutgoingLetters.cs =====
            migrationBuilder.DropTable(
                name: "OutgoingLetters");

            migrationBuilder.DropTable(
                name: "OutgoingPishnevisLetters");
        

// ===== 20260831103000_AddOfficeAutomation.cs =====
            migrationBuilder.DropTable(
                name: "Erjas");

            migrationBuilder.DropTable(
                name: "InnerLetters");

            migrationBuilder.DropTable(
                name: "LetterBayeganis");

            migrationBuilder.DropTable(
                name: "PishnevisLetters");

            migrationBuilder.DropTable(
                name: "RelatedLetters");

            migrationBuilder.DropTable(
                name: "Amalgars");

            migrationBuilder.DropTable(
                name: "LetterSources");



            migrationBuilder.DropTable(
                name: "LetterGroupMembers");

            migrationBuilder.DropTable(
                name: "LetterGroups");

        

// ===== 20260831102303_AddCalendarSettingsAndOfficialHolidays.cs =====
            migrationBuilder.DropTable(
                name: "WorkCalendarSettings");

            migrationBuilder.DropColumn(
                name: "IsOfficial",
                table: "CompanyHolidays");
        

// ===== 20260831062257_AddOvertimeModesToWorkCalendar.cs =====
            migrationBuilder.DropColumn(
                name: "OvertimeEnd",
                table: "WorkCalendarDays");

            migrationBuilder.DropColumn(
                name: "OvertimeMode",
                table: "WorkCalendarDays");

            migrationBuilder.DropColumn(
                name: "OvertimeStart",
                table: "WorkCalendarDays");
        

// ===== 20260831041448_AddWorkCalendarAndOvertime.cs =====
            migrationBuilder.DropTable(
                name: "WorkCalendarDays");

            migrationBuilder.DropColumn(
                name: "IsUnauthorized",
                table: "AttendanceSegments");

            migrationBuilder.DropColumn(
                name: "OvertimeMinutes",
                table: "AttendanceSegments");

            migrationBuilder.DropColumn(
                name: "OvertimeMinutes",
                table: "AttendanceRecords");

            migrationBuilder.DropColumn(
                name: "UnauthorizedMinutes",
                table: "AttendanceRecords");
        

// ===== 20260826101303_AddCompanyHolidaysAndAdminLeave.cs =====
            migrationBuilder.DropTable(
                name: "CompanyHolidays");

            migrationBuilder.DropColumn(
                name: "AdminCreated",
                table: "LeaveRequests");
        

// ===== 20260826044011_AddHourlyTimesAndAttendanceSegments.cs =====
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[dbo].[AttendanceSegments]', N'U') IS NOT NULL
    DROP TABLE [dbo].[AttendanceSegments];

IF COL_LENGTH(N'[dbo].[LeaveRequests]', N'EndTime') IS NOT NULL
    ALTER TABLE [dbo].[LeaveRequests] DROP COLUMN [EndTime];

IF COL_LENGTH(N'[dbo].[LeaveRequests]', N'StartTime') IS NOT NULL
    ALTER TABLE [dbo].[LeaveRequests] DROP COLUMN [StartTime];

IF COL_LENGTH(N'[dbo].[AttendanceRecords]', N'CoveredGapMinutes') IS NOT NULL
    ALTER TABLE [dbo].[AttendanceRecords] DROP COLUMN [CoveredGapMinutes];

IF EXISTS (
    SELECT 1 FROM sys.columns c
    JOIN sys.types t ON c.user_type_id = t.user_type_id
    WHERE c.object_id = OBJECT_ID(N'[dbo].[LeaveRequests]')
      AND c.name = N'Hours' AND t.name = 'float'
)
    ALTER TABLE [dbo].[LeaveRequests] ALTER COLUMN [Hours] int NOT NULL;
");
        

// ===== 20260826035600_AddShiftGroupsAndAttendance.cs =====
            migrationBuilder.DropForeignKey(
                name: "FK_Users_ShiftGroups_ShiftGroupId",
                table: "Users");

            migrationBuilder.DropTable(
                name: "AttendanceRecords");

            migrationBuilder.DropTable(
                name: "ShiftGroups");

            migrationBuilder.DropIndex(
                name: "IX_Users_ShiftGroupId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ShiftGroupId",
                table: "Users");
        

// ===== 20260825121203_AddProjectFlowCartableAndAttachFolders.cs =====
            migrationBuilder.DropColumn(
                name: "ExpertActionAt",
                table: "ProjectEntryExits");

            migrationBuilder.DropColumn(
                name: "ExpertActionById",
                table: "ProjectEntryExits");

            migrationBuilder.DropColumn(
                name: "ExpertNote",
                table: "ProjectEntryExits");

            migrationBuilder.DropColumn(
                name: "FlowStatus",
                table: "ProjectEntryExits");

            migrationBuilder.DropColumn(
                name: "ManagerActionAt",
                table: "ProjectEntryExits");

            migrationBuilder.DropColumn(
                name: "ManagerActionById",
                table: "ProjectEntryExits");

            migrationBuilder.DropColumn(
                name: "ManagerNote",
                table: "ProjectEntryExits");
        

// ===== 20260825045236_AddLeaveRequests.cs =====
            migrationBuilder.DropTable(
                name: "LeaveRequests");
        

// ===== 20260824081257_AddCodeProject.cs =====
            migrationBuilder.DropIndex(
                name: "IX_ProjectEntryExits_CodeProject",
                table: "ProjectEntryExits");

            migrationBuilder.DropColumn(
                name: "CodeProject",
                table: "ReportWorks");

            migrationBuilder.DropColumn(
                name: "CodeProject",
                table: "ProjectEntryExits");
        

// ===== 20260824075826_AddUserPhotosAndFileStore.cs =====
            migrationBuilder.DropColumn(
                name: "FilePath",
                table: "WorkOrderAttachments");

            migrationBuilder.DropColumn(
                name: "PhotoPath",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PhotoPath",
                table: "SystemUsers");

            migrationBuilder.DropColumn(
                name: "FilePath",
                table: "ItRequestAttachments");

            migrationBuilder.DropColumn(
                name: "FilePath",
                table: "AppAttachments");
        

// ===== 20260824051902_ProjectFactorOptional.cs =====
            migrationBuilder.AlterColumn<int>(
                name: "FactorTypeId",
                table: "ProjectEntryExits",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);
        

// ===== 20260824050941_AddSystemIdModuleFeatures.cs =====
            migrationBuilder.DropTable(
                name: "SystemHandovers");

            migrationBuilder.DropTable(
                name: "SystemInfoUserHistories");

            migrationBuilder.DropTable(
                name: "SystemRemoteCommands");

            migrationBuilder.DropColumn(
                name: "SmartStatus",
                table: "SystemDisks");

            migrationBuilder.DropColumn(
                name: "SmartUpdatedAt",
                table: "SystemDisks");
        

// ===== 20260823120204_AddProjectManagement.cs =====
            migrationBuilder.DropTable(
                name: "ProjectAttaches");

            migrationBuilder.DropTable(
                name: "ReportWorks");

            migrationBuilder.DropTable(
                name: "ProjectEntryExits");

            migrationBuilder.DropTable(
                name: "KarFarmas");

            migrationBuilder.DropTable(
                name: "TypeFactors");
        

// ===== 20260823094428_AddArchiveAndAttachments.cs =====
            migrationBuilder.DropTable(
                name: "AppAttachments");

            migrationBuilder.DropTable(
                name: "ArchiveFolders");

            migrationBuilder.DropTable(
                name: "ArchiveItems");

            migrationBuilder.DropColumn(
                name: "FirstName",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "LastName",
                table: "Users");
        

// ===== 20260823085629_AddWorkOrders.cs =====
            migrationBuilder.DropTable(
                name: "WorkOrderAllowedAssignees");

            migrationBuilder.DropTable(
                name: "WorkOrderAssignees");

            migrationBuilder.DropTable(
                name: "WorkOrderAttachments");

            migrationBuilder.DropTable(
                name: "WorkOrderLogs");

            migrationBuilder.DropTable(
                name: "WorkOrders");
        

// ===== 20260823071118_AddMessengers.cs =====
            migrationBuilder.DropColumn(
                name: "BaleChatId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "EitaaChatId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Mobile",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "BaleBotToken",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "EitaaToken",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "MessengerSenderNumber",
                table: "AppSettings");
        

// ===== 20260823063932_AddItServerConfig.cs =====
            migrationBuilder.DropColumn(
                name: "ItCompanyName",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "ItServerUrl",
                table: "AppSettings");
        

// ===== 20260823052806_AddItNumberSeenReject.cs =====
            migrationBuilder.DropTable(
                name: "ItRequestSeens");

            migrationBuilder.DropColumn(
                name: "Number",
                table: "ItRequests");
        

// ===== 20260823043823_AddItWorkflowAndNotifications.cs =====
            migrationBuilder.DropTable(
                name: "AppNotifications");

            migrationBuilder.DropTable(
                name: "ItRequestLogs");

            migrationBuilder.DropColumn(
                name: "ConnectionType",
                table: "OfficeMachines");

            migrationBuilder.DropColumn(
                name: "IpAddress",
                table: "OfficeMachines");

            migrationBuilder.DropColumn(
                name: "LinkedSystemInfoId",
                table: "OfficeMachines");

            migrationBuilder.DropColumn(
                name: "LinkedSystemLabel",
                table: "OfficeMachines");

            migrationBuilder.DropColumn(
                name: "RequestType",
                table: "ItRequests");

            migrationBuilder.DropColumn(
                name: "Done",
                table: "ItRequestAssignments");

            migrationBuilder.DropColumn(
                name: "ManagerDecision",
                table: "ItRequestAssignments");

            migrationBuilder.DropColumn(
                name: "ManagerDecisionNote",
                table: "ItRequestAssignments");
        

// ===== 20260822103039_AddItRequests.cs =====
            migrationBuilder.DropTable(
                name: "ItRequestAssignments");

            migrationBuilder.DropTable(
                name: "ItRequestAttachments");

            migrationBuilder.DropTable(
                name: "ItRequests");
        

// ===== 20260822033216_AddRbacTables.cs =====
            migrationBuilder.DropTable(
                name: "RolePermissions");

            migrationBuilder.DropTable(
                name: "UserRoles");

            migrationBuilder.DropTable(
                name: "Permissions");

            migrationBuilder.DropTable(
                name: "Roles");

            migrationBuilder.DropColumn(
                name: "Username",
                table: "SystemUsers");
        

// ===== 20260819090102_AddSystemBoard.cs =====
            migrationBuilder.DropTable(
                name: "SystemBoards");
        

// ===== 20260819084326_AddSystemComponentTables.cs =====
            migrationBuilder.DropTable(
                name: "SystemCpus");

            migrationBuilder.DropTable(
                name: "SystemDisks");

            migrationBuilder.DropTable(
                name: "SystemGpus");

            migrationBuilder.DropTable(
                name: "SystemMonitors");

            migrationBuilder.DropTable(
                name: "SystemNetAdapters");

            migrationBuilder.DropTable(
                name: "SystemRams");

            migrationBuilder.DropTable(
                name: "SystemVolumes");
        

// ===== 20260819070927_AddRepairDetails.cs =====
            migrationBuilder.DropColumn(
                name: "Cost",
                table: "OfficeMachineRepairs");

            migrationBuilder.DropColumn(
                name: "GoneDate",
                table: "OfficeMachineRepairs");

            migrationBuilder.DropColumn(
                name: "PerformedWork",
                table: "OfficeMachineRepairs");

            migrationBuilder.DropColumn(
                name: "ReturnDate",
                table: "OfficeMachineRepairs");
        

// ===== 20260819065947_AddOfficeMachines.cs =====
            migrationBuilder.DropTable(
                name: "OfficeMachineCosts");

            migrationBuilder.DropTable(
                name: "OfficeMachineRepairs");

            migrationBuilder.DropTable(
                name: "OfficeMachines");
        

// ===== 20260819063240_AddCctvNvrs.cs =====
            migrationBuilder.DropTable(
                name: "CctvNvrs");

            migrationBuilder.DropColumn(
                name: "NvrId",
                table: "CctvCameras");
        

// ===== 20260819061901_AddCctvCameras.cs =====
            migrationBuilder.DropTable(
                name: "CctvCameras");
        

// ===== 20260819060845_AddSystemInfoChangeTracking.cs =====
            migrationBuilder.DropTable(
                name: "SystemInfoChangeLogs");

            migrationBuilder.DropColumn(
                name: "PendingPayloadJson",
                table: "SystemInfos");

            migrationBuilder.DropColumn(
                name: "PendingReceivedAt",
                table: "SystemInfos");
        

// ===== 20260819043459_AddSystemInfoDetails.cs =====
            migrationBuilder.DropColumn(
                name: "DetailsJson",
                table: "SystemInfos");
        

// ===== 20260819040730_AddSystemTables.cs =====
            migrationBuilder.DropTable(
                name: "SystemCompanies");

            migrationBuilder.DropTable(
                name: "SystemDepartments");

            migrationBuilder.DropTable(
                name: "SystemInfos");

            migrationBuilder.DropTable(
                name: "SystemUsers");
        

// ===== 20260816053917_AddExpenses.cs =====
            migrationBuilder.DropTable(
                name: "ExpenseCategories");

            migrationBuilder.DropTable(
                name: "Expenses");
        

// ===== 20260816051853_AddReferrerCanViewProducts.cs =====
            migrationBuilder.DropColumn(
                name: "CanViewProducts",
                table: "Referrers");
        

// ===== 20260816043917_AddCashAmountMixedPayment.cs =====
            migrationBuilder.DropColumn(
                name: "CashAmount",
                table: "Transactions");
        

// ===== 20260816042258_AddPaymentsAndRoles.cs =====
            migrationBuilder.DropTable(
                name: "Cheques");

            migrationBuilder.DropTable(
                name: "Installments");

            migrationBuilder.DropColumn(
                name: "CashType",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "DueDate",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "PaymentMethod",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "SettledAmount",
                table: "Transactions");
        

// ===== 20260816033434_AddRepairs.cs =====
            migrationBuilder.DropTable(
                name: "RepairItems");

            migrationBuilder.DropTable(
                name: "Technicians");

            migrationBuilder.DropTable(
                name: "RepairOrders");
        

// ===== 20260815091428_AddReferrerBankInfo.cs =====
            migrationBuilder.DropColumn(
                name: "CardNumber",
                table: "Referrers");

            migrationBuilder.DropColumn(
                name: "Iban",
                table: "Referrers");
        

// ===== 20260815083133_AddUsers.cs =====
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'dbo.Users', N'U') IS NOT NULL
    DROP TABLE [dbo].[Users];
");
        

// ===== 20260815072211_AddWalletWarehouseCustomerReferrer.cs =====
            migrationBuilder.DropTable(
                name: "ReferrerPayments");

            migrationBuilder.DropColumn(
                name: "WarehouseId",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "ReferrerId",
                table: "Parties");
        

// ===== 20260815065428_AddReferrerCompanyName.cs =====
            migrationBuilder.DropColumn(
                name: "CompanyName",
                table: "Referrers");
        

// ===== 20260815062907_AddSettingsReferrersServices.cs =====
            migrationBuilder.DropTable(
                name: "AppSettings");

            migrationBuilder.DropTable(
                name: "Referrers");

            migrationBuilder.DropColumn(
                name: "ReferrerId",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "IsService",
                table: "Products");
        

// ===== 20260815052346_AddUnitsAndCategoryTree.cs =====
            migrationBuilder.DropTable(
                name: "MeasureUnits");

            migrationBuilder.DropColumn(
                name: "ParentId",
                table: "ProductCategories");
        

// ===== 20260815051102_AddProductCategories.cs =====
            migrationBuilder.DropTable(
                name: "ProductCategories");
        

// ===== 20260814161634_InitialCreate.cs =====
            migrationBuilder.DropTable(
                name: "Parties");

            migrationBuilder.DropTable(
                name: "Products");

            migrationBuilder.DropTable(
                name: "Stocks");

            migrationBuilder.DropTable(
                name: "TransactionLines");

            migrationBuilder.DropTable(
                name: "Warehouses");

            migrationBuilder.DropTable(
                name: "Transactions");
        
        
            }

            M_20260911213808_AddHrPerfPhase4();
            M_20260911211508_AddHrPayFilingPhase3();
            M_20260911200116_AddHrAttendPhase2();
            M_20260911183156_AddHrLegalPhase1();
            M_20260911173039_AddHrUserLink();
            M_20260911163445_AddHrTimePayModules();
            M_20260911141302_AddHrCoreModule();
            M_20260911100108_SquashedInitial();
        }
    }
}
