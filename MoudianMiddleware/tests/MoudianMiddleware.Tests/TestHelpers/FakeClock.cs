using MoudianMiddleware.DataLayer.Abstractions;

namespace MoudianMiddleware.Tests.TestHelpers;

/// <summary>
/// ساعت ثابت برای تست‌های قطعی (Deterministic).
/// </summary>
public sealed class FakeClock : IClock
{
    /// <summary>زمان ثابت پیش‌فرض تست.</summary>
    public static readonly DateTimeOffset DefaultNow = new(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// سازنده با زمان دلخواه.
    /// </summary>
    /// <param name="utcNow">زمان جاری (پیش‌فرض <see cref="DefaultNow"/>).</param>
    public FakeClock(DateTimeOffset? utcNow = null)
    {
        UtcNow = utcNow ?? DefaultNow;
    }

    /// <inheritdoc />
    public DateTimeOffset UtcNow { get; set; }
}
