@echo off
chcp 65001 >nul
echo This will create scheduled tasks (run at startup + every 60 minutes + printer status every 5 minutes).
echo.
set EXE=%~dp0InventoryAgent.exe
set CMD="%EXE%" run -s http://192.168.30.104:8181
schtasks /create /tn "InventoryAgent-Boot"   /tr "%CMD%" /sc onstart /ru SYSTEM /rl highest /f
schtasks /create /tn "InventoryAgent-Hourly" /tr "%CMD%" /sc hourly /mo 1 /ru SYSTEM /rl highest /f
set PCMD="%EXE%" printers -s http://192.168.30.104:8181
schtasks /create /tn "InventoryAgent-Printers" /tr "%PCMD%" /sc minute /mo 5 /ru SYSTEM /rl highest /f
echo.
echo Installed. Press any key to close.
pause >nul
