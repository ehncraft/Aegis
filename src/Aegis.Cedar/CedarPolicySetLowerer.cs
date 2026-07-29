using Aegis.Policies;
using Aegis.Relationships;

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
    /// <see cref="ResourceKind"/> means the resource scope was unconstrained
    /// (<c>CedarAnyScope</c>/bare <c>resource in ...</c>) with no configured
    /// <see cref="CedarLoadOptions.DefaultResourceKind"/>, and <c>null</c>
    /// <see cref="ActionNames"/> means the action scope was unconstrained
    /// (<c>CedarAnyScope</c>); both still need expanding against this
    /// batch's vocabulary (see <see cref="Lower"/>'s later passes).
    /// </summary>
    private sealed record ResolvedClause(
        string? ResourceKind, IReadOnlyList<string>? ActionNames, CedarEffect Effect, CedarExpr Condition);

    public static IReadOnlyList<ResourcePolicy> Lower(IReadOnlyList<CedarPolicy> policies, CedarLoadOptions options)
    {
        var resolved = new List<ResolvedClause>(policies.Count);
        foreach (var policy in policies)
        {
            var (resourceKind, resourceCondition) = ResolveResourceScope(policy.ResourceScope, options);
            var principalCondition = ResolvePrincipalScope(policy.PrincipalScope, options);
            var actionNames = ResolveActionScope(policy.ActionScope, options);
            var whenUnlessCondition = CombineWhenUnless(policy.Conditions);
            var condition = CombineAnd(principalCondition, resourceCondition, whenUnlessCondition);

            resolved.Add(new ResolvedClause(resourceKind, actionNames, policy.Effect, condition));
        }

        var resourceExpanded = ExpandResourceScopes(resolved);
        var vocabularyByKind = BuildActionVocabulary(resourceExpanded);
        var flattened = ExpandActionScopes(resourceExpanded, vocabularyByKind);

        return BuildResourcePolicies(flattened);
    }

    /// <summary>
    /// Pass 1: resolves every <c>ResourceKind is null</c> clause (a
    /// resource-unconstrained policy with no configured
    /// <see cref="CedarLoadOptions.DefaultResourceKind"/>) against the set
    /// of resource kinds every other policy in this batch concretely
    /// names -- the same "no schema, so infer from the batch" trick used for
    /// an unconstrained action scope. A batch that establishes no concrete
    /// resource kind at all still requires
    /// <see cref="CedarLoadOptions.DefaultResourceKind"/>.
    /// </summary>
    private static List<(string ResourceKind, IReadOnlyList<string>? ActionNames, CedarEffect Effect, CedarExpr Condition)>
        ExpandResourceScopes(IReadOnlyList<ResolvedClause> resolved)
    {
        var knownKinds = resolved
            .Where(c => c.ResourceKind is not null)
            .Select(c => c.ResourceKind!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var expanded = new List<(string, IReadOnlyList<string>?, CedarEffect, CedarExpr)>(resolved.Count);
        foreach (var clause in resolved)
        {
            if (clause.ResourceKind is not null)
            {
                expanded.Add((clause.ResourceKind, clause.ActionNames, clause.Effect, clause.Condition));
                continue;
            }

            if (knownKinds.Count == 0)
            {
                throw new CedarLoweringException(
                    "Cannot lower a policy whose resource scope doesn't determine a concrete resource kind (no " +
                    "'is'/'==', or a bare 'resource'/'resource in ...') -- no other policy in this batch " +
                    "establishes a concrete resource kind either. Configure CedarLoadOptions.DefaultResourceKind.");
            }

            foreach (var kind in knownKinds)
            {
                expanded.Add((kind, clause.ActionNames, clause.Effect, clause.Condition));
            }
        }

        return expanded;
    }

    /// <summary>
    /// Pass 2: the union of every concrete action name any policy in this
    /// batch names for a given resource kind -- what an <c>action</c>-unconstrained
    /// policy for that same resource kind expands against.
    /// </summary>
    private static Dictionary<string, HashSet<string>> BuildActionVocabulary(
        IReadOnlyList<(string ResourceKind, IReadOnlyList<string>? ActionNames, CedarEffect Effect, CedarExpr Condition)> resolved)
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
    /// Pass 3: resolves every <c>ActionNames is null</c> clause (an
    /// <c>action</c>-unconstrained policy) against pass 2's vocabulary for
    /// its resource kind, then flattens every clause -- expanded or
    /// already-concrete -- into one <c>(resourceKind, actionName)</c> tuple
    /// per action it applies to.
    /// </summary>
    private static List<(string ResourceKind, string ActionName, CedarEffect Effect, CedarExpr Condition)> ExpandActionScopes(
        IReadOnlyList<(string ResourceKind, IReadOnlyList<string>? ActionNames, CedarEffect Effect, CedarExpr Condition)> resolved,
        Dictionary<string, HashSet<string>> vocabularyByKind)
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

    private static (string? Kind, CedarExpr? Condition) ResolveResourceScope(CedarScopeConstraint scope, CedarLoadOptions options) =>
        scope switch
        {
            CedarAnyScope => (options.DefaultResourceKind, null),
            CedarEqScope eq => (LastSegment(eq.Entity.Type), Equal(ResourceIdAttr(), StringLiteral(eq.Entity.Id))),
            CedarIsScope isScope => (LastSegment(isScope.Type), null),
            CedarIsInScope isIn => (LastSegment(isIn.Type), InOp(ResourceVar(), EntityRefExpr(isIn.Entity))),
            CedarInScope inScope => (options.DefaultResourceKind, InOp(ResourceVar(), EntityRefExpr(inScope.Entity))),
            _ => throw new CedarLoweringException($"Unsupported resource scope constraint '{scope.GetType().Name}'"),
        };

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
    private static IReadOnlyList<string>? ResolveActionScope(CedarScopeConstraint scope, CedarLoadOptions options) => scope switch
    {
        CedarAnyScope => null,
        CedarEqScope eq => [eq.Entity.Id],
        CedarInScope inScope => ExpandActionGroup(inScope.Entity, options),
        CedarInSetScope inSet => [.. inSet.Entities.Select(e => e.Id)],
        _ => throw new CedarLoweringException($"Unsupported action scope constraint '{scope.GetType().Name}'"),
    };

    /// <summary>
    /// <c>action in Action::"group"</c> expands to every action reachable
    /// as a descendant of the group entity in
    /// <see cref="CedarLoadOptions.ActionGroups"/>, plus the group entity's
    /// own id -- matching Cedar's own <c>X in Y</c> semantics, where
    /// <c>X == Y</c> counts. An unconfigured <see cref="CedarLoadOptions.ActionGroups"/>
    /// (or a group name with no recorded descendants) naturally reduces to
    /// just <c>[entity.Id]</c>, identical to <c>action == Action::"X"</c>.
    /// </summary>
    private static IReadOnlyList<string> ExpandActionGroup(EntityRef entity, CedarLoadOptions options)
    {
        var ancestor = new EntityUid(LastSegment(entity.Type), entity.Id);
        var graph = options.ActionGroups ?? RelationshipGraph.Empty;
        return [entity.Id, .. graph.Descendants(ancestor).Select(e => e.Id)];
    }

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