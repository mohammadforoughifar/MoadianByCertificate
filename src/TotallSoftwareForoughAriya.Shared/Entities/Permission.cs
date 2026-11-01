namespace Inventory.Shared.Entities;

public class Permission
{
    public int Id { get; set; }
    public string Module { get; set; } = "";      // Products, Orders, Reports, ...
    public string Action { get; set; } = "";      // Create, Read, Update, Delete, Export, ...
    public string? Description { get; set; }

    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
}