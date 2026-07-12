using System.Security.Claims;

using Aegis.Policies;

namespace Aegis;

/// <summary>Entry point for embedding Aegis directly in an application, no DI required.</summary>
public sealed class AegisEngine : IDisposable
{
    private readonly PolicyEvaluator _evaluator;
    private readonly IReadOnlyList<IAttributeProvider> _attributeProviders;
    private readonly DecisionCache? _cache;

    private AegisEngine(
        PolicyEvaluator evaluator, IReadOnlyList<IAttributeProvider> attributeProviders, DecisionCache? cache = null)
    {
        _evaluator = evaluator;
        _attributeProviders = attributeProviders;
        _cache = cache;
    }

    /// <summary>
    /// Returns a new engine, sharing this one's policies and attribute
    /// providers, that caches decisions for <paramref name="options"/>'s
    /// <see cref="DecisionCacheOptions.Duration"/>. Caching happens before
    /// attribute-provider enrichment, so a cache hit skips that I/O too,
    /// not just re-evaluation.
    /// </summary>
    public AegisEngine WithDecisionCache(DecisionCacheOptions options) =>
        new(_evaluator, _attributeProviders, new DecisionCache(options));

    public void Dispose() => _cache?.Dispose();

    /// <summary>
    /// Loads every YAML policy file in <paramref name="policiesDirectory"/>.
    /// Validates before returning -- see <see cref="PolicyValidator"/> --
    /// so a bad policy fails here, not on the first request that hits it.
    /// </summary>
    public static AegisEngine Create(string policiesDirectory, params IAttributeProvider[] attributeProviders)
    {
        var policies = YamlPolicyLoader.LoadDirectory(policiesDirectory);
        PolicyValidator.Validate(policies);
        return new AegisEngine(new PolicyEvaluator(policies), attributeProviders);
    }

    /// <summary>Validates before returning; see <see cref="PolicyValidator"/>.</summary>
    public static AegisEngine FromPolicies(
        IEnumerable<ResourcePolicy> policies, params IAttributeProvider[] attributeProviders)
    {
        var policyList = policies as IReadOnlyList<ResourcePolicy> ?? [.. policies];
        PolicyValidator.Validate(policyList);
        return new AegisEngine(new PolicyEvaluator(policyList), attributeProviders);
    }

    /// <summary>
    /// Loads policies from any <see cref="IPolicyProvider"/> -- a SQL
    /// Server table, etc. Validates before returning; see
    /// <see cref="PolicyValidator"/>.
    /// </summary>
    public static async Task<AegisEngine> CreateAsync(
        IPolicyProvider policyProvider,
        IReadOnlyList<IAttributeProvider>? attributeProviders = null,
        CancellationToken cancellationToken = default)
    {
        var policies = await policyProvider.LoadPoliciesAsync(cancellationToken);
        PolicyValidator.Validate(policies);
        return new AegisEngine(new PolicyEvaluator(policies), attributeProviders ?? []);
    }

    /// <summary>
    /// Enriches <paramref name="principal"/>/<paramref name="resource"/> with
    /// any configured <see cref="IAttributeProvider"/> output (explicitly-set
    /// attributes always win over provider-supplied ones), then evaluates.
    /// If <see cref="WithDecisionCache"/> was used, an identical call within
    /// the cache's <see cref="DecisionCacheOptions.Duration"/> skips both
    /// enrichment and evaluation.
    /// </summary>
    public async Task<AuthorizationDecision> AuthorizeAsync(
        AegisPrincipal principal,
        AegisResource resource,
        string action,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = _cache is null ? null : DecisionCache.BuildKey(principal, resource, action);
        if (cacheKey is not null && _cache!.TryGet(cacheKey, out var cachedDecision))
        {
            return cachedDecision;
        }

        principal = await AttributeEnricher.EnrichAsync(principal, _attributeProviders, cancellationToken);
        resource = await AttributeEnricher.EnrichAsync(resource, _attributeProviders, cancellationToken);
        var decision = _evaluator.Authorize(principal, resource, action);

        if (cacheKey is not null)
        {
            _cache!.Set(cacheKey, decision);
        }

        return decision;
    }

    /// <summary>
    /// Maps <paramref name="claimsPrincipal"/> -- typically <c>HttpContext.User</c>
    /// -- via <paramref name="mapper"/> before authorizing. Framework-agnostic
    /// (<see cref="ClaimsPrincipal"/> is BCL, not ASP.NET Core-specific); see
    /// <c>Aegis.AspNetCore</c> for the <c>HttpContext</c> convenience overload.
    /// </summary>
    public Task<AuthorizationDecision> AuthorizeAsync(
        ClaimsPrincipal claimsPrincipal,
        IClaimsPrincipalMapper mapper,
        AegisResource resource,
        string action,
        CancellationToken cancellationToken = default) =>
        AuthorizeAsync(mapper.Map(claimsPrincipal), resource, action, cancellationToken);
}