# مرحله ۳ — طراحی دیتابیس و لایه‌ی داده

**پروژه:** نرم‌افزار واسط مودیان
**بانک اطلاعاتی:** SQL Server (EF Core 8 — Code First + Migrations)
**پیش‌نیاز:** `docs/02-solution-design-fa.md`

---

## ۱) اصول طراحی

| اصل | پیاده‌سازی |
|-----|-------------|
| چند‌مستأجری | `TenantId` روی همه‌ی جداول عملیاتی + فیلتر سراسری EF + `TenantStampInterceptor` |
| حذف نرم | `IsDeleted` در `AuditableEntity`؛ حذف فیزیکی هرگز انجام نمی‌شود (به‌جز Cascade والد→فرزند) |
| حسابرسی | `CreatedAt/By`, `ModifiedAt/By` به‌صورت خودکار توسط `AuditableEntityInterceptor` |
| مبالغ ریالی | `long` → `bigint` (ریال، بدون اعشار) |
| نرخ و مقدار | `decimal` با `HasPrecision(18,4)` و نرخ‌ها `(5,2)` |
| تاریخ | `DateTimeOffset` (UTC) → `datetimeoffset` |
| بدون داده‌ی حساس در لاگ | کلید خصوصی و پین فقط ciphertext؛ پاسخ ViewModel فقط Thumbprint/Subject |
| یکتایی | همه‌ی ایندکس‌های یکتا Tenant-scoped هستند |

---

## ۲) ERD متنی

```
                              ┌───────────────────────┐
                              │        Tenants        │  ← مودی (مرز جداسازی داده)
                              │ Id, Name, LegalName,  │
                              │ IsActive, ...         │
                              └───────────┬───────────┘
                   1:1                    │ 1:N
        ┌───────────────────────────┐     │      ┌──────────────────────────┐
        │     MoadianSettings       │     ├─────▶│       UserTenants        │◀── N:1 ── Users
        │ TenantId(UQ), MemoryId(UQ)│     │      │ UserId, TenantId, RoleId │          │
        │ EconomicCode, SigMode ... │     │      └────────────┬─────────────┘          │
        └───────────────────────────┘     │                   │ N:1                    │
                                          │                   ▼                        │
        ┌───────────────────────────┐     │              ┌──────────┐                  │
        │    MoadianCredentials     │     │              │  Roles   │ 1:N ─▶ RolePermissions
        │ TenantId(UQ), CertBase64  │     │              └──────────┘                  │
        │ EncryptedPrivateKey, ...  │     │                                            │
        └───────────────────────────┘     │                                            │
                                          │                                            │
   ┌──────────────────────┐               │        ┌──────────────────────┐            │
   │   MeasuringUnits     │◀── N:1 ──┐    │        │    RefreshTokens     │◀── N:1 ────┘
   │ Code(UQ), Title      │          │    │        └──────────────────────┘
   └──────────────────────┘          │    │
                                     │    │
   ┌──────────────────────┐   ┌──────┴────┴────────────┐        ┌────────────────────┐
   │      Products        │   │      Customers         │        │     ApiClients     │
   │ TenantId, Sstid(UQ)  │   │ TenantId, Code(UQ)     │        │ TenantId, KeyHash  │
   │ VatRate, UnitPrice   │   │ NationalId, Economic.. │        │ Scopes, IsActive   │
   └──────────────────────┘   └───────────┬────────────┘        └────────────────────┘
                                          │ 1:N (nullable)
                                          ▼
                              ┌───────────────────────────────┐
                              │           Invoices            │
                              │ TenantId, InvoiceNumber(UQ)   │
                              │ ExternalId (UQ per tenant)    │ ← Idempotency
                              │ TaxId (UQ per tenant)         │
                              │ Status, Type, PatternCode ... │
                              │ Totals: tprdis/tdis/tadis/    │
                              │         tvam/todam/tbill      │
                              │ Buyer snapshot fields         │
                              └───────────────┬───────────────┘
                     ┌────────────────────────┼─────────────────────────┐
                     │ 1:N                    │ 1:N                     │ 1:N
                     ▼                        ▼                         ▼
        ┌────────────────────┐   ┌────────────────────┐   ┌──────────────────────────┐
        │    InvoiceItems    │   │  InvoicePayments   │   │   InvoiceSubmissions     │
        │ TenantId, InvoiceId│   │ TenantId, InvoiceId│   │ TenantId, InvoiceId       │
        │ Sstid, am, fee,    │   │ Method, Amount,    │   │ Action, Status, Uid,      │
        │ dis, adis, vam ... │   │ pdt, trn, trmn ... │   │ ReferenceNumber, Error... │
        └────────────────────┘   └────────────────────┘   └──────────────────────────┘

   جداول مستقل (بدون TenantId):
   ┌───────────────────┐ ┌──────────────────┐ ┌────────────────────┐ ┌───────────────┐
   │   AuditLogs       │ │ OutboxMessages   │ │ MoadianErrorCodes  │ │ Lookupها:      │
   │ TenantId?(nullable│ │ TenantId         │ │ Code(UQ)           │ │ InvoicePatterns│
   │ Action, UserId ...│ │ Status, Retry... │ │ PersianMessage     │ │ InvoiceSubjects│
   └───────────────────┘ └──────────────────┘ └────────────────────┘ │ PaymentMethods │
                                                                     └───────────────┘
```

