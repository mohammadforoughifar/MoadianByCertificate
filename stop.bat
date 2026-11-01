@echo off
setlocal
cd /d "%~dp0"
if not exist "totall.pid" (
  echo No PID file found; application may already be stopped.
  exit /b 0
)
set /p APP_PID=<totall.pid
if "%APP_PID%"=="" (del totall.pid & echo Empty PID file removed. & exit /b 0)
taskkill /PID %APP_PID% /T /F >nul 2>nul
if errorlevel 1 (
  echo Process %APP_PID% was not running.
) else (
  echo Stopped process %APP_PID%.
)
del totall.pid >nul 2>nul
endlocal
