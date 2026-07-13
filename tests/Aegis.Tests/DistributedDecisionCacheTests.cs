using Microsoft.Extensions.Caching.Distributed;

using Xunit;

namespace Aegis.Tests;

public class DistributedDecisionCacheTests
{
    private sealed class FakeDistributedCache : IDistributedCache
    {
        private readonly Dictionary<string, byte[]> _store = new();

        public List<(string Key, DistributedCacheEntryOptions Options)> SetCalls { get; } = [];

        public byte[]? Get(string key) => _store.GetValueOrDefault(key);

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => Task.FromResult(Get(key));

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
        {
            _store[key] = value;
            SetCalls.Add((key, options));
        }

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

    private static AuthorizationDecision AllowDecision() => AuthorizationDecision.Allow(new DecisionExplanation
    {
        Effect = "allow",
        MatchedPolicy = "invoice-policy",
        MatchedRule = "view",
        Conditions =
        [
            new ConditionExplanation { Expression = "principal.roles intersects [Finance]", Result = true },
        ],
    });

    [Fact]
    public async Task TryGetAsync_MissingKey_ReturnsNullAsync()
    {
        var cache = new DistributedDecisionCache(new FakeDistributedCache(), new DecisionCacheOptions { Duration = TimeSpan.FromMinutes(1) });

        var result = await cache.TryGetAsync("missing", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task SetThenTryGetAsync_RoundTripsTheFullDecisionAsync()
    {
        var cache = new DistributedDecisionCache(new FakeDistributedCache(), new DecisionCacheOptions { Duration = TimeSpan.FromMinutes(1) });
        var decision = AllowDecision();

        await cache.SetAsync("key", decision, CancellationToken.None);
        var result = await cache.TryGetAsync("key", CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result.Allowed);
        Assert.Equal("allow", result.Explanation.Effect);
        Assert.Equal("invoice-policy", result.Explanation.MatchedPolicy);
        Assert.Equal("view", result.Explanation.MatchedRule);
        var condition = Assert.Single(result.Explanation.Conditions);
        Assert.Equal("principal.roles intersects [Finance]", condition.Expression);
        Assert.True(condition.Result);
    }

    [Fact]
    public async Task SetAsync_PassesConfiguredDurationAsExpirationAsync()
    {
        var backingCache = new FakeDistributedCache();
        var duration = TimeSpan.FromMinutes(7);
        var cache = new DistributedDecisionCache(backingCache, new DecisionCacheOptions { Duration = duration });

        await cache.SetAsync("key", AllowDecision(), CancellationToken.None);

        var call = Assert.Single(backingCache.SetCalls);
        Assert.Equal(duration, call.Options.AbsoluteExpirationRelativeToNow);
    }

    [Fact]
    public void Dispose_DoesNotDisposeTheUnderlyingCache()
    {
        var cache = new DistributedDecisionCache(new FakeDistributedCache(), new DecisionCacheOptions { Duration = TimeSpan.FromMinutes(1) });

        var exception = Record.Exception(cache.Dispose);

        Assert.Null(exception);
    }
}