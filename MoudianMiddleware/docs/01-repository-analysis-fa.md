# مرحله ۱ — مطالعه‌ی مخزن مرجع و خلاصه‌ی فنی

**پروژه:** نرم‌افزار واسط مودیان (Moudian Middleware)
**مخزن مرجع مورد مطالعه:** `github.com/mohammadforoughifar/MoadianByCertificate`
**نسخه‌ی SDK بررسی‌شده:** `TaxCollectData.Library` **2.0.29** (target: `netstandard2.0`)
**تاریخ مطالعه:** ۱۴۰۴/۰۸/۰۷ (2026-10-29)

---

## ۰) چکیده‌ی اجرایی (۹ حکم کلیدی که بر معماری ما اثر می‌گذارد)

| # | یافته | تأثیر معماری |
|---|-------|---------------|
| ۱ | مخزن مرجع **هیچ README / OpenAPI / سورس وب‌سرویس رسمی** ندارد؛ فقط ۴ فایل: پکیج NuGet، همان پکیج به‌صورت rar، زیپ سورس SDK، و PDF «راهنمای کاربری SDK — .NET 7». تنها سند رسمی همان PDF (۳۰ صفحه) است. | مرجع دانش ما = PDF + سورس SDK. هر ابهام باید یا از PDF حل شود یا از شما پرسیده شود. |
| ۲ | SDK یک لایه‌ی سطح بالا (`ITaxApi`) و یک لایه‌ی سطح پایین (`ILowLevelTaxApi`) دارد. امضا (JWS) و رمزنگاری (JWE) در `ISignatory` / `IEncryptor` جدا شده‌اند. | ما فقط همین دو اینترفیس + `ITaxApi` را مصرف می‌کنیم و پشت یک `IMoadianGateway` می‌گذاریم؛ هیچ کلاس SDK مستقیم در Controller/Service دیده نمی‌شود. |
| ۳ | احراز هویت **دو مرحله‌ای** است: `GET /api/v2/nonce` بدون توکن → امضای `{nonce, clientId}` به‌صورت JWS → ارسال در هدر `Authorization` با پیش‌وند `Bearer `. (برای هر درخواست، ۲ فراخوانی HTTP) | باید Token/Nonce Cache و شمارش فراخوانی‌ها را در لایه‌ی Core مدیریت کنیم (SDK کش نمی‌کند). |
| ۴ | در **2.0.29** که سورسش را دارم، `JwsSignatory` هدر `x5c` را **ثابت (hard-coded)** می‌سازد و کلاس امضاکننده‌ای که فقط کلید خصوصی بگیرد وجود ندارد. ادعای شما درباره‌ی **0.0.27** (امضا بدون گواهی) را نمی‌توانم با سورس تأیید کنم، چون نسخه‌ی 0.0.27 در مخزن نیست. | **سه استراتژی** امضا طراحی می‌شود: (الف) فقط کلید خصوصی، (ب) PKCS#8 + گواهی، (ج) PKCS#11. انتخاب per-مودی در تنظیمات. برای حالت (الف) یک `ISignatory` اختصاصی خودمان (JWS بدون `x5c`) می‌نویسیم. جزئیات + عدم‌قطعیت‌ها در بند ۴. |
| ۵ | SDK کلید خصوصی را **از مسیر فایل** می‌خواند (`Pkcs8SignatoryFactory.Create(path, path)`). | کلید خصوصی رمزنگاری‌شده در DB (AES-GCM با KEK خارج از DB) و **فقط در حافظه** رمزگشایی می‌شود؛ از `SignatoryFactory.Create(RSA, X509Certificate)` که public است استفاده می‌کنیم و هرگز فایل کلید روی دیسک نمی‌نویسیم. |
| ۶ | استثنای اصلی SDK یعنی `TaxCollectionApiException` **internal** است. | در Wrapper نمی‌توانیم `catch (TaxCollectionApiException)` بنویسیم؛ باید `Exception` را بگیریم و بر اساس `ex.GetType().Name` / پیام JSON نگاشت کنیم → یک `TaxApiException` اختصاصی خودمان. |
| ۷ | در `TaxApi.SendInvoicesAsync` خط `response.Result` داخل متد async استفاده شده و در نسخه‌ی sync از `.Result` استفاده می‌شود (ریسک deadlock). | هرگز متدهای sync را صدا نمی‌زنیم؛ فقط `...Async` و همیشه با `CancellationToken` سطح بالای خودمان (SDK CT ندارد → ما با `Timeout`/`Task` مدیریت می‌کنیم). |
| ۸ | DTOهای SDK نام فیلدها را با **کدهای رسمی مودیان** (camelCase: `taxid`, `indatim`, `sstid`, …) نگه می‌دارند و همه‌ی فیلدهای رشته‌ای `non-nullable` تعریف شده‌اند. | نرمال‌سازی payload (ignoring null/empty) را خودمان انجام می‌دهیم تا اعتبارسنج سرور خطای «فیلد خالی» ندهد؛ نگاشت بین Entity و DTO مودیان در CoreLayer انجام می‌شود. |
| ۹ | بین **ارسال** و **استعلام** باید حداقل **۱۰ ثانیه** فاصله باشد و ممکن است نتیجه `IN_PROGRESS` بماند (استعلام مجدد لازم است). | BackgroundJob و منطق Retry/Inquiry در مرحله ۷ با قاعده‌ی backoff طراحی می‌شود، نه با `Thread.Sleep`. |
| ۱۰ | سامانه باید **چند مودی** (چند MemoryId) را هم‌زمان سرویس دهد و هم **API ورودی برای ERP** داشته باشد. | Multi-tenant با `TenantId` + Global Query Filter؛ احراز هویت دو مسیره: JWT (UI) و API Key (سیستم‌های خارجی)؛ Idempotency با `ExternalId` + ایندکس یکتا. (بند ۷)

