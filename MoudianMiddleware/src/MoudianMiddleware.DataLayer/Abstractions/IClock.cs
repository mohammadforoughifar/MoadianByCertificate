namespace MoudianMiddleware.DataLayer.Abstractions;

/// <summary>
/// منبع زمان سیستم؛ برای تست‌پذیری و یکسان‌سازی زمان UTC.
/// </summary>
public interface IClock
{
    /// <summary>زمان جاری به وقت جهانی.</summary>
    DateTimeOffset UtcNow { get; }
}
