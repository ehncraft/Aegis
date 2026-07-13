using System.Text.Json;

using Aegis.Audit;

namespace Aegis.Sql;

/// <summary>
/// <see cref="IAuditLogStore"/> backed by an existing SQL Server table, per
/// <see cref="SqlAuditLogStoreOptions"/>. <see cref="DecisionExplanation"/>
/// is stored as JSON text, the same shape the explain API already
/// produces, alongside flat columns for the fields <see cref="AuditLogQuery"/>
/// filters on.
/// </summary>
public sealed class SqlAuditLogStore : IAuditLogStore
{
    private readonly SqlAuditLogStoreOptions _options;
    private readonly ISqlQueryExecutor _executor;

    public SqlAuditLogStore(SqlAuditLogStoreOptions options)
        : this(options, new SqlServerQueryExecutor(options.ConnectionString))
    {
    }

    public SqlAuditLogStore(SqlAuditLogStoreOptions options, ISqlQueryExecutor executor)
    {
        _options = options;
        _executor = executor;
    }

    public async Task RecordAsync(AuditLogEntry entry, CancellationToken cancellationToken = default)
    {
        var sql =
            $"INSERT INTO {SqlIdentifier.Quote(_options.Table)} " +
            $"({SqlIdentifier.Quote(_options.TenantIdColumn)}, {SqlIdentifier.Quote("PrincipalId")}, " +
            $"{SqlIdentifier.Quote("ResourceKind")}, {SqlIdentifier.Quote("ResourceId")}, " +
            $"{SqlIdentifier.Quote("Action")}, {SqlIdentifier.Quote("Allowed")}, " +
            $"{SqlIdentifier.Quote("ExplanationJson")}, {SqlIdentifier.Quote("Timestamp")}) " +
            "VALUES (@tenantId, @principalId, @resourceKind, @resourceId, @action, @allowed, @explanationJson, @timestamp)";

        var parameters = new Dictionary<string, object?>
        {
            ["@tenantId"] = _options.TenantId ?? string.Empty,
            ["@principalId"] = entry.PrincipalId,
            ["@resourceKind"] = entry.ResourceKind,
            ["@resourceId"] = entry.ResourceId,
            ["@action"] = entry.Action,
            ["@allowed"] = entry.Allowed,
            ["@explanationJson"] = JsonSerializer.Serialize(entry.Explanation),
            ["@timestamp"] = entry.Timestamp.UtcDateTime,
        };

        await _executor.ExecuteAsync(sql, parameters, cancellationToken);
    }

    public async Task<IReadOnlyList<AuditLogEntry>> QueryAsync(
        AuditLogQuery query, CancellationToken cancellationToken = default)
    {
        var whereClauses = new List<string>();
        var parameters = new Dictionary<string, object?> { ["@limit"] = query.Limit };

        if (_options.TenantId is not null)
        {
            whereClauses.Add($"{SqlIdentifier.Quote(_options.TenantIdColumn)} = @tenantId");
            parameters["@tenantId"] = _options.TenantId;
        }

        AddFilter(whereClauses, parameters, "PrincipalId", "@principalId", query.PrincipalId);
        AddFilter(whereClauses, parameters, "ResourceKind", "@resourceKind", query.ResourceKind);
        AddFilter(whereClauses, parameters, "ResourceId", "@resourceId", query.ResourceId);
        AddFilter(whereClauses, parameters, "Action", "@action", query.Action);

        if (query.Allowed is not null)
        {
            whereClauses.Add($"{SqlIdentifier.Quote("Allowed")} = @allowed");
            parameters["@allowed"] = query.Allowed.Value;
        }

        if (query.From is not null)
        {
            whereClauses.Add($"{SqlIdentifier.Quote("Timestamp")} >= @from");
            parameters["@from"] = query.From.Value.UtcDateTime;
        }

        if (query.To is not null)
        {
            whereClauses.Add($"{SqlIdentifier.Quote("Timestamp")} <= @to");
            parameters["@to"] = query.To.Value.UtcDateTime;
        }

        var sql =
            $"SELECT TOP (@limit) {SqlIdentifier.Quote("PrincipalId")}, {SqlIdentifier.Quote("ResourceKind")}, " +
            $"{SqlIdentifier.Quote("ResourceId")}, {SqlIdentifier.Quote("Action")}, {SqlIdentifier.Quote("Allowed")}, " +
            $"{SqlIdentifier.Quote("ExplanationJson")}, {SqlIdentifier.Quote("Timestamp")} " +
            $"FROM {SqlIdentifier.Quote(_options.Table)}";

        if (whereClauses.Count > 0)
        {
            sql += " WHERE " + string.Join(" AND ", whereClauses);
        }

        sql += $" ORDER BY {SqlIdentifier.Quote("Timestamp")} DESC";

        var rows = await _executor.QueryAsync(sql, parameters, cancellationToken);
        return [.. rows.Select(ToEntry)];
    }

    private static void AddFilter(
        List<string> whereClauses,
        Dictionary<string, object?> parameters,
        string column,
        string parameterName,
        string? value)
    {
        if (value is null)
        {
            return;
        }

        whereClauses.Add($"{SqlIdentifier.Quote(column)} = {parameterName}");
        parameters[parameterName] = value;
    }

    private static AuditLogEntry ToEntry(IReadOnlyDictionary<string, object?> row) => new()
    {
        PrincipalId = (string)row["PrincipalId"]!,
        ResourceKind = (string)row["ResourceKind"]!,
        ResourceId = row["ResourceId"] as string,
        Action = (string)row["Action"]!,
        Allowed = (bool)row["Allowed"]!,
        Explanation = JsonSerializer.Deserialize<DecisionExplanation>((string)row["ExplanationJson"]!)!,
        Timestamp = new DateTimeOffset((DateTime)row["Timestamp"]!, TimeSpan.Zero),
    };
}