---

## ۳) جداول و ستون‌های کلیدی

### ۳-۱) هویت و دسترسی (سراسری)

| جدول | ستون‌های مهم | ایندکس‌ها |
|------|--------------|-----------|
| `Tenants` | `Name`, `LegalName`, `IsActive`, `IsDeleted` | `Name` |
| `Users` | `UserName`, `NormalizedUserName`, `FullName`, `PasswordHash`, `IsActive`, `IsSuperAdmin`, `LastLoginAt`, `AccessFailedCount`, `LockoutEndAt` | `NormalizedUserName` (UQ), `Email` |
| `Roles` | `Name`, `NormalizedName`, `Description`, `IsSystem` | `NormalizedName` (UQ) |
| `RolePermissions` | `RoleId`, `Permission` | `(RoleId, Permission)` UQ |
| `UserTenants` | `UserId`, `TenantId`, `RoleId`, `IsActive` | `(UserId, TenantId, RoleId)` UQ، `(TenantId, UserId)` |
| `RefreshTokens` | `UserId`, `TokenHash`, `ExpiresAt`, `RevokedAt`, `ReplacedByTokenHash`, `CreatedByIp`, `UserAgent` | `TokenHash` (UQ)، `(UserId, ExpiresAt)` |

### ۳-۲) تنظیمات مودی

| جدول | ستون‌های مهم | نکته |
|------|--------------|------|
| `MoadianSettings` | `TenantId` (UQ), `MemoryId` (UQ), `EconomicCode`, `NationalId`, `BranchCode`, `SignatureMode`, `IsSignatureExperimentalApproved`, `ActiveEnvironment`, `SandboxBaseUrl`، `ProductionBaseUrl`, `LastConnectionTest*`, `IsActive` | `MemoryId` = شناسه‌ی حافظه‌ی مالیاتی |
| `MoadianCredentials` | `TenantId` (UQ), `CertificateBase64`, `CertificateThumbprint`, `CertificateSubject`, `CertificateNotAfter`, `EncryptedPrivateKey`, `PrivateKeyNonce`, `PrivateKeyTag`, `PrivateKeyKeyVersion`, `Pkcs11LibraryPath`, `Pkcs11CertificateAlias`, `EncryptedPin`, `PinNonce`, `PinTag` | کلید/پین فقط ciphertext |

> **تصمیم امنیتی:** `CertificateBase64` (داده‌ی عمومی) بدون رمزنگاری ذخیره می‌شود؛ `EncryptedPrivateKey` و `EncryptedPin` با AES-GCM رمزنگاری می‌شوند. Nonce و Tag جدا ذخیره می‌شوند تا هر رکورد کلید/IV مخصوص خودش را داشته باشد.

### ۳-۳) اطلاعات پایه

| جدول | ستون‌های مهم | ایندکس |
|------|--------------|--------|
| `MeasuringUnits` | `Code` (کد `mu`)، `Title`, `Symbol`, `SortOrder`, `IsActive` | `Code` (UQ) |
| `Products` | `TenantId`, `Sstid`, `Title`, `MeasuringUnitId`, `UnitPrice`, `VatRate`, `IsService`, `HsCode`, `IsActive` | `(TenantId, Sstid)` UQ، `(TenantId, Title)` |
| `Customers` | `TenantId`, `Code`, `Name`, `BuyerType`, `NationalId`, `EconomicCode`, `PostalCode`, `Address`, `ExternalCode`, `IsActive` | `(TenantId, Code)` UQ، `(TenantId, NationalId)` |

### ۳-۴) صورتحساب

