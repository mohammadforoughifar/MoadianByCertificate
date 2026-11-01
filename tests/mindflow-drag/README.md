# تست رفتار کشیدن گره‌های «نگار ذهنی»

این تست بدون نیاز به .NET و بدون مرورگر، فایل
`src/TotallSoftwareForoughAriya.Client/wwwroot/js/inventory-workproject-mindflow.js`
را در یک DOM ساختگی (jsdom) اجرا می‌کند و رفتارهای زیر را بررسی می‌کند:

1. کشیدن یک گره، کل زیرشاخه را با همان اختلاف جابه‌جا می‌کند و `CommitDrag` را با
   مختصات درست و `targetParentId = null` صدا می‌زند.
2. خطوط (`.wpmf-link`) در حین کشیدن به‌روز می‌شوند.
3. رهاکردن گره روی گرهٔ دیگر، شناسهٔ مقصد را به‌عنوان `targetParentId` می‌فرستد و
   کلاس `drop-target` پاک می‌شود.
4. کلیک بدون جابه‌جایی، `SelectNodeFromCanvas` را صدا می‌زند (نه `CommitDrag`).
5. روی نقشهٔ فقط‌خواندنی (`data-readonly="true"`) هیچ رویدادی به .NET نمی‌رود.
6. `pointercancel` موقعیت‌های زنده را به حالت قبل برمی‌گرداند.
7. `detachDrag` کنترلر را پاک می‌کند و `attachDrag` دوباره قابل اجراست.

اجرا:

```bash
cd tests/mindflow-drag
npm install jsdom@24 --no-save
node mindflow-drag.test.js
```

خروجی موفق: `ALL MIND-FLOW DRAG TESTS PASSED`
