# مرحله ۲ — طراحی Solution، ساختار پوشه‌ها و نمودار وابستگی

**پروژه:** نرم‌افزار واسط مودیان (Moudian Middleware)
**نسخه‌ی سند:** 1.0 — مهر ۱۴۰۴
**پیش‌نیاز:** `docs/01-repository-analysis-fa.md` (تأییدشده با اصلاحات)

---

## ۰) خلاصه‌ی تصمیم‌های این مرحله

| موضوع | تصمیم |
|-------|-------|
| محل Solution | `MoudianMiddleware/` در ریشه‌ی مخزن — `src/`، `tests/`، `docs/` |
| پروژه‌ها | ۴ پروژه‌ی اصلی (`DataLayer`, `CoreLayer`, `Api`, `Client`) + ۱ پروژه‌ی تست |
| جهت وابستگی | `Api → CoreLayer → DataLayer → Contracts` و `Client → Contracts` (قانون یک‌طرفه) |
| چند‌مستأجری | `TenantId` روی همه‌ی Entityهای سازمانی + Global Query Filter + Interceptor |
| ورود سیستم‌های خارجی | Scheme مستقل `ApiKey` + Rate Limit مجزا + Idempotency با `ExternalId` |
| امضا | ۳ استراتژی (`PrivateKeyOnly` / `Pkcs8+Cert` / `Pkcs11`) per-مودی |
| Mapping | **Mapster** (دلیل در بند ۶-۱) |
| نسخه‌ی پکیج‌ها | Central Package Management (`Directory.Packages.props`) |
| کیفیت کد | `Nullable=enable`, `TreatWarningsAsErrors=true`, `GenerateDocumentationFile=true`, `.editorconfig` با قواعد نام‌گذاری به‌صورت error |

---

## ۱) درخت کامل Solution (تا سطح فایل)

