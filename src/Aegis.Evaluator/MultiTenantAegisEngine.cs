using System.Collections.Concurrent;
using System.Security.Claims;

namespace Aegis;

/// <summary>
/// Owns one independent <see cref="AegisEngine"/> per tenant, built lazily
/// on first request and cached thereafter -- mirroring AWS Verified
/// Permissions' "one policy store per tenant" isolation model
/// (https://docs.aws.amazon.com/verifiedpermissions/latest/userguide/policy-stores.html).
/// A tenant's policies, relationships, and cache live in a wholly separate
/// <see cref="AegisEngine"/> instance, so isolation is structural -- there's
/// no shared state another tenant's evaluation could leak through -- rather
/// than a filter a policy author has to remember to apply consistently.
/// </summary>
public sealed class MultiTenantAegisEngine : IAsyncDisposable
{
    private readonly Func<string, CancellationToken, Task<AegisEngine>> _engineFactory;
    private readonly ConcurrentDictionary<string, Lazy<Task<AegisEngine>>> _engines = new(StringComparer.Ordinal);

    public MultiTenantAegisEngine(Func<string, CancellationToken, Task<AegisEngine>> engineFactory)
    {
        _engineFactory = engineFactory;
    }

    public MultiTenantAegisEngine(Func<string, AegisEngine> engineFactory)
        : this((tenantId, _) => Task.FromResult(engineFactory(tenantId)))
    {
    }

    /// <summary>
    /// One directory per tenant under <paramref name="rootDirectory"/>, each
    /// loaded via <see cref="AegisEngine.Create(string, IAttributeProvider[])"/>
    /// the first time that tenant is requested, e.g. <c>Tenants/acme-sacco/Policies</c>.
    /// </summary>
    public static MultiTenantAegisEngine FromTenantDirectories(string rootDirectory) =>
        new(tenantId => AegisEngine.Create(Path.Combine(rootDirectory, tenantId)));

    /// <summary>
    /// Resolves <paramref name="tenantId"/>'s engine (building it on first
    /// use) and authorizes against it. See <see cref="AegisEngine.AuthorizeAsync(AegisPrincipal, AegisResource, string, CancellationToken)"/>.
    /// </summary>
    public async Task<AuthorizationDecision> AuthorizeAsync(
        string tenantId,
        AegisPrincipal principal,
        AegisResource resource,
        string action,
        CancellationToken cancellationToken = default)
    {
        var engine = await GetEngineAsync(tenantId, cancellationToken);
        return await engine.AuthorizeAsync(principal, resource, action, cancellationToken);
    }

    /// <summary>See <see cref="AegisEngine.AuthorizeAsync(ClaimsPrincipal, IClaimsPrincipalMapper, AegisResource, string, CancellationToken)"/>.</summary>
    public async Task<AuthorizationDecision> AuthorizeAsync(
        string tenantId,
        ClaimsPrincipal claimsPrincipal,
        IClaimsPrincipalMapper mapper,
        AegisResource resource,
        string action,
        CancellationToken cancellationToken = default)
    {
        var engine = await GetEngineAsync(tenantId, cancellationToken);
        return await engine.AuthorizeAsync(claimsPrincipal, mapper, resource, action, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var lazy in _engines.Values)
        {
            if (!lazy.IsValueCreated)
            {
                continue;
            }

            try
            {
                (await lazy.Value).Dispose();
            }
            catch
            {
                // Never finished building -- nothing to dispose.
            }
        }
    }

    private Task<AegisEngine> GetEngineAsync(string tenantId, CancellationToken cancellationToken)
    {
        var lazy = _engines.GetOrAdd(tenantId, id => new Lazy<Task<AegisEngine>>(() => BuildAsync(id, cancellationToken)));
        return lazy.Value;
    }

    /// <summary>
    /// A failed build (a bad policy file, a database that's down, etc.)
    /// evicts the cache entry instead of permanently caching a faulted
    /// task, so a transient failure doesn't poison every future request
    /// for this tenant -- the next call retries from scratch.
    /// </summary>
    private async Task<AegisEngine> BuildAsync(string tenantId, CancellationToken cancellationToken)
    {
        try
        {
            return await _engineFactory(tenantId, cancellationToken);
        }
        catch
        {
            _engines.TryRemove(tenantId, out _);
            throw;
        }
    }
}