using Aegis.Policies;

namespace Aegis;

/// <summary>Entry point for embedding Aegis directly in an application, no DI required.</summary>
public sealed class AegisEngine
{
    private readonly PolicyEvaluator _evaluator;

    private AegisEngine(PolicyEvaluator evaluator)
    {
        _evaluator = evaluator;
    }

    /// <summary>Loads every YAML policy file in <paramref name="policiesDirectory"/>.</summary>
    public static AegisEngine Create(string policiesDirectory)
    {
        var policies = YamlPolicyLoader.LoadDirectory(policiesDirectory);
        return new AegisEngine(new PolicyEvaluator(policies));
    }

    public static AegisEngine FromPolicies(IEnumerable<ResourcePolicy> policies) =>
        new(new PolicyEvaluator(policies));

    public Task<AuthorizationDecision> AuthorizeAsync(
        AegisPrincipal principal,
        AegisResource resource,
        string action,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_evaluator.Authorize(principal, resource, action));
    }
}
