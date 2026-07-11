namespace Aegis;

/// <summary>The entity requesting access — a user, service, or other actor.</summary>
public sealed class AegisPrincipal
{
    public required string Id { get; init; }

    public IReadOnlyList<string> Roles { get; init; } = Array.Empty<string>();

    public IReadOnlyDictionary<string, object?> Attributes { get; init; } =
        new Dictionary<string, object?>();

    public static AegisPrincipal Create(string id, IEnumerable<string>? roles = null,
        IReadOnlyDictionary<string, object?>? attributes = null) => new()
        {
            Id = id,
            Roles = roles?.ToArray() ?? Array.Empty<string>(),
            Attributes = attributes ?? new Dictionary<string, object?>(),
        };
}