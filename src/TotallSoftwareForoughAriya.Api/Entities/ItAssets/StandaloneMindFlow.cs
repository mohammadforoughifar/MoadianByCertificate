using System.ComponentModel.DataAnnotations;

namespace Inventory.Api.Data;

/// <summary>A standalone MindFlow owned by one user inside the Inventory company database.</summary>
public sealed class StandaloneMindFlow
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public int OwnerUserId { get; set; }
    [MaxLength(150)] public string OwnerName { get; set; } = "";
    [MaxLength(200)] public string Title { get; set; } = "";
    public string MapJson { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    public ICollection<StandaloneMindFlowMember> Members { get; set; } = new List<StandaloneMindFlowMember>();
}

/// <summary>A role-based share on one standalone MindFlow: Viewer or Editor.</summary>
public sealed class StandaloneMindFlowMember
{
    public int Id { get; set; }
    public int MindFlowId { get; set; }
    public int UserId { get; set; }
    [MaxLength(20)] public string Role { get; set; } = "Viewer";
    public int AddedByUserId { get; set; }
    public DateTime AddedAt { get; set; } = DateTime.Now;
    public StandaloneMindFlow? MindFlow { get; set; }
}
