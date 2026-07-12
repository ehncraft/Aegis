using System.Security.Claims;

using Aegis.Policies;

using Xunit;

namespace Aegis.Tests;

public class ClaimsPrincipalMapperTests
{
    private static ClaimsPrincipal PrincipalWith(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, "TestAuth"));

    [Fact]
    public void Map_DefaultOptions_ReadsIdAndRoleFromAspNetCoreDefaultInboundMappedClaims()
    {
        // What HttpContext.User actually looks like by default -- the JWT
        // bearer handler remaps short "sub"/"role" claims to these URIs
        // unless MapInboundClaims = false.
        var claimsPrincipal = PrincipalWith(
            new Claim(ClaimTypes.NameIdentifier, "officer-1"),
            new Claim(ClaimTypes.Role, "LoanOfficer"));
        var mapper = new ClaimsPrincipalMapper(new ClaimsMappingOptions());

        var principal = mapper.Map(claimsPrincipal);

        Assert.Equal("officer-1", principal.Id);
        Assert.Equal(["LoanOfficer"], principal.Roles);
    }

    [Fact]
    public void Map_ShortClaimTypes_WorksWhenConfiguredForMapInboundClaimsFalse()
    {
        // The other common real-world shape: MapInboundClaims = false, so
        // tokens keep their original JWT-standard short claim names.
        var claimsPrincipal = PrincipalWith(
            new Claim("sub", "officer-1"),
            new Claim("role", "LoanOfficer"));
        var mapper = new ClaimsPrincipalMapper(new ClaimsMappingOptions
        {
            PrincipalIdClaimType = "sub",
            RoleClaimType = "role",
        });

        var principal = mapper.Map(claimsPrincipal);

        Assert.Equal("officer-1", principal.Id);
        Assert.Equal(["LoanOfficer"], principal.Roles);
    }

    [Fact]
    public void Map_MultipleRoleClaims_ReturnsAllRoles()
    {
        var claimsPrincipal = PrincipalWith(
            new Claim(ClaimTypes.NameIdentifier, "officer-1"),
            new Claim(ClaimTypes.Role, "LoanOfficer"),
            new Claim(ClaimTypes.Role, "Teller"));
        var mapper = new ClaimsPrincipalMapper(new ClaimsMappingOptions());

        var principal = mapper.Map(claimsPrincipal);

        Assert.Equal(["LoanOfficer", "Teller"], principal.Roles.OrderBy(role => role));
    }

    [Fact]
    public void Map_ConfiguredAttributeClaims_ParsesEachValueKind()
    {
        var claimsPrincipal = PrincipalWith(
            new Claim(ClaimTypes.NameIdentifier, "officer-1"),
            new Claim("department", "finance"),
            new Claim("approval_limit", "500000.50"),
            new Claim("is_senior", "true"));
        var mapper = new ClaimsPrincipalMapper(new ClaimsMappingOptions
        {
            AttributeClaims = new Dictionary<string, ClaimAttributeMapping>
            {
                ["department"] = new() { ClaimType = "department", ValueKind = ClaimValueKind.String },
                ["approvalLimit"] = new() { ClaimType = "approval_limit", ValueKind = ClaimValueKind.Decimal },
                ["isSenior"] = new() { ClaimType = "is_senior", ValueKind = ClaimValueKind.Boolean },
            },
        });

        var principal = mapper.Map(claimsPrincipal);

        Assert.Equal("finance", principal.Attributes["department"]);
        Assert.Equal(500_000.50m, principal.Attributes["approvalLimit"]);
        Assert.Equal(true, principal.Attributes["isSenior"]);
    }

    [Fact]
    public void Map_MissingOptionalAttributeClaim_IsSkippedNotAnError()
    {
        var claimsPrincipal = PrincipalWith(new Claim(ClaimTypes.NameIdentifier, "officer-1"));
        var mapper = new ClaimsPrincipalMapper(new ClaimsMappingOptions
        {
            AttributeClaims = new Dictionary<string, ClaimAttributeMapping>
            {
                ["department"] = new() { ClaimType = "department" },
            },
        });

        var principal = mapper.Map(claimsPrincipal);

        Assert.False(principal.Attributes.ContainsKey("department"));
    }

    [Fact]
    public void Map_MissingIdClaim_ThrowsInvalidOperationException()
    {
        var claimsPrincipal = PrincipalWith(new Claim(ClaimTypes.Role, "LoanOfficer"));
        var mapper = new ClaimsPrincipalMapper(new ClaimsMappingOptions());

        Assert.Throws<InvalidOperationException>(() => mapper.Map(claimsPrincipal));
    }

    [Fact]
    public void Map_UnparseableNumericClaim_ThrowsWithAClearMessage()
    {
        var claimsPrincipal = PrincipalWith(
            new Claim(ClaimTypes.NameIdentifier, "officer-1"),
            new Claim("approval_limit", "not-a-number"));
        var mapper = new ClaimsPrincipalMapper(new ClaimsMappingOptions
        {
            AttributeClaims = new Dictionary<string, ClaimAttributeMapping>
            {
                ["approvalLimit"] = new() { ClaimType = "approval_limit", ValueKind = ClaimValueKind.Decimal },
            },
        });

        var ex = Assert.Throws<InvalidOperationException>(() => mapper.Map(claimsPrincipal));
        Assert.Contains("approval_limit", ex.Message);
    }

    [Fact]
    public async Task MappedPrincipal_NumericAttributeSatisfiesPolicyComparisonAsync()
    {
        var claimsPrincipal = PrincipalWith(
            new Claim(ClaimTypes.NameIdentifier, "officer-1"),
            new Claim(ClaimTypes.Role, "LoanOfficer"),
            new Claim("approval_limit", "500000"));
        var mapper = new ClaimsPrincipalMapper(new ClaimsMappingOptions
        {
            AttributeClaims = new Dictionary<string, ClaimAttributeMapping>
            {
                ["approvalLimit"] = new() { ClaimType = "approval_limit", ValueKind = ClaimValueKind.Decimal },
            },
        });
        var principal = mapper.Map(claimsPrincipal);

        var policy = new ResourcePolicy
        {
            Resource = "loan_applications",
            Actions = new Dictionary<string, ActionRule>
            {
                ["approve"] = new() { Allow = new AllowRule { When = "resource.amount <= principal.approvalLimit" } },
            },
        };
        var engine = AegisEngine.FromPolicies([policy]);
        var resource = AegisResource.Create("loan_applications", "LN-1001",
            attributes: new Dictionary<string, object?> { ["amount"] = 250_000 });

        var decision = await engine.AuthorizeAsync(principal, resource, "approve");

        Assert.True(decision.Allowed);
    }
}