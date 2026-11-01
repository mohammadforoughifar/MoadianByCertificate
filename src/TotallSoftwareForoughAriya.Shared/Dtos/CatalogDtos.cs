namespace Inventory.Shared.Dtos;

/// <summary>تنظیمات برنامه.</summary>
public class AppSettings
{
    /// <summary>روش قیمت‌گذاری خروج انبار: Average | FIFO | LIFO</summary>
    public string CostingMethod { get; set; } = "Average";

    /// <summary>اجازه منفی شدن موجودی (پیش‌فرض: غیرمجاز)</summary>
    public bool AllowNegativeStock { get; set; } = false;

    /// <summary>آدرس سرور مرکزی درخواست‌های IT — خالی یعنی همین سرور، مرکزی است</summary>
    public string? ItServerUrl { get; set; }

    /// <summary>نام این شرکت (هنگام ارسال درخواست به سرور مرکزی همراه درخواست می‌رود)</summary>
    public string? ItCompanyName { get; set; }

    /// <summary>توکن ربات بله</summary>
    public string? BaleBotToken { get; set; }

    /// <summary>توکن ایتایار</summary>
    public string? EitaaToken { get; set; }

    /// <summary>شماره معرف سامانه در پیام‌ها</summary>
    public string? MessengerSenderNumber { get; set; } = "09111189771";

    /// <summary>واحد وزن پیش‌فرض (kg, g, ton)</summary>
    public string WeightUnit { get; set; } = "kg";

    /// <summary>مدت نمایش اعلان زندهٔ بالای صفحه (میلی‌ثانیه). ۰ = تا بستن دستی. پیش‌فرض ۲ دقیقه. — سراسری، توسط مدیر سامانه</summary>
    public int NotifyBannerMs { get; set; } = 120_000;

    /// <summary>مدت نمایش پیام کوتاه/توست (میلی‌ثانیه). ۰ = تا بستن دستی. پیش‌فرض ۲۰ ثانیه. — سراسری، توسط مدیر سامانه</summary>
    public int NotifyToastMs { get; set; } = 20_000;

    /// <summary>واحد ابعاد پیش‌فرض (cm, mm, m)</summary>
    public string DimensionUnit { get; set; } = "cm";

    /// <summary>واحد حجم پیش‌فرض (ltr, ml, m3)</summary>
    public string VolumeUnit { get; set; } = "ltr";

    /// <summary>وزن پیش‌فرض محموله (kg)</summary>
    public decimal DefaultShipmentWeight { get; set; } = 0;

    // ---------- تنظیمات انبارداری، ارزش افزوده و ساختار کدینگ ----------
    /// <summary>نرخ پیش‌فرض ارزش افزوده در سیستم (درصد)</summary>
    public decimal DefaultVatRate { get; set; } = 10m;

    /// <summary>نرخ پیش‌فرض عوارض قانونی (درصد)</summary>
    public decimal DefaultDutyRate { get; set; } = 0m;

    /// <summary>طول کد هر سطح از گروه‌های کالا (تعداد ارقام، پیش‌فرض: ۲)</summary>
    public int CategoryCodeLength { get; set; } = 2;

    /// <summary>طول کد اختصاصی کالا / گام کالا (تعداد ارقام/کاراکترها، پیش‌فرض: ۴)</summary>
    public int ProductCodeLength { get; set; } = 4;

    /// <summary>تولید خودکار کد کالا بر اساس سلسله‌مراتب گروه کالا</summary>
    public bool AutoCodeFromCategory { get; set; } = true;

    /// <summary>جداکننده اجزای کد کالا (خالی = بدون جداکننده مانند 01020001، یا - یا /)</summary>
    public string? CodeDelimiter { get; set; } = "";
}

/// <summary>معرف (بازاریاب).</summary>
public class Referrer
{
    public int Id { get; set; }
    public string Name { get; set; } = "";

    /// <summary>نام مجموعه / شرکت</summary>
    public string? CompanyName { get; set; }

    public string? Phone { get; set; }

    /// <summary>درصد پورسانت فروش کالا — بر مبنای «سود» سطرهای کالا</summary>
    public decimal GoodsCommissionPercent { get; set; }

    /// <summary>درصد پورسانت فروش خدمات — بر مبنای «کل مبلغ» سطرهای خدمات</summary>
    public decimal ServiceCommissionPercent { get; set; }

    /// <summary>شماره کارت بانکی (برای واریز پورسانت)</summary>
    public string? CardNumber { get; set; }

    /// <summary>شماره شبا (IBAN)</summary>
    public string? Iban { get; set; }

    /// <summary>اجازه مشاهده کالاهای موجود در پنل معرف</summary>
    public bool CanViewProducts { get; set; }