---

## ۱) ساختار مخزن مرجع

```
MoadianByCertificate/
├── TaxCollectData.Library.2.0.29.nupkg        ← بسته‌ی آماده (lib/netstandard2.0)
├── TaxCollectData.Library.2.0.29.rar          ← همان بسته
├── tax-collect-data-sdk-dotnet-...-2.0.29.zip ← **سورس کامل SDK**
│   └── tax-collect-data-sdk-dotnet-tax-collect-data-sdk-2.0.29/
│       ├── Tax Collect Data SDK.sln
│       ├── TaxCollectData.Library/             (هسته‌ی SDK)
│       └── TaxCollectData.Sample/              (نمونه‌های اجرایی)
└── راهنمای کاربری SDK اتصال به زیرسامانه‌ی جمع‌آوری .NET_7.pdf
```

ساختار درونی `TaxCollectData.Library` (۲۴ پوشه/ناحیه):

| پوشه | محتوا |
|------|-------|
| `Abstraction/Clients` | `IClient`, `ITaxApi`, `ILowLevelTaxApi`, `ITaxPublicApi` |
| `Abstraction/Cryptography` | `ISignatory`, `IEncryptor` |
| `Abstraction/Providers` | `IRequestProvider`, `IPacketProvider`, `ISerializer`, `ITaxIdProvider`, `IErrorDetectionAlgorithm`, `ICurrentDateProvider` |
| `Abstraction/Properties` | `IUrlProperties`, `IHttpHeadersProperties` |
| `Abstraction/Repositories` | `IEncryptionKeyRepository` |
| `Clients` | `TaxApi`, `LowLevelTaxApi`, `TaxPublicApi`, `RestSharpHttpClient` |
| `Cryptography` | `JwsSignatory`, `JwsHardwareTokenSignatory`, `JweEncryptor`, `EmptySignatory`, `EmptyEncryptor` |
| `Middlewares` | `Middleware` (chain of responsibility)، `SignatoryMiddleware`, `EncryptionMiddleware`, `EmptyMiddleware` |
| `Factories` | `TaxApiFactory`, `SignatoryFactory`, `Pkcs8SignatoryFactory`, `Pkcs11SignatoryFactory`, `EncryptorFactory`, `PrivateKeyFactory`, `X509CertificateFactory`, `PacketProviderFactory`, `TaxIdProviderFactory` |
| `Providers` | `RequestProvider` (ساخت HTTP Request)، `PacketProvider` (ساخت Packet)، `TaxIdProvider`, `CurrentDateProvider`, `Serializer` |
| `Algorithms` | `VerhoeffAlgorithm` (رقم کنترل شماره مالیاتی) |
| `Dto` / `Models` | ورودی/خروجی‌های API |
| `Enums` | `PaymentMethod`, `RequestStatus` |
| `Constants` | `PacketTypeConstants` (نام مسیرهای API) |
| `Configs` | `JsonSerializerConfig` (camelCase + StringEnum + UnsafeRelaxedJsonEscaping) |
| `Exceptions` | `TaxCollectionApiException` (**internal**)، `UnknownResponseException` (public)، `NotInitializedException` |

نمونه‌ها: `SampleInvoiceCreator`، `DocumentSampleAsync`، `SampleExportInvoiceCreator` (سناریوی کامل: ساخت → ارسال → Sleep 10s → استعلام).

---

## ۲) نقشه‌ی کلاس‌ها و متدهای SDK

### ۲-۱) `ITaxApi` (سطح بالا — مسیر پیشنهادی ما)

| متد | ورودی | خروجی | توضیح |
|-----|-------|-------|-------|
| `SendInvoicesAsync` | `List<InvoiceDto>` | `List<InvoiceResponseModel>` | امضا + رمزنگاری + بسته‌بندی + ارسال (batch) |
| `InquiryByReferenceIdAsync` | `InquiryByReferenceNumberDto(refNumbers, start?, end?)` | `List<InquiryResultModel>` | استعلام با شماره پیگیری |
| `InquiryByUidAsync` | `InquiryByUidDto(uidList, fiscalId, start?, end?)` | `List<InquiryResultModel>` | استعلام با UID |
| `InquiryByTimeAsync` | `InquiryByTimeRangeDto(start, end?, Pageable?, RequestStatus?)` | `List<InquiryResultModel>` | استعلام بازه‌ای صفحه‌بندی‌شده |
| `InquiryInvoiceStatusAsync` | `List<string> taxIds` | `List<InvoiceStatusInquiryResponseDto>` | وضعیت در **کارپوشه** (APPROVED/REJECTED/…) |
| `GetTaxpayerAsync` | `economicCode` | `TaxpayerModel` | اطلاعات پرونده‌ی مودی |
| `GetFiscalInformationAsync` | `memoryId` | `FiscalFullInformationModel` | اطلاعات حافظه‌ی مالیاتی |
| `RegisterPaymentRequestAsync` | `RegisterPaymentRequestDto` | `RegisterPaymentResultModel` | ثبت پرداخت صورتحساب |

### ۲-۲) `ITaxPublicApi` و `ILowLevelTaxApi`

