namespace Aegis;

/// <summary>
/// Configures <see cref="AegisEngine.WithDecisionCache"/>. <see cref="Duration"/>
/// has no default -- caching an authorization decision means a role
/// revocation or attribute change might not take effect until the cache
/// entry expires, so how long that's acceptable for is a decision this
/// library shouldn't make silently on a caller's behalf.
/// </summary>
public sealed class DecisionCacheOptions
{
    public required TimeSpan Duration { get; init; }

    /// <summary>Caps memory use across all cached principal/resource/action combinations.</summary>
    public int MaxEntries { get; init; } = 10_000;
}