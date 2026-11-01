این پوشه در زمان **انتشار** پر می‌شود و نباید در مخزن نگهداری شود.

خروجی کلاینت اینجا کپی می‌شود:

```bash
dotnet publish src/TotallSoftwareForoughAriya.Client/TotallSoftwareForoughAriya.Client.csproj -c Release -o publish/client
cp -rf publish/client/wwwroot/* src/TotallSoftwareForoughAriya.Api/wwwroot/
```

در زمان اجرا نیز پوشه‌های `uploads/` و `SecureFiles/` به‌صورت خودکار ساخته می‌شوند.
