namespace Aegis.Relationships;

/// <summary>
/// Supplies entity-hierarchy facts from a pluggable source -- a file, a
/// database, an API -- separate from policy storage (<c>IPolicyProvider</c>).
/// </summary>
public interface IRelationshipProvider
{
    Task<IReadOnlyList<EntityParent>> LoadEntityParentsAsync(CancellationToken cancellationToken = default);
}