- `ITaxPublicApi.GetServerInformationAsync()` → `ServerInformationModel { serverTime, publicKeys[] { key(Base64), id, algorithm="RSA", purpose=1 } }` — منبع **کلید عمومی سازمان** برای رمزنگاری.
- `ILowLevelTaxApi`: همان متدهای `ITaxApi` منهای بسته‌بندی رمزنگاری/امضا (`SendInvoices(List<PacketDto>)`) → برای وقتی که payload را خودمان می‌سازیم.

### ۲-۳) کارخانه‌ها و رمزنگاری

```csharp
// الگوی رسمی راه‌اندازی (PDF ص ۶) — همان چیزی که ما پشت Abstraction می‌بریم
var properties   = new TaxProperties(MemoryId);                 // MemoryId = شناسه یکتای حافظه مالیاتی (۶ کاراکتر، مثل A11216)
var factory      = new TaxApiFactory(ApiUrl, properties);       // ApiUrl مثل https://tp.tax.gov.ir/requestsmanager
var signatory    = new Pkcs8SignatoryFactory().Create(pemPath, crtPath);
var publicApi    = factory.CreatePublicApi(signatory);
var encryptor    = new EncryptorFactory().Create(publicApi);    // درون خودش GetServerInformation را صدا می‌زند
ITaxApi taxApi   = factory.CreateApi(signatory, encryptor);
```

- **امضا (JWS):** `RS256`، بدنه = JSON صورتحساب، هدرها: `x5c=[Base64(cert)]`، `sigT=UTC "yyyy-MM-dd'T'HH:mm:ss'Z'"`، `typ="jose"`، `crit=["sigT"]`، `cty="text/plain"`.
- **رمزنگاری (JWE):** `RSA-OAEP-256` + `A256GCM`، حالت `Compact`، هدر `kid` = شناسه‌ی کلید عمومی سرور. کلید هر ۱ ساعت رفرش می‌شود (`EncryptionKeyRepository`).
- **بسته (Packet):** `{ header: { requestTraceId = UID (خودمان تولید می‌کنیم), fiscalId = MemoryId }, payload = Encrypt(Sign(invoiceJson)) }`.
- **شماره مالیاتی (`TaxIdProvider`):** `memoryId(6) + hex(روز از epoch).PadLeft(5) + hex(serial).PadLeft(10) + رقم کنترل Verhoeff` و در انتها `ToUpperInvariant()` → طول ۲۲ کاراکتر. `inno = serial.ToString("X").PadLeft(10,'0')` و `indatim` = Unix milliseconds.
- **`PacketTypeConstants`** (مسیرهای API): `nonce`, `server-information`, `invoice`, `inquiry`, `inquiry-by-uid`, `inquiry-by-reference-id`, `inquiry-invoice-status`, `invoice-payment`, `fiscal-information`, `taxpayer`, `taxpayer-info`, `taxpayer-article6-status`. همه زیر `.../api/v2/<request>`.

### ۲-۴) مدل‌های پاسخ

| مدل | فیلدها |
|-----|--------|
| `BatchResponseModel` | `timestamp`, `result: List<ResponsePacketModel>` |
| `ResponsePacketModel` | `uid`, `packetType`, `referenceNumber`, `data` (در زمان ارسال `null`) |
| `InvoiceResponseModel` (SDK می‌سازد) | `uid`, `referenceNumber`, `taxId`, `data` |
| `InquiryResultModel` | `referenceNumber`, `uid`, `status`, `data: { error[], warning[], success }`, `packetType`, `fiscalId`, `sign` |
| `ErrorModel` | `code`, `message` |
| `InvoiceStatusInquiryResponseDto` | `taxId`, `invoiceStatus`, `article6Status`, `error` |
| `ErrorResponseDto` (خطای HTTP) | `timestamp`, `requestTraceId`, `errors[]` |

---

## ۳) جریان کامل ثبت یک صورتحساب (Flow — گام به گام)

```
[1] تنظیمات مودی (MemoryId، EconomicCode، گواهی/کلید، BaseUrl)          ← DB ما (رمزنگاری‌شده)
        │
[2] تولید شماره مالیاتی: serial (یکتا) → taxId (22 کاراکتر) + inno      ← TaxIdProvider
        │
[3] ساخت InvoiceDto: Header + Body + Payments (+ Extension)            ← InvoiceService (Core)
        │
[4] Serialize (camelCase)  →  JWS Sign (RS256, x5c, sigT)               ← ISignatory
        │
[5] JWE Encrypt (RSA-OAEP-256/A256GCM با کلید عمومی سرور)               ← IEncryptor
        │
[6] بسته‌بندی Packet { requestTraceId=UID, fiscalId=MemoryId, payload }  ← PacketProvider
        │
[7] احراز هویت: GET /api/v2/nonce (بدون توکن)                            ← IClient
    └─ Sign({ nonce, clientId=MemoryId }) → Authorization: Bearer <JWS>
        │
[8] POST /api/v2/invoice   (batch: List<PacketDto>)                     ← ILowLevelTaxApi
        └─ پاسخ: BatchResponseModel { result[ { uid, referenceNumber } ] }
        │
[9] ذخیره‌ی UID + ReferenceNumber + وضعیت "ارجاع‌شده به سامانه"          ← DB + AuditLog
        │
[10] (≥ ۱۰ ثانیه) استعلام: by-Reference / by-Uid → status:
     IN_PROGRESS → دوباره؛  SUCCESS → ثبت‌شده؛  FAILED/TIMEOUT/NOT_FOUND → خطا + Retry
        │
[11] (اختیاری) استعلام کارپوشه با taxIds → APPROVED / SYSTEMIC_APPROVED / REJECTED / ...
```

نکته‌های عملیاتی استخراج‌شده از PDF:

