# ماژول انبارداری (Warehousing)

این ماژول به‌صورت کامل داخل همان راه‌حل موجود (`Inventory.sln`) و با همان الگوهای پروژه پیاده‌سازی شده است:
`Inventory.Shared` (DTO و Enum) → `Inventory.Api` (Entity، Service، Controller، Migration) → `Inventory.Client` (سرویس‌های API و صفحات Blazor).

پایگاه‌داده **SQL Server** و از همان `AppDbContext` و مکانیزم Migrations استفاده می‌کند.

---

## ۱) قابلیت‌های پیاده‌سازی‌شده

| # | خواسته | وضعیت | مسیر صفحه |
|---|--------|-------|-----------|
| ۱ | تعریف کالا با همه فیلدها + **کد مالیاتی** + بخش **ویژگی‌ها**، به‌صورت **فرم چند مرحله‌ای/تب‌دار** | ✅ | `/inv/products` |
| ۲ | مدیریت گروه کالا | ✅ | `/inv/categories` |
| ۳ | گروه کالا به‌صورت **درختی و زیبا** (کشویی، جستجوی خودکار با باز شدن مسیر، پنل جزئیات، جابه‌جایی ترتیب) | ✅ | `/inv/categories` |
| ۴ | مدیریت انبارها | ✅ | `/inv/warehouses` |
| ۵ | روش قیمت‌گذاری **میانگین / FIFO / LIFO** در سطح **کالا** و **گروه کالا** (با ارث‌بری) | ✅ | `/inv/products`، `/inv/categories` |
| ۶ | بخش عملیات: اسناد **رسید و حواله** با انواع مختلف | ✅ | `/inv/docs`، `/inv/docs/new`، `/inv/docs/edit/{id}` |
| ۷ | مدیریت **نوع رسید/حواله** با **ماهیت** افزایشی/کاهشی/خنثی | ✅ | `/inv/doc-types` |
| ۸ | گزارش **کاردکس کالا** (مقداری و ریالی) | ✅ | `/inv/kardex` |
| + | ویژگی‌های کالا (تعریف مشخصه‌ها) | ✅ | `/inv/attributes` |
| + | گزارش موجودی انبار | ✅ | `/inv/stock` |

---

## ۲) فایل‌های افزوده‌شده

### Inventory.Shared
- `Dtos/WarehousingDtos.cs` — `InvCategory`, `InvCategoryMove`, `InvAttribute`, `InvAttributeOption`,
  `InvProductAttrValue`, `InvProduct`, `InvWarehouse`, `InvDocType`, `InvDoc`, `InvDocLine`,
  `InvKardexRow`, `InvKardexResult`, `InvStockRow`.
- `Enums.cs` — افزوده شد: `ValuationMethod`, `StockNature`, `InvDocStatus`, `AttrValueType`, `WarehouseKind`.

### Inventory.Api
- `Entities/Warehousing/*.cs` — جدول‌های `InvCategories`, `InvAttributes`, `InvAttributeOptions`,
  `InvProducts`, `InvProductAttrValues`, `InvWarehouses`, `InvDocTypes`, `InvDocs`, `InvDocLines`, `InvStocks`, `InvCostLayers`.
- `Services/Warehousing/IWarehousingService.cs` + `WarehousingService.cs` — همه منطق کسب‌وکار:
  درخت گروه‌ها، ارث‌بری روش قیمت‌گذاری، شماره‌گذاری خودکار سند، قطعی/برگشت از قطعی،
  لایه‌های بهای تمام‌شده (FIFO/LIFO) و میانگین موزون، کاردکس، موجودی.
- `Controllers/Warehousing/InvBaseDataController.cs` — گروه‌ها، ویژگی‌ها، انبارها.
- `Controllers/Warehousing/InvProductsController.cs` — کالاها.
- `Controllers/Warehousing/InvDocsController.cs` — انواع سند، اسناد، گزارش‌ها.
- `Data/AppDbContext.cs` — `DbSet`ها و پیکربندی روابط/ایندکس‌ها.
- `Data/DbInitializer.cs` — ایجاد ۱۲ نوع سند پیش‌فرض.
- `Data/RbacSeeder.cs` — ماژول‌های دسترسی `InvDocs` و `InvDocTypes`.
- `Migrations/20260905154353_AddWarehousingModule.*` — مهاجرت پایگاه‌داده.
- `sql/warehousing/AddWarehousingModule.sql` — اسکریپت **idempotent** برای اجرای مستقیم روی SQL Server.

