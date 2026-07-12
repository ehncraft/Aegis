using Aegis.Policies;
using Aegis.Sql;

using Microsoft.Data.SqlClient;

using Testcontainers.MsSql;

using Xunit;

namespace Aegis.IntegrationTests;

/// <summary>
/// Exercises <see cref="SqlServerQueryExecutor"/> -- the real ADO.NET
/// implementation of <see cref="ISqlQueryExecutor"/> -- against an actual
/// SQL Server instance, since that's the one piece <see cref="SqlServerAttributeProviderTests"/>
/// deliberately can't cover with a fake. Requires Docker; skipped automatically
/// (via container startup failure surfacing as a test failure) if unavailable.
/// </summary>
public sealed class SqlServerAttributeProviderIntegrationTests : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder(
            "mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04")
        .Build();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await ExecuteNonQueryAsync("""
            CREATE TABLE Users (UserId NVARCHAR(50) PRIMARY KEY, DepartmentName NVARCHAR(100), BranchCode NVARCHAR(50));
            CREATE TABLE UserRoles (UserId NVARCHAR(50), RoleName NVARCHAR(100));
            CREATE TABLE LoanApplications (LoanId NVARCHAR(50) PRIMARY KEY, PrincipalAmount INT, ApplicantUserId NVARCHAR(50));

            INSERT INTO Users (UserId, DepartmentName, BranchCode) VALUES ('officer-1', 'finance', 'nairobi-cbd');
            INSERT INTO UserRoles (UserId, RoleName) VALUES ('officer-1', 'LoanOfficer');
            INSERT INTO UserRoles (UserId, RoleName) VALUES ('officer-1', 'Teller');
            INSERT INTO LoanApplications (LoanId, PrincipalAmount, ApplicantUserId) VALUES ('LN-1001', 250000, 'member-42');
            """);
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    private async Task ExecuteNonQueryAsync(string sql)
    {
        await using var connection = new SqlConnection(_container.GetConnectionString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private SqlServerAttributeProvider CreateProvider() => new(new SqlAttributeProviderOptions
    {
        ConnectionString = _container.GetConnectionString(),
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
        ResourceTables = new Dictionary<string, SqlResourceTableMapping>(StringComparer.OrdinalIgnoreCase)
        {
            ["loan_applications"] = new SqlResourceTableMapping
            {
                Table = "LoanApplications",
                IdColumn = "LoanId",
                AttributeColumns = new Dictionary<string, string>
                {
                    ["amount"] = "PrincipalAmount",
                    ["applicantId"] = "ApplicantUserId",
                },
            },
        },
    });

    [Fact]
    public async Task GetPrincipalAttributesAsync_ReadsRealRowsFromSqlServerAsync()
    {
        var provider = CreateProvider();

        var result = await provider.GetPrincipalAttributesAsync("officer-1");

        Assert.Equal("finance", result.Attributes["department"]);
        Assert.Equal("nairobi-cbd", result.Attributes["branch"]);
        Assert.Equal(["LoanOfficer", "Teller"], result.Roles.OrderBy(role => role));
    }

    [Fact]
    public async Task GetPrincipalAttributesAsync_UnknownPrincipal_ReturnsEmptyAsync()
    {
        var provider = CreateProvider();

        var result = await provider.GetPrincipalAttributesAsync("does-not-exist");

        Assert.Empty(result.Attributes);
        Assert.Empty(result.Roles);
    }

    [Fact]
    public async Task GetResourceAttributesAsync_ReadsRealRowsFromSqlServerAsync()
    {
        var provider = CreateProvider();

        var result = await provider.GetResourceAttributesAsync("loan_applications", "LN-1001");

        Assert.Equal(250_000, result["amount"]);
        Assert.Equal("member-42", result["applicantId"]);
    }

    [Fact]
    public async Task AegisEngine_EndToEnd_EnrichesFromRealSqlServerAsync()
    {
        var policy = new ResourcePolicy
        {
            Resource = "loan_applications",
            Actions = new Dictionary<string, ActionRule>
            {
                ["approve"] = new()
                {
                    Allow = new AllowRule
                    {
                        When = "principal.branch == \"nairobi-cbd\" && resource.amount <= 500000",
                    },
                },
            },
        };
        var engine = AegisEngine.FromPolicies([policy], CreateProvider());

        var principal = AegisPrincipal.Create("officer-1");
        var resource = AegisResource.Create("loan_applications", "LN-1001");

        var decision = await engine.AuthorizeAsync(principal, resource, "approve");

        Assert.True(decision.Allowed);
    }
}