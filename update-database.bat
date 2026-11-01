@echo off
setlocal
pushd "%~dp0" >nul

echo [DB] Starting the TotallSoftwareForoughAriya and RADIS-HR database update...

if exist "%~dp0TotallSoftwareForoughAriya.Api.exe" (
    "%~dp0TotallSoftwareForoughAriya.Api.exe" --update-database
) else if exist "%~dp0TotallSoftwareForoughAriya.Api.dll" (
    dotnet "%~dp0TotallSoftwareForoughAriya.Api.dll" --update-database
) else if exist "%~dp0src\TotallSoftwareForoughAriya.Api\TotallSoftwareForoughAriya.Api.csproj" (
    dotnet run --project "%~dp0src\TotallSoftwareForoughAriya.Api\TotallSoftwareForoughAriya.Api.csproj" -- --update-database
) else (
    echo [ERROR] TotallSoftwareForoughAriya.Api.exe/.dll or the source project was not found beside this script.
    set "EXIT_CODE=2"
    goto :finish
)

set "EXIT_CODE=%ERRORLEVEL%"
if not "%EXIT_CODE%"=="0" echo [ERROR] Database update failed with exit code %EXIT_CODE%.

:finish
popd >nul
exit /b %EXIT_CODE%
