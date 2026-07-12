using System.Security.Claims;

using Aegis.AspNetCore;
using Aegis.Policies;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace Aegis.Tests;

public class AegisAspNetCoreTests
{
    private static string FixturesPath => Path.Combine(AppContext.BaseDirectory, "Fixtures");

    private sealed class FakePolicyProvider(params ResourcePolicy[] policies) : IPolicyProvider
    {
        public Task<IReadOnlyList<ResourcePolicy>> LoadPoliciesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ResourcePolicy>>(policies);
    }

    private sealed class FakeAttributeProvider(IReadOnlyDictionary<string, object?> resourceAttributes)
        : IAttributeProvider
    {
        public Task<PrincipalAttributes> GetPrincipalAttributesAsync(
            string principalId, CancellationToken cancellationToken = default) =>
            Task.FromResult(PrincipalAttributes.Empty);

        public Task<IReadOnlyDictionary<string, object?>> GetResourceAttributesAsync(
            string resourceKind, string resourceId, CancellationToken cancellationToken = default) =>
            Task.FromResult(resourceAttributes);
    }

    [Fact]
    public void AddAegis_NoPolicySource_ThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();

        Assert.Throws<InvalidOperationException>(() => services.AddAegis(_ => { }));
    }

    [Fact]
    public async Task AddAegis_WithPolicies_ResolvesAWorkingEngineAsync()
    {
        var services = new ServiceCollection();
        services.AddAegis(options => options.AddPolicies(FixturesPath));
        var provider = services.BuildServiceProvider();

        var engine = provider.GetRequiredService<AegisEngine>();
        var decision = await engine.AuthorizeAsync(
            AegisPrincipal.Create("alice", roles: ["Finance"]),
            AegisResource.Create("invoices", "INV-1"),
            "view");

        Assert.True(decision.Allowed);
    }

    [Fact]
    public async Task AddAegis_WithPolicyProvider_ResolvesAWorkingEngineAsync()
    {
        var policy = new ResourcePolicy
        {
            Resource = "invoices",
            Actions = new Dictionary<string, ActionRule>
            {
                ["view"] = new() { Allow = new AllowRule { Roles = ["Finance"] } },
            },
        };
        var services = new ServiceCollection();
        services.AddAegis(options => options.AddPolicyProvider(new FakePolicyProvider(policy)));
        var provider = services.BuildServiceProvider();

        var engine = provider.GetRequiredService<AegisEngine>();
        var decision = await engine.AuthorizeAsync(
            AegisPrincipal.Create("alice", roles: ["Finance"]),
            AegisResource.Create("invoices", "INV-1"),
            "view");

        Assert.True(decision.Allowed);
    }

    [Fact]
    public async Task AddAegis_AttributeProviderRegisteredSeparatelyInDI_IsPickedUpAutomaticallyAsync()
    {
        var policy = new ResourcePolicy
        {
            Resource = "invoices",
            Actions = new Dictionary<string, ActionRule>
            {
                ["approve"] = new() { Allow = new AllowRule { When = "principal.department == resource.department" } },
            },
        };
        var services = new ServiceCollection();

        // Registered independently -- as e.g. Aegis.Sql's AddSqlServerAttributeProvider
        // would -- *before* AddAegis, to prove pickup doesn't depend on ordering.
        services.AddSingleton<IAttributeProvider>(
            new FakeAttributeProvider(new Dictionary<string, object?> { ["department"] = "finance" }));
        services.AddAegis(options => options.AddPolicyProvider(new FakePolicyProvider(policy)));

        var provider = services.BuildServiceProvider();
        var engine = provider.GetRequiredService<AegisEngine>();
        var decision = await engine.AuthorizeAsync(
            AegisPrincipal.Create("alice", attributes: new Dictionary<string, object?> { ["department"] = "finance" }),
            AegisResource.Create("invoices", "INV-1"), // no explicit department -- must come from the provider
            "approve");

        Assert.True(decision.Allowed);
    }

    [Fact]
    public async Task AegisEngine_AuthorizeAsync_HttpContext_UsesRegisteredClaimsMapperAsync()
    {
        var services = new ServiceCollection();
        services.AddAegis(options => options.AddPolicies(FixturesPath));
        var provider = services.BuildServiceProvider();
        var engine = provider.GetRequiredService<AegisEngine>();

        var httpContext = new DefaultHttpContext { RequestServices = provider };
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "alice"),
            new Claim(ClaimTypes.Role, "Finance"),
        ], "TestAuth"));

        var decision = await engine.AuthorizeAsync(httpContext, AegisResource.Create("invoices", "INV-1"), "view");

        Assert.True(decision.Allowed);
    }
}