namespace Inventory.Api.Services.Ai;

// =====================================================================
// تنظیمات «هوش مصنوعی فروغ آریا» — بخش Ai در appsettings.json
//
// پیش‌فرض: مدل لوکال Ollama روی همان سرور (بدون اینترنت، رایگان، امن).
// راهنمای نصب کامل: inventory/AI-SETUP-FA.md
// =====================================================================
public class AiOptions
{
    /// <summary>روشن/خاموش بودن کل دستیار.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// آدرس پایه API سازگار با OpenAI.
    /// Ollama: http://localhost:11434/v1 — مدل‌های LM Studio و vLLM هم همین قرارداد را دارند.
    /// </summary>
    public string BaseUrl { get; set; } = "http://localhost:11434/v1";

    /// <summary>کلید API (برای Ollama لازم نیست؛ برای درگاه‌های واسط لازم است).</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>مدل گفتگو — پیشنهاد لوکال: qwen3.5:latest (فارسی خوب + پشتیبانی از ابزار).</summary>
    public string ChatModel { get; set; } = "qwen3.5:latest";

    /// <summary>مدل امبدینگ برای جستجوی معنایی — پیشنهاد لوکال: bge-m3 (چندزبانه، فارسی خوب).</summary>
    public string EmbeddingModel { get; set; } = "bge-m3";

    /// <summary>
    /// مدل تبدیل گفتار به متن (رونویسی پیام‌های صوتی پیام‌رسان).
    /// قرارداد OpenAI: <c>POST {BaseUrl}/audio/transcriptions</c> — نمونه‌ها:
    /// OpenAI: whisper-1 یا gpt-4o-transcribe | Groq: whisper-large-v3 |
    /// سرور لوکال faster-whisper/speaches: هر نام مدلی که آن سرویس قبول کند.
    /// ⚠️ Ollama فعلی endpoint رونویسی ندارد؛ اگر سرور شما فقط Ollama دارد،
    /// این سه مقدار Transcription* را به یک سرویس رونویسی جدا وصل کنید.
    /// </summary>
    public string TranscriptionModel { get; set; } = "";

    /// <summary>آدرس پایهٔ سرویس رونویسی؛ خالی = همان BaseUrl گفتگو استفاده می‌شود.</summary>
    public string TranscriptionBaseUrl { get; set; } = "";

    /// <summary>کلید سرویس رونویسی؛ خالی = همان ApiKey گفتگو استفاده می‌شود.</summary>
    public string TranscriptionApiKey { get; set; } = "";

    /// <summary>
    /// زبان اجباری رونویسی (ISO-639-1). پیش‌فرض fa برای فارسی؛
    /// خالی بگذارید تا مدل خودش زبان را تشخیص دهد.
    /// </summary>
    public string TranscriptionLanguage { get; set; } = "fa";

    /// <summary>سقف حجم فایل صوتی برای رونویسی (مگابایت) — سرویس‌های ابری معمولاً ~۲۵MB.</summary>
    public int TranscriptionMaxMegabytes { get; set; } = 24;

    /// <summary>آدرس مؤثر سرویس رونویسی (پایهٔ جدا یا همان گفتگو).</summary>
    public string NormalizedTranscriptionBaseUrl =>
        ((string.IsNullOrWhiteSpace(TranscriptionBaseUrl) ? BaseUrl : TranscriptionBaseUrl) ?? "").Trim().TrimEnd('/');

    /// <summary>کلید مؤثر سرویس رونویسی (کلید جدا یا همان کلید گفتگو).</summary>
    public string EffectiveTranscriptionApiKey =>
        string.IsNullOrWhiteSpace(TranscriptionApiKey) ? (ApiKey ?? "") : TranscriptionApiKey.Trim();

    /// <summary>آیا رونویسی عملاً قابل استفاده است؟ (سرویس و مدل معرفی شده باشند)</summary>
    public bool TranscriptionReady =>
        Enabled && !string.IsNullOrWhiteSpace(TranscriptionModel) && NormalizedTranscriptionBaseUrl.Length > 0;

    /// <summary>تایم‌اوت هر درخواست به مدل (ثانیه). مدل لوکال روی CPU ممکن است کند باشد.</summary>
    public int TimeoutSeconds { get; set; } = 180;

    /// <summary>حداکثر تکرار حلقه ابزار در هر پاسخ.</summary>
    public int MaxToolIterations { get; set; } = 6;

    /// <summary>حداکثر پیام‌های تاریخی که به مدل داده می‌شود (حافظه مکالمه).</summary>
    public int MaxHistoryMessages { get; set; } = 12;

    /// <summary>سقف پیام روزانه هر کاربر (0 = نامحدود).</summary>
    public int MaxMessagesPerUserPerDay { get; set; } = 200;

    /// <summary>آیا دستیار داخل ربات بله جواب بدهد؟ (فقط کاربران لینک‌شده)</summary>
    public bool BaleEnabled { get; set; } = true;

    /// <summary>آیا دستیار داخل پیام‌رسان داخلی جواب بدهد؟ (گفتگوی مستقیم با کاربر «فروغ آریا»)</summary>
    public bool MessengerEnabled { get; set; } = true;

    /// <summary>گزارش صبحگاهی خودکار در بله (خلاصه روزانه: نامه‌ها، مهلت‌ها، چک‌ها، تأییدها).</summary>
    public bool BriefingEnabled { get; set; } = true;

    /// <summary>ساعت ارسال گزارش صبحگاهی به وقت سرور (قالب 24ساعته HH:mm).</summary>
    public string BriefingTime { get; set; } = "07:30";

    /// <summary>چک‌های باز با سررسید تا چند روز آینده در گزارش بیایند.</summary>
    public int BriefingChequeDays { get; set; } = 7;

    /// <summary>ارجاع‌های با مهلت تا چند روز آینده در گزارش بیایند.</summary>
    public int BriefingDeadlineDays { get; set; } = 2;

    /// <summary>خلاصه هفتگی خودکار برای مدیران (جمع‌بندی ۷ روز: فروش، HR، تیکت، ارجاع، چک، گزارش‌کار).</summary>
    public bool DigestEnabled { get; set; } = true;

    /// <summary>روز ارسال خلاصه هفتگی (شنبه، یکشنبه، ... یا Saturday..Friday).</summary>
    public string DigestDay { get; set; } = "Saturday";

    /// <summary>ساعت ارسال خلاصه هفتگی به وقت سرور (قالب 24ساعته HH:mm).</summary>
    public string DigestTime { get; set; } = "07:30";

    /// <summary>ارسال سر وقت یادآورهای شخصی در پیام‌رسان (بررسی هر ۶۰ ثانیه).</summary>
    public bool ReminderEnabled { get; set; } = true;

    /// <summary>اجرای دوره‌ای گزارش‌های زمان‌بندی‌شده کاربران (بررسی هر ۶۰ ثانیه).</summary>
    public bool ScheduleEnabled { get; set; } = true;

    /// <summary>ایندکس دوره‌ای اسناد برای جستجوی معنایی (هر ۱۰ دقیقه، §۲۲).</summary>
    public bool SearchEnabled { get; set; } = true;

    /// <summary>ارزیابی دوره‌ای قانون‌های هشدار شرطی کاربران (هر ۵ دقیقه، §۲۳).</summary>
    public bool AlertRuleEnabled { get; set; } = true;

    /// <summary>انقضای پیش‌فاکتور اقدام (دقیقه) — اقدام با تأیید کاربر.</summary>
    public int PendingActionExpiryMinutes { get; set; } = 15;

    public string NormalizedBaseUrl => (BaseUrl ?? "").Trim().TrimEnd('/');
}
