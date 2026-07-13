using Aegis.Audit;
using Aegis.Policies;
using Aegis.Sql;

using Microsoft.Data.SqlClient;

using Testcontainers.MsSql;

using Xunit;

namespace Aegis.IntegrationTests;

/// <summary>
/// Exercises <see cref="SqlAuditLogStore"/> against a real SQL Server
/// instance, including the DDL script it's meant to be provisioned with.
/// </summary>
public sealed class SqlAuditLogStoreIntegrationTests : IAsyncLifetime
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

    private SqlAuditLogStoreOptions Options() => new() { ConnectionString = _container.GetConnectionString() };

    private static AuditLogEntry Entry(string principalId = "alice", bool allowed = true) => new()
    {
        PrincipalId = principalId,
        ResourceKind = "invoices",
        ResourceId = "INV-1",
        Action = "view",
        Allowed = allowed,
        Explanation = new DecisionExplanation
        {
            Effect = allowed ? "allow" : "deny",
            MatchedPolicy = "invoice-policy",
            Conditions = [new ConditionExplanation { Expression = "principal.roles intersects [Finance]", Result = allowed }],
        },
        Timestamp = DateTimeOffset.UtcNow,
    };

    [Fact]
    public async Task RecordAndQueryAsync_RoundTripsThroughTheShippedDdlScriptAsync()
    {
        var scriptPath = Path.Combine(AppContext.BaseDirectory, "CreateAuditLogTable.sql");
        await ExecuteNonQueryAsync(await File.ReadAllTextAsync(scriptPath));

        var store = new SqlAuditLogStore(Options());
        await store.RecordAsync(Entry());

        var results = await store.QueryAsync(new AuditLogQuery());

        var entry = Assert.Single(results);
        Assert.Equal("alice", entry.PrincipalId);
        Assert.Equal("invoices", entry.ResourceKind);
        Assert.Equal("INV-1", entry.ResourceId);
        Assert.True(entry.Allowed);
        Assert.Equal("allow", entry.Explanation.Effect);
        Assert.Equal("invoice-policy", entry.Explanation.MatchedPolicy);
        Assert.Single(entry.Explanation.Conditions);
    }

    [Fact]
    public async Task QueryAsync_FiltersByPrincipalIdAgainstRealDataAsync()
    {
        var scriptPath = Path.Combine(AppContext.BaseDirectory, "CreateAuditLogTable.sql");
        await ExecuteNonQueryAsync(await File.ReadAllTextAsync(scriptPath));

        var store = new SqlAuditLogStore(Options());
        await store.RecordAsync(Entry(principalId: "alice"));
        await store.RecordAsync(Entry(principalId: "bob"));

        var results = await store.QueryAsync(new AuditLogQuery { PrincipalId = "alice" });

        var entry = Assert.Single(results);
        Assert.Equal("alice", entry.PrincipalId);
    }

    [Fact]
    public async Task RecordAndQueryAsync_EndToEndThroughAegisEngineAsync()
    {
        var scriptPath = Path.Combine(AppContext.BaseDirectory, "CreateAuditLogTable.sql");
        await ExecuteNonQueryAsync(await File.ReadAllTextAsync(scriptPath));

        var policy = new ResourcePolicy
        {
            Resource = "invoices",
            Actions = new Dictionary<string, ActionRule>
            {
                ["view"] = new() { Allow = new AllowRule { Roles = ["Finance"] } },
            },
        };
        var store = new SqlAuditLogStore(Options());
        using var engine = AegisEngine.FromPolicies([policy]).WithAuditLog(store);
        var principal = AegisPrincipal.Create("officer-1", roles: ["Finance"]);
        var resource = AegisResource.Create("invoices", "INV-1");

        await engine.AuthorizeAsync(principal, resource, "view");

        var results = await store.QueryAsync(new AuditLogQuery());
        var entry = Assert.Single(results);
        Assert.Equal("officer-1", entry.PrincipalId);
        Assert.True(entry.Allowed);
    }
}