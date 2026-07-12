namespace Aegis.Testing;

/// <summary>
/// Thrown by <see cref="AegisEngineAssertions"/> when a decision doesn't
/// match what a policy test expected. Every test runner (xUnit, NUnit,
/// MSTest, ...) reports an uncaught exception as a failure, so this needs
/// no framework-specific dependency -- and it carries the full
/// <see cref="AuthorizationDecision"/>, so the failure is debuggable from
/// the message alone.
/// </summary>
public sealed class PolicyAssertionException : Exception
{
    public PolicyAssertionException(
        bool expectedAllowed,
        AegisPrincipal principal,
        AegisResource resource,
        string action,
        AuthorizationDecision decision)
        : base(BuildMessage(expectedAllowed, principal, resource, action, decision))
    {
        Decision = decision;
    }

    public AuthorizationDecision Decision { get; }

    private static string BuildMessage(
        bool expectedAllowed, AegisPrincipal principal, AegisResource resource, string action, AuthorizationDecision decision)
    {
        var expected = expectedAllowed ? "allow" : "deny";
        var actual = decision.Allowed ? "allow" : "deny";
        var resourceDescription = resource.Id is null ? resource.Kind : $"{resource.Kind} '{resource.Id}'";

        var lines = new List<string>
        {
            $"Expected '{expected}' for principal '{principal.Id}', action '{action}' on {resourceDescription}, but got '{actual}'.",
        };

        if (decision.Explanation.MatchedPolicy is not null)
        {
            lines.Add($"  Matched policy: {decision.Explanation.MatchedPolicy}");
        }

        if (decision.Explanation.MatchedRule is not null)
        {
            lines.Add($"  Matched rule: {decision.Explanation.MatchedRule}");
        }

        if (decision.Explanation.Reason is not null)
        {
            lines.Add($"  Reason: {decision.Explanation.Reason}");
        }

        if (decision.Explanation.Conditions.Count > 0)
        {
            lines.Add("  Conditions:");
            lines.AddRange(decision.Explanation.Conditions.Select(
                c => $"    - {c.Expression} => {(c.Result ? "true" : "false")}"));
        }

        return string.Join(Environment.NewLine, lines);
    }
}