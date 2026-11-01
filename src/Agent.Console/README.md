# ایجنت Inventory — ‏Agent.Console

ایجنت جمع‌آوری مشخصات سخت‌افزار کلاینت‌ها و ارسال خودکار به API مدیریت سخت‌افزار (`POST api/SystemInfo`).

روی هر سیستم (ویندوز / لینوکس) اجرا می‌شود، همه‌ی قطعات را می‌خواند و به سرور ارسال می‌کند.
چندتایی بودن قطعات کامل پشتیبانی می‌شود: **چند CPU، چند ماژول رم، چند هارد، چند گرافیک، چند مانیتور**.

> همین دستور داخل صفحه‌ی «شناسنامه سیستم» هم آمده است:
> `dotnet run --project src/Agent.Console`

---

## چه اطلاعاتی ارسال می‌شود؟

| قطعه | جزئیات ارسالی |
|---|---|
| مادربرد | سازنده + مدل برد، سریال برد، مدل کامپیوتر |
| CPU (هر سوکت جدا) | نام، تعداد هسته، تعداد ترد، فرکانس (GHz) |
| رم (هر ماژول جدا) | اسلات، ظرفیت (GB)، نوع (DDR3/DDR4/DDR5/...)، سرعت (MHz)، سازنده، پارت‌نامبر، سریال |
| هارد (هر دیسک جدا) | مدل، ظرفیت (GB)، اینترفیس (SATA/NVMe/...)، سریال، وضعیت S.M.A.R.T |
| گرافیک (هر کارت جدا) | نام، رزولوشن فعلی |
| مانیتور (هر مانیتور جدا) | نام (از EDID)، رزولوشن، سریال، اصلی/فرعی |
| شبکه | نام، نوع (Ethernet/WiFi/...)، MAC، IPv4، Gateway |
| درایوها | حرف درایو، لیبل، حجم کل/مصرفی (GB) |
| سیستم‌عامل | نام کامل OS |

شناسه‌ی هر سیستم (`AgentId`) پایدار است: روی ویندوز از UUID مادربرد، روی لینوکس از machine-id —
پس با اجرای مجدد، همان رکورد به‌روز می‌شود (نه رکورد جدید).

---

## اجرای سریع (روی سیستم کلاینت)

### روش ۱: با دات‌نت (نیاز به ‎.NET 8 SDK‎ روی کلاینت)

```powershell
# ویندوز (PowerShell) — داخل پوشه‌ی inventory:
dotnet run --project src/Agent.Console -- run -s http://192.168.1.10:5000
```

### روش ۲: فایل اجرایی آماده (پیشنهادی — بدون نیاز به نصب دات‌نت روی کلاینت)

روی سیستم خودت یک‌بار بیلد بگیر:

```powershell
# خروجی: یک فایل InventoryAgent.exe تکی (~30MB) در پوشه‌ی publish-win
dotnet publish src/Agent.Console -c Release -r win-x64 --self-contained true `
  /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true `
  -o publish-win
```

بعد پوشه‌ی `publish-win` را روی هر کلاینت کپی کن و اجرا کن:

```powershell
.\InventoryAgent.exe run -s http://192.168.1.10:5000
```

> اگر روی کلاینت‌ها ‎.NET 8 Runtime‎ نصب است، نسخه‌ی سبک (~۲۰۰KB) کافی است:
> `dotnet publish src/Agent.Console -c Release -r win-x64 --self-contained false /p:PublishSingleFile=true -o publish-win-fd`

---

## دستورها

| دستور | کاربرد |
|---|---|
| `run` (پیش‌فرض) | یک‌بار: ارسال گزارش + دریافت و اجرای دستورهای در انتظار |
| `watch` | حالت نگهبان: تکرار دوره‌ای (`-i 30` یعنی هر ۳۰ دقیقه) |
| `dry-run` | فقط نمایش JSON جمع‌آوری‌شده، بدون ارسال (`--out hw.json` برای ذخیره) |
| `commands` | فقط دریافت و اجرای دستورهای در انتظار (Reboot/Shutdown/Lock) |
| `init` | ساخت فایل تنظیمات `agent.json` |
| `version` | نمایش نسخه |

### سوییچ‌ها

```
-s, --server URL     آدرس API (مثل http://192.168.1.10:5000)
-i, --interval MIN   فاصله‌ی watch به دقیقه (پیش‌فرض ۶۰)
    --agent-id ID    شناسه‌ی دستی سیستم (مثل PC-101) — اختیاری
    --token T        توکن JWT — اختیاری (فعلاً لازم نیست)
    --username U --password P   گرفتن خودکار توکن — اختیاری
    --config PATH    مسیر فایل تنظیمات (پیش‌فرض agent.json کنار exe)
    --out FILE       ذخیره‌ی خروجی dry-run
    --insecure       نادیده گرفتن خطای گواهی HTTPS (فقط شبکه‌ی داخلی)
```

