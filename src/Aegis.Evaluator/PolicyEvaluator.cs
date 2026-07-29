using Aegis.Cedar;
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
    private readonly Dictionary<string, CedarExpr> _compiledCedarExpressions = new();
    private readonly RelationshipGraph _relationshipGraph;
    private readonly string _principalEntityType;

    /// <param name="principalEntityType">
    /// The entity type name used for the descendant side of every
    /// <see cref="DerivedRoleDefinition.In"/> hierarchy check -- i.e. a
    /// principal is treated as <c>{principalEntityType}:{principal.Id}</c>
    /// when testing <c>in</c> membership against a <see cref="RelationshipGraph"/>.
    /// Defaults to <c>"User"</c>, unchanged from before this was
    /// configurable. Must agree with whatever entity type a caller's
    /// <see cref="IRelationshipProvider"/> registers principals under --
    /// e.g. a consumer whose principals are <c>Membership</c> entities, not
    /// <c>User</c>, must pass <c>"Membership"</c> here or every <c>in</c>
    /// check will silently never match.
    /// </param>
    public PolicyEvaluator(
        IEnumerable<ResourcePolicy> policies,
        RelationshipGraph? relationshipGraph = null,
        string principalEntityType = "User")
    {
        var policyList = policies as IReadOnlyList<ResourcePolicy> ?? [.. policies];
        _policiesByResource = policyList.ToDictionary(p => p.Resource, StringComparer.OrdinalIgnoreCase);
        _relationshipGraph = relationshipGraph ?? RelationshipGraph.Empty;
        _principalEntityType = principalEntityType;

        // Built eagerly, not lazily, so a bad variable expression fails at
        // construction (already validated by PolicyValidator by then, but
        // AegisEngine.FromPolicies etc. all validate before constructing
        // this type) rather than on the first request that references it.
        _variableScopesByResource = policyList.ToDictionary(
            p => p.Resource, BuildVariableScope, StringComparer.OrdinalIgnoreCase);
    }

    public AuthorizationDecision Authorize(AegisPrincipal principal, AegisResource resource, string action) =>
        Authorize(principal, resource, action, actionProperties: null, context: null);

    /// <summary>
    /// <paramref name="actionProperties"/> merge into the existing
    /// <c>action</c> expression scope alongside <c>action.name</c>;
    /// <paramref name="context"/> becomes a new top-level <c>context</c>
    /// scope. Both exist primarily for <c>Aegis.AuthZen</c>, whose request
    /// shape carries them, but either is usable from any caller.
    /// </summary>
    public AuthorizationDecision Authorize(
        AegisPrincipal principal,
        AegisResource resource,
        string action,
        IReadOnlyDictionary<string, object?>? actionProperties,
        IReadOnlyDictionary<string, object?>? context)
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

        if (!policy.Actions.TryGetValue(action, out var rule) || (rule.Allow is null && rule.Forbid is null))
        {
            return AuthorizationDecision.Deny(new DecisionExplanation
            {
                Effect = "deny",
                MatchedPolicy = policyName,
                Reason = $"No rule for action '{action}' on resource '{resource.Kind}'",
            });
        }

        var conditions = new List<ConditionExplanation>();
        var evaluationContext = BuildContext(
            principal, resource, action, actionProperties, context, _variableScopesByResource[policy.Resource]);
        var cedarContext = BuildCedarContext(principal, resource, action, actionProperties, context);

        var allowed = false;

        if (rule.Allow is not null)
        {
            if (rule.Allow.Roles is { Count: > 0 } allowRoles)
            {
                allowed |= EvaluateRoles(allowRoles, policy, principal, evaluationContext, cedarContext, conditions, effectPrefix: null);
            }

            if (!string.IsNullOrWhiteSpace(rule.Allow.When))
            {
                var whenResult = EvaluateWhen(rule.Allow.When, rule.Allow.Language, evaluationContext, cedarContext, out var renderedExpression);
                conditions.Add(new ConditionExplanation { Expression = renderedExpression, Result = whenResult });
                allowed |= whenResult;
            }
        }

        var forbidden = false;

        if (rule.Forbid is not null)
        {
            if (rule.Forbid.Roles is { Count: > 0 } forbidRoles)
            {
                forbidden |= EvaluateRoles(forbidRoles, policy, principal, evaluationContext, cedarContext, conditions, "forbid");
            }

            if (!string.IsNullOrWhiteSpace(rule.Forbid.When))
            {
                var whenResult = EvaluateWhen(rule.Forbid.When, rule.Forbid.Language, evaluationContext, cedarContext, out var renderedExpression);
                conditions.Add(new ConditionExplanation { Expression = $"forbid: {renderedExpression}", Result = whenResult });
                forbidden |= whenResult;
            }
        }

        // An allow block with neither `roles` nor `when` matches nothing —
        // deny by default rather than treating it as an unconditional allow.
        // A matching `forbid` always wins over a matching `allow`, the same
        // as Cedar's `forbid` overriding any `permit`.
        var effectAllowed = allowed && !forbidden;
        var explanation = new DecisionExplanation
        {
            Effect = effectAllowed ? "allow" : "deny",
            MatchedPolicy = policyName,
            MatchedRule = action,
            Conditions = conditions,
            Reason = effectAllowed
                ? null
                : forbidden ? "Denied by forbid rule" : "No allow condition was satisfied",
        };

        return effectAllowed ? AuthorizationDecision.Allow(explanation) : AuthorizationDecision.Deny(explanation);
    }

    /// <summary>
    /// When none of <paramref name="roles"/> names a derived role, this
    /// produces the exact same single "intersects" explanation as before
    /// derived roles existed. Only once a derived role is actually in play
    /// does it switch to explaining each entry individually -- a static
    /// role as "principal.roles contains 'X'", a derived one as its
    /// underlying condition.
    ///
    /// <paramref name="effectPrefix"/> is <c>null</c> for an <c>allow</c>
    /// rule (unprefixed, unchanged from before <c>forbid</c> existed) and
    /// <c>"forbid"</c> for a <c>forbid</c> rule, so a decision's flat
    /// <see cref="ConditionExplanation"/> list stays unambiguous about which
    /// effect each entry came from.
    /// </summary>
    private bool EvaluateRoles(
        List<string> roles,
        ResourcePolicy policy,
        AegisPrincipal principal,
        EvaluationContext context,
        CedarEvaluationContext cedarContext,
        List<ConditionExplanation> conditions,
        string? effectPrefix)
    {
        if (!roles.Any(policy.DerivedRoles.ContainsKey))
        {
            var roleMatch = principal.Roles.Any(r => roles.Contains(r, StringComparer.OrdinalIgnoreCase));
            conditions.Add(new ConditionExplanation
            {
                Expression = WithPrefix(effectPrefix, $"principal.roles intersects [{string.Join(", ", roles)}]"),
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
                (result, expression) = EvaluateDerivedRole(roleName, derivedRole, principal, context, cedarContext);
            }
            else
            {
                result = principal.Roles.Contains(roleName, StringComparer.OrdinalIgnoreCase);
                expression = $"principal.roles contains '{roleName}'";
            }

            conditions.Add(new ConditionExplanation { Expression = WithPrefix(effectPrefix, expression), Result = result });
            anyMatch |= result;
        }

        return anyMatch;
    }

    private static string WithPrefix(string? effectPrefix, string expression) =>
        effectPrefix is null ? expression : $"{effectPrefix}: {expression}";

    /// <summary>
    /// ABAC-style (<see cref="DerivedRoleDefinition.When"/>) evaluates a
    /// boolean condition, unchanged (dispatching on <see cref="DerivedRoleDefinition.Language"/>
    /// the same way <see cref="EvaluateWhen"/> does, for symmetry -- the
    /// Cedar lowerer doesn't actually emit this shape today, uniformly
    /// preferring a synthesized <c>When</c> on the owning <c>AllowRule</c>/
    /// <c>ForbidRule</c> instead, but a hand-built policy could).
    /// ReBAC-style (<see cref="DerivedRoleDefinition.In"/>) evaluates its
    /// <c>id</c> expression to get the target entity's id, then asks the
    /// relationship graph whether the principal -- as
    /// <c>{_principalEntityType}:{principal.Id}</c>, matching the tuple
    /// format this feature standardized on -- is a (transitive) member of
    /// that entity's hierarchy.
    /// </summary>
    private (bool Result, string Expression) EvaluateDerivedRole(
        string roleName,
        DerivedRoleDefinition derivedRole,
        AegisPrincipal principal,
        EvaluationContext context,
        CedarEvaluationContext cedarContext)
    {
        if (derivedRole.When is not null)
        {
            var whenResult = EvaluateWhen(derivedRole.When, derivedRole.Language, context, cedarContext, out var renderedExpression);
            return (whenResult, $"derived role '{roleName}': {renderedExpression}");
        }

        var hierarchyCheck = derivedRole.In!;
        var idExpression = GetOrCompile(hierarchyCheck.Id);
        var id = idExpression.Evaluate(context)?.ToString() ?? string.Empty;
        var ancestor = new EntityUid(hierarchyCheck.Type, id);
        var descendant = new EntityUid(_principalEntityType, principal.Id);
        var result = _relationshipGraph.IsIn(descendant, ancestor);

        return (result, $"derived role '{roleName}': {descendant} in {ancestor}");
    }

    /// <summary>
    /// Dispatches a rule's <c>When</c> body to the grammar its <c>Language</c>
    /// discriminator names -- Aegis's own <c>${name}</c> expression grammar
    /// (default, <see cref="Aegis.Expressions"/>) or Cedar (<see cref="Aegis.Cedar"/>'s
    /// <see cref="CedarConditionEvaluator"/>), re-parsing and caching Cedar
    /// text the same way <see cref="GetOrCompile"/> already caches compiled
    /// Aegis expressions.
    /// </summary>
    private bool EvaluateWhen(
        string when,
        string? language,
        EvaluationContext context,
        CedarEvaluationContext cedarContext,
        out string renderedExpression)
    {
        if (string.Equals(language, "cedar", StringComparison.Ordinal))
        {
            renderedExpression = when;
            return CedarConditionEvaluator.EvaluateBoolean(GetOrParseCedar(when), cedarContext);
        }

        var compiled = GetOrCompile(when);
        renderedExpression = compiled.Source;
        return compiled.EvaluateBoolean(context);
    }

    private CedarExpr GetOrParseCedar(string source)
    {
        if (!_compiledCedarExpressions.TryGetValue(source, out var expr))
        {
            expr = CedarParser.ParseCondition(source);
            _compiledCedarExpressions[source] = expr;
        }

        return expr;
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
        AegisPrincipal principal,
        AegisResource resource,
        string action,
        IReadOnlyDictionary<string, object?>? actionProperties,
        IReadOnlyDictionary<string, object?>? context,
        VariableScope variableScope)
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
        if (actionProperties is not null)
        {
            foreach (var (key, value) in actionProperties)
            {
                actionScope[key] = value;
            }
        }

        var evaluationContext = new EvaluationContext()
            .WithScope("principal", principalScope)
            .WithScope("resource", resourceScope)
            .WithScope("action", actionScope)
            .WithVariables(variableScope);

        if (context is not null)
        {
            evaluationContext.WithScope("context", context);
        }

        return evaluationContext;
    }

    private CedarEvaluationContext BuildCedarContext(
        AegisPrincipal principal,
        AegisResource resource,
        string action,
        IReadOnlyDictionary<string, object?>? actionProperties,
        IReadOnlyDictionary<string, object?>? context) =>
        new()
        {
            Principal = principal,
            Resource = resource,
            Action = action,
            ActionProperties = actionProperties,
            Context = context,
            RelationshipGraph = _relationshipGraph,
            PrincipalEntityType = _principalEntityType,
        };
}