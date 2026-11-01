@echo off
setlocal
cd /d "%~dp0"

set "ASPNETCORE_ENVIRONMENT=Production"
set "ASPNETCORE_URLS=http://192.168.30.104:8181"
set "ConnectionStrings__Default=Server=.;Database=InventoryDb;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False"
set "DOTNET_PRINT_TELEMETRY_MESSAGE=false"

if not exist "src\Inventory.Api\Inventory.Api.csproj" (
  echo ERROR: Run this script from the inventory project folder.
  pause
  exit /b 1
)
where dotnet >nul 2>nul || (echo ERROR: .NET 8 SDK/runtime is missing. & pause & exit /b 1)

REM ابتدا از توقف نسخهٔ قبلی مطمئن شو؛ فایل‌های publish روی ویندوز هنگام اجرا قفل هستند.
if exist "totall.pid" (
  for /f %%P in (totall.pid) do tasklist /FI "PID eq %%P" | find "%%P" >nul && (
    echo Application appears to be running. Use stop.bat first.
    pause
    exit /b 1
  )
  del "totall.pid" >nul 2>nul
)

REM همیشه سورس فعلی را Publish کن. نسخهٔ پیشین فقط وقتی Inventory.Api.exe وجود نداشت
REM Publish می‌کرد؛ بنابراین پس از تغییرات مودیان، start.bat همان DLL/کلاینت قدیمی را
REM بالا می‌آورد و نتیجهٔ اجرای برنامه با سورس مخزن متفاوت می‌شد.
echo Publishing current source...
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0deploy-single.ps1"
if errorlevel 1 (
  echo ERROR: Publish failed. Read the PowerShell message above.
  pause
  exit /b 1
)

if not exist "logs" mkdir "logs"
powershell -NoProfile -ExecutionPolicy Bypass -Command "$p=Start-Process -FilePath '%~dp0publish\app\Inventory.Api.exe' -WorkingDirectory '%~dp0publish\app' -PassThru -WindowStyle Hidden -RedirectStandardOutput '%~dp0logs\server.log' -RedirectStandardError '%~dp0logs\server-error.log'; Set-Content -Path '%~dp0totall.pid' -Value $p.Id"
if errorlevel 1 (
  echo ERROR: Could not start API. Check logs\server-error.log
  pause
  exit /b 1
)
echo Started. Open http://192.168.30.104:8181
echo Health check: http://192.168.30.104:8181/api/health
echo Check Windows Firewall allows inbound TCP 8181, and SQL Server is reachable with the configured app identity.
echo.
echo API in background started. Press any key to close this window.
pause >nul
endlocal