- بازه‌ی زمانی استعلام اگر داده نشود، به‌طور پیش‌فرض **۲۴ ساعت گذشته** است.
- `pageNumber` پیش‌فرض در سند **۱** و `pageSize` بین **۱ تا ۱۰۰** (کلاس `Pageable` در کد پیش‌فرض `0/10` — ناسازگاری کد و سند؛ ما صریح مقدار می‌دهیم).
- `IN_PROGRESS` یعنی در صف اعتبارسنجی؛ باید استعلام تکرار شود (پایه‌ی طراحی Job مرحله ۷).
- خروجی `sign` در `InquiryResultModel` = بسته‌ی امضاشده‌ی نتیجه‌ی اعتبارسنجی (مدرک قابل استناد برای Audit).

---

## ۴) احراز هویت و امضا: سه استراتژی (به‌درخواست شما بازطراحی شد)

### ۴-۱) چه چیزی در کد **تأییدشده** است و چه چیزی **نامطمئن**

| موضوع | وضعیت | شاهد |
|-------|-------|------|
| در 2.0.29 هدر `x5c` همیشه ست می‌شود و `ISignatory` فقط از راه `SignatoryFactory.Create(RSA, X509Certificate)` یا `Pkcs11SignatoryFactory` ساخته می‌شود | ✅ **تأییدشده از سورس** | `Cryptography/JwsSignatory.cs` (هدر `HeaderName.X5c` بدون شرط) و `Factories/SignatoryFactory.cs` (تنها دو overload، هر دو گواهی می‌خواهند) |
| در 0.0.27 امضا فقط با کلید خصوصی کار می‌کند | ❓ **تأیید نشده** — نسخه‌ی 0.0.27 در مخزن نیست؛ فقط `2.0.29` موجود است | — |
| سرور مودیان بستهٔ JWS **بدون `x5c`** را در هدر احراز هویت یا در payload می‌پذیرد یا نه | ❓ **نامطمئن** — سند رسمی موجود (PDF) فقط مسیر «با گواهی» را توضیح داده و درباره‌ی حالت بدون گواهی چیزی نمی‌گوید | PDF ص ۱۶ (`ISignatory` = «بستهٔ امضاشدهٔ JWS طبق استاندارد API») |
| آدرس Sandbox | ❓ **در مخزن نیست** → فیلد خالی و «نیاز به تأیید» | جست‌وجوی کامل PDF: تنها `https://tp.tax.gov.ir/requestsmanager` (عملیاتی). آدرس‌های `master.nta.local` / `localhost:8055` / `tp-nta.abriment.mohaymen.ir` در نمونه‌های سورس، سرورهای **داخلی توسعه‌ی SDK** هستند، نه محیط رسمی |

> 🔎 **جمع‌بندی صادقانه:** «امضا بدون گواهی» یک قابلیت اثبات‌شده در 2.0.29 نیست. من آن را به‌عنوان یک **استراتژی قابل انتخاب و قابل تست** پیاده می‌کنم (چون شما در محیط واقعی دیده‌اید کار می‌کند)، ولی تا زمانی که با Sandbox واقعی تأیید نشود، برچسب «آزمایشی / نیازمند تأیید با سامانه» می‌خورد و حالت پیش‌فرض محصول **با گواهی** خواهد بود.

### ۴-۲) طراحی سه استراتژی

```
IMoadianSignerFactory (CoreLayer/Infrastructure/Moadian/Signing)
    └── Create(credentialMaterial, MoadianSignatureMode) → IJwsSigner
            ├── (الف) PrivateKeyOnlyJwsSigner      ← پیاده‌سازی خودمان: JWS Compact، alg=RS256، بدون x5c
            ├── (ب) Pkcs8CertificateJwsSigner      ← آداپتور روی SDK: SignatoryFactory.Create(RSA, X509Certificate)
            └── (ج) Pkcs11TokenJwsSigner           ← آداپتور روی SDK: Pkcs11SignatoryFactory.Create(alias, lib, pin)
```

| معیار | (الف) فقط کلید خصوصی | (ب) PKCS#8 + گواهی | (ج) PKCS#11 توکن |
|-------|----------------------|--------------------|-------------------|
| منبع کد | پیاده‌سازی خودمان (`RSA.SignData` + Base64Url) | SDK 2.0.29 | SDK 2.0.29 + `ShuttleCsp11_3003.dll` |
| هدرهای JWS | `alg, typ=jose, cty=text/plain, sigT` (**بدون `x5c`**) | `alg, typ, cty, sigT, crit, x5c` | همان (ب) با امضا داخل توکن |
| پیش‌نیاز سخت‌افزاری | ندارد | ندارد | توکن USB + درایور + PIN |
| تجربه‌ی کاربر نهایی | متوسط (آپلود کلید). **باید مشخص شود سرور می‌پذیرد یا نه** | **ساده‌ترین و مطمئن‌ترین** (گواهی + کلید) | پیچیده (Alias/PIN/درایور) |
| مناسب Job سروری | ✅ | ✅ | ⚠️ عملاً نه (توکن باید به سرور وصل باشد) |
| ریسک اصلی | رد شدن بسته توسط سرور | مدیریت امن کلید | وابستگی به سخت‌افزار |

**چگونه (الف) بدون fork کردن SDK ممکن می‌شود:** `ISignatory` در SDK یک اینترفیس **public** است (`string Sign(string)` و `string Sign(object)`). پس کلاس `PrivateKeyOnlyJwsSigner` علاوه بر `IJwsSigner` خودمان، **همان `ISignatory` SDK** را هم پیاده می‌کند؛ نتیجه: زنجیره‌ی احراز هویت `RestSharpHttpClient` (nonce → امضا → Bearer) دست‌نخورده کار می‌کند و فقط محتوای هدر JWS تغییر می‌کند. هیچ کد SDK کپی/دستکاری نمی‌شود.