    /// <summary>
    /// سقف درصد تخفیف مجاز این ویزیتور در سفارش میدانی.
    /// صفر = بدون سقف (تخفیف فقط با نظر مدیر فروش بررسی می‌شود).
    /// </summary>
    public decimal MaxDiscountPercent { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }

    /// <summary>تعداد اسناد فروش این معرف (پر شده توسط سرور)</summary>
    public int OrderCount { get; set; }

    /// <summary>جمع پورسانت‌های محاسبه‌شده (پر شده توسط سرور)</summary>
    public decimal TotalCommission { get; set; }

    /// <summary>جمع پرداختی‌ها (پر شده توسط سرور)</summary>
    public decimal TotalPaid { get; set; }

    /// <summary>مانده کیف پول = پورسانت − پرداختی (پر شده توسط سرور)</summary>
    public decimal WalletBalance { get; set; }
}

/// <summary>سند پرداخت پورسانت به معرف.</summary>
public class ReferrerPayment
{
    public int Id { get; set; }
    public int ReferrerId { get; set; }
    public string? ReferrerName { get; set; }
    public string Number { get; set; } = "";
    public decimal Amount { get; set; }
    public DateTime Date { get; set; }
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>گروه / دسته‌بندی کالا (درختی)</summary>
public class ProductCategory
{
    public int Id { get; set; }

    /// <summary>نام گروه</summary>
    public string Name { get; set; } = "";

    /// <summary>شناسه گروه والد (null = گروه ریشه)</summary>
    public int? ParentId { get; set; }

    /// <summary>شرح</summary>
    public string? Description { get; set; }

    /// <summary>فعال / غیرفعال</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>زمان ایجاد</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>تعداد کالاهای این گروه (پر شده توسط سرور)</summary>
    public int ProductCount { get; set; }

    /// <summary>عمق در درخت (پر شده توسط سرور — برای نمایش تورفتگی)</summary>
    public int Depth { get; set; }

    /// <summary>مسیر کامل: والد ← فرزند (پر شده توسط سرور)</summary>
    public string FullPath { get; set; } = "";
}

/// <summary>واحد شمارش کالا</summary>
public class MeasureUnit
{
    public int Id { get; set; }

    /// <summary>نام واحد (عدد، کیلوگرم، لیتر، ...)</summary>
    public string Name { get; set; } = "";

    /// <summary>فعال / غیرفعال</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>زمان ایجاد</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>تعداد کالاهای دارای این واحد (پر شده توسط سرور)</summary>
    public int ProductCount { get; set; }
}

/// <summary>نتیجه ورود گروهی کالا از اکسل</summary>
public class ExcelImportResult
{
    /// <summary>تعداد سطرهای ثبت‌شده</summary>
    public int Imported { get; set; }

    /// <summary>تعداد سطرهای رد شده</summary>
    public int Failed { get; set; }

    /// <summary>خطاهای هر سطر (شماره سطر + پیام)</summary>
    public List<string> Errors { get; set; } = new();
}

/// <summary>کالا / محصول</summary>
public class Product
{
    public int Id { get; set; }

    /// <summary>کد کالا (اختیاری، یکتا)</summary>
    public string Code { get; set; } = "";

    /// <summary>نام کالا</summary>
    public string Name { get; set; } = "";

    /// <summary>واحد شمارش (عدد، کیلوگرم، متر، ...)</summary>
    public string Unit { get; set; } = "عدد";

    /// <summary>گروه / دسته‌بندی کالا</summary>
    public string? Category { get; set; }

    /// <summary>بارکد</summary>
    public string? Barcode { get; set; }

    /// <summary>شناسه مالیاتی کالا/خدمت برای صورتحساب مودیان</summary>
    public string? TaxCode { get; set; }

    /// <summary>قیمت فروش (ریال)</summary>
    public decimal SalePrice { get; set; }

    /// <summary>قیمت خرید (ریال)</summary>
    public decimal PurchasePrice { get; set; }

    /// <summary>نقطه سفارش (حداقل موجودی مجاز)</summary>
    public decimal ReorderPoint { get; set; }

    /// <summary>حداکثر موجودی مطلوب</summary>
    public decimal MaxStock { get; set; }

    /// <summary>شرح</summary>
    public string? Description { get; set; }

    /// <summary>فعال / غیرفعال</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>خدمات است؟ (بدون موجودی انبار — فقط در فروش)</summary>
    public bool IsService { get; set; }

    /// <summary>انبار اختصاصی کالا (null = همه انبارها)</summary>
    public int? WarehouseId { get; set; }
    public string? WarehouseName { get; set; }

    /// <summary>زمان ورود (ثبت) کالا</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>مجموع موجودی در همه انبارها (پر شده توسط سرور)</summary>
    public decimal TotalStock { get; set; }

