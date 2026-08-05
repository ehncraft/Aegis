using System.Security.Claims;

using Aegis.Policies;
using Aegis.Relationships;

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
    public async Task InvalidateAsync_BuiltEngine_NextCallRebuildsFromFactoryAsync()
    {
        var buildCount = 0;
        var registry = new MultiTenantAegisEngine(_ =>
        {
            buildCount++;
            return AegisEngine.FromPolicies([AllowFinancePolicy()]);
        });
        var principal = AegisPrincipal.Create("alice", roles: ["Finance"]);
        var resource = AegisResource.Create("invoices", "INV-1");
        await registry.AuthorizeAsync("tenant-a", principal, resource, "view");
        Assert.Equal(1, buildCount);

        await registry.InvalidateAsync("tenant-a");
        await registry.AuthorizeAsync("tenant-a", principal, resource, "view");

        Assert.Equal(2, buildCount);
    }

    [Fact]
    public async Task InvalidateAsync_NextCallReflectsAnUpdatedPolicySetAsync()
    {
        // The actual scenario InvalidateAsync exists for: a tenant edits its own policy (e.g.
        // through an admin API backed by CedarSqlPolicyProvider) after its engine was already
        // cached -- the next authorization check must see the edit, not the stale cached engine.
        var useUpdatedPolicy = false;
        var registry = new MultiTenantAegisEngine(_ => AegisEngine.FromPolicies(
            useUpdatedPolicy ? [AllowAdminOnlyPolicy()] : [AllowFinancePolicy()]));
        var principal = AegisPrincipal.Create("alice", roles: ["Finance"]);
        var resource = AegisResource.Create("invoices", "INV-1");
        var beforeEdit = await registry.AuthorizeAsync("tenant-a", principal, resource, "view");
        Assert.True(beforeEdit.Allowed);

        useUpdatedPolicy = true;
        await registry.InvalidateAsync("tenant-a");
        var afterEdit = await registry.AuthorizeAsync("tenant-a", principal, resource, "view");

        Assert.False(afterEdit.Allowed);
    }

    [Fact]
    public async Task InvalidateAsync_TenantNeverBuilt_DoesNotThrowAsync()
    {
        var registry = new MultiTenantAegisEngine(_ => AegisEngine.FromPolicies([AllowFinancePolicy()]));

        var exception = await Record.ExceptionAsync(() => registry.InvalidateAsync("never-requested"));

        Assert.Null(exception);
    }

    [Fact]
    public async Task InvalidateAsync_DoesNotAffectOtherTenantsCachedEngineAsync()
    {
        var buildCounts = new Dictionary<string, int>();
        var registry = new MultiTenantAegisEngine(tenantId =>
        {
            buildCounts[tenantId] = buildCounts.GetValueOrDefault(tenantId) + 1;
            return AegisEngine.FromPolicies([AllowFinancePolicy()]);
        });
        var principal = AegisPrincipal.Create("alice", roles: ["Finance"]);
        var resource = AegisResource.Create("invoices", "INV-1");
        await registry.AuthorizeAsync("acme", principal, resource, "view");
        await registry.AuthorizeAsync("beta", principal, resource, "view");

        await registry.InvalidateAsync("acme");
        await registry.AuthorizeAsync("acme", principal, resource, "view");
        await registry.AuthorizeAsync("beta", principal, resource, "view");

        Assert.Equal(2, buildCounts["acme"]);
        Assert.Equal(1, buildCounts["beta"]);
    }

    [Fact]
    public async Task GetEngineAsync_ThenWithRelationshipsAsync_AuthorizesAgainstDerivedRoleAsync()
    {
        // The scenario GetEngineAsync exists for: a caller needs a per-call relationship graph
        // (too volatile to bake into the cached per-tenant engine itself) layered on top of the
        // tenant's own cached base engine, then to authorize against the derived copy -- neither
        // AuthorizeAsync overload above supports that derivation step.
        var committeePolicy = new ResourcePolicy
        {
            Resource = "loans",
            DerivedRoles = new Dictionary<string, DerivedRoleDefinition>
            {
                ["committeeMember"] = new() { In = new DerivedRoleHierarchyCheck { Type = "Group", Id = "'audit-committee'" } },
            },
            Actions = new Dictionary<string, ActionRule>
            {
                ["review"] = new() { Allow = new AllowRule { Roles = ["committeeMember"] } },
            },
        };
        var registry = new MultiTenantAegisEngine(_ => AegisEngine.FromPolicies([committeePolicy]));
        var provider = new InMemoryRelationshipProvider([
            new EntityParent { Child = new EntityUid("User", "alice"), Parent = new EntityUid("Group", "audit-committee") },
        ]);
        var principal = AegisPrincipal.Create("alice");
        var resource = AegisResource.Create("loans", "LOAN-1");

        var baseEngine = await registry.GetEngineAsync("acme");
        using var scopedEngine = await baseEngine.WithRelationshipsAsync(provider);
        var decision = await scopedEngine.AuthorizeAsync(principal, resource, "review");

        Assert.True(decision.Allowed);
    }

    [Fact]
    public async Task GetEngineAsync_SameTenantTwice_ReusesCachedBaseEngineAsync()
    {
        var buildCount = 0;
        var registry = new MultiTenantAegisEngine(_ =>
        {
            buildCount++;
            return AegisEngine.FromPolicies([AllowFinancePolicy()]);
        });

        await registry.GetEngineAsync("acme");
        await registry.GetEngineAsync("acme");

        Assert.Equal(1, buildCount);
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