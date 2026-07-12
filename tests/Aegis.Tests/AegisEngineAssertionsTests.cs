using Aegis.Testing;

using Xunit;

namespace Aegis.Tests;

public class AegisEngineAssertionsTests
{
    private static string FixturesPath => Path.Combine(AppContext.BaseDirectory, "Fixtures");

    private static AegisEngine Engine() => AegisEngine.Create(FixturesPath);

    [Fact]
    public async Task ShouldAllowAsync_WhenDecisionAllows_DoesNotThrow()
    {
        using var engine = Engine();
        var principal = AegisPrincipal.Create("alice", roles: ["Finance"]);
        var resource = AegisResource.Create("invoices", "INV-1");

        var exception = await Record.ExceptionAsync(() => engine.ShouldAllowAsync(principal, resource, "view"));

        Assert.Null(exception);
    }

    [Fact]
    public async Task ShouldAllowAsync_WhenDecisionDenies_ThrowsPolicyAssertionException()
    {
        using var engine = Engine();
        var principal = AegisPrincipal.Create("bob", roles: ["Sales"]);
        var resource = AegisResource.Create("invoices", "INV-1");

        var ex = await Assert.ThrowsAsync<PolicyAssertionException>(
            () => engine.ShouldAllowAsync(principal, resource, "view"));

        Assert.False(ex.Decision.Allowed);
        Assert.Contains("Expected 'allow'", ex.Message);
        Assert.Contains("got 'deny'", ex.Message);
    }

    [Fact]
    public async Task ShouldDenyAsync_WhenDecisionDenies_DoesNotThrow()
    {
        using var engine = Engine();
        var principal = AegisPrincipal.Create("bob", roles: ["Sales"]);
        var resource = AegisResource.Create("invoices", "INV-1");

        var exception = await Record.ExceptionAsync(() => engine.ShouldDenyAsync(principal, resource, "view"));

        Assert.Null(exception);
    }

    [Fact]
    public async Task ShouldDenyAsync_WhenDecisionAllows_ThrowsPolicyAssertionException()
    {
        using var engine = Engine();
        var principal = AegisPrincipal.Create("alice", roles: ["Finance"]);
        var resource = AegisResource.Create("invoices", "INV-1");

        var ex = await Assert.ThrowsAsync<PolicyAssertionException>(
            () => engine.ShouldDenyAsync(principal, resource, "view"));

        Assert.True(ex.Decision.Allowed);
        Assert.Contains("Expected 'deny'", ex.Message);
        Assert.Contains("got 'allow'", ex.Message);
    }

    [Fact]
    public async Task PolicyAssertionException_MessageIncludesConditionsForDebugging()
    {
        using var engine = Engine();
        var principal = AegisPrincipal.Create("alice",
            attributes: new Dictionary<string, object?> { ["department"] = "finance" });
        var resource = AegisResource.Create("invoices", "INV-1",
            attributes: new Dictionary<string, object?> { ["department"] = "engineering" });

        var ex = await Assert.ThrowsAsync<PolicyAssertionException>(
            () => engine.ShouldAllowAsync(principal, resource, "approve"));

        Assert.Contains("invoice-policy", ex.Message);
        Assert.Contains("principal.department == resource.department => false", ex.Message);
    }
}