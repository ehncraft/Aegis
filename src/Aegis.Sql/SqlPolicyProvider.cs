using Aegis.Policies;

using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Aegis.Sql;

/// <summary>
/// <see cref="IPolicyProvider"/> backed by an existing SQL Server table, per
/// <see cref="SqlPolicyStoreOptions"/>. Each row's policy body is parsed as
/// YAML with the same rules <c>YamlPolicyLoader</c> applies to files.
/// </summary>
public sealed class SqlPolicyProvider : IPolicyProvider
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private readonly SqlPolicyStoreOptions _options;
    private readonly ISqlQueryExecutor _executor;

    public SqlPolicyProvider(SqlPolicyStoreOptions options)
        : this(options, new SqlServerQueryExecutor(options.ConnectionString))
    {
    }

    public SqlPolicyProvider(SqlPolicyStoreOptions options, ISqlQueryExecutor executor)
    {
        _options = options;
        _executor = executor;
    }

    public async Task<IReadOnlyList<ResourcePolicy>> LoadPoliciesAsync(CancellationToken cancellationToken = default)
    {
        var source = $"sql:{_options.Table}";
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows;
        try
        {
            var sql =
                $"SELECT {SqlIdentifier.Quote(_options.ResourceNameColumn)}, {SqlIdentifier.Quote(_options.PolicyYamlColumn)} " +
                $"FROM {SqlIdentifier.Quote(_options.Table)}";
            rows = await _executor.QueryAsync(sql, new Dictionary<string, object?>(), cancellationToken);
        }
        catch (Exception ex)
        {
            throw new PolicyLoadException(source, ex);
        }

        var policies = new List<ResourcePolicy>(rows.Count);
        foreach (var row in rows)
        {
            var resourceName = row.GetValueOrDefault(_options.ResourceNameColumn) as string ?? "(unknown)";
            var rowSource = $"{source}/{resourceName}";

            if (row.GetValueOrDefault(_options.PolicyYamlColumn) is not string yaml)
            {
                throw new PolicyLoadException(
                    rowSource, new InvalidOperationException($"Column '{_options.PolicyYamlColumn}' was null or not text"));
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