| جدول | ستون‌های مهم | ایندکس |
|------|--------------|--------|
| `Invoices` | `TenantId`, `InvoiceNumber`, `ExternalId`, `Type`, `PatternCode`, `SubjectCode`, `SettlementMethod`, `Status`, `IssueDate`, `TaxId`, `Serial`, `ReferenceTaxId`, `Uid`, `ReferenceNumber`, جمع‌ها: `TotalBeforeDiscount/TotalDiscount/TotalAfterDiscount/TotalVat/TotalOtherTaxes/TotalAmount/CashPayment/CreditPayment/VatShareFromPayment/Tax17`, snapshot خریدار: `Buyer*`, `LastErrorCode`, `LastErrorMessage` | `(TenantId, InvoiceNumber)` UQ، `(TenantId, ExternalId)` UQ-filtered، `(TenantId, TaxId)` UQ-filtered، `(TenantId, Status)`، `(TenantId, IssueDate)`، `(TenantId, CustomerId)`، `ReferenceNumber` |
| `InvoiceItems` | `TenantId`, `InvoiceId`, `RowNumber`, `Sstid`, `Description`, `MeasuringUnitCode`, `Quantity`, `UnitPrice`, `PriceBeforeDiscount`, `DiscountAmount`, `PriceAfterDiscount`, `VatRate`, `VatAmount`, `OtherTax*`, `OtherLegalFee*`, `TotalAmount`, `ConstructionFee`, `SellerProfit`, `BrokerFee`, `NetWeight`, `HsCode` | `(InvoiceId, RowNumber)`، `(TenantId, Sstid)` |
| `InvoicePayments` | `TenantId`, `InvoiceId`, `Method`, `Amount`, `PaymentDate`, `ReferenceNumber`, `TerminalNumber`, `AcceptorNumber`, `SwitchNumber`, `PayerNationalId`, `MaskedCardNumber`, `VatPaymentId`, `VatBillId` | `InvoiceId` |
| `InvoiceSubmissions` | `TenantId`, `InvoiceId`, `AttemptNumber`, `Action`, `Status`, `Uid`, `ReferenceNumber`, `MoadianStatus`, `ErrorCode`, `ErrorMessage`, `WarningCount`, `CorrelationId`, `StartedAt`, `CompletedAt`, `DurationMs` | `(InvoiceId, StartedAt)`، `(TenantId, Status)`، `Uid` |

### ۳-۵) زیرساخت

| جدول | ستون‌های مهم | ایندکس |
|------|--------------|--------|
| `AuditLogs` | `TenantId?`, `UserId?`, `ApiClientId?`, `Action`, `EntityName`, `EntityId`, `Description`, `IpAddress`, `UserAgent`, `CorrelationId`, `IsSuccess`, `AdditionalData`, `CreatedAt` | `(TenantId, CreatedAt)`، `(EntityName, EntityId)`، `(UserId, CreatedAt)` |
| `ApiClients` | `TenantId`, `Name`, `KeyPrefix`, `KeyHash`, `Scopes` (int flags), `IsActive`, `ExpiresAt`, `LastUsedAt`, `RequestsPerMinute` | `KeyPrefix` UQ، `KeyHash` UQ، `(TenantId, IsActive)` |
| `OutboxMessages` | `TenantId`, `Type`, `Payload`, `Status`, `OccurredAt`, `ProcessedAt`, `RetryCount`, `NextRetryAt`, `LastError`, `InvoiceId?`, `IdempotencyKey` | `(Status, NextRetryAt)`، `(TenantId, InvoiceId)`، `(TenantId, IdempotencyKey)` UQ-filtered |

### ۳-۶) داده‌های مرجع (Lookup)

| جدول | ستون‌ها | توضیح |
|------|---------|-------|
| `InvoicePatterns` | `Code` (UQ), `Title`, `Description`, `SortOrder`, `IsActive` | فیلد `inp` — **نیازمند تکمیل با جدول رسمی** |
| `InvoiceSubjects` | `Code` (UQ), `Title`, ... | فیلد `ins` — **نیازمند تکمیل** |
| `PaymentMethodCodes` | `MethodCode` (UQ), `Title`, `MoadianCode`, `IsVerified` | فیلد `pmt` — **نیازمند تأیید** (`IsVerified=false`) |
| `MoadianErrorCodes` | `Code` (UQ), `PersianMessage`, `TechnicalMessage`, `Severity`, `SuggestedAction` | کد `*` = پیش‌فرض/ناشناخته |

---

## ۴) نگاشت Entity ← فیلدهای سامانه‌ی مودیان

