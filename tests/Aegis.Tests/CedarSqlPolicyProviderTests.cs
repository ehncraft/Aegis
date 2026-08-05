using Aegis.Policies;
using Aegis.Sql;

using Xunit;

namespace Aegis.Tests;

public class CedarSqlPolicyProviderTests
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

        public Task ExecuteAsync(
            string commandText, IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken) =>
            throw new NotSupportedException("CedarSqlPolicyProvider is read-only; this fake doesn't need to support writes.");
    }

    private const string PermitCedar = "permit(principal, action == Action::\"view\", resource is documents);";
    private const string ForbidCedar = "forbid(principal, action == Action::\"view\", resource is documents) when { principal.suspended };";

    private static CedarSqlPolicyStoreOptions Options() => new()
    {
        Schema = "dbo",
        Table = "AegisCedarPolicies",
        PolicyNameColumn = "PolicyName",
        PolicyCedarColumn = "PolicyCedar",
    };

    [Fact]
    public async Task LoadPoliciesAsync_ParsesCedarBodyIntoResourcePolicyAsync()
    {
        var executor = new FakeSqlQueryExecutor(
            [new Dictionary<string, object?> { ["PolicyName"] = "view-documents", ["PolicyCedar"] = PermitCedar }]);
        var provider = CedarSqlPolicyProvider.Create(Options(), loadOptions: null, executor);

        var policies = await provider.LoadPoliciesAsync();

        var policy = Assert.Single(policies);
        Assert.NotNull(policy.Actions["view"].Allow);
    }

    [Fact]
    public async Task LoadPoliciesAsync_MultipleRows_MergesPermitAndForbidAsync()
    {
        // Unlike SqlPolicyProvider's YAML rows (one row = one whole ResourcePolicy), Cedar rows
        // are fragments -- a permit in one row and a forbid in another, for the same
        // resource/action, only merge correctly if the whole batch is lowered together.
        var executor = new FakeSqlQueryExecutor(
            [
                new Dictionary<string, object?> { ["PolicyName"] = "allow-view", ["PolicyCedar"] = PermitCedar },
                new Dictionary<string, object?> { ["PolicyName"] = "deny-suspended", ["PolicyCedar"] = ForbidCedar },
            ]);
        var provider = CedarSqlPolicyProvider.Create(Options(), loadOptions: null, executor);

        var policies = await provider.LoadPoliciesAsync();

        var policy = Assert.Single(policies);
        var rule = policy.Actions["view"];
        Assert.NotNull(rule.Allow);
        Assert.NotNull(rule.Forbid);
    }

    [Fact]
    public async Task LoadPoliciesAsync_EmptyTable_ReturnsEmptyListAsync()
    {
        var executor = new FakeSqlQueryExecutor([]);
        var provider = CedarSqlPolicyProvider.Create(Options(), loadOptions: null, executor);

        var policies = await provider.LoadPoliciesAsync();

        Assert.Empty(policies);
    }

    [Fact]
    public async Task LoadPoliciesAsync_MalformedCedar_ThrowsPolicyLoadExceptionWithRowSourceAsync()
    {
        var executor = new FakeSqlQueryExecutor(
            [new Dictionary<string, object?> { ["PolicyName"] = "broken", ["PolicyCedar"] = "not valid cedar at all" }]);
        var provider = CedarSqlPolicyProvider.Create(Options(), loadOptions: null, executor);

        var ex = await Assert.ThrowsAsync<PolicyLoadException>(() => provider.LoadPoliciesAsync());
        Assert.Equal("sql:[dbo].[AegisCedarPolicies]/broken", ex.PolicySource);
    }

    [Fact]
    public async Task LoadPoliciesAsync_NullBodyColumn_ThrowsPolicyLoadExceptionAsync()
    {
        var executor = new FakeSqlQueryExecutor(
            [new Dictionary<string, object?> { ["PolicyName"] = "view-documents", ["PolicyCedar"] = null }]);
        var provider = CedarSqlPolicyProvider.Create(Options(), loadOptions: null, executor);

        await Assert.ThrowsAsync<PolicyLoadException>(() => provider.LoadPoliciesAsync());
    }

    [Fact]
    public async Task LoadPoliciesAsync_QueryFails_WrapsInPolicyLoadExceptionAsync()
    {
        var executor = new FakeSqlQueryExecutor(new InvalidOperationException("Invalid object name 'AegisCedarPolicies'."));
        var provider = CedarSqlPolicyProvider.Create(Options(), loadOptions: null, executor);

        var ex = await Assert.ThrowsAsync<PolicyLoadException>(() => provider.LoadPoliciesAsync());
        Assert.Equal("sql:[dbo].[AegisCedarPolicies]", ex.PolicySource);
    }

    [Fact]
    public async Task LoadPoliciesAsync_NoTenantId_QueriesWithoutTenantFilterAsync()
    {
        var executor = new FakeSqlQueryExecutor(
            [new Dictionary<string, object?> { ["PolicyName"] = "view-documents", ["PolicyCedar"] = PermitCedar }]);
        var provider = CedarSqlPolicyProvider.Create(Options(), loadOptions: null, executor);

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
            [new Dictionary<string, object?> { ["PolicyName"] = "view-documents", ["PolicyCedar"] = PermitCedar }]);
        var provider = CedarSqlPolicyProvider.Create(options, loadOptions: null, executor);

        await provider.LoadPoliciesAsync();

        Assert.Contains("[TenantId] = @tenantId", executor.LastCommandText);
        Assert.Equal("acme-sacco", executor.LastParameters!["@tenantId"]);
    }

    [Fact]
    public async Task LoadPoliciesAsync_WithSchema_QueriesSchemaQualifiedTableAsync()
    {
        // Schema-per-tenant isolation instead of a shared table + TenantId column -- the model
        // a repo whose every other table is already [tenant_{N}].[X] would actually use.
        var options = Options();
        options.Schema = "tenant_123";
        var executor = new FakeSqlQueryExecutor(
            [new Dictionary<string, object?> { ["PolicyName"] = "view-documents", ["PolicyCedar"] = PermitCedar }]);
        var provider = CedarSqlPolicyProvider.Create(options, loadOptions: null, executor);

        await provider.LoadPoliciesAsync();

        Assert.Contains("FROM [tenant_123].[AegisCedarPolicies]", executor.LastCommandText);
    }

    [Fact]
    public async Task LoadPoliciesAsync_DboSchema_QueriesDboQualifiedTableAsync()
    {
        // No implicit/unqualified path exists -- Schema is a required member (no default
        // value), so even the conventional default schema has to be spelled out explicitly.
        var executor = new FakeSqlQueryExecutor(
            [new Dictionary<string, object?> { ["PolicyName"] = "view-documents", ["PolicyCedar"] = PermitCedar }]);
        var provider = CedarSqlPolicyProvider.Create(Options(), loadOptions: null, executor);

        await provider.LoadPoliciesAsync();

        Assert.Contains("FROM [dbo].[AegisCedarPolicies]", executor.LastCommandText);
    }

    [Fact]
    public async Task LoadPoliciesAsync_PassesLoadOptionsThroughToLoweringAsync()
    {
        var executor = new FakeSqlQueryExecutor(
            [new Dictionary<string, object?>
            {
                ["PolicyName"] = "view-leave-request",
                ["PolicyCedar"] = "permit(principal, action == Action::\"view\", resource);",
            }]);
        var provider = CedarSqlPolicyProvider.Create(
            Options(), new Cedar.CedarLoadOptions { DefaultResourceKind = "LeaveRequest" }, executor);

        var policies = await provider.LoadPoliciesAsync();

        var policy = Assert.Single(policies);
        Assert.Equal("LeaveRequest", policy.Resource);
    }
}