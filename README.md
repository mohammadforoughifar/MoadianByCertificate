# سامانه جامع انبار و فروش

نرم‌افزار مدیریت انبار، خرید و فروش — ساخته‌شده با **.NET 8** و **Blazor WebAssembly (PWA)** به همراه **Web API** و پایگاه داده **SQL Server**.

## امکانات

| بخش | توضیح |
|---|---|
| 🏠 داشبورد | ارزش موجودی، فروش/خرید امروز و ماه جاری، آخرین فعالیت‌ها، هشدار کسری موجودی |
| 📦 تعریف کالا | کد، نام، گروه، واحد، بارکد، قیمت خرید/فروش، **نقطه سفارش** و حداکثر موجودی |
| 🏢 تعریف انبار | چند انبار با موجودی مجزا |
| 👥 طرف حساب | مشتریان و تأمین‌کنندگان به‌همراه مانده حساب |
| 🛒 خرید و فروش | فاکتور چندسطری با کنترل موجودی، قیمت پیشنهادی و شماره سند خودکار |
| 📊 موجودی انبار | موجودی لحظه‌ای به تفکیک انبار + اصلاح/شمارش موجودی |
| 📒 کاردکس کالا | ریز گردش ورود/خروج با مانده، فیلتر تاریخ و انبار + چاپ |
| ⚠️ گزارش نقطه سفارش | اقلام زیر حداقل موجودی با مقدار پیشنهادی خرید |
| 🏢 منابع انسانی | مرخصی/حضور مجموعه + **منابع انسانی بن‌سازه** در منوی کلاینت (`/bon-hr`) با API زیر `/radis-hr/api` |
| 📅 تاریخ شمسی | تقویم جلالی قابل انتخاب + امکان **تایپ دستی تاریخ** (مثل 1403/05/12) |
| 📱 PWA | قابل نصب روی موبایل و دسکتاپ، کارکرد آفلاین نسبی (کش) |
| 🎨 ظاهر حرفه‌ای | قالب ادمین RTL با فونت **وزیرمتن** و آیکون‌های Bootstrap |

## ساختار پروژه

```
Inventory.sln
├── src/Inventory.Shared    → مدل‌های مشترک (DTO) + ابزار تاریخ شمسی (الگوریتم جلالی)
├── src/Inventory.Api       → Web API (.NET 8) + EF Core + SQL Server
│   ├── Data/               → موجودیت‌ها، DbContext، مایگریشن و داده اولیه
│   ├── Services/           → منطق انبار، کاردکس، نقطه سفارش و داشبورد
│   └── Endpoints/          → Minimal API
├── src/Inventory.Client    → Blazor WebAssembly (PWA) — رابط کاربری
├── sql/                    → اسکریپت آماده ساخت دیتابیس (InventoryDb-Schema.sql)
└── tests/                  → اسکریپت تست کامل API (api_test.py)
```

## پایگاه داده

**پیش‌فرض: SQL Server** — رشته اتصال در `src/Inventory.Api/appsettings.json`:

```json
"Database": { "Provider": "SqlServer" },
"ConnectionStrings": {
  "Default": "Server=localhost;Database=InventoryDb;User Id=sa;Password=YourStrong!Passw0rd;TrustServerCertificate=True;Encrypt=False"
}
```

### روش‌های ساخت دیتابیس

**روش ۱ — خودکار (پیشنهادی):** کافی است API را اجرا کنید؛ مایگریشن‌ها به‌صورت خودکار اجرا شده و دیتابیس `InventoryDb` ساخته می‌شود (در صورت وجود دسترسی CREATE DATABASE).

**روش ۲ — دستی با اسکریپت:** فایل `sql/InventoryDb-Schema.sql` را در SQL Server Management Studio (SSMS) اجرا کنید. این اسکریپت idempotent است و چندبار اجرای آن مشکلی ایجاد نمی‌کند.

### حالت توسعه با SQLite (اختیاری)

برای اجرای محلی بدون SQL Server، فقط دو متغیر محیطی تنظیم کنید:

```bash
# لینوکس / مک
Database__Provider=Sqlite ConnectionStrings__Default="Data Source=inventory.db" dotnet run

# ویندوز (PowerShell)
$env:Database__Provider="Sqlite"; $env:ConnectionStrings__Default="Data Source=inventory.db"; dotnet run
```

## پیش‌نیازها

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- SQL Server (یا LocalDB/Express) برای حالت پیش‌فرض

## اجرا

### روش ۱ — تک‌سروره (پیشنهادی برای ویندوز: فقط یک پنجره)

```powershell
# در ریشه پروژه (پوشه inventory):
.\deploy-single.ps1          # پابلیش کلاینت و کپی در wwwroot مربوط به API

cd src\Inventory.Api
dotnet run
```

سپس مرورگر را باز کنید: **http://localhost:5100** (هم رابط کاربری و هم API از همین آدرس سرو می‌شوند).

