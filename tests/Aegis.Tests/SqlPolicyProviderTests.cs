using Aegis.Policies;
using Aegis.Sql;

using Xunit;

namespace Aegis.Tests;

public class SqlPolicyProviderTests
{
    private sealed class FakeSqlQueryExecutor(
        params IReadOnlyList<IReadOnlyDictionary<string, object?>>[] responses) : ISqlQueryExecutor
    {
        private readonly Queue<IReadOnlyList<IReadOnlyDictionary<string, object?>>> _responses = new(responses);
        private readonly Exception? _throws;

        public FakeSqlQueryExecutor(Exception throws) : this()
        {
            _throws = throws;
        }

        public string? LastCommandText { get; private set; }

        public IReadOnlyDictionary<string, object?>? LastParameters { get; private set; }

        public Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> QueryAsync(
            string commandText, IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken)
        {
            LastCommandText = commandText;
            LastParameters = parameters;

            if (_throws is not null)
            {
                throw _throws;
            }

            var response = _responses.Count > 0
                ? _responses.Dequeue()
                : Array.Empty<IReadOnlyDictionary<string, object?>>();
            return Task.FromResult(response);
        }
    }

    private const string ValidYaml = """
        resource: invoices
        actions:
          view:
            allow:
              roles:
                - Finance
        """;

    private static SqlPolicyStoreOptions Options() => new()
    {
        Table = "AegisPolicies",
        ResourceNameColumn = "ResourceName",
        PolicyYamlColumn = "PolicyYaml",
    };

    [Fact]
    public async Task LoadPoliciesAsync_ParsesYamlBodyIntoResourcePolicyAsync()
    {
        var executor = new FakeSqlQueryExecutor(
            [new Dictionary<string, object?> { ["ResourceName"] = "invoices", ["PolicyYaml"] = ValidYaml }]);
        var provider = new SqlPolicyProvider(Options(), executor);

        var policies = await provider.LoadPoliciesAsync();

        var policy = Assert.Single(policies);
        Assert.Equal("invoices", policy.Resource);
        Assert.Equal(["Finance"], policy.Actions["view"].Allow!.Roles);
        Assert.Equal("sql:AegisPolicies/invoices", policy.Source);
    }

    [Fact]
    public async Task LoadPoliciesAsync_MultipleRows_ReturnsMultiplePoliciesAsync()
    {
        const string secondYaml = """
            resource: loan_applications
            actions:
              view:
                allow:
                  roles:
                    - LoanOfficer
            """;
        var executor = new FakeSqlQueryExecutor(
            [
                new Dictionary<string, object?> { ["ResourceName"] = "invoices", ["PolicyYaml"] = ValidYaml },
                new Dictionary<string, object?> { ["ResourceName"] = "loan_applications", ["PolicyYaml"] = secondYaml },
            ]);
        var provider = new SqlPolicyProvider(Options(), executor);

        var policies = await provider.LoadPoliciesAsync();

        Assert.Equal(2, policies.Count);
        Assert.Contains(policies, p => p.Resource == "invoices");
        Assert.Contains(policies, p => p.Resource == "loan_applications");
    }

    [Fact]
    public async Task LoadPoliciesAsync_EmptyTable_ReturnsEmptyListAsync()
    {
        var executor = new FakeSqlQueryExecutor([]);
        var provider = new SqlPolicyProvider(Options(), executor);

        var policies = await provider.LoadPoliciesAsync();

        Assert.Empty(policies);
    }

    [Fact]
    public async Task LoadPoliciesAsync_MalformedYaml_ThrowsPolicyLoadExceptionAsync()
    {
        var executor = new FakeSqlQueryExecutor(
            [new Dictionary<string, object?> { ["ResourceName"] = "invoices", ["PolicyYaml"] = "not: [valid: yaml" }]);
        var provider = new SqlPolicyProvider(Options(), executor);

        var ex = await Assert.ThrowsAsync<PolicyLoadException>(() => provider.LoadPoliciesAsync());
        Assert.Equal("sql:AegisPolicies/invoices", ex.PolicySource);
    }

    [Fact]
    public async Task LoadPoliciesAsync_NullBodyColumn_ThrowsPolicyLoadExceptionAsync()
    {
        var executor = new FakeSqlQueryExecutor(
            [new Dictionary<string, object?> { ["ResourceName"] = "invoices", ["PolicyYaml"] = null }]);
        var provider = new SqlPolicyProvider(Options(), executor);

        await Assert.ThrowsAsync<PolicyLoadException>(() => provider.LoadPoliciesAsync());
    }

    [Fact]
    public async Task LoadPoliciesAsync_QueryFails_WrapsInPolicyLoadExceptionAsync()
    {
        var executor = new FakeSqlQueryExecutor(new InvalidOperationException("Invalid object name 'AegisPolicies'."));
        var provider = new SqlPolicyProvider(Options(), executor);

        var ex = await Assert.ThrowsAsync<PolicyLoadException>(() => provider.LoadPoliciesAsync());
        Assert.Equal("sql:AegisPolicies", ex.PolicySource);
    }

    [Fact]
    public async Task LoadPoliciesAsync_NoTenantId_QueriesWithoutTenantFilterAsync()
    {
        var executor = new FakeSqlQueryExecutor(
            [new Dictionary<string, object?> { ["ResourceName"] = "invoices", ["PolicyYaml"] = ValidYaml }]);
        var provider = new SqlPolicyProvider(Options(), executor);

        await provider.LoadPoliciesAsync();

        Assert.DoesNotContain("WHERE", executor.LastCommandText, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(executor.LastParameters!);
    }

    [Fact]
    public async Task LoadPoliciesAsync_WithTenantId_ScopesQueryByTenantAsync()
    {
        var options = Options();
        options.TenantId = "acme-sacco";
        var executor = new FakeSqlQueryExecutor(
            [new Dictionary<string, object?> { ["ResourceName"] = "invoices", ["PolicyYaml"] = ValidYaml }]);
        var provider = new SqlPolicyProvider(options, executor);

        await provider.LoadPoliciesAsync();

        Assert.Contains("[TenantId] = @tenantId", executor.LastCommandText);
        Assert.Equal("acme-sacco", executor.LastParameters!["@tenantId"]);
    }

    [Fact]
    public async Task LoadPoliciesAsync_WithTenantId_UsesConfiguredTenantIdColumnAsync()
    {
        var options = Options();
        options.TenantId = "acme-sacco";
        options.TenantIdColumn = "SaccoId";
        var executor = new FakeSqlQueryExecutor(
            [new Dictionary<string, object?> { ["ResourceName"] = "invoices", ["PolicyYaml"] = ValidYaml }]);
        var provider = new SqlPolicyProvider(options, executor);

        await provider.LoadPoliciesAsync();

        Assert.Contains("[SaccoId] = @tenantId", executor.LastCommandText);
    }
}