**توصیه‌ی نهایی من:** پیش‌فرض = (ب) PKCS#8 + گواهی؛ (ج) به‌عنوان گزینه برای پایانه‌ی محلی؛ (الف) برای مشتریانی که شما در عمل دیده‌اید کار می‌کند — با پرچم `IsExperimental` در تنظیمات و تست اتصال (Connection Test) قبل از فعال‌سازی.

### ۴-۳) نگه‌داری کلید خصوصی (الزام امنیتی شما)

1. **در DB فقط Ciphertext:** `MoadianCredential.EncryptedPrivateKey`, `Nonce`, `Tag`, `KeyVersion`. هرگز کلید خام، هرگز Connection String در کد.
2. **Envelope Encryption:** KEK از Environment Variable / User Secrets (`MOUDIAN__MASTER_KEY`) → با آن DEK هر مودی wrap می‌شود؛ KEK هیچ‌گاه در DB نیست. اگر KEK موجود نباشد، سرویس در Startup **fail-fast** می‌کند (نه اجرای ناامن).
3. **رمزگشایی فقط در حافظه:** `ISecretProtector.Unprotect(...)` → `RSA.ImportFromPem(...)` در RAM، کش با انقضای مطلق کوتاه (مثلاً ۵ دقیقه) و پاک‌سازی با `CryptographicOperations.ZeroMemory`.
4. **هیچ فایلی روی دیسک:** برخلاف `Pkcs8SignatoryFactory` که مسیر فایل می‌خواهد، از `SignatoryFactory.Create(RSA, X509Certificate)` استفاده می‌شود.
5. **Audit:** هر بار استفاده از کلید (نه محتوای آن) در `AuditLog` با `TenantId`, `UserId`, `Action=MoadianSign` ثبت می‌شود؛ در لاگ‌ها کلید/گواهی/PIN ماسک می‌شوند.




---

## ۵) استخراج فیلدها، انواع داده و مقادیر الزامی

جمع‌بندی جدول رسمی PDF (۱۰۲ قلم اطلاعاتی). ستون «الزام» بر اساس نمونه‌ی معتبر سند و رفتار اعتبارسنج مودیان تنظیم شده و در مرحله ۳ به `FluentValidation` تبدیل می‌شود.

### ۵-۱) سربرگ (`HeaderDto`)

| کد | نام فارسی | نوع در SDK | الزام | نکته |
|----|-----------|-----------|-------|------|
| `taxid` | شماره منحصر به فرد مالیاتی | `string` | ✅ | ۲۲ کاراکتر، تولید با Verhoeff |
| `indatim` | تاریخ/زمان صدور (میلادی) | `long` | ✅ | Unix **milliseconds** |
| `indati2m` | تاریخ/زمان ایجاد | `long?` | ⭕ | اغلب = `indatim` |
| `inty` | نوع صورتحساب | `int?` | ✅ | ۱ اصلی، ۲ اصلاحی، ۳ ابطالی، ۴ برگشت از فروش *(تأیید شود)* |
| `inno` | سریال صورتحساب | `string?` | ✅ | hex ده‌رقمی |
| `irtaxid` | شماره مالیاتی صورتحساب مرجع | `string?` | شرطی | برای اصلاحی/ابطالی/برگشتی |
| `inp` | الگوی صورتحساب | `int?` | ✅ | نمونه: ۱ فروش، ۷ … → جدول کامل لازم است |
| `ins` | موضوع صورتحساب | `int` | ✅ | ۱ فروش، ۲ برگشت از فروش … → جدول کامل لازم است |
| `tins` | شماره اقتصادی فروشنده | `string?` | ✅ | ۱۱ رقم |
| `tob` | نوع شخص خریدار | `int?` | ✅ | ۱ حقیقی، ۲ حقوقی |
| `bid` | شناسه/کد ملی/کد فراگیر خریدار | `string?` | ✅ | حقیقی: کد ملی، حقوقی: شناسه ملی |
| `tinb` | شماره اقتصادی خریدار | `string?` | شرطی | حقوقی‌ها |
| `bpc` / `bbc` / `sbc` | کد پستی خریدار / شعبه خریدار / شعبه فروشنده | `string?` | شرطی | ۱۰ رقم، بدون خط تیره |
| `setm` | روش تسویه | `int?` | ✅ | ۱ نقدی، ۲ نسیه، ۳ نقدی/نسیه |
| `tprdis` | مجموع قبل از تخفیف | `long?` | ✅ | ریال، ≥ ۰ |
| `tdis` | مجموع تخفیفات | `long?` | ✅ | ≤ `tprdis` |
| `tadis` | مجموع پس از تخفیف | `long?` | ✅ | = `tprdis - tdis` |
| `tvam` | مجموع مالیات ارزش افزوده | `long?` | ✅ | = Σ `vam` |
| `todam` | مجموع سایر مالیات/عوارض | `long?` | ✅ | = Σ `odam + olam` |
| `tbill` | مجموع صورتحساب | `long?` | ✅ | = `tadis + tvam + todam` |
| `tax17`, `cap`, `insp`, `tvop` | مالیات ماده ۱۷ / پرداخت نقدی / نسیه / سهم VAT از پرداخت | `long?` | شرطی | با `setm` گره خورده |
| `crn`, `billid`, `scln`, `scc`, `cdcn`, `cdcd`, `tonw`, `torv`, `tocv`, `tinc`, `lno`, `lrno`, `ocu`, `oci`, `dco`, `dci`, `tid`, `rid`, `lt`, `cno`, `did`, `sg[]`, `asn`, `asd`, `In`, `an` | حمل‌ونقل/گمرک/بورس/بیمه/قرارداد | — | ⭕ | فقط برای الگوهای خاص (`inp`) |
| `bpn`, `ft`, `tinc` | گذرنامه/پرواز/آژانس | — | ⭕ | الگوهای خاص |

