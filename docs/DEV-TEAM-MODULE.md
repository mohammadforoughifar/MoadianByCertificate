# ماژول مدیریت تیم نرم‌افزار (DevTeam)

> مسیر منو: **میز کار توسعه**  
> پیشوند API: `/api/dev-team`  
> مجوز RBAC: ماژول `DevTeam`

## مشکل

مدیر تیم نمی‌داند:

- هر نفر الان روی چه تسکی کار می‌کند
- روی هر ماژول (HR، Office، فروش، …) چه تغییری در جریان است
- هر کار در چه مرحله‌ای است
- کجا خطا / وارنینگ خورده
- زمان‌بندی و تخمین در برابر واقعیت چگونه است
- کدام کامیت/شاخه به کدام تسک وصل است

## پاسخ

یک ماژول داخل همین پلتفرم (نه ابزار خارجی) با این قابلیت‌ها:

| بخش | کاربرد |
|---|---|
| **برد کانبان** | وضعیت‌های قابل‌تنظیم + کشیدن ذهنی (با دکمه جابجایی) |
| **تسک** | عنوان، ماژول، مسئول، اولویت، نوع، اسپرینت، مهلت، تخمین/واقعی ساعت |
| **مشکلات** | ثبت Error / Warning / Info روی ماژول یا تسک |
| **Changelog ماژول** | چه چیزی در هر ماژول عوض شد + SHA کامیت |
| **اتصال گیت** | لینک دستی کامیت/شاخه/PR به تسک (وب‌هوک بعدی) |
| **اسپرینت** | بازه زمانی با هدف و وضعیت |
| **داشبورد تیم** | بار هر نفر، تسک‌های معوق، مشکلات باز، پیشرفت اسپرینت |

## مدل داده (خلاصه)

```
DtWorkflowStatus     وضعیت‌های قابل‌تعریف کاربر
DtProductModule      کاتالوگ ماژول‌های نرم‌افزار (HR, Office, …)
DtSprint             بازه زمانی / اسپرینت
DtTask               تسک اصلی
DtTaskComment        گفتگو روی تسک
DtTaskActivity       لاگ اقدامات
DtTaskGitLink        اتصال کامیت/شاخه/PR
DtProblem            خطا / وارنینگ / مشکل
DtModuleChange       changelog هر ماژول
DtTimeEntry          ثبت زمان کار (اختیاری در MVP)
```

## وضعیت‌های پیش‌فرض (قابل ویرایش)

| کلید | نام | رنگ | معنی |
|---|---|---|---|
| `backlog` | بک‌لاگ | خاکستری | هنوز شروع نشده |
| `todo` | آماده انجام | آبی | صف انجام |
| `doing` | در حال انجام | بنفش | فعال |
| `review` | بازبینی / تست | نارنجی | در حال بررسی |
| `blocked` | مسدود | قرمز | گیر کرده |
| `done` | انجام‌شده | سبز | تمام |

مدیر می‌تواند وضعیت اضافه/حذف/مرتب کند (`IsInitial` / `IsDone` / `IsBlocked`).

## مجوزها (RBAC)

| اکشن | معنی |
|---|---|
| `View` | دیدن ماژول و منو |
| `Create` | ساخت تسک / مشکل / changelog |
| `Update` | ویرایش و جابجایی وضعیت |
| `Delete` | حذف |
| `Manage` | تنظیم وضعیت‌ها، ماژول‌ها، اسپرینت‌ها |
| `Assign` | تخصیص به دیگران |

## صفحات UI

- `/dev-team` — برد + لیست + فیلتر
- تب‌ها: کانبان | لیست | مشکلات | ماژول‌ها | اسپرینت | داشبورد | تنظیمات

## فازبندی

### فاز ۱ — MVP (همین نسخه)

- Entity + Schema خودتعمیر + API + UI کامل
- وضعیت‌های قابل‌تنظیم
- تسک با ماژول/مسئول/اولویت/اسپرینت
- مشکلات Error/Warning
- changelog + لینک گیت دستی
- داشبورد خلاصه تیم

### فاز ۲ — پیاده‌شده

- ✅ وب‌هوک GitHub/GitLab (`/api/dev-team/hooks/github|gitlab`)
  - ارجاع در پیام کامیت: `DT-1405-0001` / `#DT-…` / `task:12`
  - ماژول اختیاری: `mod:Office`
  - خروجی: changelog + لینک گیت روی تسک + Problem در صورت fix/error
