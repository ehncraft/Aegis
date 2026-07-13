namespace Aegis.Audit;

/// <summary>Filters for querying persisted audit log entries. Every filter is optional and combines with AND.</summary>
public sealed class AuditLogQuery
{
    public string? PrincipalId { get; init; }

    public string? ResourceKind { get; init; }

    public string? ResourceId { get; init; }

    public string? Action { get; init; }

    public bool? Allowed { get; init; }

    public DateTimeOffset? From { get; init; }

    public DateTimeOffset? To { get; init; }

    public int Limit { get; init; } = 100;
}