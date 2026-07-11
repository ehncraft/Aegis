namespace Aegis;

/// <summary>The outcome of evaluating a single condition within a rule.</summary>
public sealed class ConditionExplanation
{
    public required string Expression { get; init; }

    public required bool Result { get; init; }
}