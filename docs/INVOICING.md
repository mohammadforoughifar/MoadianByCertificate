# ماژول فاکتور خرید و فروش (Invoicing)

پیشوند همه‌ی نام‌ها: **`Fac`** — تا با ماژول انبار (`Inv`) و حسابداری (`Acc`) تداخل نداشته باشد.

این ماژول حلقه‌ی واسط بین **انبار** و **حسابداری** است: با قطعی شدن یک فاکتور،
هم سند انبار (رسید/حواله) و هم سند حسابداری آن به‌صورت خودکار صادر و قطعی می‌شود.

---

## ۱) انواع فاکتور

| نوع | `InvoiceKind` | اثر روی انبار | ماهیت سند انبار |
|---|---|---|---|
| فاکتور خرید | `Purchase = 0` | ورود کالا | افزایشی (`Increase`) |
| فاکتور فروش | `Sale = 1` | خروج کالا | کاهشی (`Decrease`) |
| برگشت از خرید | `PurchaseReturn = 2` | خروج کالا | کاهشی (`Decrease`) |
| برگشت از فروش | `SaleReturn = 3` | ورود کالا | افزایشی (`Increase`) |

وضعیت فاکتور: `InvoiceStatus` = `Draft (0)` / `Confirmed (1)` / `Cancelled (2)`
نحوه تسویه: `SettlementType` = `Credit (0)` نسیه / `Cash (1)` نقدی

شماره‌گذاری فاکتور **به تفکیک نوع** انجام می‌شود (هر نوع سری شماره‌ی مستقل خود را دارد).

---

## ۲) چرخه‌ی قطعی شدن فاکتور

با فشردن «ثبت و قطعی» سه گام پشت سر هم اجرا می‌شود:

1. **سند انبار** — بر اساس `FacRule.DocTypeId` یک `InvDoc` ساخته، ذخیره و قطعی می‌شود
   (`SaveDocAsync` → `ConfirmDocAsync`). موجودی، بهای تمام‌شده و کاردکس به‌روز می‌شود.
   ماهیت نوع سند **باید** با جهت فاکتور بخواند وگرنه خطا داده می‌شود.
2. **سند حسابداری انبار** — `_acc.PostInventoryDocAsync` سند «موجودی کالا / بهای تمام‌شده» را می‌زند
   (طبق قواعد `AccInvRule` ماژول حسابداری).
3. **سند حسابداری فاکتور** — `PostInvoiceVoucherAsync` سند فروش/خرید را صادر می‌کند:

| ردیف | حساب | مبلغ |
|---|---|---|
| ۱ | طرف حساب (نسیه) یا صندوق/بانک (نقدی) | `TotalNet` |
| ۲ | حساب اصلی (فروش / خرید / برگشتی) | `TotalTaxable` |
| ۳ | مالیات بر ارزش افزوده | `TotalVat` |
| ۴ | هزینه حمل | `ShippingCost` |

جهت بدهکار/بستانکار: طرف حساب در `Sale` و `PurchaseReturn` **بدهکار** و در `Purchase` و `SaleReturn` **بستانکار** است.
سند با `Source = VoucherSource.Invoice`، `SourceId = invoice.Id` و `Status = Confirmed` ثبت می‌شود.

**برگشت (Unconfirm/Cancel/Delete):** اسناد حسابداری با منبع `Invoice` حذف، سند انبار unpost و حذف می‌شود.

---

## ۳) محاسبه مبالغ (`Recalculate`)

```
سطر:  Gross   = Quantity × UnitPrice
      Discount = DiscountPercent > 0 ? Gross × DiscountPercent / 100 : Discount   // درصد بر مبلغ اولویت دارد
      Taxable  = Gross − Discount
      VatAmount= Taxable × VatRate / 100
      Total    = Taxable + VatAmount

فاکتور: TotalGross        = Σ Gross
        TotalLineDiscount = Σ Discount
        InvoiceDiscount   → به نسبت مبلغ، روی سطرها سرشکن می‌شود
        TotalTaxable      = Σ Taxable
        TotalVat          = Σ VatAmount
        TotalNet          = TotalTaxable + TotalVat + ShippingCost
```

همه‌ی مبالغ با `Math.Round(..., 0)` گرد می‌شوند (ریال، بدون اعشار).

---

## ۴) قواعد فاکتور (`FacRule`)

برای هر نوع فاکتور یک رکورد پیکربندی وجود دارد. اگر نباشد، `GetRulesAsync` آن را
به‌صورت **غیرفعال** می‌سازد. تنظیمات از صفحه‌ی `/fac/rules` قابل ویرایش است.

فیلدها: `DocTypeId`، `PartyAccountId`، `MainAccountId`، `VatAccountId`، `CashAccountId`،
`ShippingAccountId`، `AutoInvDoc`، `AutoVoucher`، `IsActive`.

### مقادیر پیش‌فرض (Seed)

