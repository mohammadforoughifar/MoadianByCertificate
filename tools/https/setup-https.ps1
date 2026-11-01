<#
.SYNOPSIS
    راه‌اندازی HTTPS سامانه روی سرور ویندوز — هم برای داخل شبکه و هم برای بیرون.

.DESCRIPTION
    این اسکریپت را یک‌بار روی سرور (با PowerShell «Run as Administrator») اجرا کنید:

      1) آی‌پی عمومی و/یا دامنه را به گواهی داخلی سامانه اضافه می‌کند (Https__Hosts).
      2) پورت‌های لازم را در فایروال ویندوز باز می‌کند (HTTP، HTTPS و در صورت نیاز 443 و 80).
      3) (اختیاری -LetsEncrypt) برای دامنهٔ واقعی، گواهی رایگان و معتبر Let's Encrypt با win-acme می‌گیرد.
         این گواهی روی همهٔ گوشی‌ها بدون نصب هیچ فایلی معتبر است و هر ~۶۰ روز خودکار تمدید می‌شود؛
         سامانه فایل تمدیدشده را بدون ری‌استارت دوباره می‌خواند.
      4) (اختیاری -Use443) HTTPS را روی پورت 443 هم بالا می‌آورد تا نشانی بیرونی بدون شماره پورت باز شود.
      5) (اختیاری -RedirectHttp) بازکردن صفحه با http:// را خودکار به https:// هدایت می‌کند.

    تنظیمات به‌صورت «متغیر محیطی سطح سیستم» ذخیره می‌شوند (Https__...)، پس به محل نصب نرم‌افزار و
    فایل appsettings.json دست نمی‌زند و با به‌روزرسانی نرم‌افزار از بین نمی‌رود.
    پس از اجرا، سرویس/برنامهٔ سامانه را یک‌بار ری‌استارت کنید.

.EXAMPLE
    # فقط شبکهٔ داخلی + دسترسی بیرونی با آی‌پی عمومی (هر دستگاه یک‌بار ca.crt را نصب می‌کند)
    .\setup-https.ps1 -PublicIp 5.6.7.8

.EXAMPLE
    # داخل + بیرون با دامنهٔ واقعی و گواهی معتبر Let's Encrypt روی پورت 443 (پیشنهادی)
    .\setup-https.ps1 -Domain erp.company.ir -LetsEncrypt -Email it@company.ir -Use443 -RedirectHttp

.EXAMPLE
    # آزمایش گرفتن گواهی روی سرور آزمایشی Let's Encrypt (محدودیت تعداد ندارد؛ گواهی معتبر نیست)
    .\setup-https.ps1 -Domain erp.company.ir -LetsEncrypt -Email it@company.ir -Staging

.EXAMPLE
    # برگرداندن تنظیمات (متغیرهای محیطی و قوانین فایروال این اسکریپت حذف می‌شوند؛ گواهی‌ها می‌مانند)
    .\setup-https.ps1 -Undo
#>
[CmdletBinding()]
param(
    [string]$Domain,
    [string]$PublicIp,
    [switch]$LetsEncrypt,
    [string]$Email,
    [switch]$Use443,
    [switch]$RedirectHttp,
    [int]$HttpPort = 5100,
    [int]$HttpsPort = 5443,
    [switch]$Staging,
    [string]$WacsPath,
    [int]$ValidationPort = 80,
    [string]$DataDir = (Join-Path $env:ProgramData 'Totall\https'),
    [string]$ServiceAccount,
    [switch]$Undo
)

$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

function Write-Step($text) { Write-Host "`n==> $text" -ForegroundColor Cyan }
function Write-Ok($text)   { Write-Host "    [OK] $text" -ForegroundColor Green }
function Write-Warn2($text){ Write-Host "    [!]  $text" -ForegroundColor Yellow }
function Write-Info($text) { Write-Host "    $text" }

# ---------------------------------------------------------------- پیش‌نیاز
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) { throw "این اسکریپت را با PowerShell در حالت «Run as Administrator» اجرا کنید." }

$envNames = @('Https__Hosts', 'Https__ExtraPorts', 'Https__PublicPort', 'Https__RedirectHttp', 'Https__PublicCertificate__Path')
$rulePrefix = 'Totall ERP'

function Set-MachineEnv([string]$name, [string]$value) {
    if ([string]::IsNullOrWhiteSpace($value)) {
        [Environment]::SetEnvironmentVariable($name, $null, 'Machine')
    } else {
        [Environment]::SetEnvironmentVariable($name, $value, 'Machine')
        Write-Ok "$name = $value"
    }
}

