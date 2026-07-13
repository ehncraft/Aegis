namespace Aegis.Sql;

/// <summary>
/// Thin seam over ADO.NET so <see cref="SqlServerAttributeProvider"/> is
/// testable without a real database. <paramref name="parameters"/> values
/// are always bound as SQL parameters, never concatenated into
/// <paramref name="commandText"/>.
/// </summary>
public interface ISqlQueryExecutor
{
    Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> QueryAsync(
        string commandText,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken);

    /// <summary>Runs a non-query statement (INSERT/UPDATE/DELETE) -- e.g. <c>SqlAuditLogStore.RecordAsync</c>.</summary>
    Task ExecuteAsync(
        string commandText,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken);
}