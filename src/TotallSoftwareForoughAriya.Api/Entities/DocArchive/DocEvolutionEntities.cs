using System.ComponentModel.DataAnnotations;
namespace Inventory.Api.Data;

public class DocTemporaryGrant
{
    public int Id { get; set; }
    public int DocumentId { get; set; }
    public int UserId { get; set; }
    public bool CanDownload { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public int GrantedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
public class DocRenewalPolicy
{
    [Key] public int DocumentId { get; set; }
    public bool Enabled { get; set; }
    public int AssigneeUserId { get; set; }
    public int LeadDays { get; set; } = 30;
    public int OwnerUserId { get; set; }
}
public class DocRenewalRun
{
    public int Id { get; set; }
    public int DocumentId { get; set; }
    public DateTime ExpiryDate { get; set; }
    public int WorkOrderId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
public class DocIndexJob
{
    [Key] public int AttachmentId { get; set; }
    [MaxLength(20)] public string Status { get; set; } = "Pending";
    public int Generation { get; set; } = 1;
    public int Attempts { get; set; }
    public DateTime NextAttemptAtUtc { get; set; }
    public DateTime? LeaseUntilUtc { get; set; }
    [MaxLength(32)] public string? LeaseToken { get; set; }
    [MaxLength(500)] public string? Error { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
