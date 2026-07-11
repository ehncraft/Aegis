namespace Aegis;

/// <summary>Why a decision was reached — for debugging, auditing, and compliance.</summary>
public sealed class DecisionExplanation
{
    public required string Effect { get; init; } // "allow" | "deny"

    public string? MatchedPolicy { get; init; }

    public string? MatchedRule { get; init; }

    public IReadOnlyList<ConditionExplanation> Conditions { get; init; } =
        Array.Empty<ConditionExplanation>();

    /// <summary>Set when no policy or rule matched at all, e.g. "no policy for resource 'invoices'".</summary>
    public string? Reason { get; init; }
}
