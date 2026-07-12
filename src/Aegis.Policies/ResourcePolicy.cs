namespace Aegis.Policies;

/// <summary>The set of action rules governing one resource kind, e.g. "invoices".</summary>
public sealed class ResourcePolicy
{
    public string Resource { get; set; } = string.Empty;

    /// <summary>Optional human-readable identifier shown in decision explanations. Defaults to <see cref="Resource"/>.</summary>
    public string? Name { get; set; }

    public Dictionary<string, ActionRule> Actions { get; set; } = new();

    /// <summary>
    /// Named <c>${name}</c> expressions available to this policy's <c>when</c>
    /// conditions and derived roles. Values are expression source text, e.g.
    /// <c>"principal.department == resource.department"</c>, not literals.
    /// </summary>
    public Dictionary<string, string> Variables { get; set; } = new();

    /// <summary>
    /// Roles computed from a condition rather than held directly by the
    /// principal, e.g. <c>owner: principal.id == resource.ownerId</c>.
    /// Usable anywhere a static role name appears in an <c>allow.roles</c> list.
    /// </summary>
    public Dictionary<string, DerivedRoleDefinition> DerivedRoles { get; set; } = new();

    /// <summary>
    /// Names of <see cref="PolicyLibrary"/> files whose variables and derived
    /// roles are merged into this policy at load time.
    /// </summary>
    public List<string> Imports { get; set; } = new();

    /// <summary>Where this policy was loaded from, e.g. a file path. Set by the loader, not the YAML.</summary>
    public string? Source { get; set; }
}