```
MoudianMiddleware/
├── MoudianMiddleware.sln                     ← در ابتدای مرحله ۳ ساخته می‌شود
├── Directory.Build.props                     ✅ ساخته شد
├── Directory.Packages.props                  ✅ ساخته شد
├── .editorconfig                             ✅ ساخته شد
├── .gitignore                                ✅ ساخته شد
├── README.md                                 ← مرحله ۸
├── docs/
│   ├── 01-repository-analysis-fa.md
│   └── 02-solution-design-fa.md              ← همین سند
│
├── src/
│   ├── MoudianMiddleware.Contracts/                 ← فقط BCL (بدون هیچ PackageReference)
│   │   ├── MoudianMiddleware.Contracts.csproj
│   │   ├── Common/ApiResponse.cs                    (ApiResponse<T>، ApiError)
│   │   ├── Common/ApiResponseCodes.cs               (کدهای ثابت پاسخ)
│   │   ├── Common/PagedResult.cs
│   │   ├── Common/PaginationQuery.cs
│   │   ├── Constants/Permissions.cs                 (کدهای Permission مشترک UI/API)
│   │   └── Enums/
│   │       ├── MoadianEnums.cs                      (MoadianEnvironment, MoadianSignatureMode)
│   │       ├── InvoiceEnums.cs                      (InvoiceType, BuyerType, SettlementMethod, InvoiceStatus)
│   │       ├── PaymentEnums.cs                      (PaymentMethod)
│   │       ├── SubmissionEnums.cs                   (SubmissionAction, SubmissionStatus, MoadianRequestStatus, OutboxStatus)
│   │       └── SecurityEnums.cs                     (AuditAction, ApiScope)
│   │   ├── ViewModels/                              ← در مرحله ۴ به‌تفکیک ماژول پر می‌شود
│   │
│   ├── MoudianMiddleware.DataLayer/
│   │   ├── MoudianMiddleware.DataLayer.csproj       (ارجاع: Contracts)
│   │   ├── Abstractions/
│   │   │   ├── ITenantProvider.cs                 ← قراردادی که DataLayer از بالا می‌خواهد (DIP)
│   │   │   ├── ICurrentUserProvider.cs
│   │   │   ├── IClock.cs
│   │   │   └── Repositories/
│   │   │       ├── IGenericRepository.cs
│   │   │       ├── IInvoiceRepository.cs
│   │   │       ├── IInvoiceSubmissionRepository.cs
│   │   │       ├── IProductRepository.cs
│   │   │       ├── ICustomerRepository.cs
│   │   │       ├── IUserRepository.cs
│   │   │       ├── ITenantRepository.cs
│   │   │       ├── IAuditLogRepository.cs
│   │   │       └── IApiClientRepository.cs
│   │   ├── Context/
│   │   │   ├── MoudianDbContext.cs
│   │   │   ├── Configurations/                        (Fluent API، یک فایل per entity)
│   │   │   │   ├── TenantConfiguration.cs
│   │   │   │   ├── UserConfiguration.cs
│   │   │   │   ├── UserTenantConfiguration.cs
│   │   │   │   ├── RefreshTokenConfiguration.cs
│   │   │   │   ├── MoadianSettingConfiguration.cs
│   │   │   │   ├── MoadianCredentialConfiguration.cs
│   │   │   │   ├── ProductConfiguration.cs
│   │   │   │   ├── MeasuringUnitConfiguration.cs
│   │   │   │   ├── CustomerConfiguration.cs
│   │   │   │   ├── InvoiceConfiguration.cs
│   │   │   │   ├── InvoiceItemConfiguration.cs
│   │   │   │   ├── InvoicePaymentConfiguration.cs
│   │   │   │   ├── InvoiceSubmissionConfiguration.cs
│   │   │   │   ├── ApiClientConfiguration.cs
│   │   │   │   ├── AuditLogConfiguration.cs
│   │   │   │   ├── MoadianErrorCodeConfiguration.cs
│   │   │   │   ├── LookupConfigurations.cs            (InvoicePattern / InvoiceSubject / PaymentMethodCode)
│   │   │   │   └── OutboxMessageConfiguration.cs
│   │   │   └── Interceptors/
│   │   │       ├── AuditableEntityInterceptor.cs
│   │   │       └── TenantStampInterceptor.cs
│   │   ├── Entities/
│   │   │   ├── Common/BaseEntity.cs
│   │   │   ├── Common/ITenantEntity.cs
│   │   │   ├── Common/AuditableEntity.cs
│   │   │   ├── Tenant.cs
│   │   │   ├── User.cs
│   │   │   ├── UserTenant.cs
│   │   │   ├── RefreshToken.cs
│   │   │   ├── MoadianSetting.cs
│   │   │   ├── MoadianCredential.cs
│   │   │   ├── Product.cs
│   │   │   ├── MeasuringUnit.cs
│   │   │   ├── Customer.cs
│   │   │   ├── Invoice.cs
│   │   │   ├── InvoiceItem.cs
│   │   │   ├── InvoicePayment.cs
│   │   │   ├── InvoiceSubmission.cs
│   │   │   ├── ApiClient.cs
│   │   │   ├── AuditLog.cs
│   │   │   ├── MoadianErrorCode.cs
│   │   │   ├── InvoicePattern.cs
│   │   │   ├── InvoiceSubject.cs
│   │   │   ├── PaymentMethodCode.cs
│   │   │   └── OutboxMessage.cs
│   │   ├── Repositories/
│   │   │   ├── GenericRepository.cs
│   │   │   ├── InvoiceRepository.cs
│   │   │   ├── InvoiceSubmissionRepository.cs
│   │   │   ├── ProductRepository.cs
│   │   │   ├── CustomerRepository.cs
│   │   │   ├── UserRepository.cs
│   │   │   ├── TenantRepository.cs
│   │   │   ├── AuditLogRepository.cs
│   │   │   └── ApiClientRepository.cs
│   │   ├── UnitOfWork/
│   │   │   ├── IUnitOfWork.cs
│   │   │   └── UnitOfWork.cs
│   │   ├── Migrations/                                (تولید با dotnet ef — مرحله ۳)
│   │   ├── Seed/
│   │   │   ├── IDatabaseSeeder.cs
│   │   │   ├── DatabaseSeeder.cs
│   │   │   └── SeedData/LookupSeedData.cs             (inp / ins / pmt / واحدها / کدهای خطا)
│   │   └── DependencyInjection.cs                     (AddDataLayer)
│   │
│   ├── MoudianMiddleware.CoreLayer/
│   │   ├── MoudianMiddleware.CoreLayer.csproj            (ارجاع: Contracts + DataLayer)
│   │   ├── Common/
│   │   │   ├── Constants/AppConstants.cs
│   │   │   ├── Constants/ClaimTypes.cs
│   │   │   ├── Constants/CacheKeys.cs
│   │   │   ├── Constants/RegexPatterns.cs
│   │   │   ├── Constants/PersianMessages.cs
│   │   │   ├── Exceptions/ (AppException, NotFoundException, ValidationException, BusinessRuleException,
│   │   │   │               TaxApiException, MoadianConfigurationException, UnauthorizedException)
│   │   │   ├── Extensions/StringExtensions.cs
│   │   │   ├── Extensions/DateTimeExtensions.cs        (Unix ms، تبدیل شمسی)
│   │   │   ├── Extensions/QueryableExtensions.cs
│   │   │   └── Security/SecretRedactor.cs
│   │   │
│   │   │   ⚠️ Enumها، ApiResponse<T>، PagedResult و ViewModelها در Contracts هستند (نه اینجا)
│   │   ├── Interfaces/
│   │   │   ├── Services/ (IAuthService, ITokenService, IMoadianSettingService, IProductService,
│   │   │   │              ICustomerService, IInvoiceService, IInvoiceCalculationService,
│   │   │   │              IInvoiceSubmissionService, IInquiryService, IPaymentService, IReportService,
│   │   │   │              IExcelExportService, IUserService, IAuditLogService, IExternalInvoiceService,
│   │   │   │              IApiClientService, IMoadianErrorTranslator)
│   │   │   ├── Infrastructure/ (IMoadianGateway, IMoadianSignerFactory, IMoadianClientFactory,
│   │   │   │                   ISecretProtector, IApiKeyHasher, IPasswordHasher, ITenantContext,
│   │   │   │                   ICurrentUserContext, IDateTimeProvider)
│   │   ├── Services/
│   │   │   ├── Auth/AuthService.cs
│   │   │   ├── Auth/TokenService.cs
│   │   │   ├── Auth/PasswordHasher.cs
│   │   │   ├── Security/HttpTenantContext.cs
│   │   │   ├── Security/CurrentUserContext.cs
│   │   │   ├── Security/AesGcmSecretProtector.cs
│   │   │   ├── Security/ApiKeyHasher.cs
│   │   │   ├── MoadianSettings/MoadianSettingService.cs
│   │   │   ├── Products/ProductService.cs
│   │   │   ├── Customers/CustomerService.cs
│   │   │   ├── Invoices/InvoiceService.cs
│   │   │   ├── Invoices/InvoiceCalculationService.cs
│   │   │   ├── Invoices/InvoiceMapper.cs                (Entity → Moadian payload DTO)
│   │   │   ├── Invoices/InvoiceSubmissionService.cs
│   │   │   ├── Invoices/ExternalInvoiceService.cs       (API خارجی + Idempotency)
│   │   │   ├── Inquiries/InquiryService.cs
│   │   │   ├── Payments/PaymentService.cs
│   │   │   ├── Reports/ReportService.cs
│   │   │   ├── Reports/ExcelExportService.cs
│   │   │   ├── Users/UserService.cs
│   │   │   ├── Audit/AuditLogService.cs
│   │   │   └── ApiClients/ApiClientService.cs
│   │   ├── Validators/
│   │   │   ├── Auth/LoginRequestValidator.cs
│   │   │   ├── Auth/RefreshTokenRequestValidator.cs
│   │   │   ├── MoadianSettings/SaveMoadianSettingValidator.cs
│   │   │   ├── Products/CreateProductValidator.cs
│   │   │   ├── Products/UpdateProductValidator.cs
│   │   │   ├── Customers/CreateCustomerValidator.cs
│   │   │   ├── Invoices/CreateInvoiceValidator.cs
│   │   │   ├── Invoices/InvoiceItemValidator.cs
│   │   │   ├── Invoices/InvoicePaymentValidator.cs
│   │   │   ├── External/ExternalInvoiceRequestValidator.cs
│   │   │   ├── Users/CreateUserValidator.cs
│   │   │   └── Common/PaginationQueryValidator.cs
│   │   ├── Mappings/
│   │   │   ├── MapsterConfig.cs                         (Register از همه‌ی mappingها)
│   │   │   ├── ProductMapping.cs
│   │   │   ├── CustomerMapping.cs
│   │   │   ├── InvoiceMapping.cs
│   │   │   ├── UserMapping.cs
│   │   │   └── MoadianSettingMapping.cs
│   │   ├── Infrastructure/Moadian/
│   │   │   ├── Options/MoadianApiOptions.cs
│   │   │   ├── Dto/MoadianInvoicePayload.cs              (Header/Body/Payments/Extension)
│   │   │   ├── Dto/MoadianPacket.cs
│   │   │   ├── Dto/MoadianServerInformation.cs
│   │   │   ├── Dto/MoadianInvoiceResponse.cs
│   │   │   ├── Dto/MoadianInquiryResult.cs
│   │   │   ├── Dto/MoadianError.cs
│   │   │   ├── Signing/IJwsSigner.cs
│   │   │   ├── Signing/PrivateKeyOnlyJwsSigner.cs        (استراتژی الف — بدون x5c)
│   │   │   ├── Signing/Pkcs8CertificateJwsSigner.cs     (استراتژی ب — آداپتور SDK)
│   │   │   ├── Signing/Pkcs11TokenJwsSigner.cs          (استراتژی ج — آداپتور SDK)
│   │   │   ├── Signing/MoadianSignerFactory.cs
│   │   │   ├── Signing/SignatureMaterial.cs             (کلید/گواهی رمزگشایی‌شده در RAM)
│   │   │   ├── Serialization/MoadianJson.cs             (تنظیمات Json: camelCase، ignore-null)
│   │   │   ├── Client/MoadianClientFactory.cs           (کش per-مودی: ITaxApi/ITaxPublicApi/IEncryptor)
│   │   │   ├── Client/TaxCollectDataMoadianGateway.cs   (پیاده‌سازی IMoadianGateway)
│   │   │   └── Errors/MoadianErrorTranslator.cs         (کد خطا → پیام فارسی)
│   │   ├── BackgroundJobs/                              (مرحله ۷ — فقط طرح اولیه در این مرحله)
│   │   │   ├── OutboxDispatchJob.cs
│   │   │   ├── SubmissionInquiryJob.cs
│   │   │   └── Options/BackgroundJobOptions.cs
│   │   └── DependencyInjection.cs                       (AddCoreLayer)
│   │
│   ├── MoudianMiddleware.Api/
│   │   ├── MoudianMiddleware.Api.csproj
│   │   ├── Program.cs
│   │   ├── appsettings.json
│   │   ├── appsettings.Development.json
│   │   ├── Authentication/ApiKeyAuthenticationHandler.cs
│   │   ├── Authentication/ApiKeyAuthenticationOptions.cs
│   │   ├── Authentication/PermissionAuthorization.cs     (Policy provider + Requirement)
│   │   ├── Controllers/
│   │   │   ├── BaseApiController.cs
│   │   │   ├── AuthController.cs
│   │   │   ├── MoadianSettingsController.cs
│   │   │   ├── ProductsController.cs
│   │   │   ├── CustomersController.cs
│   │   │   ├── InvoicesController.cs
│   │   │   ├── InquiriesController.cs
│   │   │   ├── PaymentsController.cs
│   │   │   ├── ReportsController.cs
│   │   │   ├── UsersController.cs
│   │   │   ├── AuditLogsController.cs
│   │   │   ├── ApiClientsController.cs
│   │   │   └── External/
│   │   │       ├── ExternalInvoicesController.cs
│   │   │       ├── ExternalBaseInfoController.cs
│   │   │       └── ExternalInquiryController.cs
│   │   ├── Middleware/
│   │   │   ├── GlobalExceptionHandlingMiddleware.cs
│   │   │   ├── RequestResponseLoggingMiddleware.cs
│   │   │   └── CorrelationIdMiddleware.cs
│   │   ├── Extensions/
│   │   │   ├── SwaggerExtensions.cs
│   │   │   ├── AuthenticationExtensions.cs
│   │   │   ├── RateLimitingExtensions.cs
│   │   │   ├── CorsExtensions.cs
│   │   │   └── SerilogExtensions.cs
│   │   └── Models/ApiErrorResponse.cs
│   │
│   └── MoudianMiddleware.Client/
│       ├── MoudianMiddleware.Client.csproj
│       ├── Program.cs
│       ├── App.razor
│       ├── _Imports.razor
│       ├── index.html
│       ├── wwwroot/
│       │   ├── css/app.css
│       │   ├── js/app.js                                (فقط برای تاریخ‌پیکر شمسی اگر لازم شد)
│       │   └── index.html? (در ریشه‌ی پروژه)
│       ├── Layout/
│       │   ├── MainLayout.razor
│       │   ├── MainLayout.razor.css
│       │   ├── NavMenu.razor
│       │   ├── EmptyLayout.razor
│       │   └── RedirectToLogin.razor
│       ├── Shared/
│       │   ├── PageHeader.razor
│       │   ├── LoadingOverlay.razor
│       │   ├── ConfirmDialog.razor
│       │   ├── DataGrid.razor
│       │   ├── PersianDatePicker.razor
│       │   ├── PersianDateDisplay.razor
│       │   ├── StatusBadge.razor
│       │   ├── MoneyDisplay.razor
│       │   └── FormValidationSummary.razor
│       ├── Pages/
│       │   ├── Home.razor
│       │   ├── Auth/Login.razor
│       │   ├── Settings/MoadianSettings.razor
│       │   ├── Products/ProductList.razor
│       │   ├── Products/ProductEdit.razor
│       │   ├── Customers/CustomerList.razor
│       │   ├── Customers/CustomerEdit.razor
│       │   ├── Invoices/InvoiceList.razor
│       │   ├── Invoices/InvoiceEdit.razor
│       │   ├── Invoices/InvoiceDetails.razor
│       │   ├── Inquiries/InquiryList.razor
│       │   ├── Reports/InvoiceReport.razor
│       │   ├── Users/UserList.razor
│       │   ├── Users/UserEdit.razor
│       │   └── Audit/AuditLogList.razor
│       ├── Services/
│       │   ├── ApiClientBase.cs
│       │   ├── AuthApiService.cs
│       │   ├── MoadianSettingApiService.cs
│       │   ├── ProductApiService.cs
│       │   ├── CustomerApiService.cs
│       │   ├── InvoiceApiService.cs
│       │   ├── InquiryApiService.cs
│       │   ├── ReportApiService.cs
│       │   ├── UserApiService.cs
│       │   └── AuditLogApiService.cs
│       ├── Authentication/
│       │   ├── JwtAuthenticationStateProvider.cs
│       │   └── TokenAuthorizationMessageHandler.cs
│       └── Helpers/PersianDateHelper.cs
│
└── tests/
    └── MoudianMiddleware.Tests/
        ├── MoudianMiddleware.Tests.csproj
        ├── TestHelpers/{TestDbContextFactory.cs, FakeTenantProvider.cs, FakeClock.cs, FakeSecretProtector.cs}
        ├── Invoices/InvoiceCalculationServiceTests.cs
        ├── Invoices/InvoiceMapperTests.cs
        ├── Invoices/ExternalInvoiceIdempotencyTests.cs
        ├── Moadian/Signing/PrivateKeyOnlyJwsSignerTests.cs
        ├── Moadian/Signing/SignatureMaterialTests.cs
        ├── Moadian/MoadianJsonSerializationTests.cs
        ├── Security/AesGcmSecretProtectorTests.cs
        ├── Mapping/ProductMappingTests.cs
        └── Validators/CreateInvoiceValidatorTests.cs
```

