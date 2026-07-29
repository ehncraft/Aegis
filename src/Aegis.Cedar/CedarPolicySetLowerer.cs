using Aegis.Policies;

namespace Aegis.Cedar;

/// <summary>
/// Lowers every <c>permit</c>/<c>forbid</c> parsed from one or more
/// <c>.cedar</c> sources into the <see cref="ResourcePolicy"/> set they
/// collectively describe -- Aegis issue #94 milestone 2. See the design
/// notes in the plan this was built from for the two decisions this makes
/// concrete: resource/action scope constraints become compile-time dispatch
/// decisions (which <see cref="ResourcePolicy"/>/<see cref="ActionRule"/>
/// slot a policy contributes to); principal scope constraints (and any
/// <c>is</c>/<c>in</c> on resource) become a runtime-evaluated condition,
/// synthesized as Cedar source text on <see cref="AllowRule.When"/>/
/// <see cref="ForbidRule.When"/> with <c>Language = "cedar"</c>.
///
/// Internal, not public -- its <c>CedarPolicy</c> input type is itself
/// internal (matching <see cref="CedarParser"/>'s own accessibility), so
/// this is a building block for <see cref="CedarPolicyProvider"/> (the
/// actual public entry point for this milestone), not something an
/// external caller invokes directly.
/// </summary>
internal static class CedarPolicySetLowerer
{
    /// <summary>
    /// One resolved <c>permit</c>/<c>forbid</c>, after its scope
    /// constraints and <c>when</c>/<c>unless</c> conditions have all been
    /// folded down to a single <see cref="Condition"/> -- <c>null</c>
    /// <see cref="ActionNames"/> means the action scope was unconstrained
    /// (<c>CedarAnyScope</c>) and still needs expanding against this
    /// resource kind's action vocabulary (see <see cref="Lower"/>'s second
    /// pass).
    /// </summary>
    private sealed record ResolvedClause(
        string ResourceKind, IReadOnlyList<string>? ActionNames, CedarEffect Effect, CedarExpr Condition);

    public static IReadOnlyList<ResourcePolicy> Lower(IReadOnlyList<CedarPolicy> policies, CedarLoadOptions options)
    {
        var resolved = new List<ResolvedClause>(policies.Count);
        foreach (var policy in policies)
        {
            var (resourceKind, resourceCondition) = ResolveResourceScope(policy.ResourceScope, options);
            var principalCondition = ResolvePrincipalScope(policy.PrincipalScope, options);
            var actionNames = ResolveActionScope(policy.ActionScope);
            var whenUnlessCondition = CombineWhenUnless(policy.Conditions);
            var condition = CombineAnd(principalCondition, resourceCondition, whenUnlessCondition);

            resolved.Add(new ResolvedClause(resourceKind, actionNames, policy.Effect, condition));
        }

        var vocabularyByKind = BuildActionVocabulary(resolved);
        var flattened = ExpandActionScopes(resolved, vocabularyByKind);

        return BuildResourcePolicies(flattened);
    }

    /// <summary>
    /// Pass 1: the union of every concrete action name any policy in this
    /// batch names for a given resource kind -- what an <c>action</c>-unconstrained
    /// policy for that same resource kind expands against.
    /// </summary>
    private static Dictionary<string, HashSet<string>> BuildActionVocabulary(IReadOnlyList<ResolvedClause> resolved)
    {
        var vocabularyByKind = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var clause in resolved)
        {
            if (clause.ActionNames is null)
            {
                continue;
            }

            if (!vocabularyByKind.TryGetValue(clause.ResourceKind, out var actions))
            {
                actions = new HashSet<string>(StringComparer.Ordinal);
                vocabularyByKind[clause.ResourceKind] = actions;
            }

            foreach (var actionName in clause.ActionNames)
            {
                actions.Add(actionName);
            }
        }

