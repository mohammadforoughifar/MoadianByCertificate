using System.ComponentModel.DataAnnotations;
using Inventory.Shared;
namespace Inventory.Api.Data;
public class SystemDepartment
{
    public int Id { get; set; }
    [MaxLength(150)] public string Name { get; set; } = "";
    public int? CompanyId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}