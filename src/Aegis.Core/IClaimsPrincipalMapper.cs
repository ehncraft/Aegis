using System.Security.Claims;

namespace Aegis;

/// <summary>
/// Turns a <see cref="ClaimsPrincipal"/> -- what an ASP.NET Identity /
/// IdentityServer / OpenIddict auth server leaves on <c>HttpContext.User</c>
/// -- into an <see cref="AegisPrincipal"/>. An interface rather than a
/// static factory method, so a deployment with unusual claim shapes can
/// supply its own mapping without Aegis needing to anticipate it.
/// </summary>
public interface IClaimsPrincipalMapper
{
    AegisPrincipal Map(ClaimsPrincipal claimsPrincipal);
}