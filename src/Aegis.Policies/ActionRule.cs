namespace Aegis.Policies;

public sealed class ActionRule
{
    public AllowRule? Allow { get; set; }

    /// <summary>An explicit deny -- overrides <see cref="Allow"/> when it also matches.</summary>
    public ForbidRule? Forbid { get; set; }
}