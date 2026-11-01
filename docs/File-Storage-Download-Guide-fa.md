# راهنمای استاندارد ذخیره و دانلود فایل‌ها

## هدف

این سند قرارداد واحد ذخیره و دانلود فایل‌ها را برای توسعه‌دهندگان پروژه تعریف می‌کند. اصل استاندارد این است:

```text
فایل فیزیکی روی سرور API
متادیتا و مسیر نسبی در دیتابیس
دانلود فقط از طریق endpoint احراز هویت‌شده API
```

فایل نباید روی سیستم کاربر، داخل پوشه سورس یا فقط در دیتابیس ذخیره شود.

## قرارداد استاندارد

برای ماژول‌هایی که از `AppAttachments` استفاده می‌کنند:

```text
جدول: AppAttachments
Module: نام ثابت ماژول
RefId: شناسه رکورد مالک فایل
FilePath: مسیر نسبی زیر wwwroot
```

الگوی مسیر فیزیکی:

```text
wwwroot/uploads/{Module}/{RefId}/{Guid}_{FileName}
```

نمونه:

```text
wwwroot/uploads/WorkOrders/125/3b9f_report.pdf
```

مقدار `FilePath` در دیتابیس:

```text
uploads/WorkOrders/125/3b9f_report.pdf
```

`Data` برای فایل‌های جدید باید خالی باشد؛ فایل از دیسک خوانده می‌شود. پشتیبانی از `Data` فقط برای رکوردهای قدیمی نگه داشته شده است.

## دستور کار

```text
Module: WorkOrders
جدول: AppAttachments
مسیر: wwwroot/uploads/WorkOrders/{WorkOrderId}/{Guid}_{FileName}
```

endpointهای استاندارد:

```http
POST /api/attachments/WorkOrders/{WorkOrderId}
GET  /api/attachments/WorkOrders/{WorkOrderId}
GET  /api/attachments/download/{AttachmentId}
```

تمام مسیرهای شمارش، حذف، گزارش‌ساز، آپلود و دانلود دستور کار به `AppAttachments` منتقل شده‌اند. جدول قدیمی `WorkOrderAttachments` برای فایل‌های جدید استفاده نمی‌شود.

## آرشیو اسناد

```text
Module: DocVersion
جدول: AppAttachments
مسیر: wwwroot/uploads/DocVersion/{DocumentVersionId}/{Guid}_{FileName}
```

آپلود از مسیر عمومی پیوست‌ها انجام می‌شود، اما کنترل دسترسی، محرمانگی، تأیید مجدد رمز و واترمارک در `AttachmentsController` و سرویس آرشیو اعمال می‌شود. فایل مستقیماً از طریق Static Files منتشر نمی‌شود.

## اتوماسیون اداری

### نامه داخلی

```text
Module: InnerLetters
جدول: AppAttachments
مسیر جدید: wwwroot/uploads/InnerLetters/{LetterId}/{Guid}_{FileName}
```

پیش‌نویس:

```text
wwwroot/uploads/InnerLetters/pishnevis/{PishnevisId}/{Guid}_{FileName}
```

### نامه وارده

```text
Module: IncomingLetters
جدول: AppAttachments
مسیر: wwwroot/uploads/IncomingLetters/{LetterId}/{Guid}_{FileName}
```

### نامه صادره

```text
Module: OutgoingLetters
جدول: AppAttachments
مسیر: wwwroot/uploads/OutgoingLetters/{LetterId}/{Guid}_{FileName}
```

پیش‌نویس نامه صادره:

```text
wwwroot/uploads/OutgoingLetters/pishnevis/{PishnevisId}/{Guid}_{FileName}
```

## درخواست‌های واحد فناوری

```text
Module: ItRequests
جدول: AppAttachments
مسیر: wwwroot/uploads/ItRequests/{RequestId}/{Guid}_{FileName}
```

## الگوی پیاده‌سازی API

آپلود باید با `FileStore` انجام شود:

```csharp
await using var stream = file.OpenReadStream();
var relativePath = await _store.SaveAsync(
    "WorkOrders",
    workOrderId,
    stream,
    file.FileName);

_db.AppAttachments.Add(new AppAttachment
{
    Module = "WorkOrders",
    RefId = workOrderId,
    FileName = Path.GetFileName(file.FileName),
    ContentType = string.IsNullOrWhiteSpace(file.ContentType)
        ? "application/octet-stream"
        : file.ContentType,
    FilePath = relativePath,
    Data = Array.Empty<byte>(),
    UploaderUserId = currentUserId,
    UploaderName = currentUserName
});

await _db.SaveChangesAsync();
```

دانلود:

```csharp
var attachment = await _db.AppAttachments
    .AsNoTracking()
    .FirstOrDefaultAsync(x =>
        x.Id == attachmentId &&
        x.Module == "WorkOrders");

if (attachment == null)
    return NotFound();

if (!await CanAccessAsync(attachment.RefId))
    return Forbid();

var bytes = _store.ReadBytes(attachment.FilePath)
    ?? (attachment.Data is { Length: > 0 } ? attachment.Data : null);

if (bytes == null)
    return NotFound(new { message = "فایل روی سرور پیدا نشد." });

return File(bytes, attachment.ContentType, attachment.FileName);
```

