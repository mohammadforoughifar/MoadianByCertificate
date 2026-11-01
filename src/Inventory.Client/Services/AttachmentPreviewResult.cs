namespace Inventory.Client.Services;

/// <summary>Structured browser/API result: never turn a 401/403/HTML error into a file.</summary>
public sealed class AttachmentPreviewResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public string? Code { get; set; }
    public int Status { get; set; }
    public int DocumentId { get; set; }
    public string? Url { get; set; }
    public string? Text { get; set; }
    public string? Mime { get; set; }
    public int Pages { get; set; }
}
