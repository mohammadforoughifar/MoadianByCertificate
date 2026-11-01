# قرارداد مودال‌ها: بسته‌شدن فقط با دکمهٔ × / «بستن» / «انصراف»

## قاعده

هیچ مودال، فرم، شیت یا دیالوگی در سامانه **نباید** با کلیک روی پس‌زمینهٔ تیره
(backdrop / overlay / اصطلاحاً «فرم‌زیری») بسته شود. کاربر فقط با این سه راه مودال را می‌بندد:

1. دکمهٔ **×** در هدر مودال
2. دکمهٔ **«بستن»**
3. دکمهٔ **«انصراف»**

کلید **Esc** هم مجاز است، چون در `wwwroot/index.html` دقیقاً همان دکمهٔ × را کلیک می‌کند
(هندلر سراسری `data-hotkey="esc"` روی آخرین المنت = بالاترین مودال باز).

**دلیل:** کلیک تصادفی روی حاشیهٔ مودال، فرم پرشده را می‌بندد و همهٔ دادهٔ تایپ‌شدهٔ کاربر
(متن نامه، فاکتور، اطلاعات پرسنل، دلیل رد درخواست و…) بی‌خبر از بین می‌رود.

## استثناها (این‌ها مودال نیستند و بستنشان با کلیک بیرون درست است)

| کلاس | کاربرد |
|---|---|
| `pop-overlay` | دراپ‌داون انتخاب (`SearchSelect`, `SearchSelectStr`, `MultiSearchSelect`) و منوهای کوچک صفحات |
| `dfi-overlay` | تقویم `DateFilterInput` |
| `menu-overlay` | منوی راست‌کلیک/کارت `ProjectsList` |
| `sidebar-backdrop` | منوی موبایل (`MainLayout`) |
| `da-tools-backdrop` | نوار ابزار شناور آرشیو اسناد |

این لایه‌ها ورودی متنی ندارند و کلیک بیرون، رفتار استاندارد و مورد انتظار آن‌هاست.

## الگوی درست نوشتن مودال

الگوی بوت‌استرپ (کامپوننت مشترک `Shared/Modal.razor` و مودال‌های درون‌صفحه‌ای):

```razor
@* ✅ درست: روی لایهٔ پس‌زمینه هیچ @onclick نگذارید *@
<div class="modal fade show d-block" tabindex="-1" style="background:rgba(0,0,0,.5)">
    <div class="modal-dialog modal-dialog-centered" @onclick:stopPropagation="true">
        <div class="modal-content">
            <div class="modal-header">
                <h5 class="modal-title">عنوان</h5>
                <button type="button" class="btn-close" @onclick="CloseForm"></button>
            </div>
            ...
            <div class="modal-footer">
                <button class="btn btn-light" @onclick="CloseForm">انصراف</button>
                <button class="btn btn-primary" @onclick="Save">ذخیره</button>
            </div>
        </div>
    </div>
</div>
```

```razor
@* ❌ غلط: با کلیک روی پس‌زمینه بسته می‌شود *@
<div class="modal fade show d-block" style="background:rgba(0,0,0,.5)" @onclick="CloseForm">
```

الگوی ماژول نامه‌ها (`letters.css` — کلاس‌های `gov` برای مودال و `ov` برای شیت):

```razor
<div class="ltx" style="animation:none;transform:none">
    <div class="gov"></div>              @* ✅ بدون هندلر کلیک *@
    <div class="gmodal" @onclick:stopPropagation="true">
        <div class="gm-h">
            <button class="x" @onclick="Close"><i class="bi bi-x-lg"></i></button>
        </div>
        ...
        <div class="gm-f">
            <button class="lbtn ghost" @onclick="Close">انصراف</button>
        </div>
    </div>
</div>
```

نکته: `@onclick:stopPropagation="true"` روی خودِ جعبهٔ مودال لازم نیست ولی بی‌ضرر است؛
ماندنش باعث می‌شود اگر روزی هندلری به پس‌زمینه اضافه شد، کلیک داخل مودال به آن نرسد.

## چک‌لیست هر مودال جدید

- [ ] لایهٔ پس‌زمینه (`modal` / `gov` / `ov` / `*-backdrop`) هیچ `@onclick` ندارد
- [ ] هدر مودال دکمهٔ × دارد (`btn-close` یا `class="x"`)
- [ ] فوتر مودال دکمهٔ «انصراف» یا «بستن» دارد (برای فرم‌ها)
- [ ] اگر دکمهٔ × از `data-hotkey="esc"` استفاده می‌کند، Esc هم همان × را صدا می‌زند

## نگهبان خودکار (Regression Guard)

تست `tests/compatibility/ModalDismissTests.cs` سورس همهٔ فایل‌های `.razor` کلاینت را اسکن می‌کند
و اگر روی لایهٔ پس‌زمینهٔ هر مودال `@onclick` ببیند، **قرمز** می‌شود و فایل و شمارهٔ خط را
گزارش می‌کند. یعنی این ایراد دیگر نمی‌تواند بدون شکستن بیلد/تست وارد مخزن شود.

اجرا:

```bash
dotnet test tests/compatibility --filter "FullyQualifiedName~ModalDismiss"
```

## اصلاح انجام‌شده (۱۴۰۴/۰۶/۲۵)

۳۳ هندلر «بستن با کلیک پس‌زمینه» در ۲۷ فایل حذف شد:

- **کامپوننت مشترک:** `Shared/Modal.razor` (پوشش ۲۰۶ مودال در همهٔ ماژول‌ها: حسابداری، کاتالوگ،
  انبار، خزانه‌داری، فاکتور/سفارش، انبارگردانی، تعمیرات، پروژه‌ها، منابع انسانی، HrCore، HrMain،
  بن‌سازه، FaAtt، FaLms، FaCom، FaPay، مودیان، آرشیو اسناد، تنظیمات)، `Shared/FilePreviewModal.razor`،
  `Shared/ArchiveButton.razor`
- **دوربین مداربسته:** `CctvCameras`، `CctvNvrs`
- **چت:** لایت‌باکس پیش‌نمایش تصویر در `ChatPage`
- **منابع انسانی:** `AttendanceAdmin` (مودال شیفت)، `HrPanel` (مودال رد درخواست)
- **دارایی/IT:** `SystemId` (چک‌لیست تحویل)، `SystemInfoList` (تایم‌لاین تغییرات)
- **اداری:** `OfficeMachines` (فرم ماشین + فرم تعمیرات)، `Email` (ارسال ایمیل + حساب ایمیل)
- **نامه‌ها (وارده و صادره):** `ComposeLetterModal`، `ComposeOutgoingModal`، `ErjaSheet`،
  `OutgoingErjaSheet`، `GardeshModal`، `OutgoingGardeshModal`، `PishnevisModal`،
  `LetterConfirmModal`، `BayeganiFolderPickerModal`، `LetterCartable` (ثبت پاسخ)،
  `OutgoingLetterCartable` (ثبت پاسخ + امضا)، `OutgoingDabirkhane` (ثبت دبیرخانه)
- **تنظیمات/سیستم:** `AuditLog` (جزئیات لاگ)، `Roles` (فرم نقش)، `SystemUsers` (فرم کاربر + تغییر عکس)

همچنین دو متد خصوصی بی‌استفاده (`CloseRelIfSelf`) و ۹ هندلر بی‌اثر `@onclick="() => { }"`
که فقط برای جلوگیری از نفوذ کلیک به پس‌زمینه نوشته شده بودند، پاک شدند.
