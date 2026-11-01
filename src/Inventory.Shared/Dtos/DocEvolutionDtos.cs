namespace Inventory.Shared.Dtos;
public class DocSearchPageDto
{
    public List<DocumentListDto> Items { get; set; } = new();
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}
public class DocArchiveStatsDto
{
    public int Inactive { get; set; }
    public int Deleted { get; set; }
    public int Expired { get; set; }
    public int Expiring { get; set; }
}
public class DocTemporaryGrantDto
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string UserName { get; set; } = "";
    public bool CanDownload { get; set; }
    public DateTime ExpiresAtUtc { get; set; } = DateTime.UtcNow.AddDays(7);
    public DateTime? RevokedAtUtc { get; set; }
}
public class DocRenewalPolicyDto
{
    public bool Enabled { get; set; }
    public int AssigneeUserId { get; set; }
    public int LeadDays { get; set; } = 30;
    public List<DocRenewalOrderDto> Orders { get; set; } = new();
}
public class DocRenewalOrderDto
{
    public int Id { get; set; }
    public string Number { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTime ExpiryDate { get; set; }
}
public class DocIndexQueueDto
{
    public int Pending { get; set; }
    public int Working { get; set; }
    public int Failed { get; set; }
    public int Done { get; set; }
    public List<DocIndexFailureDto> Failures { get; set; } = new();
}
public class DocIndexFailureDto
{
    public int AttachmentId { get; set; }
    public string FileName { get; set; } = "";
    public string? Error { get; set; }
}
public class DocContentCompareRequest
{
    public int LeftAttachmentId { get; set; }
    public int RightAttachmentId { get; set; }
}
public class DocContentCompareDto
{
    public string LeftName { get; set; } = "";
    public string RightName { get; set; } = "";
    public string Mode { get; set; } = "Text";
    public List<DocVersionDiffRow> Rows { get; set; } = new();
    public string? Notice { get; set; }
    public bool Truncated { get; set; }
}
