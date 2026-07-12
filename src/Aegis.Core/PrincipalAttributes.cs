namespace Aegis;

/// <summary>Roles and attributes an <see cref="IAttributeProvider"/> supplies for a principal.</summary>
public sealed class PrincipalAttributes
{
    public IReadOnlyList<string> Roles { get; init; } = Array.Empty<string>();

    public IReadOnlyDictionary<string, object?> Attributes { get; init; } =
        new Dictionary<string, object?>();

    public static readonly PrincipalAttributes Empty = new();
}