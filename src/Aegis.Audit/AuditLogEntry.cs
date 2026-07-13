namespace Aegis.Audit;

/// <summary>A persisted record of one authorization decision -- who, what, when, and why.</summary>
public sealed class AuditLogEntry
{
    public required string PrincipalId { get; init; }

    public required string ResourceKind { get; init; }

    public string? ResourceId { get; init; }

    public required string Action { get; init; }

    public required bool Allowed { get; init; }

    public required DecisionExplanation Explanation { get; init; }

    public required DateTimeOffset Timestamp { get; init; }
}