using Microsoft.Extensions.DependencyInjection;

namespace Aegis.AuthZen;

public static class HealthCheckServiceCollectionExtensions
{
    /// <summary>
    /// Registers a health check verifying the DI-registered <see cref="AegisEngine"/>
    /// is available -- pair with <c>MapAegisHealthChecks</c> to expose it over
    /// HTTP for a load balancer or orchestrator readiness/liveness probe.
    /// </summary>
    public static IServiceCollection AddAegisHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks().AddCheck<AegisHealthCheck>("aegis");
        return services;
    }
}