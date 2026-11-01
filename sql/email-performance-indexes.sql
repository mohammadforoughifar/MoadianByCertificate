/* ============================================================
   ایندکس‌های کاراییِ ماژول ایمیل (SQL Server)
   ------------------------------------------------------------
   چرا لازم است؟
   فهرست‌های دریافتی/ارسالی/بایگانی همیشه با «Email_Id + مرتب‌سازی بر Date»
   و فیلترهای تاریخ/پیامِ خوانده‌نشده/پیوست/پوشه خوانده می‌شوند.
   بدون ایندکس، SQL Server برای هر باز کردنِ صندوق یا هر تغییرِ فیلتر،
   کل جدول (که ستونِ سنگینِ Body هم دارد) را اسکن و مرتب می‌کند؛ نتیجه:
   «یا نمی‌آورد یا خیلی کند می‌آورد».
   ------------------------------------------------------------
   • این اسکریپت idempotent است؛ اجرای چندباره بی‌خطر است.
   • خودِ برنامه هم در DbInitializer (OfficeEmailSchemaV1) همین ایندکس‌ها را
     در اولین اجرا می‌سازد؛ این فایل برای اجرای دستی روی دیتابیسِ بزرگ است
     (تا ساختِ ایندکس در زمانِ استارت‌آپ باعث طولانی شدنِ راه‌اندازی نشود).
   • روی دیتابیسِ خیلی بزرگ، اجرا در ساعتِ کم‌ترافیک توصیه می‌شود.
   ============================================================ */

-- ---------- دریافتی: فهرست و فیلترِ تاریخ ----------
IF OBJECT_ID(N'dbo.Oto_TBL_Inbox', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Oto_TBL_Inbox_Email_Date' AND object_id = OBJECT_ID(N'dbo.Oto_TBL_Inbox'))
    CREATE INDEX [IX_Oto_TBL_Inbox_Email_Date] ON [dbo].[Oto_TBL_Inbox] ([Email_Id], [Date] DESC)
        INCLUDE ([Subject], [From_Address], [Is_Read], [Is_Neshan], [Is_Attachment], [IsInFolder], [Is_Spam]);
GO

-- ---------- دریافتی: بایگانی/اسپم/خوانده‌نشده ----------
IF OBJECT_ID(N'dbo.Oto_TBL_Inbox', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Oto_TBL_Inbox_Email_Folder' AND object_id = OBJECT_ID(N'dbo.Oto_TBL_Inbox'))
    CREATE INDEX [IX_Oto_TBL_Inbox_Email_Folder] ON [dbo].[Oto_TBL_Inbox] ([Email_Id], [IsInFolder], [Is_Spam], [Is_Read]);
GO

IF OBJECT_ID(N'dbo.Oto_TBL_Inbox', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Oto_TBL_Inbox_Email_Spam' AND object_id = OBJECT_ID(N'dbo.Oto_TBL_Inbox'))
    CREATE INDEX [IX_Oto_TBL_Inbox_Email_Spam] ON [dbo].[Oto_TBL_Inbox] ([Email_Id], [Is_Spam]);
GO

-- ---------- ارسالی ----------
IF OBJECT_ID(N'dbo.Oto_TBL_Sent', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Oto_TBL_Sent_Email_Date' AND object_id = OBJECT_ID(N'dbo.Oto_TBL_Sent'))
    CREATE INDEX [IX_Oto_TBL_Sent_Email_Date] ON [dbo].[Oto_TBL_Sent] ([Email_Id], [Date] DESC)
        INCLUDE ([Subject], [To_Display], [Is_Neshan], [Is_Attachment], [IsInFolder]);
GO

IF OBJECT_ID(N'dbo.Oto_TBL_Sent', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Oto_TBL_Sent_Email_Folder' AND object_id = OBJECT_ID(N'dbo.Oto_TBL_Sent'))
    CREATE INDEX [IX_Oto_TBL_Sent_Email_Folder] ON [dbo].[Oto_TBL_Sent] ([Email_Id], [IsInFolder]);
GO

-- ---------- پوشه‌های بایگانی ----------
IF OBJECT_ID(N'dbo.Oto_TBl_EmailFolder', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Oto_TBl_EmailFolder_User_Folder' AND object_id = OBJECT_ID(N'dbo.Oto_TBl_EmailFolder'))
    CREATE INDEX [IX_Oto_TBl_EmailFolder_User_Folder] ON [dbo].[Oto_TBl_EmailFolder] ([UserId], [IsFolder]);
GO

/* ============================================================
   (اختیاری) یک‌بار اصلاحِ تاریخِ رکوردهای قدیمی
   اگر سرور در منطقهٔ زمانیِ غیر از ایران اجرا می‌شده، تاریخِ ذخیره‌شده
   با ساعتِ ایران اختلاف دارد. بعد از این تغییر، رکوردهای جدید درست ذخیره
   می‌شوند؛ برای اصلاحِ گذشته ابتدا پشتیبان بگیرید و سپس (با محاسبهٔ اختلاف
   ساعتِ سرور) تاریخ‌ها را جابه‌جا کنید. این کار را بدون پشتیبان انجام ندهید.
   ============================================================ */