| فیلد مودیان | ستون دیتابیس | تبدیل در CoreLayer |
|--------------|---------------|--------------------|
| `taxid` | `Invoices.TaxId` | `TaxIdGenerator` (Verhoeff) — مرحله ۴ |
| `indatim` | `Invoices.IssueDate` | `ToUnixTimeMilliseconds()` |
| `inty` | `Invoices.Type` | `(int)InvoiceType` |
| `inp` / `ins` | `Invoices.PatternCode` / `SubjectCode` | مستقیم |
| `inno` | `Invoices.Serial` | hex با PadLeft(10, '0') |
| `irtaxid` | `Invoices.ReferenceTaxId` | مستقیم |
| `tins` | `MoadianSettings.EconomicCode` | مستقیم |
| `tob` | `Invoices.BuyerType` | `(int)BuyerType` |
| `bid` | `Invoices.BuyerNationalId` | مستقیم |
| `tinb` | `Invoices.BuyerEconomicCode` | مستقیم |
| `bpc` | `Invoices.BuyerPostalCode` | مستقیم |
| `setm` | `Invoices.SettlementMethod` | `(int)SettlementMethod` |
| `tprdis/tdis/tadis/tvam/todam/tbill` | `TotalBeforeDiscount/TotalDiscount/TotalAfterDiscount/TotalVat/TotalOtherTaxes/TotalAmount` | محاسبه‌شده در `InvoiceCalculationService` |
| `cap/insp/tvop/tax17` | `CashPayment/CreditPayment/VatShareFromPayment/Tax17` | محاسبه‌شده |
| `sstid/sstt/mu/am/fee` | `InvoiceItems.Sstid/Description/MeasuringUnitCode/Quantity/UnitPrice` | مستقیم |
| `prdis/dis/adis/vra/vam` | `PriceBeforeDiscount/DiscountAmount/PriceAfterDiscount/VatRate/VatAmount` | محاسبه‌شده |
| `tsstam` | `InvoiceItems.TotalAmount` | `adis + vam + odam + olam` |
| `pmt/pv/pdt/trn/trmn/acn/iinn` | `InvoicePayments.Method/Amount/PaymentDate/ReferenceNumber/TerminalNumber/AcceptorNumber/SwitchNumber` | `pmt` از `PaymentMethodCodes.MoadianCode` |

---

## ۵) فیلترهای سراسری و سیاست امنیت داده

```csharp
// موجودیت‌های سازمانی دارای Audit
HasQueryFilter(e => CurrentTenantId != null && e.TenantId == CurrentTenantId && !e.IsDeleted);

// موجودیت‌های فرزند (اقلام/پرداخت/تاریخچه/Outbox)
HasQueryFilter(e => CurrentTenantId != null && e.TenantId == CurrentTenantId);

// داده‌های مرجع سراسری
HasQueryFilter(e => !e.IsDeleted);
```

| سیاست | نتیجه |
|-------|-------|
| `CurrentTenantId == null` | **هیچ داده‌ای** برنمی‌گردد (Refuse by default) |
| تلاش برای ذخیره‌ی رکورد سازمانی بدون مستأجر | `InvalidOperationException` در `TenantStampInterceptor` |
| تلاش برای تغییر `TenantId` | نادیده گرفته می‌شود (`IsModified = false`) |
| عملیات سیستمی (Job/Seed) | استفاده‌ی صریح از `IgnoreQueryFilters()` + مستندسازی دلیل |

---

## ۶) Seed اولیه (Idempotent)

| داده | تعداد | وضعیت |
|------|-------|-------|
| نقش‌ها | ۵ (SuperAdmin, TenantAdmin, Accountant, Operator, Viewer) | ✅ قطعی |
| Permissionها | از `Permissions.All` (۲۲ کد) | ✅ قطعی |
| الگوی صورتحساب (`inp`) | ۱ رکورد (فروش) | 🟡 نیازمند تکمیل رسمی |
| موضوع صورتحساب (`ins`) | ۲ رکورد (فروش، برگشت از فروش) | 🟡 نیازمند تکمیل رسمی |
| روش پرداخت (`pmt`) | ۸ رکورد (`IsVerified=false`) | 🟡 نیازمند تأیید |
| واحد اندازه‌گیری | ۱ رکورد (کد ۱۶۴ = عدد) | 🟡 نیازمند تکمیل |
| کد خطا | ۲ رکورد (`*` پیش‌فرض، `NETWORK`) | ✅ ساختاری |
| کاربر مدیر | از `DatabaseSeed:AdminUserName/AdminPassword` | ✅ اختیاری (بدون رمز، ساخته نمی‌شود) |

---