> **چرا `MoudianMiddleware.sln` در این مرحله ساخته نشد؟** چون در محیط من `dotnet` نصب نیست و sln بدون دست‌کاری دستی، فایل نیمه‌کاره می‌شود. sln و csprojها در ابتدای مرحله ۳ با ساختار نهایی و همراه با کد ساختاریافته نوشته می‌شوند (فرمت sln کاملاً متنی و قابل نوشتن است؛ اما بهتر است هم‌زمان با اولین بیلد منطقی باشد).

---

## ۲) نمودار وابستگی لایه‌ها

```
                        ┌──────────────────────────────┐
                        │   MoudianMiddleware.Client   │   (Blazor WebAssembly)
                        │  Pages / Components / Http   │
                        └──────────────┬───────────────┘
                                       │ ProjectReference
                                       ▼
                        ┌──────────────────────────────┐
                        │ MoudianMiddleware.Contracts  │  ← فقط BCL
                        │ ViewModels / Enums /         │
                        │ ApiResponse<T> / PagedResult │
                        └──────────────▲───────────────┘
                                       │ ProjectReference
┌──────────────────────┐      ┌────────┴─────────────────────┐
│  MoudianMiddleware   │─────▶│  MoudianMiddleware.CoreLayer │
│         .Api         │      │ Services / Interfaces /      │
│ Controllers / MW     │      │ Validators / Moadian Wrapper │
│ JWT + ApiKey + Rate  │      └──────────────┬───────────────┘
└──────────────────────┘                     │ ProjectReference
                                             ▼
                              ┌──────────────────────────────┐
                              │ MoudianMiddleware.DataLayer  │
                              │ DbContext / Entities / Repos │
                              │ UnitOfWork / Migrations      │
                              └──────────────┬───────────────┘
                                             │ ProjectReference
                                             ▼
                                 (Contracts — برای Enumهای مشترک)

                              ┌──────────────────────────────┐
                              │ MoudianMiddleware.Tests      │──▶ Contracts + DataLayer (+ CoreLayer در مرحله ۸)
                              └──────────────────────────────┘
```

