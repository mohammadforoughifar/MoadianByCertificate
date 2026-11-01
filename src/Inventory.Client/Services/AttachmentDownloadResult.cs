namespace Inventory.Client.Services;

/// <summary>Result of the authenticated browser download; errors are shown in the current page.</summary>
public sealed class AttachmentDownloadResult
{
    public bool Success { get; set; }
    public string? Code { get; set; }
    public int DocumentId { get; set; }
    public string? Message { get; set; }
}
