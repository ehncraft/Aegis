namespace Aegis.Policies;

/// <summary>
/// A role computed rather than held directly by the principal -- either
/// from a condition (<see cref="When"/>) or from entity-hierarchy
/// membership (<see cref="In"/>), mutually exclusive.
/// </summary>
public sealed class DerivedRoleDefinition
{
    /// <summary>ABAC-style: the derived role applies when this condition evaluates to true.</summary>
    public string? When { get; set; }

    /// <summary>
    /// ReBAC-style, mirroring Cedar's <c>in</c> operator: the derived role
    /// applies when the principal is a member of this entity's hierarchy,
    /// directly or transitively.
    /// </summary>
    public DerivedRoleHierarchyCheck? In { get; set; }
}

/// <summary>
/// The entity a <see cref="DerivedRoleDefinition.In"/> check tests
/// membership against, e.g. <c>{ type: Group, id: resource.committeeId }</c>.
/// </summary>
public sealed class DerivedRoleHierarchyCheck
{
    /// <summary>The target entity's type, e.g. "Group" -- a literal, not an expression.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Expression evaluating to the target entity's id, e.g. "resource.committeeId".</summary>
    public string Id { get; set; } = string.Empty;
}