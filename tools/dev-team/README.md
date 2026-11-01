# DevTeam — وب‌هوک گیت و CI (مسیر A)

## پیش‌نیاز سرور

```bash
export DEVTEAM_GIT_WEBHOOK_SECRET='یک-رمز-قوی'
# یا در appsettings: DevTeam:GitWebhookSecret
```

`DevTeam:GitWebhookEnabled` پیش‌فرض `true` است.

## URLها (جایگزین `https://YOUR-HOST`)

| کاربرد | متد | مسیر |
|--------|-----|------|
| GitHub push | POST | `/api/dev-team/hooks/github` |
| GitLab push | POST | `/api/dev-team/hooks/gitlab` |
| CI عمومی | POST | `/api/dev-team/hooks/ci` |
| GitHub Actions | POST | `/api/dev-team/hooks/github-ci` |
| GitLab Pipeline | POST | `/api/dev-team/hooks/gitlab-ci` |
| سلامت | GET | `/api/dev-team/hooks/health` |

احراز: هدر `X-DevTeam-Token: <secret>` یا امضای مخصوص هر provider.

## کامیت

```
fix(office): pagination DT-1405-0003 mod:Office
# یا: task:12
```

## فایل‌ها

- `github-actions-ci.yml` — نمونه workflow که در failure به `hooks/ci` می‌زند
- `gitlab-ci-snippet.yml` — نمونه job گیت‌لب
- `smoke-hooks.sh` — تست health + CI dry failure

## RBAC

- پرمیشن‌های ماژول `DevTeam`: View, Create, Update, Delete, Manage, Assign
- نقش **DevDeveloper**: View/Create/Update/Assign
- **Admin**: همهٔ پرمیشن‌های DevTeam

در UI: میز کار توسعه ← تنظیمات ← کارت «یکپارچه‌سازی».

## ساده‌سازی رابط کاربری (به‌روزرسانی اخیر)

مودال تسک قبلاً ۱۸ بخش در یک پنجره بود و استفاده را سخت می‌کرد. الان:

| تغییر | نتیجه |
|---|---|
| **ثبت سریع** | یک سطر بالای برد: عنوان + نوع + اولویت + مسئول. Enter = ثبت. بدون باز شدن هیچ مودالی. |
| **دکمهٔ + روی هر ستون** | تسک تازه مستقیم در همان ستون ساخته می‌شود. |
| **آکاردئون در مودال** | فقط ۴ فیلد همیشه باز است (عنوان، وضعیت، نوع، اولویت، مسئول). بقیه در بخش‌های تاشو: «شرح و جزئیات»، «ساب‌تسک‌ها»، «وابستگی‌ها»، «دستور کار»، «چک‌لیست»، «گیت و زمان»، «گفتگو و فعالیت». هر بخش شمارنده دارد (مثلاً ساب‌تسک ۲/۵). |
| **رنگ با انتخابگر** | در تنظیمات، رنگ ستون/ماژول با `<input type="color">` انتخاب می‌شود (کد hex هم قابل تایپ است). |
| **مهلت با تقویم** | به‌جای تایپ دستی `1405/01/15`، از DatePicker استفاده می‌شود. |
| **راهنمای برد خالی** | اگر تسکی نباشد، کارت راهنما نشان داده می‌شود. اگر ستونی هم نباشد، دکمهٔ «ساخت ستون‌های آماده» ظاهر می‌شود (بک‌لاگ / در حال انجام / بازبینی / مسدود / انجام‌شده). |

> نکته: سرور از ابتدا ۶ ستون پیش‌فرض (`backlog, todo, doing, review, blocked, done`) را seed می‌کند؛
> پس راهنمای «ستونی نیست» فقط وقتی ظاهر می‌شود که همهٔ ستون‌ها حذف شده باشند.

کلیدهای میان‌بر: Enter در عنوان مودال = ذخیره، Esc = بستن، Enter در کادر نظر = ارسال.
