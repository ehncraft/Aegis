using System.Security.Claims;

namespace Aegis;

/// <summary>
/// Maps <see cref="ClaimsPrincipalMapper"/> onto whatever claim shape the
/// existing auth server actually emits. Defaults (<see cref="ClaimTypes.NameIdentifier"/>,
/// <see cref="ClaimTypes.Role"/>) match ASP.NET Core's default inbound JWT
/// claim mapping (short "sub"/"role" claims get remapped to these URIs
/// automatically unless <c>MapInboundClaims = false</c>) -- override them if
/// the auth server's tokens are read with inbound mapping disabled, or under
/// different names entirely.
/// </summary>
public sealed class ClaimsMappingOptions
{
    /// <summary>Claim providing <c>AegisPrincipal.Id</c>. Required -- mapping fails without it.</summary>
    public string PrincipalIdClaimType { get; set; } = ClaimTypes.NameIdentifier;

    public string RoleClaimType { get; set; } = ClaimTypes.Role;

    /// <summary>Output attribute name -> which claim to read it from and how to parse it. Missing claims are skipped, not errors.</summary>
    public Dictionary<string, ClaimAttributeMapping> AttributeClaims { get; set; } = [];
}

/// <summary>Where an attribute comes from and how its claim string should be parsed.</summary>
public sealed class ClaimAttributeMapping
{
    public required string ClaimType { get; init; }

    public ClaimValueKind ValueKind { get; init; } = ClaimValueKind.String;
}

/// <summary>
/// Claim values are always strings on the wire; this is how far
/// <see cref="ClaimsPrincipalMapper"/> will parse one before giving up.
/// Numeric kinds matter for policies that compare attributes with
/// <![CDATA[<=]]>/<![CDATA[>=]]> rather than just <![CDATA[==]]>.
/// </summary>
#pragma warning disable CA1720 // String/Integer/Decimal/Boolean are the clearest names for "parse as what"
public enum ClaimValueKind
{
    String,
    Integer,
    Decimal,
    Boolean,
}
#pragma warning restore CA1720