@echo off
rem Runs setup-https.ps1 without ExecutionPolicy issues. Right-click - Run as administrator.
rem Example: setup-https.cmd -Domain erp.company.ir -LetsEncrypt -Email it@company.ir -Use443 -RedirectHttp
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0setup-https.ps1" %*
pause
