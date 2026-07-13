using System.Net;

using Aegis.AuthZen;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Xunit;

namespace Aegis.Tests;

public class AegisHealthCheckTests
{
    [Fact]
    public async Task HealthEndpoint_EngineResolves_Returns200Async()
    {
        var host = await BuildHostAsync(services => services.AddSingleton(AegisEngine.FromPolicies([])));
        try
        {
            var response = await host.GetTestClient().GetAsync("/health");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    [Fact]
    public async Task HealthEndpoint_EngineFailsToResolve_Returns503WithoutCrashingTheEndpointAsync()
    {
        var host = await BuildHostAsync(
            services => services.AddSingleton<AegisEngine>(_ => throw new InvalidOperationException("simulated bad policy file")));
        try
        {
            var response = await host.GetTestClient().GetAsync("/health");

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    private static async Task<IHost> BuildHostAsync(Action<IServiceCollection> registerEngine)
    {
        var builder = new HostBuilder().ConfigureWebHost(webHost =>
        {
            webHost.UseTestServer();
            webHost.ConfigureServices(services =>
            {
                services.AddRouting();
                registerEngine(services);
                services.AddAegisHealthChecks();
            });
            webHost.Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(endpoints => endpoints.MapAegisHealthChecks());
            });
        });

        return await builder.StartAsync();
    }
}