### ۲-۱) قواعد وابستگی (قابل بازبینی در Code Review)

| قاعده | مجاز | غیرمجاز |
|-------|------|---------|
| `Contracts → *` | **فقط BCL** (`System.*`) | EF Core، ASP.NET، Blazor، SDK مودیان، Mapster، هر `PackageReference` |
| `DataLayer → *` | `Contracts` + EF Core | ارجاع به CoreLayer/Api/Client |
| `CoreLayer → *` | `Contracts` + `DataLayer` + پکیج‌های دامنه‌ای | ارجاع به Api یا Client |
| `Api → *` | `CoreLayer` (و گذرا `DataLayer` برای Migration) | کوئری EF، منطق کسب‌وکار، ارجاع به Client |
| `Client → *` | **فقط `Contracts`** | `CoreLayer`، `DataLayer`، `Api`، EF Core، ساخت `HttpClient` بدون DI |
| `DataLayer` → `CoreLayer` | ❌ هرگز؛ نیازهای داده‌ای از طریق `DataLayer/Abstractions` (مثل `ITenantProvider`, `IPasswordHasher`) تعریف و از لایه‌های بالاتر تزریق می‌شوند | — |

### ۲-۲) چرا `Contracts` اضافه شد (و چه چیزی را حل می‌کند)

قبلاً `Client → CoreLayer` بود و چون `CoreLayer → DataLayer → EF Core`، خروجی WASM به‌طور گذرا EF Core را حمل می‌کرد (حجم اضافه + ریسک خطای publish). با `Contracts`:

1. `Client` فقط به یک اسمبلی سبک BCL ارجاع می‌دهد → **صفر وابستگی EF/ASP.NET در مرورگر**.
2. `Api` و `Client` دقیقاً یک قرارداد مشترک دارند → تغییر یک ViewModel در یک جا، کامپایل هر دو طرف را می‌شکند (امنیت تغییر).
3. Enumهای مشترک، منبع واحد دارند (نه کپی در DataLayer و Client) → از Magic Number جلوگیری می‌شود.
4. جهت وابستگی بدون چرخه: `Api → Core → Data → Contracts` و `Client → Contracts` و `Tests → *`.

> **قاعده‌ی طلایی برای Junior:** هر نوعی که هم در API و هم در UI دیده می‌شود ⇒ `Contracts`. هر چیزی که به دیتابیس یا EF مربوط است ⇒ `DataLayer`. هر منطقی ⇒ `CoreLayer`.

---

## ۳) مسئولیت هر فایل (قرارداد لایه‌ها)

### ۳-۱) DataLayer — «فقط داده»

| فایل/پوشه | مسئولیت | چه چیزی **نمی‌کند** |
|-----------|----------|----------------------|
| `Context/MoudianDbContext.cs` | `DbSet`ها، اعمال Configurationها، Global Query Filter برای `ITenantEntity` | محاسبه، اعتبارسنجی کسب‌وکار |
| `Context/Configurations/*` | Fluent API: طول، ایندکس، رابطه، `decimal(18,0)` برای مبالغ ریالی | Seed داده‌ی نمونه (Seed در `Seed/`) |
| `Context/Interceptors/AuditableEntityInterceptor.cs` | پر کردن `CreatedAt/By`, `ModifiedAt/By`, `IsDeleted` | دسترسی به HttpContext |
| `Context/Interceptors/TenantStampInterceptor.cs` | ست کردن `TenantId` هنگام Insert و جلوگیری از تغییر آن | — |
| `Abstractions/ITenantProvider.cs` | قرارداد «TenantId جاری» (پیاده‌سازی در CoreLayer) | — |
| `Entities/*` | فقط داده + رفتارهای ساده (مثلاً `Invoice.RecalculateTotals()` نه) | سرویس، DI، Validation |
| `Repositories/*` | کوئری و پیمایش؛ فیلتر/صفحه‌بندی در سطح دیتابیس | منطق کسب‌وکار |
| `UnitOfWork/*` | `SaveChangesAsync` تراکنشی واحد | — |
| `Seed/*` | داده‌های پایه (نقش‌ها، واحدها، الگو/موضوع صورتحساب، کدهای خطا) | داده‌ی عملیاتی |

