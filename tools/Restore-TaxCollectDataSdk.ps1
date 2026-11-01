$ErrorActionPreference = 'Stop'

# TaxCollectData.Library 2.0.29 (Certificate JWS + Folder Status + Payment Registration)
# بسته‌ی محلی پیش‌فرض در پوشه packages موجود است؛ این اسکریپت در صورت حذف‌شدن بسته، آن را با بررسی SHA-1 بازگردانی می‌کند.
$packageName = 'TaxCollectData.Library.2.0.29.nupkg'
$expectedBlobSha = '6332ea9700d76b35816198d1e6d3ce73161f9189'
$ref = '514efbc88f988b1c2a107029f58d383d25a02c57'
$uri = "https://api.github.com/repos/mohammadforoughifar/MoadianByCertificate/contents/$packageName`?ref=$ref"
$repoRoot = Split-Path -Parent $PSScriptRoot
$feedPath = Join-Path $repoRoot 'packages'
$packagePath = Join-Path $feedPath $packageName

New-Item -ItemType Directory -Path $feedPath -Force | Out-Null

$response = Invoke-RestMethod -Uri $uri -Headers @{
    Accept = 'application/vnd.github+json'
    'X-GitHub-Api-Version' = '2022-11-28'
    'User-Agent' = 'MoadianByCertificate-sdk-bootstrap'
}
if ($response.name -ne $packageName -or $response.sha -ne $expectedBlobSha) {
    throw 'The SDK package identity or GitHub blob hash did not match the pinned artifact.'
}

$encodedContent = ($response.content -replace '\s', '')
$bytes = [Convert]::FromBase64String($encodedContent)
$prefix = [Text.Encoding]::ASCII.GetBytes("blob $($bytes.Length)`0")
$blob = New-Object byte[] ($prefix.Length + $bytes.Length)
[Array]::Copy($prefix, 0, $blob, 0, $prefix.Length)
[Array]::Copy($bytes, 0, $blob, $prefix.Length, $bytes.Length)
$sha1 = [Security.Cryptography.SHA1]::Create()
try {
    $actualBlobSha = ([BitConverter]::ToString($sha1.ComputeHash($blob))).Replace('-', '').ToLowerInvariant()
}
finally {
    $sha1.Dispose()
}
if ($actualBlobSha -ne $expectedBlobSha) {
    throw "Downloaded SDK package failed its integrity check. Expected $expectedBlobSha; got $actualBlobSha."
}

[IO.File]::WriteAllBytes($packagePath, $bytes)
Write-Host "Restored verified SDK 2.0.29 package to $packagePath"
Write-Host 'Now run: dotnet restore .\Inventory.sln'
