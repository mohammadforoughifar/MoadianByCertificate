using MoudianMiddleware.DataLayer.Entities.Common;

namespace MoudianMiddleware.DataLayer.Entities;

/// <summary>
/// اتصال نقش به یک Permission (کد متنی مثل <c>Invoice.Send</c>).
/// </summary>
public class RolePermission : BaseEntity
{
    /// <summary>شناسه‌ی نقش.</summary>
    public int RoleId { get; set; }

    /// <summary>کد Permission (از <c>Permissions</c> در Contracts).</summary>
    public string Permission { get; set; } = string.Empty;

    /// <summary>نقش مربوطه.</summary>
    public Role? Role { get; set; }
}
