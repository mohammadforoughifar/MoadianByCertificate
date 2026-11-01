# deploy-single.ps1
# استقرار تک‌سروره در ویندوز: پابلیش کلاینت Blazor + پابلیش سبک API
# مصرف: .\deploy-single.ps1 [-Rid win-x64]
# خروجی نهایی: publish\app (نیازمند ران‌تایم .NET 8 روی سرور)
param([string]$Rid = "win-x64")
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

Write-Host "==> [1/3] Publishing Blazor client..." -ForegroundColor Cyan
dotnet publish src/Inventory.Client -c Release -o publish/client
if ($LASTEXITCODE -ne 0) { throw "Inventory client publish failed" }

Write-Host "==> [2/3] Copying static files into src/Inventory.Api/wwwroot ..." -ForegroundColor Cyan
$dest = Join-Path $PSScriptRoot "src/Inventory.Api/wwwroot"
New-Item -ItemType Directory -Path $dest -Force | Out-Null
# uploads و SecureFiles فایل‌های واقعی کاربران‌اند (مدارک آرشیو، پیوست‌ها)، نه خروجی بیلد؛ نباید پاک شوند.
$keep = @('README.md', 'uploads', 'SecureFiles')
Get-ChildItem $dest -Force | Where-Object { $keep -notcontains $_.Name } | Remove-Item -Recurse -Force
Copy-Item -Path (Join-Path $PSScriptRoot "publish/client/wwwroot/*") -Destination $dest -Recurse -Force

Write-Host "==> [3/3] Publishing slim API ($Rid, framework-dependent) ..." -ForegroundColor Cyan
dotnet publish src/Inventory.Api/Inventory.Api.csproj -c Release -r $Rid --self-contained false -o publish/app
if ($LASTEXITCODE -ne 0) { throw "Inventory API publish failed" }
Remove-Item (Join-Path $PSScriptRoot "publish/app/wwwroot/SecureFiles") -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path $PSScriptRoot "publish/app/wwwroot/uploads") -Recurse -Force -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "Done!" -ForegroundColor Green
$size = (Get-ChildItem (Join-Path $PSScriptRoot "publish/app") -Recurse | Measure-Object Length -Sum).Sum / 1MB
Write-Host ("Output size: {0:N0} MB" -f $size) -ForegroundColor White
Write-Host "Run on server (.NET 8 runtime required):" -ForegroundColor White
Write-Host "    cd publish\app" -ForegroundColor Yellow
Write-Host "    .\Inventory.Api.exe" -ForegroundColor Yellow
Write-Host "Then open in browser:" -ForegroundColor White
Write-Host "    http://localhost:5100" -ForegroundColor Cyan
