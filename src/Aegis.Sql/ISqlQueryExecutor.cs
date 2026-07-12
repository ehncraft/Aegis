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
}