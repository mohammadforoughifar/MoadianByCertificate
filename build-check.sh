#!/usr/bin/env bash
# بیلد تأییدی در محیط کم‌رم — تک‌هسته، بدون آنالایزر (فقط برای صحت‌سنجی کامپایل)
# استفاده: ./build-check.sh [Api|Client|Shared|All]
set -e
export PATH=/var/tmp/dotnet:$PATH
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_gcServer=0 DOTNET_GCHeapHardLimit=0x30000000 MSBUILDDISABLENODEREUSE=1
cd "$(dirname "$0")"
TARGET="${1:-All}"
FLAGS=(-c Debug -m:1 -p:MaxCpuCount=1 -p:RunAnalyzers=false -p:EnableNETAnalyzers=false)
case "$TARGET" in
  Shared) dotnet build src/Inventory.Shared/Inventory.Shared.csproj --no-restore "${FLAGS[@]}";;
  Api)    dotnet build src/Inventory.Api/Inventory.Api.csproj --no-restore "${FLAGS[@]}";;
  Client) dotnet build src/Inventory.Client/Inventory.Client.csproj --no-restore "${FLAGS[@]}";;
  All)
    dotnet build src/Inventory.Api/Inventory.Api.csproj --no-restore "${FLAGS[@]}"
    dotnet build src/Inventory.Client/Inventory.Client.csproj --no-restore "${FLAGS[@]}"
    ;;
esac
