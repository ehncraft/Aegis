using Aegis.Audit;
using Aegis.Policies;

using Xunit;

namespace Aegis.Tests;

public class AegisEngineAuditLogTests
{
    private static ResourcePolicy Policy() => new()
    {
        Resource = "invoices",
        Actions = new Dictionary<string, ActionRule>
        {
            ["view"] = new() { Allow = new AllowRule { Roles = ["Finance"] } },
        },
    };

    [Fact]
    public async Task AuthorizeAsync_WithAuditLog_RecordsFreshEvaluationAsync()
    {
        var store = new InMemoryAuditLogStore();
        using var engine = AegisEngine.FromPolicies([Policy()]).WithAuditLog(store);
        var principal = AegisPrincipal.Create("alice", roles: ["Finance"]);
        var resource = AegisResource.Create("invoices", "INV-1");

        await engine.AuthorizeAsync(principal, resource, "view");

        var entries = await store.QueryAsync(new AuditLogQuery());
        var entry = Assert.Single(entries);
        Assert.Equal("alice", entry.PrincipalId);
        Assert.Equal("invoices", entry.ResourceKind);
        Assert.Equal("INV-1", entry.ResourceId);
        Assert.Equal("view", entry.Action);
        Assert.True(entry.Allowed);
        Assert.Equal("allow", entry.Explanation.Effect);
    }

    [Fact]
    public async Task AuthorizeAsync_WithAuditLog_RecordsDeniedDecisionsTooAsync()
    {
        var store = new InMemoryAuditLogStore();
        using var engine = AegisEngine.FromPolicies([Policy()]).WithAuditLog(store);
        var principal = AegisPrincipal.Create("bob", roles: ["Sales"]);
        var resource = AegisResource.Create("invoices", "INV-1");

        await engine.AuthorizeAsync(principal, resource, "view");

        var entries = await store.QueryAsync(new AuditLogQuery());
        var entry = Assert.Single(entries);
        Assert.False(entry.Allowed);
    }

    [Fact]
    public async Task AuthorizeAsync_WithAuditLogAndDecisionCache_RecordsCacheHitsToo_NotJustFreshEvaluationsAsync()
    {
        var store = new InMemoryAuditLogStore();
        using var engine = AegisEngine.FromPolicies([Policy()])
            .WithDecisionCache(new DecisionCacheOptions { Duration = TimeSpan.FromMinutes(1) })
            .WithAuditLog(store);
        var principal = AegisPrincipal.Create("alice", roles: ["Finance"]);
        var resource = AegisResource.Create("invoices", "INV-1");

        await engine.AuthorizeAsync(principal, resource, "view"); // fresh eval
        await engine.AuthorizeAsync(principal, resource, "view"); // cache hit
        await engine.AuthorizeAsync(principal, resource, "view"); // cache hit

        var entries = await store.QueryAsync(new AuditLogQuery());
        Assert.Equal(3, entries.Count);
    }

    [Fact]
    public async Task AuthorizeAsync_WithAuditLogAndDecisionCacheBuiltInEitherOrder_BothComposeAsync()
    {
        var store = new InMemoryAuditLogStore();
        using var engine = AegisEngine.FromPolicies([Policy()])
            .WithAuditLog(store)
            .WithDecisionCache(new DecisionCacheOptions { Duration = TimeSpan.FromMinutes(1) });
        var principal = AegisPrincipal.Create("alice", roles: ["Finance"]);
        var resource = AegisResource.Create("invoices", "INV-1");

        await engine.AuthorizeAsync(principal, resource, "view");
        await engine.AuthorizeAsync(principal, resource, "view");

        var entries = await store.QueryAsync(new AuditLogQuery());
        Assert.Equal(2, entries.Count);
    }

    [Fact]
    public async Task AuthorizeAsync_WithoutAuditLog_DoesNotThrowAndRecordsNothingAsync()
    {
        using var engine = AegisEngine.FromPolicies([Policy()]);
        var principal = AegisPrincipal.Create("alice", roles: ["Finance"]);
        var resource = AegisResource.Create("invoices", "INV-1");

        var decision = await engine.AuthorizeAsync(principal, resource, "view");

        Assert.True(decision.Allowed);
    }
}