## الزامات امنیتی

- مسیر فیزیکی فایل نباید مستقیماً از کلاینت دریافت شود.
- مسیر فایل نباید بدون کنترل دسترسی به‌صورت URL عمومی ارائه شود.
- endpoint دانلود باید احراز هویت و مجوز مالک رکورد را بررسی کند.
- از `Path.GetFileName` برای نام اصلی فایل استفاده شود.
- نام فیزیکی فایل باید GUID داشته باشد.
- فایل خارج از `wwwroot/uploads` یا در فضای اشتراکی پایدار ذخیره شود؛ در صورت استفاده از `wwwroot`، Static Files نباید مسیرهای خصوصی را بدون کنترل دسترسی منتشر کند.
- توکن JWT نباید داخل `FilePath` یا لاگ ذخیره شود.
- سقف حجم، نوع محتوا و نام فایل باید در API اعتبارسنجی شود.

## انتقال و استقرار Publish

پوشه زیر بخشی از داده‌های عملیاتی برنامه است و نباید با هر Publish حذف شود:

```text
wwwroot/uploads/
```

در استقرار چند سروری، همه نمونه‌های API باید به یک فضای ذخیره‌سازی مشترک دسترسی داشته باشند. انتقال فقط دیتابیس کافی نیست؛ فایل‌های فیزیکی نیز باید منتقل شوند.

## فایل‌های قدیمی

در این مرحله مهاجرت فیزیکی فایل‌های قدیمی انجام نمی‌شود. دانلود باید ابتدا `FilePath` فعلی را بخواند و در صورت نبود فایل، مسیرهای legacy مجاز را پشتیبانی کند. قبل از حذف مسیر قدیمی:

1. از `AppAttachments` خروجی `Module` و `FilePath` تهیه شود.
2. وجود فیزیکی فایل‌ها بررسی شود.
3. فایل‌ها به مسیر جدید منتقل شوند.
4. مقدار `FilePath` با تراکنش اصلاح شود.
5. دانلود و مجوزها تست شوند.
6. پس از تأیید، مسیر قدیمی حذف شود.

## ماژول‌هایی که ساختار اختصاصی دارند

برخی بخش‌ها به‌دلیل ماهیت امنیتی یا دامنه‌ای خود جدول اختصاصی دارند و نباید بدون مهاجرت جداگانه به `AppAttachments` تبدیل شوند:

- پیام‌رسان خصوصی: `ChatAttachments` و سرویس `ChatAttachmentService`؛ فایل‌ها عمداً با endpoint خصوصی و کنترل عضویت گفتگو سرو می‌شوند.
- ایمیل: موجودیت‌های اختصاصی پیوست ایمیل و مسیر سازگار با پیوست‌های دریافتی/ارسالی.
- اسناد منابع انسانی: موجودیت‌های اختصاصی مدارک و تصاویر کارکنان.
- امضای کاربران: مسیر اختصاصی `UserSignatures`.

این ماژول‌ها از نظر قرارداد امنیتی باید همان اصل را رعایت کنند: ذخیره روی سرور، مسیر نسبی در دیتابیس و دانلود از endpoint کنترل‌شده. یکسان‌سازی جدول آن‌ها نیازمند migration مستقل و نباید با تغییر نام پوشه انجام شود.

## بررسی خطاهای رایج

### رکورد دیتابیس وجود دارد، فایل دانلود نمی‌شود

`FilePath` را از دیتابیس بخوانید و همان مسیر را نسبت به ریشه `wwwroot` بررسی کنید.

### خطای 403

فایل وجود دارد، اما کاربر مجوز مشاهده یا دانلود ندارد.

### خطای 404

رکورد وجود ندارد یا فایل فیزیکی در مسیر ثبت‌شده موجود نیست.

### دانلود روی یک سرور و عدم دانلود روی سرور دیگر

پوشه `wwwroot/uploads` بین دو سرور مشترک یا منتقل نشده است.

## کوئری بررسی پیوست‌های یک ماژول

```sql
SELECT
    Id,
    Module,
    RefId,
    FileName,
    FilePath,
    ContentType,
    UploadedAt,
    DATALENGTH(Data) AS DataSize
FROM AppAttachments
WHERE Module = 'WorkOrders'
ORDER BY Id DESC;
```

## قانون نهایی توسعه

هر قابلیت جدیدی که فایل آپلود می‌کند باید قبل از پیاده‌سازی این موارد را مشخص کند:

```text
Module ثابت
جدول متادیتا
RefId مالک فایل
پوشه فیزیکی
endpoint آپلود
endpoint لیست
endpoint دانلود
قواعد دسترسی
سیاست نگهداری و پشتیبان‌گیری
```

برای ماژول‌های عمومی، انتخاب پیش‌فرض باید این باشد:

```text
AppAttachments + FileStore + wwwroot/uploads/{Module}/{RefId}/
```