function Add-FirewallPort([string]$label, [int]$port) {
    $name = "$rulePrefix - $label ($port)"
    Get-NetFirewallRule -DisplayName $name -ErrorAction SilentlyContinue | Remove-NetFirewallRule -ErrorAction SilentlyContinue
    New-NetFirewallRule -DisplayName $name -Direction Inbound -Protocol TCP -LocalPort $port -Action Allow -Profile Any | Out-Null
    Write-Ok "فایروال: پورت $port باز شد ($label)"
}

function Get-PortOwner([int]$port) {
    $conn = Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $conn) { return $null }
    $proc = Get-Process -Id $conn.OwningProcess -ErrorAction SilentlyContinue
    if ($conn.OwningProcess -eq 4) { return 'System (http.sys — معمولاً IIS)' }
    if ($proc) { return "$($proc.ProcessName) (PID $($proc.Id))" }
    return "PID $($conn.OwningProcess)"
}

# ---------------------------------------------------------------- حالت برگرداندن
if ($Undo) {
    Write-Step "حذف تنظیمات HTTPS این اسکریپت"
    foreach ($n in $envNames) { [Environment]::SetEnvironmentVariable($n, $null, 'Machine') }
    Write-Ok "متغیرهای محیطی Https__* حذف شدند (appsettings.json دوباره ملاک است)."
    Get-NetFirewallRule -DisplayName "$rulePrefix - *" -ErrorAction SilentlyContinue | Remove-NetFirewallRule
    Write-Ok "قوانین فایروال «$rulePrefix» حذف شدند."
    Write-Info "گواهی‌ها در $DataDir باقی ماندند. تمدید خودکار win-acme را در صورت نیاز از Task Scheduler حذف کنید."
    Write-Warn2 "سرویس سامانه را ری‌استارت کنید."
    return
}

if ($LetsEncrypt -and [string]::IsNullOrWhiteSpace($Domain)) { throw "برای -LetsEncrypt باید -Domain (مثلاً erp.company.ir) را بدهید. Let's Encrypt برای آی‌پی خالی گواهی نمی‌دهد." }
if ($LetsEncrypt -and [string]::IsNullOrWhiteSpace($Email))  { throw "برای -LetsEncrypt یک -Email بدهید (هشدارهای انقضا به آن ارسال می‌شود)." }
if ($Domain) { $Domain = $Domain.Trim().ToLowerInvariant() -replace '^https?://', '' -replace '/.*$', '' }

# ---------------------------------------------------------------- ۱) نام‌ها و آی‌پی‌ها
Write-Step "۱) نام‌ها/آی‌پی‌های گواهی داخلی"
if (-not $PublicIp) {
    try {
        $detected = (Invoke-RestMethod -Uri 'https://api.ipify.org' -TimeoutSec 8).ToString().Trim()
        Write-Info "آی‌پی عمومی شناسایی‌شدهٔ این سرور: $detected  (برای افزودن آن: -PublicIp $detected)"
    } catch { }
}
$hosts = @()
if ($PublicIp) { $hosts += $PublicIp.Trim() }
if ($Domain)   { $hosts += $Domain }
Set-MachineEnv 'Https__Hosts' ($hosts -join ',')
if (-not $hosts) { Write-Info "چیزی اضافه نشد؛ گواهی داخلی فقط شامل آی‌پی‌های کارت شبکه و نام سرور است." }

# ---------------------------------------------------------------- ۲) پورت‌ها
Write-Step "۲) پورت‌ها و فایروال"
Add-FirewallPort 'HTTP'  $HttpPort
Add-FirewallPort 'HTTPS' $HttpsPort
if ($Use443) {
    $owner = Get-PortOwner 443
    if ($owner -and $owner -notmatch 'Inventory') {
        Write-Warn2 "پورت 443 را «$owner» گرفته است؛ سامانه نمی‌تواند روی 443 گوش دهد."
        Write-Warn2 "یا آن برنامه (مثلاً سایت پیش‌فرض IIS) را متوقف کنید، یا در مودم پورت 443 بیرونی را به $HttpsPort سرور هدایت کنید."
    }
    Add-FirewallPort 'HTTPS-443' 443
    Set-MachineEnv 'Https__ExtraPorts' '443'
    Set-MachineEnv 'Https__PublicPort' '443'
} else {
    Set-MachineEnv 'Https__ExtraPorts' ''
    Set-MachineEnv 'Https__PublicPort' ''
}
if ($RedirectHttp) { Set-MachineEnv 'Https__RedirectHttp' 'true' } else { Set-MachineEnv 'Https__RedirectHttp' '' }

