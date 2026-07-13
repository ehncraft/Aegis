using System.Security.Claims;

using Aegis.Policies;

using Xunit;

namespace Aegis.Tests;

public class MultiTenantAegisEngineTests
{
    private static string FixturesPath => Path.Combine(AppContext.BaseDirectory, "Fixtures", "Tenants");

    private static ResourcePolicy AllowFinancePolicy() => new()
    {
        Resource = "invoices",
        Actions = new Dictionary<string, ActionRule>
        {
            ["view"] = new() { Allow = new AllowRule { Roles = ["Finance"] } },
        },
    };

    private static ResourcePolicy AllowAdminOnlyPolicy() => new()
    {
        Resource = "invoices",
        Actions = new Dictionary<string, ActionRule>
        {
            ["view"] = new() { Allow = new AllowRule { Roles = ["Admin"] } },
        },
    };

    [Fact]
    public async Task AuthorizeAsync_BuildsEngineOnceThenReusesForSameTenantAsync()
    {
        var buildCount = 0;
        var registry = new MultiTenantAegisEngine(tenantId =>
        {
            buildCount++;
            return AegisEngine.FromPolicies([AllowFinancePolicy()]);
        });
        var principal = AegisPrincipal.Create("alice", roles: ["Finance"]);
        var resource = AegisResource.Create("invoices", "INV-1");

        await registry.AuthorizeAsync("tenant-a", principal, resource, "view");
        await registry.AuthorizeAsync("tenant-a", principal, resource, "view");

        Assert.Equal(1, buildCount);
    }

    [Fact]
    public async Task AuthorizeAsync_DifferentTenants_AreIsolatedAsync()
    {
        var registry = new MultiTenantAegisEngine(tenantId => tenantId switch
        {
            "acme" => AegisEngine.FromPolicies([AllowFinancePolicy()]),
            "beta" => AegisEngine.FromPolicies([AllowAdminOnlyPolicy()]),
            _ => throw new InvalidOperationException($"Unknown tenant '{tenantId}'"),
        });
        var principal = AegisPrincipal.Create("alice", roles: ["Finance"]);
        var resource = AegisResource.Create("invoices", "INV-1");

        var acmeDecision = await registry.AuthorizeAsync("acme", principal, resource, "view");
        var betaDecision = await registry.AuthorizeAsync("beta", principal, resource, "view");

        Assert.True(acmeDecision.Allowed);
        Assert.False(betaDecision.Allowed);
    }

    [Fact]
    public async Task AuthorizeAsync_FailedBuild_EvictsCacheSoRetrySucceedsAsync()
    {
        var attempt = 0;
        var registry = new MultiTenantAegisEngine(_ =>
        {
            attempt++;
            return attempt == 1
                ? throw new InvalidOperationException("simulated transient failure")
                : AegisEngine.FromPolicies([AllowFinancePolicy()]);
        });
        var principal = AegisPrincipal.Create("alice", roles: ["Finance"]);
        var resource = AegisResource.Create("invoices", "INV-1");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => registry.AuthorizeAsync("tenant-a", principal, resource, "view"));

        var decision = await registry.AuthorizeAsync("tenant-a", principal, resource, "view");

        Assert.True(decision.Allowed);
        Assert.Equal(2, attempt);
    }

    [Fact]
    public async Task FromTenantDirectories_LoadsEachTenantsOwnPolicySetAsync()
    {
        await using var registry = MultiTenantAegisEngine.FromTenantDirectories(FixturesPath);
        var principal = AegisPrincipal.Create("alice", roles: ["Finance"]);
        var resource = AegisResource.Create("invoices", "INV-1");

        var acmeDecision = await registry.AuthorizeAsync("acme-sacco", principal, resource, "view");
        var betaDecision = await registry.AuthorizeAsync("beta-bank", principal, resource, "view");

        Assert.True(acmeDecision.Allowed);
        Assert.False(betaDecision.Allowed);
    }

    [Fact]
    public async Task AuthorizeAsync_ClaimsPrincipalOverload_MapsThenAuthorizesAsync()
    {
        var registry = new MultiTenantAegisEngine(_ => AegisEngine.FromPolicies([AllowFinancePolicy()]));
        var mapper = new ClaimsPrincipalMapper(new ClaimsMappingOptions());
        var claimsPrincipal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "alice"),
            new Claim(ClaimTypes.Role, "Finance"),
        ], "TestAuth"));

        var decision = await registry.AuthorizeAsync(
            "tenant-a", claimsPrincipal, mapper, AegisResource.Create("invoices", "INV-1"), "view");

        Assert.True(decision.Allowed);
    }

    [Fact]
    public async Task DisposeAsync_DisposesBuiltEnginesAsync()
    {
        var registry = new MultiTenantAegisEngine(_ =>
            AegisEngine.FromPolicies([AllowFinancePolicy()])
                .WithDecisionCache(new DecisionCacheOptions { Duration = TimeSpan.FromMinutes(1) }));
        var principal = AegisPrincipal.Create("alice", roles: ["Finance"]);
        var resource = AegisResource.Create("invoices", "INV-1");

        await registry.AuthorizeAsync("tenant-a", principal, resource, "view");
        await registry.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => registry.AuthorizeAsync("tenant-a", principal, resource, "view"));
    }
}