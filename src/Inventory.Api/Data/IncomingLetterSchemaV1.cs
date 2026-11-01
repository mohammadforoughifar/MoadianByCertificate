using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

// ============================================================
//  خودتعمیرِ اسکیمای «نامه وارده» — نسخه ۱
//
//  چرا لازم است؟
//  موجودیت IncomingLetter بعد از مایگریشن SquashedInitial اضافه شده و
//  هیچ مایگریشنی جدول IncomingLetters را نمی‌سازد. در SQL Server نتیجه
//  خطای «Invalid object name 'IncomingLetters'» در کارتابل، گزارش‌ساز و
//  ویجت‌های چارت اتوماسیون اداری است.
//
//  اینجا جدول را به‌صورت idempotent می‌سازیم (مثل WorkOrderSchemaV1 و
//  OfficeEmailSchemaV1) تا دیتابیس‌های قدیمی هم بدون مایگریشن جدید
//  به‌روز شوند.
// ============================================================
public static class IncomingLetterSchemaV1
{
    private static int _done;
    private static int _running;

    /// <summary>آخرین خطای ساخت جدول (null یعنی موفق).</summary>
    public static string? LastError { get; private set; }

    public static Task EnsureAsync(AppDbContext db) =>
        db.Database.IsSqlite() ? EnsureSqliteAsync(db) : EnsureSqlServerAsync(db);

    /// <summary>تضمین یک‌بار ساخت جدول در هر فرایند (با تلاش دوباره در صورت شکست قبلی).</summary>
    public static async Task EnsureOnceAsync(AppDbContext db)
    {
        if (Volatile.Read(ref _done) == 1 && LastError is null) return;
        if (Interlocked.Exchange(ref _running, 1) == 1) return;
        try
        {
            LastError = null;
            await EnsureAsync(db);
            if (LastError is null) Volatile.Write(ref _done, 1);
        }
        finally { Interlocked.Exchange(ref _running, 0); }
    }

    /// <summary>آیا جدول نامه وارده واقعاً وجود دارد؟ (برای هشدار صریح در لاگ شروع)</summary>
    public static async Task<bool> TableExistsAsync(AppDbContext db)
    {
        try
        {
            await db.IncomingLetters.AsNoTracking().Select(x => x.Id).Take(1).ToListAsync();
            return true;
        }
        catch { return false; }
    }

