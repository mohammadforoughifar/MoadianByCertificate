using System.ComponentModel.DataAnnotations;
using Inventory.Shared;
namespace Inventory.Api.Data;
public class SystemCompany
{
    public int Id { get; set; }
    [MaxLength(200)] public string Name { get; set; } = "";
    [MaxLength(100)] public string? Code { get; set; }
    [MaxLength(50)] public string? Phone { get; set; }
    [MaxLength(250)] public string? Address { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// مسیر فایل PDF سربرگ نسبت به wwwroot (مثال:
    /// «uploads/letterheads/company-letterhead.pdf»).
    /// هنگام چاپ نامه صادره، متن نامه روی همین سربرگ قرار می‌گیرد.
    /// </summary>
    [MaxLength(200)] public string? LetterheadFileName { get; set; }
}