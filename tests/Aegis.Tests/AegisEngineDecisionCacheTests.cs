using Aegis.Policies;

using Xunit;

namespace Aegis.Tests;

public class AegisEngineDecisionCacheTests
{
    private sealed class CountingAttributeProvider : IAttributeProvider
    {
        public int CallCount { get; private set; }

        public Task<PrincipalAttributes> GetPrincipalAttributesAsync(
            string principalId, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(PrincipalAttributes.Empty);
        }

        public Task<IReadOnlyDictionary<string, object?>> GetResourceAttributesAsync(
            string resourceKind, string resourceId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, object?>>(new Dictionary<string, object?>());
    }

    private static ResourcePolicy Policy() => new()
    {
        Resource = "invoices",
        Actions = new Dictionary<string, ActionRule>
        {
            ["view"] = new() { Allow = new AllowRule { Roles = ["Finance"] } },
            ["approve"] = new() { Allow = new AllowRule { Roles = ["Finance"] } },
        },
    };

    [Fact]
    public async Task NoDecisionCache_RepeatedIdenticalCall_InvokesProviderEveryTimeAsync()
    {
        var provider = new CountingAttributeProvider();
        var engine = AegisEngine.FromPolicies([Policy()], provider);
        var principal = AegisPrincipal.Create("alice", roles: ["Finance"]);
        var resource = AegisResource.Create("invoices", "INV-1");

        await engine.AuthorizeAsync(principal, resource, "view");
        await engine.AuthorizeAsync(principal, resource, "view");

        Assert.Equal(2, provider.CallCount);
    }

    [Fact]
    public async Task WithDecisionCache_RepeatedIdenticalCall_InvokesProviderOnlyOnceAsync()
    {
        var provider = new CountingAttributeProvider();
        using var engine = AegisEngine.FromPolicies([Policy()], provider)
            .WithDecisionCache(new DecisionCacheOptions { Duration = TimeSpan.FromMinutes(1) });
        var principal = AegisPrincipal.Create("alice", roles: ["Finance"]);
        var resource = AegisResource.Create("invoices", "INV-1");

        var first = await engine.AuthorizeAsync(principal, resource, "view");
        var second = await engine.AuthorizeAsync(principal, resource, "view");

        Assert.Equal(1, provider.CallCount);
        Assert.Equal(first.Allowed, second.Allowed);
        Assert.Equal(first.Explanation.Effect, second.Explanation.Effect);
    }

    [Fact]
    public async Task WithDecisionCache_DifferentAction_IsACacheMissAsync()
    {
        var provider = new CountingAttributeProvider();
        using var engine = AegisEngine.FromPolicies([Policy()], provider)
            .WithDecisionCache(new DecisionCacheOptions { Duration = TimeSpan.FromMinutes(1) });
        var principal = AegisPrincipal.Create("alice", roles: ["Finance"]);
        var resource = AegisResource.Create("invoices", "INV-1");

        await engine.AuthorizeAsync(principal, resource, "view");
        await engine.AuthorizeAsync(principal, resource, "approve");

        Assert.Equal(2, provider.CallCount);
    }

    [Fact]
    public async Task WithDecisionCache_DifferentPrincipalAttributes_IsACacheMissAsync()
    {
        var provider = new CountingAttributeProvider();
        using var engine = AegisEngine.FromPolicies([Policy()], provider)
            .WithDecisionCache(new DecisionCacheOptions { Duration = TimeSpan.FromMinutes(1) });
        var resource = AegisResource.Create("invoices", "INV-1");
        var financePrincipal = AegisPrincipal.Create("alice", roles: ["Finance"]);
        var noRolesPrincipal = AegisPrincipal.Create("alice");

        await engine.AuthorizeAsync(financePrincipal, resource, "view");
        await engine.AuthorizeAsync(noRolesPrincipal, resource, "view");

        Assert.Equal(2, provider.CallCount);
    }

    [Fact]
    public async Task WithDecisionCache_AfterDurationExpires_ReEvaluatesAsync()
    {
        var provider = new CountingAttributeProvider();
        using var engine = AegisEngine.FromPolicies([Policy()], provider)
            .WithDecisionCache(new DecisionCacheOptions { Duration = TimeSpan.FromMilliseconds(50) });
        var principal = AegisPrincipal.Create("alice", roles: ["Finance"]);
        var resource = AegisResource.Create("invoices", "INV-1");

        await engine.AuthorizeAsync(principal, resource, "view");
        await Task.Delay(TimeSpan.FromMilliseconds(200));
        await engine.AuthorizeAsync(principal, resource, "view");

        Assert.Equal(2, provider.CallCount);
    }
}