namespace Aegis.Policies;

public sealed class AllowRule
{
    /// <summary>Grants access if the principal holds any of these roles.</summary>
    public List<string>? Roles { get; set; }

    /// <summary>Grants access if this condition expression evaluates to true.</summary>
    public string? When { get; set; }
}
