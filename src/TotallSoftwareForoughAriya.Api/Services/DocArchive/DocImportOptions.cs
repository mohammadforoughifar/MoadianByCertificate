namespace Inventory.Api.Services.DocArchive;

/// <summary>پیکربندی ورود انبوه — عمداً کوچک است. سه مقدار، نه بیشتر.</summary>
public class DocImportOptions
{
    public const string SectionName = "DocArchiveImport";

    /// <summary>همیشه پیش‌فرض روشن. مقدار Enabled در appsettings نادیده گرفته می‌شود؛
    /// تنها راه خاموش کردن: متغیر محیطی DocArchiveImport__Enabled=false</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>سقف حجم هر فایل؛ همان سقف آپلود دستی.</summary>
    public long MaxFileBytes { get; set; } = 10L * 1024 * 1024;

    /// <summary>پوشه‌های ناموجود در برنامه به‌صورت خودکار ساخته شوند.</summary>
    public bool CreateMissingFolders { get; set; } = true;

    /// <summary>سقف طول مسیر کامل فایل روی دیسک.</summary>
    public int MaxFullPathLength { get; set; } = 259;
}

/// <summary>کدهای خطای ورود انبوه و پیام فارسی آن‌ها.</summary>
public static class DocImportErrorCodes
{
    public const string NameNoSeparator = "ERR_NAME_NO_SEPARATOR";
    public const string NameMultiSeparator = "ERR_NAME_MULTI_SEPARATOR";
    public const string TitleEmpty = "ERR_TITLE_EMPTY";
    public const string CodeEmpty = "ERR_CODE_EMPTY";
    public const string CodeChars = "ERR_CODE_CHARS";
    public const string CodeLong = "ERR_CODE_LONG";
    public const string TitleLong = "ERR_TITLE_LONG";
    public const string CodeDupFile = "ERR_CODE_DUP_FILE";
    public const string CodeDupDb = "ERR_CODE_DUP_DB";
    public const string FolderNotFound = "ERR_FOLDER_NOT_FOUND";
    public const string FolderNameLong = "ERR_FOLDER_NAME_LONG";
    public const string FileEmpty = "ERR_FILE_EMPTY";
    public const string FileTooLarge = "ERR_FILE_TOO_LARGE";
    public const string FileNameLong = "ERR_FILENAME_LONG";
    public const string PathLong = "ERR_PATH_LONG";
    public const string PathInvalid = "ERR_PATH_INVALID";
    public const string WriteFailed = "ERR_WRITE_FAILED";
    public const string InfoSystemFile = "INFO_SYSTEM_FILE";
    /// <summary>فایل شماره ندارد و هنگام ورود انبوه، شمارهٔ مدرک به‌صورت خودکار تخصیص می‌یابد (اطلاعاتی، نه خطا).</summary>
    public const string AutoCode = "INFO_AUTO_CODE";

    public static string MessageFa(string code) => code switch
    {
        NameNoSeparator => "نام فایل علامت «+» ندارد؛ الگو باید «عنوان+شماره» باشد.",
        NameMultiSeparator => "نام فایل بیش از یک «+» دارد؛ فقط یکی مجاز است.",
        TitleEmpty => "بخش عنوان (پیش از «+») خالی است.",
        CodeEmpty => "بخش شماره (پس از «+») خالی است.",
        CodeChars => "شماره فقط می‌تواند حرف، رقم، فاصله و یکی از «- _ .» داشته باشد.",
        CodeLong => "شماره بیش از ۸۰ کاراکتر است.",
        TitleLong => "عنوان بیش از ۲۵۰ کاراکتر است.",
        CodeDupFile => "شمارهٔ این فایل با فایل دیگری در همین پوشه یکی است.",
        CodeDupDb => "این شماره قبلاً در آرشیو وجود دارد.",
        FolderNotFound => "پوشهٔ مقصد پیدا نشد و ساخت خودکار پوشه خاموش است.",
        FolderNameLong => "نام یکی از پوشه‌ها بیش از ۲۰۰ کاراکتر است.",
        FileEmpty => "حجم فایل صفر بایت است.",
        FileTooLarge => "حجم فایل از سقف مجاز بیشتر است.",
        FileNameLong => "نام فایل بیش از ۲۵۵ کاراکتر است.",
        PathLong => "مسیر کامل فایل خیلی بلند است.",
        PathInvalid => "مسیر فایل معتبر نیست.",
        WriteFailed => "ذخیرهٔ فایل یا ساخت سند ناموفق بود.",
        InfoSystemFile => "فایل سیستمی است و نادیده گرفته شد.",
        AutoCode => "شمارهٔ مدرک در نام فایل نیست؛ هنگام ورود، به‌صورت خودکار شماره‌گذاری می‌شود.",
        _ => "خطای نامشخص."
    };
}
