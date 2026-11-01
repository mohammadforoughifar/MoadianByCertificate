# نقشه‌ی ماژول‌های سورس (Module Map)

ساختار سورس بر اساس **ماژول‌های کسب‌وکار** مرتب شده است تا پیدا کردن، تغییر و مرور هر بخش راحت باشد.
**نکته:** نام‌های فضا (namespace) برای حفظ سازگاری دست نخورده ماندند؛ فقط پوشه‌ها ماژولی شدند.

## اصول
- هر ماژول = یک پوشه در `Controllers` + یک پوشه در `Entities` + (در صورت وجود) خدمات مربوطه در `Services`
- کلاس‌های مشترک/زیرساختی در روت: `ApiControllerBase.cs` (کنترلرها)، `AppDbContext.cs`، `Data/`، `Hubs/`
- `Migrations/` دست نخورده بماند (EF به ساختار پوشه حساس است)
- کلاینت بلزور: صفحه‌ها در `Pages/<ماژول>/`؛ صفحه‌های منتقل‌شده `@namespace Inventory.Client.Pages` دارند تا namespace یکسان بماند

## جدول ماژول‌ها

| ماژول | کنترلرها (API/Controllers/) | انتیتی‌ها (API/Entities/) | خدمات | صفحه‌ها (Client/Pages/) |
|---|---|---|---|---|
| **Core** (هسته) | Auth | User, AppSetting | AuthService | — (Login در Shared) |
| **Catalog** (کاتالوگ) | Products, Categories, Units, Parties, Warehouses, Stock | Product, ProductCategory, MeasureUnit, Party, Warehouse, Stock, Referrer, ReferrerPayment | InventoryService | Products, Categories, Units, Parties, Warehouses, Stock, Cartable, Referrers, ReferrerWallets |
| **Sales** (فروش) | Orders | Transaction, TransactionLine, Cheque, InstallmentLine | — | Orders/ |
| **Finance** (مالی) | Expenses | Expense, ExpenseCategory | ExpenseService | Expenses |
| **Repairs** (تعمیرات) | Repairs | RepairOrder, RepairItem, Technician | RepairService | Repairs |
| **Hr** (منابع انسانی فعلی) | Attendance, LeaveRequests | AttendanceRecord, AttendanceSegment, ShiftGroup, LeaveRequest, CompanyHoliday | AttendanceRecalcService | Attendance, AttendanceAdmin, Leave, HrPanel |
| **BonHr** (منابع انسانی بن‌سازه) | `Api/Modules/RadisHr/Controllers` (پیشوند `/radis-hr`) | `Api/Modules/RadisHr/Shared/Models` | Payroll/Attendance در همان پوشه | `Client/Pages/BonHr` در منوی مجموعه |
| **ItAssets** (دارایی‌های IT) | ItRequests, WorkOrders, SystemInfo | SystemInfo, SystemInfoChangeLog, SystemComponents, SystemIdModule, ItRequest, WorkOrder | SystemHealth, SystemInfoPdf | ItRequests, WorkOrders, SystemId, SystemInfoList, NetworkScan, PublicRequest |
| **Cctv** (دوربین) | CctvCameras, CctvNvrs, CctvScan, NetworkScan | CctvCamera, CctvNvr | — | CctvCameras, CctvNvrs |
| **Office** (لوازم اداری + اتوماسیون) | OfficeMachines, InnerLetters | OfficeMachine, LetterSource, InnerLetter, Erja, Amalgar, PishnevisLetter, RelatedLetter, LetterBayegani, LetterGroup, LetterGroupMember | InnerLetterService, ErjaService, PishnevisService, LetterGroupService | OfficeMachines, Office/Letters/ (LetterCartable, ComposeLetterModal, ErjaSheet, GardeshModal, LetterPickCombo, LetterView) |
| **Reports** (گزارشات) | Reports | — | — | Reports/ |
| **System** (سیستم/سازمان) | SystemCompanies, SystemDepartments, SystemUsers, Roles, Permissions, Settings, Notifications, Dashboard, Archive | SystemCompany, SystemDepartment, SystemUser, ArchiveAndAttachments | FileStore, UserPhotoService, MessengerService | SystemUsers, SystemCompany, SystemDepartment, MyArchive, Users, Settings/ |
| **Dashboards** (لندینگ) | — (در System) | — | — | DashboardLive, DashboardHardware (در روت Pages) |
| **DevTeam** (میز کار توسعه) | `Controllers/DevTeam` (پیشوند `/api/dev-team`) | `Entities/DevTeam` — DtTask, DtProblem, DtSprint, DtModuleChange, … | `Services/DevTeam` | `Pages/DevTeam` — برد کانبان، مشکلات، changelog، داشبورد تیم |

## زیرساخت (ماژول‌دار نیست)
- `API/Data/` — DbContext، Initializer، Seederها
- `API/Hubs/` — SignalR (Dashboard, Notify)
- `Client/Layout/`، `Client/Shared/`، `Client/Services/`
- `Inventory.Shared/` — DTO و انتیتی‌های RBAC مشترک
- `Migrations/` — فقط از طریق `dotnet ef` تغییر کند

## راهنمای تغییرات
- **افزودن فیلد/نقش جدید در یک ماژول:** فقط پوشه‌ی همان ماژول در هر سه لایه (Controller/Entity/Page) + یک `dotnet ef migrations add`
- **جست‌وجو:** `grep -r "نام" src/Inventory.Api/Controllers/Hr/`
- بعد از هر تغییر: `dotnet build Inventory.sln` (باید 0 خطا باشد)