    // ==================== SQL Server ====================
    private static async Task EnsureSqlServerAsync(AppDbContext db)
    {
        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.IncomingLetters', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[IncomingLetters](
        [Id] int NOT NULL CONSTRAINT [PK_IncomingLetters] PRIMARY KEY,
        [LetterNumber] nvarchar(100) NULL,
        [Number] int NOT NULL DEFAULT(0),
        [NumberSabt] int NOT NULL DEFAULT(0),
        [Title] nvarchar(200) NOT NULL DEFAULT(N''),
        [TypeErsal] nvarchar(100) NULL,
        [Creator] int NOT NULL DEFAULT(0),
        [CreatorId] int NOT NULL DEFAULT(0),
        [CreateUserId] int NOT NULL DEFAULT(0),
        [Ferestande] nvarchar(200) NOT NULL DEFAULT(N''),
        [NumberLetterVarede] nvarchar(100) NULL,
        [Date] datetime2 NOT NULL DEFAULT(GETDATE()),
        [DateErsal] datetime2 NOT NULL DEFAULT(GETDATE()),
        [Description] nvarchar(max) NULL,
        [DeliveryName] nvarchar(100) NULL,
        [Mahramanegi] int NOT NULL DEFAULT(0),
        [Foriat] int NOT NULL DEFAULT(0),
        [IsNeshan] bit NOT NULL DEFAULT(0),
        [IsBayegani] bit NOT NULL DEFAULT(0),
        [IsDelete] bit NOT NULL DEFAULT(0),
        CONSTRAINT [FK_IncomingLetters_LetterSources_Id] FOREIGN KEY ([Id])
            REFERENCES [dbo].[LetterSources] ([Id]) ON DELETE CASCADE
    );
    CREATE INDEX [IX_IncomingLetters_DateErsal] ON [dbo].[IncomingLetters] ([DateErsal]);
    CREATE INDEX [IX_IncomingLetters_Number] ON [dbo].[IncomingLetters] ([Number]);
    CREATE INDEX [IX_IncomingLetters_NumberSabt] ON [dbo].[IncomingLetters] ([NumberSabt]);
    CREATE INDEX [IX_IncomingLetters_Ferestande] ON [dbo].[IncomingLetters] ([Ferestande]);
    CREATE INDEX [IX_IncomingLetters_CreateUserId] ON [dbo].[IncomingLetters] ([CreateUserId]);
END");

        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.IncomingLetters', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.IncomingLetters', N'CreatorId') IS NULL
    ALTER TABLE [dbo].[IncomingLetters] ADD [CreatorId] int NOT NULL CONSTRAINT [DF_IncomingLetters_CreatorId] DEFAULT(0);");

        // جدول رزرو شماره اندیکاتور/نامه (TBL_RezervationNumberLetter)
        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.LetterNumberReservations', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[LetterNumberReservations](
        [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_LetterNumberReservations] PRIMARY KEY,
        [TypeForm] int NOT NULL DEFAULT(3),
        [NumberSabt] int NOT NULL DEFAULT(0),
        [DateRezerv] datetime2 NOT NULL DEFAULT(GETDATE()),
        [SematId] int NULL,
        [UserId] int NOT NULL DEFAULT(0),
        [IsUsed] bit NOT NULL DEFAULT(0),
        [IsDelete] bit NOT NULL DEFAULT(0)
    );
    CREATE INDEX [IX_LetterNumberReservations_TypeForm_NumberSabt] ON [dbo].[LetterNumberReservations] ([TypeForm], [NumberSabt]);
    CREATE INDEX [IX_LetterNumberReservations_UserId] ON [dbo].[LetterNumberReservations] ([UserId]);
END");

        // ستون‌هایی که ممکن است در نصب‌های نیمه‌کاره جا مانده باشند
        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.IncomingLetters', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.IncomingLetters', N'IsNeshan') IS NULL
        ALTER TABLE dbo.IncomingLetters ADD IsNeshan bit NOT NULL DEFAULT(0);
    IF COL_LENGTH(N'dbo.IncomingLetters', N'IsBayegani') IS NULL
        ALTER TABLE dbo.IncomingLetters ADD IsBayegani bit NOT NULL DEFAULT(0);
    IF COL_LENGTH(N'dbo.IncomingLetters', N'IsDelete') IS NULL
        ALTER TABLE dbo.IncomingLetters ADD IsDelete bit NOT NULL DEFAULT(0);
    IF COL_LENGTH(N'dbo.IncomingLetters', N'NumberSabt') IS NULL
        ALTER TABLE dbo.IncomingLetters ADD NumberSabt int NOT NULL DEFAULT(0);
    IF COL_LENGTH(N'dbo.IncomingLetters', N'DeliveryName') IS NULL
        ALTER TABLE dbo.IncomingLetters ADD DeliveryName nvarchar(100) NULL;
    IF COL_LENGTH(N'dbo.IncomingLetters', N'NumberLetterVarede') IS NULL
        ALTER TABLE dbo.IncomingLetters ADD NumberLetterVarede nvarchar(100) NULL;
