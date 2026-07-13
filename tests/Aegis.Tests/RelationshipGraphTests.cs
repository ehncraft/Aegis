using Aegis.Relationships;

using Xunit;

namespace Aegis.Tests;

public class RelationshipGraphTests
{
    private static readonly EntityUid Alice = new("User", "alice");
    private static readonly EntityUid Bob = new("User", "bob");
    private static readonly EntityUid SeniorAuditors = new("Group", "senior-auditors");
    private static readonly EntityUid AuditCommittee = new("Group", "audit-committee");

    [Fact]
    public void IsIn_EntityIsInItself_ReturnsTrue()
    {
        var graph = new RelationshipGraph([]);

        Assert.True(graph.IsIn(Alice, Alice));
    }

    [Fact]
    public void IsIn_DirectParent_ReturnsTrue()
    {
        var graph = new RelationshipGraph([new EntityParent { Child = Alice, Parent = SeniorAuditors }]);

        Assert.True(graph.IsIn(Alice, SeniorAuditors));
    }

    [Fact]
    public void IsIn_TransitiveGrandparent_ReturnsTrue()
    {
        var graph = new RelationshipGraph([
            new EntityParent { Child = Alice, Parent = SeniorAuditors },
            new EntityParent { Child = SeniorAuditors, Parent = AuditCommittee },
        ]);

        Assert.True(graph.IsIn(Alice, AuditCommittee));
    }

    [Fact]
    public void IsIn_Unrelated_ReturnsFalse()
    {
        var graph = new RelationshipGraph([new EntityParent { Child = Alice, Parent = SeniorAuditors }]);

        Assert.False(graph.IsIn(Bob, SeniorAuditors));
        Assert.False(graph.IsIn(Alice, AuditCommittee));
    }

    [Fact]
    public void IsIn_EmptyGraph_OnlyEntityIsInItself()
    {
        var graph = RelationshipGraph.Empty;

        Assert.True(graph.IsIn(Alice, Alice));
        Assert.False(graph.IsIn(Alice, SeniorAuditors));
    }

    [Fact]
    public void IsIn_CyclicHierarchy_TerminatesAndDoesNotFalselyMatch()
    {
        // A malformed but real-world-possible data set -- a cycle -- must
        // not hang or stack-overflow the BFS, and must not make unrelated
        // entities appear related just because they're on the cycle.
        var graph = new RelationshipGraph([
            new EntityParent { Child = Alice, Parent = SeniorAuditors },
            new EntityParent { Child = SeniorAuditors, Parent = AuditCommittee },
            new EntityParent { Child = AuditCommittee, Parent = SeniorAuditors }, // cycle
        ]);

        Assert.True(graph.IsIn(Alice, AuditCommittee));
        Assert.False(graph.IsIn(Bob, AuditCommittee));
    }
}