namespace Aegis.Sql;

/// <summary>
/// <see cref="IAttributeProvider"/> backed by an existing SQL Server
/// database, per <see cref="SqlAttributeProviderOptions"/>'s table/column
/// mapping. Values are always parameterized; table/column names come only
/// from configuration, never from request input.
/// </summary>
public sealed class SqlServerAttributeProvider : IAttributeProvider
{
    private readonly SqlAttributeProviderOptions _options;
    private readonly ISqlQueryExecutor _executor;

    public SqlServerAttributeProvider(SqlAttributeProviderOptions options)
        : this(options, new SqlServerQueryExecutor(options.ConnectionString))
    {
    }

    public SqlServerAttributeProvider(SqlAttributeProviderOptions options, ISqlQueryExecutor executor)
    {
        _options = options;
        _executor = executor;
    }

    public async Task<PrincipalAttributes> GetPrincipalAttributesAsync(
        string principalId, CancellationToken cancellationToken = default)
    {
        var attributes = await SelectSingleRowAsync(
            _options.PrincipalTable, _options.PrincipalIdColumn, principalId,
            _options.PrincipalAttributeColumns, cancellationToken);

        var roles = Array.Empty<string>();
        if (_options is { RoleTable: not null, RoleUserIdColumn: not null, RoleNameColumn: not null })
        {
            var sql =
                $"SELECT {SqlIdentifier.Quote(_options.RoleNameColumn)} " +
                $"FROM {SqlIdentifier.Quote(_options.RoleTable)} " +
                $"WHERE {SqlIdentifier.Quote(_options.RoleUserIdColumn)} = @principalId";
            var rows = await _executor.QueryAsync(
                sql, new Dictionary<string, object?> { ["@principalId"] = principalId }, cancellationToken);

            roles = [.. rows
                .Select(row => row.GetValueOrDefault(_options.RoleNameColumn) as string)
                .Where(role => !string.IsNullOrEmpty(role))
                .Select(role => role!)];
        }

        return new PrincipalAttributes { Roles = roles, Attributes = attributes };
    }

    public async Task<IReadOnlyDictionary<string, object?>> GetResourceAttributesAsync(
        string resourceKind, string resourceId, CancellationToken cancellationToken = default)
    {
        if (!_options.ResourceTables.TryGetValue(resourceKind, out var mapping))
        {
            return new Dictionary<string, object?>();
        }

        return await SelectSingleRowAsync(
            mapping.Table, mapping.IdColumn, resourceId, mapping.AttributeColumns, cancellationToken);
    }

    private async Task<Dictionary<string, object?>> SelectSingleRowAsync(
        string table,
        string idColumn,
        string id,
        IReadOnlyDictionary<string, string> attributeColumns,
        CancellationToken cancellationToken)
    {
        var attributes = new Dictionary<string, object?>();
        if (attributeColumns.Count == 0)
        {
            return attributes;
        }

        var columns = string.Join(", ", attributeColumns.Values.Select(SqlIdentifier.Quote));
        var sql = $"SELECT {columns} FROM {SqlIdentifier.Quote(table)} WHERE {SqlIdentifier.Quote(idColumn)} = @id";
        var rows = await _executor.QueryAsync(
            sql, new Dictionary<string, object?> { ["@id"] = id }, cancellationToken);

        if (rows.Count == 0)
        {
            return attributes;
        }

        var row = rows[0];
        foreach (var (attributeName, columnName) in attributeColumns)
        {
            if (row.TryGetValue(columnName, out var value))
            {
                attributes[attributeName] = value;
            }
        }

        return attributes;
    }
}