# پاک‌سازی bin/obj و بیلد کامل — رفع CS0006 (Inventory.Shared.dll not found)
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

Write-Host "حذف پوشه‌های bin و obj..." -ForegroundColor Yellow
Get-ChildItem -Recurse -Directory -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -in @('bin', 'obj') } |
    ForEach-Object { Remove-Item $_.FullName -Recurse -Force -ErrorAction SilentlyContinue }

Write-Host "dotnet restore..." -ForegroundColor Yellow
dotnet restore Inventory.sln
if ($LASTEXITCODE -ne 0) { throw "restore failed" }

Write-Host "بیلد Inventory.Shared..." -ForegroundColor Yellow
dotnet build src/Inventory.Shared/Inventory.Shared.csproj -c Debug --no-restore
if ($LASTEXITCODE -ne 0) { throw "Inventory.Shared build failed" }

Write-Host "بیلد کل Solution..." -ForegroundColor Yellow
dotnet build Inventory.sln -c Debug --no-restore
if ($LASTEXITCODE -ne 0) { throw "solution build failed" }

Write-Host "بیلد با موفقیت تمام شد." -ForegroundColor Green
