using System.Text.Json;

namespace Aegis;

/// <summary>
/// Builds decision-cache keys from a principal/resource/action/context
/// tuple. Backend-agnostic -- shared by <see cref="MemoryDecisionCache"/>
/// and <see cref="DistributedDecisionCache"/>, since the key doesn't
/// depend on where entries end up stored. The key is built by
/// JSON-serializing a canonical (sorted) representation rather than
/// hand-concatenating a delimited string -- a manual "key1=val1|key2=val2"
/// scheme would let two different logical inputs collide into the same
/// string if an attribute's own value happened to contain the delimiter,
/// which for a security cache means serving the wrong decision to the
/// wrong principal.
/// </summary>
internal static class DecisionCacheKey
{
    public static string Build(AegisPrincipal principal, AegisResource resource, string action) =>
        Build(principal, resource, action, actionProperties: null, context: null);

    /// <summary>
    /// Includes <paramref name="actionProperties"/>/<paramref name="context"/>
    /// in the key -- omitting them would let two calls with the same
    /// principal/resource/action but different context collide onto the
    /// same cache entry, serving a decision computed under the wrong
    /// context.
    /// </summary>
    public static string Build(
        AegisPrincipal principal,
        AegisResource resource,
        string action,
        IReadOnlyDictionary<string, object?>? actionProperties,
        IReadOnlyDictionary<string, object?>? context)
    {
        var model = new CacheKeyModel(
            principal.Id,
            [.. principal.Roles.OrderBy(role => role, StringComparer.Ordinal)],
            SortAttributes(principal.Attributes),
            resource.Kind,
            resource.Id,
            SortAttributes(resource.Attributes),
            action,
            SortAttributes(actionProperties ?? new Dictionary<string, object?>()),
            SortAttributes(context ?? new Dictionary<string, object?>()));

        return JsonSerializer.Serialize(model);
    }

    private static SortedDictionary<string, object?> SortAttributes(IReadOnlyDictionary<string, object?> attributes) =>
        new(attributes.ToDictionary(pair => pair.Key, pair => pair.Value), StringComparer.Ordinal);

    private sealed record CacheKeyModel(
        string PrincipalId,
        string[] Roles,
        SortedDictionary<string, object?> PrincipalAttributes,
        string ResourceKind,
        string? ResourceId,
        SortedDictionary<string, object?> ResourceAttributes,
        string Action,
        SortedDictionary<string, object?> ActionProperties,
        SortedDictionary<string, object?> Context);
}