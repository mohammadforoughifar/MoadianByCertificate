using MoudianMiddleware.DataLayer.Entities;

namespace MoudianMiddleware.DataLayer.Abstractions.Repositories;

/// <summary>
/// عملیات داده‌ی کاربران و عضویت‌های آن‌ها در مودیان.
/// </summary>
public interface IUserRepository : IGenericRepository<User>
{
    /// <summary>دریافت کاربر با نام کاربری نرمال‌شده، همراه با نقش‌ها و Permissionها.</summary>
    /// <param name="normalizedUserName">نام کاربری نرمال‌شده (حروف بزرگ).</param>
    /// <param name="cancellationToken">توکن لغو.</param>
    /// <returns>کاربر یا <c>null</c>.</returns>
    Task<User?> GetByNormalizedUserNameAsync(string normalizedUserName, CancellationToken cancellationToken = default);

    /// <summary>دریافت کاربر با شناسه، همراه با نقش‌ها.</summary>
    /// <param name="id">شناسه‌ی کاربر.</param>
    /// <param name="cancellationToken">توکن لغو.</param>
    /// <returns>کاربر یا <c>null</c>.</returns>
    Task<User?> GetWithRolesAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>بررسی وجود نام کاربری.</summary>
    /// <param name="normalizedUserName">نام کاربری نرمال‌شده.</param>
    /// <param name="cancellationToken">توکن لغو.</param>
    /// <returns>در صورت وجود <c>true</c>.</returns>
    Task<bool> ExistsUserNameAsync(string normalizedUserName, CancellationToken cancellationToken = default);
}
