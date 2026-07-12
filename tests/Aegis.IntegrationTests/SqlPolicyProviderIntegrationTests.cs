using Aegis.Policies;
using Aegis.Sql;

using Microsoft.Data.SqlClient;

using Testcontainers.MsSql;

using Xunit;

namespace Aegis.IntegrationTests;

/// <summary>
/// Exercises <see cref="SqlPolicyProvider"/> against a real SQL Server
/// instance, including the DDL script it's meant to be provisioned with.
/// </summary>
public sealed class SqlPolicyProviderIntegrationTests : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder(
            "mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04")
        .Build();

    public async Task InitializeAsync() => await _container.StartAsync();

    public async Task DisposeAsync() => await _container.DisposeAsync();

    private async Task ExecuteNonQueryAsync(string sql)
    {
        await using var connection = new SqlConnection(_container.GetConnectionString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private SqlPolicyStoreOptions Options() => new() { ConnectionString = _container.GetConnectionString() };

    [Fact]
    public async Task LoadPoliciesAsync_RoundTripsAPolicyWrittenViaTheShippedDdlScriptAsync()
    {
        var scriptPath = Path.Combine(AppContext.BaseDirectory, "CreatePolicyTable.sql");
        await ExecuteNonQueryAsync(await File.ReadAllTextAsync(scriptPath));

        const string yaml = """
            resource: loan_applications
            actions:
              approve:
                allow:
                  when: principal.branch == resource.branch && resource.amount <= principal.approvalLimit
            """;
        await ExecuteNonQueryAsync(
            $"INSERT INTO AegisPolicies (ResourceName, PolicyYaml) VALUES ('loan_applications', N'{yaml.Replace("'", "''")}')");

        var provider = new SqlPolicyProvider(Options());
        var policies = await provider.LoadPoliciesAsync();

        var policy = Assert.Single(policies);
        Assert.Equal("loan_applications", policy.Resource);
        Assert.Equal(
            "principal.branch == resource.branch && resource.amount <= principal.approvalLimit",
            policy.Actions["approve"].Allow!.When);
    }

    [Fact]
    public async Task LoadPoliciesAsync_EndToEndThroughAegisEngineAsync()
    {
        await ExecuteNonQueryAsync("""
            CREATE TABLE AegisPolicies (ResourceName NVARCHAR(200) PRIMARY KEY, PolicyYaml NVARCHAR(MAX) NOT NULL);
            INSERT INTO AegisPolicies (ResourceName, PolicyYaml) VALUES ('loan_applications', N'
            resource: loan_applications
            actions:
              view:
                allow:
                  roles:
                    - LoanOfficer
            ');
            """);

        var engine = await AegisEngine.CreateAsync(new SqlPolicyProvider(Options()));
        var principal = AegisPrincipal.Create("officer-1", roles: ["LoanOfficer"]);
        var resource = AegisResource.Create("loan_applications", "LN-1001");

        var decision = await engine.AuthorizeAsync(principal, resource, "view");

        Assert.True(decision.Allowed);
    }

    [Fact]
    public async Task LoadPoliciesAsync_MissingTable_ThrowsPolicyLoadExceptionAsync()
    {
        // No CREATE TABLE call -- the table genuinely doesn't exist.
        var provider = new SqlPolicyProvider(Options());

        var ex = await Assert.ThrowsAsync<PolicyLoadException>(() => provider.LoadPoliciesAsync());
        Assert.Equal("sql:AegisPolicies", ex.PolicySource);
        Assert.IsType<SqlException>(ex.InnerException);
    }
}