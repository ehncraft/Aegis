namespace Aegis.Audit;

/// <summary>
/// Pluggable persistence for authorization decisions, separate from policy
/// and relationship storage -- so decisions are queryable after the fact
/// (audit trail, compliance review), not just returned inline from
/// <c>AuthorizeAsync</c> and discarded.
/// </summary>
public interface IAuditLogStore
{
    Task RecordAsync(AuditLogEntry entry, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AuditLogEntry>> QueryAsync(AuditLogQuery query, CancellationToken cancellationToken = default);
}