> ⚠️ ناسازگاری: `lt` (نوع بارنامه/حمل) در سند **String** است ولی در `HeaderDto` به‌صورت `byte?` تعریف شده؛ `ocu/oci/dco/dci` و `sg` ایندکس‌دار هستند. این‌ها در مرحله ۴ با DTO اختصاصی خودمان (و مپ صریح) پوشش داده می‌شوند.

### ۵-۲) اقلام (`BodyItemDto`)

| کد | نام فارسی | نوع | الزام | نکته |
|----|-----------|-----|-------|------|
| `sstid` | شناسه کالا/خدمت | `string` | ✅ | از فهرست رسمی (۱۳ رقمی) |
| `sstt` | شرح کالا/خدمت | `string` | ✅ | |
| `mu` | واحد اندازه‌گیری | `string` | ✅ | کد واحد (مثل ۱۶۴) |
| `am` | تعداد/مقدار | `decimal?` | ✅ | > ۰، حداکثر ۵ رقم اعشار |
| `fee` | مبلغ واحد | `decimal?` | ✅ | ریال |
| `cfee`, `cut`, `exr`, `cpr` | میزان/نوع/نرخ ارز | — | شرطی | فقط الگوی ارزی |
| `prdis` | مبلغ قبل از تخفیف | `long?` | ✅ | = `am × fee` (گرد شده) |
| `dis` | مبلغ تخفیف | `long?` | ✅ | ≤ `prdis` |
| `adis` | مبلغ بعد از تخفیف | `long?` | ✅ | = `prdis - dis` |
| `vra` | نرخ مالیات ارزش افزوده | `decimal?` | ✅ | ۰ / ۹ / ۱۰ |
| `vam` | مبلغ مالیات ارزش افزوده | `long?` | ✅ | = `adis × vra / 100` |
| `odt`, `odr`, `odam` | سایر مالیات/عوارض (موضوع/نرخ/مبلغ) | — | ⭕ | اگر `odam` باشد `odt` لازم است |
| `olt`, `olr`, `olam` | سایر وجوه قانونی | — | ⭕ | |
| `consfee`, `spro`, `bros`, `tcpbs` | اجرت ساخت/سود/حق‌العمل/جمع | — | ⭕ | الگوی طلا/حق‌العملکاری |
| `cop`, `vop` | سهم نقدی / سهم VAT از پرداخت | `long?` | شرطی | |
| `bsrn` | شناسه قرارداد حق‌العملکاری | `string` | ⭕ | |
| `tsstam` | مبلغ کل کالا/خدمت | `long?` | ✅ | = `adis + vam + odam + olam` |
| `nw`, `ssrv`, `sscv`, `cui`, `hs`, `vba`, `sovat` | وزن/ارزش/عیار/HS/پایه VAT/ماخذ | — | ⭕ | الگوهای خاص |

### ۵-۳) پرداخت‌ها (`PaymentItemDto`)

| کد | نام فارسی | نوع | الزام |
|----|-----------|-----|-------|
| `iinn` | شماره سوییچ پرداخت | `string` | شرطی (کارتی) |
| `acn` | شماره پذیرنده فروشگاهی | `string` | شرطی |
| `trmn` | شماره پایانه | `string` | شرطی |
| `trn` | شماره پیگیری | `string` | شرطی |
| `pcn` | شماره کارت پرداخت‌کننده | `string` | ⭕ |
| `pid` | کد ملی/فراگیر پرداخت‌کننده | `string` | ⭕ |
| `pdt` | تاریخ/زمان پرداخت | `long?` | ✅ (اگر پرداخت ثبت شود) |
| `pmt` | روش پرداخت | `int?` | ✅ (اگر پرداخت ثبت شود) |
| `pv` | مبلغ پرداختی | `long?` | ✅ |
| `vatpid`, `vatbid` | شناسه پرداخت/قبض (بورس) | `string` | ⭕ |

### ۵-۴) Extension

`ExtensionItemDto` در SDK **خالی** است (کلاس بدون پراپرتی) — برای فیلدهای توسعه‌ای آینده. ما آن را با `Dictionary<string, object?>` پوشش می‌دهیم و در payload فقط در صورت وجود مقدار ارسال می‌کنیم.

### ۵-۵) Enumها و ثابت‌های موردنیاز

