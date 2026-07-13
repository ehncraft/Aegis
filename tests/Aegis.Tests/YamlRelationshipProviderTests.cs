using Aegis.Relationships;

using Xunit;

namespace Aegis.Tests;

public class YamlRelationshipProviderTests
{
    private static string FixturesPath => Path.Combine(AppContext.BaseDirectory, "Fixtures", "Entities");

    [Fact]
    public async Task LoadEntityParentsAsync_ParsesEveryEntitysParents()
    {
        var provider = new YamlRelationshipProvider(FixturesPath);

        var entityParents = await provider.LoadEntityParentsAsync();

        Assert.Contains(entityParents, p =>
            p.Child == new EntityUid("User", "alice") && p.Parent == new EntityUid("Group", "senior-auditors"));
        Assert.Contains(entityParents, p =>
            p.Child == new EntityUid("Group", "senior-auditors") && p.Parent == new EntityUid("Group", "audit-committee"));
    }

    [Fact]
    public async Task LoadEntityParentsAsync_EntityWithNoParents_ContributesNothing()
    {
        var provider = new YamlRelationshipProvider(FixturesPath);

        var entityParents = await provider.LoadEntityParentsAsync();

        Assert.DoesNotContain(entityParents, p => p.Child == new EntityUid("Group", "audit-committee"));
    }

    [Fact]
    public async Task LoadEntityParentsAsync_BuildsAWorkingGraphAsync()
    {
        var provider = new YamlRelationshipProvider(FixturesPath);
        var entityParents = await provider.LoadEntityParentsAsync();
        var graph = new RelationshipGraph(entityParents);

        Assert.True(graph.IsIn(new EntityUid("User", "alice"), new EntityUid("Group", "audit-committee")));
    }

    [Fact]
    public async Task LoadEntityParentsAsync_MissingDirectory_ThrowsAsync()
    {
        var provider = new YamlRelationshipProvider(Path.Combine(FixturesPath, "does-not-exist"));

        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => provider.LoadEntityParentsAsync());
    }
}