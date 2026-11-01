namespace Inventory.Client.Services;

// =====================================================================
// سرویس کلاینت «هوش مصنوعی فروغ آریا» — تنها نقطه ساخت مسیرهای API دستیار
// =====================================================================

public class AiHealth
{
    public bool Enabled { get; set; }
    public bool ModelReachable { get; set; }
    public string ChatModel { get; set; } = "";
    public string? Error { get; set; }
}

public class AiChatAttachment
{
    public string Kind { get; set; } = "excel";
    public string ReportId { get; set; } = "";
    public string Title { get; set; } = "";
    public int TotalRows { get; set; }
}

public class AiChatResult
{
    public int ConversationId { get; set; }
    public int MessageId { get; set; }
    public string Reply { get; set; } = "";
    public bool UsedFallback { get; set; }
    public List<string> ToolsUsed { get; set; } = new();
    public List<AiChatAttachment> Attachments { get; set; } = new();
}

public class AiConversation
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Channel { get; set; } = "web";
    public DateTime LastMessageAtUtc { get; set; }
    public List<AiChatMessage> Messages { get; set; } = new();
}

public class AiChatMessage
{
    public string Role { get; set; } = "user";
    public int Id { get; set; }
    public int? MyRating { get; set; }
    public string Content { get; set; } = "";
    public bool UsedFallback { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public List<AiChatAttachment> Attachments { get; set; } = new();
}

public class AiLetterDraft
{
    public string Subject { get; set; } = "";
    public string BodyHtml { get; set; } = "";
    public bool UsedFallback { get; set; }
}

public class AiLetterSummary
{
    public int LetterId { get; set; }
    public string Summary { get; set; } = "";
    public string CurrentStatus { get; set; } = "";
    public bool UsedFallback { get; set; }
}

public class AiReferralSuggestion
{
    public int UserId { get; set; }
    public string Name { get; set; } = "";
    public string? Department { get; set; }
    public string Reason { get; set; } = "";
}

public class AiReferralResponse
{
    public int LetterId { get; set; }
    public List<AiReferralSuggestion> Suggestions { get; set; } = new();
    public bool UsedFallback { get; set; }
}

public class AiLetterTask
{
    public string Action { get; set; } = "";
    public string? AssigneeHint { get; set; }
    public string? DeadlineText { get; set; }
    public DateTime? DeadlineDate { get; set; }
}

public class AiLetterTasks
{
    public int LetterId { get; set; }
    public List<AiLetterTask> Tasks { get; set; } = new();
    public bool UsedFallback { get; set; }
}

public class AiLetterCategory
{
    public int LetterId { get; set; }
    public string Category { get; set; } = "";
    public int Priority { get; set; } = 3;
    public List<string> Tags { get; set; } = new();
    public bool UsedFallback { get; set; }
}

public class AiMinutesItem
{
    public string Text { get; set; } = "";
    public string Kind { get; set; } = "مصوبه";
    public string? Responsible { get; set; }
    public string? DeadlineText { get; set; }
}

public class AiMinutesDraft
{
    public string Title { get; set; } = "";
    public string Summary { get; set; } = "";
    public List<AiMinutesItem> Items { get; set; } = new();
    public bool UsedFallback { get; set; }
}

public class AiReceiversResponse
{
    public List<AiReferralSuggestion> Suggestions { get; set; } = new();
    public bool UsedFallback { get; set; }
}

public class AiAssistantService
{
    private readonly IApiClient _api;
    public AiAssistantService(IApiClient api) => _api = api;

    public Task<AiHealth> GetHealthAsync()
        => _api.GetAsync<AiHealth>("api/ai/health");

    public Task<AiChatResult> ChatAsync(int? conversationId, string message)
        => _api.PostLongAsync<AiChatResult>("api/ai/chat", new { conversationId, message });

    public Task<List<AiConversation>> GetConversationsAsync()
        => _api.GetAsync<List<AiConversation>>("api/ai/conversations");

    public Task<AiConversation> GetConversationAsync(int id)
        => _api.GetAsync<AiConversation>($"api/ai/conversations/{id}");

    public Task DeleteConversationAsync(int id)
        => _api.DeleteAsync($"api/ai/conversations/{id}");

    public Task<AiLetterDraft> DraftLetterAsync(string topic, string tone, string kind, string? toName, string? extra)
        => _api.PostLongAsync<AiLetterDraft>("api/ai/letters/draft",
            new { topic, tone, kind, toName, extraContext = extra });

    public Task<AiLetterSummary> SummarizeLetterAsync(int letterId)
        => _api.PostLongAsync<AiLetterSummary>($"api/ai/letters/{letterId}/summarize", new { });

    public Task<AiReferralResponse> SuggestReferralAsync(int letterId)
        => _api.PostLongAsync<AiReferralResponse>($"api/ai/letters/{letterId}/suggest-referral", new { });

    public Task<AiLetterTasks> ExtractTasksAsync(int letterId)
        => _api.PostLongAsync<AiLetterTasks>($"api/ai/letters/{letterId}/extract-tasks", new { });

    public Task<AiLetterCategory> CategorizeLetterAsync(int letterId)
        => _api.PostLongAsync<AiLetterCategory>($"api/ai/letters/{letterId}/categorize", new { });

