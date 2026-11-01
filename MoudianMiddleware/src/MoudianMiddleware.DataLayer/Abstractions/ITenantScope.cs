namespace MoudianMiddleware.DataLayer.Abstractions;

/// <summary>
/// Explicit ambient scope for trusted background, seed, and tenant-provisioning work.
/// HTTP tenant resolution remains separate and defaults to no tenant.
/// </summary>
public interface ITenantScope
{
    /// <summary>The explicitly selected tenant in the current asynchronous flow, if any.</summary>
    int? TenantId { get; }

    /// <summary>
    /// Enters an explicit tenant scope. The returned lease must be disposed to restore the prior scope.
    /// Scopes may be nested; disposing them out of order is an error.
    /// </summary>
    /// <param name="tenantId">A positive tenant identifier.</param>
    /// <returns>A disposable lease that restores the previous scope.</returns>
    IDisposable Enter(int tenantId);
}
