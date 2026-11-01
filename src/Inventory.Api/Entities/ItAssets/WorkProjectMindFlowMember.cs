namespace Inventory.Api.Data;

/// <summary>کاربری که فقط برای مشاهده به MindFlow یک پروژه دعوت شده است.</summary>
public sealed class WorkProjectMindFlowMember
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public int UserId { get; set; }
    public int AddedByUserId { get; set; }
    public DateTime AddedAt { get; set; } = DateTime.Now;
    public WorkProject? Project { get; set; }
}
