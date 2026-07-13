using System.Text.Json;

namespace Aegis.AuthZen;

/// <summary>
/// Maps AuthZEN's wire types onto Aegis's evaluation model. AuthZEN has no
/// first-class notion of RBAC roles, so this establishes one convention: a
/// <c>subject.properties.roles</c> JSON array of strings becomes
/// <see cref="AegisPrincipal.Roles"/>; every other property becomes an
/// attribute.
/// </summary>
internal static class AuthZenMapping
{
    public static AegisPrincipal ToPrincipal(AuthZenEntity subject)
    {
        var attributes = ToAttributes(subject.Properties, out var roles);
        attributes["type"] = subject.Type;
        return AegisPrincipal.Create(subject.Id, roles, attributes);
    }

    public static AegisResource ToResource(AuthZenEntity resource)
    {
        var attributes = ToAttributes(resource.Properties, out _);
        return AegisResource.Create(resource.Type, resource.Id, attributes);
    }

    public static Dictionary<string, object?> ToActionProperties(AuthZenAction action) =>
        ToAttributes(action.Properties, out _);

    public static Dictionary<string, object?>? ToContext(Dictionary<string, JsonElement>? context) =>
        context is null ? null : ToAttributes(context, out _);

    private static Dictionary<string, object?> ToAttributes(
        Dictionary<string, JsonElement>? properties, out string[] roles)
    {
        var attributes = new Dictionary<string, object?>();
        roles = [];

        if (properties is null)
        {
            return attributes;
        }

        foreach (var (key, element) in properties)
        {
            if (key == "roles" && element.ValueKind == JsonValueKind.Array)
            {
                roles = [.. element.EnumerateArray()
                    .Where(e => e.ValueKind == JsonValueKind.String)
                    .Select(e => e.GetString()!)];
                continue;
            }

            attributes[key] = ToClrValue(element);
        }

        return attributes;
    }

    private static object? ToClrValue(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.TryGetInt64(out var integer) ? integer : element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => element,
    };
}