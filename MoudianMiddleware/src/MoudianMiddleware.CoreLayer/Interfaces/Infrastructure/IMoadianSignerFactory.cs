using MoudianMiddleware.CoreLayer.Infrastructure.Moadian.Signing;
using MoudianMiddleware.DataLayer.Entities;

namespace MoudianMiddleware.CoreLayer.Interfaces.Infrastructure;

/// <summary>انتخاب امن استراتژی امضا بر اساس تنظیمات هر مودی.</summary>
public interface IMoadianSignerFactory
{
    /// <summary>ساخت امضاکننده‌ی موقتی از credential رمزنگاری‌شده؛ caller باید آن را Dispose کند.</summary>
    MoadianSigningMaterial Create(MoadianSetting setting, MoadianCredential credential);
}
