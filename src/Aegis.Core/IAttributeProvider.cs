namespace Aegis;

/// <summary>
/// Supplies roles/attributes for a principal or resource from a source
/// external to the caller -- a database, an HTTP API, etc. -- so policies
/// can reference attributes the caller didn't already have on hand.
///
/// Providers enrich the principal/resource passed to
/// <c>AegisEngine.AuthorizeAsync</c> before evaluation, rather than being
/// consulted lazily during expression evaluation: the expression engine
/// stays synchronous, and a provider is asked for everything it knows about
/// a principal/resource once per decision rather than once per referenced
/// attribute.
/// </summary>
public interface IAttributeProvider
{
    Task<PrincipalAttributes> GetPrincipalAttributesAsync(
        string principalId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Attributes for a resource of the given <paramref name="resourceKind"/>
    /// (matching <see cref="AegisResource.Kind"/>) and id. Providers that
    /// only supply principal attributes can return an empty dictionary.
    /// </summary>
    Task<IReadOnlyDictionary<string, object?>> GetResourceAttributesAsync(
        string resourceKind, string resourceId, CancellationToken cancellationToken = default);
}