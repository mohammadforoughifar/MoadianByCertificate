using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MoudianMiddleware.DataLayer.Context;

namespace MoudianMiddleware.Api.Infrastructure;

public sealed class DatabaseHealthCheck(MoudianDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try { return await db.Database.CanConnectAsync(cancellationToken).ConfigureAwait(false) ? HealthCheckResult.Healthy("database reachable") : HealthCheckResult.Unhealthy("database unavailable"); }
        catch (Exception) { return HealthCheckResult.Unhealthy("database unavailable"); }
    }
}