    public Task<AiMinutesDraft> DraftMinutesAsync(string rawText, string? title)
        => _api.PostLongAsync<AiMinutesDraft>("api/ai/minutes/draft", new { rawText, title });

    public Task<(byte[] Data, string FileName, string ContentType)> DownloadReportExcelAsync(string reportId)
        => _api.GetFileAsync($"api/ai/reports/{reportId}/excel");

    public Task<AiLetterDraft> DraftReplyAsync(int letterId, string? hint)
        => _api.PostLongAsync<AiLetterDraft>($"api/ai/letters/{letterId}/draft-reply", new { hint });

    public Task<AiReceiversResponse> SuggestReceiversAsync(string title, string text)
        => _api.PostLongAsync<AiReceiversResponse>("api/ai/letters/suggest-receivers", new { title, text });

    public class AiBriefingPreview
    {
        public string Briefing { get; set; } = "";
    }

    public class AiBriefingSendResult
    {
        public int Sent { get; set; }
    }

    public Task<AiBriefingPreview> GetBriefingPreviewAsync()
        => _api.GetAsync<AiBriefingPreview>("api/ai/briefing/preview");

    public Task<AiBriefingSendResult> SendBriefingNowAsync()
        => _api.PostLongAsync<AiBriefingSendResult>("api/ai/briefing/send-now", new { });

    public Task<AiFeedbackSubmitResult> SubmitFeedbackAsync(int messageId, int rating, string? reason, string? comment)
        => _api.PostAsync<AiFeedbackSubmitResult>("api/ai/feedback/submit",
            new { messageId, rating, reason, comment });

    public Task<AiFeedbackStats> GetFeedbackStatsAsync(int days = 14)
        => _api.GetAsync<AiFeedbackStats>($"api/ai/feedback/stats?days={days}");

    public Task<List<AiFeedbackItem>> GetFeedbackListAsync(int? rating, int skip, int take)
        => _api.GetAsync<List<AiFeedbackItem>>(
            $"api/ai/feedback/list?skip={skip}&take={take}" + (rating == null ? "" : $"&rating={rating}"));

    public Task<AiAuditStats> GetAuditStatsAsync(int days = 14)
        => _api.GetAsync<AiAuditStats>($"api/ai/audit/stats?days={days}");

    public Task<List<AiAuditTurn>> GetAuditListAsync(bool? success, string? search, int skip, int take)
        => _api.GetAsync<List<AiAuditTurn>>(
            $"api/ai/audit/list?skip={skip}&take={take}"
            + (success == null ? "" : success.Value ? "&success=true" : "&success=false")
            + (string.IsNullOrWhiteSpace(search) ? "" : $"&search={Uri.EscapeDataString(search.Trim())}"));
}

public class AiAuditStats
{
    public int Total { get; set; }
    public int Failed { get; set; }
    public int Fallback { get; set; }
    public double? SuccessPct { get; set; }
    public int ActionsExecuted { get; set; }
    public int ActionsRejected { get; set; }
    public int ActionsPending { get; set; }
    public List<AiAuditCount> TopTools { get; set; } = new();
    public List<AiAuditCount> TopUsers { get; set; } = new();
    public List<AiAuditDay> ByDay { get; set; } = new();
    public List<AiAuditAction> RecentActions { get; set; } = new();
}

public class AiAuditCount
{
    public string Key { get; set; } = "";
    public int Count { get; set; }
}

public class AiAuditDay
{
    public string Day { get; set; } = "";
    public int Total { get; set; }
    public int Failed { get; set; }
}

public class AiAuditTurn
{
    public int Id { get; set; }
    public string User { get; set; } = "";
    public string Channel { get; set; } = "";
    public string Message { get; set; } = "";
    public List<string> Tools { get; set; } = new();
    public bool Success { get; set; }
    public bool UsedFallback { get; set; }
    public string? Error { get; set; }
    public int DurationMs { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class AiAuditAction
{
    public int Id { get; set; }
    public string User { get; set; } = "";
    public string Action { get; set; } = "";
    public string Summary { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

public class AiFeedbackSubmitResult
{
    public bool Removed { get; set; }
}

public class AiFeedbackStats
{
    public int Total { get; set; }
    public int Up { get; set; }
    public int Down { get; set; }
    public double? SatisfactionPct { get; set; }
    public List<AiFeedbackCount> ByReason { get; set; } = new();
    public List<AiFeedbackDay> ByDay { get; set; } = new();
    public List<AiFeedbackCount> TopToolsDown { get; set; } = new();
}

public class AiFeedbackCount
{
    public string Key { get; set; } = "";
    public int Count { get; set; }
}

public class AiFeedbackDay
{
    public string Day { get; set; } = "";
    public int Up { get; set; }
    public int Down { get; set; }
}

public class AiFeedbackItem
{
    public int Id { get; set; }
    public int MessageId { get; set; }
    public string User { get; set; } = "";
    public int Rating { get; set; }
    public string? Reason { get; set; }
    public string? Comment { get; set; }
    public List<string> Tools { get; set; } = new();
    public bool UsedFallback { get; set; }
    public string Excerpt { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}
