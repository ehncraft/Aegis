using System.Security.Claims;

using Aegis.Audit;
using Aegis.Policies;
using Aegis.Relationships;

namespace Aegis;

/// <summary>Entry point for embedding Aegis directly in an application, no DI required.</summary>
public sealed class AegisEngine : IDisposable
{
    private readonly IReadOnlyList<ResourcePolicy> _policies;
    private readonly PolicyEvaluator _evaluator;
    private readonly IReadOnlyList<IAttributeProvider> _attributeProviders;
    private readonly DecisionCache? _cache;
    private readonly IAuditLogStore? _auditLogStore;

    private AegisEngine(
        IReadOnlyList<ResourcePolicy> policies,
        PolicyEvaluator evaluator,
        IReadOnlyList<IAttributeProvider> attributeProviders,
        DecisionCache? cache = null,
        IAuditLogStore? auditLogStore = null)
    {
        _policies = policies;
        _evaluator = evaluator;
        _attributeProviders = attributeProviders;
        _cache = cache;
        _auditLogStore = auditLogStore;
    }

    /// <summary>
    /// Returns a new engine, sharing this one's policies and attribute
    /// providers, that caches decisions for <paramref name="options"/>'s
    /// <see cref="DecisionCacheOptions.Duration"/>. Caching happens before
    /// attribute-provider enrichment, so a cache hit skips that I/O too,
    /// not just re-evaluation.
    /// </summary>
    public AegisEngine WithDecisionCache(DecisionCacheOptions options) =>
        new(_policies, _evaluator, _attributeProviders, new DecisionCache(options), _auditLogStore);

    /// <summary>
    /// Returns a new engine, sharing this one's policies, attribute
    /// providers, and cache, whose derived roles can also test entity-hierarchy
    /// membership (<c>in</c>, Cedar-style) against the tuples <paramref name="relationshipProvider"/>
    /// supplies. Opt-in and rebuilt eagerly (not lazily) for the same reason
    /// <see cref="Create"/> validates eagerly -- a bad tuple fails here, not
    /// on the first request that references it.
    /// </summary>
    public async Task<AegisEngine> WithRelationshipsAsync(
        IRelationshipProvider relationshipProvider, CancellationToken cancellationToken = default)
    {
        var entityParents = await relationshipProvider.LoadEntityParentsAsync(cancellationToken);
        var graph = new RelationshipGraph(entityParents);
        return new AegisEngine(
            _policies, new PolicyEvaluator(_policies, graph), _attributeProviders, _cache, _auditLogStore);
    }

    /// <summary>
    /// Returns a new engine, sharing this one's policies/attribute providers/
    /// cache, that records every decision to <paramref name="store"/> --
    /// including decisions served from <see cref="WithDecisionCache"/>, not
    /// just freshly-evaluated ones, since skipping cache hits would leave
    /// silent gaps in the audit trail for repeat access. The write is
    /// awaited, not fire-and-forget: a failed audit write fails the
    /// authorization call rather than silently losing a compliance record.
    /// That's a real latency/availability tradeoff for a store that's slow
    /// or down -- an intentional default for a compliance feature, not an
    /// oversight.
    /// </summary>
    public AegisEngine WithAuditLog(IAuditLogStore store) =>
        new(_policies, _evaluator, _attributeProviders, _cache, store);

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
        return new AegisEngine(policies, new PolicyEvaluator(policies), attributeProviders);
    }

    /// <summary>Validates before returning; see <see cref="PolicyValidator"/>.</summary>
    public static AegisEngine FromPolicies(
        IEnumerable<ResourcePolicy> policies, params IAttributeProvider[] attributeProviders)
    {
        var policyList = policies as IReadOnlyList<ResourcePolicy> ?? [.. policies];
        PolicyValidator.Validate(policyList);
        return new AegisEngine(policyList, new PolicyEvaluator(policyList), attributeProviders);
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
        return new AegisEngine(policies, new PolicyEvaluator(policies), attributeProviders ?? []);
    }

    /// <summary>
    /// Enriches <paramref name="principal"/>/<paramref name="resource"/> with
    /// any configured <see cref="IAttributeProvider"/> output (explicitly-set
    /// attributes always win over provider-supplied ones), then evaluates.
    /// If <see cref="WithDecisionCache"/> was used, an identical call within
    /// the cache's <see cref="DecisionCacheOptions.Duration"/> skips both
    /// enrichment and evaluation.
    /// </summary>
    public Task<AuthorizationDecision> AuthorizeAsync(
        AegisPrincipal principal,
        AegisResource resource,
        string action,
        CancellationToken cancellationToken = default) =>
        AuthorizeAsync(principal, resource, action, actionProperties: null, context: null, cancellationToken);

    /// <summary>
    /// <paramref name="actionProperties"/>/<paramref name="context"/> flow
    /// into the <c>action</c>/<c>context</c> expression scopes -- see
    /// <see cref="PolicyEvaluator.Authorize(AegisPrincipal, AegisResource, string, IReadOnlyDictionary{string, object?}?, IReadOnlyDictionary{string, object?}?)"/>.
    /// Primarily for <c>Aegis.AuthZen</c>, whose request shape carries both,
    /// but usable from any caller.
    /// </summary>
    public async Task<AuthorizationDecision> AuthorizeAsync(
        AegisPrincipal principal,
        AegisResource resource,
        string action,
        IReadOnlyDictionary<string, object?>? actionProperties,
        IReadOnlyDictionary<string, object?>? context,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = _cache is null
            ? null
            : DecisionCache.BuildKey(principal, resource, action, actionProperties, context);

        AuthorizationDecision decision;
        if (cacheKey is not null && _cache!.TryGet(cacheKey, out var cachedDecision))
        {
            decision = cachedDecision;
        }
        else
        {
            var enrichedPrincipal = await AttributeEnricher.EnrichAsync(principal, _attributeProviders, cancellationToken);
            var enrichedResource = await AttributeEnricher.EnrichAsync(resource, _attributeProviders, cancellationToken);
            decision = _evaluator.Authorize(enrichedPrincipal, enrichedResource, action, actionProperties, context);

            if (cacheKey is not null)
            {
                _cache!.Set(cacheKey, decision);
            }
        }

        if (_auditLogStore is not null)
        {
            await _auditLogStore.RecordAsync(
                new AuditLogEntry
                {
                    PrincipalId = principal.Id,
                    ResourceKind = resource.Kind,
                    ResourceId = resource.Id,
                    Action = action,
                    Allowed = decision.Allowed,
                    Explanation = decision.Explanation,
                    Timestamp = DateTimeOffset.UtcNow,
                },
                cancellationToken);
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