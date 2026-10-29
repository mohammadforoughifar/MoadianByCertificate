namespace MoudianMiddleware.DataLayer.Entities.Common;

/// <summary>
/// کلاس پایه‌ی همه‌ی موجودیت‌ها؛ فقط کلید اصلی مشترک.
/// </summary>
public abstract class BaseEntity
{
    /// <summary>کلید اصلی (Identity).</summary>
    public int Id { get; set; }
}
