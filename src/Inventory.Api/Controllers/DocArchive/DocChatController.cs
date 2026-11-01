using Inventory.Api.Data;
using Inventory.Api.Services.DocArchive;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers.DocArchive;

/// <summary>گفتگو با سند — پرسش آزاد از متن استخراج‌شدهٔ مدرک (RAG لوکال).</summary>
[ApiController]
[Route("api/doc-archive/documents/{id:int}/chat")]
public class DocChatController : RbacControllerBase
{
    private const string Mod = "DocArchive";
    private readonly IDocChatService _svc;

    public DocChatController(AppDbContext db, IDocChatService svc) : base(db)
    {
        _svc = svc;
    }

    private Task<bool> IsManagerAsync() => HasAsync(Mod, "Manage");

    [HttpPost]
    public async Task<IActionResult> Ask(int id, [FromBody] DocChatRequest req, CancellationToken ct)
    {
        if (await ForbiddenUnlessDocArchiveAsync(Mod, "Read") is { } f) return f;
        if (string.IsNullOrWhiteSpace(req?.Question))
            return BadRequest(new { message = "پرسش را بنویسید." });

        var manager = await IsManagerAsync();
        try
        {
            return Ok(await _svc.AskAsync(MyUserId, manager, id, req!, ct));
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(403, new { message = ex.Message });
        }
    }
}
