namespace Aegis.Policies;

/// <summary>The set of action rules governing one resource kind, e.g. "invoices".</summary>
public sealed class ResourcePolicy
{
    public string Resource { get; set; } = string.Empty;

    /// <summary>Optional human-readable identifier shown in decision explanations. Defaults to <see cref="Resource"/>.</summary>
    public string? Name { get; set; }

    public Dictionary<string, ActionRule> Actions { get; set; } = new();

    /// <summary>Where this policy was loaded from, e.g. a file path. Set by the loader, not the YAML.</summary>
    public string? Source { get; set; }
}