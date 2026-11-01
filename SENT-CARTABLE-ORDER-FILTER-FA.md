# نامهٔ «گم‌شده» در کارتابل ارسالی — تحلیل و اصلاح

تاریخ: ۱۴۰۵/۰۸/۰۵ (۲۰۲۶-۱۰-۲۷)

## گزارش کاربر
برای یک کاربر (شناسه ۴، `430004`) بخشی از نامه‌های داخلی در کارتابل **ارسالی** دیده نمی‌شد،
اما با جستجوی عنوان همان نامه پیدا می‌شد.

## عیب‌یابی
اسکریپت `diagnose-inner-letter-missing-FA.sql` اجرا شد. خروجی:

| ساخته‌شده | حذف‌شده | منبع‌حذف‌شده | بایگانی‌شده | قابل‌نمایش |
|---|---|---|---|---|
| ۲۰۱ | ۰ | ۰ | ۰ | ۲۰۱ |

یعنی **هیچ نامه‌ای گم نشده بود**؛ مشکل نمایش/صفحه‌بندی بود. دو علت ریشه‌ای:

### علت ۱ — ترتیب «بریدگی صفحه» با ترتیب «نمایش» یکی نبود
`InnerLetterService.GetSentAsync` صفحه‌ها را با `OrderByDescending(l => l.Id)` می‌برید،
ولی رابط همان صفحه را با `OrderByDescending(x => x.Date)` می‌چید. در داده‌ای که شناسه و
تاریخ هم‌جهت نیستند (دادهٔ واردشده/مهاجرت‌شده) این دو از هم جدا می‌شوند. نمونهٔ واقعی:

| نامه | تاریخ ثبت | رتبهٔ شناسه | رتبهٔ تاریخ | صفحهٔ واقعی | صفحهٔ انتظار |
|---|---|---|---|---|---|
| 23558 | 2026-10-10 | ۵۲ | ۸ | ۴ | ۱ |
| 22274 | 2026-04-28 | ۱۵۴ | ۶۳ | ۱۱ | ۵ |
| 23600 | 2025-04-27 | ۴۵ | ۱۹۵ | ۳ | ۱۳ |

یعنی تازه‌ترین نامه‌های کاربر در صفحه‌های ۴ تا ۱۱ افتاده بودند و قدیمی‌ترین‌ها در صفحه‌های اول.
جستجو اما روی **کل** ردیف‌ها فیلتر می‌زند؛ پس همان نامه را در یک صفحه نشان می‌داد.

### علت ۲ — چیپ‌های فیلتر فقط صفحهٔ جاری را می‌فیلتر کردند
`LetterCartable.razor` فیلترهای «نشان‌شده / مهلت‌دار / پیوست‌دار» را روی آرایهٔ ۱۵ ردیفی
صفحهٔ جاری اعمال می‌کرد؛ اگر کاربر یکی از این چیپ‌ها را روشن می‌گذاشت، ۱۸۶ نامهٔ دیگر بی‌صدا
ناپدید می‌شدند. «مهلت‌دار» در «ارسالی» هم همیشه خالی بود چون DTO نامهٔ ارسالی `MohlatPasokh` ندارد.

### علت ۳ — شمارنده‌ها گمراه‌کننده بودند
`SentTotal` نامه‌های بایگانی‌شده/منبع‌حذف‌شده را هم می‌شمرد و چیپ «همه» در «ارسالی» به‌جای
تعداد کل، تعداد صفحهٔ جاری (۱۵) را نشان می‌داد.

## اصلاحات

### ۱) ترتیب صفحه‌بندی = ترتیب نمایش (هر سه نوع نامه)
| سرویس | متد | ترتیب جدید |
|---|---|---|
| `InnerLetterService` | `GetInboxAsync` / `GetArchiveAsync` | `Date DESC, ErjaId DESC` |
| `InnerLetterService` | `GetSentAsync` | `DateSabt DESC, Id DESC` |
| `IncomingLetterService` | `GetInboxAsync` | `Date DESC, ErjaId DESC` |
| `IncomingLetterService` | `GetSentAsync` | `Date DESC, Id DESC` |
| `OutgoingLetterService` | `GetSentAsync` | `DateSabt DESC, Id DESC` |
| `OutgoingLetterService` | `GetArchiveAsync` | `Date DESC, ErjaId DESC` |
| `OutgoingLetterService` | `GetInboxAsync` | از قبل `keys` را با `Date, LetterId` می‌برید — بدون تغییر |

کلاینت هم با همان کلید ثانویه (`LetterId`) مرتب می‌کند تا ترتیب پایدار بماند.

### ۲) فیلترهای چیپ سمت سرور
پارامترهای تازهٔ اختیاری (در انتهای امضا، برای سازگاری با فراخوان‌های موجود):

| endpoint | پارامترها |
|---|---|
| `GET api/letters/inbox` | `starredOnly`, `attachOnly`, `deadlineOnly` (+ `unreadOnly` قبلی) |
| `GET api/letters/sent` | `starredOnly`, `attachOnly` |
| `GET api/outgoing-letters/inbox` | `starredOnly`, `attachOnly` |
| `GET api/outgoing-letters/sent` | `starredOnly`, `attachOnly` |
| `GET api/incoming-letters/inbox` | `attachOnly` (+ `starredOnly` قبلی) |
| `GET api/incoming-letters/sent` | `starredOnly`, `attachOnly` |

- پیوست با `AppAttachments(Module, RefId)` بررسی می‌شود (`InnerLetters` / `OutgoingLetters` / `IncomingLetters`).
- در کارتابل‌ها، `SetFilter` برای همهٔ چیپ‌ها `ReloadAsync` می‌زند و فیلتر سمت کلاینت حذف شد.
  تنها استثنا **«دستورکارشده»** است، چون فهرست شناسه‌هایش از ماژول دستور کار می‌آید.
- چیپ «مهلت‌دار» در پوشهٔ «ارسالی» نامهٔ داخلی پنهان شد (فیلد مهلت در DTO ارسالی وجود ندارد).

### ۳) شمارنده‌ها
- `SentTotal` اکنون همان شرط فهرست ارسالی را دارد (بدون بایگانی‌شده و منبع‌حذف‌شده).
- چیپ «همه» در «ارسالی» تعداد کل سرور (`totalServerCount`) را نشان می‌دهد، نه ۱۵ ردیف صفحه.

## راهنماها
- `Pages/Office/Letters/InnerLetterGuide.razor` → بخش «جستجو، فیلتر و مرتب‌سازی»: یادداشت
  «فیلترها روی همهٔ نامه‌ها اثر می‌گذارند».
- `Pages/Office/Guides/OutgoingLetterGuide.razor` و `IncomingLetterGuide.razor` → همان توضیح در بخش جستجو/فیلتر.

## عیب‌یابی آینده
`diagnose-inner-letter-missing-FA.sql` را با `@UserId` و بخشی از عنوان اجرا کنید؛ ستون `Reason`
برای هر نامه کد دلیل چاپ می‌کند (حذف نرم، منبع گم‌شده، بایگانی، فرستندهٔ دیگر، گیرنده‌بودن، ترتیب).
