namespace Aegis;

/// <summary>
/// Merges attribute-provider output into a principal/resource before
/// evaluation. Providers fill gaps -- an attribute the caller already set
/// explicitly always wins over one a provider supplies, and among
/// providers, the first one to return a given attribute wins.
/// </summary>
internal static class AttributeEnricher
{
    public static async Task<AegisPrincipal> EnrichAsync(
        AegisPrincipal principal,
        IReadOnlyList<IAttributeProvider> providers,
        CancellationToken cancellationToken)
    {
        if (providers.Count == 0)
        {
            return principal;
        }

        var roles = new List<string>(principal.Roles);
        var attributes = new Dictionary<string, object?>();

        foreach (var provider in providers)
        {
            var result = await provider.GetPrincipalAttributesAsync(principal.Id, cancellationToken);

            foreach (var role in result.Roles)
            {
                if (!roles.Contains(role, StringComparer.OrdinalIgnoreCase))
                {
                    roles.Add(role);
                }
            }

            foreach (var (key, value) in result.Attributes)
            {
                attributes.TryAdd(key, value);
            }
        }

        foreach (var (key, value) in principal.Attributes)
        {
            attributes[key] = value;
        }

        return AegisPrincipal.Create(principal.Id, roles, attributes);
    }

    public static async Task<AegisResource> EnrichAsync(
        AegisResource resource,
        IReadOnlyList<IAttributeProvider> providers,
        CancellationToken cancellationToken)
    {
        if (providers.Count == 0 || resource.Id is null)
        {
            return resource;
        }

        var attributes = new Dictionary<string, object?>();

        foreach (var provider in providers)
        {
            var result = await provider.GetResourceAttributesAsync(resource.Kind, resource.Id, cancellationToken);

            foreach (var (key, value) in result)
            {
                attributes.TryAdd(key, value);
            }
        }

        foreach (var (key, value) in resource.Attributes)
        {
            attributes[key] = value;
        }

        return AegisResource.Create(resource.Kind, resource.Id, attributes);
    }
}