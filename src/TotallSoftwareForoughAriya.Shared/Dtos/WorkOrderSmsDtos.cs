namespace Inventory.Shared.Dtos;

/// <summary>Acceptance by the SMS gateway is not a delivery receipt.</summary>
public class WorkOrderSmsResult
{
    public List<WorkOrderSmsRecipientResult> Recipients { get; set; } = new();
}

public class WorkOrderSmsRecipientResult
{
    public int UserId { get; set; }
    public string Name { get; set; } = "";
    // accepted | failed | skipped | unknown. Never expose mobile numbers or the API key.
    public string State { get; set; } = "";
    public string Message { get; set; } = "";
}
