@echo off
rem ============================================================
rem  agent.bat  -  ارسال مشخصات سخت افزاری این سیستم به سرور
rem  سرور: http://192.168.30.104:8181
rem
rem  استفاده:
rem     agent.bat            ارسال یک باره به سرور
rem     agent.bat watch      هر ۶۰ دقیقه یک بار تکرار
rem     agent.bat dry        فقط نمایش داده، بدون ارسال
rem     agent.bat install    نصب اجرای خودکار - هنگام روشن شدن + هر ساعت + وضعیت چاپگرها هر ۵ دقیقه
rem ============================================================
chcp 65001 >nul
setlocal EnableExtensions
set "SERVER=http://192.168.30.104:8181"
set "SERVERIP=192.168.30.104"
set "AGENTKEY="
rem  اگر سرور کلید مشترک دارد، متغیر محیطی AGENT_KEY را ست کنید یا مقدار را زیر بگذارید
if defined AGENT_KEY set "AGENTKEY=--key %AGENT_KEY%"
rem  اگر آدرس سرور عوض شد، SERVER و SERVERIP را با هم تغییر دهید.
title Inventory Agent

rem ---------- حالت اجرا ----------
set "MODE=%~1"
if /i "%~1"=="-Elevated" set "MODE=%~2"
if "%MODE%"=="" set "MODE=run"

rem ---------- پیدا کردن فایل اجرایی ایجنت ----------
set "EXE="
if exist "%~dp0InventoryAgent.exe" set "EXE=%~dp0InventoryAgent.exe"
if not defined EXE if exist "%~dp0win-x64-no-dotnet\InventoryAgent.exe" set "EXE=%~dp0win-x64-no-dotnet\InventoryAgent.exe"
if not defined EXE if exist "%~dp0win-x64-needs-dotnet8\InventoryAgent.exe" set "EXE=%~dp0win-x64-needs-dotnet8\InventoryAgent.exe"
if not defined EXE goto noexe

rem ---------- دسترسی Administrator (برای سریال ها و SMART) ----------
if /i "%~1"=="-Elevated" goto afterelev
net session >nul 2>&1
if not errorlevel 1 goto afterelev
echo.
echo [i] برای خواندن سریال مادربرد و هارد و وضعیت SMART دسترسی Administrator لازم است.
powershell -NoProfile -Command "$p = Start-Process -FilePath '%~f0' -ArgumentList '-Elevated %MODE%' -Verb RunAs -PassThru -ErrorAction SilentlyContinue; if ($null -eq $p) { exit 1 } else { exit 0 }"
if not errorlevel 1 exit /b 0
echo [i] ادامه می دهم بدون دسترسی Administrator - ممکن است بعضی سریال ها خالی بماند.
:afterelev


echo ============================================================
echo   Inventory Agent  -  ارسال مشخصات سخت افزاری
echo   سرور : %SERVER%
echo   حالت : %MODE%
echo ============================================================
echo.

rem ---------- انتخاب دستور ----------
set "ARGS="
if /i "%MODE%"=="install" goto install
if /i "%MODE%"=="watch" set "ARGS=watch -s %SERVER% -i 60 %AGENTKEY%"
if /i "%MODE%"=="dry" set "ARGS=dry-run"
if /i "%MODE%"=="dry-run" set "ARGS=dry-run"
if /i "%MODE%"=="commands" set "ARGS=commands -s %SERVER% %AGENTKEY%"
if /i "%MODE%"=="run" set "ARGS=run -s %SERVER% %AGENTKEY%"
if not defined ARGS set "ARGS=%* -s %SERVER%"

rem ---------- بررسی سریع دسترسی به سرور ----------
if /i "%MODE%"=="dry" goto skipnet
if /i "%MODE%"=="dry-run" goto skipnet
ping -n 1 -w 2000 %SERVERIP% >nul 2>&1
if errorlevel 1 echo [هشدار] سرور به ping جواب نداد - اگر فایروال ICMP را بسته باشد طبیعی است.
:skipnet
echo.
echo [اجرا] %EXE% %ARGS%
echo اگر بیش از دو دقیقه طول کشید یعنی سرور یا پورت 8181 در دسترس نیست.
echo.
"%EXE%" %ARGS%
set "RC=%ERRORLEVEL%"
echo.
if "%RC%"=="0" echo [پایان] انجام شد.
if not "%RC%"=="0" echo [پایان] کد خطا %RC% - راهنمای عیب یابی در فایل README کنار همین فایل است.
if not "%RC%"=="0" echo [راهنما] اگر ارسال نشد پورت 8181 و فایروال سرور و شبکه را بررسی کنید.
echo.
pause
exit /b %RC%

rem ---------- فایل اجرایی پیدا نشد ----------
:noexe
echo.
echo [خطا] فایل InventoryAgent.exe پیدا نشد.
echo این فایل agent.bat باید کنار فایل اجرایی یا کنار پوشه های win-x64 باشد.
echo.
pause
exit /b 1

rem ---------- نصب اجرای خودکار ----------
:install
echo در حال ساخت سه تسک زمان بندی شده ...
echo.
schtasks /create /tn "InventoryAgent-Boot" /tr "\"%EXE%\" run -s %SERVER% %AGENTKEY%" /sc onstart /ru SYSTEM /rl highest /f
schtasks /create /tn "InventoryAgent-Hourly" /tr "\"%EXE%\" run -s %SERVER% %AGENTKEY%" /sc hourly /mo 1 /ru SYSTEM /rl highest /f
schtasks /create /tn "InventoryAgent-Printers" /tr "\"%EXE%\" printers -s %SERVER% %AGENTKEY%" /sc minute /mo 5 /ru SYSTEM /rl highest /f
echo.
schtasks /query /tn "InventoryAgent-Boot" >nul 2>&1 && echo [OK] تسک هنگام روشن شدن سیستم نصب شد.
schtasks /query /tn "InventoryAgent-Hourly" >nul 2>&1 && echo [OK] تسک هر ساعت نصب شد.
schtasks /query /tn "InventoryAgent-Printers" >nul 2>&1 && echo [OK] تسک وضعیت چاپگرها - هر ۵ دقیقه - نصب شد.
echo.
schtasks /query /tn "InventoryAgent-Boot" >nul 2>&1 || echo [هشدار] نصب تسک اول ناموفق بود - این فایل را با دسترسی Administrator اجرا کنید.
echo.
echo برای حذف تسک ها فایل UNINSTALL-AUTORUN.bat را اجرا کنید.
echo.
pause
exit /b 0
