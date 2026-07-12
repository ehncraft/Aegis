using System.Security.Claims;

using Aegis.Policies;

namespace Aegis;

/// <summary>Entry point for embedding Aegis directly in an application, no DI required.</summary>
public sealed class AegisEngine
{
    private readonly PolicyEvaluator _evaluator;
    private readonly IReadOnlyList<IAttributeProvider> _attributeProviders;

    private AegisEngine(PolicyEvaluator evaluator, IReadOnlyList<IAttributeProvider> attributeProviders)
    {
        _evaluator = evaluator;
        _attributeProviders = attributeProviders;
    }

    /// <summary>Loads every YAML policy file in <paramref name="policiesDirectory"/>.</summary>
    public static AegisEngine Create(string policiesDirectory, params IAttributeProvider[] attributeProviders)
    {
        var policies = YamlPolicyLoader.LoadDirectory(policiesDirectory);
        return new AegisEngine(new PolicyEvaluator(policies), attributeProviders);
    }

    public static AegisEngine FromPolicies(
        IEnumerable<ResourcePolicy> policies, params IAttributeProvider[] attributeProviders) =>
        new(new PolicyEvaluator(policies), attributeProviders);

    /// <summary>Loads policies from any <see cref="IPolicyProvider"/> -- a SQL Server table, etc.</summary>
    public static async Task<AegisEngine> CreateAsync(
        IPolicyProvider policyProvider,
        IReadOnlyList<IAttributeProvider>? attributeProviders = null,
        CancellationToken cancellationToken = default)
    {
        var policies = await policyProvider.LoadPoliciesAsync(cancellationToken);
        return new AegisEngine(new PolicyEvaluator(policies), attributeProviders ?? []);
    }

    /// <summary>
    /// Enriches <paramref name="principal"/>/<paramref name="resource"/> with
    /// any configured <see cref="IAttributeProvider"/> output (explicitly-set
    /// attributes always win over provider-supplied ones), then evaluates.
    /// </summary>
    public async Task<AuthorizationDecision> AuthorizeAsync(
        AegisPrincipal principal,
        AegisResource resource,
        string action,
        CancellationToken cancellationToken = default)
    {
        principal = await AttributeEnricher.EnrichAsync(principal, _attributeProviders, cancellationToken);
        resource = await AttributeEnricher.EnrichAsync(resource, _attributeProviders, cancellationToken);
        return _evaluator.Authorize(principal, resource, action);
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