### Inventory.Client
- `Services/IWarehousingApi.cs` + `Services/WarehousingApiServices.cs` — ۷ سرویس (تنها جایی که آدرس API نوشته می‌شود).
- `Program.cs` — ثبت ۷ سرویس در DI.
- `Pages/Warehousing/InvCategories.razor` — درخت گروه کالا.
- `Pages/Warehousing/InvProducts.razor` — فهرست + فرم ۵ مرحله‌ای تعریف کالا.
- `Pages/Warehousing/InvAttributes.razor` — ویژگی‌های کالا.
- `Pages/Warehousing/InvWarehouses.razor` — انبارها.
- `Pages/Warehousing/InvDocTypes.razor` — نوع رسید/حواله و ماهیت.
- `Pages/Warehousing/InvDocs.razor` — فهرست اسناد.
- `Pages/Warehousing/InvDocForm.razor` — فرم ثبت سند با گرید اقلام.
- `Pages/Warehousing/InvKardex.razor` — کاردکس.
- `Pages/Warehousing/InvStock.razor` — موجودی.
- `Layout/NavMenu.razor` — آیتم‌های منو در گروه‌های «عملیات»، «گزارش‌ها» و «اطلاعات پایه».
- `wwwroot/css/app.css` — استایل‌های `.mtabs`, `.wiz-bar`, `.tree`, `.badge-nature-*`, `.doc-grid`, `.mini-kpi`, `.attr-box`.

---

## ۳) نقشه API

```
GET    api/inv/categories/tree?activeOnly=
GET    api/inv/categories?activeOnly=
POST   api/inv/categories
POST   api/inv/categories/move
DELETE api/inv/categories/{id}

GET    api/inv/attributes?activeOnly=&categoryId=
POST   api/inv/attributes
DELETE api/inv/attributes/{id}

GET    api/inv/warehouses?activeOnly=
GET    api/inv/warehouses/lookups
POST   api/inv/warehouses
DELETE api/inv/warehouses/{id}

GET    api/inv/products?search=&categoryId=&warehouseId=&below=&activeOnly=&page=&pageSize=
GET    api/inv/products/{id}
GET    api/inv/products/new?categoryId=
GET    api/inv/products/lookups?search=&warehouseId=
POST   api/inv/products
DELETE api/inv/products/{id}

GET    api/inv/doc-types?activeOnly=&nature=
POST   api/inv/doc-types
DELETE api/inv/doc-types/{id}

GET    api/inv/docs?nature=&docTypeId=&warehouseId=&status=&search=&from=&to=&page=&pageSize=
GET    api/inv/docs/{id}
POST   api/inv/docs
POST   api/inv/docs/{id}/confirm
POST   api/inv/docs/{id}/unconfirm
POST   api/inv/docs/{id}/cancel
DELETE api/inv/docs/{id}

GET    api/inv/reports/kardex?productId=&warehouseId=&from=&to=
GET    api/inv/reports/stock?warehouseId=&categoryId=&search=&below=&page=&pageSize=
```

---

## ۴) ماهیت سند و اثر آن بر موجودی

| ماهیت | اثر | نمونه انواع پیش‌فرض |
|-------|-----|----------------------|
| **افزایشی** (`StockNature.Increase`) | با قطعی شدن سند، مقدار به موجودی انبار **اضافه** و یک لایه بهای تمام‌شده ایجاد می‌شود | رسید خرید، رسید برگشت از فروش، رسید تولید، رسید اضافی انبارگردانی، موجودی اول دوره |
| **کاهشی** (`StockNature.Decrease`) | مقدار از موجودی **کسر** و بهای خروج طبق روش قیمت‌گذاری موثر کالا (میانگین/FIFO/LIFO) محاسبه می‌شود | حواله فروش، حواله مصرف، حواله برگشت از خرید، حواله ضایعات، کسری انبارگردانی |
| **خنثی** (`StockNature.Neutral`) | بدون اثر بر مانده کل؛ اگر `IsTransfer` باشد از انبار مبدأ کسر و به انبار مقصد اضافه می‌شود | انتقال بین انبار، سند یادداشتی |

**روش قیمت‌گذاری موثر** به ترتیب اولویت: روش کالا → روش گروه کالا (با ارث‌بری از والدها) → پیش‌فرض «میانگین موزون».

---

## ۵) اجرا

```bash
# اعمال مهاجرت
dotnet ef database update -p src/Inventory.Api -s src/Inventory.Api

# یا اجرای مستقیم اسکریپت
sqlcmd -S . -d InventoryDb -i sql/warehousing/AddWarehousingModule.sql

# اجرای پروژه
dotnet run --project src/Inventory.Api
```