> **مبالغ:** همه‌ی مبالغ ریالی `long` (ریال، بدون اعشار) و مقادیر مقداری (`am`, `vra`, `fee`) `decimal(18,4)` — مطابق DTOهای مودیان که `long` برای مبلغ و `decimal` برای نرخ/مقدار هستند.

### ۳-۲) CoreLayer — «همه‌ی منطق»

| پوشه | مسئولیت |
|------|----------|
| `Common/Constants` | حذف Magic String/Number: نام Claimها، کلید Cache، Regex کد ملی/شناسه ملی/کدپستی، پیام‌های فارسی |
| `Common/Enums` | `MoadianSignatureMode`, `MoadianEnvironment`, `InvoiceType`, `BuyerType`, `SettlementMethod`, `InvoiceStatus`, `SubmissionStatus`, `InquiryStatus`, `AuditAction`, `ApiScope` |
| `Common/Exceptions` | `AppException` (پایه، `StatusCode` + `Code`)، `NotFoundException`, `ValidationException`, `BusinessRuleException`, `TaxApiException` (خطای مودیان با `ErrorCode`/`ReferenceNumber`)، `MoadianConfigurationException` |
| `Common/Models` | `ApiResponse<T>` (`IsSuccess`, `Data`, `Message`, `Errors`, `TraceId`)، `PagedResult<T>`، `PaginationQuery` |
| `Interfaces/Services` | قرارداد سرویس‌های کسب‌وکار (بدون هیچ جزئیات پیاده‌سازی) |
| `Interfaces/Infrastructure` | `IMoadianGateway`, `IMoadianSignerFactory`, `IMoadianClientFactory`, `ISecretProtector`, `IApiKeyHasher`, `IPasswordHasher`, `ITenantContext`, `ICurrentUserContext`, `IDateTimeProvider` |
| `Services/*` | پیاده‌سازی؛ هر سرویس یک مسئولیت؛ متدها < ۳۰ خط |
| `Validators/*` | FluentValidation برای **همه‌ی** ورودی‌ها (UI و API خارجی) |
| `Mappings/*` | Mapster `IRegister` per ماژول |
| `Infrastructure/Moadian/*` | تنها جایی که `TaxCollectData.Library` دیده می‌شود (بند ۵) |
| `BackgroundJobs/*` | Jobهای دوره‌ای (مرحله ۷) |

### ۳-۳) Api — «نازک»

| فایل | مسئولیت |
|------|----------|
| `Controllers/*` | دریافت DTO → `_service.XAsync(dto, ct)` → `ApiResponse<T>.Ok(...)`؛ حداکثر ۵ خط در هر Action |
| `Controllers/External/*` | همان الگو + `[Authorize(AuthenticationSchemes = ApiKey)]` + `[EnableRateLimiting]` |
| `Middleware/GlobalExceptionHandlingMiddleware.cs` | نگاشت `AppException`/`ValidationException`/`TaxApiException`/سایر → `ApiResponse` با `StatusCode` درست + `TraceId`؛ هرگز StackTrace به کلاینت |
| `Middleware/RequestResponseLoggingMiddleware.cs` | لاگ متادیتای درخواست (متد، مسیر، کد وضعیت، مدت) + ماسک کردن `Authorization`, `ApiKey`, `privateKey` |
| `Middleware/CorrelationIdMiddleware.cs` | تولید/انتقال `X-Correlation-Id` برای ردیابی در لاگ و Audit |
| `Authentication/ApiKeyAuthenticationHandler.cs` | احراز هویت سیستم‌های خارجی با `X-Api-Key` (هش SHA-256 در DB) |
| `Authentication/PermissionAuthorization.cs` | Policyهای دانه‌ای (`Moadian.View`, `Invoice.Create`, `Invoice.Send`, …) |
| `Extensions/*` | Swagger (دو Scheme: Bearer + ApiKey)، JWT، Rate Limiter، CORS، Serilog |
| `Program.cs` | فقط Composition Root: `AddDataLayer().AddCoreLayer().AddApiServices()` + pipeline |

### ۳-۴) Client — «UI فارسی RTL»

| پوشه | مسئولیت |
|------|----------|
| `Pages/*` | یک صفحه per سناریو؛ فقط فراخوانی `*ApiService` و نمایش؛ بدون منطق محاسباتی |
| `Shared/*` | کامپوننت‌های قابل استفاده‌ی مجدد (`PersianDatePicker`، `DataGrid`، `StatusBadge`، `MoneyDisplay`) |
| `Services/*ApiService.cs` | یک کلاس per Controller؛ `ApiClientBase` مدیریت `ApiResponse<T>` و خطا را centralized می‌کند |
| `Authentication/*` | نگه‌داری JWT در `localStorage` (ساده و قابل فهم برای Junior) + تزریق هدر و رفرش خودکار |
| `Helpers/PersianDateHelper.cs` | تبدیل/نمایش شمسی (`PersianCalendar`) — بدون پکیج اضافه |

---

## ۴) مدل چند‌مستأجری (Multi-Tenancy)

### ۴-۱) مدل داده

```
Tenant (مودی) 1 ──── 1 MoadianSetting (شناسه حافظه مالیاتی، شماره اقتصادی، آدرس‌ها، حالت امضا)
   │ 1
   ├── N UserTenant ── N User (هویت مشترک، SuperAdmin برای دسترسی به همه‌ی Tenants)
   ├── N Product      (شناسه کالا/خدمت، واحد، نرخ مالیات)
   ├── N Customer     (حقیقی/حقوقی، کد ملی/شناسه ملی، کد اقتصادی، کد پستی)
   ├── N Invoice ─┬─ N InvoiceItem
   │              ├─ N InvoicePayment
   │              └─ N InvoiceSubmission (هر تلاش ارسال/استعلام = یک رکورد Audit)
   ├── N ApiClient (کلید API، Scopeها، محدودیت نرخ)
   └── N AuditLog
```