### مثال‌ها

```powershell
# ارسال یک‌باره
.\InventoryAgent.exe run -s http://192.168.1.10:5000

# نگهبان هر ۳۰ دقیقه
.\InventoryAgent.exe watch -s http://192.168.1.10:5000 -i 30

# تست بدون ارسال
.\InventoryAgent.exe dry-run --out hw.json

# شناسه‌ی دستی برای یک سیستم خاص
.\InventoryAgent.exe run -s http://192.168.1.10:5000 --agent-id PC-101
```

---

## اجرای خودکار روی کلاینت‌ها

### ویندوز — Task Scheduler (پیشنهادی)

```powershell
# هر ۶۰ دقیقه + هنگام روشن شدن سیستم
$exe = "C:\InventoryAgent\InventoryAgent.exe"
schtasks /create /tn "InventoryAgent" /tr "$exe run -s http://192.168.1.10:5000" `
  /sc minute /mo 60 /ru SYSTEM /rl highest /f
schtasks /create /tn "InventoryAgentBoot" /tr "$exe run -s http://192.168.1.10:5000" `
  /sc onstart /ru SYSTEM /rl highest /f
```

> اجرای با کاربر SYSTEM باعث می‌شود سریال‌ها و وضعیت S.M.A.R.T کامل خوانده شود.

### لینوکس — systemd

```ini
# /etc/systemd/system/inventory-agent.service
[Unit]
Description=Inventory Hardware Agent
After=network-online.target
Wants=network-online.target

[Service]
Type=oneshot
ExecStart=/opt/inventory-agent/InventoryAgent run -s http://192.168.1.10:5000
```

```ini
# /etc/systemd/system/inventory-agent.timer — هر ۶۰ دقیقه
[Unit]
Description=Run Inventory Agent hourly

[Timer]
OnBootSec=2min
OnUnitActiveSec=60min

[Install]
WantedBy=timers.target
```

```bash
sudo systemctl enable --now inventory-agent.timer
```

---

## دستورهای از راه دور

از صفحه‌ی «شناسنامه سیستم» می‌توان برای هر سیستم دستور **ری‌استارت / خاموش / قفل** صادر کرد.
ایجنت در هر چرخه (`run` / `watch` / `commands`) دستورهای در انتظار سیستم خودش را می‌گیرد، اجرا می‌کند
و نتیجه را به سرور برمی‌گرداند.

---

## فایل تنظیمات (agent.json)

```powershell
.\InventoryAgent.exe init -s http://192.168.1.10:5000
```

فایل `agent.json` کنار exe ساخته می‌شود؛ سوییچ‌های خط فرمان همیشه بر آن اولویت دارند.
نمونه‌ی کامل با توضیحات: `agent.sample.json`

---

## نکات فنی

- سورس در `src/Agent.Console` و عضو solution اصلی است.
- ویندوز: خواندن از WMI (Win32_*‎ و WmiMonitorID از EDID). لینوکس: ‎/proc و ‎/sys.
- خروجی دقیقاً با قرارداد سرور (`DetailsJson` با کلیدهای camelCase) سازگار است.
- اگر سرور تغییری نسبت به قبل ببیند، آن را «در انتظار تایید» نگه می‌دارد و چیزی را خودکار جایگزین نمی‌کند.

---

## 🚀 نصب و اجرا روی کلاینت‌ها (فایل‌های آماده)

پوشهٔ **`deploy/`** کنار همین فایل، ابزارهای آماده برای اجرا روی کلاینت‌ها را دارد:

| فایل | کار |
|---|---|
| `agent.bat` | دابل‌کلیک = ارسال مشخصات این سیستم به سرور · `agent.bat watch/dry/install` |
| `TEST-SERVER.bat` | عیب‌یابی ارتباط کلاینت با سرور (ping، پورت، ارسال آزمایشی) و معنی هر کد خطا |
| `INSTALL-AUTORUN.bat` / `UNINSTALL-AUTORUN.bat` | اجرای خودکار (هنگام بوت + هر ساعت) / حذف آن |
| `agent.json` | تنظیمات آماده — فقط مقدار `server` را با آدرس سرور خودتان عوض کنید |
| `README-نصب-روی-کلاینت.md` | راهنمای کامل فارسی نصب، اجرا و عیب‌یابی |

> ⚠️ نکتهٔ مهم سروری: اکشن `POST api/SystemInfo` باید `[AllowAnonymous]` باشد (ایجنت توکن ندارد).
> اگر ایجنت خطای **401** گرفت یعنی نسخهٔ سرور قدیمی است و باید با نسخهٔ جدید منتشر شود.
> راهنمای کامل: `deploy/README-نصب-روی-کلاینت.md`
