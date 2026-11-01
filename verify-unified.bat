@echo off
setlocal
cd /d "%~dp0"
dotnet --version
if errorlevel 1 goto failed
dotnet restore TotallSoftwareForoughAriya.sln
if errorlevel 1 goto failed
dotnet build TotallSoftwareForoughAriya.sln -c Release --no-restore -m:1 -p:UseRazorSourceGenerator=false
if errorlevel 1 goto failed
echo [OK] Canonical unified solution (TotallSoftwareForoughAriya.sln) built. Runtime/browser/SQL testing is still required.
pause
exit /b 0
:failed
echo [ERROR] Build failed. See errors above.
pause
exit /b 1
