namespace Aegis.Relationships;

/// <summary>
/// A Cedar-style entity reference (type + id), e.g. ("Group", "audit-committee")
/// -- see Cedar's entity data model: https://docs.cedarpolicy.com/auth/entities-syntax.html.
/// </summary>
public readonly record struct EntityUid(string Type, string Id)
{
    public override string ToString() => $"{Type}::{Id}";
}