namespace MoudianMiddleware.Contracts.Enums;

/// <summary>
/// نوع عملیات ثبت‌شده در Audit Log.
/// </summary>
public enum AuditAction
{
    /// <summary>ایجاد موجودیت.</summary>
    Create = 1,

    /// <summary>ویرایش موجودیت.</summary>
    Update = 2,

    /// <summary>حذف (نرم) موجودیت.</summary>
    Delete = 3,

    /// <summary>ورود کاربر.</summary>
    Login = 4,

    /// <summary>خروج کاربر.</summary>
    Logout = 5,

    /// <summary>ارسال صورتحساب.</summary>
    SendInvoice = 6,

    /// <summary>استعلام وضعیت.</summary>
    Inquiry = 7,

    /// <summary>تلاش مجدد ارسال.</summary>
    Retry = 8,

    /// <summary>خروجی گرفتن (Excel).</summary>
    Export = 9,

    /// <summary>تغییر تنظیمات (شامل تنظیمات مودی و کلیدها).</summary>
    ConfigChange = 10,

    /// <summary>دسترسی به کلید خصوصی برای امضا (بدون ثبت محتوای کلید).</summary>
    SecretAccess = 11
}

/// <summary>
/// دسترسی‌های قابل واگذاری به کلید API سیستم‌های خارجی (قابل ترکیب).
/// </summary>
[Flags]
public enum ApiScope
{
    /// <summary>بدون دسترسی.</summary>
    None = 0,

    /// <summary>ثبت و ارسال صورتحساب.</summary>
    InvoiceWrite = 1,

    /// <summary>خواندن صورتحساب‌ها.</summary>
    InvoiceRead = 2,

    /// <summary>خواندن اطلاعات پایه (کالا/خدمت و مشتری).</summary>
    BaseInfoRead = 4,

    /// <summary>استعلام وضعیت از سامانه.</summary>
    InquiryRead = 8,

    /// <summary>ثبت پرداخت مستقل.</summary>
    PaymentWrite = 16
}
