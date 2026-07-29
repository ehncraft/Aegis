namespace Aegis.Relationships;

/// <summary>
/// In-memory index over a fixed set of entity-parent facts, implementing
/// Cedar's <c>in</c> operator (https://docs.cedarpolicy.com/policies/syntax-datatypes.html#operator-in):
/// <c>X in Y</c> is true if <c>X == Y</c>, or <c>Y</c> is reachable by
/// walking up <c>X</c>'s parent hierarchy. Built once from a fixed entity
/// set, like <c>PolicyEvaluator</c> compiles its expressions once at
/// construction rather than per-request.
/// </summary>
public sealed class RelationshipGraph
{
    private readonly ILookup<EntityUid, EntityUid> _parentsByChild;
    private readonly ILookup<EntityUid, EntityUid> _childrenByParent;

    public RelationshipGraph(IEnumerable<EntityParent> entityParents)
    {
        var entityParentList = entityParents as IReadOnlyCollection<EntityParent> ?? [.. entityParents];
        _parentsByChild = entityParentList.ToLookup(p => p.Child, p => p.Parent);
        _childrenByParent = entityParentList.ToLookup(p => p.Parent, p => p.Child);
    }

    public static RelationshipGraph Empty { get; } = new([]);

    /// <summary>
    /// True if <paramref name="descendant"/> equals <paramref name="ancestor"/>,
    /// or <paramref name="ancestor"/> is reachable from <paramref name="descendant"/>
    /// by walking up the parent hierarchy (breadth-first, cycle-safe via a
    /// visited set -- entity data, unlike a policy's variables, isn't
    /// statically validated for cycles up front).
    /// </summary>
    public bool IsIn(EntityUid descendant, EntityUid ancestor)
    {
        if (descendant == ancestor)
        {
            return true;
        }

        var visited = new HashSet<EntityUid> { descendant };
        var queue = new Queue<EntityUid>();
        queue.Enqueue(descendant);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            foreach (var parent in _parentsByChild[current])
            {
                if (parent == ancestor)
                {
                    return true;
                }

                if (visited.Add(parent))
                {
                    queue.Enqueue(parent);
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Every entity reachable from <paramref name="ancestor"/> by walking
    /// down the parent hierarchy (breadth-first, cycle-safe) -- the reverse
    /// of <see cref="IsIn"/>'s traversal. Does not include
    /// <paramref name="ancestor"/> itself.
    /// </summary>
    public IReadOnlySet<EntityUid> Descendants(EntityUid ancestor)
    {
        var visited = new HashSet<EntityUid> { ancestor };
        var descendants = new HashSet<EntityUid>();
        var queue = new Queue<EntityUid>();
        queue.Enqueue(ancestor);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            foreach (var child in _childrenByParent[current])
            {
                if (visited.Add(child))
                {
                    descendants.Add(child);
                    queue.Enqueue(child);
                }
            }
        }

        return descendants;
    }
}