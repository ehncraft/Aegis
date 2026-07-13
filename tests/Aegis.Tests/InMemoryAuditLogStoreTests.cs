using Aegis.Audit;

using Xunit;

namespace Aegis.Tests;

public class InMemoryAuditLogStoreTests
{
    private static AuditLogEntry Entry(
        string principalId = "alice",
        string resourceKind = "invoices",
        string? resourceId = "INV-1",
        string action = "view",
        bool allowed = true,
        DateTimeOffset? timestamp = null) => new()
        {
            PrincipalId = principalId,
            ResourceKind = resourceKind,
            ResourceId = resourceId,
            Action = action,
            Allowed = allowed,
            Explanation = new DecisionExplanation { Effect = allowed ? "allow" : "deny" },
            Timestamp = timestamp ?? DateTimeOffset.UtcNow,
        };

    [Fact]
    public async Task QueryAsync_NoFilters_ReturnsEveryEntryAsync()
    {
        var store = new InMemoryAuditLogStore();
        await store.RecordAsync(Entry());
        await store.RecordAsync(Entry(principalId: "bob"));

        var results = await store.QueryAsync(new AuditLogQuery());

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task QueryAsync_FiltersByPrincipalIdAsync()
    {
        var store = new InMemoryAuditLogStore();
        await store.RecordAsync(Entry(principalId: "alice"));
        await store.RecordAsync(Entry(principalId: "bob"));

        var results = await store.QueryAsync(new AuditLogQuery { PrincipalId = "alice" });

        var result = Assert.Single(results);
        Assert.Equal("alice", result.PrincipalId);
    }

    [Fact]
    public async Task QueryAsync_FiltersByAllowedAsync()
    {
        var store = new InMemoryAuditLogStore();
        await store.RecordAsync(Entry(allowed: true));
        await store.RecordAsync(Entry(allowed: false));

        var results = await store.QueryAsync(new AuditLogQuery { Allowed = false });

        var result = Assert.Single(results);
        Assert.False(result.Allowed);
    }

    [Fact]
    public async Task QueryAsync_FiltersByTimeRangeAsync()
    {
        var store = new InMemoryAuditLogStore();
        var now = DateTimeOffset.UtcNow;
        await store.RecordAsync(Entry(timestamp: now.AddDays(-2)));
        await store.RecordAsync(Entry(timestamp: now));

        var results = await store.QueryAsync(new AuditLogQuery { From = now.AddDays(-1) });

        Assert.Single(results);
    }

    [Fact]
    public async Task QueryAsync_OrdersByTimestampDescendingAsync()
    {
        var store = new InMemoryAuditLogStore();
        var now = DateTimeOffset.UtcNow;
        await store.RecordAsync(Entry(action: "older", timestamp: now.AddMinutes(-5)));
        await store.RecordAsync(Entry(action: "newer", timestamp: now));

        var results = await store.QueryAsync(new AuditLogQuery());

        Assert.Equal("newer", results[0].Action);
        Assert.Equal("older", results[1].Action);
    }

    [Fact]
    public async Task QueryAsync_RespectsLimitAsync()
    {
        var store = new InMemoryAuditLogStore();
        for (var i = 0; i < 5; i++)
        {
            await store.RecordAsync(Entry());
        }

        var results = await store.QueryAsync(new AuditLogQuery { Limit = 2 });

        Assert.Equal(2, results.Count);
    }
}