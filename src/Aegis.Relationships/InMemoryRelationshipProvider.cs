namespace Aegis.Relationships;

/// <summary>A fixed, in-process set of entity-parent facts -- for tests and quick prototyping.</summary>
public sealed class InMemoryRelationshipProvider(IEnumerable<EntityParent> entityParents) : IRelationshipProvider
{
    private readonly IReadOnlyList<EntityParent> _entityParents = [.. entityParents];

    public Task<IReadOnlyList<EntityParent>> LoadEntityParentsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_entityParents);
}