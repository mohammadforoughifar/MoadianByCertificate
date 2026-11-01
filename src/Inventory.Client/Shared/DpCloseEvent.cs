using Microsoft.AspNetCore.Components;

namespace Inventory.Client.Shared;

/// <summary>رویداد سفارشیِ بستن پنجره‌ی DatePicker با کلیک بیرون (dispatch از جاوااسکریپت).</summary>
[EventHandler("dpclose", typeof(EventArgs), false, false)]
public static class DpCloseEvent { }
