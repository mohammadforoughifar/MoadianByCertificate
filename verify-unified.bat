@echo off
setlocal
cd /d "%~dp0"
dotnet --version
if errorlevel 1 goto failed
dotnet restore Inventory.sln
if errorlevel 1 goto failed
dotnet build Inventory.sln -c Release --no-restore -m:1 -p:UseRazorSourceGenerator=false
if errorlevel 1 goto failed
echo [OK] Canonical unified solution built. Runtime/browser/SQL testing is still required.
pause
exit /b 0
:failed
echo [ERROR] Build failed. See errors above.
pause
exit /b 1
