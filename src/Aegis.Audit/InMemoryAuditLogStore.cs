namespace Aegis.Audit;

/// <summary>An in-process audit log -- for tests and prototyping. Not durable across restarts.</summary>
public sealed class InMemoryAuditLogStore : IAuditLogStore
{
    private readonly List<AuditLogEntry> _entries = [];
    private readonly object _lock = new();

    public Task RecordAsync(AuditLogEntry entry, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _entries.Add(entry);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AuditLogEntry>> QueryAsync(AuditLogQuery query, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            IEnumerable<AuditLogEntry> results = _entries;

            if (query.PrincipalId is not null)
            {
                results = results.Where(e => e.PrincipalId == query.PrincipalId);
            }

            if (query.ResourceKind is not null)
            {
                results = results.Where(e => string.Equals(e.ResourceKind, query.ResourceKind, StringComparison.OrdinalIgnoreCase));
            }

            if (query.ResourceId is not null)
            {
                results = results.Where(e => e.ResourceId == query.ResourceId);
            }

            if (query.Action is not null)
            {
                results = results.Where(e => e.Action == query.Action);
            }

            if (query.Allowed is not null)
            {
                results = results.Where(e => e.Allowed == query.Allowed);
            }

            if (query.From is not null)
            {
                results = results.Where(e => e.Timestamp >= query.From);
            }

            if (query.To is not null)
            {
                results = results.Where(e => e.Timestamp <= query.To);
            }

            return Task.FromResult<IReadOnlyList<AuditLogEntry>>(
                [.. results.OrderByDescending(e => e.Timestamp).Take(query.Limit)]);
        }
    }
}