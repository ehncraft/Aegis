using Aegis.Cedar;
using Aegis.Policies;

namespace Aegis.Sql;

/// <summary>
/// Constructs an <see cref="IPolicyProvider"/> backed by an existing SQL Server table, per
/// <see cref="CedarSqlPolicyStoreOptions"/> -- the implementation itself is internal; callers
/// only ever see it through the interface.
/// </summary>
public static class CedarSqlPolicyProvider
{
    public static IPolicyProvider Create(CedarSqlPolicyStoreOptions options, CedarLoadOptions? loadOptions = null) =>
        new CedarSqlPolicyProviderImpl(options, loadOptions, SqlServerQueryExecutor.Create(options.ConnectionString));

    public static IPolicyProvider Create(
        CedarSqlPolicyStoreOptions options, CedarLoadOptions? loadOptions, ISqlQueryExecutor executor) =>
        new CedarSqlPolicyProviderImpl(options, loadOptions, executor);
}

/// <summary>
/// Each row's policy body is Cedar text -- parsed and lowered as one batch via
/// <see cref="CedarPolicyProvider.LoadFromText"/>, not row-by-row, the same way
/// <see cref="CedarPolicyProvider.LoadDirectory"/> batches every <c>*.cedar</c> file in a
/// directory together: a <c>permit</c> in one row and a <c>forbid</c> in another, both for the
/// same resource/action, only merge correctly if lowered together (see
/// <c>CedarPolicySetLowerer</c>'s merge step). This is the one real difference from
/// <see cref="SqlPolicyProviderImpl"/>'s YAML rows, which deserialize independently -- one row
/// there is already a whole <see cref="ResourcePolicy"/>, not a fragment to be combined with
/// others.
/// </summary>
internal sealed class CedarSqlPolicyProviderImpl(
    CedarSqlPolicyStoreOptions options, CedarLoadOptions? loadOptions, ISqlQueryExecutor executor) : IPolicyProvider
{
    public async Task<IReadOnlyList<ResourcePolicy>> LoadPoliciesAsync(CancellationToken cancellationToken = default)
    {
        var qualifiedTable = SqlIdentifier.Quote(options.Schema, options.Table);
        var source = $"sql:{qualifiedTable}";
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows;
        try
        {
            var sql =
                $"SELECT {SqlIdentifier.Quote(options.PolicyNameColumn)}, {SqlIdentifier.Quote(options.PolicyCedarColumn)} " +
                $"FROM {qualifiedTable}";
            var parameters = new Dictionary<string, object?>();

            if (options.TenantId is not null)
            {
                sql += $" WHERE {SqlIdentifier.Quote(options.TenantIdColumn)} = @tenantId";
                parameters["@tenantId"] = options.TenantId;
            }

            rows = await executor.QueryAsync(sql, parameters, cancellationToken);
        }
        catch (Exception ex)
        {
            throw new PolicyLoadException(source, ex);
        }

        var policyTexts = new List<string>(rows.Count);
        var sourceNames = new List<string>(rows.Count);
        foreach (var row in rows)
        {
            var policyName = row.GetValueOrDefault(options.PolicyNameColumn) as string ?? "(unknown)";
            var rowSource = $"{source}/{policyName}";

            if (row.GetValueOrDefault(options.PolicyCedarColumn) is not string cedarText)
            {
                throw new PolicyLoadException(
                    rowSource, new InvalidOperationException($"Column '{options.PolicyCedarColumn}' was null or not text"));
            }

            policyTexts.Add(cedarText);
            sourceNames.Add(rowSource);
        }

        return CedarPolicyProvider.LoadFromText(policyTexts, sourceNames, loadOptions);
    }
}