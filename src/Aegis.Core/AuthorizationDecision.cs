namespace Aegis;

public sealed class AuthorizationDecision
{
    public required bool Allowed { get; init; }

    public required DecisionExplanation Explanation { get; init; }

    public static AuthorizationDecision Allow(DecisionExplanation explanation) =>
        new() { Allowed = true, Explanation = explanation };

    public static AuthorizationDecision Deny(DecisionExplanation explanation) =>
        new() { Allowed = false, Explanation = explanation };
}