- ✅ ثبت زمان دستی روی تسک (TimeEntry)
- ✅ ساخت/اتصال/قطع دستور کار از تسک (`SourceModule=DevTeam`)
- ✅ اعلان SignalR + نوتیفیکیشن هنگام تخصیص و ساخت تسک
- ✅ ساب‌تسک و وابستگی (Schema V3)

### ساب‌تسک و وابستگی

| مفهوم | مدل | رفتار |
|---|---|---|
| **ساب‌تسک** | `DtTasks.ParentTaskId` | حداکثر ۳ سطح؛ در کانبان فقط ریشه‌ها؛ پیشرفت `done/total` روی والد |
| **تبدیل** | `POST .../make-subtask` | تسک موجود → فرزند والد (جلوگیری از حلقه) |
| **ارتقا** | `POST .../promote` | فرزند → ریشه |
| **وابستگی Blocks** | `DtTaskDependencies` | تا تمام‌شدن dependsOn، بستن تسک ممنوع |
| **RelatesTo** | همان جدول | لینک نرم بدون قفل |

API:
- `POST /api/dev-team/tasks/{id}/subtasks`
- `POST /api/dev-team/tasks/{id}/make-subtask` body `{ parentTaskId }`
- `POST /api/dev-team/tasks/{id}/promote`
- `POST /api/dev-team/tasks/{id}/dependencies` body `{ dependsOnTaskId, kind }`
- `DELETE /api/dev-team/dependencies/{id}`
- `GET /api/dev-team/tasks/search?q=`

### فاز ۳ — پیاده‌شده

- ✅ برد drag-and-drop واقعی (HTML5 DnD + دکمه جابجایی)
- ✅ گزارش burndown (`GET /api/dev-team/burndown?sprintId=` + تب UI)
- ✅ یکپارچگی CI → Problem  
  - `POST /api/dev-team/hooks/ci` (generic)  
  - `POST /api/dev-team/hooks/github-ci` (workflow_run / check_suite)  
  - `POST /api/dev-team/hooks/gitlab-ci` (Pipeline Hook)
- ✅ تایمر زنده ثبت زمان (`…/timer/start|stop`) — Schema V4: `TimerStartedAt`
- ✅ مرتب‌سازی drag ساب‌تسک‌ها (`POST …/subtasks/reorder`)

### مسیر C — WIP + Checklist (پیاده‌شده)

- ✅ `WipLimit` روی وضعیت workflow (Schema V5) — جلوگیری از پر شدن ستون
- ✅ چک‌لیست تسک: add/toggle/delete/reorder + نوار پیشرفت
- ✅ `POST /problems/{id}/create-task` — تسک از مشکل + ۳ آیتم چک‌لیست پیش‌فرض

### مسیر B — UX (پیاده‌شده)

- ✅ فیلتر/تب پایدار در URL: `?tab=&q=&module=&sprint=&assignee=&type=&mine=1&open=`
- ✅ نوار تایمر سراسری بالای صفحه + `GET /api/dev-team/my-timer`
- ✅ توقف تایمر از نوار بدون باز بودن مودال

### مسیر A — بهره‌برداری (پیاده‌شده)

- ✅ `GET /api/dev-team/integration-status` — وضعیت secret، URL کامل endpointها، RBAC
- ✅ تب **تنظیمات** — کارت یکپارچه‌سازی + کپی URL/curl
- ✅ نقش RBAC **DevDeveloper** (View/Create/Update/Assign) + تضمین پرمیشن Admin
- ✅ نمونه‌ها: `inventory/tools/dev-team/` (GitHub Actions، GitLab CI، smoke-hooks.sh)

## تمایز با «دستور کار»

| | دستور کار | میز کار توسعه |
|---|---|---|
| مخاطب | همه سازمان | تیم نرم‌افزار |
| تمرکز | محول‌کاری اداری/عملیاتی | تحویل فیچر/باگ روی ماژول‌ها |
| گیت / changelog | ندارد | دارد |
| وضعیت | Open/Closed (+کانبان ساده) | workflow کاملاً قابل‌تعریف |

هر دو می‌توانند هم‌زیست باشند؛ بعداً می‌توان از تسک DevTeam یک WorkOrder ساخت.
