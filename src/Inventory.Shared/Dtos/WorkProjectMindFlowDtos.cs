using System.Text.Json.Serialization;

namespace Inventory.Shared.Dtos;

/// <summary>MindFlow document used by a work-project map or a standalone Inventory map (ProjectId=0).</summary>
public sealed class WorkProjectMindFlowDto
{
    public int ProjectId { get; set; }
    public string Title { get; set; } = "";
    public List<WorkProjectMindFlowNodeDto> Nodes { get; set; } = new();
}

public sealed class WorkProjectMindFlowNodeDto
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? ParentId { get; set; }
    public string Text { get; set; } = "موضوع جدید";
    public double X { get; set; }
    public double Y { get; set; }
    public string Color { get; set; } = "#596cf3";
    /// <summary>Optional per-node dimensions; null preserves the legacy automatic size.</summary>
    public double? Width { get; set; }
    public double? Height { get; set; }
    public string Note { get; set; } = "";
    public bool Collapsed { get; set; }

    /// <summary>Existing work orders attached to this node; checklist state stays authoritative in WorkOrders.</summary>
    public List<int> WorkOrderIds { get; set; } = new();

    /// <summary>Legacy single-order property, read to migrate previously saved maps.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? WorkOrderId { get; set; }

    /// <summary>New nested MindFlow stored within the parent map.</summary>
    public WorkProjectMindFlowDto? ChildMindFlow { get; set; }

    /// <summary>Optional existing MindFlow from another accessible work project.</summary>
    public int? LinkedMindFlowProjectId { get; set; }
}

/// <summary>Small project summary visible only to users who can access its MindFlow.</summary>
public sealed class WorkProjectMindFlowAccessibleProjectDto
{
    public int ProjectId { get; set; }
    public string Number { get; set; } = "";
    public string Title { get; set; } = "";
    public string Status { get; set; } = "Open";
    public bool CanManage { get; set; }
}

public sealed class WorkProjectMindFlowMemberDto
{
    public int UserId { get; set; }
    public string Name { get; set; } = "";
    public DateTime AddedAt { get; set; }
    public string Role { get; set; } = "Viewer";
}

public sealed class WorkProjectMindFlowUserDto
{
    public int UserId { get; set; }
    public string Name { get; set; } = "";
}

public sealed class AddWorkProjectMindFlowMembersDto
{
    public List<int> UserIds { get; set; } = new();
    public string Role { get; set; } = "Viewer";
}

/// <summary>WorkOrder connected to a node anywhere in a project's MindFlow tree.</summary>
public sealed class WorkProjectMindFlowLinkedOrderDto
{
    public int Id { get; set; }
    public string Number { get; set; } = "";
    public string Title { get; set; } = "";
    public string OwnerName { get; set; } = "";
    public string Status { get; set; } = "Open";
    public DateTime DueAt { get; set; }
    public int Priority { get; set; }
    public int Weight { get; set; }
    public int? ProjectId { get; set; }
    public string? ProjectNumber { get; set; }
    public string? ProjectTitle { get; set; }
    public Guid NodeId { get; set; }
    public string NodeTitle { get; set; } = "";
    public string SubFlowPath { get; set; } = "";
}

public sealed class CreateStandaloneMindFlowDto
{
    public string Title { get; set; } = "";
}

public sealed class StandaloneMindFlowSummaryDto
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public int OwnerUserId { get; set; }
    public string OwnerName { get; set; } = "";
    public string Role { get; set; } = "Owner";
    public bool CanEdit { get; set; }
    public bool CanManageAccess { get; set; }
    public bool IsOwner { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class WorkOrderMindFlowLinkDto
{
    public int ProjectId { get; set; }
    public int? StandaloneMindFlowId { get; set; }
    public string ProjectNumber { get; set; } = "";
    public string ProjectTitle { get; set; } = "";
    public string MapTitle { get; set; } = "";
    public Guid NodeId { get; set; }
    public string NodeText { get; set; } = "";
    public List<Guid> SubFlowPath { get; set; } = new();
}
