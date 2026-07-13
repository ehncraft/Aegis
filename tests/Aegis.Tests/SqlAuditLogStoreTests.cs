using Aegis.Audit;
using Aegis.Sql;

using Xunit;

namespace Aegis.Tests;

public class SqlAuditLogStoreTests
{
    private sealed class FakeSqlQueryExecutor(
        params IReadOnlyList<IReadOnlyDictionary<string, object?>>[] queryResponses) : ISqlQueryExecutor
    {
        private readonly Queue<IReadOnlyList<IReadOnlyDictionary<string, object?>>> _queryResponses = new(queryResponses);

        public List<(string CommandText, IReadOnlyDictionary<string, object?> Parameters)> ExecuteCalls { get; } = [];

        public List<(string CommandText, IReadOnlyDictionary<string, object?> Parameters)> QueryCalls { get; } = [];

        public Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> QueryAsync(
            string commandText, IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken)
        {
            QueryCalls.Add((commandText, parameters));
            var response = _queryResponses.Count > 0
                ? _queryResponses.Dequeue()
                : Array.Empty<IReadOnlyDictionary<string, object?>>();
            return Task.FromResult(response);
        }

        public Task ExecuteAsync(
            string commandText, IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken)
        {
            ExecuteCalls.Add((commandText, parameters));
            return Task.CompletedTask;
        }
    }

    private static AuditLogEntry Entry() => new()
    {
        PrincipalId = "alice",
        ResourceKind = "invoices",
        ResourceId = "INV-1",
        Action = "view",
        Allowed = true,
        Explanation = new DecisionExplanation { Effect = "allow" },
        Timestamp = DateTimeOffset.UtcNow,
    };

    private static SqlAuditLogStoreOptions Options() => new() { Table = "AegisAuditLog" };

    [Fact]
    public async Task RecordAsync_InsertsWithParameterizedValuesAsync()
    {
        var executor = new FakeSqlQueryExecutor();
        var store = new SqlAuditLogStore(Options(), executor);

        await store.RecordAsync(Entry());

        var call = Assert.Single(executor.ExecuteCalls);
        Assert.Contains("INSERT INTO", call.CommandText);
        Assert.Equal("alice", call.Parameters["@principalId"]);
        Assert.Equal("invoices", call.Parameters["@resourceKind"]);
        Assert.Equal(true, call.Parameters["@allowed"]);
    }

    [Fact]
    public async Task RecordAsync_NoTenantId_WritesEmptyStringTenantAsync()
    {
        var executor = new FakeSqlQueryExecutor();
        var store = new SqlAuditLogStore(Options(), executor);

        await store.RecordAsync(Entry());

        Assert.Equal(string.Empty, executor.ExecuteCalls[0].Parameters["@tenantId"]);
    }

    [Fact]
    public async Task RecordAsync_WithTenantId_WritesConfiguredTenantAsync()
    {
        var options = Options();
        options.TenantId = "acme-sacco";
        var executor = new FakeSqlQueryExecutor();
        var store = new SqlAuditLogStore(options, executor);

        await store.RecordAsync(Entry());

        Assert.Equal("acme-sacco", executor.ExecuteCalls[0].Parameters["@tenantId"]);
    }

    [Fact]
    public async Task QueryAsync_NoFilters_DoesNotAddWhereClauseAsync()
    {
        var executor = new FakeSqlQueryExecutor([]);
        var store = new SqlAuditLogStore(Options(), executor);

        await store.QueryAsync(new AuditLogQuery());

        Assert.DoesNotContain("WHERE", executor.QueryCalls[0].CommandText);
    }

    [Fact]
    public async Task QueryAsync_WithTenantId_ScopesQueryByTenantAsync()
    {
        var options = Options();
        options.TenantId = "acme-sacco";
        var executor = new FakeSqlQueryExecutor([]);
        var store = new SqlAuditLogStore(options, executor);

        await store.QueryAsync(new AuditLogQuery());

        var call = executor.QueryCalls[0];
        Assert.Contains("[TenantId] = @tenantId", call.CommandText);
        Assert.Equal("acme-sacco", call.Parameters["@tenantId"]);
    }