END");

        // ==================== باگ B4: ایندکس یکتای شمارهٔ اندیکاتور ====================
        // چرا لازم است؟ هر دو مسیر شماره‌گذاری «MAX()+1» بودند و تا پیش از این هیچ
        // تراکنش، lock یا محدودیت یکتایی وجود نداشت؛ پس دو ثبت همزمان می‌توانستند
        // NumberSabt یکسان بگیرند. منطق برنامه (تراکنش + بررسی صریح در AddAsync) تنها
        // خطا را «گزارش» می‌کند؛ چیزی که واقعاً جلوی race را می‌گیرد محدودیت یکتایی
        // در خود دیتابیس است، چون دو تراکنش همزمان هر دو MAX یکسانی می‌خوانند.
        //
        // دو نکتهٔ ایمنی که عمداً رعایت شده‌اند:
        //  ۱) FILTERED روی IsDelete = 0 — حذف نرم وجود دارد، پس شمارهٔ یک نامهٔ
        //     حذف‌شده نباید جلوی استفادهٔ مجدد از همان شماره را بگیرد.
        //  ۲) فقط وقتی ساخته می‌شود که دادهٔ «موجود» تکراری نداشته باشد. نصب‌های قدیمی
        //     به‌خاطر همین باگ احتمالاً شمارهٔ تکراری دارند و ساختِ بی‌قید‌و‌شرط باعث
        //     خطا در راه‌اندازی می‌شد؛ بدتر اینکه LastError را آلوده می‌کرد و طبق
        //     EnsureOnceAsync موجب تلاش مجدد در «هر» فراخوانی می‌شد. در آن حالت ایندکس
        //     ساخته نمی‌شود و اپ مثل قبل بالا می‌آید — محافظت لایهٔ برنامه باقی است و
        //    operator می‌تواند پس از پاک‌سازی تکراری‌ها با یک ری‌استارت ایندکس را بگیرد.
        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.IncomingLetters', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes
                   WHERE name = N'UX_IncomingLetters_NumberSabt'
                     AND object_id = OBJECT_ID(N'dbo.IncomingLetters'))
   AND NOT EXISTS (SELECT 1 FROM dbo.IncomingLetters
                   WHERE IsDelete = 0 GROUP BY NumberSabt HAVING COUNT(*) > 1)
BEGIN
    CREATE UNIQUE INDEX [UX_IncomingLetters_NumberSabt]
        ON [dbo].[IncomingLetters] ([NumberSabt]) WHERE [IsDelete] = 0;
END");

        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.LetterNumberReservations', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes
                   WHERE name = N'UX_LetterNumberReservations_TypeForm_NumberSabt'
                     AND object_id = OBJECT_ID(N'dbo.LetterNumberReservations'))
   AND NOT EXISTS (SELECT 1 FROM dbo.LetterNumberReservations
                   WHERE IsDelete = 0 GROUP BY TypeForm, NumberSabt HAVING COUNT(*) > 1)
BEGIN
    CREATE UNIQUE INDEX [UX_LetterNumberReservations_TypeForm_NumberSabt]
        ON [dbo].[LetterNumberReservations] ([TypeForm], [NumberSabt]) WHERE [IsDelete] = 0;