| نوع فاکتور | نوع سند | طرف حساب | حساب اصلی | مالیات | نقد | حمل |
|---|---|---|---|---|---|---|
| خرید | `RC-BUY` | ۳۱۰۱ پرداختنی | ۶۲۰۱ خرید | ۳۲۰۱ | ۱۱۰۱ | ۶۳۰۴ |
| فروش | `IS-SELL` | ۱۲۰۱ دریافتنی | ۵۱۰۱ فروش | ۳۲۰۱ | ۱۱۰۱ | ۵۲۰۱ |
| برگشت از خرید | `IS-RET` | ۳۱۰۱ پرداختنی | ۶۲۰۲ برگشت از خرید | ۳۲۰۱ | ۱۱۰۱ | ۶۳۰۴ |
| برگشت از فروش | `RC-RET` | ۱۲۰۱ دریافتنی | ۵۱۰۲ برگشت از فروش | ۳۲۰۱ | ۱۱۰۱ | ۵۲۰۱ |

Seeding در `Data/DbInitializer.SeedInvoicing` انجام می‌شود و اگر جدول `FacRules` خالی نباشد
یا حساب‌ها/انواع سند موجود نباشند، از آن صرف‌نظر می‌کند.

---

## ۵) API

پایه: `api/fac`

| متد | مسیر | توضیح |
|---|---|---|
| `GET` | `invoices?kind&status&partyId&warehouseId&search&from&to&page&pageSize` | فهرست صفحه‌بندی‌شده |
| `GET` | `invoices/new?kind` | فاکتور خالی با شماره و تاریخ پیشنهادی |
| `GET` | `invoices/line?productId&kind&warehouseId` | ساخت سطر با قیمت، نرخ مالیات و موجودی |
| `GET` | `invoices/{id}` | دریافت فاکتور با اقلام |
| `POST` | `invoices` | ثبت / ویرایش (فقط پیش‌نویس) |
| `POST` | `invoices/{id}/confirm` | قطعی + صدور اسناد |
| `POST` | `invoices/{id}/unconfirm` | برگشت به پیش‌نویس + حذف اسناد |
| `POST` | `invoices/{id}/cancel` | ابطال |
| `DELETE` | `invoices/{id}` | حذف |
| `GET` / `POST` | `rules` | خواندن / ذخیره قواعد |
| `GET` | `reports/summary?kind&groupBy&from&to` | `groupBy ∈ {month, day, party, product}` |
| `GET` | `reports/dashboard?from&to` | خلاصه داشبورد |

**RBAC:** ماژول `FacInvoices` با دسترسی‌های `Create, Read, Update, Delete, Export, Confirm, Cancel`.

---

## ۶) صفحات کلاینت

| مسیر | فایل | توضیح |
|---|---|---|
| `/fac/invoices` | `Pages/Invoicing/FacInvoices.razor` | فهرست با تب نوع، فیلتر وضعیت/انبار/تاریخ، جدول دسکتاپ + کارت موبایل |
| `/fac/invoices/new/{Kind}` | `Pages/Invoicing/FacInvoiceForm.razor` | ثبت فاکتور جدید |
| `/fac/invoices/edit/{Id}` | همان | ویرایش، قطعی، برگشت، چاپ |
| `/fac/rules` | `Pages/Invoicing/FacRules.razor` | تنظیمات چهار نوع فاکتور |
| `/fac/dashboard` | `Pages/Invoicing/FacDashboardPage.razor` | داشبورد فروش و خرید |
| `/fac/reports/summary` | `Pages/Invoicing/FacSummary.razor` | گزارش تجمیعی + نوار سهم |

> نام کلاس صفحه نباید با نام DTO یکی باشد؛ به همین دلیل فایل داشبورد `FacDashboardPage.razor`
> نام‌گذاری شده تا با DTO `FacDashboard` تداخل نکند.

سرویس‌های کلاینت در `Services/IInvoicingApi.cs` و `Services/InvoicingApiServices.cs`:
`IFacInvoiceService`، `IFacRuleService`، `IFacReportService` — در `Program.cs` ثبت شده‌اند.

منو: زیرگروه‌های «فاکتور — عملیات» و «فاکتور — گزارش و تنظیمات» داخل گروه **ERP** سایدبار،
با باز شدن خودکار گروه در مسیرهای `/fac/…`.

---

## ۷) پایگاه داده

سه جدول: `FacInvoices`، `FacInvoiceLines`، `FacRules`.

Migration: `Migrations/20260906033059_AddInvoicingModule.cs` — مانند سایر ماژول‌ها به
**T-SQL خام و idempotent** بازنویسی شده تا روی دیتابیس واقعی که با ModelSnapshot مخزن هم‌خوان نیست هم اجرا شود.
اسکریپت مستقل: `sql/invoicing/AddInvoicingModule.sql` (۱۴ دستور گاردشده)،
نسخه‌ی اصلی EF برای مرجع: `sql/invoicing/AddInvoicingModule.original.cs.txt`.

```bash
dotnet ef database update --project src/Inventory.Api
# یا اجرای مستقیم sql/invoicing/AddInvoicingModule.sql روی SQL Server
```
