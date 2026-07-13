using Aegis.Expressions;
using Aegis.Policies;
using Aegis.Relationships;

namespace Aegis;

/// <summary>
/// Evaluates a fixed set of resource policies against a principal/resource/action
/// triple, tree-walking each matched rule's conditions and recording every
/// condition it evaluated so the resulting decision is explainable.
/// </summary>
public sealed class PolicyEvaluator
{
    private readonly Dictionary<string, ResourcePolicy> _policiesByResource;
    private readonly Dictionary<string, VariableScope> _variableScopesByResource;
    private readonly Dictionary<string, CompiledExpression> _compiledExpressions = new();
    private readonly RelationshipGraph _relationshipGraph;

    public PolicyEvaluator(IEnumerable<ResourcePolicy> policies, RelationshipGraph? relationshipGraph = null)
    {
        var policyList = policies as IReadOnlyList<ResourcePolicy> ?? [.. policies];
        _policiesByResource = policyList.ToDictionary(p => p.Resource, StringComparer.OrdinalIgnoreCase);
        _relationshipGraph = relationshipGraph ?? RelationshipGraph.Empty;

        // Built eagerly, not lazily, so a bad variable expression fails at
        // construction (already validated by PolicyValidator by then, but
        // AegisEngine.FromPolicies etc. all validate before constructing
        // this type) rather than on the first request that references it.
        _variableScopesByResource = policyList.ToDictionary(
            p => p.Resource, BuildVariableScope, StringComparer.OrdinalIgnoreCase);
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
        var context = BuildContext(principal, resource, action, _variableScopesByResource[policy.Resource]);

        if (rule.Allow.Roles is { Count: > 0 } roles)
        {
            allowed |= EvaluateRoles(roles, policy, principal, context, conditions);
        }

        if (!string.IsNullOrWhiteSpace(rule.Allow.When))
        {
            var compiled = GetOrCompile(rule.Allow.When);
            var whenResult = compiled.EvaluateBoolean(context);
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

    /// <summary>
    /// When none of <paramref name="roles"/> names a derived role, this
    /// produces the exact same single "intersects" explanation as before
    /// derived roles existed. Only once a derived role is actually in play
    /// does it switch to explaining each entry individually -- a static
    /// role as "principal.roles contains 'X'", a derived one as its
    /// underlying condition.
    /// </summary>
    private bool EvaluateRoles(
        List<string> roles,
        ResourcePolicy policy,
        AegisPrincipal principal,
        EvaluationContext context,
        List<ConditionExplanation> conditions)
    {
        if (!roles.Any(policy.DerivedRoles.ContainsKey))
        {
            var roleMatch = principal.Roles.Any(r => roles.Contains(r, StringComparer.OrdinalIgnoreCase));
            conditions.Add(new ConditionExplanation
            {
                Expression = $"principal.roles intersects [{string.Join(", ", roles)}]",
                Result = roleMatch,
            });
            return roleMatch;
        }

        var anyMatch = false;
        foreach (var roleName in roles)
        {
            bool result;
            string expression;

            if (policy.DerivedRoles.TryGetValue(roleName, out var derivedRole))
            {
                (result, expression) = EvaluateDerivedRole(roleName, derivedRole, principal, context);
            }
            else
            {
                result = principal.Roles.Contains(roleName, StringComparer.OrdinalIgnoreCase);
                expression = $"principal.roles contains '{roleName}'";
            }

            conditions.Add(new ConditionExplanation { Expression = expression, Result = result });
            anyMatch |= result;
        }

        return anyMatch;
    }

    /// <summary>
    /// ABAC-style (<see cref="DerivedRoleDefinition.When"/>) evaluates a
    /// boolean condition, unchanged. ReBAC-style (<see cref="DerivedRoleDefinition.In"/>)
    /// evaluates its <c>id</c> expression to get the target entity's id,
    /// then asks the relationship graph whether the principal -- always
    /// "User:{principal.Id}", matching the tuple format this feature
    /// standardized on -- is a (transitive) member of that entity's
    /// hierarchy.
    /// </summary>
    private (bool Result, string Expression) EvaluateDerivedRole(
        string roleName, DerivedRoleDefinition derivedRole, AegisPrincipal principal, EvaluationContext context)
    {
        if (derivedRole.When is not null)
        {
            var compiled = GetOrCompile(derivedRole.When);
            return (compiled.EvaluateBoolean(context), $"derived role '{roleName}': {compiled.Source}");
        }

        var hierarchyCheck = derivedRole.In!;
        var idExpression = GetOrCompile(hierarchyCheck.Id);
        var id = idExpression.Evaluate(context)?.ToString() ?? string.Empty;
        var ancestor = new EntityUid(hierarchyCheck.Type, id);
        var descendant = new EntityUid("User", principal.Id);
        var result = _relationshipGraph.IsIn(descendant, ancestor);

        return (result, $"derived role '{roleName}': {descendant} in {ancestor}");
    }

    private VariableScope BuildVariableScope(ResourcePolicy policy)
    {
        if (policy.Variables.Count == 0)
        {
            return VariableScope.Empty;
        }

        var compiled = new Dictionary<string, CompiledExpression>(StringComparer.Ordinal);
        foreach (var (name, expression) in policy.Variables)
        {
            compiled[name] = GetOrCompile(expression);
        }

        return new VariableScope(compiled);
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

    private static EvaluationContext BuildContext(
        AegisPrincipal principal, AegisResource resource, string action, VariableScope variableScope)
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
            .WithScope("action", actionScope)
            .WithVariables(variableScope);
    }
}