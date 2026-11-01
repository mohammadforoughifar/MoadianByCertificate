@echo off
setlocal EnableExtensions
chcp 65001 >nul

rem ================================================================
rem Inventory - انتشار روی Windows Server
rem آدرس اجرا: http://192.168.30.104:8181
rem SQL Server: 192.168.30.104 / InventoryDb
rem ================================================================

set "ROOT=%~dp0"
set "CLIENT=%ROOT%src\Inventory.Client"
set "API=%ROOT%src\Inventory.Api"
set "CLIENT_PUBLISH=%ROOT%artifacts\client-publish"
set "API_PUBLISH=%ROOT%artifacts\inventory-server"
set "DEPLOY=%ROOT%deploy\inventory-server"
set "DB_CONNECTION=Server=.;Database=InventoryDb;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False"

where dotnet >nul 2>nul
if errorlevel 1 (
  echo [ERROR] .NET SDK 8 روی این سیستم نصب نیست.
  exit /b 1
)

echo [1/6] پاک‌سازی خروجی قبلی...
if exist "%CLIENT_PUBLISH%" rmdir /s /q "%CLIENT_PUBLISH%"
if exist "%API_PUBLISH%" rmdir /s /q "%API_PUBLISH%"
if exist "%DEPLOY%" rmdir /s /q "%DEPLOY%"
mkdir "%CLIENT_PUBLISH%" "%API_PUBLISH%" "%DEPLOY%"

pushd "%ROOT%"

echo [2/6] انتشار کلاینت Blazor...
dotnet publish "%CLIENT%\Inventory.Client.csproj" -c Release --no-restore -o "%CLIENT_PUBLISH%"
if errorlevel 1 goto :failed

rem فایل‌های static کلاینت باید در wwwroot API قرار بگیرند.
echo [3/6] انتقال فایل‌های رابط کاربری به API wwwroot...
if not exist "%API%\wwwroot" mkdir "%API%\wwwroot"
xcopy "%CLIENT_PUBLISH%\wwwroot\*" "%API%\wwwroot\" /E /I /Y >nul
if errorlevel 1 goto :failed

echo [4/6] انتشار API برای Windows x64...
dotnet publish "%API%\Inventory.Api.csproj" -c Release -r win-x64 --self-contained false -o "%API_PUBLISH%"
if errorlevel 1 goto :failed

echo [5/6] آماده‌سازی پوشه استقرار...
xcopy "%API_PUBLISH%\*" "%DEPLOY%\" /E /I /Y >nul
if errorlevel 1 goto :failed

rem تنظیمات اجرایی را جداگانه می‌سازیم تا اجرای سرور بدون ویرایش دستی انجام شود.
>"%DEPLOY%\run-server.bat" echo @echo off
>>"%DEPLOY%\run-server.bat" echo set "ASPNETCORE_ENVIRONMENT=Production"
>>"%DEPLOY%\run-server.bat" echo set "ASPNETCORE_URLS=http://192.168.30.104:8181"
>>"%DEPLOY%\run-server.bat" echo set "ConnectionStrings__Default=%DB_CONNECTION%"
>>"%DEPLOY%\run-server.bat" echo cd /d "%%~dp0"
>>"%DEPLOY%\run-server.bat" echo Inventory.Api.exe

>"%DEPLOY%\README-DEPLOY.txt" echo آدرس سامانه: http://192.168.30.104:8181
>>"%DEPLOY%\README-DEPLOY.txt" echo اجرا: فایل run-server.bat را با دسترسی مناسب اجرا کنید.
>>"%DEPLOY%\README-DEPLOY.txt" echo در Windows Firewall پورت TCP 8181 را باز کنید.
>>"%DEPLOY%\README-DEPLOY.txt" echo SQL Server: 192.168.30.104 / InventoryDb

popd
echo.
echo [OK] انتشار با موفقیت انجام شد.
echo پوشه آماده انتقال به سرور:
echo %DEPLOY%
echo.
echo روی سرور فایل run-server.bat را اجرا کنید.
exit /b 0

:failed
popd
echo.
echo [ERROR] انتشار ناموفق بود. خروجی خطا را بررسی کنید.
exit /b 1
