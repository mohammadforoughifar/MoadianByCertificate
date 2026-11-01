# مستند فنی و اجرایی ماژول سامانه مودیان (نسخه گواهی امضای الکترونیکی — SDK 2.0.29)

این مستند معماری، نحوه استقرار، امنیت گواهی امضا و قابلیت‌های ماژول بازطراحی‌شدهٔ **سامانه مودیان مالیاتی** در پروژه یکپارچهٔ **`TotallSoftwareForoughAriya`** را شرح می‌دهد.

---

## ۱. خلاصه تغییرات ساختاری مخزن

1. **استقرار کامل و یکپارچه با نام `TotallSoftwareForoughAriya` در ریشه مخزن:** ساختار اصلی پروژه (`TotallSoftwareForoughAriya.sln`، `src/TotallSoftwareForoughAriya.Shared`، `src/TotallSoftwareForoughAriya.Api`، `src/TotallSoftwareForoughAriya.Client`، `tests/`، `sql/`، `packages/`) مستقیماً در ریشه مخزن قرار گرفته و نام دیتابیس پیش‌فرض SQL Server به **`TotallSoftwareForoughAriya`** تغییر یافته است.
2. **حذف کامل ماژول منسوخ `0.0.27`:** تمام کنترلرها، سرویس‌ها، مدل‌ها، صفحات Blazor قدیمی و جداول SQL نسخه `0.0.27` به طور کامل حذف شدند.
3. **بسته‌بندی محلی و آفلاین `TaxCollectData.Library` نسخه `2.0.29`:**
   - پکیج رسمی `packages/TaxCollectData.Library.2.0.29.nupkg` در مخزن قرار دارد و توسط `NuGet.config` (منبع `local-moadian`) بدون نیاز به دسترسی خارجی بازیابی می‌شود.
   - اسکریپت کمکی `tools/Restore-TaxCollectDataSdk.ps1` نیز با بررسی هش SHA-1 برای بازیابی مجدد در دسترس است.

---

## ۲. معماری ماژول جدید مودیان (`SDK 2.0.29`)

### لایه مشترک (`src/TotallSoftwareForoughAriya.Shared/Dtos/Moadian/MoadianDtos.cs`)
- انواع شمارشی استاندارد منطبق با دستورالعمل سازمان امور مالیاتی:
  - `MdnInvoiceType` (نوع اول و دوم)، `MdnInvoicePattern` (الگوهای ۱ تا ۷ شامل فروش، ارزی، طلا و جواهر، پیمانکاری، قبوض، بلیط هواپیما و صادرات)، `MdnInvoiceSubject` (اصلی، اصلاحی، ابطالی، برگشت از فروش)
  - `MdnSubmissionStatus` (وضعیت پذیرش فنی درگاه ارسال)
  - `MdnFolderStatus` (وضعیت به‌روز کارپوشه خریدار از طریق `InquiryInvoiceStatus`: در انتظار واکنش، تأیید شده، تأیید سیستمی، رد شده، باطل شده)
  - `MdnPaymentMethod` (روش‌های پرداخت رسمی ۱ تا ۸) و `MdnPaymentSyncState` (چرخه ثبت پرداخت در مودیان)

### لایه دیتابیس و EF Core (`src/TotallSoftwareForoughAriya.Api/Data/`)
- **موجودیت‌ها (`Entities/Moadian/MoadianCertEntities.cs`):**
  - `MdnTaxpayerProfile` — پرونده مالیاتی و گاوصندوق رمزنگاری‌شده گواهی امضا به تفکیک هر شرکت (`ICompanyOwned`)
  - `MdnMeasureUnit` — کدهای رسمی واحدهای سنجش سازمان امور مالیاتی (۲۰ واحد پرکاربرد به صورت خودکار Seed می‌شوند)
  - `MdnBuyer` — پرونده خریداران مودیان با قابلیت همگام‌سازی یک‌کلیکی از `Parties`
  - `MdnCatalogItem` — شناسه‌های ۱۳ رقمی کالا و خدمت (`SSTID`) با قابلیت همگام‌سازی یک‌کلیکی از `Products`
  - `MdnInvoice` و `MdnInvoiceLine` — سربرگ و اقلام صورتحساب با مبالغ دقیق ریالی (`long`) و زنجیره مرجع (`ParentInvoiceId` / `ReferenceTaxId`)
  - `MdnInvoicePayment` — رکوردهای پرداخت صورتحساب (هم پرداخت هم‌زمان با صدور و هم ثبت پرداخت نسیه از طریق `RegisterPaymentRequestAsync`)
  - `MdnSubmissionLog` — لاگ کامل تبادلات، کد پیگیری، `UID` و جزئیات خطاهای برگشتی درگاه