# ---------------------------------------------------------------- ۳) Let's Encrypt
if ($LetsEncrypt) {
    Write-Step "۳) گرفتن گواهی معتبر Let's Encrypt برای $Domain"

    # بررسی DNS
    try {
        $resolved = (Resolve-DnsName -Name $Domain -Type A -ErrorAction Stop | Where-Object { $_.IPAddress } | Select-Object -ExpandProperty IPAddress) -join ', '
        Write-Info "DNS دامنه: $Domain → $resolved"
        if ($PublicIp -and ($resolved -notmatch [regex]::Escape($PublicIp))) {
            Write-Warn2 "رکورد A دامنه به $PublicIp اشاره نمی‌کند؛ ابتدا در پنل دامنه رکورد A را روی آی‌پی عمومی سرور بگذارید."
        }
    } catch { Write-Warn2 "دامنهٔ $Domain resolve نشد. رکورد A را در پنل دامنه روی آی‌پی عمومی سرور تنظیم کنید." }

    $owner80 = Get-PortOwner $ValidationPort
    if ($owner80 -and $owner80 -notmatch 'http.sys') {
        Write-Warn2 "پورت $ValidationPort را «$owner80» گرفته است؛ اعتبارسنجی Let's Encrypt ممکن است ناموفق شود."
    }
    Add-FirewallPort 'ACME-validation' $ValidationPort
    Write-Info "یادآوری: Let's Encrypt از اینترنت به http://$Domain`:80 وصل می‌شود؛ در مودم/فایروال شبکه پورت 80 بیرونی باید به پورت $ValidationPort این سرور هدایت شود."

    # win-acme
    if (-not $WacsPath) {
        $wacsDir = Join-Path $DataDir 'win-acme'
        $WacsPath = Join-Path $wacsDir 'wacs.exe'
        if (-not (Test-Path $WacsPath)) {
            Write-Info "دانلود win-acme از GitHub ..."
            New-Item -ItemType Directory -Force -Path $wacsDir | Out-Null
            $release = Invoke-RestMethod -Uri 'https://api.github.com/repos/win-acme/win-acme/releases/latest' -Headers @{ 'User-Agent' = 'totall-setup' }
            $asset = $release.assets | Where-Object { $_.name -like '*x64.trimmed.zip' } | Select-Object -First 1
            if (-not $asset) { $asset = $release.assets | Where-Object { $_.name -like '*x64*.zip' } | Select-Object -First 1 }
            if (-not $asset) { throw "فایل win-acme پیدا نشد. آن را دستی از https://www.win-acme.com دانلود و با -WacsPath مسیر wacs.exe را بدهید." }
            $zip = Join-Path $env:TEMP $asset.name
            Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $zip -UseBasicParsing
            Expand-Archive -Path $zip -DestinationPath $wacsDir -Force
            Remove-Item $zip -Force -ErrorAction SilentlyContinue
        }
    }
    if (-not (Test-Path $WacsPath)) { throw "wacs.exe در «$WacsPath» پیدا نشد." }
    Write-Ok "win-acme: $WacsPath"

    # محل و رمز فایل گواهی عمومی (رمز قبلی حفظ می‌شود چون تمدید خودکار با همان رمز فایل را می‌نویسد)
    $pubDir = Join-Path $DataDir 'public'
    New-Item -ItemType Directory -Force -Path $pubDir | Out-Null
    $pwdFile = Join-Path $pubDir 'public.pfx.pwd'
    if (Test-Path $pwdFile) {
        $pfxPassword = (Get-Content $pwdFile -Raw).Trim()
    } else {
        $chars = [char[]]'ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789'
        $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
        $bytes = New-Object byte[] 32; $rng.GetBytes($bytes)
        $pfxPassword = -join ($bytes | ForEach-Object { $chars[$_ % $chars.Length] })
        Set-Content -Path $pwdFile -Value $pfxPassword -NoNewline -Encoding ASCII
    }

    $wacsArgs = @(
        '--source', 'manual', '--host', $Domain, '--friendlyname', "Totall ERP $Domain",
        '--validation', 'selfhosting', '--validationport', "$ValidationPort",
        '--store', 'pfxfile', '--pfxfilepath', $pubDir, '--pfxfilename', 'public', '--pfxpassword', $pfxPassword,
        '--accepttos', '--emailaddress', $Email
    )
    if ($Staging) { $wacsArgs += @('--baseuri', 'https://acme-staging-v02.api.letsencrypt.org/') }

    Write-Info "اجرای win-acme (چند ثانیه تا یک دقیقه) ..."
    & $WacsPath @wacsArgs
    if ($LASTEXITCODE -ne 0) { throw "win-acme با خطا تمام شد (کد $LASTEXITCODE). پیام‌های بالا را بررسی کنید: DNS دامنه، باز بودن پورت 80 از اینترنت." }

    $pfx = Join-Path $pubDir 'public.pfx'
    if (-not (Test-Path $pfx)) {
        $found = Get-ChildItem $pubDir -Filter *.pfx | Sort-Object LastWriteTime -Descending | Select-Object -First 1
        if (-not $found) { throw "فایل pfx در $pubDir ساخته نشد." }
        $pfx = $found.FullName
        Set-MachineEnv 'Https__PublicCertificate__Path' $pfx
    } else {
        Set-MachineEnv 'Https__PublicCertificate__Path' ''
    }
    Write-Ok "گواهی عمومی ذخیره شد: $pfx"
    Write-Ok "تمدید خودکار: win-acme یک Scheduled Task روزانه ساخته است (Task Scheduler → win-acme renew)."

    # محدود کردن دسترسی به کلید خصوصی
    $grants = @('*S-1-5-32-544:(OI)(CI)F', '*S-1-5-18:(OI)(CI)F', "$($env:USERNAME):(OI)(CI)R")
    if ($ServiceAccount) { $grants += "$($ServiceAccount):(OI)(CI)R" }
    $icaclsArgs = @($pubDir, '/inheritance:r') + ($grants | ForEach-Object { @('/grant:r', $_) })
    & icacls @icaclsArgs | Out-Null
    Write-Ok "دسترسی پوشهٔ گواهی به Administrators، SYSTEM و $($env:USERNAME)$(if ($ServiceAccount) { " و $ServiceAccount" }) محدود شد."
    if (-not $ServiceAccount) { Write-Info "اگر سامانه با کاربر دیگری اجرا می‌شود، اسکریپت را با -ServiceAccount <نام‌کاربر> دوباره اجرا کنید." }
}

