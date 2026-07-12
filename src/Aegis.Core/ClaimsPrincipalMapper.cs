using System.Globalization;
using System.Security.Claims;

namespace Aegis;

/// <summary>Default <see cref="IClaimsPrincipalMapper"/>, configured via <see cref="ClaimsMappingOptions"/>.</summary>
public sealed class ClaimsPrincipalMapper(ClaimsMappingOptions options) : IClaimsPrincipalMapper
{
    public AegisPrincipal Map(ClaimsPrincipal claimsPrincipal)
    {
        var id = claimsPrincipal.FindFirst(options.PrincipalIdClaimType)?.Value
            ?? throw new InvalidOperationException(
                $"ClaimsPrincipal has no '{options.PrincipalIdClaimType}' claim to use as the AegisPrincipal id.");

        var roles = claimsPrincipal.FindAll(options.RoleClaimType)
            .Select(claim => claim.Value)
            .ToArray();

        var attributes = new Dictionary<string, object?>();
        foreach (var (attributeName, mapping) in options.AttributeClaims)
        {
            var claim = claimsPrincipal.FindFirst(mapping.ClaimType);
            if (claim is not null)
            {
                attributes[attributeName] = ParseClaimValue(claim, mapping.ValueKind);
            }
        }

        return AegisPrincipal.Create(id, roles, attributes);
    }

    private static object ParseClaimValue(Claim claim, ClaimValueKind kind) => kind switch
    {
        ClaimValueKind.String => claim.Value,
        ClaimValueKind.Integer => int.TryParse(claim.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)
            ? i
            : throw NotParseable(claim, "an integer"),
        ClaimValueKind.Decimal => decimal.TryParse(claim.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var d)
            ? d
            : throw NotParseable(claim, "a decimal"),
        ClaimValueKind.Boolean => bool.TryParse(claim.Value, out var b)
            ? b
            : throw NotParseable(claim, "a boolean"),
        _ => claim.Value,
    };

    private static InvalidOperationException NotParseable(Claim claim, string expected) =>
        new($"Claim '{claim.Type}' value '{claim.Value}' is not {expected}.");
}