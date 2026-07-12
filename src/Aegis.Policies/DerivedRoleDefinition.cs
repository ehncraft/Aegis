namespace Aegis.Policies;

/// <summary>A role computed from a condition rather than held directly by the principal.</summary>
public sealed class DerivedRoleDefinition
{
    /// <summary>Condition expression; the derived role applies when this evaluates to true.</summary>
    public required string When { get; set; }
}