> در لینوکس/مک به‌جای آن: `./deploy-single.sh`

### روش ۲ — دو پنجره جدا (مناسب توسعه)

**پنجره ۱ — API (پورت 5100):**
```bash
cd src/Inventory.Api
dotnet run
```

**پنجره ۲ — رابط کاربری (پورت 5210):**
```bash
cd src/Inventory.Client
dotnet run
```

سپس مرورگر را باز کنید: **http://localhost:5210**

> **نکته:** اگر فقط API را اجرا کنید و آدرس `http://localhost:5100` را باز کنید، چیزی نمایش داده نمی‌شود؛ چون رابط کاربری یک پروژه جداگانه است و فقط با «روش ۱» یا اجرای پروژه Client در دسترس قرار می‌گیرد.

## خروجی نهایی (Publish)

```bash
dotnet publish src/Inventory.Client -c Release -o publish/client
dotnet publish src/Inventory.Api -c Release -o publish/api
```

### استقرار تک‌سروره (همه‌چیز از یک پورت)

- **ویندوز:** `.\deploy-single.ps1`
- **لینوکس/مک:** `./deploy-single.sh`

سپس `cd src/Inventory.Api && dotnet run` و باز کردن `http://localhost:5100`

## تست خودکار

اسکریپت تست کامل (۴۰ مورد) شامل CRUD کالا/انبار/طرف حساب، خرید و فروش، کنترل موجودی منفی، کاردکس، نقطه سفارش و داشبورد:

```bash
# ابتدا API را با SQLite اجرا کنید (برای محیط تست)
Database__Provider=Sqlite ConnectionStrings__Default="Data Source=inventory.db" dotnet run

# سپس تست را اجرا کنید
python3 tests/api_test.py
```

## فناوری‌ها

- .NET 8 ، ASP.NET Core Minimal API
- Blazor WebAssembly (Standalone + PWA)
- Entity Framework Core + **SQL Server** (پشتیبانی از SQLite برای توسعه/تست)
- Bootstrap 5.3 + Bootstrap Icons (به‌صورت آفلاین داخل پروژه)
- فونت وزیرمتن (Vazirmatn)
- تاریخ شمسی با الگوریتم استاندارد جلالی (بدون کتابخانه خارجی)

## رفع اشکال

### خطای `Metadata file '...Inventory.Shared.dll' could not be found`

پوشه‌های `bin` و `obj` را حذف کنید و Solution را Rebuild کنید. جزئیات در بخش همین خطا در `RUN-GUIDE.md` است.

### خطای `icudt_EFIGS.dat` هنگام بوت (صفحه سفید)

این مشکل شناخته‌شده پابلیش Blazor است و در این پروژه با `InvariantGlobalization=true` کاملاً غیرفعال شده است (به ICU نیازی نداریم چون تاریخ شمسی و اعداد فارسی را خود برنامه پیاده‌سازی می‌کند).

### پاک کردن کش مرورگر (PWA)

بعد از هر به‌روزرسانی، یک‌بار کش مرورگر را خالی کنید (Ctrl+Shift+R).

### خطای اتصال به SQL Server

اگر خطای اتصال دیدید، بررسی کنید:
1. سرویس SQL Server در حال اجرا باشد.
2. رشته اتصال (نام سرور، کاربر، رمز) صحیح باشد.
3. اگر از SQL Server محلی بدون کاربر/رمز استفاده می‌کنید (Windows Auth):
   `Server=.;Database=InventoryDb;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False`

### خطای `Login failed for user 'sa'` با وجود تغییر رشته اتصال

اگر رشته اتصال را عوض کرده‌اید ولی برنامه همچنان با کاربر `sa` تلاش می‌کند، علت این است که هنگام اجرا با `dotnet run` یا Visual Studio، محیط **Development** فعال است و فایل **`appsettings.Development.json`** روی **`appsettings.json`** غلبه می‌کند. بنابراین:

- **هر دو فایل** `appsettings.json` و `appsettings.Development.json` را ویرایش کنید.
- در زمان راه‌اندازی، لاگ `[DB] ConnectionString = ...` نمایش داده می‌شود؛ آن را بررسی کنید تا مطمئن شوید برنامه از کدام رشته اتصال استفاده می‌کند (رمز عبور در لاگ پوشیده می‌شود).

### فعال‌سازی ورود SQL Server (در صورت نیاز به کاربر sa)

اگر می‌خواهید به‌جای Windows Auth از کاربر `sa` استفاده کنید:
1. در SSMS با Windows Authentication وصل شوید.
2. راست‌کلیک روی سرور → Properties → تب Security → گزینه `SQL Server and Windows Authentication mode`.
3. در Security → Logins روی `sa` راست‌کلیک → Properties → تب Status → Enabled.
4. سرویس SQL Server را ریاستارت کنید.
