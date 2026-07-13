using Aegis.Sql;

using Xunit;

namespace Aegis.Tests;

public class SqlServerAttributeProviderTests
{
    private sealed class FakeSqlQueryExecutor(
        params IReadOnlyList<IReadOnlyDictionary<string, object?>>[] responses) : ISqlQueryExecutor
    {
        private readonly Queue<IReadOnlyList<IReadOnlyDictionary<string, object?>>> _responses = new(responses);

        public List<(string CommandText, IReadOnlyDictionary<string, object?> Parameters)> Calls { get; } = [];

        public Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> QueryAsync(
            string commandText, IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken)
        {
            Calls.Add((commandText, parameters));
            var response = _responses.Count > 0
                ? _responses.Dequeue()
                : Array.Empty<IReadOnlyDictionary<string, object?>>();
            return Task.FromResult(response);
        }
    }

    private static SqlAttributeProviderOptions OptionsWithRoles() => new()
    {
        PrincipalTable = "Users",
        PrincipalIdColumn = "UserId",
        PrincipalAttributeColumns = new Dictionary<string, string>
        {
            ["department"] = "DepartmentName",
            ["branch"] = "BranchCode",
        },
        RoleTable = "UserRoles",
        RoleUserIdColumn = "UserId",
        RoleNameColumn = "RoleName",
    };

    [Fact]
    public async Task GetPrincipalAttributesAsync_MapsConfiguredColumnsAsync()
    {
        var executor = new FakeSqlQueryExecutor(
            [new Dictionary<string, object?> { ["DepartmentName"] = "finance", ["BranchCode"] = "nairobi-cbd" }],
            [new Dictionary<string, object?> { ["RoleName"] = "LoanOfficer" }]);
        var provider = new SqlServerAttributeProvider(OptionsWithRoles(), executor);

        var result = await provider.GetPrincipalAttributesAsync("officer-1");

        Assert.Equal("finance", result.Attributes["department"]);
        Assert.Equal("nairobi-cbd", result.Attributes["branch"]);
        Assert.Contains("LoanOfficer", result.Roles);
    }

    [Fact]
    public async Task GetPrincipalAttributesAsync_PassesIdAsParameterNotConcatenatedAsync()
    {
        var executor = new FakeSqlQueryExecutor([]);
        var provider = new SqlServerAttributeProvider(OptionsWithRoles(), executor);

        await provider.GetPrincipalAttributesAsync("officer-1; DROP TABLE Users;--");

        var attributeQuery = executor.Calls[0];
        Assert.Equal("officer-1; DROP TABLE Users;--", attributeQuery.Parameters["@id"]);
        Assert.DoesNotContain("DROP TABLE", attributeQuery.CommandText);

        var roleQuery = executor.Calls[1];
        Assert.Equal("officer-1; DROP TABLE Users;--", roleQuery.Parameters["@principalId"]);
        Assert.DoesNotContain("DROP TABLE", roleQuery.CommandText);
    }

    [Fact]
    public async Task GetPrincipalAttributesAsync_NoMatchingRow_ReturnsEmptyAttributesAsync()
    {
        var executor = new FakeSqlQueryExecutor([], []);
        var provider = new SqlServerAttributeProvider(OptionsWithRoles(), executor);

        var result = await provider.GetPrincipalAttributesAsync("unknown");

        Assert.Empty(result.Attributes);
        Assert.Empty(result.Roles);
    }

    [Fact]
    public async Task GetPrincipalAttributesAsync_NoRoleTableConfigured_SkipsRoleQueryAsync()
    {
        var options = new SqlAttributeProviderOptions
        {
            PrincipalTable = "Users",
            PrincipalIdColumn = "UserId",
            PrincipalAttributeColumns = new Dictionary<string, string> { ["department"] = "DepartmentName" },
        };
        var executor = new FakeSqlQueryExecutor(
            [new Dictionary<string, object?> { ["DepartmentName"] = "finance" }]);
        var provider = new SqlServerAttributeProvider(options, executor);

        var result = await provider.GetPrincipalAttributesAsync("officer-1");

        Assert.Single(executor.Calls);
        Assert.Empty(result.Roles);
    }

