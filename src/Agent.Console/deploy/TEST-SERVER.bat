@echo off
rem ============================================================
rem  TEST-SERVER.bat - عیب یابی ارتباط کلاینت با سرور شناسنامه سیستم
rem  این فایل را روی همان کلاینتی که agent.bat را اجرا کردید اجرا کنید.
rem  خروجی: 4 خط [1/4] ... [4/4] - همین را برای بررسی بفرستید.
rem ============================================================
chcp 65001 >nul
setlocal EnableExtensions
set "SRV=http://192.168.30.104:8181"
set "SRVIP=192.168.30.104"
set "PORT=8181"
set "OUT=%TEMP%\inv_diag"
if not exist "%TEMP%" set "OUT=%~dp0inv_diag"

echo ============================================================
echo   عیب یابی ارتباط با سرور شناسنامه سیستم
echo   سرور: %SRV%
echo ============================================================
echo.

echo [1/4] تست ping ...
ping -n 2 %SRVIP% >"%OUT%.ping" 2>&1
find "TTL=" <"%OUT%.ping" >nul
if errorlevel 1 echo       هشدار: پاسخ ping نیامد - اگر فایروال ICMP بسته باشد طبیعی است.
if not errorlevel 1 echo       OK - سرور در شبکه پاسخ می دهد.
echo.

echo [2/4] تست باز بودن پورت %PORT% (درخواست به صفحه اصلی برنامه) ...
curl -s -m 6 -o "%OUT%.root" -w "%%{http_code}" "%SRV%/" >"%OUT%.rootcode" 2>nul
set /p ROOTCODE=<"%OUT%.rootcode"
if "%ROOTCODE%"=="000" goto portclosed
if "%ROOTCODE%"=="" goto portclosed
if "%ROOTCODE%"=="404" echo       OK - پورت باز است ^(HTTP 404: اینجا صفحه ی اصلی نیست و برای نصب فقط-API طبیعی است^).
if not "%ROOTCODE%"=="404" echo       OK - برنامه روی سرور پاسخ داد ^(HTTP %ROOTCODE%^) - پورت باز است.
goto portok

:portclosed
echo       ❌ پورت %PORT% جواب نمی دهد!
echo          یعنی یکی از اینها: برنامه روی سرور اجرا نشده / پورتش %PORT% نیست / فایروال ویندوزِ سرور بسته است / آدرس اشتباه است.
echo          مسیر قطع: کلاینت به سرور نمی رسد ^(مشکل برنامه نیست^).
goto afternet

:portok

echo.
echo [3/4] تست ارسال گزارش آزمایشی ^(POST api/SystemInfo^) ...
> "%OUT%.json" echo {"agentId":"DIAG-TEST-%COMPUTERNAME%","cpu":"Diagnostic CPU","ram":"8 GB","hardDisk":"DiagDisk 256GB","totalRamGb":8,"osName":"Windows-Diagnostic","detailsJson":"{\"cpus\":[],\"ramSticks\":[],\"disks\":[]}"}
curl -s -m 30 -o "%OUT%.resp" -w "%%{http_code}" -X POST -H "Content-Type: application/json" --data-binary "@%OUT%.json" "%SRV%/api/SystemInfo" >"%OUT%.postcode" 2>nul
set /p POSTCODE=<"%OUT%.postcode"
echo       کد پاسخ سرور: %POSTCODE%
if "%POSTCODE%"=="200" goto postok
if "%POSTCODE%"=="401" goto post401
if "%POSTCODE%"=="000" goto postnet
if "%POSTCODE%"=="" goto postnet
if "%POSTCODE%"=="404" goto post404
if "%POSTCODE%"=="307" goto post307
if "%POSTCODE%"=="308" goto post307
echo       پاسخ سرور: 
type "%OUT%.resp"
goto afterpost

:postok
echo       ✅ سرور گزارش را پذیرفت!
echo          پاسخ: 
type "%OUT%.resp"
echo.
echo          الان در برنامه ^(بخش شناسنامه سیستم^) باید سیستمی به نام DIAG-TEST-%COMPUTERNAME% ببینید.
goto afterpost

:post401
echo       ❌ خطای 401 = سرور درخواست بدون توکن را رد می کند.
echo          >> یعنی نسخه ی سرور، اصلاحیه ی اجازه ی ایجنت ^([AllowAnonymous]^) را ندارد.
echo          >> راه حل: سرور را با نسخه ی به روز دوباره منتشر ^(republish^) و راه اندازی کنید.
goto afterpost

:post404
echo       ❌ خطای 404 = مسیر api/SystemInfo روی این سرور پیدا نشد.
echo          >> یعنی برنامه ی دیگری روی این پورت جواب می دهد، یا سرور نسخه ی خیلی قدیمی است.
goto afterpost

:post307
echo       ⚠️ ریدایرکت به HTTPS - سرور http را به https می فرستد.
echo          >> راه حل: در agent.json مقدار insecure را true کنید یا از آدرس https با پورت 5443 استفاده کنید.
goto afterpost

:postnet
echo       ❌ هیچ پاسخ HTTP نیامد ^(نه 200 نه 401^) = مشکل شبکه/فایروال/آدرس است، نه برنامه.
echo          >> با ping و تست پورت بالا وضعیت شبکه را بررسی کنید.
goto afterpost

:afterpost

:afternet
echo.
echo [4/4] تست خواندن فهرست سیستم ها ^(GET api/SystemInfo^) ...
curl -s -m 15 -o nul -w "%%{http_code}" "%SRV%/api/SystemInfo" >"%OUT%.listcode" 2>nul
set /p LISTCODE=<"%OUT%.listcode"
echo       کد پاسخ: %LISTCODE%  ^(401 اینجا طبیعی است: فهرست فقط داخل برنامه و با ورود کاربر دیده می شود^)
echo.

echo ============================================================
echo   خلاصه را کامل کپی کنید و بفرستید ^(4 خط بالا^)
echo ============================================================
echo.
pause
