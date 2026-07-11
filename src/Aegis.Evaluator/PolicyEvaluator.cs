using Aegis.Expressions;
using Aegis.Policies;

namespace Aegis;

/// <summary>
/// Evaluates a fixed set of resource policies against a principal/resource/action
/// triple, tree-walking each matched rule's conditions and recording every
/// condition it evaluated so the resulting decision is explainable.
/// </summary>
public sealed class PolicyEvaluator
{
    private readonly Dictionary<string, ResourcePolicy> _policiesByResource;
    private readonly Dictionary<string, CompiledExpression> _compiledExpressions = new();

    public PolicyEvaluator(IEnumerable<ResourcePolicy> policies)
    {
        _policiesByResource = policies.ToDictionary(p => p.Resource, StringComparer.OrdinalIgnoreCase);
    }

    public AuthorizationDecision Authorize(AegisPrincipal principal, AegisResource resource, string action)
    {
        if (!_policiesByResource.TryGetValue(resource.Kind, out var policy))
        {
            return AuthorizationDecision.Deny(new DecisionExplanation
            {
                Effect = "deny",
                Reason = $"No policy found for resource '{resource.Kind}'",
            });
        }

        var policyName = policy.Name ?? policy.Resource;

        if (!policy.Actions.TryGetValue(action, out var rule) || rule.Allow is null)
        {
            return AuthorizationDecision.Deny(new DecisionExplanation
            {
                Effect = "deny",
                MatchedPolicy = policyName,
                Reason = $"No rule for action '{action}' on resource '{resource.Kind}'",
            });
        }

        var conditions = new List<ConditionExplanation>();
        var allowed = false;

        if (rule.Allow.Roles is { Count: > 0 } roles)
        {
            var roleMatch = principal.Roles.Any(r => roles.Contains(r, StringComparer.OrdinalIgnoreCase));
            conditions.Add(new ConditionExplanation
            {
                Expression = $"principal.roles intersects [{string.Join(", ", roles)}]",
                Result = roleMatch,
            });
            allowed |= roleMatch;
        }

        if (!string.IsNullOrWhiteSpace(rule.Allow.When))
        {
            var compiled = GetOrCompile(rule.Allow.When);
            var whenResult = compiled.EvaluateBoolean(BuildContext(principal, resource, action));
            conditions.Add(new ConditionExplanation { Expression = compiled.Source, Result = whenResult });
            allowed |= whenResult;
        }

        // An allow block with neither `roles` nor `when` matches nothing —
        // deny by default rather than treating it as an unconditional allow.
        var explanation = new DecisionExplanation
        {
            Effect = allowed ? "allow" : "deny",
            MatchedPolicy = policyName,
            MatchedRule = action,
            Conditions = conditions,
            Reason = allowed ? null : "No allow condition was satisfied",
        };

        return allowed ? AuthorizationDecision.Allow(explanation) : AuthorizationDecision.Deny(explanation);
    }

    private CompiledExpression GetOrCompile(string source)
    {
        if (!_compiledExpressions.TryGetValue(source, out var compiled))
        {
            compiled = CompiledExpression.Parse(source);
            _compiledExpressions[source] = compiled;
        }

        return compiled;
    }

    private static EvaluationContext BuildContext(AegisPrincipal principal, AegisResource resource, string action)
    {
        var principalScope = new Dictionary<string, object?>(principal.Attributes)
        {
            ["id"] = principal.Id,
            ["roles"] = principal.Roles,
        };

        var resourceScope = new Dictionary<string, object?>(resource.Attributes)
        {
            ["id"] = resource.Id,
            ["kind"] = resource.Kind,
        };

        var actionScope = new Dictionary<string, object?> { ["name"] = action };

        return new EvaluationContext()
            .WithScope("principal", principalScope)
            .WithScope("resource", resourceScope)
            .WithScope("action", actionScope);
    }
}
