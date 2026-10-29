using Microsoft.EntityFrameworkCore;
using MoudianMiddleware.DataLayer.Abstractions.Repositories;
using MoudianMiddleware.DataLayer.Context;
using MoudianMiddleware.DataLayer.Entities;

namespace MoudianMiddleware.DataLayer.Repositories;

/// <summary>
/// پیاده‌سازی داده‌ی کاربران.
/// </summary>
public sealed class UserRepository : GenericRepository<User>, IUserRepository
{
    /// <summary>
    /// سازنده با تزریق Context.
    /// </summary>
    /// <param name="context">Context دیتابیس.</param>
    public UserRepository(MoudianDbContext context)
        : base(context)
    {
    }

    /// <inheritdoc />
    public Task<User?> GetByNormalizedUserNameAsync(string normalizedUserName, CancellationToken cancellationToken = default)
        => Set.AsNoTracking()
            .Include(x => x.UserTenants)
            .ThenInclude(ut => ut.Role)
            .ThenInclude(role => role!.Permissions)
            .FirstOrDefaultAsync(x => x.NormalizedUserName == normalizedUserName, cancellationToken);

    /// <inheritdoc />
    public Task<User?> GetWithRolesAsync(int id, CancellationToken cancellationToken = default)
        => Set.AsNoTracking()
            .Include(x => x.UserTenants)
            .ThenInclude(ut => ut.Role)
            .ThenInclude(role => role!.Permissions)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<bool> ExistsUserNameAsync(string normalizedUserName, CancellationToken cancellationToken = default)
        => Set.AnyAsync(x => x.NormalizedUserName == normalizedUserName, cancellationToken);
}
