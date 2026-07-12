using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Aegis.AspNetCore;

public static class HttpContextAuthorizationExtensions
{
    /// <summary>
    /// Authorizes <paramref name="httpContext"/>'s current user directly --
    /// the mapping from claims to <see cref="AegisPrincipal"/> that
    /// <c>AddAegis</c> registered handles the translation, so callers never
    /// hand-roll it per request.
    /// </summary>
    public static Task<AuthorizationDecision> AuthorizeAsync(
        this AegisEngine engine,
        HttpContext httpContext,
        AegisResource resource,
        string action,
        CancellationToken cancellationToken = default)
    {
        var mapper = httpContext.RequestServices.GetRequiredService<IClaimsPrincipalMapper>();
        return engine.AuthorizeAsync(httpContext.User, mapper, resource, action, cancellationToken);
    }
}