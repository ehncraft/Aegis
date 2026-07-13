using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Aegis.AuthZen;

/// <summary>
/// Verifies the registered <see cref="AegisEngine"/> is resolvable from DI
/// -- catching a bad policy file, or an unreachable SQL-backed policy/attribute
/// provider, at the point a load balancer or orchestrator probes readiness
/// rather than on the first real authorization request. Deliberately does
/// <em>not</em> call <c>AuthorizeAsync</c>: a real decision would pollute
/// both the decision cache and (if configured) the audit log with synthetic
/// health-check noise. Takes <see cref="IServiceProvider"/> rather than
/// <see cref="AegisEngine"/> directly so a resolution failure is caught and
/// converted into an <see cref="HealthCheckResult.Unhealthy(string, Exception, IReadOnlyDictionary{string, object})"/>
/// result here, not an uncaught exception during the health check's own
/// construction.
/// </summary>
internal sealed class AegisHealthCheck(IServiceProvider services) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            services.GetRequiredService<AegisEngine>();
            return Task.FromResult(HealthCheckResult.Healthy("AegisEngine is available."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("AegisEngine failed to resolve.", ex));
        }
    }
}