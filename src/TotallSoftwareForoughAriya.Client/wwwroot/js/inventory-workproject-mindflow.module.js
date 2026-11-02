/*
 * پوشش ES-module برای «نگار ذهنی».
 *
 * چرا لازم است: بارگذاری با import پویا (IJSObjectReference) فقط وقتی کار می‌کند که
 * ماژول واقعاً export داشته باشد؛ فایل اصلی یک اسکریپت کلاسیک است که فقط متغیر
 * سراسری می‌سازد. با این پوشش، مسیر «import» همیشه تابع attachDrag را می‌دهد و
 * دیگر لازم نیست index.html (که ممکن است از کش سرویس‌ورکر قدیمی باشد) تگ script
 * داشته باشد.
 */
import './inventory-workproject-mindflow.js?v=2';

export function attachDrag(svgId, dotnet) {
  const api = globalThis.inventoryWorkProjectMindFlow;
  if (!api || typeof api.attachDrag !== 'function') {
    throw new Error('inventoryWorkProjectMindFlow not initialised');
  }
  return api.attachDrag(svgId, dotnet) !== false;
}

export function init(svgId, dotnet) {
  return attachDrag(svgId, dotnet);
}

export default { attachDrag, init };
