using Aegis.Policies;

using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Aegis.Sql;

/// <summary>
/// Constructs an <see cref="IPolicyProvider"/> backed by an existing SQL Server table, per
/// <see cref="SqlPolicyStoreOptions"/> -- the implementation itself is internal; callers only
/// ever see it through the interface.
/// </summary>
public static class SqlPolicyProvider
{
    public static IPolicyProvider Create(SqlPolicyStoreOptions options) =>
        new SqlPolicyProviderImpl(options, SqlServerQueryExecutor.Create(options.ConnectionString));

    public static IPolicyProvider Create(SqlPolicyStoreOptions options, ISqlQueryExecutor executor) =>
        new SqlPolicyProviderImpl(options, executor);
}

/// <summary>Each row's policy body is parsed as YAML with the same rules
/// <c>YamlPolicyLoader</c> applies to files.</summary>
internal sealed class SqlPolicyProviderImpl(SqlPolicyStoreOptions options, ISqlQueryExecutor executor) : IPolicyProvider
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public async Task<IReadOnlyList<ResourcePolicy>> LoadPoliciesAsync(CancellationToken cancellationToken = default)
    {
        var source = $"sql:{options.Table}";
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows;
        try
        {
            var sql =
                $"SELECT {SqlIdentifier.Quote(options.ResourceNameColumn)}, {SqlIdentifier.Quote(options.PolicyYamlColumn)} " +
                $"FROM {SqlIdentifier.Quote(options.Table)}";
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

        var policies = new List<ResourcePolicy>(rows.Count);
        foreach (var row in rows)
        {
            var resourceName = row.GetValueOrDefault(options.ResourceNameColumn) as string ?? "(unknown)";
            var rowSource = $"{source}/{resourceName}";

            if (row.GetValueOrDefault(options.PolicyYamlColumn) is not string yaml)
            {
                throw new PolicyLoadException(
                    rowSource, new InvalidOperationException($"Column '{options.PolicyYamlColumn}' was null or not text"));
            }

            try
            {
                using var reader = new StringReader(yaml);
                var policy = Deserializer.Deserialize<ResourcePolicy>(reader)
                    ?? throw new InvalidOperationException("Policy row is empty");
                policy.Source = rowSource;
                policies.Add(policy);
            }
            catch (Exception ex) when (ex is not PolicyLoadException)
            {
                throw new PolicyLoadException(rowSource, ex);
            }
        }

        return policies;
    }
}