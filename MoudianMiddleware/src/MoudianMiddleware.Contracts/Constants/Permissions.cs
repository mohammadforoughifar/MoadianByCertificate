namespace MoudianMiddleware.Contracts.Constants;

/// <summary>
/// کدهای Permission سیستم؛ منبع واحد برای Policyهای API، منوی Blazor و Seed نقش‌ها.
/// هر کد به شکل <c>Area.Action</c> است و در جدول <c>RolePermissions</c> ذخیره می‌شود.
/// </summary>
public static class Permissions
{
    /// <summary>مشاهده‌ی تنظیمات مودی.</summary>
    public const string MoadianView = "Moadian.View";

    /// <summary>ویرایش تنظیمات مودی و کلیدها.</summary>
    public const string MoadianEdit = "Moadian.Edit";

    /// <summary>اجرای «تست اتصال» به سامانه‌ی مودیان.</summary>
    public const string MoadianTestConnection = "Moadian.TestConnection";

    /// <summary>مشاهده‌ی کالا و خدمات.</summary>
    public const string ProductView = "Product.View";

    /// <summary>ایجاد/ویرایش کالا و خدمات.</summary>
    public const string ProductEdit = "Product.Edit";

    /// <summary>حذف کالا و خدمات.</summary>
    public const string ProductDelete = "Product.Delete";

    /// <summary>مشاهده‌ی مشتریان.</summary>
    public const string CustomerView = "Customer.View";

    /// <summary>ایجاد/ویرایش مشتری.</summary>
    public const string CustomerEdit = "Customer.Edit";

    /// <summary>حذف مشتری.</summary>
    public const string CustomerDelete = "Customer.Delete";

    /// <summary>مشاهده‌ی صورتحساب‌ها.</summary>
    public const string InvoiceView = "Invoice.View";

    /// <summary>ایجاد/ویرایش صورتحساب.</summary>
    public const string InvoiceEdit = "Invoice.Edit";

    /// <summary>حذف پیش‌نویس صورتحساب.</summary>
    public const string InvoiceDelete = "Invoice.Delete";

    /// <summary>ارسال صورتحساب به سامانه.</summary>
    public const string InvoiceSend = "Invoice.Send";

    /// <summary>تلاش مجدد ارسال ناموفق.</summary>
    public const string InvoiceRetry = "Invoice.Retry";

    /// <summary>استعلام وضعیت از سامانه.</summary>
    public const string InquiryExecute = "Inquiry.Execute";

    /// <summary>مشاهده‌ی گزارش‌ها.</summary>
    public const string ReportView = "Report.View";

    /// <summary>خروجی Excel گزارش‌ها.</summary>
    public const string ReportExport = "Report.Export";

    /// <summary>مشاهده‌ی کاربران.</summary>
    public const string UserView = "User.View";

    /// <summary>ایجاد/ویرایش/غیرفعال‌سازی کاربران.</summary>
    public const string UserEdit = "User.Edit";

    /// <summary>مدیریت نقش‌ها و Permissionها.</summary>
    public const string RoleManage = "Role.Manage";

    /// <summary>مشاهده‌ی Audit Log.</summary>
    public const string AuditView = "Audit.View";

    /// <summary>مدیریت کلیدهای API سیستم‌های خارجی.</summary>
    public const string ApiClientManage = "ApiClient.Manage";

    /// <summary>همه‌ی Permissionها (برای نقش SuperAdmin).</summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        MoadianView, MoadianEdit, MoadianTestConnection,
        ProductView, ProductEdit, ProductDelete,
        CustomerView, CustomerEdit, CustomerDelete,
        InvoiceView, InvoiceEdit, InvoiceDelete, InvoiceSend, InvoiceRetry,
        InquiryExecute,
        ReportView, ReportExport,
        UserView, UserEdit, RoleManage,
        AuditView, ApiClientManage
    };
}
