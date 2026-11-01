using Inventory.Api.Services.Cartable;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers;

/// <summary>
/// کارتابل یکپارچهٔ کاربر جاری.
/// <para>
/// یک endpoint واحد که همهٔ منابع کارتابل (ویزیتوری، و به‌تدریج بقیهٔ ماژول‌ها) را
/// تجمیع می‌کند. تفکیک «منتظر اقدام» و «اطلاع» با فیلد <c>Actionable</c> انجام می‌شود
/// و خطای یک منبع، بقیهٔ کارتابل را از کار نمی‌اندازد.
/// </para>
/// </summary>
[ApiController]
[Authorize]
[Route("api/cartable")]
public class CartableController : ControllerBase
{
    private readonly ICartableService _cartable;

    public CartableController(ICartableService cartable) => _cartable = cartable;

    /// <summary>همهٔ آیتم‌های کارتابل کاربر جاری با شمارنده‌ها.</summary>
    [HttpGet("me")]
    public async Task<ActionResult<CartableResponseDto>> Me(CancellationToken ct)
        => Ok(await _cartable.GetMineAsync(ct));

    /// <summary>فقط تعداد موارد «منتظر اقدام» — برای badge منو و زنگ سربرگ.</summary>
    [HttpGet("me/count")]
    public async Task<ActionResult<object>> Count(CancellationToken ct)
        => Ok(new { actionable = await _cartable.CountActionableAsync(ct) });
}
