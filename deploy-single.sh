#!/usr/bin/env bash
# استقرار تک‌سروره: پابلیش کلاینت Blazor + پابلیش سبک API + بسته فشرده قابل انتقال
# مصرف: ./deploy-single.sh [RID]   مثل: ./deploy-single.sh linux-x64  (پیش‌فرض: linux-x64)
# خروجی نهایی: publish/app  (تک‌فایل فشرده + فایل‌های استاتیک؛ نیازمند ران‌تایم ‎.NET 8‎ روی سرور)
#           و: publish/app.tar.gz  (بسته قابل انتقال به سرور)
set -e
cd "$(dirname "$0")"
RID="${1:-linux-x64}"

echo "► [1/4] پابلیش کلاینت Blazor WebAssembly…"
dotnet publish src/Inventory.Client -c Release -o publish/client

echo "► [2/4] کپی فایل‌های استاتیک در wwwroot مربوط به API…"
mkdir -p src/Inventory.Api/wwwroot
# uploads و SecureFiles فایل‌های واقعی کاربران‌اند (مدارک آرشیو، پیوست‌ها)، نه خروجی بیلد؛ نباید پاک شوند.
find src/Inventory.Api/wwwroot -mindepth 1 -maxdepth 1 ! -name 'README.md' ! -name 'uploads' ! -name 'SecureFiles' -exec rm -rf {} +
cp -r publish/client/wwwroot/. src/Inventory.Api/wwwroot/
rm -rf publish/client  # حذف خروجی میانی (کپی آن داخل publish/app هست)

echo "► [3/4] پابلیش سبک API (فقط $RID، بدون ران‌تایم اضافه)…"
dotnet publish src/Inventory.Api/Inventory.Api.csproj -c Release -r "$RID" --self-contained false -o publish/app
rm -rf publish/app/wwwroot/SecureFiles publish/app/wwwroot/uploads

echo "► [4/4] ساخت بسته فشرده قابل انتقال به سرور…"
rm -f publish/app.tar.gz
tar -czf publish/app.tar.gz -C publish app

echo ""
echo "✔ تمام شد. حجم خروجی:"
du -sh publish/app publish/app.tar.gz
echo ""
echo "انتقال به سرور: فایل publish/app.tar.gz را منتقل و باز کنید:"
echo "    tar -xzf app.tar.gz && cd app && ./Inventory.Api  (یا: dotnet Inventory.Api.dll)"
echo "سپس در مرورگر باز کنید:  http://localhost:5100"
