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
    public async Task QueryAsync_FiltersByResourceKindAsync()
    {
        var store = new InMemoryAuditLogStore();
        await store.RecordAsync(Entry(resourceKind: "invoices"));
        await store.RecordAsync(Entry(resourceKind: "loan_applications"));

        var results = await store.QueryAsync(new AuditLogQuery { ResourceKind = "invoices" });

        var result = Assert.Single(results);
        Assert.Equal("invoices", result.ResourceKind);
    }

    [Fact]
    public async Task QueryAsync_FiltersByResourceIdAsync()
    {
        var store = new InMemoryAuditLogStore();
        await store.RecordAsync(Entry(resourceId: "INV-1"));
        await store.RecordAsync(Entry(resourceId: "INV-2"));

        var results = await store.QueryAsync(new AuditLogQuery { ResourceId = "INV-1" });

        var result = Assert.Single(results);
        Assert.Equal("INV-1", result.ResourceId);
    }

    [Fact]
    public async Task QueryAsync_FiltersByActionAsync()
    {
        var store = new InMemoryAuditLogStore();
        await store.RecordAsync(Entry(action: "view"));
        await store.RecordAsync(Entry(action: "approve"));

        var results = await store.QueryAsync(new AuditLogQuery { Action = "approve" });

        var result = Assert.Single(results);
        Assert.Equal("approve", result.Action);
    }

    [Fact]
    public async Task QueryAsync_FiltersByFromAsync()
    {
        var store = new InMemoryAuditLogStore();
        var now = DateTimeOffset.UtcNow;
        await store.RecordAsync(Entry(timestamp: now.AddDays(-2)));
        await store.RecordAsync(Entry(timestamp: now));

        var results = await store.QueryAsync(new AuditLogQuery { From = now.AddDays(-1) });

        Assert.Single(results);
    }

    [Fact]
    public async Task QueryAsync_FiltersByToAsync()
    {
        var store = new InMemoryAuditLogStore();
        var now = DateTimeOffset.UtcNow;
        await store.RecordAsync(Entry(timestamp: now.AddDays(-2)));
        await store.RecordAsync(Entry(timestamp: now));

        var results = await store.QueryAsync(new AuditLogQuery { To = now.AddDays(-1) });

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