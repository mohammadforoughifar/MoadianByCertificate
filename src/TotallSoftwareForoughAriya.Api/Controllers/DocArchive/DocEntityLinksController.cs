using Inventory.Api.Data;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers.DocArchive;

/// <summary>
/// Retired ERP integration routes. Return an explicit 410 even to managers.
/// Do not read, create, update or delete historical DocEntityLinks or documents.
/// Keeping route tombstones prevents old clients from mutating retained data.
/// </summary>
[Route("api/doc-archive/entity-links")]
public class DocEntityLinksController(AppDbContext db) : RbacControllerBase(db)
{
    private Task<IActionResult> Disabled() => Task.FromResult<IActionResult>(StatusCode(410, new
    {
        code = "DOC_ARCHIVE_ERP_DISABLED",
        message = "اتصال آرشیو به سامانه ERP غیرفعال شده است."
    }));

    [HttpGet("{module}/{entityId:int}")]
    public Task<IActionResult> GetLinkedDocuments(string module, int entityId) => Disabled();

    [HttpPost]
    public Task<IActionResult> AddLink([FromBody] DocEntityLinkSaveDto dto) => Disabled();

    [HttpDelete("{id:int}")]
    public Task<IActionResult> RemoveLink(int id) => Disabled();

    [HttpPost("quick-create")]
    public Task<IActionResult> QuickCreateLinked([FromBody] DocQuickCreateLinkedDto dto) => Disabled();

    [HttpGet("/api/doc-archive/entity-lookups/{module}")]
    public Task<IActionResult> SearchEntities(string module, [FromQuery] string? q = null) => Disabled();
}
