using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using TacticSim.Web.Data;

namespace TacticSim.Web.Services;

// Readiness includes the schema used by the game and production cookie keys.
// No migrations, keys, owners, or matches are created by a probe.
public sealed class DatabaseHealthCheck(MatchDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _ = await db.Sessions.AnyAsync(cancellationToken);
            _ = await db.DataProtectionKeys.AnyAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Do not expose database details or credentials in the public response.
            return HealthCheckResult.Unhealthy("PostgreSQL schema is unavailable.");
        }
    }
}
