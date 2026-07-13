using System.Text.Json;

namespace Aegis.AuthZen;

/// <summary>An <c>action</c> per the AuthZEN Authorization API 1.0 spec (https://openid.github.io/authzen/).</summary>
public sealed class AuthZenAction
{
    public required string Name { get; init; }

    public Dictionary<string, JsonElement>? Properties { get; init; }
}