| مفهوم | مقادیر در SDK/سند | وضعیت |
|-------|-------------------|-------|
| `inty` نوع صورتحساب | ۱ اصلی، ۲ اصلاحی، ۳ ابطالی، ۴ برگشت از فروش | نیازمند تأیید |
| `tob` نوع شخص خریدار | ۱ حقیقی، ۲ حقوقی | تأییدشده (خودتان هم فرمودید) |
| `setm` روش تسویه | ۱ نقدی، ۲ نسیه، ۳ نقدی/نسیه | نیازمند تأیید |
| `inp` الگوی صورتحساب | ۱..۱۳+ (فروش، ارزی، طلا، قرارداد، حمل…) | **جدول رسمی لازم است** |
| `ins` موضوع صورتحساب | ۱ فروش، ۲ برگشت از فروش، … | **جدول رسمی لازم است** |
| `pmt` روش پرداخت (بدنه) | Int (کد) | **جدول رسمی لازم است** |
| `PaymentMethod` (ثبت پرداخت) | `CHEQUE, BARTER, CASH, POS, INTERNET, CARD, TRANSFER, OTHER` | تأییدشده (رشته‌ای) |
| `RequestStatus` (ارسال/فیلتر استعلام) | `SUCCESS, FAILED, TIMEOUT` | تأییدشده |
| `status` نتیجه استعلام | `IN_PROGRESS, FAILED, SUCCESS, TIMEOUT, NOT_FOUND` | تأییدشده |
| `invoiceStatus` کارپوشه | `APPROVED, SYSTEMIC_APPROVED, REJECTED, IMPOSSIBLE_REACTION, AWAITING_REACTION, NO_NEED_REACTION, CANCELED` | تأییدشده |
| `article6Status` | `EXCEEDED, NOT_EXCEEDED` | تأییدشده |

---

## ۶) خطاها و رفتار SDK (نکات عملیاتی برای Wrapper)

1. **خطای HTTP:** `RestSharpHttpClient` اگر `!IsSuccessStatusCode` باشد، بدنه را به `ErrorResponseDto` پارس می‌کند؛ اگر پارس نشد `UnknownResponseException(statusCode, body)` و در غیر این صورت `TaxCollectionApiException` (**internal**).
2. **نگاشت خطا:** کدهای خطای مودیان (`ErrorModel.code` + `message`) باید به پیام فارسی قابل‌فهم + `MoadianErrorCode` در دیتابیس (Audit) نگاشت شوند → جدول `MoadianErrorCode` با seed اولیه و امکان افزودن سازمانی.
3. **Timeout:** SDK هیچ `CancellationToken` و retry ندارد و `HttpClient.Timeout` را ۱۰ دقیقه می‌گذارد → ما در لایه‌ی Core با `Task.WhenAny`/`WaitAsync(ct)` و Polly (Retry با backoff برای خطاهای گذرا) پوشش می‌دهیم.
4. **Nonce:** هر درخواست ۲ فراخوانی HTTP دارد؛ برای Batch و Jobها باید نرخ فراخوانی کنترل شود.
5. **Thread-safety:** `TaxApi`/`Encryptor` برای یک MemoryId ساخته می‌شوند و کش کلید داخلی دارند → یک نمونه per-MemoryId (Singleton سبک در `IMoadianClientFactory`) و نه per-request.
6. **Sync API:** به‌طور کامل ممنوع (`Thread.Sleep`/`.Result` فقط در نمونه‌های SDK است).
7. **پکیج‌ها:** `netstandard2.0` + `RestSharp 108`، `Newtonsoft.Json`، `Portable.BouncyCastle 1.9`، `JWT 10.0.2`، `jose-jwt 4.1`، `Pkcs11Interop`، `Microsoft.VisualStudio.Threading` → در .NET 8 بدون مشکل مصرف می‌شود ولی **هیچ‌کدام از این‌ها را مستقیماً در پروژه‌های خودمان استفاده نمی‌کنیم** (فقط از طریق Abstraction).
8. **رمزنگاری تصادفی کلید:** `EncryptionKeyRepository` کلید عمومی را تصادفی از لیست انتخاب و هر ساعت رفرش می‌کند → تست‌های واحد باید این رفتار را mock کنند (`Func<List<KeyModel>>`).

---

## ۷) تصمیم‌های معماری پیشنهادی برای مراحل بعد (خلاصه)

| موضوع | تصمیم پیشنهادی | دلیل (۲–۳ خط) |
|-------|----------------|----------------|
| لایه‌بندی SDK | `IMoadianGateway` (Core) با پیاده‌سازی `TaxCollectDataMoadianGateway` + DTOهای اختصاصی خودمان؛ SDK فقط در یک پوشه‌ی `Infrastructure/Moadian` دیده می‌شود | تست‌پذیری، قابلیت تعویض SDK/API مستقیم، جلوگیری از نشت `HeaderDto` به لایه‌های بالاتر |
| Mapping | **Mapster** | سبک‌تر، source-generator و بدون Reflection در زمان اجرا، سرعت بهتر و boilerplate کمتر از AutoMapper برای تعداد بالای DTO |
| حالت امضا | سه استراتژی قابل انتخاب per-مودی؛ پیش‌فرض PKCS#8+گواهی، «فقط کلید خصوصی» با برچسب آزمایشی | پوشش هر سه سناریوی واقعی مشتری، بدون قفل شدن به رفتار یک نسخه از SDK |
| ساخت payload | بسته‌بندی با `ISignatory`/`IEncryptor` و **Serializer اختصاصی خودمان** (ignore-null، بدون فیلد خالی) روی `ILowLevelTaxApi` | کنترل کامل روی فیلدهای ارسالی و پرهیز از خطاهای «فیلد خالی/غیرمجاز»؛ رمزنگاری همان SDK معتبر |
| تاریخ‌ها | `DateTimeOffset` (UTC) در Domain، نمایش شمسی در Client | جلوگیری از خطاهای `indatim` و اختلاف منطقه زمانی |
| اجرای ارسال | Outbox + BackgroundService (مرحله ۷) | جلوگیری از گم‌شدن فاکتور هنگام قطعی سامانه/شبکه |
| **Multi-tenancy** | یک دیتابیس مشترک، جداسازی با `TenantId` روی همه‌ی Entityهای سازمانی + Global Query Filter + Interceptor که `TenantId` را در SaveChanges ست می‌کند | هزینه‌ی نگه‌داری پایین (یک Migration برای همه)، جداسازی داده تضمین‌شده در سطح EF؛ تنانت از Claim توکن/API Key استخراج می‌شود، نه از ورودی کاربر |
| **API خارجی** | `ExternalInvoicesController` با Scheme مستقل `ApiKey`، Scope-based، Rate Limit جدا (پارتیشن بر ApiClientId) و DTO ورودی مستقل از UI | جداسازی کامل مسیر ماشین‌به‌ماشین از UI؛ امکان ابطال/محدودسازی هر کلید بدون اثر روی کاربران |
| **Idempotency** | `Invoice.ExternalId` + ایندکس یکتای `(TenantId, ExternalId)`؛ ارسال تکراری → بازگرداندن همان فاکتور با `IsIdempotentReplay=true` (نه خطای 500/دوباره‌ثبت) | جلوگیری از صورتحساب تکراری در سامانه مودیان؛ Retry سمت ERP بی‌خطر می‌شود |
| **کلید خصوصی** | AES-GCM + Envelope Encryption، فقط در RAM رمزگشایی، بدون نوشتن روی دیسک | الزام امنیتی شما؛ حذف ریسک سرقت فایل کلید و لاگ شدن آن |

