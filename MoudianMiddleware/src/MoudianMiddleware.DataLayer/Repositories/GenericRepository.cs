using Microsoft.EntityFrameworkCore;
using MoudianMiddleware.DataLayer.Abstractions.Repositories;
using MoudianMiddleware.DataLayer.Context;
using MoudianMiddleware.DataLayer.Entities.Common;

namespace MoudianMiddleware.DataLayer.Repositories;

/// <summary>
/// پیاده‌سازی عمومی Repository بر پایه‌ی EF Core.
/// </summary>
/// <typeparam name="TEntity">نوع موجودیت.</typeparam>
public class GenericRepository<TEntity> : IGenericRepository<TEntity>
    where TEntity : BaseEntity
{
    private readonly DbSet<TEntity> _dbSet;

    /// <summary>
    /// سازنده با تزریق Context.
    /// </summary>
    /// <param name="context">Context دیتابیس.</param>
    public GenericRepository(MoudianDbContext context)
    {
        _dbSet = context.Set<TEntity>();
    }

    /// <summary>مجموعه‌ی موجودیت‌ها برای استفاده‌ی Repositoryهای مشتق‌شده.</summary>
    protected DbSet<TEntity> Set => _dbSet;

    /// <inheritdoc />
    public IQueryable<TEntity> Query(bool asNoTracking = true, bool ignoreQueryFilters = false)
    {
        IQueryable<TEntity> query = _dbSet;

        if (ignoreQueryFilters)
        {
            query = query.IgnoreQueryFilters();
        }

        return asNoTracking ? query.AsNoTracking() : query;
    }

    /// <inheritdoc />
    public Task<TEntity?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
        => _dbSet.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<List<TEntity>> ListAsync(CancellationToken cancellationToken = default)
        => _dbSet.AsNoTracking().ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task AddAsync(TEntity entity, CancellationToken cancellationToken = default)
        => await _dbSet.AddAsync(entity, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task AddRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
        => await _dbSet.AddRangeAsync(entities, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public void Update(TEntity entity) => _dbSet.Update(entity);

    /// <inheritdoc />
    public void Remove(TEntity entity) => _dbSet.Remove(entity);

    /// <inheritdoc />
    public Task<bool> ExistsAsync(int id, CancellationToken cancellationToken = default)
        => _dbSet.AnyAsync(x => x.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<int> CountAsync(CancellationToken cancellationToken = default)
        => _dbSet.CountAsync(cancellationToken);
}
