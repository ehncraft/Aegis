namespace Aegis.AuthZen;

/// <summary>
/// Response body for <c>POST /access/v1/evaluation</c> -- https://openid.github.io/authzen/.
/// <see cref="Context"/> carries Aegis's own <see cref="DecisionExplanation"/>
/// (matched policy/rule, every condition evaluated, and why) in the spec's
/// optional free-form context slot, so an AuthZEN caller gets Aegis's
/// explainability for free rather than a bare boolean.
/// </summary>
public sealed class AuthZenEvaluationResponse
{
    public required bool Decision { get; init; }

    public object? Context { get; init; }
}