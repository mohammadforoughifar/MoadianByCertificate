namespace Inventory.Shared.Dtos;

// ==================== FaCom (§۱۵ ارتباطات داخلی) ====================

public class FaComAnnouncementDto
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public int Audience { get; set; }
    public int? OrgUnitId { get; set; }
    public string? OrgUnitName { get; set; }
    public DateTime? PublishFrom { get; set; }
    public DateTime? PublishTo { get; set; }
    public bool IsActive { get; set; }
    public DateTime? NotifiedAt { get; set; }
    public string? CreatedByName { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class FaComAnnouncementSaveDto
{
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public int Audience { get; set; }
    public int? OrgUnitId { get; set; }
    public DateTime? PublishFrom { get; set; }
    public DateTime? PublishTo { get; set; }
    public bool IsActive { get; set; } = true;
}

public class FaComReplyDto
{
    public int Id { get; set; }
    public int TicketId { get; set; }
    public int UserId { get; set; }
    public string UserName { get; set; } = "";
    public string Body { get; set; } = "";
    public bool IsHrReply { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class FaComReplySaveDto
{
    public int TicketId { get; set; }
    public string Body { get; set; } = "";
}

public class FaComTicketDto
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public string? EmployeeName { get; set; }
    public string Subject { get; set; } = "";
    public string Body { get; set; } = "";
    public int Category { get; set; }
    public int Priority { get; set; }
    public int Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public string? ClosedByName { get; set; }
    public int RepliesCount { get; set; }
    public List<FaComReplyDto> Replies { get; set; } = new();
}

public class FaComTicketSaveDto
{
    public string Subject { get; set; } = "";
    public string Body { get; set; } = "";
    public int Category { get; set; }
    public int Priority { get; set; } = 1;
    public int? EmployeeId { get; set; }
}

public class FaComPollOptionDto
{
    public int Id { get; set; }
    public string Text { get; set; } = "";
    public int SortOrder { get; set; }
    public int VotesCount { get; set; }
    public double Percent { get; set; }
}

public class FaComPollDto
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public int Audience { get; set; }
    public int? OrgUnitId { get; set; }
    public string? OrgUnitName { get; set; }
    public bool IsActive { get; set; }
    public DateTime? CloseAt { get; set; }
    public string? CreatedByName { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<FaComPollOptionDto> Options { get; set; } = new();
    public int TotalVotes { get; set; }
    public int? MyOptionId { get; set; }
    public bool IsOpen { get; set; }
}

public class FaComPollSaveDto
{
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public int Audience { get; set; }
    public int? OrgUnitId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? CloseAt { get; set; }
    public List<string> Options { get; set; } = new();
}

// ==================== صندوق پیشنهادها ====================

public class FaComSuggestionDto
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    /// <summary>0=پیشنهاد، 1=انتقاد، 2=سایر</summary>
    public int Category { get; set; }
    /// <summary>0=جدید، 1=در بررسی، 2=پذیرفته، 3=ردشده، 4=اجراشده</summary>
    public int Status { get; set; }
    public bool IsAnonymous { get; set; }
    /// <summary>نام ثبت‌کننده (اگر ناشناس: «ناشناس»)</summary>
    public string? EmployeeName { get; set; }
    public string? Response { get; set; }
    public string? RespondedByName { get; set; }
    public DateTime? RespondedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class FaComSuggestionSaveDto
{
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public int Category { get; set; }
    public bool IsAnonymous { get; set; }
}

public class FaComSuggestionRespondDto
{
    public int Status { get; set; }
    public string? Response { get; set; }
}
