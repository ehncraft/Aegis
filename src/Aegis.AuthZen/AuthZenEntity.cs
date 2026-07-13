using System.Text.Json;

namespace Aegis.AuthZen;

/// <summary>
/// A <c>subject</c> or <c>resource</c> per the AuthZEN Authorization API 1.0
/// spec (https://openid.github.io/authzen/) -- both share this shape.
/// </summary>
public sealed class AuthZenEntity
{
    public required string Type { get; init; }

    public required string Id { get; init; }

    public Dictionary<string, JsonElement>? Properties { get; init; }
}