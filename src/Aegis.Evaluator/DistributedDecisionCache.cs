using System.Text.Json;

using Microsoft.Extensions.Caching.Distributed;

namespace Aegis;

/// <summary>
/// Decision cache backed by any <see cref="IDistributedCache"/> (Redis via
/// <c>Microsoft.Extensions.Caching.StackExchangeRedis</c>, SQL Server via
/// <c>Microsoft.Extensions.Caching.SqlServer</c>, etc.) -- shared across
/// every instance behind a load balancer, so a decision one instance
/// already computed skips both attribute-provider I/O and evaluation on
/// every other instance too, not just repeat calls to the same one.
/// Aegis stays storage-agnostic: it depends on the BCL interface, not any
/// specific backend package.
/// </summary>
internal sealed class DistributedDecisionCache : IDecisionCacheBackend
{
    private readonly IDistributedCache _cache;
    private readonly TimeSpan _duration;

    /// <summary>
    /// <paramref name="cache"/> is caller-owned (typically DI-registered
    /// alongside the backend package, e.g. <c>AddStackExchangeRedisCache</c>)
    /// -- this type never disposes it.
    /// </summary>
    public DistributedDecisionCache(IDistributedCache cache, DecisionCacheOptions options)
    {
        _cache = cache;
        _duration = options.Duration;
    }

    public async Task<AuthorizationDecision?> TryGetAsync(string key, CancellationToken cancellationToken)
    {
        var bytes = await _cache.GetAsync(key, cancellationToken);
        return bytes is null ? null : JsonSerializer.Deserialize<AuthorizationDecision>(bytes);
    }

    public async Task SetAsync(string key, AuthorizationDecision decision, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(decision);
        var entryOptions = new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = _duration };
        await _cache.SetAsync(key, bytes, entryOptions, cancellationToken);
    }

    public void Dispose()
    {
        // Doesn't own _cache -- see the constructor remarks.
    }
}