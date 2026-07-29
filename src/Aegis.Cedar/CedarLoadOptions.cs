namespace Aegis.Cedar;

/// <summary>Options controlling how <see cref="CedarPolicySetLowerer"/> resolves ambiguity Cedar allows but Aegis's model can't.</summary>
public sealed class CedarLoadOptions
{
    /// <summary>
    /// Used only when a policy's resource scope is unconstrained
    /// (<c>CedarAnyScope</c>) with no <c>is</c>/<c>==</c> to determine a
    /// kind from -- the resource kind that batch of policies applies to.
    /// <c>null</c> (default) means "error on an unconstrained resource
    /// scope" -- Aegis's <c>ResourcePolicy.Actions</c> dictionary has no way
    /// to represent a policy that applies to every resource kind at once.
    /// </summary>
    public string? DefaultResourceKind { get; init; }

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