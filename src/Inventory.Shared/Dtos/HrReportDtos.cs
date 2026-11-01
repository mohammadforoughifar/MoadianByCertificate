namespace Inventory.Shared.Dtos;

// ==================== گزارش‌ساز سفارشی (§۶) ====================

public class HrReportColumnDto
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    /// <summary>text | number | date | enum | bool</summary>
    public string Type { get; set; } = "text";
    public List<HrReportOptionDto> Options { get; set; } = new();
}

public class HrReportOptionDto
{
    public string Value { get; set; } = "";
    public string Label { get; set; } = "";
}

public class HrReportFilterDto
{
    public string Field { get; set; } = "";
    /// <summary>contains | eq | gte | lte</summary>
    public string Op { get; set; } = "contains";
    public string? Value { get; set; }
}

public class HrReportRunDto
{
    /// <summary>employee | contract | decree | leave | mission</summary>
    public string Entity { get; set; } = "";
    public List<string> Columns { get; set; } = new();
    public List<HrReportFilterDto> Filters { get; set; } = new();
    public int Take { get; set; } = 500;
}

public class HrReportResultDto
{
    public List<HrReportColumnDto> Columns { get; set; } = new();
    /// <summary>هر ردیف: مقادیر متنی آماده‌نمایش به‌ترتیب ستون‌ها</summary>
    public List<List<string>> Rows { get; set; } = new();
    public int Total { get; set; }
    public bool Truncated { get; set; }
}

public class HrReportTemplateDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Entity { get; set; } = "";
    public List<string> Columns { get; set; } = new();
    public List<HrReportFilterDto> Filters { get; set; } = new();
    public DateTime CreatedAt { get; set; }
}

public class HrReportTemplateSaveDto
{
    public string Name { get; set; } = "";
    public string Entity { get; set; } = "";
    public List<string> Columns { get; set; } = new();
    public List<HrReportFilterDto> Filters { get; set; } = new();
}