    [Fact]
    public async Task QueryAsync_FiltersCombineWithAndAsync()
    {
        var executor = new FakeSqlQueryExecutor([]);
        var store = new SqlAuditLogStore(Options(), executor);

        await store.QueryAsync(new AuditLogQuery { PrincipalId = "alice", Allowed = false });

        var call = executor.QueryCalls[0];
        Assert.Contains("[PrincipalId] = @principalId", call.CommandText);
        Assert.Contains("[Allowed] = @allowed", call.CommandText);
        Assert.Contains("AND", call.CommandText);
        Assert.Equal("alice", call.Parameters["@principalId"]);
        Assert.Equal(false, call.Parameters["@allowed"]);
    }

    [Fact]
    public async Task QueryAsync_FiltersByResourceKindAndResourceIdAsync()
    {
        var executor = new FakeSqlQueryExecutor([]);
        var store = new SqlAuditLogStore(Options(), executor);

        await store.QueryAsync(new AuditLogQuery { ResourceKind = "invoices", ResourceId = "INV-1" });

        var call = executor.QueryCalls[0];
        Assert.Contains("[ResourceKind] = @resourceKind", call.CommandText);
        Assert.Contains("[ResourceId] = @resourceId", call.CommandText);
        Assert.Equal("invoices", call.Parameters["@resourceKind"]);
        Assert.Equal("INV-1", call.Parameters["@resourceId"]);
    }

    [Fact]
    public async Task QueryAsync_FiltersByActionAsync()
    {
        var executor = new FakeSqlQueryExecutor([]);
        var store = new SqlAuditLogStore(Options(), executor);

        await store.QueryAsync(new AuditLogQuery { Action = "approve" });

        var call = executor.QueryCalls[0];
        Assert.Contains("[Action] = @action", call.CommandText);
        Assert.Equal("approve", call.Parameters["@action"]);
    }

    [Fact]
    public async Task QueryAsync_FiltersByFromAndToAsync()
    {
        var executor = new FakeSqlQueryExecutor([]);
        var store = new SqlAuditLogStore(Options(), executor);
        var from = DateTimeOffset.UtcNow.AddDays(-1);
        var to = DateTimeOffset.UtcNow;

        await store.QueryAsync(new AuditLogQuery { From = from, To = to });

        var call = executor.QueryCalls[0];
        Assert.Contains("[Timestamp] >= @from", call.CommandText);
        Assert.Contains("[Timestamp] <= @to", call.CommandText);
        Assert.Equal(from.UtcDateTime, call.Parameters["@from"]);
        Assert.Equal(to.UtcDateTime, call.Parameters["@to"]);
    }

    [Fact]
    public async Task QueryAsync_PassesLimitAsync()
    {
        var executor = new FakeSqlQueryExecutor([]);
        var store = new SqlAuditLogStore(Options(), executor);

        await store.QueryAsync(new AuditLogQuery { Limit = 5 });

        Assert.Equal(5, executor.QueryCalls[0].Parameters["@limit"]);
    }

    [Fact]
    public async Task QueryAsync_MapsRowsBackToEntriesAsync()
    {
        var explanationJson = """{"Effect":"allow","Conditions":[]}""";
        var executor = new FakeSqlQueryExecutor(
            [
                new Dictionary<string, object?>
                {
                    ["PrincipalId"] = "alice",
                    ["ResourceKind"] = "invoices",
                    ["ResourceId"] = "INV-1",
                    ["Action"] = "view",
                    ["Allowed"] = true,
                    ["ExplanationJson"] = explanationJson,
                    ["Timestamp"] = DateTime.UtcNow,
                },
            ]);
        var store = new SqlAuditLogStore(Options(), executor);

        var results = await store.QueryAsync(new AuditLogQuery());

        var entry = Assert.Single(results);
        Assert.Equal("alice", entry.PrincipalId);
        Assert.Equal("allow", entry.Explanation.Effect);
    }
}