END");
    }

    // ==================== SQLite ====================
    private static async Task EnsureSqliteAsync(AppDbContext db)
    {
        await SafeAsync(db, @"
CREATE TABLE IF NOT EXISTS IncomingLetters (
    Id INTEGER NOT NULL PRIMARY KEY,
    LetterNumber TEXT NULL,
    Number INTEGER NOT NULL DEFAULT 0,
    NumberSabt INTEGER NOT NULL DEFAULT 0,
    Title TEXT NOT NULL DEFAULT '',
    TypeErsal TEXT NULL,
    Creator INTEGER NOT NULL DEFAULT 0,
    CreatorId INTEGER NOT NULL DEFAULT 0,
    CreateUserId INTEGER NOT NULL DEFAULT 0,
    Ferestande TEXT NOT NULL DEFAULT '',
    NumberLetterVarede TEXT NULL,
    Date TEXT NOT NULL,
    DateErsal TEXT NOT NULL,
    Description TEXT NULL,
    DeliveryName TEXT NULL,
    Mahramanegi INTEGER NOT NULL DEFAULT 0,
    Foriat INTEGER NOT NULL DEFAULT 0,
    IsNeshan INTEGER NOT NULL DEFAULT 0,
    IsBayegani INTEGER NOT NULL DEFAULT 0,
    IsDelete INTEGER NOT NULL DEFAULT 0,
    CONSTRAINT FK_IncomingLetters_LetterSources_Id FOREIGN KEY (Id)
        REFERENCES LetterSources (Id) ON DELETE CASCADE
);");
        await SafeAsync(db, @"
CREATE TABLE IF NOT EXISTS LetterNumberReservations (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    TypeForm INTEGER NOT NULL DEFAULT 3,
    NumberSabt INTEGER NOT NULL DEFAULT 0,
    DateRezerv TEXT NOT NULL,
    SematId INTEGER NULL,
    UserId INTEGER NOT NULL DEFAULT 0,
    IsUsed INTEGER NOT NULL DEFAULT 0,
    IsDelete INTEGER NOT NULL DEFAULT 0
);");
        await SafeAsync(db, "CREATE INDEX IF NOT EXISTS IX_IncomingLetters_DateErsal ON IncomingLetters (DateErsal);");
        await SafeAsync(db, "CREATE INDEX IF NOT EXISTS IX_IncomingLetters_Number ON IncomingLetters (Number);");
        await SafeAsync(db, "CREATE INDEX IF NOT EXISTS IX_IncomingLetters_NumberSabt ON IncomingLetters (NumberSabt);");
        await SafeAsync(db, "CREATE INDEX IF NOT EXISTS IX_IncomingLetters_Ferestande ON IncomingLetters (Ferestande);");
        await SafeAsync(db, "ALTER TABLE IncomingLetters ADD COLUMN CreatorId INTEGER NOT NULL DEFAULT 0;");
        await SafeAsync(db, "CREATE INDEX IF NOT EXISTS IX_IncomingLetters_CreateUserId ON IncomingLetters (CreateUserId);");
        await SafeAsync(db, "CREATE INDEX IF NOT EXISTS IX_LetterNumberReservations_TypeForm_NumberSabt ON LetterNumberReservations (TypeForm, NumberSabt);");

        // باگ B4 — همان ایندکس‌های یکتای فیلترشدهٔ مسیر SQL Server.
        // SQLite شرط «تکراری نبودن دادهٔ موجود» را در DDL ندارد؛ اگر تکراری وجود داشته
        // باشد خطای «unable to create a UNIQUE index: duplicate values» می‌دهد که
        // SafeAsync آن را بی‌صدا رد می‌کند (چون حاوی کلمهٔ duplicate است) و LastError
        // آلوده نمی‌شود — یعنی دقیقاً همان رفتار محافظتی مسیر SQL Server.
        await SafeAsync(db, "CREATE UNIQUE INDEX IF NOT EXISTS UX_IncomingLetters_NumberSabt ON IncomingLetters (NumberSabt) WHERE IsDelete = 0;");
        await SafeAsync(db, "CREATE UNIQUE INDEX IF NOT EXISTS UX_LetterNumberReservations_TypeForm_NumberSabt ON LetterNumberReservations (TypeForm, NumberSabt) WHERE IsDelete = 0;");
    }

    /// <summary>اجرای امن — خطای «شیء تکراری/موجود» راه‌اندازی برنامه را متوقف نکند.</summary>
    private static async Task SafeAsync(AppDbContext db, string sql)
    {
        try { await db.Database.ExecuteSqlRawAsync(sql); }
        catch (Exception ex)
        {
            var m = ex.Message ?? "";
            if (m.Contains("duplicate", StringComparison.OrdinalIgnoreCase) ||
                m.Contains("already exists", StringComparison.OrdinalIgnoreCase)) return;

            LastError = m;
            Console.WriteLine();
            Console.WriteLine("================= خطای ساخت جدول نامه وارده =================");
            Console.WriteLine($"[DB] IncomingLetterSchemaV1: {m}");
            Console.WriteLine("جدول IncomingLetters ساخته نشد؛ کارتابل نامه وارده، گزارش «همه نامه‌ها»");
            Console.WriteLine("و ویجت‌های چارت اتوماسیون اداری خطای «Invalid object name» می‌دهند.");
            Console.WriteLine("=============================================================");
            Console.WriteLine();
        }
    }
}