- **`TenantId` روی همه‌ی جداول سازمانی** (`ITenantEntity`)؛ `User` و جداول Lookup مستثنی هستند.
- **Global Query Filter:** در `OnModelCreating` برای هر `ITenantEntity` فیلتر `e.TenantId == _tenantProvider.TenantId` اعمال می‌شود. اگر `TenantId` خالی باشد → نتیجه خالی (default deny)، نه همه‌ی داده‌ها. برای عملیات سیستمی (Seeder/Job) از `IgnoreQueryFilters()` با دلیل مستند استفاده می‌شود.
- **تنانت از کجا می‌آید؟** هرگز از Body/Query. از Claim `tenant_id` در JWT یا از `ApiClient.TenantId` در احراز هویت API Key → `HttpTenantContext` (CoreLayer) → `ITenantProvider` (DataLayer).
- **جلوگیری از تغییر TenantId:** `TenantStampInterceptor` در Update مقدار `TenantId` را immutable می‌کند و در Insert مقدار جاری را ست می‌کند.
- **ایندکس‌های یکتا Tenant-scoped:** تمام Unique Indexها با `TenantId` شروع می‌شوند (مثلاً `(TenantId, ExternalId)`، `(TenantId, NationalId)`).

### ۴-۲) چه چیزی «مشترک» می‌ماند؟

| داده | Tenant-scoped؟ | دلیل |
|------|----------------|-------|
| Lookupها (`InvoicePattern`, `InvoiceSubject`, `PaymentMethodCode`, `MeasuringUnit`, `MoadianErrorCode`) | خیر (سراسری) | داده‌ی مرجع ملی، یکسان برای همه‌ی مودیان |
| `User` | خیر + `UserTenant` | یک کاربر می‌تواند در چند مودی کار کند |
| `Invoice`, `Product`, `Customer`, `MoadianSetting`, `ApiClient`, `AuditLog` | ✅ بله | داده‌ی عملیاتی هر مودی |

---

## ۵) لایه‌ی واسط SDK (تنها محل تماس با `TaxCollectData`)

```
InvoiceService ──▶ IMoadianGateway ──▶ TaxCollectDataMoadianGateway ──▶ SDK
                  (Core/Interfaces)      (Core/Infrastructure/Moadian)
```

| فایل | نقش |
|------|-----|
| `Dto/MoadianInvoicePayload.cs` و بقیه | DTOهای **خودمان** (مالکیت ما، مستقل از SDK) — SDK فقط در Gateway دیده می‌شود |
| `Serialization/MoadianJson.cs` | تنظیمات Json (camelCase، `IgnoreNullValues`، `UnsafeRelaxedJsonEscaping` برای متن فارسی) |
| `Signing/IJwsSigner.cs` | `string Sign(string payload)` — قرارداد ما |
| `Signing/PrivateKeyOnlyJwsSigner.cs` | JWS Compact با `RS256` **بدون `x5c`** + پیاده‌سازی `ISignatory` SDK برای سازگاری با زنجیره‌ی احراز هویت |
| `Signing/Pkcs8CertificateJwsSigner.cs` | آداپتور `SignatoryFactory.Create(RSA, X509Certificate)` روی کلید/گواهی **در حافظه** |
| `Signing/Pkcs11TokenJwsSigner.cs` | آداپتور `Pkcs11SignatoryFactory` (Alias + LibraryPath + PIN) |
| `Signing/MoadianSignerFactory.cs` | انتخاب استراتژی بر اساس `MoadianSetting.SignatureMode` |
| `Client/MoadianClientFactory.cs` | ساخت/کش `ITaxApi` per (TenantId, Environment, BaseUrl) — چون SDK وضعیت داخلی (کش کلید عمومی) دارد |
| `Client/TaxCollectDataMoadianGateway.cs` | پیاده‌سازی `IMoadianGateway`: `SendInvoicesAsync`, `InquiryByReferenceAsync`, `InquiryByUidAsync`, `InquiryByTimeAsync`, `InquiryStatusAsync`, `GetServerInformationAsync`, `GetTaxpayerAsync`, `GetFiscalInformationAsync`, `RegisterPaymentAsync` — همه با نگاشت خطا به `TaxApiException` |
| `Errors/MoadianErrorTranslator.cs` | `ErrorModel.code` → `MoadianErrorCode` (DB) → پیام فارسی + راهنمای اقدام |

> **نکته‌ی صحت:** تنها پیاده‌سازی «با گواهی» از سورس 2.0.29 **تأییدشده** است؛ استراتژی «فقط کلید خصوصی» تا تأیید با سامانه، `IsExperimental=true` دارد.

---

## ۶) API ورودی سیستم‌های خارجی (ERP / فروش)

### ۶-۱) مسیر و احراز هویت

| موضوع | طراحی |
|-------|-------|
| Base Route | `POST /api/v1/external/invoices` |
| احراز هویت | Header `X-Api-Key` → `ApiKeyAuthenticationHandler` (Scheme مستقل `ApiKey`)؛ در DB فقط **هش SHA-256** کلید + `Prefix` برای شناسایی |
| Scope | `invoice.write`, `invoice.read`, `baseinfo.read`, `inquiry.read` — Policy-based |
| Rate Limit | Policy اختصاصی `external` با Partition بر `ApiClientId` (پیش‌فرض ۶۰ درخواست/دقیقه، قابل تنظیم per کلید) |
| مستندسازی | Tag جدا در Swagger + نمونه‌ی cURL |

### ۶-۲) Idempotency (جلوگیری از ثبت/ارسال تکراری)

```
POST /api/v1/external/invoices
Headers: X-Api-Key, Idempotency-Key (اختیاری)
Body: { externalId, customerCode, issueDate, items[], payments[] }
```

1. `ExternalId` **الزامی** و یکتا در سطح `(TenantId, ExternalId)`.
2. اگر فاکتور با همان `ExternalId` موجود باشد → `200 OK` با همان فاکتور و `isIdempotentReplay=true` (بدون ثبت دوم، بدون ارسال دوم).
3. اگر درخواست هم‌زمان دومی برسد → Unique Index دیتابیس مانع می‌شود و سرویس رکورد موجود را برمی‌گرداند (الگوی `catch DbUpdateException → read existing`).
4. `Idempotency-Key` جداگانه برای عملیات‌های غیرفاکتور (مثلاً ارسال مجدد) در `OutboxMessage`/Audit نگه داشته می‌شود.
5. ارسال به مودیان هم idempotent است: قبل از ارسال، `SubmissionStatus` چک می‌شود؛ فاکتور `Sent/Verified` دوباره ارسال نمی‌شود و `UID` موجود بازگردانده می‌شود.

