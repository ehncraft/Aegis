namespace Aegis.Relationships;

/// <summary>
/// A single entity-hierarchy fact, mirroring the <c>parents</c> field of
/// Cedar's entity data format (https://docs.cedarpolicy.com/auth/entities-syntax.html):
/// <see cref="Child"/> has <see cref="Parent"/> as a direct ancestor in the
/// <c>in</c> hierarchy.
/// </summary>
public sealed class EntityParent
{
    public required EntityUid Child { get; init; }

    public required EntityUid Parent { get; init; }
}