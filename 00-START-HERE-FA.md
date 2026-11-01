# راهنمای شروع سریع — `TotallSoftwareForoughAriya`

مسیر مرجع توسعه و Build در ریشه مخزن قرار دارد:

- **فایل Solution:** `TotallSoftwareForoughAriya.sln`
- **پروژه مدل‌های مشترک:** `src/TotallSoftwareForoughAriya.Shared/TotallSoftwareForoughAriya.Shared.csproj`
- **پروژه سرور (Web API):** `src/TotallSoftwareForoughAriya.Api/TotallSoftwareForoughAriya.Api.csproj`
- **پروژه کلاینت (Blazor WASM):** `src/TotallSoftwareForoughAriya.Client/TotallSoftwareForoughAriya.Client.csproj`
- **نام دیتابیس پیش‌فرض SQL Server:** `TotallSoftwareForoughAriya`

## بررسی بیلد و راه‌اندازی در ویندوز

1. در ریشه مخزن فایل `verify-unified.bat` یا دستورات زیر را اجرا کنید (`.NET 8 SDK` الزامی است):
   ```powershell
   dotnet restore TotallSoftwareForoughAriya.sln
   dotnet build TotallSoftwareForoughAriya.sln -c Release --no-restore
   ```
2. رشته اتصال دیتابیس در `src/TotallSoftwareForoughAriya.Api/appsettings.json` (و در محیط توسعه `appsettings.Development.json`) به صورت پیش‌فرض روی دیتابیس **`TotallSoftwareForoughAriya`** تنظیم شده است.
3. برای استقرار تک‌سروره در ویندوز از `.\deploy-single.ps1` یا `publish-server.bat` و برای ارتقای مستقیم دیتابیس از `update-database.bat` استفاده کنید.
4. مستند کامل ماژول جدید سامانه مودیان (مبتنی بر گواهی امضا و `TaxCollectData.Library 2.0.29`) در مسیر `docs/MOADIAN-SDK-2.0.29-GUIDE-FA.md` قرار دارد.