### ۶-۳) قرارداد پاسخ

همیشه `ApiResponse<T>` با کدهای معنادار:

| حالت | HTTP | `ApiResponse.Code` |
|------|------|--------------------|
| ثبت جدید | 201 | `CREATED` |
| تکراری (Replay) | 200 | `IDEMPOTENT_REPLAY` |
| خطای اعتبارسنجی | 400 | `VALIDATION_FAILED` + `Errors[]` |
| Api Key نامعتبر/منقضی | 401 | `INVALID_API_KEY` |
| کمبود Scope | 403 | `FORBIDDEN_SCOPE` |
| Rate Limit | 429 | `RATE_LIMITED` + هدر `Retry-After` |
| خطای سامانه مودیان | 422 | `MOADIAN_REJECTED` + `MoadianErrorCode` |

---

## ۷) امنیت کلید خصوصی (خلاصه‌ی اجرایی — جزئیات در سند ۰۱ بند ۴-۳)

```
UI/API (آپلود کلید) → AesGcmSecretProtector.Protect() → DB (Ciphertext + Nonce + Tag + KeyVersion)
                                   ▲
                        KEK از Environment/User Secrets (خارج از DB)
                                   │
Signing ←──── CryptographicOperations.ZeroMemory ← RSA.ImportFromPem (فقط RAM، کش ۵ دقیقه‌ای)
```

| قاعده | پیاده‌سازی |
|-------|------------|
| هیچ کلیدی در لاگ | `SecretRedactor` در Middleware لاگ + Serilog Destructuring Policy |
| هیچ کلیدی در پاسخ API | ViewModel فقط `HasPrivateKey`, `CertificateSubject`, `ExpiresAt`, `Thumbprint` برمی‌گرداند |
| فیلد `PrivateKeyPem` فقط در ورودی Write-Only | در Validator و DTO اختصاصی؛ در Response همان DTO استفاده نمی‌شود |

---

## ۸) بسته‌های NuGet هر پروژه

| پروژه | پکیج‌ها |
|-------|---------|
| **Contracts** | **هیچ** (فقط BCL) — این محدودیت عمدی و بخشی از معماری است |
| **DataLayer** | `Microsoft.EntityFrameworkCore`, `.SqlServer`, `.Relational`, `.Design` (فقط `PrivateAssets=all`) |
| **CoreLayer** | `FluentValidation`, `FluentValidation.DependencyInjectionExtensions`, `Mapster`, `Mapster.DependencyInjection`, `ClosedXML` (Excel), `Microsoft.Extensions.Http`, `Microsoft.Extensions.Caching.Memory`, `Microsoft.Extensions.Hosting.Abstractions`, `Microsoft.Extensions.Options.ConfigurationExtensions`, **`TaxCollectData.Library` 2.0.29**, `Serilog` |
| **Api** | `Microsoft.AspNetCore.Authentication.JwtBearer`, `System.IdentityModel.Tokens.Jwt`, `Serilog.AspNetCore`, `Swashbuckle.AspNetCore` (+ FrameworkReference `Microsoft.AspNetCore.App`) |
| **Client** | `Microsoft.AspNetCore.Components.WebAssembly`, `...WebAssembly.DevServer`, `Microsoft.AspNetCore.Components.Authorization`, `Microsoft.Extensions.Http`, `Blazored.LocalStorage` |
| **Tests** | `Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio`, `Moq`, `FluentAssertions`, `Microsoft.EntityFrameworkCore.InMemory`, `coverlet.collector` |

> ⚠️ `TaxCollectData.Library` وابستگی‌های transitive دارد (`RestSharp 108`, `Newtonsoft.Json`, `BouncyCastle`, `JWT`, `jose-jwt`, `Pkcs11Interop`). این‌ها فقط در `CoreLayer` ظاهر می‌شوند و در هیچ لایه‌ی دیگری استفاده نمی‌شوند. توجه: `TaxCollectData.Library` روی `netstandard2.0` است و در .NET 8 مصرف می‌شود؛ این را **از نظر سازگاری تأییدشده** می‌دانم (سند رسمی .NET 7 به بالا را پشتیبانی می‌کند) ولی **بیلد واقعی را در این محیط نتوانستم اجرا کنم**.

---

## ۹) الگوهای تکرارشونده (که Junior باید یاد بگیرد)

### ۹-۱) `ApiResponse<T>`

```csharp
public sealed class ApiResponse<T>
{
    public bool IsSuccess { get; init; }
    public string Code { get; init; } = ApiResponseCodes.Ok;
    public string? Message { get; init; }
    public T? Data { get; init; }
    public IReadOnlyList<ApiError>? Errors { get; init; }
    public string? TraceId { get; init; }
}
```

### ۹-۲) جریان یک درخواست (مثال: ثبت فاکتور از ERP)

```
HTTP POST /external/invoices
  → CorrelationIdMiddleware            (X-Correlation-Id)
  → RateLimiter ("external" policy)    (429 در صورت عبور)
  → ApiKeyAuthenticationHandler        (TenantId + Scopes → Claims)
  → ExternalInvoicesController         (thin: model → service)
  → ExternalInvoiceService             (Validator → Idempotency → Calculation → Save → Outbox)
  → IUnitOfWork.SaveChangesAsync()
  → ApiResponse<InvoiceViewModel>      (201 / 200 Replay)
```

### ۹-۳) الگوی نوشتن سرویس (اجباری)

```csharp
public sealed class ProductService : IProductService
{
    private readonly IProductRepository _productRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;
    private readonly IValidator<CreateProductRequest> _validator;

    public async Task<ProductViewModel> CreateAsync(CreateProductRequest request, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(request, ct);
        var entity = _mapper.Map<Product>(request);
        await _productRepository.AddAsync(entity, ct);
        await _unitOfWork.SaveChangesAsync(ct);
        return _mapper.Map<ProductViewModel>(entity);
    }
}
```

### ۹-۴) قواعد نام‌گذاری و ساختار