- **مهاجرت دیتابیس:**
  - مایگریشن `20261101120000_ReplaceLegacyMoadianWithCertSdkV2` و ارتقادهندهٔ Idempotent در `MoadianCertSchemaV2.cs` (و اسکریپت دستی `sql/MoadianCertV2-SDK-2.0.29.sql`) ابتدا محدودیت‌های کلید خارجی و تمام ۱۶ جدول قدیمی مودیان را با `DROP TABLE IF EXISTS` پاک‌سازی کرده و سپس جداول `Mdn*` را ایجاد می‌کنند.

### لایه امنیتی گواهی امضا (`MoadianSecretVault.cs`)
- کلید خصوصی `PKCS#8`، گواهی `X.509` و پین توکن `PKCS#11` با الگوریتم **AES-256-GCM** و مشتق‌سازی کلید **HKDF-SHA256** و داده‌های احراز هویت مرتبط با شناسه شرکت (`CompanyId` به عنوان AAD) رمزنگاری می‌شوند.
- پیش از ذخیره‌سازی، تطابق ریاضی زوج‌کلید RSA میان کلید خصوصی و گواهی امضای X.509 با امضای آزمایشی SHA-256 بررسی می‌شود تا از خطای امضای نامعتبر در درگاه جلوگیری گردد.
- در محیط عملیاتی (Production)، کلید `MoadianVault:MasterKey` در تنظیمات یا متغیر محیطی `MoadianVault__MasterKey` (کلید ۲۵۶ بیتی به فرمت Base64) را روی سرور تنظیم نمایید (در صورت عدم تنظیم، کلید تصادفی ۲۵۶ بیتی به صورت خودکار خارج از دیتابیس در مسیر امن سرور تولید و نگهداری می‌شود):
  ```powershell
  # تولید کلید ۲۵۶ بیتی تصادفی در PowerShell:
  $bytes = New-Object byte[] 32
  [System.Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
  [Convert]::ToBase64String($bytes)
  ```

### لایه درگاه رسمی SDK 2.0.29 (`MoadianSdkGateway.cs`)
- کلاس `NormalizedMoadianUrlProperties` مشکل شناخته شدهٔ `Path.Combine` در `DefaultUrlProperties` کتابخانه رسمی (تولید اسلش معکوس در ویندوز و تکرار `/api/v2`) را برطرف می‌کند.
- پشتیبانی کامل از هر دو حالت امضای دیجیتال در SDK `2.0.29`:
  1. **گواهی امضای نرم‌افزاری (`Pkcs8Signatory`):** کلید خصوصی PEM + گواهی X.509 (یا استخراج خودکار از فایل `.pfx` / `.p12`).
  2. **توکن سخت‌افزاری (`Pkcs11KeySignatory`):** اتصال به ماژول PKCS#11 توکن‌های سخت‌افزاری استاندارد.
- پوشش متدهای کلیدی نسخه `2.0.29`:
  - `GetServerInformationAsync` + `GetTokenAsync` + `GetFiscalInformationAsync` (تست اتصال ۵ مرحله‌ای)
  - `GetEconomicCodeInformationAsync` (استعلام برخط کد اقتصادی خریدار)
  - `GetArticle6StatusAsync` (بررسی سقف اعتبار موضوع ماده ۶)
  - `SendInvoicesAsync` (امضای JWS، رمزنگاری JWE با کلید عمومی سرور مالیاتی و ارسال بسته)
  - `InquiryByReferenceIdAsync` و `InquiryByTimeRangeAsync` (استعلام وضعیت پذیرش فنی درگاه با صفحه‌بندی 0-based مطابق SDK)
  - `InquiryInvoiceStatusAsync` (استعلام وضعیت به‌روز صورتحساب در کارپوشه خریدار و وضعیت ماده ۶)
  - `RegisterPaymentRequestAsync` (ثبت رسمی پرداخت‌های صورتحساب‌های نسیه در سامانه مودیان)

---

## ۳. مسیرهای رابط کاربری (Blazor WebAssembly)

