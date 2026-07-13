using Aegis.Audit;
using Aegis.Policies;

using Microsoft.Extensions.Caching.Distributed;

using Xunit;

namespace Aegis.Tests;

public class AegisEngineDistributedDecisionCacheTests
{
    private sealed class FakeDistributedCache : IDistributedCache
    {
        private readonly Dictionary<string, byte[]> _store = new();

        public byte[]? Get(string key) => _store.GetValueOrDefault(key);

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => Task.FromResult(Get(key));

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => _store[key] = value;

        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            Set(key, value, options);
            return Task.CompletedTask;
        }

        public void Refresh(string key)
        {
        }

        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;

        public void Remove(string key) => _store.Remove(key);

        public Task RemoveAsync(string key, CancellationToken token = default)
        {
            Remove(key);
            return Task.CompletedTask;
        }
    }

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
        },
    };

    [Fact]
    public async Task WithDistributedDecisionCache_RepeatedIdenticalCall_InvokesProviderOnlyOnceAsync()
    {
        var provider = new CountingAttributeProvider();
        var distributedCache = new FakeDistributedCache();
        using var engine = AegisEngine.FromPolicies([Policy()], provider)
            .WithDistributedDecisionCache(distributedCache, new DecisionCacheOptions { Duration = TimeSpan.FromMinutes(1) });
        var principal = AegisPrincipal.Create("alice", roles: ["Finance"]);
        var resource = AegisResource.Create("invoices", "INV-1");

        var first = await engine.AuthorizeAsync(principal, resource, "view");
        var second = await engine.AuthorizeAsync(principal, resource, "view");

        Assert.Equal(1, provider.CallCount);
        Assert.Equal(first.Allowed, second.Allowed);
    }

    [Fact]
    public async Task WithDistributedDecisionCache_SharedBackingStore_SecondEngineGetsACacheHitAsync()
    {
        // The whole point of a distributed cache: two independent AegisEngine
        // instances (e.g. two pods behind a load balancer) sharing one
        // backing store see each other's cached decisions.
        var distributedCache = new FakeDistributedCache();
        var providerA = new CountingAttributeProvider();
        var providerB = new CountingAttributeProvider();
        using var engineA = AegisEngine.FromPolicies([Policy()], providerA)
            .WithDistributedDecisionCache(distributedCache, new DecisionCacheOptions { Duration = TimeSpan.FromMinutes(1) });
        using var engineB = AegisEngine.FromPolicies([Policy()], providerB)
            .WithDistributedDecisionCache(distributedCache, new DecisionCacheOptions { Duration = TimeSpan.FromMinutes(1) });
        var principal = AegisPrincipal.Create("alice", roles: ["Finance"]);
        var resource = AegisResource.Create("invoices", "INV-1");

        await engineA.AuthorizeAsync(principal, resource, "view");
        await engineB.AuthorizeAsync(principal, resource, "view");

        Assert.Equal(1, providerA.CallCount);
        Assert.Equal(0, providerB.CallCount);
    }

    [Fact]
    public async Task WithDistributedDecisionCache_DifferentPrincipal_IsACacheMissAsync()
    {
        var provider = new CountingAttributeProvider();
        var distributedCache = new FakeDistributedCache();
        using var engine = AegisEngine.FromPolicies([Policy()], provider)
            .WithDistributedDecisionCache(distributedCache, new DecisionCacheOptions { Duration = TimeSpan.FromMinutes(1) });
        var resource = AegisResource.Create("invoices", "INV-1");

        await engine.AuthorizeAsync(AegisPrincipal.Create("alice", roles: ["Finance"]), resource, "view");
        await engine.AuthorizeAsync(AegisPrincipal.Create("bob", roles: ["Finance"]), resource, "view");

        Assert.Equal(2, provider.CallCount);
    }

    [Fact]
    public async Task WithDistributedDecisionCacheAndAuditLog_RecordsCacheHitsTooAsync()
    {
        var distributedCache = new FakeDistributedCache();
        var auditLog = new InMemoryAuditLogStore();
        using var engine = AegisEngine.FromPolicies([Policy()])
            .WithDistributedDecisionCache(distributedCache, new DecisionCacheOptions { Duration = TimeSpan.FromMinutes(1) })
            .WithAuditLog(auditLog);
        var principal = AegisPrincipal.Create("alice", roles: ["Finance"]);
        var resource = AegisResource.Create("invoices", "INV-1");

        await engine.AuthorizeAsync(principal, resource, "view");
        await engine.AuthorizeAsync(principal, resource, "view");

        var entries = await auditLog.QueryAsync(new AuditLogQuery());
        Assert.Equal(2, entries.Count);
    }
}