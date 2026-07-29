using Aegis.Relationships;

namespace Aegis.Cedar;

/// <summary>Options controlling how <see cref="CedarPolicySetLowerer"/> resolves ambiguity Cedar allows but Aegis's model can't.</summary>
public sealed class CedarLoadOptions
{
    /// <summary>
    /// An explicit override for the resource kind an unconstrained resource
    /// scope (<c>CedarAnyScope</c>, e.g. bare <c>resource</c> or
    /// <c>resource in ...</c> with no <c>is</c>/<c>==</c>) resolves to. When
    /// unset (the default), the lowerer instead infers it by expanding that
    /// policy across every concrete resource kind named elsewhere in the
    /// same batch -- the same "no schema, so infer from the batch" trick
    /// already used for an unconstrained action scope. Lowering only fails
    /// if the batch establishes no concrete resource kind at all.
    /// </summary>
    public string? DefaultResourceKind { get; init; }

    /// <summary>
    /// Entity-parent facts describing which action entities belong to which
    /// action group, consulted only when lowering <c>action in Action::"X"</c>
    /// -- e.g. <c>EntityParent { Child = Action:"approve", Parent = Action:"department_management" }</c>
    /// makes <c>action in Action::"department_management"</c> expand to
    /// every action reachable as a descendant of that group entity, plus the
    /// group entity's own id (matching Cedar's own <c>X in Y</c> semantics,
    /// where <c>X == Y</c> counts). <c>null</c> (default) preserves the
    /// simplest behavior: an unrecognized or unconfigured group name
    /// resolves to just itself, identical to <c>action == Action::"X"</c>.
    /// </summary>
    public RelationshipGraph? ActionGroups { get; init; }

    /// <summary>
    /// The entity type name Aegis principals are treated as for
    /// <c>principal is X</c>/<c>principal is X in Y</c> checks and for the
    /// descendant side of any <c>in</c> check this lowering emits -- must
    /// agree with whatever <c>PolicyEvaluator</c>'s own
    /// <c>principalEntityType</c> constructor parameter ends up being when
    /// this lowering's output is fed into one. Defaults to <c>"User"</c>,
    /// matching <c>PolicyEvaluator</c>'s own default.
    /// </summary>
    public string PrincipalEntityType { get; init; } = "User";
}