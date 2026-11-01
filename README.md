# نرم‌افزار یکپارچه فروغ آریا (`TotallSoftwareForoughAriya`)

نرم‌افزار جامع مدیریت انبار، خرید و فروش، حسابداری، خزانه‌داری، اتوماسیون اداری، منابع انسانی، مدیریت دارایی‌های فناوری اطلاعات و **سامانه مودیان مالیاتی مبتنی بر گواهی امضا (SDK 2.0.29)** — ساخته‌شده با **.NET 8** و **Blazor WebAssembly (PWA)** به همراه **ASP.NET Core Web API** و پایگاه داده **SQL Server**.

## امکانات کلیدی

| بخش | توضیح |
|---|---|
| 🏠 داشبورد | ارزش موجودی، فروش/خرید روزانه و ماهانه، گزارش‌ساز و داشبورد شخصی، هشدار کسری موجودی |
| 📦 انبار و کالا | کد، نام، گروه، واحد، بارکد، قیمت خرید/فروش، نقطه سفارش، انبارگردانی و کاردکس |
| 🛒 خرید، فروش و ویزیتوری | پیش‌فاکتور، فاکتور چندسطری، کنترل موجودی، کارتابل سفارش ویزیتور و چندشرکتی |
| 🧾 سامانه مودیان (`SDK 2.0.29`) | امضای دیجیتال با گواهی امضا (`PEM/CRT/PFX` و توکن سخت‌افزاری `PKCS#11`)، ارسال صورتحساب نوع ۱ و ۲ (الگوهای ۱ تا ۷)، استعلام وضعیت کارپوشه و ثبت پرداخت |
| 📒 حسابداری و خزانه‌داری | اسناد حسابداری، ابعاد تفصیلی، بودجه، دارایی ثابت، چک، بانک و صندوق |
| 🏢 اتوماسیون اداری و نامه‌ها | کارتابل نامه‌های داخلی و صادره، چاپ رسمی با سربرگ شرکت، آرشیو اسناد و OCR |
| 👥 منابع انسانی | حضور و غیاب، مرخصی، تقویم کاری و ماژول منابع انسانی بن‌سازه (`/bon-hr`) |
| 🤖 هوش مصنوعی لوکال | دستیار هوشمند مرکزی، خلاصه‌سازی نامه و تبدیل صوت به متن (Ollama / Whisper) |
| 📅 تاریخ شمسی و PWA | تقویم استاندارد جلالی، قالب RTL با فونت وزیرمتن و قابلیت نصب PWA |

## ساختار پروژه

```text
TotallSoftwareForoughAriya.sln
├── src/TotallSoftwareForoughAriya.Shared   → مدل‌های مشترک (DTO)، قراردادها و ابزار تاریخ شمسی
├── src/TotallSoftwareForoughAriya.Api      → Web API (.NET 8) + EF Core + SQL Server + Moadian SDK 2.0.29
│   ├── Controllers/                        → کنترلرهای REST با کنترل دسترسی ماژولار (RBAC)
│   ├── Data/                               → موجودیت‌ها، DbContext، مایگریشن‌ها و ارتقای خودکار اسکیما
│   └── Services/                           → منطق تجاری ماژول‌ها و درگاه گواهی امضای مودیان
├── src/TotallSoftwareForoughAriya.Client   → Blazor WebAssembly (PWA) — رابط کاربری یکپارچه
├── src/Agent.Console                       → ایجنت جمع‌آوری اطلاعات سخت‌افزار و چاپگرهای شبکه
├── packages/                               → پکیج محلی TaxCollectData.Library 2.0.29
├── sql/                                    → اسکریپت‌های کمکی و ارتقای دستی SQL Server (Idempotent)
└── tests/                                  → تست‌های صحت‌سنجی ساختار، استقرار دیتابیس و چاپ
```

## پایگاه داده

