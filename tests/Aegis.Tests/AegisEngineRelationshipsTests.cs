using Aegis.Policies;
using Aegis.Relationships;

using Xunit;

namespace Aegis.Tests;

public class AegisEngineRelationshipsTests
{
    private static ResourcePolicy CommitteePolicy() => new()
    {
        Resource = "loans",
        DerivedRoles = new Dictionary<string, DerivedRoleDefinition>
        {
            ["committeeMember"] = new()
            {
                In = new DerivedRoleHierarchyCheck { Type = "Group", Id = "'audit-committee'" },
            },
        },
        Actions = new Dictionary<string, ActionRule>
        {
            ["review"] = new() { Allow = new AllowRule { Roles = ["committeeMember"] } },
        },
    };

    [Fact]
    public async Task WithoutRelationships_ReBacDerivedRole_AlwaysDeniesAsync()
    {
        using var engine = AegisEngine.FromPolicies([CommitteePolicy()]);
        var principal = AegisPrincipal.Create("alice");
        var resource = AegisResource.Create("loans", "LOAN-1");

        var decision = await engine.AuthorizeAsync(principal, resource, "review");

        Assert.False(decision.Allowed);
    }

    [Fact]
    public async Task WithRelationshipsAsync_ReBacDerivedRole_AllowsWhenGraphHasMatchAsync()
    {
        var provider = new InMemoryRelationshipProvider([
            new EntityParent { Child = new EntityUid("User", "alice"), Parent = new EntityUid("Group", "audit-committee") },
        ]);
        using var engine = await AegisEngine.FromPolicies([CommitteePolicy()]).WithRelationshipsAsync(provider);
        var principal = AegisPrincipal.Create("alice");
        var resource = AegisResource.Create("loans", "LOAN-1");

        var decision = await engine.AuthorizeAsync(principal, resource, "review");

        Assert.True(decision.Allowed);
    }

    [Fact]
    public async Task WithRelationshipsAsync_PreservesDecisionCacheAsync()
    {
        var provider = new InMemoryRelationshipProvider([
            new EntityParent { Child = new EntityUid("User", "alice"), Parent = new EntityUid("Group", "audit-committee") },
        ]);
        using var engine = await AegisEngine.FromPolicies([CommitteePolicy()])
            .WithDecisionCache(new DecisionCacheOptions { Duration = TimeSpan.FromMinutes(1) })
            .WithRelationshipsAsync(provider);
        var principal = AegisPrincipal.Create("alice");
        var resource = AegisResource.Create("loans", "LOAN-1");

        var first = await engine.AuthorizeAsync(principal, resource, "review");
        var second = await engine.AuthorizeAsync(principal, resource, "review");

        Assert.True(first.Allowed);
        Assert.True(second.Allowed);
    }
}