    /// <summary>قیمت میانگین موزون خرید (پر شده توسط سرور)</summary>
    public decimal AvgCost { get; set; }

    /// <summary>آیا موجودی زیر نقطه سفارش است؟</summary>
    public bool BelowReorder => ReorderPoint > 0 && TotalStock <= ReorderPoint;
}

/// <summary>انبار</summary>
public class Warehouse
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public string? Note { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>تعداد اقلام موجود در این انبار (پر شده توسط سرور)</summary>
    public int ItemCount { get; set; }
}

/// <summary>طرف حساب (مشتری / تأمین‌کننده)</summary>
public class Party
{
    public int Id { get; set; }
    public PartyType Type { get; set; }
    public string Name { get; set; } = "";
    public string? Phone { get; set; }
    public string? Mobile { get; set; }
    public string? Address { get; set; }
    public string? TaxId { get; set; }
    public string? PostalCode { get; set; }
    public string? Note { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>معرف پیش‌فرض مشتری (اختیاری — null = بدون معرف)</summary>
    public int? ReferrerId { get; set; }
    public string? ReferrerName { get; set; }

    /// <summary>کاربری که این طرف حساب را ثبت کرده است</summary>
    public int? RegisteredByUserId { get; set; }

    /// <summary>نام ثبت‌کننده (ویزیتور/کاربر) — «این مشتری را چه کسی ثبت کرد»</summary>
    public string? RegisteredByName { get; set; }

    /// <summary>پروندهٔ معرف ثبت‌کننده</summary>
    public int? RegisteredByReferrerId { get; set; }

    /// <summary>نام معرفِ ثبت‌کننده (برای نمایش در فهرست)</summary>
    public string? RegisteredByReferrerName { get; set; }

    /// <summary>زمان ثبت</summary>
    public DateTime? RegisteredAt { get; set; }

    /// <summary>مانده حساب (پر شده توسط سرور)</summary>
    public decimal Balance { get; set; }

    /// <summary>تعداد چک‌های برگشتی فعال/پاس‌نشده مشتری</summary>
    public int BouncedChequesCount { get; set; }

    /// <summary>جمع مبلغ چک‌های برگشتی مشتری (ریال)</summary>
    public decimal BouncedChequesTotal { get; set; }

    /// <summary>تعداد اقساط معوق پرداخت‌نشده</summary>
    public int OverdueInstallmentsCount { get; set; }

    /// <summary>جمع اقساط معوق پرداخت‌نشده (ریال)</summary>
    public decimal OverdueInstallmentsTotal { get; set; }
}

/// <summary>نتیجه فیلتر/صفحه‌بندی اقلام پایه</summary>
/// <remarks>
/// تعریف یکپارچهٔ PagedResult: بخش پایه از CatalogDtos و بخش صفحه‌بندی/جمع (Page, PageSize,
/// PageCount, SumTicks) از نسخهٔ پروژه‌ها. (در یکی از PRهای موازی، کلاس دوم در ProjectDtos
/// اضافه شده بود و Inventory.Shared را ناقابل‌کامپایل می‌کرد؛ این‌جا یک‌جا شده است.)
/// </remarks>
public class PagedResult<T>
{
    public List<T> Items { get; set; } = new();

    /// <summary>تعداد کل ردیف‌های منطبق با فیلتر (نه فقط این صفحه)</summary>
    public int TotalCount { get; set; }

    /// <summary>نام مستعار برای لیست پروژه‌ها — همان TotalCount</summary>
    public int Total
    {
        get => TotalCount;
        set => TotalCount = value;
    }

    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;

    /// <summary>تعداد صفحه‌ها (محاسبه‌شده)</summary>
    public int PageCount => PageSize <= 0 ? 1 : Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));

    /// <summary>نام مستعارِ PageCount — تعداد صفحه‌ها</summary>
    public int TotalPages => PageCount;

    /// <summary>
    /// جمع کل (تیک) روی «همهٔ» ردیف‌های منطبق با فیلتر — نه فقط صفحهٔ جاری.
    /// در لیست گزارش‌های کار برای نمایش «جمع ساعت» استفاده می‌شود.
    /// </summary>
    public long? SumTicks { get; set; }
}

/// <summary>آیتم سبک برای انتخاب در فرم‌ها</summary>
public class LookupItem
{
    public int Id { get; set; }
    public string Name { get; set; } = "";

    /// <summary>آدرس آواتار (اختیاری) — نمایش تصویر دایره‌ای در جستجو/انتخاب</summary>
    public string? AvatarUrl { get; set; }

    /// <summary>اطلاعات تکمیلی اختیاری برای نمایش زیر عنوان گزینه در انتخابگرها.</summary>
    public string? InfoText { get; set; }
}