## ۷) دستورهای ساخت و اجرای Migration

```bash
# ۱) ساخت اولین Migration (پروژه‌ی Api در مرحله ۵ ساخته می‌شود؛ تا آن زمان از همین پروژه استفاده کنید)
dotnet ef migrations add InitialCreate \
  --project src/MoudianMiddleware.DataLayer \
  --startup-project src/MoudianMiddleware.DataLayer \
  --output-dir Migrations

# ۲) ساخت اسکریپت SQL برای محیط عملیاتی (قبل از Deploy)
dotnet ef migrations script --idempotent \
  --project src/MoudianMiddleware.DataLayer \
  --startup-project src/MoudianMiddleware.DataLayer \
  --output migrations.sql

# ۳) اعمال Migration (توصیه: در Startup برنامه به‌صورت کنترل‌شده یا با دستور زیر)
dotnet ef database update \
  --project src/MoudianMiddleware.DataLayer \
  --startup-project src/MoudianMiddleware.DataLayer
```

> **رشته‌ی اتصال برای ابزار EF:** `MoudianDbContextFactory` (Design-Time) مقدار را از
> `MOUDIAN__CONNECTION_STRING` می‌خواند؛ در نبود آن یک LocalDB ساختگی استفاده می‌کند تا Migration
> بدون نیاز به دیتابیس واقعی ساخته شود.

---

## ۸) ساختار فایل‌های پیاده‌سازی‌شده در این مرحله

```
src/MoudianMiddleware.Contracts/
├── Common/{ApiResponse, ApiResponseCodes, PagedResult, PaginationQuery}.cs
├── Constants/Permissions.cs
└── Enums/{MoadianEnums, InvoiceEnums, PaymentEnums, SubmissionEnums, SecurityEnums}.cs

src/MoudianMiddleware.DataLayer/
├── Abstractions/{ITenantProvider, ICurrentUserProvider, IClock, IPasswordHasher}.cs
├── Abstractions/Queries/InvoiceQuery.cs
├── Abstractions/Repositories/{IGenericRepository, IInvoiceRepository, IInvoiceSubmissionRepository,
│                              IProductRepository, ICustomerRepository, IUserRepository,
│                              IApiClientRepository, ITenantRepository}.cs
├── Context/MoudianDbContext.cs
├── Context/MoudianDbContextFactory.cs
├── Context/Configurations/{Tenant, Identity, Catalog, Invoice, Lookup, Infrastructure}Configurations.cs
├── Context/Interceptors/{AuditableEntityInterceptor, TenantStampInterceptor}.cs
├── Entities/   (۲۱ موجودیت + BaseEntity/AuditableEntity/ITenantEntity/ISoftDeletable)
├── Repositories/ (Generic + 7 Repository اختصاصی)
├── UnitOfWork/{IUnitOfWork, UnitOfWork}.cs
├── Seed/{IDatabaseSeeder, DatabaseSeedOptions, DatabaseSeeder}.cs
├── Seed/SeedData/LookupSeedData.cs
└── DependencyInjection.cs

tests/MoudianMiddleware.Tests/
├── TestHelpers/{FakeTenantProvider, FakeCurrentUserProvider, FakeClock, TestDbContextFactory}.cs
└── Data/TenantIsolationTests.cs   (۵ تست: جداسازی، رد پیش‌فرض، خطای بدون مستأجر، حذف نرم، پوشش فیلترها)
```

---

## ۹) نکات مهم برای توسعه‌دهنده‌ی جدید

1. **افزودن موجودیت سازمانی جدید:** ارث‌بری از `AuditableEntity` + `ITenantEntity` ، تعریف `IEntityTypeConfiguration` در `Context/Configurations`، افزودن `DbSet` و **یک خط `ApplyTenantSoftDeleteFilter<T>()`** در `OnModelCreating`. تست `AllTenantEntities_HaveQueryFilter` فراموشی را می‌گیرد.
2. **هرگز** در سرویس‌ها `CreatedAt`/`TenantId` را دستی ست نکنید.
3. حذف همیشه `Remove()` است؛ Interceptor آن را به حذف نرم تبدیل می‌کند. برای حذف واقعی (نادر) باید صریحاً `IgnoreQueryFilters` + `DeleteBehavior` بررسی شود.
4. مبالغ ریالی `long` هستند؛ از `decimal` برای مبلغ استفاده نکنید (خطای گرد کردن در `tbill`).
5. `ExternalId` کلید Idempotency سیستم‌های خارجی است؛ Index یکتای فیلترشده در دیتابیس ضامن آن است.