| مورد | قاعده |
|------|-------|
| نام فایل | یک کلاس عمومی per فایل، نام فایل = نام کلاس |
| Namespace | `MoudianMiddleware.<Layer>.<Area>.<SubArea>` (مثلاً `MoudianMiddleware.CoreLayer.Services.Invoices`) |
| متد async | پسوند `Async` + `CancellationToken ct` آخرین پارامتر (اجباری) |
| ViewModel | `XxxViewModel` (خروجی)، `CreateXxxRequest`/`UpdateXxxRequest` (ورودی) — پسوند `Dto` استفاده نمی‌شود؛ **محل فایل: `Contracts/ViewModels/<Module>/`** |
| Exception | `XxxException`، ارث‌بری از `AppException` |
| Validation | پیام‌ها فارسی و در `PersianMessages` متمرکز؛ `WithMessage(PersianMessages.X)` |
| تاریخ | Domain: `DateTimeOffset` UTC؛ نمایش: `PersianDateHelper` |

---

## ۱۰) نقشه‌ی ماژول‌ها (چه فایلی برای چه ماژولی)

| # | ماژول | DataLayer | CoreLayer | Api | Client |
|---|-------|-----------|-----------|-----|--------|
| 1 | تنظیمات مودی | `Tenant`, `MoadianSetting`, `MoadianCredential` | `MoadianSettingService`, `AesGcmSecretProtector`, `MoadianSignerFactory` | `MoadianSettingsController` | `Settings/MoadianSettings.razor` |
| 2 | کالا و خدمات | `Product`, `MeasuringUnit` | `ProductService`, `CreateProductValidator` | `ProductsController` | `Products/*` |
| 3 | مشتریان | `Customer` | `CustomerService` | `CustomersController` | `Customers/*` |
| 4 | صورتحساب | `Invoice`, `InvoiceItem`, `InvoicePayment` | `InvoiceService`, `InvoiceCalculationService`, `InvoiceMapper` | `InvoicesController` | `Invoices/*` |
| 5 | موتور ارسال | `InvoiceSubmission`, `OutboxMessage` | `InvoiceSubmissionService`, `TaxCollectDataMoadianGateway`, `MoadianClientFactory` | `InvoicesController.Send` | دکمه‌ی «ارسال» + `InvoiceDetails` |
| 6 | پیگیری وضعیت | `InvoiceSubmission`, `InvoiceInquiryLog` (داخل Submission) | `InquiryService`, `SubmissionInquiryJob` | `InquiriesController` | `Inquiries/InquiryList.razor` |
| 7 | خطا و Retry | `MoadianErrorCode` | `MoadianErrorTranslator`, `RetryPolicy` | `InquiriesController.Retry` | نمایش خطای فارسی + Retry |
| 8 | لاگ و Audit | `AuditLog` | `AuditLogService`, `SecretRedactor` | `AuditLogsController` | `Audit/AuditLogList.razor` |
| 9 | گزارش‌ها | — (روی Repositoryها) | `ReportService`, `ExcelExportService` | `ReportsController` | `Reports/InvoiceReport.razor` |
| 10 | کاربران و نقش‌ها | `User`, `UserTenant`, `RefreshToken` | `UserService`, `AuthService`, `TokenService` | `UsersController`, `AuthController` | `Users/*`, `Auth/Login.razor` |
| + | ورودی ERP | `ApiClient` | `ExternalInvoiceService`, `ApiClientService` | `Controllers/External/*` | — |

---

## ۱۱) ریسک‌ها و موارد «نیازمند تأیید» (شفاف)

| # | ریسک | اثر | راهکار |
|---|------|-----|--------|
| 1 | آدرس Sandbox نامعلوم | تست E2E غیرممکن | فیلد خالی + «نیاز به تأیید» در UI؛ Seed نمی‌شود |
| 2 | رفتار «امضا بدون گواهی» در 2.0.29 تأیید نشده | ممکن است سرور بسته را رد کند | پرچم `IsExperimental` + دکمه‌ی «تست اتصال» + پیش‌فرض «با گواهی» |
| 3 | جدول رسمی `inp`/`ins`/`pmt` در دست نیست | مقادیر Seed ممکن است ناقص باشد | Enum + Lookup قابل‌ویرایش در DB، بدون Magic Number در کد |
| 4 | `dotnet` در محیط من نیست | بیلد/تست اجرا نشد | سند README با دستور دقیق؛ CPM برای اصلاح سریع نسخه |
| 5 | ~~وابستگی EF به WASM~~ | ✅ **حل شد** | پروژه‌ی `Contracts` (BCL-only) اضافه شد؛ Client دیگر به CoreLayer ارجاع ندارد |
| 6 | ناسازگاری `lt` (String در سند، `byte?` در SDK) و فیلدهای حمل‌ونقل | خطای اعتبارسنجی برای الگوهای حمل | DTO اختصاصی + تست `MoadianJsonSerializationTests` |

---

## ۱۲) قدم بعدی (مرحله ۳)

1. ساخت `MoudianMiddleware.sln` + `Contracts` + `DataLayer` + `Tests` با ارجاع‌های صحیح (Core/Api/Client در مراحل ۴/۵/۶ اضافه می‌شوند تا هیچ پروژه‌ی بدون EntryPoint در Solution نباشد).
2. `BaseEntity` / `AuditableEntity` / `ITenantEntity` و `MoudianDbContext`.
3. همه‌ی Entityها + Fluent API configurationها + ایندکس‌های Tenant-scoped.
4. `UnitOfWork` + Repositoryهای عمومی و اختصاصی.
5. Interceptorها (Audit + Tenant).
6. Seed اولیه (نقش‌ها و Permissionها، واحدها، الگو/موضوع صورتحساب، روش‌های پرداخت، کدهای خطا) + کاربر Admin از Environment.
7. **ERD متنی** در `docs/03-database-design-fa.md` + دستورهای `dotnet ef` برای ساخت Migration.

> **یادداشت امضا (تصمیم نهایی شما):** SDK فقط `2.0.29` و فقط در `Directory.Packages.props`. پیش‌فرض هر مودی `Pkcs8Certificate`؛ `Pkcs11Token` اختیاری (فقط برای پایانه‌ی محلی با درایور)؛ `PrivateKeyOnly` صرفاً `IsExperimental` و **هرگز پیش‌فرض نیست** — در UI نیازمند تأیید صریح و «تست اتصال» موفق است.

**منتظر تأیید شما برای ورود به مرحله ۳ هستم.**
