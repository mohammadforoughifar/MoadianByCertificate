using MoudianMiddleware.DataLayer.Context;

namespace MoudianMiddleware.DataLayer.UnitOfWork;

/// <summary>
/// پیاده‌سازی واحد کار بر پایه‌ی <see cref="MoudianDbContext"/>.
/// </summary>
public sealed class UnitOfWork : IUnitOfWork
{
    private readonly MoudianDbContext _context;

    /// <summary>
    /// سازنده با تزریق Context.
    /// </summary>
    /// <param name="context">Context دیتابیس.</param>
    public UnitOfWork(MoudianDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => _context.SaveChangesAsync(cancellationToken);
}