# ---------------------------------------------------------------- خلاصه
Write-Step "انجام شد — سامانه را یک‌بار ری‌استارت کنید"
$svc = Get-Service -ErrorAction SilentlyContinue | Where-Object { $_.Name -match 'Totall|Inventory' -or $_.DisplayName -match 'Totall|Inventory' }
if ($svc) {
    foreach ($s in $svc) { Write-Info "سرویس پیدا شد: $($s.Name) — برای اعمال:  Restart-Service '$($s.Name)'" }
} else {
    Write-Info "پنجرهٔ برنامه (Inventory.Api.exe / dotnet) را ببندید و دوباره اجرا کنید. متغیرهای محیطی فقط در پنجره/فرایند جدید دیده می‌شوند."
}

$lanIps = Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue |
    Where-Object { $_.IPAddress -notlike '127.*' -and $_.IPAddress -notlike '169.254.*' } | Select-Object -ExpandProperty IPAddress

Write-Host ""
Write-Host "  داخل شبکه (یک‌بار ca.crt روی هر دستگاه نصب شود):" -ForegroundColor White
foreach ($ip in $lanIps) {
    Write-Host "     نصب گواهی:  http://$ip`:$HttpPort/https-setup"
    Write-Host "     نشانی امن:  https://$ip`:$HttpsPort" -ForegroundColor Green
}
if ($PublicIp) {
    Write-Host "  بیرون با آی‌پی عمومی (ca.crt لازم است؛ مودم: پورت $HttpsPort → این سرور):" -ForegroundColor White
    Write-Host "     https://$PublicIp`:$HttpsPort" -ForegroundColor Green
}
if ($Domain) {
    $portPart = if ($Use443) { '' } else { ":$HttpsPort" }
    $note = if ($LetsEncrypt -and -not $Staging) { 'بدون نیاز به نصب گواهی' } else { 'ca.crt لازم است' }
    Write-Host "  بیرون با دامنه ($note):" -ForegroundColor White
    Write-Host "     https://$Domain$portPart" -ForegroundColor Green
}
Write-Host ""
Write-Host "  پس از ری‌استارت در کنسول سامانه باید «[HTTPS] گواهی عمومی بارگذاری شد ...» و «... در دسترس است» را ببینید." -ForegroundColor DarkGray
Write-Host "  سپس: تنظیمات ← «اعلان گوشی و مرورگر» ← فعال‌سازی اعلان این دستگاه." -ForegroundColor DarkGray
