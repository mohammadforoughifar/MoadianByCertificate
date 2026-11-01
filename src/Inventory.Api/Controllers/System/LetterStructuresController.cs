using Inventory.Api.Data;
using Inventory.Api.Services;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers;

/// <summary>
/// تنظیمات ساختار شماره نامه (شماره اندیکاتور) — اتوماسیون اداری.
/// برای هر سه نوع نامه (داخلی/صادره/وارده) ترتیب اجزای «واحد | شماره | سال» تعیین می‌شود.
/// ماژول دسترسی: LetterStructures (Read = مشاهده، Update = تغییر ساختار)
/// </summary>
[Route("api/letter-structures")]
public class LetterStructuresController : RbacControllerBase
{
    private const string Module = "LetterStructures";

    private readonly ILetterStratureService _strature;
    private readonly IOrganizationServices _org;

    public LetterStructuresController(AppDbContext db, ILetterStratureService strature, IOrganizationServices org)
        : base(db)
    {
        _strature = strature;
        _org = org;
    }

    private static readonly string[] FormTitles = { "", "نامه داخلی", "نامه صادره", "نامه وارده" };

    /// <summary>داده‌ی صفحه‌ی تنظیمات: ساختار هر سه نوع + سازمان‌ها</summary>
    [HttpGet]
    public async Task<IActionResult> GetPage()
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;

        var items = new List<LetterStructureItemDto>();
        for (var typeForm = 1; typeForm <= 3; typeForm++)
        {
            items.Add(new LetterStructureItemDto
            {
                TypeForm = typeForm,
                Title = FormTitles.ElementAtOrDefault(typeForm) ?? "",
                Parts = await _strature.GetStructureAsync(typeForm)
            });
        }

        return Ok(new LetterStructurePageDto
        {
            Items = items,
            Organizations = await _org.GetOrganizationsAsync(),
            DefaultOrganizationId = await _org.GetDefaultOrganizationIdAsync()
        });
    }

    /// <summary>ساختار یک نوع نامه</summary>
    [HttpGet("{typeForm:int}")]
    public async Task<IActionResult> Get(int typeForm)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        if (typeForm < 1 || typeForm > 3)
            return BadRequest(new { message = "نوع نامه نامعتبر است (1=داخلی، 2=صادره، 3=وارده)." });

        return Ok(new LetterStructureItemDto
        {
            TypeForm = typeForm,
            Title = FormTitles.ElementAtOrDefault(typeForm) ?? "",
            Parts = await _strature.GetStructureAsync(typeForm)
        });
    }

    /// <summary>جایگزینی ساختار یک نوع نامه — فقط دارنده‌ی مجوز Update (مدیر سیستم)</summary>
    [HttpPut("{typeForm:int}")]
    public async Task<IActionResult> Set(int typeForm, [FromBody] UpdateLetterStructureDto dto)
    {
        if (await ForbiddenUnlessAsync(Module, "Update") is { } forbid) return forbid;
        if (typeForm < 1 || typeForm > 3)
            return BadRequest(new { message = "نوع نامه نامعتبر است (1=داخلی، 2=صادره، 3=وارده)." });

        await _strature.SetStructureAsync(typeForm, dto?.Parts ?? new List<string>());

        return Ok(new LetterStructureItemDto
        {
            TypeForm = typeForm,
            Title = FormTitles.ElementAtOrDefault(typeForm) ?? "",
            Parts = await _strature.GetStructureAsync(typeForm)
        });
    }
}
