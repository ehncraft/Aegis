using Microsoft.Extensions.Caching.Memory;

namespace Aegis;

/// <summary>
/// In-process decision cache backed by <see cref="MemoryCache"/> -- fast,
/// but private to this instance; see <see cref="DistributedDecisionCache"/>
/// for sharing across instances.
/// </summary>
internal sealed class MemoryDecisionCache : IDecisionCacheBackend
{
    private readonly MemoryCache _cache;
    private readonly TimeSpan _duration;

    public MemoryDecisionCache(DecisionCacheOptions options)
    {
        _duration = options.Duration;
        _cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = options.MaxEntries });
    }

    public Task<AuthorizationDecision?> TryGetAsync(string key, CancellationToken cancellationToken)
    {
        var found = _cache.TryGetValue(key, out var cached) && cached is AuthorizationDecision decision
            ? decision
            : null;
        return Task.FromResult(found);
    }

    public Task SetAsync(string key, AuthorizationDecision decision, CancellationToken cancellationToken)
    {
        _cache.Set(key, decision, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = _duration,
            Size = 1,
        });
        return Task.CompletedTask;
    }

    public void Dispose() => _cache.Dispose();
}