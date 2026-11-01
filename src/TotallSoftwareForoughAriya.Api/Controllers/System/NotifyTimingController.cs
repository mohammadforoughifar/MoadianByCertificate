using Inventory.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers;

/// <summary>
/// مدت نمایش سراسری اعلان‌ها/پیام‌ها — فقط‌خواندنی و برای «همهٔ کاربرانِ واردشده».
/// جدا از SettingsController نگه داشته شده تا:
///   • برخلاف GET /api/settings (که محدود به Admin/Operator است و توکن‌های حساس بله/ایتا را هم برمی‌گرداند)،
///     هر کاربر بتواند فقط دو عددِ مدت نمایش را بگیرد، بدون افشای هیچ اطلاعات حساسی.
/// تعیین مقدار همچنان فقط توسط مدیر سامانه از POST /api/settings انجام می‌شود.
/// </summary>
[ApiController]
[Route("api/settings")]
[Authorize]
public class NotifyTimingController : ControllerBase
{
    private readonly IInventoryService _service;

    public NotifyTimingController(IInventoryService service) => _service = service;

    /// <summary>مدت نمایش اعلان زنده و پیام کوتاه (میلی‌ثانیه). ۰ = تا بستن دستی.</summary>
    [HttpGet("notify-timing")]
    public async Task<ActionResult<NotifyTimingDto>> Get()
    {
        var s = await _service.GetSettingsAsync();
        return Ok(new NotifyTimingDto { BannerMs = s.NotifyBannerMs, ToastMs = s.NotifyToastMs });
    }

    public class NotifyTimingDto
    {
        public int BannerMs { get; set; }
        public int ToastMs { get; set; }
    }
}
