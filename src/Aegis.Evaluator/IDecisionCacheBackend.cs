namespace Aegis;

/// <summary>
/// Storage for the decision cache -- <see cref="MemoryDecisionCache"/>
/// (single instance, <c>AegisEngine.WithDecisionCache</c>) or
/// <see cref="DistributedDecisionCache"/> (shared across instances,
/// <c>AegisEngine.WithDistributedDecisionCache</c>). Async throughout even
/// though the in-memory backend never actually awaits anything, so
/// <c>AegisEngine.AuthorizeAsync</c> doesn't need to know which backend
/// it's talking to.
/// </summary>
internal interface IDecisionCacheBackend : IDisposable
{
    Task<AuthorizationDecision?> TryGetAsync(string key, CancellationToken cancellationToken);

    Task SetAsync(string key, AuthorizationDecision decision, CancellationToken cancellationToken);
}