| مسیر | صفحه | کاربرد اصلی |
|---|---|---|
| `/moadian` | `MoadianDashboard.razor` | داشبورد مدیریتی، وضعیت اعتبار گواهی امضا، شاخص‌های فروش/مالیات/کارپوشه/پرداخت و آخرین تبادلات |
| `/moadian/profile` | `MoadianProfile.razor` | تنظیم شناسه حافظه ۶ کاراکتری، آپلود گواهی امضا (`.pem`/`.crt` یا `.pfx`)، توکن `PKCS#11`، تست اتصال، استعلام کد اقتصادی و ماده ۶ |
| `/moadian/invoices` | `MoadianInvoices.razor` | فهرست صورتحساب‌ها با فیلتر وضعیت ارسال/کارپوشه/پرداخت، عملیات گروهی، پیش‌نمایش JSON و تبدیل از فاکتور فروش سیستم |
| `/moadian/invoices/new` | `MoadianInvoiceForm.razor` | صدور و ویرایش صورتحساب (الگوهای ۱ تا ۷) با محاسبه خودکار ریالی و انتخاب سریع از کاتالوگ و خریداران |
| `/moadian/invoices/{Id}` | `MoadianInvoiceDetail.razor` | نمای ۳۶۰ درجه صورتحساب، چاپ رسمی، استعلام درگاه و کارپوشه، ثبت پرداخت در مودیان و صدور اصلاحی/ابطالی/برگشت |
| `/moadian/status-payments` | `MoadianCenter.razor` | مرکز ویژه پایش یکجای کارپوشه، تسویه صورتحساب‌های نسیه (`RegisterPayment`) و استعلام بازه‌ای درگاه |
| `/moadian/basic-info` | `MoadianMasterData.razor` | مدیریت کالاها/خدمات مالیاتی (`SSTID`)، خریداران و واحدهای سنجش + همگام‌سازی یک‌کلیکی با `Products` و `Parties` |
| `/moadian/guide` | `MoadianGuide.razor` | راهنمای گام‌به‌گام کاربری برای اپراتورها و حسابداران |

---

## ۴. مجوزهای کنترل دسترسی (RBAC)

ماژول `Moadian` دارای ۱۱ سطح دسترسی تفکیکی در `RbacSeeder.cs` و صفحه `Roles.razor` است:

- `Moadian.View` — مشاهده داشبورد، فهرست و جزئیات صورتحساب‌ها
- `Moadian.Certificate` — مدیریت پرونده مالیاتی و بارگذاری/حذف گواهی امضای الکترونیکی
- `Moadian.MasterData` — مدیریت کالاها (`SSTID`)، خریداران، واحدهای سنجش و همگام‌سازی ERP
- `Moadian.InvoiceCreate` — صدور صورتحساب جدید و تبدیل از فاکتور فروش سیستم
- `Moadian.InvoiceUpdate` — ویرایش پیش‌نویس و صدور صورتحساب اصلاحی، ابطالی و برگشت از فروش
- `Moadian.InvoiceDelete` — حذف پیش‌نویس‌های ارسال‌نشده
- `Moadian.InvoiceSend` — امضای دیجیتال و ارسال صورتحساب به سامانه مودیان
- `Moadian.InvoiceInquiry` — استعلام وضعیت پذیرش درگاه (شماره پیگیری و بازه زمانی)
- `Moadian.FolderStatus` — استعلام وضعیت به‌روز کارپوشه خریداران و وضعیت ماده ۶
- `Moadian.PaymentRegister` — ثبت و ارسال پرداخت صورتحساب‌های نسیه در سامانه مودیان
- `Moadian.Diagnose` — اجرای تست فنی اتصال و استعلام آنلاین کد اقتصادی

---

## ۵. دستورات ساخت و راه‌اندازی در محیط توسعه / سرور

```powershell
# ۱. بازیابی پکیج‌ها (شامل TaxCollectData.Library 2.0.29 از پوشه محلی packages/)
dotnet restore TotallSoftwareForoughAriya.sln

# ۲. بیلد کل راهکار
dotnet build TotallSoftwareForoughAriya.sln -c Release

# ۳. اعمال خودکار ارتقای دیتابیس روی TotallSoftwareForoughAriya بدون بالا آوردن وب‌سرور (اختیاری)
dotnet run --project src/TotallSoftwareForoughAriya.Api -- --update-database
```
