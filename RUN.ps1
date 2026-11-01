# =====================================================================
#  RUN.ps1 — اجرای کامل نرم‌افزار یکپارچه فروغ آریا (بدون نیاز به SQL Server)
#  هر ۴ مرحله را خودکار انجام می‌دهد:
#    1) پابلیش کلاینت Blazor
#    2) کپی فایل‌های کلاینت در wwwroot مربوط به API (استقرار تک‌سروره)
#    3) تنظیم SQLite به‌جای SQL Server (نیازی به نصب SQL Server نیست)
#    4) اجرای API روی http://localhost:5100
#
#  بعد از اجرا، در مرورگر باز کنید:  http://localhost:5100
#  ورود پیش‌فرض:  admin / admin
# =====================================================================
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

Write-Host "" -ForegroundColor Cyan
Write-Host "=====================================================" -ForegroundColor Cyan
Write-Host "  اجرای نرم‌افزار یکپارچه فروغ آریا (تک‌سروره + SQLite)" -ForegroundColor Cyan
Write-Host "=====================================================" -ForegroundColor Cyan

# 1) پابلیش کلاینت
Write-Host "`n[1/4] پابلیش کلاینت Blazor WebAssembly..." -ForegroundColor Yellow
dotnet publish src/TotallSoftwareForoughAriya.Client -c Release -o publish/client
if ($LASTEXITCODE -ne 0) { throw "TotallSoftwareForoughAriya client publish failed" }

# 2) کپی در wwwroot
Write-Host "`n[2/4] کپی فایل‌های کلاینت در wwwroot مربوط به API..." -ForegroundColor Yellow
$dest = Join-Path $PSScriptRoot "src/TotallSoftwareForoughAriya.Api/wwwroot"
New-Item -ItemType Directory -Path $dest -Force | Out-Null
$keep = @('README.md', 'uploads', 'SecureFiles')
Get-ChildItem $dest -Force | Where-Object { $keep -notcontains $_.Name } | Remove-Item -Recurse -Force
Copy-Item -Path (Join-Path $PSScriptRoot "publish/client/wwwroot/*") -Destination $dest -Recurse -Force

# 3) تنظیم SQLite
Write-Host "`n[3/4] تنظیم SQLite به‌جای SQL Server..." -ForegroundColor Yellow
$env:Database__Provider = "Sqlite"
$env:ConnectionStrings__Default = "Data Source=TotallSoftwareForoughAriya.db"
$env:ASPNETCORE_ENVIRONMENT = "Production"

# 4) اجرای API
Write-Host "`n[4/4] اجرای API روی http://localhost:5100" -ForegroundColor Yellow
Write-Host "" -ForegroundColor Green
Write-Host "  در مرورگر باز کنید:  http://localhost:5100" -ForegroundColor Green
Write-Host "  ورود پیش‌فرض:        admin / admin" -ForegroundColor Green
Write-Host "  (برای توقف: Ctrl+C)" -ForegroundColor Green
Write-Host ""
Set-Location (Join-Path $PSScriptRoot "src/TotallSoftwareForoughAriya.Api")
dotnet run
