using System.Text.Json;

using Microsoft.Extensions.Caching.Memory;

namespace Aegis;

/// <summary>
/// Caches <see cref="AuthorizationDecision"/>s by principal/resource/action,
/// so a repeated identical call skips both attribute-provider I/O and
/// expression evaluation. The cache key is built by JSON-serializing a
/// canonical (sorted) representation rather than hand-concatenating a
/// delimited string -- a manual "key1=val1|key2=val2" scheme would let two
/// different logical inputs collide into the same string if an attribute's
/// own value happened to contain the delimiter, which for a security cache
/// means serving the wrong decision to the wrong principal.
/// </summary>
internal sealed class DecisionCache : IDisposable
{
    private readonly MemoryCache _cache;
    private readonly TimeSpan _duration;

    public DecisionCache(DecisionCacheOptions options)
    {
        _duration = options.Duration;
        _cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = options.MaxEntries });
    }

    public bool TryGet(string key, out AuthorizationDecision decision)
    {
        if (_cache.TryGetValue(key, out var cached) && cached is AuthorizationDecision found)
        {
            decision = found;
            return true;
        }

        decision = null!;
        return false;
    }

    public void Set(string key, AuthorizationDecision decision) =>
        _cache.Set(key, decision, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = _duration,
            Size = 1,
        });

    public void Dispose() => _cache.Dispose();

    public static string BuildKey(AegisPrincipal principal, AegisResource resource, string action)
    {
        var model = new CacheKeyModel(
            principal.Id,
            [.. principal.Roles.OrderBy(role => role, StringComparer.Ordinal)],
            SortAttributes(principal.Attributes),
            resource.Kind,
            resource.Id,
            SortAttributes(resource.Attributes),
            action);

        return JsonSerializer.Serialize(model);
    }

    private static SortedDictionary<string, object?> SortAttributes(IReadOnlyDictionary<string, object?> attributes) =>
        new(attributes.ToDictionary(pair => pair.Key, pair => pair.Value), StringComparer.Ordinal);

    private sealed record CacheKeyModel(
        string PrincipalId,
        string[] Roles,
        SortedDictionary<string, object?> PrincipalAttributes,
        string ResourceKind,
        string? ResourceId,
        SortedDictionary<string, object?> ResourceAttributes,
        string Action);
}