**پیش‌فرض: SQL Server** — نام دیتابیس پیش‌فرض **`TotallSoftwareForoughAriya`** در `src/TotallSoftwareForoughAriya.Api/appsettings.json`:

```json
"Database": { "Provider": "SqlServer" },
"ConnectionStrings": {
  "Default": "Server=.;Database=TotallSoftwareForoughAriya;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False"
}
```

### روش‌های ساخت و ارتقای دیتابیس

**روش ۱ — خودکار (پیشنهادی):** کافی است API را اجرا کنید؛ مایگریشن‌ها و ارتقاهای اسکیما به‌صورت خودکار اجرا شده و دیتابیس **`TotallSoftwareForoughAriya`** ساخته یا به‌روزرسانی می‌شود (در صورت وجود مجوز `CREATE DATABASE`).

**روش ۲ — دستور نگهداری خط فرمان:**
```powershell
.\update-database.bat
```

**روش ۳ — اسکریپت‌های دستی SQL Server:** در محیط‌هایی که کاربر رشته اتصال مجوز DDL ندارد، اسکریپت‌های داخل پوشه `sql/` (مانند `sql/MoadianCertV2-SDK-2.0.29.sql` و `sql/UserDashboards.sql`) را روی دیتابیس `TotallSoftwareForoughAriya` در SSMS اجرا کنید.

### حالت توسعه با SQLite (اختیاری)

برای اجرای محلی بدون SQL Server:

```bash
# لینوکس / مک
Database__Provider=Sqlite ConnectionStrings__Default="Data Source=TotallSoftwareForoughAriya.db" dotnet run --project src/TotallSoftwareForoughAriya.Api

# ویندوز (PowerShell)
$env:Database__Provider="Sqlite"; $env:ConnectionStrings__Default="Data Source=TotallSoftwareForoughAriya.db"; dotnet run --project src/TotallSoftwareForoughAriya.Api
```

## پیش‌نیازها

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- SQL Server (یا LocalDB / SQL Server Express) برای حالت پیش‌فرض

## بیلد و اجرا

```powershell
dotnet restore TotallSoftwareForoughAriya.sln
dotnet build TotallSoftwareForoughAriya.sln -c Release
```

### روش ۱ — تک‌سروره (پیشنهادی برای استقرار: یک پورت یکپارچه)

```powershell
.\deploy-single.ps1          # پابلیش کلاینت و API در publish\app
cd publish\app
.\TotallSoftwareForoughAriya.Api.exe
```

سپس مرورگر را باز کنید: **http://localhost:5100** (هم رابط کاربری و هم API از همین آدرس سرو می‌شوند).
در لینوکس/مک می‌توانید از `./deploy-single.sh` استفاده کنید.

### روش ۲ — دو پنجره جدا (مناسب توسعه)

**پنجره ۱ — API (پورت 5100):**
```bash
cd src/TotallSoftwareForoughAriya.Api
dotnet run
```

**پنجره ۲ — رابط کاربری (پورت 5210):**
```bash
cd src/TotallSoftwareForoughAriya.Client
dotnet run
```

سپس مرورگر را باز کنید: **http://localhost:5210**

## رفع اشکال متداول

### خطای `Metadata file '...TotallSoftwareForoughAriya.Shared.dll' could not be found`

اسکریپت `.\rebuild.ps1` را اجرا کنید تا پوشه‌های `bin` و `obj` پاک‌سازی شده و Solution به ترتیب صحیح بیلد شود.

### خطای اتصال به SQL Server

1. مطمئن شوید سرویس SQL Server در حال اجراست.
2. هر دو فایل `appsettings.json` و `appsettings.Development.json` (یا متغیر محیطی `ConnectionStrings__Default`) را بررسی کنید:
   `Server=.;Database=TotallSoftwareForoughAriya;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False`
3. در زمان راه‌اندازی، لاگ `[DB] ConnectionString = ...` در خروجی کنسول چاپ می‌شود تا از رشته اتصال فعال مطمئن شوید.