---

## ۸) سؤالات باز و تصمیمات تأییدشده

| # | موضوع | نتیجه |
|---|-------|-------|
| ۱ | محل Solution | ✅ **`MoudianMiddleware/` در ریشه‌ی مخزن**، ساختار `MoudianMiddleware/{src,tests,docs}` |
| ۲ | آدرس محیط‌ها | 🟡 **تولید (تأییدشده از PDF):** `https://tp.tax.gov.ir/requestsmanager` — **Sandbox: خالی، «نیاز به تأیید».** کل PDF و سورس جست‌وجو شد؛ آدرس رسمی تست وجود ندارد. آدرس‌های `master.nta.local`، `localhost:8055` و `tp-nta.abriment.mohaymen.ir` در `TaxCollectData.Sample` سرورهای داخلی تیم SDK هستند و **seed نمی‌شوند**. در DB: `MoadianSetting.BaseUrl` برای محیط `Sandbox` خالی و اجباری‌الاصلاح از UI؛ اعتبارسنجی هنگام ذخیره، آدرس خالی را برای محیط Sandbox رد می‌کند. |
| ۳ | Enumهای `inp` / `ins` / `pmt` | ✅ خودم seed می‌کنم |
| ۴ | `RegisterPaymentRequest` | ✅ **در اسکوپ نسخه‌ی اول** (Module 5 → بخش پرداخت + Service و صفحه‌ی مربوطه) |
| ۵ | گواهی امضای Sandbox برای تست E2E | ⏳ باز — تا زمان تأمین گواهی، تست‌های امضا/بسته‌بندی با **گواهی self-signed** و Mock انجام می‌شود (ساختار تست طوری طراحی می‌شود که با جایگزینی گواهی واقعی، E2E هم اجرا شود). |

**پیامد تصمیم ۳ (مهم برای مرحله ۳):** چون جدول رسمی در دست نیست، مقادیر `inp`/`ins`/`pmt` به شکل **Enum در CoreLayer + جدول Lookup در DB (`InvoicePattern`, `InvoiceSubject`, `PaymentMethodCode`) با Seed اولیه** پیاده می‌شود؛ به این ترتیب اگر مقدار یا برچسب فارسی تغییر کند، فقط Seed/Dataset عوض می‌شود و کد دست نمی‌خورد. صحت مقادیر ارسالی پیش از ارسال با `FluentValidation` (بازه‌ی مجاز) کنترل می‌شود.

### ۸-۱) محدودیت‌های محیط توسعه (شفاف‌سازی)

| مورد | وضعیت |
|------|-------|
| `dotnet` SDK | ❌ در این محیط نصب نیست و راه نصب هم بسته است → **بیلد/تست را نمی‌توانم اجرا کنم**. کد را با دقت و بر اساس APIهای پایدار .NET 8 می‌نویسم و در README دستور بیلد/تست را می‌گذارم تا روی ماشین شما اجرا شود. |
| دسترسی به `nuget.org` | ❌ بسته است → نسخه‌های پکیج از حافظه/سند انتخاب و در `Directory.Packages.props` متمرکز می‌شوند؛ اگر restore اعتراض کرد، فقط همان یک فایل اصلاح می‌شود. |
| `github.com` | ✅ برای مطالعه‌ی مخزن مرجع |
| تست E2E با سامانه مودیان | ⏳ نیازمند آدرس Sandbox + گواهی (بند ۸، ردیف ۲ و ۵) |

---

## ۹) مرحله ۲ — تحویل‌شده

سند **`docs/02-solution-design-fa.md`** شامل: درخت کامل Solution تا سطح فایل، نمودار وابستگی و قواعد آن، مسئولیت هر فایل، بسته‌های NuGet، `Directory.Build.props` و `Directory.Packages.props`، قراردادهای نام‌گذاری و الگوهای تکرارشونده، و نقشه‌ی «ماژول ← فایل‌ها».

فایل‌های پیکربندی واقعی که در همین مرحله ساخته شدند: `Directory.Build.props`، `Directory.Packages.props`، `.editorconfig`، `.gitignore`. ساخت `MoudianMiddleware.sln` و چهار `.csproj` در ابتدای مرحله ۳ همراه با اولین کد (لایه‌ی داده) انجام می‌شود تا از ابتدا پروژه‌ی نیمه‌ساخته و بدون EntryPoint نداشته باشیم.
