namespace Aegis.Policies;

/// <summary>
/// An explicit deny -- unlike an <see cref="AllowRule"/> that simply fails
/// to match, a matching <c>forbid</c> always overrides a matching
/// <c>allow</c> on the same action, the same as Cedar's <c>forbid</c>
/// overriding any <c>permit</c>.
/// </summary>
public sealed class ForbidRule
{
    /// <summary>Denies access if the principal holds any of these roles.</summary>
    public List<string>? Roles { get; set; }

    /// <summary>Denies access if this condition expression evaluates to true.</summary>
    public string? When { get; set; }
}