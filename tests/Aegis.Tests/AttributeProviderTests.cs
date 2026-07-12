using Aegis.Policies;

using Xunit;

namespace Aegis.Tests;

public class AttributeProviderTests
{
    private static ResourcePolicy InvoicePolicy() => new()
    {
        Resource = "invoices",
        Name = "invoice-policy",
        Actions = new Dictionary<string, ActionRule>
        {
            ["view"] = new() { Allow = new AllowRule { Roles = ["Finance"] } },
            ["approve"] = new()
            {
                Allow = new AllowRule { When = "principal.department == resource.department" },
            },
        },
    };

    private sealed class FakeAttributeProvider(
        PrincipalAttributes? principalAttributes = null,
        IReadOnlyDictionary<string, object?>? resourceAttributes = null) : IAttributeProvider
    {
        public Task<PrincipalAttributes> GetPrincipalAttributesAsync(
            string principalId, CancellationToken cancellationToken = default) =>
            Task.FromResult(principalAttributes ?? PrincipalAttributes.Empty);

        public Task<IReadOnlyDictionary<string, object?>> GetResourceAttributesAsync(
            string resourceKind, string resourceId, CancellationToken cancellationToken = default) =>
            Task.FromResult(resourceAttributes ?? new Dictionary<string, object?>());
    }

    [Fact]
    public async Task NoProviders_BehavesExactlyLikeBeforeAsync()
    {
        var engine = AegisEngine.FromPolicies([InvoicePolicy()]);
        var principal = AegisPrincipal.Create("alice", roles: ["Finance"]);
        var resource = AegisResource.Create("invoices", "INV-1");

        var decision = await engine.AuthorizeAsync(principal, resource, "view");

        Assert.True(decision.Allowed);
    }

    [Fact]
    public async Task ProviderSuppliedRole_GrantsAccessAsync()
    {
        var provider = new FakeAttributeProvider(
            principalAttributes: new PrincipalAttributes { Roles = ["Finance"] });
        var engine = AegisEngine.FromPolicies([InvoicePolicy()], provider);

        var principal = AegisPrincipal.Create("alice"); // no roles set explicitly
        var resource = AegisResource.Create("invoices", "INV-1");

        var decision = await engine.AuthorizeAsync(principal, resource, "view");

        Assert.True(decision.Allowed);
    }

    [Fact]
    public async Task ProviderSuppliedAttributes_SatisfyAbacConditionAsync()
    {
        var provider = new FakeAttributeProvider(
            principalAttributes: new PrincipalAttributes
            {
                Attributes = new Dictionary<string, object?> { ["department"] = "finance" },
            },
            resourceAttributes: new Dictionary<string, object?> { ["department"] = "finance" });
        var engine = AegisEngine.FromPolicies([InvoicePolicy()], provider);

        var principal = AegisPrincipal.Create("alice");
        var resource = AegisResource.Create("invoices", "INV-1");

        var decision = await engine.AuthorizeAsync(principal, resource, "approve");

        Assert.True(decision.Allowed);
    }

    [Fact]
    public async Task ExplicitAttribute_WinsOverProviderSuppliedAsync()
    {
        var provider = new FakeAttributeProvider(
            principalAttributes: new PrincipalAttributes
            {
                Attributes = new Dictionary<string, object?> { ["department"] = "finance" },
            });
        var engine = AegisEngine.FromPolicies([InvoicePolicy()], provider);

        // Caller explicitly set a different department than the provider would supply.
        var principal = AegisPrincipal.Create("alice",
            attributes: new Dictionary<string, object?> { ["department"] = "engineering" });
        var resource = AegisResource.Create("invoices", "INV-1",
            attributes: new Dictionary<string, object?> { ["department"] = "engineering" });

        var decision = await engine.AuthorizeAsync(principal, resource, "approve");

        Assert.True(decision.Allowed);
        Assert.Equal("principal.department == resource.department",
            decision.Explanation.Conditions[0].Expression);
    }

    [Fact]
    public async Task FirstProvider_WinsOnAttributeConflictAsync()
    {
        var first = new FakeAttributeProvider(
            principalAttributes: new PrincipalAttributes
            {
                Attributes = new Dictionary<string, object?> { ["department"] = "finance" },
            });
        var second = new FakeAttributeProvider(
            principalAttributes: new PrincipalAttributes
            {
                Attributes = new Dictionary<string, object?> { ["department"] = "engineering" },
            });
        var engine = AegisEngine.FromPolicies([InvoicePolicy()], first, second);

        var principal = AegisPrincipal.Create("alice");
        var resource = AegisResource.Create("invoices", "INV-1",
            attributes: new Dictionary<string, object?> { ["department"] = "finance" });

        var decision = await engine.AuthorizeAsync(principal, resource, "approve");

        Assert.True(decision.Allowed);
    }

    [Fact]
    public async Task ResourceWithoutId_SkipsResourceEnrichmentAsync()
    {
        var provider = new FakeAttributeProvider(
            resourceAttributes: new Dictionary<string, object?> { ["department"] = "finance" });
        var engine = AegisEngine.FromPolicies([InvoicePolicy()], provider);

        var principal = AegisPrincipal.Create("alice",
            attributes: new Dictionary<string, object?> { ["department"] = "finance" });
        var resource = AegisResource.Create("invoices"); // no id -> can't be looked up

        var decision = await engine.AuthorizeAsync(principal, resource, "approve");

        Assert.False(decision.Allowed);
    }
}