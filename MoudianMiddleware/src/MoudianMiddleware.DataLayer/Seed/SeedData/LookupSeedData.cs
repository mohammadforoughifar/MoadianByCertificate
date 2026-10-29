using MoudianMiddleware.Contracts.Constants;
using MoudianMiddleware.DataLayer.Entities;

namespace MoudianMiddleware.DataLayer.Seed.SeedData;

/// <summary>
/// داده‌های اولیه‌ی Lookupها و نقش‌ها.
/// </summary>
/// <remarks>
/// ⚠️ مقادیر <c>inp</c> (الگو)، <c>ins</c> (موضوع) و <c>pmt</c> (روش پرداخت) تا دریافت جدول رسمی
/// «نیازمند تأیید» هستند و به همین دلیل <c>IsVerified = false</c> و افزودن/ویرایش آن‌ها از UI ممکن است.
/// </remarks>
internal static class LookupSeedData
{
    internal static IReadOnlyList<InvoicePattern> InvoicePatterns() => new List<InvoicePattern>
    {
        new() { Code = 1, Title = "فروش", Description = "الگوی استاندارد فروش کالا/خدمت", SortOrder = 1, IsActive = true }
    };

    internal static IReadOnlyList<InvoiceSubject> InvoiceSubjects() => new List<InvoiceSubject>
    {
        new() { Code = 1, Title = "فروش", Description = "موضوع صورتحساب فروش", SortOrder = 1, IsActive = true },
        new() { Code = 2, Title = "برگشت از فروش", Description = "موضوع صورتحساب برگشت از فروش", SortOrder = 2, IsActive = true }
    };

    internal static IReadOnlyList<PaymentMethodCode> PaymentMethods() => new List<PaymentMethodCode>
    {
        new() { MethodCode = 1, Title = "چک", MoadianCode = 1, IsVerified = false, IsActive = true },
        new() { MethodCode = 2, Title = "تهاتر", MoadianCode = 2, IsVerified = false, IsActive = true },
        new() { MethodCode = 3, Title = "وجه نقد", MoadianCode = 3, IsVerified = false, IsActive = true },
        new() { MethodCode = 4, Title = "دستگاه کارت‌خوان (POS)", MoadianCode = 4, IsVerified = false, IsActive = true },
        new() { MethodCode = 5, Title = "درگاه پرداخت اینترنتی", MoadianCode = 5, IsVerified = false, IsActive = true },
        new() { MethodCode = 6, Title = "کارت به کارت", MoadianCode = 6, IsVerified = false, IsActive = true },
        new() { MethodCode = 7, Title = "انتقال به حساب", MoadianCode = 7, IsVerified = false, IsActive = true },
        new() { MethodCode = 8, Title = "سایر", MoadianCode = 8, IsVerified = false, IsActive = true }
    };

    /// <summary>
    /// واحدهای اندازه‌گیری؛ فقط واحدهایی که کد آن‌ها در مستندات دیده شده‌اند.
    /// بقیه‌ی واحدها باید از فهرست رسمی تکمیل شوند.
    /// </summary>
    internal static IReadOnlyList<MeasuringUnit> MeasuringUnits() => new List<MeasuringUnit>
    {
        new() { Code = "164", Title = "عدد", Symbol = "عدد", SortOrder = 1, IsActive = true }
    };

    /// <summary>
    /// کد خطای عمومی؛ برای خطاهایی که در جدول نیستند استفاده می‌شود (کد <c>*</c> = پیش‌فرض).
    /// </summary>
    internal static IReadOnlyList<MoadianErrorCode> ErrorCodes() => new List<MoadianErrorCode>
    {
        new()
        {
            Code = "*",
            PersianMessage = "سامانه‌ی مودیان خطایی اعلام کرده است که در جدول کدهای خطا موجود نیست.",
            TechnicalMessage = null,
            Severity = "Error",
            SuggestedAction = "متن فنی خطا را بررسی کنید و در صورت نیاز با پشتیبانی سامانه تماس بگیرید.",
            IsActive = true
        },
        new()
        {
            Code = "NETWORK",
            PersianMessage = "ارتباط با سامانه‌ی مودیان برقرار نشد.",
            TechnicalMessage = "Network/transport error",
            Severity = "Error",
            SuggestedAction = "اتصال شبکه/فایروال سرور را بررسی کنید و مجدداً تلاش کنید.",
            IsActive = true
        }
    };

    /// <summary>
    /// نقش‌های سیستمی و Permissionهای هرکدام.
    /// </summary>
    internal static IReadOnlyList<(string Name, string Description, IReadOnlyList<string> Permissions)> Roles() => new List<(string, string, IReadOnlyList<string>)>
    {
        ("SuperAdmin", "دسترسی کامل به همه‌ی مودیان و تنظیمات", Permissions.All),
        ("TenantAdmin", "مدیر مودی؛ تنظیمات، کاربران و همه‌ی عملیات صورتحساب",
            new[]
            {
                Permissions.MoadianView, Permissions.MoadianEdit, Permissions.MoadianTestConnection,
                Permissions.ProductView, Permissions.ProductEdit, Permissions.ProductDelete,
                Permissions.CustomerView, Permissions.CustomerEdit, Permissions.CustomerDelete,
                Permissions.InvoiceView, Permissions.InvoiceEdit, Permissions.InvoiceDelete,
                Permissions.InvoiceSend, Permissions.InvoiceRetry,
                Permissions.InquiryExecute,
                Permissions.ReportView, Permissions.ReportExport,
                Permissions.UserView, Permissions.UserEdit,
                Permissions.AuditView, Permissions.ApiClientManage
            }),
        ("Accountant", "ثبت و ارسال صورتحساب و گزارش‌گیری",
            new[]
            {
                Permissions.MoadianView,
                Permissions.ProductView, Permissions.ProductEdit,
                Permissions.CustomerView, Permissions.CustomerEdit,
                Permissions.InvoiceView, Permissions.InvoiceEdit,
                Permissions.InvoiceSend, Permissions.InvoiceRetry,
                Permissions.InquiryExecute,
                Permissions.ReportView, Permissions.ReportExport
            }),
        ("Operator", "ثبت پیش‌نویس صورتحساب و استعلام",
            new[]
            {
                Permissions.MoadianView,
                Permissions.ProductView, Permissions.CustomerView,
                Permissions.InvoiceView, Permissions.InvoiceEdit,
                Permissions.InquiryExecute
            }),
        ("Viewer", "فقط مشاهده",
            new[]
            {
                Permissions.MoadianView, Permissions.ProductView, Permissions.CustomerView,
                Permissions.InvoiceView, Permissions.ReportView
            })
    };
}
