این پوشه در زمان **انتشار** پر می‌شود و نباید در مخزن نگهداری شود.

طبق `DEPLOY.md` خروجی کلاینت اینجا کپی می‌شود:

```bash
dotnet publish inventory/src/Inventory.Client/Inventory.Client.csproj -c Release -o publish/client
cp -rf publish/client/wwwroot/* inventory/src/Inventory.Api/wwwroot/
```

در زمان اجرا نیز پوشه‌های `uploads/` و `SecureFiles/` به‌صورت خودکار ساخته می‌شوند.
