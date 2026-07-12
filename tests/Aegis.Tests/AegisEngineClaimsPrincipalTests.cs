using System.Security.Claims;

using Aegis.Policies;

using Xunit;

namespace Aegis.Tests;

public class AegisEngineClaimsPrincipalTests
{
    [Fact]
    public async Task AuthorizeAsync_ClaimsPrincipalOverload_MapsThenAuthorizesAsync()
    {
        var policy = new ResourcePolicy
        {
            Resource = "invoices",
            Actions = new Dictionary<string, ActionRule>
            {
                ["view"] = new() { Allow = new AllowRule { Roles = ["Finance"] } },
            },
        };
        var engine = AegisEngine.FromPolicies([policy]);
        var mapper = new ClaimsPrincipalMapper(new ClaimsMappingOptions());
        var claimsPrincipal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "alice"),
            new Claim(ClaimTypes.Role, "Finance"),
        ], "TestAuth"));

        var decision = await engine.AuthorizeAsync(
            claimsPrincipal, mapper, AegisResource.Create("invoices", "INV-1"), "view");

        Assert.True(decision.Allowed);
    }
}