    [Fact]
    public async Task GetResourceAttributesAsync_UnmappedResourceKind_ReturnsEmptyWithoutQueryingAsync()
    {
        var executor = new FakeSqlQueryExecutor();
        var provider = new SqlServerAttributeProvider(OptionsWithRoles(), executor);

        var result = await provider.GetResourceAttributesAsync("loan_applications", "LN-1001");

        Assert.Empty(result);
        Assert.Empty(executor.Calls);
    }

    [Fact]
    public async Task GetResourceAttributesAsync_MappedResourceKind_ReturnsConfiguredColumnsAsync()
    {
        var options = OptionsWithRoles();
        options.ResourceTables["loan_applications"] = new SqlResourceTableMapping
        {
            Table = "LoanApplications",
            IdColumn = "LoanId",
            AttributeColumns = new Dictionary<string, string>
            {
                ["amount"] = "PrincipalAmount",
                ["applicantId"] = "ApplicantUserId",
            },
        };
        var executor = new FakeSqlQueryExecutor(
            [new Dictionary<string, object?> { ["PrincipalAmount"] = 250_000, ["ApplicantUserId"] = "member-42" }]);
        var provider = new SqlServerAttributeProvider(options, executor);

        var result = await provider.GetResourceAttributesAsync("loan_applications", "LN-1001");

        Assert.Equal(250_000, result["amount"]);
        Assert.Equal("member-42", result["applicantId"]);
    }

    [Fact]
    public async Task GetPrincipalAttributesAsync_TenantIdWithoutColumnConfigured_StaysUnscopedAsync()
    {
        var options = OptionsWithRoles();
        options.TenantId = "acme-sacco";
        var executor = new FakeSqlQueryExecutor(
            [new Dictionary<string, object?> { ["DepartmentName"] = "finance" }], [new Dictionary<string, object?>()]);
        var provider = new SqlServerAttributeProvider(options, executor);

        await provider.GetPrincipalAttributesAsync("officer-1");

        Assert.DoesNotContain("AND", executor.Calls[0].CommandText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AND", executor.Calls[1].CommandText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetPrincipalAttributesAsync_TenantScopesPrincipalAndRoleQueriesAsync()
    {
        var options = OptionsWithRoles();
        options.TenantId = "acme-sacco";
        options.PrincipalTenantColumn = "TenantId";
        options.RoleTenantColumn = "TenantId";
        var executor = new FakeSqlQueryExecutor(
            [new Dictionary<string, object?> { ["DepartmentName"] = "finance" }], [new Dictionary<string, object?>()]);
        var provider = new SqlServerAttributeProvider(options, executor);

        await provider.GetPrincipalAttributesAsync("officer-1");

        var principalQuery = executor.Calls[0];
        Assert.Contains("[TenantId] = @tenantId", principalQuery.CommandText);
        Assert.Equal("acme-sacco", principalQuery.Parameters["@tenantId"]);

        var roleQuery = executor.Calls[1];
        Assert.Contains("[TenantId] = @tenantId", roleQuery.CommandText);
        Assert.Equal("acme-sacco", roleQuery.Parameters["@tenantId"]);
    }

    [Fact]
    public async Task GetResourceAttributesAsync_TenantScopesQueryWhenMappingConfiguresColumnAsync()
    {
        var options = OptionsWithRoles();
        options.TenantId = "acme-sacco";
        options.ResourceTables["loan_applications"] = new SqlResourceTableMapping
        {
            Table = "LoanApplications",
            IdColumn = "LoanId",
            AttributeColumns = new Dictionary<string, string> { ["amount"] = "PrincipalAmount" },
            TenantColumn = "SaccoId",
        };
        var executor = new FakeSqlQueryExecutor(
            [new Dictionary<string, object?> { ["PrincipalAmount"] = 250_000 }]);
        var provider = new SqlServerAttributeProvider(options, executor);

        await provider.GetResourceAttributesAsync("loan_applications", "LN-1001");

        var query = Assert.Single(executor.Calls);
        Assert.Contains("[SaccoId] = @tenantId", query.CommandText);
        Assert.Equal("acme-sacco", query.Parameters["@tenantId"]);
    }
}