---

## ۵-۱) خطای بازیابی بسته‌ها: `Unable to resolve … PackageSourceMapping is enabled`

اگر هنگام Restore خطای زیر را دیدید:

```
Unable to resolve 'QuestPDF (>= 2024.12.3)' for 'net8.0'.
PackageSourceMapping is enabled, the following source(s) were not considered: … nuget.org
```

علت، پروژه نیست؛ در **NuGet.Config سطح ماشین/ویژوال‌استودیو** بخش `<packageSourceMapping>` فعال است و
الگویی که این بسته‌ها را به `nuget.org` نگاشت کند وجود ندارد، پس nuget.org کنار گذاشته می‌شود.

راه‌حل: فایل `inventory/NuGet.config` به مخزن اضافه شده است که با `<clear />` تنظیمات ارث‌بری‌شده را
خنثی می‌کند و الگوی `*` را به nuget.org می‌سپارد. کافی است پس از `git pull`، کش را پاک و دوباره Restore کنید:

```bash
dotnet nuget locals http-cache --clear
dotnet restore Inventory.sln
```

اگر فید داخلی سازمانی دارید، آن را هم به هر دو بخش `packageSources` و `packageSourceMapping` همین فایل اضافه کنید.

---

## ۶) دو نکته مهم پیش از اجرا روی پایگاه‌داده واقعی ⚠️

1) **مهاجرت «مقاوم در برابر ناهماهنگی اسکیما» بازنویسی شد.** ✅
   `ModelSnapshot` مخزن با دیتابیس‌های واقعی همگام نبود؛ بنابراین EF علاوه بر جدول‌های انبارداری،
   عملیاتی برای `PushSubscriptions`, `ShiftGroups`, `AttendanceSegments`, `AttendanceRecords`,
   `OutgoingLetterSigners`, `CompanyHolidays`, `AuditLogs`, `WorkCalendar*`,
   `ProductAttribute*` هم تولید کرده بود — که باعث خطای زیر می‌شد:

   ```
   SqlException: Cannot drop the index 'PushSubscriptions.IX_PushSubscriptions_UserId',
   because it does not exist or you do not have permission.
   ```

   حالا کل `Up()` و `Down()` این مهاجرت به **T-SQL محافظت‌شده** تبدیل شده است؛ هر دستور
   با یکی از شرط‌های زیر پوشش داده می‌شود:

   | عملیات | نگهبان |
   |---|---|
   | `DROP INDEX` | `IF EXISTS (SELECT 1 FROM sys.indexes WHERE name=… AND object_id=OBJECT_ID(…))` |
   | `ALTER TABLE … ADD [col]` | `IF COL_LENGTH(N'[Table]', N'col') IS NULL` |
   | `CREATE TABLE` | `IF OBJECT_ID(N'[Table]', N'U') IS NULL` |
   | `CREATE INDEX` | `IF NOT EXISTS (SELECT 1 FROM sys.indexes …)` |
   | `ADD CONSTRAINT … FOREIGN KEY` | `IF OBJECT_ID(N'[FK_…]', N'F') IS NULL` |

   نتیجه: مهاجرت روی هر وضعیتی از دیتابیس اجرا می‌شود، شیءهای موجود دست‌نخورده می‌مانند و
   **چندبار هم قابل اجراست**. نسخه‌ی اصلیِ تولیدشده توسط EF برای مرجع در
   `sql/warehousing/AddWarehousingModule.original.cs.txt` نگهداری می‌شود.

   ⚠️ این مهاجرت مخصوص **SQL Server** است؛ مسیر Sqlite در `DbInitializer` از `EnsureCreated()`
   استفاده می‌کند و اصلاً مایگریشن اجرا نمی‌کند، پس تداخلی ایجاد نمی‌شود.

2) **دو مدل موجودی به‌صورت موازی وجود دارد.**
   سیستم قدیمی جدول `Stocks`/`Products` خودش را دارد و ماژول جدید جدول‌های `InvStocks`/`InvProducts`.
   برای اینکه داشبورد و گزارش‌های قدیمی خالی نشوند، در `InventoryService.RecalcStockAsync`
   موجودیِ جدید روی جدول قدیمی **آینه (mirror)** می‌شود. اگر می‌خواهید کاملاً یکپارچه شود،
   باید داده‌های `Products` قدیمی به `InvProducts` مهاجرت داده و صفحات قدیمی
   (`/products`, `/categories`, `/warehouses`, `/stock`) بازنشسته شوند.
