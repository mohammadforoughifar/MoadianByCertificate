namespace MoudianMiddleware.DataLayer.Entities.Common;

/// <summary>
/// موجودیت‌هایی که حذف آن‌ها «نرم» است (رکورد فیزیکی باقی می‌ماند).
/// </summary>
public interface ISoftDeletable
{
    /// <summary>آیا رکورد حذف شده است؟</summary>
    bool IsDeleted { get; set; }
}