        return vocabularyByKind;
    }

    /// <summary>
    /// Pass 2: resolves every <c>ActionNames is null</c> clause (an
    /// <c>action</c>-unconstrained policy) against pass 1's vocabulary for
    /// its resource kind, then flattens every clause -- expanded or
    /// already-concrete -- into one <c>(resourceKind, actionName)</c> tuple
    /// per action it applies to.
    /// </summary>
    private static List<(string ResourceKind, string ActionName, CedarEffect Effect, CedarExpr Condition)> ExpandActionScopes(
        IReadOnlyList<ResolvedClause> resolved, Dictionary<string, HashSet<string>> vocabularyByKind)
    {
        var flattened = new List<(string, string, CedarEffect, CedarExpr)>();
        foreach (var clause in resolved)
        {
            var actionNames = clause.ActionNames;
            if (actionNames is null)
            {
                if (!vocabularyByKind.TryGetValue(clause.ResourceKind, out var vocabulary) || vocabulary.Count == 0)
                {
                    throw new CedarLoweringException(
                        $"Cannot lower a policy with an unconstrained action scope for resource kind " +
                        $"'{clause.ResourceKind}' -- no other policy in this batch establishes a concrete " +
                        "action for it.");
                }

                actionNames = [.. vocabulary];
            }

            foreach (var actionName in actionNames)
            {
                flattened.Add((clause.ResourceKind, actionName, clause.Effect, clause.Condition));
            }
        }

        return flattened;
    }

    /// <summary>
    /// Merge step: N permits + M forbids for the same <c>(resourceKind,
    /// actionName)</c> collapse into one <see cref="ActionRule"/> --
    /// <see cref="AllowRule"/>'s condition is the OR of every permit's
    /// condition (Cedar authorizes if <em>any</em> permit matches);
    /// <see cref="ForbidRule"/>'s is the OR of every forbid's (Cedar denies
    /// if <em>any</em> forbid matches). An unconditional clause's
    /// <see cref="ResolvedClause.Condition"/> is already the literal
    /// <c>true</c> (see <see cref="CombineAnd"/>), so ORing it in correctly
    /// makes the whole rule unconditionally true/false without needing a
    /// separate short-circuit case here.
    /// </summary>
    private static IReadOnlyList<ResourcePolicy> BuildResourcePolicies(
        List<(string ResourceKind, string ActionName, CedarEffect Effect, CedarExpr Condition)> flattened)
    {
        var actionRulesByKind = new Dictionary<string, Dictionary<string, ActionRule>>(StringComparer.OrdinalIgnoreCase);
        var clausesByKindAndAction =
            new Dictionary<string, Dictionary<string, (List<CedarExpr> Permits, List<CedarExpr> Forbids)>>(StringComparer.OrdinalIgnoreCase);

        foreach (var (resourceKind, actionName, effect, condition) in flattened)
        {
            if (!clausesByKindAndAction.TryGetValue(resourceKind, out var byAction))
            {
                byAction = new Dictionary<string, (List<CedarExpr>, List<CedarExpr>)>(StringComparer.Ordinal);
                clausesByKindAndAction[resourceKind] = byAction;
            }

            if (!byAction.TryGetValue(actionName, out var clauses))
            {
                clauses = ([], []);
                byAction[actionName] = clauses;
            }

            (effect == CedarEffect.Permit ? clauses.Permits : clauses.Forbids).Add(condition);
        }

        foreach (var (resourceKind, byAction) in clausesByKindAndAction)
        {
            var actions = new Dictionary<string, ActionRule>(StringComparer.Ordinal);
            foreach (var (actionName, clauses) in byAction)
            {
                actions[actionName] = new ActionRule
                {
                    Allow = clauses.Permits.Count > 0
                        ? new AllowRule { When = CedarExprRenderer.Render(CombineOr(clauses.Permits)), Language = "cedar" }
                        : null,
                    Forbid = clauses.Forbids.Count > 0
                        ? new ForbidRule { When = CedarExprRenderer.Render(CombineOr(clauses.Forbids)), Language = "cedar" }
                        : null,
                };
            }

            actionRulesByKind[resourceKind] = actions;
        }

        return [.. actionRulesByKind.Select(kv => new ResourcePolicy { Resource = kv.Key, Actions = kv.Value })];
    }

    // -- resource scope -----------------------------------------------------

    private static (string Kind, CedarExpr? Condition) ResolveResourceScope(CedarScopeConstraint scope, CedarLoadOptions options) =>
        scope switch
        {
            CedarAnyScope => (RequireDefaultResourceKind(options), null),
            CedarEqScope eq => (LastSegment(eq.Entity.Type), Equal(ResourceIdAttr(), StringLiteral(eq.Entity.Id))),
            CedarIsScope isScope => (LastSegment(isScope.Type), null),
            CedarIsInScope isIn => (LastSegment(isIn.Type), InOp(ResourceVar(), EntityRefExpr(isIn.Entity))),
            CedarInScope inScope => (RequireDefaultResourceKind(options), InOp(ResourceVar(), EntityRefExpr(inScope.Entity))),
            _ => throw new CedarLoweringException($"Unsupported resource scope constraint '{scope.GetType().Name}'"),
        };

    private static string RequireDefaultResourceKind(CedarLoadOptions options) => options.DefaultResourceKind
        ?? throw new CedarLoweringException(
            "Cannot lower a policy whose resource scope doesn't determine a concrete resource kind (no 'is'/'==', " +
            "or a bare 'resource in ...') -- configure CedarLoadOptions.DefaultResourceKind.");

    // -- principal scope ------------------------------------------------

    private static CedarExpr? ResolvePrincipalScope(CedarScopeConstraint scope, CedarLoadOptions options) => scope switch
    {
        CedarAnyScope => null,
        CedarEqScope eq => Equal(PrincipalIdAttr(), StringLiteral(eq.Entity.Id)),
        CedarIsScope isScope => RequireMatchingPrincipalType(isScope.Type, options),
        CedarIsInScope isIn => CombineAnd(
            RequireMatchingPrincipalType(isIn.Type, options), InOp(PrincipalVar(), EntityRefExpr(isIn.Entity))),
        CedarInScope inScope => InOp(PrincipalVar(), EntityRefExpr(inScope.Entity)),
        _ => throw new CedarLoweringException($"Unsupported principal scope constraint '{scope.GetType().Name}'"),
    };

    /// <summary>
    /// <c>principal is X</c> has no direct runtime check (Aegis principals
    /// have no type of their own beyond <see cref="CedarLoadOptions.PrincipalEntityType"/>)
    /// -- so a mismatch is rejected here, at lowering time, rather than
    /// silently never matching at evaluation time. A match needs no
    /// synthesized condition at all (<c>null</c>).
    /// </summary>
    private static CedarExpr? RequireMatchingPrincipalType(IReadOnlyList<string> type, CedarLoadOptions options)
    {
        var typeName = string.Join("::", type);
        if (!string.Equals(typeName, options.PrincipalEntityType, StringComparison.Ordinal))
        {
            throw new CedarLoweringException(
                $"'principal is {typeName}' does not match the configured " +
                $"CedarLoadOptions.PrincipalEntityType ('{options.PrincipalEntityType}').");
        }

        return null;
    }

    // -- action scope -------------------------------------------------------

    /// <summary><c>null</c> means <c>CedarAnyScope</c> -- resolved later, against the batch's action vocabulary for this resource kind.</summary>
    private static IReadOnlyList<string>? ResolveActionScope(CedarScopeConstraint scope) => scope switch
    {
        CedarAnyScope => null,
        CedarEqScope eq => [eq.Entity.Id],
        // No action-group expansion (no schema input in this milestone) --
        // treated the same as CedarEqScope with that one action, a
        // deliberate, documented simplification (see CedarLoadOptions'
        // doc comment and the plan this was built from).
        CedarInScope inScope => [inScope.Entity.Id],
        CedarInSetScope inSet => [.. inSet.Entities.Select(e => e.Id)],
        _ => throw new CedarLoweringException($"Unsupported action scope constraint '{scope.GetType().Name}'"),
    };

    // -- when/unless ----------------------------------------------------

    private static CedarExpr? CombineWhenUnless(IReadOnlyList<CedarCondition> conditions)
    {
        CedarExpr? combined = null;
        foreach (var condition in conditions)
        {
            var clause = condition.Kind == CedarConditionKind.When ? condition.Body : Not(condition.Body);
            combined = combined is null ? clause : And(combined, clause);
        }

        return combined;
    }

    // -- CedarExpr synthesis helpers -----------------------------------

    /// <summary>
    /// Combines every non-null fragment with <c>&amp;&amp;</c>; a fully
    /// unconstrained policy (every fragment null) becomes the literal
    /// <c>true</c> rather than a null/empty condition -- Aegis's own
    /// <c>AllowRule</c> treats "neither Roles nor When set" as "matches
    /// nothing" (see <c>PolicyEvaluator.Authorize</c>'s doc comment), so an
    /// explicit always-true condition is what correctly represents an
    /// unconditional Cedar <c>permit</c>/<c>forbid</c> here.
    /// </summary>
    private static CedarExpr CombineAnd(params CedarExpr?[] fragments)
    {
        CedarExpr? combined = null;
        foreach (var fragment in fragments)
        {
            if (fragment is null)
            {
                continue;
            }

            combined = combined is null ? fragment : And(combined, fragment);
        }

        return combined ?? LiteralTrue();
    }

    private static CedarExpr CombineOr(List<CedarExpr> clauses)
    {
        var combined = clauses[0];
        for (var i = 1; i < clauses.Count; i++)
        {
            combined = Or(combined, clauses[i]);
        }

        return combined;
    }

    private static CedarLiteralExpr LiteralTrue() => new(true, 0);

    private static CedarLiteralExpr StringLiteral(string value) => new(value, 0);

    private static CedarVarExpr PrincipalVar() => new(CedarVar.Principal, 0);

    private static CedarVarExpr ResourceVar() => new(CedarVar.Resource, 0);

    private static CedarAttrExpr PrincipalIdAttr() => new(PrincipalVar(), "id", 0);

    private static CedarAttrExpr ResourceIdAttr() => new(ResourceVar(), "id", 0);

    private static CedarEntityRefExpr EntityRefExpr(EntityRef entity) => new(entity.Type, entity.Id, 0);

    private static CedarBinaryExpr Equal(CedarExpr left, CedarExpr right) => new(CedarBinaryOperator.Equal, left, right, 0);

    private static CedarBinaryExpr And(CedarExpr left, CedarExpr right) => new(CedarBinaryOperator.And, left, right, 0);

    private static CedarBinaryExpr Or(CedarExpr left, CedarExpr right) => new(CedarBinaryOperator.Or, left, right, 0);

    private static CedarUnaryExpr Not(CedarExpr operand) => new(CedarUnaryOperator.Not, operand, 0);

    private static CedarInExpr InOp(CedarExpr left, CedarExpr right) => new(left, right, 0);

    private static string LastSegment(IReadOnlyList<string> type) => type[^1];
}
